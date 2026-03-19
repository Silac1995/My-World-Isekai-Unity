using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using MWI.Time;

[RequireComponent(typeof(CommercialBuilding))]
public class BuildingLogisticsManager : MonoBehaviour
{
    private CommercialBuilding _building;

    // Liste des commandes (Achats) liées à d'autres bâtiments clients
    private List<BuyOrder> _activeOrders = new List<BuyOrder>();
    public IReadOnlyList<BuyOrder> ActiveOrders => _activeOrders;

    // Commandes d'achat qu'on a émises (en tant que client)
    private List<BuyOrder> _placedBuyOrders = new List<BuyOrder>();
    public IReadOnlyList<BuyOrder> PlacedBuyOrders => _placedBuyOrders;

    // Commandes de transport qu'on a émises pour satisfaire nos clients (en tant que fournisseur)
    private List<TransportOrder> _placedTransportOrders = new List<TransportOrder>();
    public IReadOnlyList<TransportOrder> PlacedTransportOrders => _placedTransportOrders;

    // Commandes de transport (pour le TransporterBuilding)
    private List<TransportOrder> _activeTransportOrders = new List<TransportOrder>();
    public IReadOnlyList<TransportOrder> ActiveTransportOrders => _activeTransportOrders;

    // Liste des commandes de fabrication (Crafting) locales au bâtiment
    private List<CraftingOrder> _activeCraftingOrders = new List<CraftingOrder>();
    public IReadOnlyList<CraftingOrder> ActiveCraftingOrders => _activeCraftingOrders;

    // File d'attente des commandes à placer physiquement
    private Queue<PendingOrder> _pendingOrders = new Queue<PendingOrder>();
    public bool HasPendingOrders => _pendingOrders.Count > 0;

    public enum OrderType { Buy, Crafting, Transport }

    public struct PendingOrder
    {
        public OrderType Type;
        public BuyOrder BuyOrder;
        public CraftingOrder CraftingOrder;
        public TransportOrder TransportOrder;
        public CommercialBuilding TargetBuilding;

        public PendingOrder(BuyOrder order, CommercialBuilding target)
        {
            Type = OrderType.Buy;
            BuyOrder = order;
            CraftingOrder = null;
            TransportOrder = null;
            TargetBuilding = target;
        }

        public PendingOrder(CraftingOrder order, CommercialBuilding target)
        {
            Type = OrderType.Crafting;
            BuyOrder = null;
            CraftingOrder = order;
            TransportOrder = null;
            TargetBuilding = target;
        }

        public PendingOrder(TransportOrder order, CommercialBuilding target)
        {
            Type = OrderType.Transport;
            BuyOrder = null;
            CraftingOrder = null;
            TransportOrder = order;
            TargetBuilding = target;
        }
    }

    private void Awake()
    {
        _building = GetComponent<CommercialBuilding>();
    }

    private void Start()
    {
        if (TimeManager.Instance != null)
        {
            TimeManager.Instance.OnNewDay += CheckExpiredOrders;
        }
    }

    private void OnDestroy()
    {
        if (TimeManager.Instance != null)
        {
            TimeManager.Instance.OnNewDay -= CheckExpiredOrders;
        }

        foreach (var order in _activeOrders.ToList())
        {
            CancelBuyOrder(order);
        }
        foreach (var order in _placedBuyOrders.ToList())
        {
            CancelBuyOrder(order);
        }
    }

