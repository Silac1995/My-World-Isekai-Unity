using System.Collections.Generic;
using UnityEngine;

namespace MWI.AI
{
    public class GoapAction_UnloadTransport : GoapAction
    {
        private JobTransporter _job;
        private bool _isMoving = false;
        private bool _isActionStarted = false;
        private Vector3 _lastTargetPos = Vector3.positiveInfinity;
        private float _lastRouteRequestTime;
        protected bool _isComplete = false;

        public override string ActionName => "Unload Transport";
        public override float Cost => 1f;

        public override Dictionary<string, bool> Preconditions => new Dictionary<string, bool>
        {
            { "isLoaded", true }
        };

        public override Dictionary<string, bool> Effects => new Dictionary<string, bool>
        {
            { "hasDelivered", true }
        };

        public override bool IsComplete => _isComplete;

        public GoapAction_UnloadTransport(JobTransporter job)
        {
            _job = job;
        }

        public override bool IsValid(Character worker)
        {
            return _job != null && _job.CurrentOrder != null && _job.CurrentOrder.Destination != null;
        }

        public override void Execute(Character worker)
        {
            if (_job.CurrentOrder == null)
            {
                _isComplete = true; // Lost order mid-transit
                return;
            }

            var movement = worker.CharacterMovement;
            if (movement == null) return;

            CommercialBuilding destination = _job.CurrentOrder.Destination;
            Zone zone = destination.DeliveryZone ?? destination.MainRoom.GetComponent<Zone>();
            
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
                    if (Vector3.Distance(worker.transform.position, destination.transform.position) < 3f)
                    {
                        isCloseEnough = true;
                    }
                }

                if (!isCloseEnough)
                {
                    bool hasPathFailed = (UnityEngine.Time.time - _lastRouteRequestTime > 0.2f) && (movement.PathStatus == UnityEngine.AI.NavMeshPathStatus.PathInvalid || (!movement.HasPath && !movement.PathPending));

                    if (!_isMoving || hasPathFailed)
                    {
                        Vector3 dest = zone != null ? zone.GetRandomPointInZone() : destination.transform.position;
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

                // Drop Phase
                var dropAction = new CharacterDropItem(worker, _job.CarriedItem);
                if (worker.CharacterActions.ExecuteAction(dropAction))
                {
                    _isActionStarted = true;
                    dropAction.OnActionFinished += FinishDropoff;
                }
                else
                {
                    // Fallback clear
                    var inventory = worker.CharacterEquipment?.GetInventory();
                    if (inventory != null && inventory.HasAnyItemSO(new List<ItemSO> { _job.CarriedItem.ItemSO }))
                        inventory.RemoveItem(_job.CarriedItem, worker);
                    else
                        worker.CharacterVisual?.BodyPartsController?.HandsController?.DropCarriedItem();
                    
                    FinishDropoff();
                }
            }
        }

        private void FinishDropoff()
        {
            _job.NotifyDeliveryProgress(1); // Clears the carried item internally
            _isComplete = true;
        }

        public override void Exit(Character worker)
        {
            _isComplete = false;
            _isMoving = false;
            _isActionStarted = false;
            _lastTargetPos = Vector3.positiveInfinity;
            worker.CharacterMovement?.Stop();
        }
    }
}
