using System.Collections.Generic;
using UnityEngine;
using MWI.Ambition;
using MWI.WorldSystem;

/// <summary>
/// Composite GOAP action: the founder/NPC autonomously gathers + delivers + consumes
/// every material listed in an ambition-targeted Building's
/// <see cref="Building.ConstructionRequirements"/>, then finalizes it.
///
/// <para>Lifecycle is GOAP-shaped (this is a single planner step from
/// <see cref="NeedAmbitionFinishConstruction"/>), but internally runs a 5-step state
/// machine that mirrors the existing JobBuilder pipeline
/// (TakeMaterial → GoToZone → Drop → Finalize) extended with a Harvest step at the
/// front for the no-storage-yet case:</para>
/// <list type="number">
/// <item>Carrying a relevant item → walk to BuildingZone + queue
/// <see cref="CharacterDropItem"/>. The drop spawns a fresh <see cref="WorldItem"/> at
/// the actor's own position; while the actor is inside the zone this lands the item
/// where <see cref="CharacterAction_FinishConstruction.ConsumeFromZone"/> can eat it
/// the next tick.</item>
/// <item>A loose <see cref="WorldItem"/> nearby matches a missing req (typically the
/// freshly-harvested output at a tree's foot) → walk to it + queue
/// <see cref="CharacterPickUpItem"/>.</item>
/// <item>A <see cref="Harvestable"/> visible inside
/// <see cref="CharacterAwareness.GetVisibleInteractables{Harvestable}"/> yields a
/// missing item AND <see cref="Harvestable.CanHarvest"/> → walk to its
/// <see cref="InteractableObject.InteractionZone"/> + queue
/// <see cref="CharacterHarvestAction"/>. Mirrors the canonical rule #36 + softlock
/// guard from <see cref="GoapAction_HarvestResources"/> verbatim.</item>
/// <item>Inside the BuildingZone AND at least one zone-resident WorldItem already
/// matches a req → queue <see cref="CharacterAction_FinishConstruction"/> so it
/// consumes the dropped items and bumps progress.</item>
/// <item>Nothing in awareness yields a missing item → set <c>_isComplete = true</c>
/// so the GOAP planner re-evaluates next tick. The BT will fall through to
/// <see cref="BTAction_Wander"/>; the next time the planner re-runs, awareness will
/// be re-scanned from the new wander position. The 4-step throttle on
/// <see cref="CharacterGoapController.Replan"/> means the planner runs ~2/s, so the
/// NPC drifts and re-checks at a comfortable cadence.</item>
/// </list>
///
/// <para>Visibility / inspector hook: because this lives as a GoapAction inside a
/// GoapGoal, <see cref="CharacterGoapController.CurrentGoalName"/> renders
/// "FulfillAmbitionConstruction" and the BT debug label shows GOAP — closing the
/// "his goap goal and action is at none" gap that the prior BTAction-only path had.</para>
///
/// <para>Authority: GOAP runs server-only (gated by
/// <see cref="NPCBehaviourTree.Update"/> IsServer); all queued <c>CharacterAction</c>s
/// run their own replication paths (Harvest / Pickup / Drop / FinishConstruction).
/// No new replication channel.</para>
/// </summary>
public class GoapAction_FulfillAmbitionConstruction : GoapAction
{
    public override string ActionName => "FulfillAmbitionConstruction";

    public override Dictionary<string, bool> Preconditions => new Dictionary<string, bool>
    {
        { "ambitionBuildingFinalized", false }
    };

    public override Dictionary<string, bool> Effects => new Dictionary<string, bool>
    {
        { "ambitionBuildingFinalized", true }
    };

    /// <summary>Moderate — not cheap (real work) but not punitively expensive.</summary>
    public override float Cost => 5f;

    private bool _isComplete = false;
    public override bool IsComplete => _isComplete;

    // Reused scratch buffers (rule #34: no per-tick allocation).
    private readonly Dictionary<ItemSO, int> _scratchMissing = new Dictionary<ItemSO, int>(4);
    private readonly Collider[] _scratchOverlap = new Collider[32];

