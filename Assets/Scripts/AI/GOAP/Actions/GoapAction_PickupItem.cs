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
            if (_job.CurrentOrder == null || _job.TargetWorldItem == null)
            {
                _isComplete = true; // Lost track
                return;
            }

            if (!_isActionStarted)
            {
                CommercialBuilding source = _job.CurrentOrder.Source;
                _takenItem = _job.TargetWorldItem.ItemInstance;
                
                // Retirer L'INSTANCE EXACTE de l'inventaire logique de la source
                bool success = source.RemoveExactItemFromInventory(_takenItem);
                if (!success)
                {
                    Debug.LogWarning($"<color=orange>[PickupItem]</color> Fantôme détecté ! L'item {_takenItem.ItemSO.ItemName} n'était plus dans l'inventaire de {source.BuildingName}.");
                    _job.CancelCurrentOrder();
                    _isComplete = true;
                    return;
                }

                var pickupAction = new CharacterPickUpItem(worker, _takenItem, _job.TargetWorldItem.gameObject);
                if (worker.CharacterActions.ExecuteAction(pickupAction))
                {
                    _isActionStarted = true;
                }
                else
                {
                    Debug.LogWarning($"<color=orange>[PickupItem]</color> {_job.Worker.CharacterName} a les mains pleines et pas de sac. Annulation.");
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