    public void CancelBuyOrder(BuyOrder order)
    {
        if (order == null) return;

        bool wasRemoved = false;

        if (_activeOrders.Contains(order))
        {
            _activeOrders.Remove(order);
            wasRemoved = true;
            Debug.Log($"<color=orange>[BuildingLogisticsManager]</color> {_building?.BuildingName} : BuyOrder annulée/retirée (Fournisseur) : {order.Quantity}x {order.ItemToTransport.ItemName}");
        }

        if (_placedBuyOrders.Contains(order))
        {
            _placedBuyOrders.Remove(order);
            var newQueue = new Queue<PendingOrder>(_pendingOrders.Where(p => p.BuyOrder != order));
            _pendingOrders = newQueue;
            wasRemoved = true;
            Debug.Log($"<color=orange>[BuildingLogisticsManager]</color> {_building?.BuildingName} : BuyOrder annulée/retirée (Client) : {order.Quantity}x {order.ItemToTransport.ItemName}");
        }

        // Remove any strictly linked TransportOrder that hasn't been physically sent yet
        var linkedTransports = _placedTransportOrders.Where(t => t.AssociatedBuyOrder == order).ToList();
        foreach (var lt in linkedTransports)
        {
            _placedTransportOrders.Remove(lt);
            var newQueue = new Queue<PendingOrder>(_pendingOrders.Where(p => p.TransportOrder != lt));
            _pendingOrders = newQueue;
            Debug.Log($"<color=orange>[BuildingLogisticsManager]</color> {_building?.BuildingName} : TransportOrder liée annulée.");
            
            if (lt.Destination != null && lt.Destination.LogisticsManager != null)
            {
                lt.Destination.LogisticsManager.CancelActiveTransportOrder(lt);
            }
        }

        if (!wasRemoved) return;

        if (order.Source != null && order.Source != _building && order.Source.LogisticsManager != null)
        {
            order.Source.LogisticsManager.CancelBuyOrder(order);
        }

        if (order.Destination != null && order.Destination != _building && order.Destination.LogisticsManager != null)
        {
             order.Destination.LogisticsManager.CancelBuyOrder(order);
        }
    }

    public int GetReservedItemCount(ItemSO itemSO)
    {
        HashSet<ItemInstance> reservedInstances = new HashSet<ItemInstance>();

        foreach (var tOrder in _placedTransportOrders)
            foreach (var item in tOrder.ReservedItems)
                if (item.ItemSO == itemSO) reservedInstances.Add(item);

        foreach (var bOrder in _placedBuyOrders)
            foreach (var item in bOrder.ReservedItems)
                if (item.ItemSO == itemSO) reservedInstances.Add(item);

        foreach (var aOrder in _activeOrders)
            foreach (var item in aOrder.ReservedItems)
                if (item.ItemSO == itemSO) reservedInstances.Add(item);

        return reservedInstances.Count;
    }

    public bool PlaceBuyOrder(BuyOrder order)
    {
        if (order == null || order.Quantity <= 0) return false;
        _activeOrders.Add(order);
        Debug.Log($"<color=cyan>[BuildingLogisticsManager]</color> Commande reçue : {order.Quantity}x {order.ItemToTransport.ItemName} pour {order.Destination.BuildingName}. Jours restants : {order.RemainingDays}");
        return true;
    }

    public bool PlaceCraftingOrder(CraftingOrder order)
    {
        if (order == null || order.Quantity <= 0) return false;
        _activeCraftingOrders.Add(order);
        Debug.Log($"<color=cyan>[BuildingLogisticsManager]</color> Commande Craft reçue : {order.Quantity}x {order.ItemToCraft.ItemName}. Jours restants : {order.RemainingDays}");

        if (_building is CraftingBuilding craftingBuilding)
        {
            CheckCraftingIngredients(craftingBuilding);
        }
        return true;
    }

    public bool PlaceTransportOrder(TransportOrder order)
    {
        if (order == null || order.Quantity <= 0) return false;
        _activeTransportOrders.Add(order);
        Debug.Log($"<color=cyan>[BuildingLogisticsManager]</color> Commande Transport reçue : {order.Quantity}x {order.ItemToTransport.ItemName} pour {order.Destination.BuildingName}.");
        return true;
    }