    // Cached movement target across consecutive Execute calls (planner reuses the
    // same instance until _isComplete = true).
    private Vector3? _activeDestination;
    private Harvestable _targetHarvestable;
    private YieldMode _targetMode;
    private bool _actionStarted; // true while a queued CharacterAction is in flight.

    // Reused list of missing ItemSO keys — handed to Harvestable.HasAnyDestructionOutput.
    // Repopulated each Execute from _scratchMissing.Keys.
    private readonly System.Collections.Generic.List<ItemSO> _scratchMissingKeys = new System.Collections.Generic.List<ItemSO>(4);

    /// <summary>
    /// How a candidate <see cref="Harvestable"/> yields a wanted item.
    /// <see cref="Harvest"/> = pick its <c>HarvestOutputs</c> via
    /// <see cref="CharacterHarvestAction"/>; <see cref="Destroy"/> = chop the node
    /// for its <c>DestructionOutputs</c> via <see cref="CharacterAction_DestroyHarvestable"/>
    /// (the apple-tree-yields-Wood-on-destruction case).
    /// </summary>
    private enum YieldMode { None, Harvest, Destroy }

    public override bool IsValid(Character worker)
    {
        if (worker == null) return false;
        var target = FindTargetBuilding(worker);
        return target != null && target.IsUnderConstruction;
    }

