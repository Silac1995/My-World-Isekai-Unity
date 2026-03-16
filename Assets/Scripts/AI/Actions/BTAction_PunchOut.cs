using UnityEngine;
using System.Linq;

namespace MWI.AI
{
    /// <summary>
    /// Action de forcer un NPC à dépointer d'un bâtiment commercial quand son horaire se termine.
    /// Se déplace vers la BuildingZone et déclenche Action_PunchOut.
    /// </summary>
    public class BTAction_PunchOut : BTNode
    {
        private enum PunchOutPhase
        {
            CleaningUpInventory,
            MovingToBuilding,
            PunchingOut
        }

        private PunchOutPhase _currentPhase = PunchOutPhase.CleaningUpInventory;

        protected override void OnEnter(Blackboard bb)
        {
            _currentPhase = PunchOutPhase.CleaningUpInventory;
        }

        protected override BTNodeStatus OnExecute(Blackboard bb)
        {
            Character self = bb.Self;
            if (self == null) return BTNodeStatus.Failure;

            CommercialBuilding workplace = self.CharacterJob?.Workplace;
            if (workplace == null || !workplace.ActiveWorkersOnShift.Contains(self))
            {
                // Si on a plus de building ou qu'on n'est plus dedans (Action_PunchOut a réussi), succès.
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
                case PunchOutPhase.PunchingOut:
                    return HandlePunchingOut(self);
            }

            return BTNodeStatus.Failure;
        }

        private BTNodeStatus HandleCleaningUpInventory(Character self, CharacterMovement movement, CommercialBuilding workplace)
        {
            // Vérifier s'il a un objet en main ou dans son inventaire
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

            // Si rien à nettoyer, on avance à la phase suivante
            if (carriedItem == null)
            {
                _currentPhase = PunchOutPhase.MovingToBuilding;
                return BTNodeStatus.Running;
            }

            // Si on porte qqch, on l'amène dans la StorageZone du bâtiment (ou au pire le centre)
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

            // Arrivé ? On drop.
            if (dropCol != null && dropCol.bounds.Contains(self.transform.position))
            {
                movement.Stop();
                var dropAction = new CharacterDropItem(self, carriedItem);
                
                if (self.CharacterActions.ExecuteAction(dropAction))
                {
                    // Action acceptée, on laisse l'animation jouer. Au prochain appel, `carriedItem` vérifiera s'il en reste.
                    // Si on était un gatherer/transporter, on réclame que l'objet aille dans le bâtiment pour éviter la perte:
                    dropAction.OnActionFinished += () => 
                    {
                        workplace.AddToInventory(carriedItem);
                        Debug.Log($"<color=cyan>[Punch Out]</color> {self.CharacterName} a déposé {carriedItem.ItemSO.ItemName} avant de quitter.");
                    };
                }
                else
                {
                    // Fallback sécurité si action bloquée
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
                // Avancer vers la zone de drop
                if (!movement.HasPath || movement.RemainingDistance <= movement.StoppingDistance + 0.5f)
                {
                    movement.SetDestination(dropPos);
                }
            }

            return BTNodeStatus.Running;
        }

        private BTNodeStatus HandleMovementToBuilding(Character self, CharacterMovement movement, CommercialBuilding workplace)
        {
            if (workplace.BuildingZone != null && workplace.BuildingZone.bounds.Contains(self.transform.position))
            {
                movement.Stop();
                _currentPhase = PunchOutPhase.PunchingOut;
                
                Action_PunchOut punchOut = new Action_PunchOut(self, workplace);
                if (punchOut.CanExecute())
                {
                    self.CharacterActions.ExecuteAction(punchOut);
                    return BTNodeStatus.Running;
                }
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

        private BTNodeStatus HandlePunchingOut(Character self)
        {
            var currentAction = self.CharacterActions.CurrentAction;
            if (currentAction != null && currentAction is Action_PunchOut)
            {
                return BTNodeStatus.Running; // Toujours en train de lire l'animation
            }

            // L'action est terminée, on devrait avoir quitté
            return BTNodeStatus.Success;
        }

        protected override void OnExit(Blackboard bb)
        {
            base.OnExit(bb);
            bb.Self?.CharacterMovement?.ResetPath();
            
            // Sécurité FailSafe si le root nous abort avant la fin de l'anim
            Character self = bb.Self;
            if (self != null && self.CharacterJob != null)
            {
                var workplace = self.CharacterJob.Workplace;
                if (workplace != null && workplace.ActiveWorkersOnShift.Contains(self))
                {
                    workplace.WorkerEndingShift(self);
                }
            }

            // Réinitialiser le GOAP pour qu'il passe à autre chose (ex: rentrer chez soi, manger)
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