    public TransportOrder GetNextAvailableTransportOrder()
    {
        if (_activeTransportOrders.Count == 0) return null;
        foreach (var order in _activeTransportOrders)
        {
            if (order.Quantity > order.DeliveredQuantity + order.InTransitQuantity)
            {
                return order;
            }
        }
        return null;
    }

    public CraftingOrder GetNextAvailableCraftingOrder()
    {
        var pendingOrders = _activeCraftingOrders.Where(o => !o.IsCompleted).ToList();
        if (pendingOrders.Count == 0) return null;
        CraftingOrder nextOrder = pendingOrders[0];
        foreach (var order in pendingOrders)
        {
            if (order.RemainingDays < nextOrder.RemainingDays)
            {
                nextOrder = order;
            }
        }
        return nextOrder;
    }

    public void UpdateOrderProgress(BuyOrder order, int deliveredAmount)
    {
        if (!_activeOrders.Contains(order)) return;
        bool completed = order.RecordDelivery(deliveredAmount);
        if (completed)
        {
            _activeOrders.Remove(order);
            Debug.Log($"<color=green>[BuildingLogisticsManager]</color> Commande {order.Quantity}x {order.ItemToTransport.ItemName} COMPLÉTÉE.");
        }
    }

    public void UpdateTransportOrderProgress(TransportOrder order, int deliveredAmount)
    {
        if (!_activeTransportOrders.Contains(order)) return;

        bool completed = order.RecordDelivery(deliveredAmount);

        if (order.AssociatedBuyOrder != null)
        {
            order.AssociatedBuyOrder.RecordDelivery(deliveredAmount);

            var clientLogistics = order.Destination?.LogisticsManager;
            if (clientLogistics != null)
            {
                clientLogistics.OnItemsDeliveredByTransporter(order.AssociatedBuyOrder, deliveredAmount);
                
                var supplierLogistics = order.AssociatedBuyOrder.Source?.LogisticsManager;
                if (supplierLogistics != null)
                {
                    supplierLogistics.AcknowledgeDeliveryProgress(order.AssociatedBuyOrder, deliveredAmount);
                }
            }
        }

        if (completed)
        {
            _activeTransportOrders.Remove(order);
            Debug.Log($"<color=green>[BuildingLogisticsManager]</color> Commande Transport {order.Quantity}x {order.ItemToTransport.ItemName} COMPLÉTÉE.");
        }
    }

    public void UpdateCraftingOrderProgress(CraftingOrder order, int craftedAmount)
    {
        if (!_activeCraftingOrders.Contains(order)) return;
        bool completed = order.RecordCraft(craftedAmount);
        if (completed)
        {
            Debug.Log($"<color=green>[BuildingLogisticsManager]</color> Commande Craft {order.Quantity}x {order.ItemToCraft.ItemName} COMPLÉTÉE. Maintenue en mémoire temporairement.");
        }
    }

    private CommercialBuilding FindTransporterBuilding()
    {
        if (BuildingManager.Instance == null) return null;
        foreach (var b in BuildingManager.Instance.allBuildings)
        {
            if (b is TransporterBuilding trans) return trans;
        }
        return null;
    }

    private void CheckExpiredOrders()
    {
        if (TimeManager.Instance == null) return;
        CheckExpiredBuyOrders();
        CheckExpiredCraftingOrders();
    }

    public void OnWorkerPunchIn(Character worker)
    {
        _building.RefreshStorageInventory();

        if (_building is ShopBuilding shop)
        {
            CheckShopInventory(shop, worker);
        }
        else if (_building is CraftingBuilding crafting)
        {
            CheckCraftingIngredients(crafting, worker);
        }
    }