    public override void Execute(Character worker)
    {
        if (_isComplete) return;

        var building = FindTargetBuilding(worker);
        if (building == null || !building.IsUnderConstruction || building.BuildingZone == null)
        {
            _isComplete = true;
            return;
        }

        // Wait for any queued CharacterAction to finish — NPCBehaviourTree.Update pauses
        // the BT while CurrentAction != null, so we won't even re-enter; but Replan still
        // runs at 0.5 Hz so be defensive.
        if (worker.CharacterActions != null && worker.CharacterActions.CurrentAction != null)
            return;

        ComputeMissingMaterials(building, _scratchMissing);

        var zoneBounds = building.BuildingZone.bounds;
        Vector3 actorPos = worker.transform.position;
        bool inZone = actorPos.x >= zoneBounds.min.x && actorPos.x <= zoneBounds.max.x
                   && actorPos.z >= zoneBounds.min.z && actorPos.z <= zoneBounds.max.z;

        // Step 1: carrying a relevant item → walk to zone + drop.
        if (_scratchMissing.Count > 0)
        {
            var carried = FindCarriedRelevantItem(worker, _scratchMissing);
            if (carried != null)
            {
                if (inZone)
                {
                    TryQueueAction(worker, new CharacterDropItem(worker, carried));
                    return;
                }
                WalkTo(worker, zoneBounds.center);
                return;
            }
        }

        // Step 2: loose WorldItem nearby matching a missing req → walk + pickup.
        if (_scratchMissing.Count > 0)
        {
            var loose = FindNearbyMatchingWorldItem(worker, _scratchMissing, out var looseObj);
            if (loose != null && looseObj != null)
            {
                if (IsInPickupRange(worker, looseObj))
                {
                    TryQueueAction(worker, new CharacterPickUpItem(worker, loose, looseObj));
                    return;
                }
                WalkTo(worker, looseObj.transform.position);
                return;
            }
        }

        // Refresh the keys list so destruction queries can use it.
        _scratchMissingKeys.Clear();
        foreach (var k in _scratchMissing.Keys) _scratchMissingKeys.Add(k);

        // Step 3: cached harvestable still yields something we need + accessible?
        if (_targetHarvestable != null
            && _targetHarvestable.gameObject != null
            && _targetMode != YieldMode.None)
        {
            // Re-validate that the cached mode is still viable on this tick.
            var modeCheck = ClassifyYield(_targetHarvestable, _scratchMissing, _scratchMissingKeys, worker);
            if (modeCheck == _targetMode)
            {
                DriveHarvestableInteraction(worker, _targetHarvestable, _targetMode);
                return;
            }
            // Mode shifted (e.g. harvestable was harvested-out and destruction is now the path):
            // drop the cache and re-scan.
            _targetHarvestable = null;
            _targetMode = YieldMode.None;
        }

        // Step 4: scan awareness for a harvestable that yields a missing item — try Harvest
        // first (cheaper / preserves the node), then Destroy (chop the apple tree for Wood).
        if (_scratchMissing.Count > 0)
        {
            var awareness = worker.CharacterAwareness;
            if (awareness != null)
            {
                var candidates = awareness.GetVisibleInteractables<Harvestable>();
                // Two-pass: any harvest match wins over any destroy match in awareness.
                for (int i = 0; i < candidates.Count; i++)
                {
                    var h = candidates[i];
                    if (h == null) continue;
                    if (!h.CanHarvest()) continue;
                    if (!HarvestableYieldsAny(h, _scratchMissing)) continue;
                    _targetHarvestable = h;
                    _targetMode = YieldMode.Harvest;
                    DriveHarvestableInteraction(worker, h, YieldMode.Harvest);
                    return;
                }
                for (int i = 0; i < candidates.Count; i++)
                {
                    var h = candidates[i];
                    if (h == null) continue;
                    if (!CanNpcDestroy(h)) continue;
                    if (!h.HasAnyDestructionOutput(_scratchMissingKeys)) continue;
                    _targetHarvestable = h;
                    _targetMode = YieldMode.Destroy;
                    DriveHarvestableInteraction(worker, h, YieldMode.Destroy);
                    return;
                }
            }
        }

        // Step 5: inside zone + zone has consumable items → queue the consume action.
        if (inZone && AnyZoneItemSatisfiesReq(building, _scratchMissing))
        {
            TryQueueAction(worker, new CharacterAction_FinishConstruction(worker, building));
            return;
        }

        // Step 6: empty awareness, nothing to consume, nothing to deliver — give up this
        // pass. The planner will replan after the throttle window; if the NPC has moved
        // (Wander branch) by then, awareness will catch a new harvestable.
        if (NPCDebug.VerboseActions)
        {
            var missingNames = new System.Text.StringBuilder();
            foreach (var kv in _scratchMissing)
            {
                if (missingNames.Length > 0) missingNames.Append(", ");
                missingNames.Append(kv.Key?.ItemName ?? "?").Append('×').Append(kv.Value);
            }
            int visibleHarv = worker.CharacterAwareness != null
                ? worker.CharacterAwareness.GetVisibleInteractables<Harvestable>().Count
                : 0;
            // Surface why each visible harvestable was rejected — most common scenario is
            // "1 visible, but outputs Apple while we need Wood" or "destruction would yield
            // Wood but AllowNpcDestruction is false". Logged once at completion (not per tick).
            var rejReasons = new System.Text.StringBuilder();
            if (worker.CharacterAwareness != null)
            {
                var cands = worker.CharacterAwareness.GetVisibleInteractables<Harvestable>();
                for (int i = 0; i < cands.Count; i++)
                {
                    var h = cands[i];
                    if (h == null) continue;
                    rejReasons.Append("  ").Append(h.name)
                        .Append(" canHarvest=").Append(h.CanHarvest())
                        .Append(" yieldsMissing=").Append(HarvestableYieldsAny(h, _scratchMissing))
                        .Append(" allowDestroy=").Append(h.AllowDestruction)
                        .Append(" allowNpcDestroy=").Append(h.AllowNpcDestruction)
                        .Append(" destroyYieldsMissing=").Append(h.HasAnyDestructionOutput(_scratchMissingKeys))
                        .Append('\n');
                }
            }
            Debug.Log($"<color=orange>[GoapAction_FulfillAmbitionConstruction]</color> " +
                $"{worker.CharacterName}: no actionable step. missing=[{missingNames}], visibleHarvestables={visibleHarv}, inZone={inZone}.\n{rejReasons}" +
                $"Completing action so BT falls through to Wander; will retry next Replan.");
        }
        _isComplete = true;
    }

