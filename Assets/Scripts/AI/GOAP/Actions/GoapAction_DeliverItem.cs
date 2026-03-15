using System.Collections.Generic;
using UnityEngine;

namespace MWI.AI
{
    public class GoapAction_DeliverItem : GoapAction
    {
        private JobTransporter _job;
        private bool _isActionStarted = false;
        private int _dropIndex = 0;
        protected bool _isComplete = false;

        public override string ActionName => "Deliver Item";
        public override float Cost => 1f;

        public override Dictionary<string, bool> Preconditions => new Dictionary<string, bool>
        {
            { "atDestination", true },
            { "itemCarried", true }
        };

        public override Dictionary<string, bool> Effects => new Dictionary<string, bool>
        {
            { "itemDelivered", true }
        };

        public override bool IsComplete => _isComplete;

        public GoapAction_DeliverItem(JobTransporter job)
        {
            _job = job;
        }

        public override bool IsValid(Character worker)
        {
            return _job != null && _job.CurrentOrder != null && _job.CurrentOrder.Destination != null;
        }

        public override void Execute(Character worker)
        {
            if (_job.CurrentOrder == null || _job.CarriedItems.Count == 0 || _dropIndex >= _job.CarriedItems.Count)
            {
                _isComplete = true; 
                return;
            }

            var currentItem = _job.CarriedItems[_dropIndex];

            // Security Check: Verify the worker STILL possesses the item logically.
            bool hasItem = worker.CharacterEquipment != null && worker.CharacterEquipment.HasItemSO(currentItem.ItemSO);

            if (!hasItem)
            {
                // Transporter arrived empty-handed for this specific item.
                Debug.Log($"<color=red>[DeliverItem]</color> {worker.CharacterName} n'a physiquement plus l'item {currentItem.ItemSO.ItemName}. Passage au suivant.");
                _dropIndex++;
                _isActionStarted = false;
                return;
            }

            // Drop Phase
            if (!_isActionStarted)
            {
                var dropAction = new CharacterDropItem(worker, currentItem);
                if (worker.CharacterActions.ExecuteAction(dropAction))
                {
                    _isActionStarted = true;
                }
                else
                {
                    Debug.Log($"<color=orange>[DeliverItem]</color> {worker.CharacterName} patiente pour executer CharacterDropItem...");
                }
            }
            else
            {
                // Wait for drop animation CharacterAction to resolve
                if (!(worker.CharacterActions.CurrentAction is CharacterDropItem))
                {
                    // Item dropped!
                    _job.NotifyDeliveryProgress(1);
                    _dropIndex++;
                    _isActionStarted = false;
                }
            }
        }

        public override void Exit(Character worker)
        {
            _isComplete = false;
            _isActionStarted = false;
            _dropIndex = 0;
        }
    }
}
