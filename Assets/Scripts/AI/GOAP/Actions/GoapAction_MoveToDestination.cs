using System.Collections.Generic;
using UnityEngine;

namespace MWI.AI
{
    public class GoapAction_MoveToDestination : GoapAction_MoveToTarget
    {
        private JobTransporter _job;

        public override string ActionName => "Move To Destination";

        public override Dictionary<string, bool> Preconditions => new Dictionary<string, bool>
        {
            { "itemCarried", true },
            { "atDestination", false }
        };

        public override Dictionary<string, bool> Effects => new Dictionary<string, bool>
        {
            { "atDestination", true }
        };

        public GoapAction_MoveToDestination(JobTransporter job)
        {
            _job = job;
        }

        public override bool IsValid(Character worker)
        {
            if (_job == null || _job.CurrentOrder == null || _job.CurrentOrder.Destination == null || _job.CarriedItems.Count == 0) 
            {
                return false;
            }

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

            // Force a replan (loop back to LocateItem) ONLY if we don't have enough AND we can carry more
            // If canCarryMore is false (e.g. inventory full OR no bag and hands are full), we MUST proceed to deliver what we have!
            if (!hasEnough && canCarryMore && !_job.ForceDeliverPartialBatch)
            {
                return false; 
            }

            return true;
        }

        protected override Collider GetTargetCollider(Character worker)
        {
            if (_job == null || _job.CurrentOrder == null || _job.CurrentOrder.Destination == null) return null;
            
            CommercialBuilding destination = _job.CurrentOrder.Destination;
            Zone zone = destination.DeliveryZone ?? destination.MainRoom.GetComponent<Zone>();
            return zone != null ? zone.GetComponent<Collider>() : null;
        }

        protected override Vector3 GetDestinationPoint(Character worker)
        {
            if (_job == null || _job.CurrentOrder == null || _job.CurrentOrder.Destination == null) return worker.transform.position;
            
            CommercialBuilding destination = _job.CurrentOrder.Destination;
            Zone zone = destination.DeliveryZone ?? destination.MainRoom.GetComponent<Zone>();
            return zone != null ? zone.GetRandomPointInZone() : destination.transform.position;
        }
    }
}
