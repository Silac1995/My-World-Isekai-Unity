using System.Collections.Generic;
using UnityEngine;
using MWI.Ambition;
using MWI.WorldSystem;

namespace MWI.AI
{
    /// <summary>
    /// Drives the actor toward completing the active step of their <see cref="CharacterAmbition.Current"/>.
    /// Sits between the Schedule node (priority 5) and the GOAP node (priority 6) in
    /// <see cref="NPCBehaviourTree"/> — Schedule preempts so workers still punch in for their shift,
    /// Ambition preempts GOAP so a concrete life-goal commitment trumps proactive planning.
    ///
    /// <para>The ambition-task system is split between active and passive tasks:</para>
    /// <list type="bullet">
    /// <item><c>Task_CreateCommunity</c> — active, drives itself on Tick (calls
    /// <see cref="CharacterCommunity.CheckAndCreateCommunity"/>).</item>
    /// <item><c>Task_PlaceBuilding</c>, <c>Task_FinishConstruction</c>,
    /// <c>Task_PromoteCommunity</c> — passive, only watch world state. Their docstrings
    /// say "driven by an NPC BTAction (Plan 4b)" — that's this class.</item>
    /// </list>
    ///
    /// <para>Per-tick flow:</para>
    /// <list type="number">
    /// <item>Pull <see cref="AmbitionQuest"/> from the active step (CurrentStepQuest).</item>
    /// <item>Scan its tasks for an actionable passive task — if one matches a driver
    /// (place / finalize / promote), drive it and return <see cref="BTNodeStatus.Running"/>.</item>
    /// <item>Otherwise pump <see cref="AmbitionQuest.TickActiveTasks"/> so active tasks
    /// (like CreateCommunity) and passive watchers (re-checking world state) advance the
    /// ambition state machine. <see cref="CharacterAmbition.HandleStepStateChanged"/>
    /// auto-advances to the next step on Completed.</item>
    /// </list>
    ///
    /// <para>Movement gates follow rule #36 (use <c>InteractableObject.IsCharacterInInteractionZone</c>
    /// when the target has one; fall back to a flat-XZ <c>Bounds.Contains</c> against the
    /// building's BuildingZone otherwise). Allocation rule #34 — all scratch lists are
    /// cached fields, no LINQ in hot paths. All Debug.Logs gate on
    /// <see cref="NPCDebug.VerboseActions"/>.</para>
    ///
    /// <para>Network safety: this whole class is server-only (BT ticks only on
    /// <see cref="NPCBehaviourTree.Update"/>'s <c>IsServer</c> gate). All world mutations
    /// route through existing canonical paths (<see cref="BuildingPlacementManager.PlaceCivicBuildingForLeader"/>,
    /// <see cref="CharacterActions.ExecuteAction"/> with <c>CharacterAction_FinishConstruction</c>,
    /// <see cref="Community.TryPromoteLevel"/>). No new replication channel.</para>
    /// </summary>
    public class BTAction_PursueAmbition : BTNode
    {
        // Cached scratch for BuildingManager.allBuildings scans — avoids re-allocating
        // per-tick. Cleared and re-populated each call.
        private readonly List<Building> _scratchBuildings = new List<Building>(8);

        // Reused buffers for the gather state machine.
        private readonly Dictionary<ItemSO, int> _scratchMissing = new Dictionary<ItemSO, int>(4);
        private readonly Collider[] _scratchOverlap = new Collider[32];

        // While walking to a target, cache the target position so we don't recompute it
        // every tick. Cleared on OnExit.
        private Vector3? _activeDestination;
        private bool _hasActiveAction;

        // Gather state machine — persists across ticks so the BTAction can resume a
        // multi-step harvest → pickup → drop chain (each step takes 1+ ticks because
        // the queued CharacterAction blocks the BT until it ends).
        private Harvestable _targetHarvestable;
        private float _wanderUntilTime;
        private Vector3? _wanderDestination;

        protected override void OnEnter(Blackboard bb)
        {
            _activeDestination = null;
            _hasActiveAction = false;
            _targetHarvestable = null;
            _wanderUntilTime = 0f;
            _wanderDestination = null;
        }

        protected override void OnExit(Blackboard bb)
        {
            _activeDestination = null;
            _hasActiveAction = false;
            _targetHarvestable = null;
            _wanderUntilTime = 0f;
            _wanderDestination = null;
        }