    public override void Exit(Character worker)
    {
        // Drop sticky state — the next plan invocation gets a fresh instance anyway, but
        // be defensive in case a reused instance ever lands here.
        _isComplete = false;
        _actionStarted = false;
        _activeDestination = null;
        _targetHarvestable = null;
        _targetMode = YieldMode.None;
        worker?.CharacterMovement?.ResetPath();
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Locates the building this ambition wants finalized. v1: scans
    /// <see cref="BuildingManager.allBuildings"/> for one placed by this actor that
    /// matches a Task_FinishConstruction.TargetBlueprint in the active step. Returns
    /// the first under-construction match; null if none.
    /// </summary>
    private static Building FindTargetBuilding(Character worker)
    {
        var ambition = worker?.CharacterAmbition;
        if (ambition == null || !ambition.HasActive) return null;
        if (!(ambition.Current?.CurrentStepQuest is AmbitionQuest aq)) return null;

        var tasks = aq.Tasks;
        if (tasks == null) return null;

        var bm = BuildingManager.Instance;
        if (bm == null) return null;

        string actorId = worker.CharacterId;
        if (string.IsNullOrEmpty(actorId)) return null;

        for (int t = 0; t < tasks.Count; t++)
        {
            if (!(tasks[t] is Task_FinishConstruction finish)) continue;
            if (finish.TargetBlueprint == null) continue;
            for (int i = 0; i < bm.allBuildings.Count; i++)
            {
                var b = bm.allBuildings[i];
                if (b == null) continue;
                if (!b.IsUnderConstruction) continue;
                if (b.Blueprint != finish.TargetBlueprint) continue;
                if (b.PlacedByCharacterId.Value.ToString() != actorId) continue;
                return b;
            }
        }
        return null;
    }

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

    /// <summary>
    /// NPC-side destruction gate. Mirrors <see cref="Harvestable.CanDestroyWith"/> minus
    /// the held-tool check (the held tool is validated by
    /// <see cref="CharacterAction_DestroyHarvestable.CanExecute"/> when the action is
    /// queued — if the actor's hands are wrong the action no-ops and the GoapAction
    /// completes harmlessly). Designers can opt a harvestable out of NPC chopping via
    /// <see cref="Harvestable.AllowNpcDestruction"/> on its SO.
    /// </summary>
    private static bool CanNpcDestroy(Harvestable h)
    {
        if (h == null) return false;
        if (!h.AllowDestruction) return false;
        if (!h.AllowNpcDestruction) return false;
        return true;
    }

    /// <summary>
    /// Returns the yield mode (harvest preferred over destroy) by which
    /// <paramref name="h"/> can produce any of the items in <paramref name="missing"/>.
    /// Called by the cached-target validity check in step 3.
    /// </summary>
    private static YieldMode ClassifyYield(Harvestable h, Dictionary<ItemSO, int> missing,
        System.Collections.Generic.List<ItemSO> missingKeys, Character actor)
    {
        if (h == null || h.gameObject == null) return YieldMode.None;
        if (h.CanHarvest() && HarvestableYieldsAny(h, missing)) return YieldMode.Harvest;
        if (CanNpcDestroy(h) && h.HasAnyDestructionOutput(missingKeys)) return YieldMode.Destroy;
        return YieldMode.None;
    }

    /// <summary>
    /// Returns the first carried <see cref="ItemInstance"/> matching a missing material.
    /// Looks at BOTH the bag inventory (when one is equipped) AND the actor's
    /// HandsController — see <see cref="CharacterEquipment.PickUpItem"/> which falls back
    /// to <see cref="CharacterEquipment.CarryItemInHand"/> when no bag is equipped (the
    /// typical NPC case). Skipping the hand check made the founder pick up wood, fail to
    /// recognise it as carried, fall through to the loose-item scan, walk to the next wood
    /// on the ground, and loop because hands were already full (PickUpItem rejects).
    /// </summary>
    private static ItemInstance FindCarriedRelevantItem(Character self, Dictionary<ItemSO, int> missing)
    {
        // 1. Bag inventory (if equipped).
        var equipment = self.CharacterEquipment;
        if (equipment != null && equipment.HaveInventory())
        {
            var inv = equipment.GetInventory();
            if (inv != null && inv.ItemSlots != null)
            {
                for (int i = 0; i < inv.ItemSlots.Count; i++)
                {
                    var s = inv.ItemSlots[i];
                    if (s == null || s.IsEmpty()) continue;
                    var inst = s.ItemInstance;
                    if (inst == null || inst.ItemSO == null) continue;
                    if (missing.ContainsKey(inst.ItemSO)) return inst;
                }
            }
        }

        // 2. Hand-held item. CharacterDropItem.OnApplyEffect already drops from hand when
        // the item isn't found in the bag inventory, so the drop side handles both paths
        // — we just have to surface the hand item here so Step 1 of the orchestrator can
        // trigger the walk-to-zone-and-drop branch.
        var hands = self.CharacterVisual?.BodyPartsController?.HandsController;
        if (hands != null && hands.CarriedItem != null)
        {
            var carried = hands.CarriedItem;
            if (carried.ItemSO != null && missing.ContainsKey(carried.ItemSO)) return carried;
        }
        return null;
    }

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

    private static bool IsInPickupRange(Character self, GameObject worldObject)
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
    /// Mode-dispatched approach + interaction. <see cref="YieldMode.Harvest"/> queues
    /// <see cref="CharacterHarvestAction"/>; <see cref="YieldMode.Destroy"/> queues
    /// <see cref="CharacterAction_DestroyHarvestable"/>. Both share the InteractionZone-
    /// based arrival gate (rule #36 + softlock guard) verbatim from
    /// <see cref="GoapAction_HarvestResources"/>.
    /// </summary>
    private void DriveHarvestableInteraction(Character self, Harvestable target, YieldMode mode)
    {
        if (target == null || target.gameObject == null) { _isComplete = true; return; }
        var movement = self.CharacterMovement;
        if (movement == null) { _isComplete = true; return; }

        // Rule #36: prefer InteractionZone-based arrival; softlock guard for sampled landing.
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
            self.transform.LookAt(new Vector3(target.transform.position.x, self.transform.position.y, target.transform.position.z));
            CharacterAction action = mode == YieldMode.Destroy
                ? (CharacterAction)new CharacterAction_DestroyHarvestable(self, target)
                : (CharacterAction)new CharacterHarvestAction(self, target);
            TryQueueAction(self, action);
            return;
        }

        if (!_activeDestination.HasValue
            || Vector3.Distance(_activeDestination.Value, gatherPos) > 0.5f
            || !movement.HasPath)
        {
            try { movement.SetDestination(gatherPos); } catch (System.Exception e) { Debug.LogException(e); }
            _activeDestination = gatherPos;
        }
    }

    private void WalkTo(Character self, Vector3 dest)
    {
        var movement = self.CharacterMovement;
        if (movement == null) { _isComplete = true; return; }
        if (!_activeDestination.HasValue
            || Vector3.Distance(_activeDestination.Value, dest) > 0.5f
            || !movement.HasPath)
        {
            try { movement.SetDestination(dest); } catch (System.Exception e) { Debug.LogException(e); }
            _activeDestination = dest;
        }
    }

    private void TryQueueAction(Character worker, CharacterAction action)
    {
        try
        {
            if (worker.CharacterActions.ExecuteAction(action))
            {
                _actionStarted = true;
                _activeDestination = null;
                action.OnActionFinished += () =>
                {
                    _actionStarted = false;
                    // Don't complete the GoapAction here — let the next Execute re-evaluate.
                    // E.g. after harvest, we still need to pickup + carry + drop + consume.
                };
            }
        }
        catch (System.Exception e) { Debug.LogException(e); }
    }
}
