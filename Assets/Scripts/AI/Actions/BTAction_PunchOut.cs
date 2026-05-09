using UnityEngine;
using System.Linq;

namespace MWI.AI
{
    /// <summary>
    /// Action that forces an NPC to clock out of a commercial building when its shift ends.
    /// Moves toward the BuildingZone and triggers Action_PunchOut.
    /// </summary>
    public class BTAction_PunchOut : BTNode
    {
        private enum PunchOutPhase
        {
            CleaningUpInventory,
            MovingToBuilding,
            MovingToTimeClock,
            PunchingOut
        }

        private PunchOutPhase _currentPhase = PunchOutPhase.CleaningUpInventory;
        private bool _warnedNoTimeClock = false;
        private bool _warnedNoInteractable = false;

        protected override void OnEnter(Blackboard bb)
        {
            _currentPhase = PunchOutPhase.CleaningUpInventory;
            _warnedNoTimeClock = false;
            _warnedNoInteractable = false;
        }

        protected override BTNodeStatus OnExecute(Blackboard bb)
        {
            Character self = bb.Self;
            if (self == null) return BTNodeStatus.Failure;

            CommercialBuilding workplace = self.CharacterJob?.Workplace;
            if (workplace == null || !workplace.IsWorkerOnShift(self))
            {
                // If we no longer have a building or are no longer inside it (Action_PunchOut succeeded), success.
                return BTNodeStatus.Success;
            }

            var movement = self.CharacterMovement;
            if (movement == null) return BTNodeStatus.Failure;

            switch (_currentPhase)
            {
                case PunchOutPhase.CleaningUpInventory:
                    return HandleCleaningUpInventory(self, movement, workplace);
                case PunchOutPhase.MovingToBuilding:
                    return HandleMovementToBuilding(self, movement, workplace);
                case PunchOutPhase.MovingToTimeClock:
                    return HandleMovementToTimeClock(self, movement, workplace);
                case PunchOutPhase.PunchingOut:
                    return HandlePunchingOut(self);
            }

            return BTNodeStatus.Failure;
        }

        private BTNodeStatus HandleCleaningUpInventory(Character self, CharacterMovement movement, CommercialBuilding workplace)
        {
            // Check whether they have an item in hand or in their inventory
            var inventory = self.CharacterEquipment?.GetInventory();
            ItemInstance carriedItem = null;

            if (inventory != null && inventory.ItemSlots.Exists(s => !s.IsEmpty()))
            {
                carriedItem = inventory.ItemSlots.FindLast(s => !s.IsEmpty()).ItemInstance;
            }
            if (carriedItem == null)
            {
                carriedItem = self.CharacterVisual?.BodyPartsController?.HandsController?.CarriedItem;
            }

            // If nothing to clean up, advance to the next phase
            if (carriedItem == null)
            {
                _currentPhase = PunchOutPhase.MovingToBuilding;
                return BTNodeStatus.Running;
            }

            // If we are carrying something, take it to the building's StorageZone (or at worst the center)
            Zone storageZone = workplace.StorageZone;
            Zone deliveryZone = workplace.DeliveryZone;
            Collider buildingZoneCol = workplace.BuildingZone;

            Collider dropCol = null;
            if (storageZone != null) dropCol = storageZone.GetComponent<Collider>();
            else if (deliveryZone != null) dropCol = deliveryZone.GetComponent<Collider>();
            else dropCol = buildingZoneCol;

            Vector3 dropPos = dropCol != null ? dropCol.bounds.center : workplace.transform.position;
            if (storageZone != null) dropPos = storageZone.GetRandomPointInZone();
            else if (deliveryZone != null) dropPos = deliveryZone.GetRandomPointInZone();

            // Arrived? Drop it.
            if (dropCol != null && dropCol.bounds.Contains(self.transform.position))
            {
                movement.Stop();
                var dropAction = new CharacterDropItem(self, carriedItem);

                if (self.CharacterActions.ExecuteAction(dropAction))
                {
                    // Action accepted, let the animation play. On the next call, `carriedItem` will check if any remains.
                    // If we were a harvester/transporter, route the item into the building to avoid losing it:
                    dropAction.OnActionFinished += () =>
                    {
                        workplace.AddToInventory(carriedItem);
                        Debug.Log($"<color=cyan>[Punch Out]</color> {self.CharacterName} dropped {carriedItem.ItemSO.ItemName} before leaving.");
                    };
                }
                else
                {
                    // Safety fallback if the action is blocked
                    if (self.CharacterActions.CurrentAction == null)
                    {
                        if (inventory != null && inventory.HasAnyItemSO(new System.Collections.Generic.List<ItemSO> { carriedItem.ItemSO }))
                        {
                            inventory.RemoveItem(carriedItem, self);
                        }
                        else
                        {
                            self.CharacterVisual?.BodyPartsController?.HandsController?.DropCarriedItem();
                        }
                        workplace.AddToInventory(carriedItem);
                    }
                }
            }
            else
            {
                // Move toward the drop zone
                if (!movement.HasPath || movement.RemainingDistance <= movement.StoppingDistance + 0.5f)
                {
                    movement.SetDestination(dropPos);
                }
            }

            return BTNodeStatus.Running;
        }

