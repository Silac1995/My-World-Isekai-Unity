using System.Collections.Generic;
using UnityEngine;

namespace MWI.AI
{
    /// <summary>
    /// Furniture-source pickup for the transporter. When <see cref="GoapAction_LocateItem"/>
    /// commits to the slot path, it sets <c>JobTransporter.TargetSourceFurniture</c> +
    /// <c>JobTransporter.TargetItemFromFurniture</c>; this action then walks the worker to
    /// the furniture's interaction point and queues a <see cref="CharacterTakeFromFurnitureAction"/>
    /// to drain the slot directly into the worker's hands. Mutually exclusive with the
    /// loose-WorldItem path (<see cref="GoapAction_MoveToItem"/> + <see cref="GoapAction_PickupItem"/>),
    /// which both early-out from <c>IsValid</c> when this path is active.
    ///
    /// Cost <c>0.5f</c> — cheaper than <c>MoveToItem</c> (1) + <c>PickupItem</c> (1) combined,
    /// so the planner prefers this single-shot when the furniture path is registered.
    /// Effects claim <c>atItem=true</c> in addition to <c>itemCarried=true</c> so the planner
    /// can optimise away the redundant <c>MoveToItem</c> node from the resulting plan.
    ///
    /// Registration is gated in <see cref="JobTransporter.PlanNextActions"/> on
    /// <c>TargetSourceFurniture != null</c>: the planner does not call <c>IsValid</c> during
    /// search, so unconditional registration would let it pick this (cheapest) plan even
    /// when the loose path is active, and the resulting replan loop would never break.
    /// </summary>
    public class GoapAction_TakeFromSourceFurniture : GoapAction
    {
        private readonly JobTransporter _job;

        private bool _isComplete;
        private bool _actionStarted;
        private TakeState _state = TakeState.MovingToFurniture;
        private Vector3 _targetPos;

        // Real-time stamp captured the first tick we enter MovingToFurniture for the
        // current furniture target. Mirror of the softlock guard in
        // GoapAction_GatherStorageItems — typical cause of a non-arrival is a missing
        // _interactionPoint Transform on the StorageFurniture prefab so the resolved
        // target sits inside the NavMeshObstacle carve.
        private float _moveStartedAt = -1f;
        private const float FurnitureMoveTimeoutSeconds = 5f;

        public override string ActionName => "Take From Source Furniture";

        // Cheaper than MoveToItem (1) + PickupItem (1) combined so the planner picks
        // this single-shot when registered. PlanOrder() guarantees registration only
        // when TargetSourceFurniture is set, so we cannot win the cost race against
        // the loose path while it's active.
        public override float Cost => 0.5f;

        public override Dictionary<string, bool> Preconditions => new Dictionary<string, bool>
        {
            { "atSourceStorage", true },
            { "itemCarried", false }
        };

        public override Dictionary<string, bool> Effects => new Dictionary<string, bool>
        {
            { "itemCarried", true },
            { "atItem", true }
        };

        public override bool IsComplete => _isComplete;

        private enum TakeState
        {
            MovingToFurniture,
            Taking,
            Done
        }

        public GoapAction_TakeFromSourceFurniture(JobTransporter job)
        {
            _job = job;
        }