    private void CheckShopInventory(ShopBuilding shop, Character worker)
    {
        var entries = shop.ShopEntries;
        string workerName = worker != null ? worker.CharacterName : "?";
        Debug.Log($"<color=cyan>[BuildingLogisticsManager]</color> {workerName} vérifie l'inventaire de {shop.BuildingName} ({entries.Count} types d'items).");

        foreach (var entry in entries)
        {
            var itemSO = entry.Item;
            int maxStock = entry.MaxStock > 0 ? entry.MaxStock : 5;
            int currentStock = shop.GetStockCount(itemSO);
            
            int alreadyOrdered = _placedBuyOrders
                .Where(o => o.ItemToTransport == itemSO && !o.IsCompleted)
                .Sum(o => o.Quantity);

            int virtualStock = currentStock + alreadyOrdered;

            if (virtualStock >= maxStock)
            {
                Debug.Log($"<color=cyan>[BuildingLogisticsManager]</color>   ✓ {itemSO.ItemName}: {virtualStock}/{maxStock} (Virtuel) — stock suffisant.");
                continue;
            }

            Debug.Log($"<color=yellow>[BuildingLogisticsManager]</color>   ✗ {itemSO.ItemName}: {virtualStock}/{maxStock} (Virtuel) — stock bas, commande nécessaire...");
            int quantityToOrder = maxStock - virtualStock;
            RequestStock(itemSO, quantityToOrder);
        }
    }

    private void CheckCraftingIngredients(CraftingBuilding building, Character worker = null)
    {
        Dictionary<ItemSO, int> globalIngredientNeeds = new Dictionary<ItemSO, int>();

        foreach (var order in _activeCraftingOrders)
        {
            if (order.IsCompleted) continue;
            var recipe = order.ItemToCraft.CraftingRecipe;
            if (recipe == null) continue;

            foreach (var ingredient in recipe)
            {
                if (!globalIngredientNeeds.ContainsKey(ingredient.Item))
                    globalIngredientNeeds[ingredient.Item] = 0;

                globalIngredientNeeds[ingredient.Item] += (ingredient.Amount * order.Quantity);
            }
        }

        foreach (var kvp in globalIngredientNeeds)
        {
            ItemSO itemSO = kvp.Key;
            int totalNeeded = kvp.Value;
            int possessed = building.GetItemCount(itemSO);
            
            int alreadyOrdered = _placedBuyOrders
                .Where(o => o.ItemToTransport == itemSO && !o.IsCompleted)
                .Sum(o => o.Quantity);

            int virtualStock = possessed + alreadyOrdered;

            if (virtualStock < totalNeeded)
            {
                int quantityToOrder = totalNeeded - virtualStock;
                Debug.Log($"<color=yellow>[BuildingLogisticsManager]</color> Déficit global pour {itemSO.ItemName} : {virtualStock}/{totalNeeded} (Possédés:{possessed}, EnAttente:{alreadyOrdered}). Placement d'une commande pour {quantityToOrder}...");
                RequestStock(itemSO, quantityToOrder);
            }
        }
    }

    private void RequestStock(ItemSO itemSO, int quantityToOrder)
    {
        var supplier = FindSupplierFor(itemSO);
        if (supplier == null)
        {
            Debug.LogWarning($"<color=orange>[BuildingLogisticsManager]</color>   Aucun fournisseur trouvé pour {itemSO.ItemName}.");
            return;
        }

        var supplierLogistics = supplier.LogisticsManager;
        if (supplierLogistics == null)
        {
            Debug.LogWarning($"<color=orange>[BuildingLogisticsManager]</color>   {supplier.BuildingName} n'a pas de LogisticsManager assigné.");
            return;
        }

        bool alreadyOrdered = supplierLogistics.ActiveOrders.Any(o => o.ItemToTransport == itemSO && o.Destination == _building);
        if (alreadyOrdered)
        {
            Debug.Log($"<color=cyan>[BuildingLogisticsManager]</color>   ⏳ {itemSO.ItemName}: BuyOrder déjà en cours chez {supplier.BuildingName}.");
            return;
        }

        var pendingOrder = _placedBuyOrders.FirstOrDefault(o => o.ItemToTransport == itemSO && o.Source == supplier && !o.IsPlaced);
        if (pendingOrder != null)
        {
            Debug.Log($"<color=cyan>[BuildingLogisticsManager]</color>   ⏳ {itemSO.ItemName}: Une commande en attente existe! Ajout de la quantité (+{quantityToOrder}) à celle-ci.");
            pendingOrder.AddQuantity(quantityToOrder);
            return;
        }

        var buyOrder = new BuyOrder(
            itemSO,
            quantityToOrder,
            supplier,
            _building,
            3,
            _building.Owner,
            null
        );

        _placedBuyOrders.Add(buyOrder);
        _pendingOrders.Enqueue(new PendingOrder(buyOrder, supplier));
        Debug.Log($"<color=cyan>[BuildingLogisticsManager]</color>   📦 Enregistrement d'une commande d'achat (BuyOrder) de {quantityToOrder}x {itemSO.ItemName} auprès de {supplier.BuildingName}.");
    }