        protected override BTNodeStatus OnExecute(Blackboard bb)
        {
            Character self = bb.Self;
            if (self == null) return BTNodeStatus.Failure;

            var ambitions = self.CharacterAmbition;
            if (ambitions == null || !ambitions.HasActive) return BTNodeStatus.Failure;

            var current = ambitions.Current;
            if (!(current?.CurrentStepQuest is AmbitionQuest aq)) return BTNodeStatus.Failure;

            // 1. Try drivers for any actionable passive task in the current step.
            var driveStatus = TryDrive(self, aq);
            if (driveStatus.HasValue) return driveStatus.Value;

            // 2. No driver took control. Pump the ambition state machine so active tasks
            //    (Task_CreateCommunity) run + passive watchers re-check world state.
            //    Note: CharacterAmbition.HandleStepStateChanged auto-advances to the next
            //    step on Completed, so we don't have to.
            TaskStatus tickStatus;
            try { tickStatus = aq.TickActiveTasks(self); }
            catch (System.Exception e) { Debug.LogException(e); return BTNodeStatus.Failure; }

            return tickStatus switch
            {
                TaskStatus.Completed => BTNodeStatus.Success,
                TaskStatus.Failed    => BTNodeStatus.Failure,
                _                    => BTNodeStatus.Running,
            };
        }

        /// <summary>
        /// Walks the active step's task list. Returns the driver result for the first
        /// actionable passive task; null when no driver applies (caller falls through to
        /// the state-machine pump).
        /// </summary>
        private BTNodeStatus? TryDrive(Character self, AmbitionQuest aq)
        {
            var tasks = aq.Tasks;
            if (tasks == null) return null;

            for (int i = 0; i < tasks.Count; i++)
            {
                switch (tasks[i])
                {
                    case Task_PlaceBuilding place when place.TargetBlueprint != null:
                        if (!HasActorPlacedBuilding(self, place.TargetBlueprint))
                            return DrivePlaceBuilding(self, place.TargetBlueprint);
                        break;

                    case Task_FinishConstruction finish when finish.TargetBlueprint != null:
                        // DELIBERATE STEP-ASIDE: Task_FinishConstruction is delegated to GOAP
                        // via NeedAmbitionFinishConstruction → GoapAction_FulfillAmbitionConstruction.
                        // The BT priority order is Ambition (this node) → GOAP → Wander. By
                        // returning Failure here we step aside; the BT Selector tries the next
                        // child (GOAP) which picks up the goal "ambitionBuildingFinalized=true"
                        // and executes the composite gather/carry/drop/finalize state machine.
                        // This makes the active goal + action visible in the CharacterGoapController
                        // inspector — closing the "his goap goal and action is at none" gap from
                        // the BTAction-only path. The BTAction's own DriveFinishConstruction
                        // helper below is kept for reference but no longer called (left in place
                        // for any future ambition step whose driver can't easily be expressed as
                        // a GoapGoal).
                        if (TryFindActorBuilding(self, finish.TargetBlueprint, requireUnderConstruction: true, out _))
                            return BTNodeStatus.Failure;
                        break;

                    case Task_PromoteCommunity promote:
                        if (NeedsPromotion(self, promote.TargetLevel, out var ab))
                            return DrivePromote(self, ab);
                        break;
                }
            }
            return null;
        }

        // ─── Drivers ─────────────────────────────────────────────────────────