        private BTNodeStatus HandleMovementToBuilding(Character self, CharacterMovement movement, CommercialBuilding workplace)
        {
            // Arrived inside the BuildingZone → advance to the TimeClock phase
            // (which itself soft-falls-back to zone-punch if no clock is authored).
            if (workplace.BuildingZone != null && workplace.BuildingZone.bounds.Contains(self.transform.position))
            {
                movement.Stop();
                _currentPhase = PunchOutPhase.MovingToTimeClock;
                return BTNodeStatus.Running;
            }

            if (!movement.HasPath || movement.RemainingDistance <= movement.StoppingDistance + 0.5f)
            {
                if (workplace.BuildingZone != null)
                {
                    Vector3 dest = workplace.GetRandomPointInBuildingZone(self.transform.position.y);
                    movement.SetDestination(dest);
                }
            }

            return BTNodeStatus.Running;
        }

        private BTNodeStatus HandleMovementToTimeClock(Character self, CharacterMovement movement, CommercialBuilding workplace)
        {
            var clock = workplace.TimeClock;

            // Soft fallback (rule #4): no clock authored → use the legacy zone-punch
            // directly. One-shot warning so the author sees the gap.
            if (clock == null)
            {
                if (!_warnedNoTimeClock)
                {
                    Debug.LogWarning($"<color=orange>[BTAction_PunchOut]</color> {workplace.BuildingName} has no TimeClockFurniture. Falling back to zone-punch for {self.CharacterName}.");
                    _warnedNoTimeClock = true;
                }
                movement.Stop();
                _currentPhase = PunchOutPhase.PunchingOut;
                Action_PunchOut fallback = new Action_PunchOut(self, workplace);
                if (fallback.CanExecute())
                {
                    self.CharacterActions.ExecuteAction(fallback);
                }
                return BTNodeStatus.Running;
            }

            var interactable = clock.GetComponent<TimeClockFurnitureInteractable>();
            if (interactable == null)
            {
                if (!_warnedNoInteractable)
                {
                    // One-shot per BT-branch-entry (OnEnter resets the flag) to avoid per-tick spam
                    // on misconfigured workplaces.
                    Debug.LogError($"<color=red>[BTAction_PunchOut]</color> {workplace.BuildingName}'s TimeClockFurniture has no TimeClockFurnitureInteractable sibling. Falling back to zone-punch.");
                    _warnedNoInteractable = true;
                }
                movement.Stop();
                _currentPhase = PunchOutPhase.PunchingOut;
                Action_PunchOut fallback = new Action_PunchOut(self, workplace);
                if (fallback.CanExecute())
                {
                    self.CharacterActions.ExecuteAction(fallback);
                }
                return BTNodeStatus.Running;
            }

            // Arrival: canonical Interactable-System rule via
            // InteractableObject.IsCharacterInInteractionZone — tests
            // Character.transform.position against the authored InteractionZone.
            bool inZone = interactable.IsCharacterInInteractionZone(self);

            if (inZone)
            {
                movement.Stop();
                interactable.Interact(self);
                _currentPhase = PunchOutPhase.PunchingOut;
                return BTNodeStatus.Running;
            }

            // Not yet in range — path toward the authored InteractionPoint.
            Vector3 clockTarget = clock.GetInteractionPosition();
            if (!movement.HasPath || movement.RemainingDistance <= movement.StoppingDistance + 0.5f)
            {
                movement.SetDestination(clockTarget);
            }
            return BTNodeStatus.Running;
        }

        private BTNodeStatus HandlePunchingOut(Character self)
        {
            var currentAction = self.CharacterActions.CurrentAction;
            if (currentAction != null && currentAction is Action_PunchOut)
            {
                return BTNodeStatus.Running; // Still playing the animation
            }

            // The action is complete, we should have left
            return BTNodeStatus.Success;
        }

        protected override void OnExit(Blackboard bb)
        {
            base.OnExit(bb);
            bb.Self?.CharacterMovement?.ResetPath();

            // FailSafe in case the root aborts us before the animation finishes
            Character self = bb.Self;
            if (self != null && self.CharacterJob != null)
            {
                var workplace = self.CharacterJob.Workplace;
                if (workplace != null && workplace.IsWorkerOnShift(self))
                {
                    workplace.WorkerEndingShift(self);
                }
            }

            // Reset the GOAP so it moves on to something else (e.g. go home, eat)
            if (self != null && self.CharacterGoap != null)
            {
                self.CharacterGoap.CancelPlan();
            }

            // Clean up any lingering physical action block 
            if (self != null && self.CharacterActions != null)
            {
                self.CharacterActions.ClearCurrentAction();
            }
        }
    }
}