    public void OnItemGathered(ItemSO itemSO)
    {
        Debug.Log($"<color=gray>[BuildingLogisticsManager]</color> L'item {itemSO.ItemName} a été physiquement déplacé de la zone de livraison vers le stockage.");
    }

    public void OnItemsDeliveredByTransporter(BuyOrder clientOrder, int amount)
    {
        var myOrder = _placedBuyOrders.FirstOrDefault(o => o == clientOrder);
        if (myOrder != null)
        {
            if (myOrder.IsCompleted)
            {
                _placedBuyOrders.Remove(myOrder);
                Debug.Log($"<color=green><h2>[ECONOMY]</h2></color> <color=yellow>CONGRATULATIONS !</color> La commande de {myOrder.Quantity}x {myOrder.ItemToTransport.ItemName} a été entièrement livrée à {myOrder.Destination.BuildingName} !");
                
                Character clientBoss = myOrder.ClientBoss;
                Character supplierBoss = myOrder.Source?.Owner;
                
                if (clientBoss != null && supplierBoss != null && clientBoss != supplierBoss)
                {
                    if (clientBoss.CharacterRelation != null) clientBoss.CharacterRelation.UpdateRelation(supplierBoss, 5);
                }
            }
        }
    }

    public void AcknowledgeDeliveryProgress(BuyOrder clientOrder, int amount = 1)
    {
        var myOrder = _activeOrders.FirstOrDefault(o => o == clientOrder);
        if (myOrder != null)
        {
            if (myOrder.IsCompleted)
            {
                _activeOrders.Remove(myOrder);
                Debug.Log($"<color=green>[BuildingLogisticsManager]</color> 🤝 Le client a acquitté avoir reçu {myOrder.Quantity}x {myOrder.ItemToTransport.ItemName} !");

                Character clientBoss = myOrder.ClientBoss;
                Character supplierBoss = myOrder.Source?.Owner;
                
                if (supplierBoss != null && clientBoss != null && supplierBoss != clientBoss)
                {
                    if (supplierBoss.CharacterRelation != null) supplierBoss.CharacterRelation.UpdateRelation(clientBoss, 5);
                }
            }
        }

        var linkedTransportOrder = _placedTransportOrders.FirstOrDefault(t => t.AssociatedBuyOrder == clientOrder);
        if (linkedTransportOrder != null)
        {
            if (linkedTransportOrder.IsCompleted)
            {
                _placedTransportOrders.Remove(linkedTransportOrder);
                Debug.Log($"<color=gray>[BuildingLogisticsManager]</color> TransportOrder {linkedTransportOrder.Quantity}x {linkedTransportOrder.ItemToTransport.ItemName} retirée du suivi fournisseur.");
            }
        }
    }

    public void CancelActiveTransportOrder(TransportOrder order)
    {
        if (order != null && _activeTransportOrders.Contains(order))
        {
            _activeTransportOrders.Remove(order);
            Debug.Log($"<color=orange>[BuildingLogisticsManager]</color> TransportOrder de {order.Quantity}x {order.ItemToTransport.ItemName} retirée définitivement de la file active (Échec).");
        }
    }