        /// <summary>
        /// Places <paramref name="blueprint"/> at the actor's current cell (snapped to grid).
        /// If the actor's cell is occupied or out-of-region, returns Failure so the BT falls
        /// through to lower priorities — the NPC will likely wander, then retry from a new
        /// position next tick. Builds at NPC's position rather than searching a wider area
        /// for v1; cell-search can be added later if Kevin finds NPCs getting stuck.
        /// </summary>
        private BTNodeStatus DrivePlaceBuilding(Character self, BuildingSO blueprint)
        {
            var bpm = self.GetComponentInChildren<BuildingPlacementManager>();
            if (bpm == null)
            {
                if (NPCDebug.VerboseActions)
                    Debug.LogWarning($"[BTAction_PursueAmbition] {self.CharacterName}: no BuildingPlacementManager subsystem on actor.");
                return BTNodeStatus.Failure;
            }

            Vector3 npcPos = self.transform.position;
            var hostMap = MapController.GetMapAtPosition(npcPos);

            // Snap to grid centre when the host map has a grid; otherwise place raw.
            Vector3 placePos = npcPos;
            if (hostMap != null && hostMap.BuildingGrid != null)
            {
                placePos = hostMap.BuildingGrid.SnapToGridCenter(npcPos);
                Vector2Int originCell = hostMap.BuildingGrid.GetCellCoord(placePos);
                if (!hostMap.BuildingGrid.CanPlace(originCell, blueprint.GridFootprintCells))
                {
                    if (NPCDebug.VerboseActions)
                        Debug.Log($"[BTAction_PursueAmbition] {self.CharacterName}: cell {originCell} blocked for '{blueprint.BuildingName}' — falling through.");
                    return BTNodeStatus.Failure;
                }
            }

            // Region gate is enforced inside PlaceCivicBuildingForLeader; no need to re-check.
            Building placed = null;
            try
            {
                placed = bpm.PlaceCivicBuildingForLeader(blueprint, self, placePos, Quaternion.identity);
            }
            catch (System.Exception e) { Debug.LogException(e); }

            if (placed == null)
            {
                if (NPCDebug.VerboseActions)
                    Debug.LogWarning($"[BTAction_PursueAmbition] {self.CharacterName}: PlaceCivicBuildingForLeader returned null for '{blueprint.BuildingName}' at {placePos}.");
                return BTNodeStatus.Failure;
            }

            // Register the cell on the grid so future placements know it's taken.
            // (PlaceCivicBuildingForLeader doesn't do this — it's only called from the
            // admin-console path which registers afterward. We mirror that here.)
            if (hostMap != null && hostMap.BuildingGrid != null && placed.NetworkObject != null)
            {
                Vector2Int originCell = hostMap.BuildingGrid.GetCellCoord(placePos);
                try
                {
                    hostMap.BuildingGrid.Register(placed.NetworkObject.NetworkObjectId,
                                                  originCell, blueprint.GridFootprintCells);
                }
                catch (System.Exception e) { Debug.LogException(e); }
            }

            if (NPCDebug.VerboseActions)
                Debug.Log($"<color=cyan>[BTAction_PursueAmbition]</color> {self.CharacterName} placed '{blueprint.BuildingName}' at {placePos}.");
            return BTNodeStatus.Running; // Task_PlaceBuilding will report Completed on the next tick.
        }

