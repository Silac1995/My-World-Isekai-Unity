using System.Collections.Generic;
using System.Linq;
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
            _isComplete = false;
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

            // Reset the furniture-source path fields so a stale value from a previous
            // order (or a previous LocateItem run) cannot leak across into this resolution.
            // Will be re-set below if the furniture-first scan finds a match.
            _job.TargetSourceFurniture = null;
            _job.TargetItemFromFurniture = null;

            // 0. Furniture-first scan — runs before CharacterAwareness. If a reserved
            // ItemInstance lives in a StorageFurniture slot, we route through
            // GoapAction_TakeFromSourceFurniture (cost 0.5) instead of the loose-pickup
            // chain (MoveToItem 1 + PickupItem 1). This eliminates the wait that used to
            // happen when the source's JobLogisticsManager had to stage the item out of
            // the slot via GoapAction_StageItemForPickup before a transporter could grab it.
            foreach (var (furniture, item) in source.GetItemsInStorageFurniture())
            {
                if (furniture == null || item == null) continue;
                if (furniture.IsLocked) continue;
                if (worker.PathingMemory.IsBlacklisted(furniture.gameObject.GetInstanceID())) continue;
                if (!_job.CurrentOrder.ReservedItems.Contains(item)) continue;

                _job.TargetSourceFurniture = furniture;
                _job.TargetItemFromFurniture = item;
                _job.TargetWorldItem = null;

                if (NPCDebug.VerboseActions)
                    Debug.Log($"<color=magenta>[LocateItem]</color> {_job.Worker.CharacterName} found reserved item in {furniture.FurnitureName} (slot pickup path).");

                _isComplete = true;
                return;
            }

            // 1. Get awareness component
            CharacterAwareness awareness = worker.CharacterAwareness;
            if (awareness != null)
            {
                // 3. Scan visible items (ItemInteractable inherits from InteractableObject)
                List<ItemInteractable> visibleInteractables = awareness.GetVisibleInteractables<ItemInteractable>();
                List<WorldItem> validItems = new List<WorldItem>();
                
                foreach (var itemInteractable in visibleInteractables)
                {
                    WorldItem wi = itemInteractable != null ? itemInteractable.WorldItem : null;
                    
                    // --- NOUVEAU: Le livreur ne cible plus N'IMPORTE QUEL ITEM, mais EXCLUSIVEMENT ceux qui lui sont réservés ---
                    if (wi != null && wi.ItemInstance != null && _job.CurrentOrder.ReservedItems.Contains(wi.ItemInstance) && !wi.IsBeingCarried)
                    {
                        if (worker.PathingMemory.IsBlacklisted(wi.gameObject.GetInstanceID()))
                        {
                            // Per-tick reachable, scales with blacklist size which only grows. Gated to avoid
                            // the Windows console-buffer progressive-freeze documented in
                            // wiki/gotchas/host-progressive-freeze-debug-log-spam.md.
                            if (NPCDebug.VerboseActions)
                                Debug.Log($"<color=cyan>[LocateItem]</color> {_job.Worker.CharacterName} ignore l'item blacklisté: {wi.name}.");
                            continue;
                        }

                        // Logical verification: Ensure it's inside the source's inventory
                        if (source.GetItemCount(wantedSO) > 0)
                        {
                            validItems.Add(wi);
                        }
                    }
                }
                
                if (validItems.Count > 0)
                {
                    targetWorldItem = validItems[Random.Range(0, validItems.Count)];
                    if (NPCDebug.VerboseActions)
                        Debug.Log($"<color=magenta>[LocateItem]</color> {_job.Worker.CharacterName} a trouvé {validItems.Count} items valides. Choisi: {targetWorldItem.name}");
                }
                else if (NPCDebug.VerboseActions)
                {
                    Debug.Log($"<color=magenta>[LocateItem]</color> {_job.Worker.CharacterName} a trouvé ZERO item valide parmi {visibleInteractables.Count} interactables visibles.");
                }
            }
            else if (NPCDebug.VerboseActions)
            {
                // Without the gate this LogWarning fires every tick of the Execute loop when the worker
                // is missing the CharacterAwareness component — a persistent misconfiguration state that
                // would otherwise spam the console at the job-tick rate.
                Debug.LogWarning($"<color=orange>[LocateItem]</color> {_job.Worker.CharacterName} n'a pas de CharacterAwareness ! Impossible de chercher l'objet.");
            }

            // 4. Fallback Handling
            if (targetWorldItem == null)
            {
                // NOUVEAU: Si l'item n'est pas vu par CharacterAwareness (trop loin etc.), on scanne directement
                // la StorageZone complète du bâtiment. Cela évite d'annuler faussement la commande 
                // alors que l'item est parfaitement valide mais à 12 mètres de distance.
                List<WorldItem> storageItems = source.GetWorldItemsInStorage();
                foreach (WorldItem wi in storageItems)
                {
                    if (wi != null && wi.ItemInstance != null && _job.CurrentOrder.ReservedItems.Contains(wi.ItemInstance) && !wi.IsBeingCarried)
                    {
                        if (!worker.PathingMemory.IsBlacklisted(wi.gameObject.GetInstanceID()))
                        {
                            targetWorldItem = wi;
                            if (NPCDebug.VerboseActions)
                                Debug.Log($"<color=magenta>[LocateItem]</color> {_job.Worker.CharacterName} a trouvé l'item hors de portée visuelle via scan de zone: {targetWorldItem.name}");
                            break;
                        }
                    }
                }
            }

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
                    // NEW CHECK: Are the remaining items already picked up by another transporter?
                    // Because multiple transporters can be assigned the same order.
                    int accountedFor = _job.CurrentOrder.DeliveredQuantity + _job.CurrentOrder.InTransitQuantity;
                    if (accountedFor >= _job.CurrentOrder.Quantity)
                    {
                        Debug.LogWarning($"<color=cyan>[LocateItem]</color> {_job.Worker.CharacterName} remarque que tous les items sont déjà en transit ou livrés. Abandon local de la commande.");
                        _job.WaitCooldown = 1f;
                        _job.CancelCurrentOrder(false); // DO NOT report missing items globally!
                        _isComplete = true;
                        return;
                    }

                    // Verify if items are logically there but not visible (maybe crafter hasn't dropped them yet)
                    // NOUVEAU: On vérifie *spécifiquement* si NOS items réservés sont encore dans l'inventaire physique du bâtiment.
                    bool itemsStillInInventory = false;
                    foreach (var reservedItem in _job.CurrentOrder.ReservedItems)
                    {
                        if (source.Inventory.Contains(reservedItem))
                        {
                            itemsStillInInventory = true;
                            break;
                        }
                    }

                    // Furniture-first audit extension: if a reserved item is currently
                    // sitting in a StorageFurniture slot, the building is healthy — the
                    // furniture-first scan at the top of Execute should have caught it
                    // unless the furniture is locked / blacklisted (transient). In that
                    // case do NOT trigger RefreshStorageInventory (which would purge
                    // logical ghosts and cancel the order); instead apply a short cooldown
                    // and let the next replan re-run the furniture scan.
                    bool itemsStillInFurniture = false;
                    foreach (var (furn, slotItem) in source.GetItemsInStorageFurniture())
                    {
                        if (slotItem != null && _job.CurrentOrder.ReservedItems.Contains(slotItem))
                        {
                            itemsStillInFurniture = true;
                            break;
                        }
                    }

                    if (itemsStillInFurniture)
                    {
                        Debug.LogWarning($"<color=orange>[LocateItem]</color> Reserved items found in StorageFurniture slot for {_job.Worker.CharacterName} — furniture scan was skipped (locked/blacklisted?). Short cooldown then replan.");
                        _job.WaitCooldown = 0.5f;
                        _isComplete = true;
                        return;
                    }

                    if (itemsStillInInventory)
                    {
                        Debug.LogWarning($"<color=orange>[LocateItem]</color> Les items réservés sont dans l'inventaire logique mais pas trouvés physiquement ! {_job.Worker.CharacterName} effectue un audit de sécurité (RefreshStorageInventory).");

                        // SANITY CHECK: Purge the inventory from logical ghosts directly
                        source.RefreshStorageInventory();

                        // Force the transporter to replan immediately after the audit.
                        // Order will be cancelled implicitly by the refresh audit (which triggers ReportMissingReservedItem internally).
                        _job.WaitCooldown = 2f;
                        _job.CancelCurrentOrder(true);
                        _isComplete = true;
                        return;
                    }
                    else
                    {
                        Debug.LogWarning($"<color=orange>[LocateItem]</color> Les items réservés pour {wantedSO.ItemName} n'existent plus physiquement. Annulation et notification logistique.");
                        _job.WaitCooldown = 2f;
                        
                        // Notifier le LogisticsManager que les items ont été volés/détruits
                        var logisticsManager = source.LogisticsManager;
                        if (logisticsManager != null)
                        {
                            logisticsManager.ReportMissingReservedItem(_job.CurrentOrder);
                        }

                        _job.CancelCurrentOrder(true);
                        _isComplete = true;
                        return;
                    }
                }
            }

            _job.TargetWorldItem = targetWorldItem;
            // Defensive: ensure the furniture-source path is fully cleared whenever we
            // commit to the loose-WorldItem path. The fields are reset at the top of
            // Execute, but a future refactor could leave a window where they're set
            // from an earlier branch — clearing here keeps the two paths strictly
            // mutually exclusive at the `_job` level (which is what the planner gate
            // and MoveToItem/PickupItem early-out both rely on).
            _job.TargetSourceFurniture = null;
            _job.TargetItemFromFurniture = null;
            _isComplete = true;
            if (NPCDebug.VerboseActions)
                Debug.Log($"<color=cyan>[LocateItem]</color> {_job.Worker.CharacterName} a assigné TargetWorldItem: {(_job.TargetWorldItem != null ? _job.TargetWorldItem.name : "NULL")}. isComplete=true.");
        }

        public override void Exit(Character worker)
        {
            _isComplete = false;
        }
    }
}
