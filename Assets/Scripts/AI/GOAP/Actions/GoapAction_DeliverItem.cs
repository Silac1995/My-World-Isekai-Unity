using System.Collections.Generic;
using UnityEngine;

namespace MWI.AI
{
    public class GoapAction_DeliverItem : GoapAction_ExecuteCharacterAction
    {
        private JobTransporter _job;
        private ItemInstance _droppingItem;

        public override string ActionName => "Deliver Item";

        public override Dictionary<string, bool> Preconditions => new Dictionary<string, bool>
        {
            { "atDestination", true },
            { "itemCarried", true }
        };

        public override Dictionary<string, bool> Effects => new Dictionary<string, bool>
        {
            { "itemDelivered", true }
        };

        public GoapAction_DeliverItem(JobTransporter job)
        {
            _job = job;
        }

        public override bool IsValid(Character worker)
        {
            return _job != null && _job.CurrentOrder != null && _job.CurrentOrder.Destination != null;
        }
        
        protected override CharacterAction PrepareAction(Character worker)
        {
            if (_job.CurrentOrder == null || _job.CarriedItems.Count == 0)
            {
                return null;
            }

            var currentItem = _job.CarriedItems[0];

            // Security Check: Verify the worker STILL possesses the item logically.
            bool hasItem = worker.CharacterEquipment != null && worker.CharacterEquipment.HasItemSO(currentItem.ItemSO);

            if (!hasItem)
            {
                // Transporter arrived empty-handed for this specific item.
                Debug.Log($"<color=red>[DeliverItem]</color> {worker.CharacterName} n'a physiquement plus l'item {currentItem.ItemSO.ItemName}. Passage au suivant.");
                _job.RemoveCarriedItem(currentItem);
                return null;
            }

            _droppingItem = currentItem;
            return new CharacterDropItem(worker, currentItem, true);
        }

        protected override void OnActionFinished()
        {
            if (_job == null || _droppingItem == null) return;
            
            _job.NotifyDeliveryProgress(1);
            _job.RemoveCarriedItem(_droppingItem);
            _droppingItem = null;
        }

        public override void Exit(Character worker)
        {
            base.Exit(worker);
            _droppingItem = null;
        }
    }
}