        /// <summary>
        /// Top-level orchestrator for the Task_FinishConstruction step. Computes the
        /// missing-materials delta from <see cref="Building.ConstructionRequirements"/>
        /// minus <see cref="Building.ContributedMaterials"/>, then dispatches:
        /// <list type="number">
        /// <item>If carrying a relevant item → walk to <see cref="Building.BuildingZone"/> + drop.</item>
        /// <item>If a matching <see cref="WorldItem"/> lies near the actor (e.g. the
        /// just-harvested output at the harvestable's foot) → pick it up.</item>
        /// <item>If a target <see cref="Harvestable"/> is cached and still valid → walk to
        /// it; when in its <see cref="InteractableObject.InteractionZone"/> queue
        /// <see cref="CharacterHarvestAction"/>.</item>
        /// <item>Else scan <see cref="CharacterAwareness.GetVisibleInteractables{Harvestable}"/>
        /// for a yielder of any missing item — set as target and walk there.</item>
        /// <item>Else nothing in awareness → wander to a random nearby NavMesh point and
        /// retry next tick. The wander destination is re-rolled every few seconds so the
        /// NPC actually moves across the map looking for something.</item>
        /// <item>If inside the BuildingZone AND zone WorldItems already match a req →
        /// queue <see cref="CharacterAction_FinishConstruction"/> (consumes them).</item>
        /// </list>
        /// </summary>
        private BTNodeStatus DriveFinishConstruction(Character self, Building building)
        {
            if (building == null || building.BuildingZone == null) return BTNodeStatus.Failure;
            if (!building.IsUnderConstruction) return BTNodeStatus.Success;

            // Outstanding-material accounting. Empty when every requirement is met (or
            // when the SO authored zero requirements).
            ComputeMissingMaterials(building, _scratchMissing);

            // Step 0: action already queued (harvest / pickup / drop / consume in flight).
            // BT will be paused by NPCBehaviourTree.Update; we just stay Running.
            if (self.CharacterActions != null && self.CharacterActions.CurrentAction != null)
                return BTNodeStatus.Running;

            var zoneBounds = building.BuildingZone.bounds;
            Vector3 actorPos = self.transform.position;
            bool inZone = actorPos.x >= zoneBounds.min.x && actorPos.x <= zoneBounds.max.x
                       && actorPos.z >= zoneBounds.min.z && actorPos.z <= zoneBounds.max.z;

            // Step 1: carrying a relevant item? walk to zone + drop it inside.
            if (_scratchMissing.Count > 0)
            {
                var carried = FindCarriedRelevantItem(self, _scratchMissing);
                if (carried != null)
                {
                    if (inZone)
                    {
                        try
                        {
                            self.CharacterActions.ExecuteAction(new CharacterDropItem(self, carried));
                        }
                        catch (System.Exception e) { Debug.LogException(e); }
                        return BTNodeStatus.Running;
                    }
                    return WalkTo(self, zoneBounds.center);
                }
            }

            // Step 2: nearby loose WorldItem that matches a missing req? pick it up.
            if (_scratchMissing.Count > 0)
            {
                var loose = FindNearbyMatchingWorldItem(self, _scratchMissing, out var looseObj);
                if (loose != null && looseObj != null)
                {
                    if (IsInPickupRange(self, looseObj))
                    {
                        try
                        {
                            self.CharacterActions.ExecuteAction(new CharacterPickUpItem(self, loose, looseObj));
                        }
                        catch (System.Exception e) { Debug.LogException(e); }
                        return BTNodeStatus.Running;
                    }
                    return WalkTo(self, looseObj.transform.position);
                }
            }

            // Step 3: cached target harvestable still yields something we need?
            if (_targetHarvestable != null
                && _targetHarvestable.gameObject != null
                && _targetHarvestable.CanHarvest()
                && HarvestableYieldsAny(_targetHarvestable, _scratchMissing))
            {
                return DriveHarvest(self, _targetHarvestable);
            }
            _targetHarvestable = null;

            // Step 4: still need materials AND there's something in awareness that yields them?
            if (_scratchMissing.Count > 0)
            {
                var awareness = self.CharacterAwareness;
                if (awareness != null)
                {
                    var candidates = awareness.GetVisibleInteractables<Harvestable>();
                    for (int i = 0; i < candidates.Count; i++)
                    {
                        var h = candidates[i];
                        if (h == null || !h.CanHarvest()) continue;
                        if (!HarvestableYieldsAny(h, _scratchMissing)) continue;
                        _targetHarvestable = h;
                        return DriveHarvest(self, h);
                    }
                }
            }

            // Step 5: inside zone AND already-dropped items can be consumed → queue the
            // canonical consume action. This is the "finalize what we already delivered"
            // branch; it also covers the no-reqs case (CharacterAction_FinishConstruction
            // early-returns harmlessly when there's nothing to consume).
            if (inZone && _scratchMissing.Count == 0)
            {
                try
                {
                    self.CharacterActions.ExecuteAction(new CharacterAction_FinishConstruction(self, building));
                }
                catch (System.Exception e) { Debug.LogException(e); return BTNodeStatus.Failure; }
                return BTNodeStatus.Running;
            }
            if (inZone && AnyZoneItemSatisfiesReq(building, _scratchMissing))
            {
                try
                {
                    self.CharacterActions.ExecuteAction(new CharacterAction_FinishConstruction(self, building));
                }
                catch (System.Exception e) { Debug.LogException(e); return BTNodeStatus.Failure; }
                return BTNodeStatus.Running;
            }

            // Step 6: still need materials, nothing visible, nothing on hand → wander to
            // expand awareness coverage. Wander destination is re-rolled every 4-8 seconds
            // unless we've arrived sooner.
            if (_scratchMissing.Count > 0) return DriveWander(self);

            // No requirements outstanding AND not in zone → just walk to zone.
            return WalkTo(self, zoneBounds.center);
        }

        // ─── Gather state-machine helpers ────────────────────────────────────

