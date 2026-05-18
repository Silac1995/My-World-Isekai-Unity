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

        // While walking to a target, cache the target position so we don't recompute it
        // every tick. Cleared on OnExit.
        private Vector3? _activeDestination;
        private bool _hasActiveAction;

        protected override void OnEnter(Blackboard bb)
        {
            _activeDestination = null;
            _hasActiveAction = false;
        }

        protected override void OnExit(Blackboard bb)
        {
            _activeDestination = null;
            _hasActiveAction = false;
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
                        if (TryFindActorBuilding(self, finish.TargetBlueprint, requireUnderConstruction: true, out var b))
                            return DriveFinishConstruction(self, b);
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
        /// Walks the actor into the building's BuildingZone and queues a
        /// <c>CharacterAction_FinishConstruction</c>. Once the action ends (construction
        /// complete or stall), this BTNode returns Failure on the next tick if the building
        /// is still under construction (BT retries) or Success once it's complete.
        /// </summary>
        private BTNodeStatus DriveFinishConstruction(Character self, Building building)
        {
            if (building == null || building.BuildingZone == null) return BTNodeStatus.Failure;

            // If we're already inside the zone, queue the action and stay Running. The
            // CharacterAction blocks the BT tick (NPCBehaviourTree.Update checks
            // CurrentAction != null and returns) so we won't re-enter this branch until
            // the action ends — at which point the NPC is still inside the zone.
            var bounds = building.BuildingZone.bounds;
            Vector3 pos = self.transform.position;
            bool inZone = pos.x >= bounds.min.x && pos.x <= bounds.max.x
                       && pos.z >= bounds.min.z && pos.z <= bounds.max.z;

            if (inZone)
            {
                if (self.CharacterActions == null) return BTNodeStatus.Failure;
                if (self.CharacterActions.CurrentAction != null) return BTNodeStatus.Running;
                try
                {
                    var action = new CharacterAction_FinishConstruction(self, building);
                    self.CharacterActions.ExecuteAction(action);
                    _hasActiveAction = true;
                }
                catch (System.Exception e) { Debug.LogException(e); return BTNodeStatus.Failure; }
                return BTNodeStatus.Running;
            }

            // Walk to the zone centre.
            var dest = bounds.center;
            var movement = self.CharacterMovement;
            if (movement == null) return BTNodeStatus.Failure;
            if (!_activeDestination.HasValue || Vector3.Distance(_activeDestination.Value, dest) > 0.5f
                || !movement.HasPath)
            {
                movement.SetDestination(dest);
                _activeDestination = dest;
            }
            return BTNodeStatus.Running;
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
                var dest = bounds.center;
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