        public override bool IsValid(Character worker)
        {
            // Once the physical CharacterAction is queued we must ride out the animation —
            // matches the same "_isActionStarted ride-out" pattern as GoapAction_PickupItem
            // and GoapAction_GatherStorageItems.
            if (_actionStarted) return true;

            if (_job == null || _job.CurrentOrder == null) return false;
            if (_job.TargetSourceFurniture == null || _job.TargetItemFromFurniture == null) return false;

            var furniture = _job.TargetSourceFurniture;
            if (furniture.IsLocked) return false;

            // The slot must still hold our target instance. Walk ItemSlots directly
            // (cheaper than CollectReservedOutgoingInstances + GetItemsInStorageFurniture).
            var slots = furniture.ItemSlots;
            if (slots == null) return false;
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].ItemInstance == _job.TargetItemFromFurniture) return true;
            }
            return false;
        }

        public override void Execute(Character worker)
        {
            if (_isComplete) return;

            var movement = worker.CharacterMovement;
            if (movement == null) return;

            switch (_state)
            {
                case TakeState.MovingToFurniture:
                    if (_job.TargetSourceFurniture == null || _job.TargetItemFromFurniture == null)
                    {
                        _isComplete = true;
                        return;
                    }

                    var furniture = _job.TargetSourceFurniture;

                    // Worker-aware overload — when no _interactionPoint is authored, this
                    // lands the target on the closest InteractionZone face to the worker
                    // (i.e. on the navmesh-walkable side, not inside the obstacle carve).
                    _targetPos = furniture.GetInteractionPosition(worker.transform.position);

                    if (_moveStartedAt < 0f) _moveStartedAt = UnityEngine.Time.unscaledTime;

                    HandleMovementTo(worker, _targetPos, out bool arrived, null);

                    if (!arrived)
                    {
                        // Single-point target: HandleMovementTo's collider-based early-exit
                        // can't fire (no collider). Mirror GoapAction_StageItemForPickup's
                        // 1.5f flat-XZ proximity check so behaviour stays consistent.
                        Vector3 flatWorker = new Vector3(worker.transform.position.x, 0f, worker.transform.position.z);
                        Vector3 flatTarget = new Vector3(_targetPos.x, 0f, _targetPos.z);
                        if (Vector3.Distance(flatWorker, flatTarget) <= 1.5f)
                        {
                            movement.ResetPath();
                            arrived = true;
                        }
                    }

                    if (!arrived && UnityEngine.Time.unscaledTime - _moveStartedAt > FurnitureMoveTimeoutSeconds)
                    {
                        Debug.LogWarning($"<color=orange>[TakeFromFurniture]</color> {worker.CharacterName} could not reach {furniture.FurnitureName} after {FurnitureMoveTimeoutSeconds}s (dist={Vector3.Distance(worker.transform.position, _targetPos):F2}). Blacklisting and falling back to loose-pickup path. CHECK: does {furniture.FurnitureName} have an _interactionPoint Transform assigned in the prefab?");

                        worker.PathingMemory.RecordFailure(furniture.gameObject.GetInstanceID());
                        movement.Stop();
                        movement.ResetPath();

                        // Clear the furniture-source path so the next replan re-runs LocateItem,
                        // which will skip this blacklisted furniture and probably fall through
                        // to the loose-pickup path (or pick a different unlocked furniture).
                        _job.TargetSourceFurniture = null;
                        _job.TargetItemFromFurniture = null;

                        _isComplete = true;
                        return;
                    }

                    if (arrived)
                    {
                        _state = TakeState.Taking;
                        _actionStarted = false;
                    }
                    break;

                case TakeState.Taking:
                    if (_job.TargetSourceFurniture == null || _job.TargetItemFromFurniture == null)
                    {
                        _isComplete = true;
                        return;
                    }

                    if (!_actionStarted)
                    {
                        var takeAction = new CharacterTakeFromFurnitureAction(worker, _job.TargetItemFromFurniture, _job.TargetSourceFurniture);

                        // Capture refs at queue time — _job fields may be mutated by the time
                        // the lambda fires (multi-tick animation window).
                        ItemInstance takenInstance = _job.TargetItemFromFurniture;
                        StorageFurniture takenFromFurniture = _job.TargetSourceFurniture;

                        if (worker.CharacterActions.ExecuteAction(takeAction))
                        {
                            _actionStarted = true;
                            takeAction.OnActionFinished += () =>
                            {
                                FinishTake(worker, takeAction, takenInstance, takenFromFurniture);
                            };
                        }
                        else
                        {
                            Debug.LogWarning($"<color=orange>[TakeFromFurniture]</color> {worker.CharacterName} could not start CharacterTakeFromFurnitureAction. Cooldown + retry.");
                            _job.WaitCooldown = 1f;
                            _job.TargetSourceFurniture = null;
                            _job.TargetItemFromFurniture = null;
                            _isComplete = true;
                        }
                    }
                    break;

                case TakeState.Done:
                    _isComplete = true;
                    break;
            }
        }

        private void FinishTake(Character worker, CharacterTakeFromFurnitureAction takeAction, ItemInstance takenInstance, StorageFurniture fromFurniture)
        {
            if (takeAction.Taken)
            {
                CommercialBuilding source = _job.CurrentOrder != null ? _job.CurrentOrder.Source : null;

                // Mirror GoapAction_PickupItem: the slot transfer doesn't auto-update the
                // source's logical _inventory, so we have to drain the reservation manually.
                bool removed = source != null && source.RemoveExactItemFromInventory(takenInstance);
                if (!removed)
                {
                    // Self-heal pattern (matches GoapAction_PickupItem): the WorldItem-style
                    // logical inventory entry is gone but our reservation is still authoritative
                    // and the item is now physically in our hands. Proceed.
                    if (_job.CurrentOrder != null && _job.CurrentOrder.ReservedItems.Contains(takenInstance))
                    {
                        Debug.LogWarning($"<color=orange>[TakeFromFurniture]</color> {worker.CharacterName}: logical inventory out of sync for {takenInstance?.ItemSO?.ItemName} but reservation + slot extraction are intact → proceeding (self-heal).");
                    }
                    else
                    {
                        Debug.LogWarning($"<color=orange>[TakeFromFurniture]</color> {worker.CharacterName}: extracted {takenInstance?.ItemSO?.ItemName} from {fromFurniture?.FurnitureName} but it's not in the building's logical inventory NOR in our reservation. Proceeding anyway — the item is in our hands.");
                    }
                }

                _job.AddCarriedItem(takenInstance);
                Debug.Log($"<color=green>[TakeFromFurniture]</color> {worker.CharacterName} took {takenInstance?.ItemSO?.ItemName} from {fromFurniture?.FurnitureName} (slot pickup path).");

                // We're done — clear the furniture-source fields so the next plan
                // doesn't try to re-take from the (now-empty) slot.
                _job.TargetSourceFurniture = null;
                _job.TargetItemFromFurniture = null;
            }
            else
            {
                Debug.LogWarning($"<color=orange>[TakeFromFurniture]</color> {worker.CharacterName}: take-from-furniture finished without success. Cooldown + clear furniture target so LocateItem re-runs.");
                _job.WaitCooldown = 1f;
                _job.TargetSourceFurniture = null;
                _job.TargetItemFromFurniture = null;
            }

            // Important: keep _actionStarted = true. JobTransporter.Execute checks IsValid
            // BEFORE IsComplete every tick — IsValid's `if (_actionStarted) return true;`
            // ride-out then keeps the action alive long enough for the IsComplete branch
            // in JobTransporter.Execute to fire and dequeue the next plan action. Resetting
            // it here would make IsValid fail (TargetSourceFurniture is now null), the plan
            // would be discarded, and the worker would replan instead of cleanly advancing.
            _state = TakeState.Done;
            _isComplete = true;
        }

        private void HandleMovementTo(Character worker, Vector3 targetPos, out bool arrived, Collider targetCollider = null, bool bypassEarlyExit = false)
        {
            arrived = false;
            var movement = worker.CharacterMovement;

            if (!bypassEarlyExit && NavMeshUtility.IsCharacterAtTargetZone(worker, targetCollider, 1.5f))
            {
                movement.ResetPath();
                arrived = true;
                return;
            }

            if (movement.PathPending) return;

            bool hasPathFailed = NavMeshUtility.HasPathFailed(movement, 0, 0.2f);
            if (!movement.HasPath || hasPathFailed)
            {
                if (targetCollider != null)
                {
                    bool blacklisted = worker.PathingMemory.RecordFailure(targetCollider.gameObject.GetInstanceID());
                    if (blacklisted)
                    {
                        movement.Stop();
                        movement.ResetPath();
                        return;
                    }
                }
            }

            if (!movement.HasPath)
            {
                movement.SetDestination(targetPos);
                return;
            }

            if (movement.RemainingDistance <= movement.StoppingDistance + 0.5f)
            {
                float distance = Vector3.Distance(
                    new Vector3(worker.transform.position.x, 0, worker.transform.position.z),
                    new Vector3(targetPos.x, 0, targetPos.z));

                if (targetCollider == null && distance > movement.StoppingDistance + 0.5f)
                {
                    movement.SetDestination(targetPos);
                }
                else
                {
                    movement.ResetPath();
                    arrived = true;
                }
            }
        }

        public override void Exit(Character worker)
        {
            _isComplete = false;
            _actionStarted = false;
            _state = TakeState.MovingToFurniture;
            _moveStartedAt = -1f;
            // Don't clear _job.TargetSourceFurniture / _job.TargetItemFromFurniture here —
            // those are owned by GoapAction_LocateItem (and FinishTake handles success-cleanup).
            // Touching them here would race with the planner re-running LocateItem on Exit.
        }
    }
}