    public void ReportMissingReservedItem(TransportOrder order)
    {
        if (order == null || !_placedTransportOrders.Contains(order)) return;

        Debug.LogWarning($"<color=orange>[BuildingLogisticsManager]</color> 🚨 Le transporteur a signalé des items réservés manquants pour {order.Quantity}x {order.ItemToTransport.ItemName}. Annulation de la commande physique pour forcer le recalcul logistics.");
        
        order.ReservedItems.Clear();

        if (order.AssociatedBuyOrder != null)
        {
            int amountToRecover = order.Quantity - order.DeliveredQuantity;
            order.AssociatedBuyOrder.CancelDispatch(amountToRecover);
        }
        
        _placedTransportOrders.Remove(order);

        var newQueue = new Queue<PendingOrder>(_pendingOrders.Where(p => p.TransportOrder != order));
        _pendingOrders = newQueue;
    }

    public void ProcessActiveBuyOrders()
    {
        HashSet<ItemInstance> globallyReservedItems = new HashSet<ItemInstance>();
        
        foreach (var tOrder in _placedTransportOrders)
            foreach (var item in tOrder.ReservedItems)
                globallyReservedItems.Add(item);
                
        foreach (var bOrder in _placedBuyOrders)
            foreach (var item in bOrder.ReservedItems)
                globallyReservedItems.Add(item);

        for (int i = _activeOrders.Count - 1; i >= 0; i--)
        {
            var buyOrder = _activeOrders[i];
            
            int remainingToDispatch = buyOrder.Quantity - buyOrder.DispatchedQuantity;
            if (remainingToDispatch <= 0) continue;

            if (_placedTransportOrders.Any(t => t.ItemToTransport == buyOrder.ItemToTransport && t.Destination == buyOrder.Destination && !t.IsPlaced))
            {
                continue;
            }

            var physicallyAvailableInstances = _building.Inventory
                .Where(inst => inst.ItemSO == buyOrder.ItemToTransport && !globallyReservedItems.Contains(inst))
                .ToList();
            
            if (physicallyAvailableInstances.Count >= remainingToDispatch)
            {
                DispatchTransportOrder(buyOrder, remainingToDispatch, physicallyAvailableInstances, globallyReservedItems);

                var linkedCompletedCraft = _activeCraftingOrders.FirstOrDefault(c => c.IsCompleted && c.ItemToCraft == buyOrder.ItemToTransport);
                if (linkedCompletedCraft != null)
                {
                    _activeCraftingOrders.Remove(linkedCompletedCraft);
                }
            }
            else
            {
                bool craftInProgress = _activeCraftingOrders.Any(c => !c.IsCompleted && c.ItemToCraft == buyOrder.ItemToTransport);
                
                int actuallyAvailableStock = physicallyAvailableInstances.Count;
                int safeAvailable = Mathf.Max(0, actuallyAvailableStock);
                
                var stolenProvenOrder = _activeCraftingOrders.FirstOrDefault(c => c.IsCompleted && c.ItemToCraft == buyOrder.ItemToTransport);

                if (stolenProvenOrder != null)
                {
                    Debug.LogWarning($"<color=orange>[BuildingLogisticsManager]</color> 🚨 VOL DETECTÉ: Craft fini pour {buyOrder.ItemToTransport.ItemName} mais items manquants! Livraison partielle de {safeAvailable}.");
                    
                    if (safeAvailable > 0)
                    {
                        DispatchTransportOrder(buyOrder, safeAvailable, physicallyAvailableInstances, globallyReservedItems);
                    }

                    _activeCraftingOrders.Remove(stolenProvenOrder);
                    
                    int newRemainingToDispatch = buyOrder.Quantity - buyOrder.DispatchedQuantity;
                    if (newRemainingToDispatch > 0)
                    {
                        var craftOrder = new CraftingOrder(
                            buyOrder.ItemToTransport,
                            newRemainingToDispatch,
                            buyOrder.RemainingDays,
                            _building.Owner,
                            buyOrder.Destination
                        );
                        PlaceCraftingOrder(craftOrder);
                        Debug.Log($"<color=cyan>[BuildingLogisticsManager]</color>   🔨 Nouvelle commande de craft interne ({newRemainingToDispatch}x) lancée pour compenser le vol.");
                    }
                }
                else
                {
                    if (_building.RequiresCraftingFor(buyOrder.ItemToTransport) && !craftInProgress)
                    {
                        var craftOrder = new CraftingOrder(
                            buyOrder.ItemToTransport,
                            remainingToDispatch,
                            buyOrder.RemainingDays,
                            _building.Owner,
                            buyOrder.Destination
                        );
                        PlaceCraftingOrder(craftOrder);
                        Debug.Log($"<color=cyan>[BuildingLogisticsManager]</color>   🔨 Génération d'un ordre de craft interne pour la BuyOrder de {buyOrder.Destination.BuildingName}.");
                    }
                }
            }
        }
    }

