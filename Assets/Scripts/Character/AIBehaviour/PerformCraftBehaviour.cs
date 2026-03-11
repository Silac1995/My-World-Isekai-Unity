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

        private bool _isFinished = false;
        private bool _isWaiting = false;
        private Coroutine _craftCoroutine;

        private enum CraftPhase
        {
            SearchingOrder,
            MovingToStation,
            Crafting
        }

        private CraftPhase _currentPhase = CraftPhase.SearchingOrder;

        public bool IsFinished => _isFinished;

        public PerformCraftBehaviour(NPCController npc, JobCrafter job)
        {
            _npc = npc;
            _job = job;

            if (_job.Workplace is CraftingBuilding cb)
            {
                _manager = cb.GetJobsOfType<JobLogisticsManager>().FirstOrDefault();
            }
        }

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

                case CraftPhase.Crafting:
                    _craftCoroutine = _npc.StartCoroutine(WaitAndCraft(selfCharacter, 5f)); // 5 secondes de craft
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
                        if (station.CanCraft(_currentOrder.ItemToCraft) && (!station.IsOccupied || station.Occupant == self))
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

            _currentStation.Use(self);
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
                    _currentPhase = CraftPhase.Crafting;
                }
            }
        }

        private IEnumerator WaitAndCraft(Character self, float time)
        {
            _isWaiting = true;
            Debug.Log($"<color=cyan>[PerformCraft]</color> {self.CharacterName} commence la fabrication de {_currentOrder.ItemToCraft.ItemName}...");
            
            // TODO: Jouer animation d'artisanat selon la station (Enclume, Etabli...)
            
            yield return new WaitForSeconds(time);

            // Crafting terminé
            if (_currentStation != null && _currentOrder != null)
            {
                ItemInstance craftedItem = _currentStation.Craft(_currentOrder.ItemToCraft, self, true, _job.Workplace as CommercialBuilding);
                if (craftedItem != null)
                {
                    if (_job.Workplace is CommercialBuilding building)
                    {
                        building.AddToInventory(craftedItem);
                    }
                    _manager.UpdateCraftingOrderProgress(_currentOrder, 1);
                    
                    if (_job.RequiredSkill != null && self.CharacterSkills != null)
                    {
                        self.CharacterSkills.GainXP(_job.RequiredSkill, 10);
                    }
                }
            }

            _isFinished = true;
            _isWaiting = false;
            _craftCoroutine = null;
        }

        public void Exit(Character selfCharacter)
        {
            if (_npc != null && _craftCoroutine != null)
            {
                _npc.StopCoroutine(_craftCoroutine);
                _craftCoroutine = null;
            }

            if (_currentStation != null && _currentStation.Occupant == selfCharacter)
            {
                _currentStation.Release();
            }

            _isWaiting = false;
            selfCharacter.CharacterMovement?.ResetPath();
        }

        public void Terminate() => _isFinished = true;
    }
}