        private BTNodeStatus DriveHarvest(Character self, Harvestable target)
        {
            if (target == null || target.gameObject == null) return BTNodeStatus.Failure;
            var movement = self.CharacterMovement;
            if (movement == null) return BTNodeStatus.Failure;

            // Pick a destination on the harvestable's InteractionZone (rule #36).
            Vector3 gatherPos = target.transform.position;
            if (target.InteractionZone != null)
                gatherPos = target.InteractionZone.bounds.ClosestPoint(self.transform.position);

            bool isAtTarget = false;
            if (target.InteractionZone != null)
            {
                var b = target.InteractionZone.bounds;
                if (b.Contains(self.transform.position))
                {
                    isAtTarget = true;
                }
                else
                {
                    float dist = Vector3.Distance(self.transform.position, b.ClosestPoint(self.transform.position));
                    if (dist <= 2.5f) isAtTarget = true;
                    // Softlock guard (rule #36): NavMesh sampled landing point is just
                    // outside the zone but the agent stopped advancing → treat as arrived.
                    if (!isAtTarget && (!movement.HasPath || movement.RemainingDistance <= movement.StoppingDistance + 0.5f))
                    {
                        Vector3 a = new Vector3(self.transform.position.x, 0f, self.transform.position.z);
                        Vector3 c = new Vector3(target.transform.position.x, 0f, target.transform.position.z);
                        if (Vector3.Distance(a, c) <= 4f) isAtTarget = true;
                    }
                }
            }
            else
            {
                isAtTarget = Vector3.Distance(self.transform.position, target.transform.position) <= 3f;
            }

            if (isAtTarget)
            {
                try
                {
                    self.transform.LookAt(new Vector3(target.transform.position.x, self.transform.position.y, target.transform.position.z));
                    var action = new CharacterHarvestAction(self, target);
                    self.CharacterActions.ExecuteAction(action);
                }
                catch (System.Exception e) { Debug.LogException(e); return BTNodeStatus.Failure; }
                return BTNodeStatus.Running;
            }

            // Walk to gatherPos. Re-fire SetDestination on path-loss (rule #36 softlock guard).
            if (!_activeDestination.HasValue
                || Vector3.Distance(_activeDestination.Value, gatherPos) > 0.5f
                || !movement.HasPath)
            {
                movement.SetDestination(gatherPos);
                _activeDestination = gatherPos;
            }
            return BTNodeStatus.Running;
        }

        private BTNodeStatus DriveWander(Character self)
        {
            var movement = self.CharacterMovement;
            if (movement == null) return BTNodeStatus.Failure;

            float now = UnityEngine.Time.time;
            bool arrived = !movement.HasPath || movement.RemainingDistance <= movement.StoppingDistance + 0.5f;
            bool needNewDest = !_wanderDestination.HasValue || now >= _wanderUntilTime || arrived;
            if (needNewDest)
            {
                Vector3 dest = self.transform.position;
                float walkRadius = 18f; // ≈ awareness radius default × 1.2 — enough to drift into new awareness coverage each pick.
                for (int i = 0; i < 5; i++)
                {
                    Vector2 r = UnityEngine.Random.insideUnitCircle * walkRadius;
                    Vector3 candidate = self.transform.position + new Vector3(r.x, 0f, r.y);
                    if (UnityEngine.AI.NavMesh.SamplePosition(candidate, out var hit, walkRadius, UnityEngine.AI.NavMesh.AllAreas))
                    {
                        dest = hit.position;
                        break;
                    }
                }
                movement.SetDestination(dest);
                _wanderDestination = dest;
                _wanderUntilTime = now + UnityEngine.Random.Range(4f, 8f);

                if (NPCDebug.VerboseActions)
                    Debug.Log($"[BTAction_PursueAmbition.Wander] {self.CharacterName} → {dest} (search-for-materials wander).");
            }
            return BTNodeStatus.Running;
        }

        private BTNodeStatus WalkTo(Character self, Vector3 dest)
        {
            var movement = self.CharacterMovement;
            if (movement == null) return BTNodeStatus.Failure;
            if (!_activeDestination.HasValue
                || Vector3.Distance(_activeDestination.Value, dest) > 0.5f
                || !movement.HasPath)
            {
                movement.SetDestination(dest);
                _activeDestination = dest;
            }
            return BTNodeStatus.Running;
        }

        // ─── Material accounting ─────────────────────────────────────────────

        /// <summary>
        /// Fills <paramref name="missing"/> with the per-ItemSO shortfall (required - contributed),
        /// skipping fully-satisfied requirements. Cleared at the start so the caller can
        /// reuse a scratch dictionary.
        /// </summary>
        private static void ComputeMissingMaterials(Building building, Dictionary<ItemSO, int> missing)
        {
            missing.Clear();
            var reqs = building.ConstructionRequirements;
            if (reqs == null) return;
            var contributed = building.ContributedMaterials;
            for (int i = 0; i < reqs.Count; i++)
            {
                var r = reqs[i];
                if (r.Item == null || r.Amount <= 0) continue;
                int delivered = (contributed != null && contributed.TryGetValue(r.Item, out int v)) ? v : 0;
                int delta = r.Amount - delivered;
                if (delta > 0) missing[r.Item] = delta;
            }
        }