    private void DispatchTransportOrder(BuyOrder buyOrder, int amountToDispatch, List<ItemInstance> availableInstances, HashSet<ItemInstance> globallyReservedItems)
    {
        var transporter = FindTransporterBuilding();
        if (transporter == null)
        {
            Debug.LogWarning($"<color=orange>[BuildingLogisticsManager]</color> Aucun TransporterBuilding trouvé pour expédier {buyOrder.ItemToTransport.ItemName} à {buyOrder.Destination.BuildingName}.");
            return; 
        }

        var transportOrder = new TransportOrder(
            buyOrder.ItemToTransport,
            amountToDispatch,
            _building,
            buyOrder.Destination,
            buyOrder
        );

        for (int j = 0; j < amountToDispatch; j++)
        {
            ItemInstance instanceToReserve = availableInstances[j];
            transportOrder.ReserveItem(instanceToReserve);
            buyOrder.ReserveItem(instanceToReserve);
            globallyReservedItems.Add(instanceToReserve); 
        }

        buyOrder.RecordDispatch(amountToDispatch);

        _placedTransportOrders.Add(transportOrder); 
        _pendingOrders.Enqueue(new PendingOrder(transportOrder, transporter));
        Debug.Log($"<color=cyan>[BuildingLogisticsManager]</color>   🚚 Expédition de {amountToDispatch}x {buyOrder.ItemToTransport.ItemName} vers {buyOrder.Destination.BuildingName} préparée avec réservation physique stricte.");
    }

    private CommercialBuilding FindSupplierFor(ItemSO item)
    {
        if (BuildingManager.Instance == null) return null;

        foreach (var b in BuildingManager.Instance.allBuildings)
        {
            if (b == _building || !(b is CommercialBuilding commBuilding)) continue;

            if (commBuilding.ProducesItem(item))
            {
                return commBuilding;
            }
        }
        return null;
    }

