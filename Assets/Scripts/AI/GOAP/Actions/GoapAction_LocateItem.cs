using System.Collections.Generic;
using UnityEngine;

namespace MWI.AI
{
    public class GoapAction_LocateItem : GoapAction
    {
        private JobTransporter _job;
        protected bool _isComplete = false;

        public override string ActionName => "Locate Delivery Item";
        public override float Cost => 1f;

        public override Dictionary<string, bool> Preconditions => new Dictionary<string, bool>
        {
            { "atSourceStorage", true },
            { "itemLocated", false }
        };

        public override Dictionary<string, bool> Effects => new Dictionary<string, bool>
        {
            { "itemLocated", true }
        };

        public override bool IsComplete => _isComplete;

        public GoapAction_LocateItem(JobTransporter job)
        {
            _job = job;
        }

        public override bool IsValid(Character worker)
        {
            return _job != null && _job.CurrentOrder != null && _job.CurrentOrder.Source != null;
        }

        public override void Execute(Character worker)
        {
            if (_job.CurrentOrder == null || _job.CurrentOrder.Source == null)
            {
                _isComplete = true;
                return;
            }

            CommercialBuilding source = _job.CurrentOrder.Source;
            ItemSO wantedSO = _job.CurrentOrder.ItemToTransport;
            WorldItem targetWorldItem = null;

            // 1. Get awareness component
            CharacterAwareness awareness = worker.CharacterAwareness;
            if (awareness != null)
            {
                // 3. Scan visible items (ItemInteractable inherits from InteractableObject)
                List<ItemInteractable> visibleInteractables = awareness.GetVisibleInteractables<ItemInteractable>();
                
                foreach (var itemInteractable in visibleInteractables)
                {
                    WorldItem wi = itemInteractable != null ? itemInteractable.WorldItem : null;
                    if (wi != null && wi.ItemInstance != null && wi.ItemInstance.ItemSO == wantedSO && !wi.IsBeingCarried)
                    {
                        if (worker.PathingMemory.IsBlacklisted(wi.gameObject.GetInstanceID())) continue;

                        // Logical verification: Ensure it's inside the source's inventory
                        if (source.GetItemCount(wantedSO) > 0)
                        {
                            targetWorldItem = wi;
                            break;
                        }
                    }
                }
            }
            else
            {
                Debug.LogWarning($"<color=orange>[LocateItem]</color> {_job.Worker.CharacterName} n'a pas de CharacterAwareness ! Impossible de chercher l'objet.");
            }

            // 4. Fallback Handling
            if (targetWorldItem == null)
            {
                if (_job.CarriedItems.Count > 0)
                {
                    Debug.LogWarning($"<color=orange>[LocateItem]</color> Plus de {wantedSO.ItemName} physiquement visible. {_job.Worker.CharacterName} lance la livraison ({_job.CarriedItems.Count} items).");
                    _job.ForceDeliverPartialBatch = true;
                    _isComplete = true;
                    return;
                }
                else
                {
                    // Verify if items are logically there but not visible (maybe crafter hasn't dropped them yet)
                    if (source.GetItemCount(wantedSO) > 0)
                    {
                        Debug.Log($"<color=cyan>[LocateItem]</color> Les items sont dans l'inventaire mais pas encore par terre. {_job.Worker.CharacterName} patiente.");
                        _job.WaitCooldown = 1f; // Check again in a bit
                        _isComplete = true;
                        return;
                    }
                    else
                    {
                        Debug.LogWarning($"<color=orange>[LocateItem]</color> Plus de {wantedSO.ItemName} du tout chez {source.BuildingName}. Annulation.");
                        _job.WaitCooldown = 2f;
                        _job.CancelCurrentOrder();
                        _isComplete = true;
                        return;
                    }
                }
            }

            _job.TargetWorldItem = targetWorldItem;
            _isComplete = true;
        }

        public override void Exit(Character worker)
        {
            _isComplete = false;
        }
    }
}
