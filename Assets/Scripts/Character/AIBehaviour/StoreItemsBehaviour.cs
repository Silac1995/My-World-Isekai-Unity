using UnityEngine;
using System.Linq;
using System.Collections.Generic;

namespace MWI.AI
{
    /// <summary>
    /// Comportement qui permet à un travailleur de ramasser les objets traînant dans le bâtiment
    /// et de les déplacer vers la zone de stockage.
    /// </summary>
    public class StoreItemsBehaviour : IAIBehaviour
    {
        private NPCController _npc;
        private CommercialBuilding _building;
        private WorldItem _targetItem;
        private bool _isFinished = false;
        private bool _isMovingToItem = false;
        private bool _isMovingToStorage = false;
        private float _checkTimer = 0f;
        private const float CHECK_INTERVAL = 0.5f;

        public bool IsFinished => _isFinished;

        public StoreItemsBehaviour(NPCController npc, CommercialBuilding building)
        {
            _npc = npc;
            _building = building;
        }

        public void Act(Character self)
        {
            if (_isFinished) return;

            if (_targetItem == null && !_isMovingToStorage)
            {
                FindNextItem(self);
                if (_targetItem == null)
                {
                    _isFinished = true;
                    return;
                }
            }

            _checkTimer += UnityEngine.Time.deltaTime;
            if (_checkTimer < CHECK_INTERVAL) return;
            _checkTimer = 0f;

            var movement = self.CharacterMovement;
            if (movement == null) return;

            if (_isMovingToItem)
            {
                HandleMovingToItem(self, movement);
            }
            else if (_isMovingToStorage)
            {
                HandleMovingToStorage(self, movement);
            }
        }

        private void FindNextItem(Character self)
        {
            // Trouver tous les WorldItems dans le bâtiment
            var worldItems = Object.FindObjectsByType<WorldItem>(FindObjectsSortMode.None);
            
            foreach (var item in worldItems)
            {
                if (item.IsBeingCarried) continue;

                // Est-il dans le bâtiment ?
                if (IsPointInBuilding(item.transform.position))
                {
                    // Est-il DÉJÀ dans la storage zone ?
                    if (_building.StorageZone != null && _building.StorageZone.IsPointInZone(item.transform.position))
                    {
                        continue;
                    }

                    _targetItem = item;
                    _isMovingToItem = true;
                    return;
                }
            }
        }

        private bool IsPointInBuilding(Vector3 point)
        {
            if (_building.BuildingZone == null) return false;
            return _building.BuildingZone.bounds.Contains(point);
        }

        private void HandleMovingToItem(Character self, CharacterMovement movement)
        {
            if (_targetItem == null)
            {
                _isMovingToItem = false;
                return;
            }

            float dist = Vector3.Distance(self.transform.position, _targetItem.transform.position);
            if (dist > 1.5f)
            {
                movement.SetDestination(_targetItem.transform.position);
            }
            else
            {
                // Ramasser
                movement.ResetPath();
                _targetItem.IsBeingCarried = true;
                
                // On simule le transport en attachant visuellement ou juste en cachant l'objet
                // Pour l'instant, on va juste le désactiver et s'en souvenir
                _targetItem.gameObject.SetActive(false);
                
                _isMovingToItem = false;
                _isMovingToStorage = true;
                Debug.Log($"<color=cyan>[Logistics]</color> {self.CharacterName} a ramassé {_targetItem.ItemInstance.ItemSO.ItemName} pour le ranger.");
            }
        }

        private void HandleMovingToStorage(Character self, CharacterMovement movement)
        {
            if (_building.StorageZone == null)
            {
                DropItem(self, self.transform.position);
                _isFinished = true;
                return;
            }

            Vector3 storagePos = _building.StorageZone.Bounds.center;
            float dist = Vector3.Distance(self.transform.position, storagePos);

            if (dist > 2f)
            {
                movement.SetDestination(storagePos);
            }
            else
            {
                // Arrivé au stockage
                movement.ResetPath();
                DropItem(self, _building.StorageZone.GetRandomPointInZone());
                _isMovingToStorage = false;
                
                // On peut continuer à ranger d'autres objets
                _targetItem = null;
            }
        }

        private void DropItem(Character self, Vector3 pos)
        {
            if (_targetItem == null) return;

            _targetItem.gameObject.SetActive(true);
            _targetItem.transform.position = pos;
            _targetItem.IsBeingCarried = false;

            // Logique d'inventaire
            _building.AddToInventory(_targetItem.ItemInstance);
            
            Debug.Log($"<color=green>[Logistics]</color> {self.CharacterName} a rangé {_targetItem.ItemInstance.ItemSO.ItemName} dans la zone de stockage.");
            _targetItem = null;
        }

        public void Exit(Character self)
        {
            if (_isMovingToStorage && _targetItem != null)
            {
                // Si on arrête le comportement alors qu'on porte l'objet, on le lâche
                DropItem(self, self.transform.position);
            }
            self.CharacterMovement?.ResetPath();
        }

        public void Terminate() => _isFinished = true;
    }
}
