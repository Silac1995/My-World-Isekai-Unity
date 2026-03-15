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
            
            // Movement phase
            if (!_isActionStarted)
            {
                bool isCloseEnough = false;

                if (zone != null)
                {
                    if (zone.GetComponent<Collider>().bounds.Contains(worker.transform.position))
                    {
                        isCloseEnough = true;
                    }
                }
                else
                {
                    if (Vector3.Distance(worker.transform.position, source.transform.position) < 3f)
                    {
                        isCloseEnough = true;
                    }
                }

                if (!isCloseEnough)
                {
                    bool hasPathFailed = (UnityEngine.Time.time - _lastRouteRequestTime > 0.2f) && (movement.PathStatus == UnityEngine.AI.NavMeshPathStatus.PathInvalid || (!movement.HasPath && !movement.PathPending));

                    if (!_isMoving || hasPathFailed)
                    {
                        Vector3 dest = zone != null ? zone.GetRandomPointInZone() : source.transform.position;
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

                // Pickup Phase
                _takenItem = source.TakeFromInventory(_job.CurrentOrder.ItemToTransport);
                if (_takenItem == null)
                {
                    Debug.LogWarning($"<color=orange>[LoadTransport]</color> Plus de {_job.CurrentOrder.ItemToTransport.ItemName} disponible chez {source.BuildingName}. Annulation.");
                    _job.CancelCurrentOrder();
                    _isComplete = true;
                    return;
                }

                var pickupAction = new CharacterPickUpItem(worker, _takenItem, null);
                if (worker.CharacterActions.ExecuteAction(pickupAction))
                {
                    _isActionStarted = true;
                }
                else
                {
                    // Fallback force
                    worker.CharacterVisual?.BodyPartsController?.HandsController?.CarryItem(_takenItem);
                    _isComplete = true; 
                }
            }
            else
            {
                // Wait for pickup animation
                if (!(worker.CharacterActions.CurrentAction is CharacterPickUpItem))
                {
                    _job.SetCarriedItem(_takenItem); // Update transporter state
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
            worker.CharacterMovement?.Stop();
        }
    }
}
