using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace MWI.AI
{
    public class GoapAction_MoveToItem : GoapAction_MoveToTarget
    {
        private JobTransporter _job;

        public override string ActionName => "Move To Item";

        public override Dictionary<string, bool> Preconditions => new Dictionary<string, bool>
        {
            { "itemLocated", true },
            { "atItem", false }
        };

        public override Dictionary<string, bool> Effects => new Dictionary<string, bool>
        {
            { "atItem", true }
        };

        public GoapAction_MoveToItem(JobTransporter job)
        {
            _job = job;
        }

        public override bool IsValid(Character worker)
        {
            // Furniture-first mutual exclusion: when LocateItem committed to the slot
            // pickup path, GoapAction_TakeFromSourceFurniture handles arrival + extraction
            // in a single action and this leg becomes redundant. Returning false here lets
            // the planner skip MoveToItem in favour of the cheaper furniture path.
            if (_job != null && _job.TargetSourceFurniture != null) return false;

            return _job != null && _job.CurrentOrder != null && _job.TargetWorldItem != null;
        }

        protected override Collider GetTargetCollider(Character worker)
        {
            if (_job == null || _job.TargetWorldItem == null) return null;

            // Phase-A: PickupZone takes precedence when authored. The base MoveToTarget uses
            // this collider for "am I close enough / inside?" arrival checks, so returning the
            // zone collider here means the transporter counts as arrived once they step into
            // the staging zone — never inside StorageZone.
            if (_job.CurrentOrder != null && _job.CurrentOrder.Source != null)
            {
                var pickupZone = _job.CurrentOrder.Source.PickupZone;
                if (pickupZone != null)
                {
                    return pickupZone.GetComponent<Collider>();
                }
            }

            var interactable = _job.TargetWorldItem.ItemInteractable;
            return interactable != null ? interactable.InteractionZone : _job.TargetWorldItem.GetComponentInChildren<Collider>();
        }

        protected override bool ShouldGoInsideZone()
        {
            // When we're routing to a PickupZone the worker needs to be *inside* the zone
            // to trigger the arrival check + the subsequent PickupItem IsValid gate.
            if (_job != null && _job.CurrentOrder != null && _job.CurrentOrder.Source != null
                && _job.CurrentOrder.Source.PickupZone != null)
            {
                return true;
            }
            return base.ShouldGoInsideZone();
        }

        protected override Vector3 GetDestinationPoint(Character worker)
        {
            if (_job == null || _job.TargetWorldItem == null) return worker.transform.position;

            // Phase-A: prefer a PickupZone when the source authored one — it's guaranteed
            // reachable by design, unlike a raw WorldItem position that can sit inside a
            // StorageZone the transporter's NavMesh can't enter. Falls through to the raw
            // item position for buildings that haven't opted in (backward compatibility).
            if (_job.CurrentOrder != null && _job.CurrentOrder.Source != null)
            {
                var pickupZone = _job.CurrentOrder.Source.PickupZone;
                if (pickupZone != null)
                {
                    return pickupZone.GetRandomPointInZone();
                }
            }

            return _job.TargetWorldItem.transform.position;
        }

        /// <summary>
        /// Phase-B rollback: a transporter walking toward a target item with no valid
        /// NavMesh route rolls back the whole TransportOrder. <c>CancelCurrentOrder(true)</c>
        /// removes the TransportOrder from the source's active queue and the BuyOrder's
        /// DispatchedQuantity is refunded via <c>ReportCancelledTransporter</c> so the
        /// supplier can recompute (or park the BuyOrder once its PathUnreachableCount tops out).
        /// </summary>
        protected override void OnPathUnreachable(Character worker, Vector3 attemptedDestination, NavMeshPathStatus status)
        {
            if (_job == null) return;

            var order = _job.CurrentOrder;
            if (order != null)
            {
                Debug.LogError($"<color=red>[MoveToItem]</color> {worker?.CharacterName}: unreachable pickup — source='{order.Source?.BuildingName}', item='{order.ItemToTransport?.ItemName}', dest='{attemptedDestination}', status={status}. Cancelling TransportOrder and reporting to supplier.");

                // Bump the BuyOrder's reachability counter so the dispatcher eventually parks it.
                var buyOrder = order.AssociatedBuyOrder;
                if (buyOrder != null)
                {
                    bool stalled = buyOrder.RecordPathUnreachable();
                    if (stalled)
                    {
                        Debug.LogError($"<color=red>[MoveToItem]</color> BuyOrder for {buyOrder.Quantity}x {buyOrder.ItemToTransport?.ItemName} → {buyOrder.Destination?.BuildingName} reached MaxPathUnreachableAttempts ({BuyOrder.MaxPathUnreachableAttempts}). Parked until expiration.");
                    }
                }

                // Report missing reservation → source clears ReservedItems + refunds dispatch + removes from PlacedTransportOrders.
                var supplierLogistics = order.Source != null ? order.Source.LogisticsManager : null;
                if (supplierLogistics != null)
                {
                    try { supplierLogistics.ReportMissingReservedItem(order); }
                    catch (System.Exception e)
                    {
                        Debug.LogException(e);
                        Debug.LogError($"[MoveToItem] ReportMissingReservedItem threw during OnPathUnreachable rollback. Continuing to CancelCurrentOrder.");
                    }
                }
            }

            // Tear down the transporter's local plan. true => also drop from the transporter's own active queue.
            _job.TargetWorldItem = null;
            _job.WaitCooldown = 1.5f;
            _job.CancelCurrentOrder(true);
        }
    }
}
