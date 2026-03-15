using System.Collections.Generic;
using UnityEngine;

namespace MWI.AI
{
    public class GoapAction_LoadTransport : GoapAction
    {
        private JobTransporter _job;
        private bool _isMoving = false;
        private bool _isActionStarted = false;
        private Vector3 _lastTargetPos = Vector3.positiveInfinity;
        private float _lastRouteRequestTime;
        private ItemInstance _takenItem;
        private WorldItem _targetWorldItem;
        protected bool _isComplete = false;

        public override string ActionName => "Load Transport";
        public override float Cost => 1f;

        public override Dictionary<string, bool> Preconditions => new Dictionary<string, bool>
        {
            { "isLoaded", false }
        };

        public override Dictionary<string, bool> Effects => new Dictionary<string, bool>
        {
            { "isLoaded", true }
        };

        public override bool IsComplete => _isComplete;

        public GoapAction_LoadTransport(JobTransporter job)
        {
            _job = job;
        }

        public override bool IsValid(Character worker)
        {
            return _job != null && _job.CurrentOrder != null && _job.CurrentOrder.Source != null;
        }

        public override void Execute(Character worker)
        {
            if (_job.CurrentOrder == null)
            {
                _isComplete = true;
                return;
            }

            var movement = worker.CharacterMovement;
            if (movement == null) return;

            CommercialBuilding source = _job.CurrentOrder.Source;
            Zone zone = source.StorageZone ?? source.MainRoom.GetComponent<Zone>();
            ItemSO wantedSO = _job.CurrentOrder.ItemToTransport;
            
            // Phase 1: Trouver l'objet PHYSIQUE dans la zone de stockage
            if (_targetWorldItem == null && !_isActionStarted)
            {
                Collider searchZone = zone != null ? zone.GetComponent<Collider>() : source.GetComponent<Collider>();
                
                if (searchZone != null)
                {
                    Collider[] colliders = Physics.OverlapBox(searchZone.bounds.center, searchZone.bounds.extents, Quaternion.identity);
                    foreach (var col in colliders)
                    {
                        var wi = col.GetComponentInParent<WorldItem>();
                        if (wi != null && wi.ItemInstance != null && wi.ItemInstance.ItemSO == wantedSO)
                        {
                            // On s'assure que cet objet est bien DANS l'inventaire logique de la source
                            if (source.GetItemCount(wantedSO) > 0)
                            {
                                _targetWorldItem = wi;
                                break;
                            }
                        }
                    }
                }

                // Fallback de secours (si pas trouvé dans bounds strict)
                if (_targetWorldItem == null)
                {
                    WorldItem[] allItems = Object.FindObjectsByType<WorldItem>(FindObjectsSortMode.None);
                    foreach (var wi in allItems)
                    {
                        if (wi.ItemInstance != null && wi.ItemInstance.ItemSO == wantedSO && Vector3.Distance(wi.transform.position, source.transform.position) < 25f)
                        {
                            _targetWorldItem = wi;
                            break;
                        }
                    }
                }

                if (_targetWorldItem == null)
                {
                    Debug.LogWarning($"<color=orange>[LoadTransport]</color> Plus de {wantedSO.ItemName} physiquement disponible chez {source.BuildingName}. Annulation.");
                    _job.CancelCurrentOrder();
                    _isComplete = true;
                    return;
                }
            }

            // Phase 2: Mouvement physique vers l'objet
            if (!_isActionStarted && _targetWorldItem != null)
            {
                bool isCloseEnough = false;
                var workerCol = worker.GetComponent<Collider>();
                
                if (_targetWorldItem.ItemInteractable != null && _targetWorldItem.ItemInteractable.InteractionZone != null && workerCol != null)
                {
                    isCloseEnough = _targetWorldItem.ItemInteractable.InteractionZone.bounds.Intersects(workerCol.bounds);
                }
                else
                {
                    isCloseEnough = movement.RemainingDistance <= movement.StoppingDistance + 0.5f;
                }

                if (!isCloseEnough)
                {
                    bool hasPathFailed = (UnityEngine.Time.time - _lastRouteRequestTime > 0.2f) && (movement.PathStatus == UnityEngine.AI.NavMeshPathStatus.PathInvalid || (!movement.HasPath && !movement.PathPending));

                    if (!_isMoving || hasPathFailed)
                    {
                        Vector3 dest = _targetWorldItem.transform.position;
                        if (_targetWorldItem.ItemInteractable != null && _targetWorldItem.ItemInteractable.InteractionZone != null)
                        {
                            dest = _targetWorldItem.ItemInteractable.InteractionZone.bounds.ClosestPoint(worker.transform.position);
                        }
                        
                        movement.SetDestination(dest);
                        _lastTargetPos = dest;
                        _lastRouteRequestTime = UnityEngine.Time.time;
                        _isMoving = true;
                    }
                    return;
                }

                if (_isMoving)
                {
                    movement.Stop();
                    _isMoving = false;
                }

                // Phase 3: Pickup logic
                _takenItem = _targetWorldItem.ItemInstance;
                
                // On retire L'INSTANCE EXACTE de l'inventaire logique de la source
                bool success = source.RemoveExactItemFromInventory(_takenItem);
                if (!success)
                {
                    Debug.LogWarning($"<color=orange>[LoadTransport]</color> Fantôme détecté ! Le WorldItem {wantedSO.ItemName} n'était plus dans l'inventaire de {source.BuildingName}.");
                    _job.CancelCurrentOrder();
                    _isComplete = true;
                    return;
                }

                var pickupAction = new CharacterPickUpItem(worker, _takenItem, _targetWorldItem.gameObject);
                if (worker.CharacterActions.ExecuteAction(pickupAction))
                {
                    _isActionStarted = true;
                }
                else
                {
                    // Fallback force (NPC has no bag, put it in hands)
                    worker.CharacterVisual?.BodyPartsController?.HandsController?.CarryItem(_takenItem);
                    _job.SetCarriedItem(_takenItem);
                    Object.Destroy(_targetWorldItem.gameObject);
                    _isComplete = true; 
                }
            }
            else if (_isActionStarted)
            {
                // Wait for pickup animation to resolve
                if (!(worker.CharacterActions.CurrentAction is CharacterPickUpItem))
                {
                    _job.SetCarriedItem(_takenItem);
                    _isComplete = true;
                }
            }
        }

        public override void Exit(Character worker)
        {
            _isComplete = false;
            _isMoving = false;
            _isActionStarted = false;
            _lastTargetPos = Vector3.positiveInfinity;
            _takenItem = null;
            _targetWorldItem = null;
            worker.CharacterMovement?.Stop();
        }
    }
}
