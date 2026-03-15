using System.Collections.Generic;
using UnityEngine;

namespace MWI.AI
{
    public class GoapAction_PickupItem : GoapAction
    {
        private JobTransporter _job;
        private bool _isActionStarted = false;
        private ItemInstance _takenItem;
        protected bool _isComplete = false;

        public override string ActionName => "Pickup Transport Item";
        public override float Cost => 1f;

        public override Dictionary<string, bool> Preconditions => new Dictionary<string, bool>
        {
            { "atItem", true },
            { "itemCarried", false }
        };

        public override Dictionary<string, bool> Effects => new Dictionary<string, bool>
        {
            { "itemCarried", true }
        };

        public override bool IsComplete => _isComplete;

        public GoapAction_PickupItem(JobTransporter job)
        {
            _job = job;
        }

        public override bool IsValid(Character worker)
        {
            return _job != null && _job.CurrentOrder != null && _job.TargetWorldItem != null && _job.CurrentOrder.Source != null;
        }

        public override void Execute(Character worker)
        {
            if (_job.CurrentOrder == null)
            {
                _isComplete = true; // Lost track
                return;
            }

            if (_job.TargetWorldItem == null || _job.TargetWorldItem.IsBeingCarried)
            {
                // Lost the race! Someone else destroyed/picked up the physical item
                Debug.Log($"<color=orange>[PickupItem]</color> {_job.Worker.CharacterName} lost the race to pick up the item. Cooldown applied.");
                _job.TargetWorldItem = null;
                _job.WaitCooldown = 1.0f;
                _isComplete = true;
                return;
            }

            if (!_isActionStarted)
            {
                CommercialBuilding source = _job.CurrentOrder.Source;
                WorldItem exactTarget = _job.TargetWorldItem;
                
                // Retirer n'importe quelle instance du même type de l'inventaire logique de la source
                ItemInstance logicalItemFromShop = source.TakeFromInventory(exactTarget.ItemInstance.ItemSO);
                if (logicalItemFromShop == null)
                {
                    Debug.LogWarning($"<color=orange>[PickupItem]</color> Fantôme détecté ! {_job.Worker.CharacterName} lost the race in logic. Applying cooldown.");
                    _job.TargetWorldItem = null;
                    _job.WaitCooldown = 1.0f;
                    _isComplete = true;
                    return;
                }

                _takenItem = logicalItemFromShop;

                var pickupAction = new CharacterPickUpItem(worker, _takenItem, exactTarget.gameObject);
                if (worker.CharacterActions.ExecuteAction(pickupAction))
                {
                    _isActionStarted = true;
                }
                else
                {
                    Debug.LogWarning($"<color=orange>[PickupItem]</color> {_job.Worker.CharacterName} n'a pas pu executer l'action (hors de portee ou mains pleines). Annulation.");
                    source.AddToInventory(_takenItem); // Remettre dans l'inventaire logique !
                    _job.WaitCooldown = 2f;
                    _job.CancelCurrentOrder();
                    _isComplete = true; 
                }
            }
            else
            {
                // Wait for pickup animation CharacterAction to resolve
                if (!(worker.CharacterActions.CurrentAction is CharacterPickUpItem))
                {
                    // Strict verification: Does the worker ACTUALLY have it?
                    bool actuallyHasItem = worker.CharacterEquipment != null && worker.CharacterEquipment.HasItemSO(_takenItem.ItemSO);

                    if (actuallyHasItem)
                    {
                        _job.AddCarriedItem(_takenItem);
                    }
                    else
                    {
                        Debug.LogWarning($"<color=orange>[PickupItem]</color> {_job.Worker.CharacterName} n'a pas pu physiquement ramasser l'item.");
                        CommercialBuilding source = _job.CurrentOrder.Source;
                        if (source != null) source.AddToInventory(_takenItem);
                        _job.WaitCooldown = 2f;
                        _job.CancelCurrentOrder();
                    }
                    
                    _isComplete = true;
                }
            }
        }

        public override void Exit(Character worker)
        {
            _isComplete = false;
            _isActionStarted = false;
            _takenItem = null;
        }
    }
}
