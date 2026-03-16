using System.Collections.Generic;
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
            if (_isActionStarted) return true;
            return _job != null && _job.CurrentOrder != null && _job.TargetWorldItem != null && _job.CurrentOrder.Source != null;
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
                _job.TargetWorldItem = null;
                _job.WaitCooldown = 1.0f;
                return null;
            }

            CommercialBuilding source = _job.CurrentOrder.Source;
            WorldItem exactTarget = _job.TargetWorldItem;
            
            // Retirer n'importe quelle instance du même type de l'inventaire logique de la source
            ItemInstance logicalItemFromShop = source.TakeFromInventory(exactTarget.ItemInstance.ItemSO);
            if (logicalItemFromShop == null)
            {
                Debug.LogWarning($"<color=orange>[PickupItem]</color> Fantôme détecté ! {_job.Worker.CharacterName} lost the race in logic. Applying cooldown.");
                _job.TargetWorldItem = null;
                _job.WaitCooldown = 1.0f;
                return null;
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
                if (source != null) source.AddToInventory(_takenItem);
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
