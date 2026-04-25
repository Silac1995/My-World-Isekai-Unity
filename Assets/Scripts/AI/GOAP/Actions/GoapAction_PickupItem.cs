using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MWI.AI
{
    public class GoapAction_PickupItem : GoapAction_ExecuteCharacterAction
    {
        private JobTransporter _job;
        private ItemInstance _takenItem;

        public override string ActionName => "Pickup Transport Item";

        public override Dictionary<string, bool> Preconditions => new Dictionary<string, bool>
        {
            { "atItem", true },
            { "itemCarried", false }
        };

        public override Dictionary<string, bool> Effects => new Dictionary<string, bool>
        {
            { "itemCarried", true }
        };

        public GoapAction_PickupItem(JobTransporter job)
        {
            _job = job;
        }

        public override bool IsValid(Character worker)
        {
            // Furniture-first mutual exclusion: when LocateItem committed to the slot
            // pickup path, GoapAction_TakeFromSourceFurniture owns the cycle. This action
            // must refuse to run so the planner falls back to the furniture path. Placed
            // before the _isActionStarted ride-out because once we've started, the loose
            // path is already locked in (TargetSourceFurniture would already be null).
            if (_job != null && _job.TargetSourceFurniture != null) return false;

            if (_isActionStarted) return true;
            if (_job == null || _job.CurrentOrder == null || _job.TargetWorldItem == null || _job.CurrentOrder.Source == null)
                return false;

            // Phase-A: if the source authored a PickupZone, the TargetWorldItem MUST be inside
            // that staging zone before we commit to pickup. Items still sitting in StorageZone
            // are the source's responsibility to stage (via GoapAction_StageItemForPickup).
            // Returning false here makes the transporter idle / replan one frame while the
            // source's logistics manager moves items outward — no busy-loop, no false cancel.
            var pickupZone = _job.CurrentOrder.Source.PickupZone;
            if (pickupZone != null)
            {
                var zoneCol = pickupZone.GetComponent<Collider>();
                if (zoneCol != null)
                {
                    Vector3 itemPos = _job.TargetWorldItem.transform.position;
                    Vector3 flatItemPos = new Vector3(itemPos.x, zoneCol.bounds.center.y, itemPos.z);
                    if (!zoneCol.bounds.Contains(flatItemPos))
                    {
                        return false;
                    }
                }
            }

            return true;
        }
        
        protected override CharacterAction PrepareAction(Character worker)
        {
            if (_job.CurrentOrder == null)
            {
                return null;
            }

            if (_job.TargetWorldItem == null || _job.TargetWorldItem.IsBeingCarried)
            {
                // Lost the race! Someone else destroyed/picked up the physical item before we started grabbing it
                Debug.Log($"<color=orange>[PickupItem]</color> {_job.Worker.CharacterName} lost the race to pick up the item. Cooldown applied.");
                
                var logisticsManager = _job.CurrentOrder.Source.LogisticsManager;
                if (logisticsManager != null) 
                { 
                    logisticsManager.ReportMissingReservedItem(_job.CurrentOrder); 
                } 
                
                _job.CancelCurrentOrder(true);
                return null;
            }

            CommercialBuilding source = _job.CurrentOrder.Source;
            WorldItem exactTarget = _job.TargetWorldItem;

            // --- FIX: On retire L'INSTANCE EXACTE de l'inventaire logistique du Shop, pas n'importe laquelle ---
            ItemInstance logicalItemFromShop = exactTarget.ItemInstance;
            bool success = source.RemoveExactItemFromInventory(logicalItemFromShop);

            if (!success)
            {
                // Self-heal: the WorldItem we're standing in front of carries the exact
                // ItemInstance reserved by our TransportOrder, but the source's logical
                // _inventory lost it (almost always a RefreshStorageInventory ghost-pass
                // that raced a settling non-kinematic WorldItem). The reservation is still
                // authoritative — proceed with the pickup instead of aborting.
                if (_job.CurrentOrder.ReservedItems.Contains(logicalItemFromShop))
                {
                    Debug.LogWarning($"<color=orange>[PickupItem]</color> {_job.Worker.CharacterName}: logical inventory out of sync for {logicalItemFromShop.ItemSO.ItemName} but reservation + physical item are intact → proceeding (self-heal).");
                }
                else
                {
                    Debug.LogWarning($"<color=orange>[PickupItem]</color> Instance reservee introuvable dans l'inventaire logique ET dans la réservation ! {_job.Worker.CharacterName} lost the race in logic. Applying cooldown.");
                    var logisticsManager = source.LogisticsManager;
                    if (logisticsManager != null)
                    {
                        logisticsManager.ReportMissingReservedItem(_job.CurrentOrder);
                    }

                    _job.CancelCurrentOrder(true);
                    return null;
                }
            }

            _takenItem = logicalItemFromShop;
            return new CharacterPickUpItem(worker, _takenItem, exactTarget.gameObject);
        }

        protected override void OnActionFailed(Character worker)
        {
            if (_job == null || _takenItem == null) return;
            
            Debug.LogWarning($"<color=orange>[PickupItem]</color> {_job.Worker.CharacterName} Action Failed (out of range/hands full). Canceling.");
            CommercialBuilding source = _job.CurrentOrder.Source;
            if (source != null) source.AddToInventory(_takenItem); // Refund logic inventory
            
            _job.TargetWorldItem = null;
            _job.WaitCooldown = 1.0f;
            _takenItem = null;
        }

        protected override void OnActionFinished()
        {
            if (_job == null || _takenItem == null) return;
            
            // Strict verification: Does the worker ACTUALLY have it?
            bool actuallyHasItem = _job.Worker.CharacterEquipment != null && _job.Worker.CharacterEquipment.HasItemSO(_takenItem.ItemSO);

            if (actuallyHasItem)
            {
                _job.AddCarriedItem(_takenItem);
            }
            else
            {
                Debug.LogWarning($"<color=orange>[PickupItem]</color> {_job.Worker.CharacterName} n'a pas pu physiquement ramasser l'item. Essai suivant.");
                CommercialBuilding source = _job.CurrentOrder.Source;
                if (source != null) source.AddToInventory(_takenItem); // Refund if pickup physically failed
                _job.TargetWorldItem = null;
                _job.WaitCooldown = 1.0f;
            }
            _takenItem = null;
        }

        public override void Exit(Character worker)
        {
            base.Exit(worker);
            _takenItem = null;
        }
    }
}
