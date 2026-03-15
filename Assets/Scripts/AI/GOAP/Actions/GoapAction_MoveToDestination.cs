using System.Collections.Generic;
using UnityEngine;

namespace MWI.AI
{
    public class GoapAction_MoveToDestination : GoapAction
    {
        private JobTransporter _job;
        private bool _isMoving = false;
        private float _lastRouteRequestTime;
        protected bool _isComplete = false;

        public override string ActionName => "Move To Destination";
        public override float Cost => 1f;

        public override Dictionary<string, bool> Preconditions => new Dictionary<string, bool>
        {
            { "itemCarried", true },
            { "atDestination", false }
        };

        public override Dictionary<string, bool> Effects => new Dictionary<string, bool>
        {
            { "atDestination", true }
        };

        public override bool IsComplete => _isComplete;

        public GoapAction_MoveToDestination(JobTransporter job)
        {
            _job = job;
        }

        public override bool IsValid(Character worker)
        {
            if (_job == null || _job.CurrentOrder == null || _job.CurrentOrder.Destination == null || _job.CarriedItems.Count == 0) 
                return false;

            int remainingNeeded = _job.CurrentOrder.Quantity - _job.CurrentOrder.DeliveredQuantity;
            bool hasEnough = _job.CarriedItems.Count >= remainingNeeded;
            
            bool canCarryMore = false;
            if (worker.CharacterEquipment != null)
            {
                if (worker.CharacterEquipment.HasFreeSpaceForItemSO(_job.CurrentOrder.ItemToTransport))
                {
                    canCarryMore = true;
                }
                else
                {
                    var hands = worker.CharacterVisual?.BodyPartsController?.HandsController;
                    if (hands != null && hands.AreHandsFree()) canCarryMore = true;
                }
            }

            // Force a replan (loop back to LocateItem) if we don't have enough and can carry more
            if (!hasEnough && canCarryMore)
            {
                Debug.Log($"<color=cyan>[MoveToDestination]</color> Wait! {worker.CharacterName} can still carry {(_job.CurrentOrder.ItemToTransport.ItemName)}. Returning to storage for more!");
                return false; 
            }

            return true;
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

            CommercialBuilding destination = _job.CurrentOrder.Destination;
            Zone zone = destination.DeliveryZone ?? destination.MainRoom.GetComponent<Zone>();

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
                    _lastRouteRequestTime = UnityEngine.Time.time;
                    _isMoving = true;
                }
            }
            else
            {
                if (_isMoving)
                {
                    movement.Stop();
                    _isMoving = false;
                }
                _isComplete = true; // Arrived
            }
        }

        public override void Exit(Character worker)
        {
            _isComplete = false;
            _isMoving = false;
            worker.CharacterMovement?.Stop();
        }
    }
}