        private static bool HarvestableYieldsAny(Harvestable h, Dictionary<ItemSO, int> missing)
        {
            if (h == null || h.HarvestOutputs == null || missing.Count == 0) return false;
            for (int i = 0; i < h.HarvestOutputs.Count; i++)
            {
                var o = h.HarvestOutputs[i];
                if (o.Item == null) continue;
                if (missing.ContainsKey(o.Item)) return true;
            }
            return false;
        }

        private static ItemInstance FindCarriedRelevantItem(Character self, Dictionary<ItemSO, int> missing)
        {
            var equipment = self.CharacterEquipment;
            if (equipment == null || !equipment.HaveInventory()) return null;
            var inv = equipment.GetInventory();
            if (inv == null) return null;
            var slots = inv.ItemSlots;
            if (slots == null) return null;
            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                if (s == null || s.IsEmpty()) continue;
                var inst = s.ItemInstance;
                if (inst == null || inst.ItemSO == null) continue;
                if (missing.ContainsKey(inst.ItemSO)) return inst;
            }
            return null;
        }

        /// <summary>
        /// Overlap-scans a small radius around <paramref name="self"/> for a
        /// <see cref="WorldItem"/> whose <c>ItemSO</c> matches a missing requirement and
        /// which is not currently being carried by someone. Returns the underlying
        /// <see cref="ItemInstance"/> + the <see cref="GameObject"/> so callers can hand
        /// them straight to <see cref="CharacterPickUpItem"/>.
        /// </summary>
        private ItemInstance FindNearbyMatchingWorldItem(Character self, Dictionary<ItemSO, int> missing, out GameObject worldObject)
        {
            worldObject = null;
            if (missing.Count == 0) return null;
            const float SearchRadius = 4f;
            int hitCount = Physics.OverlapSphereNonAlloc(self.transform.position, SearchRadius, _scratchOverlap,
                Physics.AllLayers, QueryTriggerInteraction.Collide);
            for (int i = 0; i < hitCount; i++)
            {
                var col = _scratchOverlap[i];
                if (col == null) continue;
                var wi = col.GetComponent<WorldItem>() ?? col.GetComponentInParent<WorldItem>();
                if (wi == null || wi.IsBeingCarried) continue;
                var inst = wi.ItemInstance;
                if (inst == null || inst.ItemSO == null) continue;
                if (!missing.ContainsKey(inst.ItemSO)) continue;
                worldObject = wi.gameObject;
                return inst;
            }
            return null;
        }

        private bool IsInPickupRange(Character self, GameObject worldObject)
        {
            if (worldObject == null) return false;
            var interactable = worldObject.GetComponent<InteractableObject>() ?? worldObject.GetComponentInParent<InteractableObject>();
            if (interactable != null && interactable.InteractionZone != null)
            {
                var bounds = interactable.InteractionZone.bounds;
                if (bounds.Contains(self.transform.position)) return true;
                Vector3 closest = bounds.ClosestPoint(self.transform.position);
                Vector3 a = new Vector3(self.transform.position.x, 0f, self.transform.position.z);
                Vector3 b = new Vector3(closest.x, 0f, closest.z);
                return Vector3.Distance(a, b) <= 1.5f;
            }
            return Vector3.Distance(self.transform.position, worldObject.transform.position) <= 1.5f;
        }

        private bool AnyZoneItemSatisfiesReq(Building building, Dictionary<ItemSO, int> missing)
        {
            if (building == null || building.BuildingZone == null || missing.Count == 0) return false;
            var b = building.BuildingZone.bounds;
            int hitCount = Physics.OverlapBoxNonAlloc(b.center, b.extents, _scratchOverlap, Quaternion.identity,
                Physics.AllLayers, QueryTriggerInteraction.Collide);
            for (int i = 0; i < hitCount; i++)
            {
                var col = _scratchOverlap[i];
                if (col == null) continue;
                var wi = col.GetComponent<WorldItem>() ?? col.GetComponentInParent<WorldItem>();
                if (wi == null || wi.IsBeingCarried) continue;
                var inst = wi.ItemInstance;
                if (inst == null || inst.ItemSO == null) continue;
                if (missing.ContainsKey(inst.ItemSO)) return true;
            }
            return false;
        }

