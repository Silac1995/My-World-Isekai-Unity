using UnityEngine;
using System.Linq;
using System.Collections;
using System.Collections.Generic;

namespace MWI.AI
{
    public class PerformCraftBehaviour : IAIBehaviour
    {
        private NPCController _npc;
        private JobCrafter _job;
        private CraftingOrder _currentOrder;
        private CraftingStation _currentStation;
        private JobLogisticsManager _manager;
        private System.Action _onFinished;

        private bool _isFinished = false;
        private bool _isWaiting = false;
        private Coroutine _craftCoroutine;

        private enum CraftPhase
        {
            SearchingOrder,
            MovingToStation,
            ExecutingAction
        }

        private CraftPhase _currentPhase = CraftPhase.SearchingOrder;

        public bool IsFinished => _isFinished;

        public PerformCraftBehaviour(NPCController npc, JobCrafter job, System.Action onFinished = null)
        {
            _npc = npc;
            _job = job;
            _onFinished = onFinished;

            if (_job.Workplace is CraftingBuilding cb)
            {
                _manager = cb.GetJobsOfType<JobLogisticsManager>().FirstOrDefault();
            }
        }

        public void Enter(Character selfCharacter) { }
    public void Act(Character selfCharacter)
        {
            if (_isFinished || _isWaiting) return;

            var movement = selfCharacter.CharacterMovement;
            if (movement == null) return;

            switch (_currentPhase)
            {
                case CraftPhase.SearchingOrder:
                    HandleSearchOrder(selfCharacter);
                    break;

                case CraftPhase.MovingToStation:
                    HandleMovementToStation(selfCharacter, movement);
                    break;

                case CraftPhase.ExecutingAction:
                    HandleCraftingExecution(selfCharacter);
                    break;
            }
        }

        private void HandleSearchOrder(Character self)
        {
            if (_manager == null)
            {
                Debug.Log($"<color=orange>[PerformCraft]</color> {self.CharacterName} : Pas de Manager Logistique dans le bâtiment.");
                _isFinished = true;
                return;
            }

            _currentOrder = _manager.GetNextAvailableCraftingOrder();
            if (_currentOrder == null)
            {
                // Pas de commande, on s'arrête là (le root du BT passera sûrement à Wander/Idle)
                _isFinished = true;
                return;
            }

            // Trouver une station libre et compatible
            if (_job.Workplace is CraftingBuilding cb)
            {
                foreach (var room in cb.Rooms)
                {
                    foreach (var station in room.GetFurnitureOfType<CraftingStation>())
                    {
                        if (station.CanCraft(_currentOrder.ItemToCraft) && (station.IsFree() || station.Occupant == self))
                        {
                            _currentStation = station;
                            break;
                        }
                    }
                    if (_currentStation != null) break;
                }
            }

            if (_currentStation == null)
            {
                Debug.Log($"<color=orange>[PerformCraft]</color> {self.CharacterName} : Pas de station libre capable de crafter {_currentOrder.ItemToCraft.ItemName}.");
                _isFinished = true;
                return;
            }

            _currentStation.Reserve(self);
            _currentPhase = CraftPhase.MovingToStation;
        }

        private void HandleMovementToStation(Character self, CharacterMovement movement)
        {
            if (_currentStation == null)
            {
                _isFinished = true;
                return;
            }

            Vector3 targetPos = _currentStation.InteractionPoint != null ? _currentStation.InteractionPoint.position : _currentStation.transform.position;
            
            if (!movement.HasPath || movement.RemainingDistance <= movement.StoppingDistance + 0.5f)
            {
                if (Vector3.Distance(self.transform.position, targetPos) > movement.StoppingDistance + 0.5f)
                {
                    movement.SetDestination(targetPos);
                }
                else
                {
                    // Arrivé à la station
                    movement.ResetPath();
                    _currentStation.Use(self);

                    // TODO: Gérer la couleur dynamiquement selon l'ItemSO si nécessaire (ex: via une palette ou recette)
                    Color targetColor = Color.white; 
                    
                    self.CharacterActions.ExecuteAction(new CharacterCraftAction(self, _currentOrder.ItemToCraft, targetColor, default, _currentOrder.ItemToCraft.CraftingDuration));
                    _currentPhase = CraftPhase.ExecutingAction;
                }
            }
        }

        private void HandleCraftingExecution(Character self)
        {
            // Vérifier si l'action est toujours en cours
            var currentAction = self.CharacterActions.CurrentAction;
            
            // Soit il fait spécifiquement un CharacterCraftAction, soit il n'a pas encore eu le temps de s'initialiser
            if (currentAction != null && currentAction is CharacterCraftAction)
            {
                return; // On attend la fin
            }

            // L'action est terminée ou annulée. On vérifie si elle a réussi.
            // Actuellement, Action_Craft Item applique l'effet directement. 
            // On peut détecter le succès global soit en vérifiant l'XP ou la commande.
            // (La station aura crafté l'objet physiquement).
            
            if (_manager != null && _currentOrder != null)
            {
                // Note : CharacterCraftAction appelle _station.Craft() qui crée l'objet.
                // On notifie le manager logistique que la commande avance.
                _manager.UpdateCraftingOrderProgress(_currentOrder, 1);
            }

            if (_job.RequiredSkill != null && self.CharacterSkills != null)
            {
                self.CharacterSkills.GainXP(_job.RequiredSkill, 10);
            }

            FinishBehaviour();
        }

        private void FinishBehaviour()
        {
            if (_isFinished) return;
            _isFinished = true;
            _onFinished?.Invoke();
        }

        public void Exit(Character selfCharacter)
        {
            // Libère la station si on l'occupait ou si on l'avait réservée
            if (_currentStation != null)
            {
                if (_currentStation.Occupant == selfCharacter || _currentStation.ReservedBy == selfCharacter)
                {
                    _currentStation.Release();
                }
            }

            _isWaiting = false;
            selfCharacter.CharacterMovement?.ResetPath();
        }

        public void Terminate()
        {
            FinishBehaviour();
        }
    }
}