    private void CheckExpiredBuyOrders()
    {
        if (_activeOrders.Count > 0)
        {
            List<BuyOrder> expiredSupplierOrders = new List<BuyOrder>();
            foreach (var order in _activeOrders)
            {
                order.DecreaseRemainingDays();
                if (order.RemainingDays <= 0)
                {
                    expiredSupplierOrders.Add(order);
                }
            }

            foreach (var expired in expiredSupplierOrders)
            {
                Debug.Log($"<color=red>[BuildingLogisticsManager]</color> Commande Client {expired.Quantity}x {expired.ItemToTransport.ItemName} EXPIRÉE chez le fournisseur.");

                if (_building != null && _building.Owner != null)
                {
                    Character workplaceBoss = _building.Owner;
                    if (expired.ClientBoss != null && expired.ClientBoss.IsAlive())
                    {
                        expired.ClientBoss.CharacterRelation?.UpdateRelation(workplaceBoss, -25);
                    }
                }

                CancelBuyOrder(expired);
            }
        }

        if (_placedBuyOrders.Count > 0)
        {
            List<BuyOrder> expiredClientOrders = new List<BuyOrder>();
            foreach (var order in _placedBuyOrders)
            {
                if (!order.IsPlaced)
                {
                    order.DecreaseRemainingDays();
                }

                if (order.RemainingDays <= 0)
                {
                    expiredClientOrders.Add(order);
                }
            }

            foreach (var expired in expiredClientOrders)
            {
                Debug.Log($"<color=red>[BuildingLogisticsManager]</color> Notre commande de {expired.Quantity}x {expired.ItemToTransport.ItemName} a EXPIRÉ et est retirée de nos suivis client.");

                CancelBuyOrder(expired);
            }
        }
    }

    private void CheckExpiredCraftingOrders()
    {
        if (_activeCraftingOrders.Count == 0) return;

        List<CraftingOrder> expiredOrders = new List<CraftingOrder>();

        foreach (var order in _activeCraftingOrders)
        {
            if (order.IsCompleted) continue; 

            order.DecreaseRemainingDays();
            if (order.RemainingDays <= 0)
            {
                expiredOrders.Add(order);
            }
        }

        foreach (var expired in expiredOrders)
        {
            _activeCraftingOrders.Remove(expired);
            Debug.Log($"<color=red>[BuildingLogisticsManager]</color> Commande Craft {expired.Quantity}x {expired.ItemToCraft.ItemName} EXPIRÉE.");

            if (_building != null && _building.Owner != null)
            {
                Character workplaceBoss = _building.Owner;

                if (expired.ClientBoss != null && expired.ClientBoss.IsAlive())
                {
                    expired.ClientBoss.CharacterRelation?.UpdateRelation(workplaceBoss, -25);
                }
            }
        }
    }

    public PendingOrder PeekPendingOrder()
    {
        return _pendingOrders.Peek();
    }

    public void DequeuePendingOrder()
    {
        if (_pendingOrders.Count > 0)
        {
            _pendingOrders.Dequeue();
        }
    }

    public void EnqueuePendingOrder(PendingOrder order)
    {
        _pendingOrders.Enqueue(order);
    }

    public void RetryUnplacedOrders(Character worker = null)
    {
        string workerName = worker != null ? worker.CharacterName : "LogisticsManager";

        foreach (var order in _placedBuyOrders)
        {
            if (!order.IsPlaced && !order.IsCompleted)
            {
                bool alreadyInQueue = _pendingOrders.Any(p => p.Type == OrderType.Buy && p.BuyOrder == order);
                if (!alreadyInQueue)
                {
                    Debug.Log($"<color=yellow>[BuildingLogisticsManager]</color> {workerName} : La BuyOrder de {order.Quantity}x {order.ItemToTransport.ItemName} pour {order.Source.BuildingName} avait échoué. On retente.");
                    EnqueuePendingOrder(new PendingOrder(order, order.Source));
                }
            }
        }

        foreach (var order in _placedTransportOrders)
        {
            if (!order.IsPlaced && !order.IsCompleted)
            {
                bool alreadyInQueue = _pendingOrders.Any(p => p.Type == OrderType.Transport && p.TransportOrder == order);
                if (!alreadyInQueue)
                {
                    var transporter = FindTransporterBuilding();
                    if (transporter != null)
                    {
                        Debug.Log($"<color=yellow>[BuildingLogisticsManager]</color> {workerName} : La TransportOrder de {order.Quantity}x {order.ItemToTransport.ItemName} vers {order.Destination.BuildingName} avait échoué. On retente.");
                        EnqueuePendingOrder(new PendingOrder(order, transporter));
                    }
                }
            }
        }
    }
}