        /// <summary>
        /// Walks the actor to the community's <see cref="AdministrativeBuilding"/> and calls
        /// <see cref="Community.TryPromoteLevel"/>. If gates are unmet (population / treasury /
        /// required buildings), returns Failure so the BT falls through and the NPC can work
        /// on other branches that progress those gates.
        /// </summary>
        private BTNodeStatus DrivePromote(Character self, AdministrativeBuilding ab)
        {
            if (ab == null || ab.BuildingZone == null) return BTNodeStatus.Failure;

            var bounds = ab.BuildingZone.bounds;
            Vector3 pos = self.transform.position;
            bool inZone = pos.x >= bounds.min.x && pos.x <= bounds.max.x
                       && pos.z >= bounds.min.z && pos.z <= bounds.max.z;

            if (!inZone)
            {
                var movement = self.CharacterMovement;
                if (movement == null) return BTNodeStatus.Failure;
                // Project zone center down to the actor's Y — see commit c7812f76 +
                // GoapAction_FulfillAmbitionConstruction Step 1 for the rationale.
                // BuildingZone box colliders are tall, so bounds.center.y sits ~7.9u
                // above the floor, past NavMesh.SamplePosition's 5m tolerance.
                var dest = new Vector3(bounds.center.x, pos.y, bounds.center.z);
                if (!_activeDestination.HasValue || Vector3.Distance(_activeDestination.Value, dest) > 0.5f
                    || !movement.HasPath)
                {
                    movement.SetDestination(dest);
                    _activeDestination = dest;
                }
                return BTNodeStatus.Running;
            }

            // At AB. Try to promote.
            var community = ab.OwnerCommunity;
            if (community == null) return BTNodeStatus.Failure;

            (bool ok, string reason) result;
            try { result = community.TryPromoteLevel(ab); }
            catch (System.Exception e) { Debug.LogException(e); return BTNodeStatus.Failure; }

            if (result.ok)
            {
                if (NPCDebug.VerboseActions)
                    Debug.Log($"<color=green>[BTAction_PursueAmbition]</color> {self.CharacterName} promoted '{community.communityName}' to {community.level}.");
                return BTNodeStatus.Running; // Task_PromoteCommunity will report Completed next tick.
            }

            if (NPCDebug.VerboseActions)
                Debug.Log($"[BTAction_PursueAmbition] {self.CharacterName} promote denied: {result.reason}. Falling through.");
            return BTNodeStatus.Failure;
        }

        // ─── World-state queries ─────────────────────────────────────────────

        private bool HasActorPlacedBuilding(Character self, BuildingSO blueprint)
        {
            return TryFindActorBuilding(self, blueprint, requireUnderConstruction: false, out _);
        }

        /// <summary>
        /// Scans <see cref="BuildingManager.allBuildings"/> for a building whose
        /// <see cref="Building.Blueprint"/> matches AND was placed by this actor.
        /// When <paramref name="requireUnderConstruction"/> is true, only matches
        /// in-progress builds; when false, matches regardless of construction state.
        /// </summary>
        private bool TryFindActorBuilding(Character self, BuildingSO blueprint,
                                          bool requireUnderConstruction, out Building result)
        {
            result = null;
            var bm = BuildingManager.Instance;
            if (bm == null) return false;
            string actorId = self.CharacterId;
            if (string.IsNullOrEmpty(actorId)) return false;

            for (int i = 0; i < bm.allBuildings.Count; i++)
            {
                var b = bm.allBuildings[i];
                if (b == null) continue;
                if (b.Blueprint != blueprint) continue;
                if (b.PlacedByCharacterId.Value.ToString() != actorId) continue;
                if (requireUnderConstruction && !b.IsUnderConstruction) continue;
                result = b;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Returns true when the actor's <see cref="Community.level"/> is below
        /// <paramref name="targetLevel"/> AND the community has an AB to anchor the
        /// promotion call.
        /// </summary>
        private bool NeedsPromotion(Character self, CommunityLevel targetLevel, out AdministrativeBuilding ab)
        {
            ab = null;
            if (self.CharacterCommunity == null) return false;
            var community = self.CharacterCommunity.CurrentCommunity;
            if (community == null) return false;
            if (community.level >= targetLevel) return false;
            ab = community.AdministrativeBuilding;
            return ab != null;
        }
    }
}
