using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using MWI.Time;

/// <summary>
/// Gère la réception et la distribution des commandes (BuyOrders) pour un bâtiment.
/// Inclus dans TransporterBuilding et les bâtiments nécessitant des expéditions (ex: GatheringBuilding).
/// </summary>
public class JobLogisticsManager : Job
{
    private string _customTitle;
    public override string JobTitle => _customTitle;
    public override JobCategory Category => JobCategory.Service;

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

    // Liste des commandes de fabrication (Crafting) locales au bâtiment
    private List<CraftingOrder> _activeCraftingOrders = new List<CraftingOrder>();
    public IReadOnlyList<CraftingOrder> ActiveCraftingOrders => _activeCraftingOrders;

    // File d'attente des commandes à placer physiquement
    private Queue<PendingOrder> _pendingOrders = new Queue<PendingOrder>();
    public bool HasPendingOrders => _pendingOrders.Count > 0;

    // GOAP
    private GoapGoal _logisticsGoal;
    private List<GoapAction> _availableActions;
    private Queue<GoapAction> _currentPlan;
    private GoapAction _currentAction;

    public override string CurrentActionName => _currentAction != null ? _currentAction.ActionName : "Planning / Idle";
    public override string CurrentGoalName => _logisticsGoal != null ? _logisticsGoal.GoalName : "No Goal";

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

    public JobLogisticsManager(string title = "Logistics Manager")
    {
        _customTitle = title;
    }

    /// <summary>
    /// On s'abonne à OnNewDay au moment de l'assignation (quand _workplace et TimeManager sont dispo).
    /// </summary>
    public override void Assign(Character worker, CommercialBuilding workplace)
    {
        base.Assign(worker, workplace);

        if (TimeManager.Instance != null)
        {
            TimeManager.Instance.OnNewDay += CheckExpiredOrders;
            Debug.Log($"<color=cyan>[Logistics]</color> {worker.CharacterName} abonné à OnNewDay pour {workplace.BuildingName}.");
        }
    }

    public override void Unassign()
    {
        if (TimeManager.Instance != null)
        {
            TimeManager.Instance.OnNewDay -= CheckExpiredOrders;
        }

        // Cleanup GOAP
        if (_currentAction != null)
        {
            _currentAction.Exit(_worker);
            _currentAction = null;
        }
        _currentPlan = null;

        base.Unassign();
    }

    ~JobLogisticsManager()
    {
        if (TimeManager.Instance != null)
        {
            TimeManager.Instance.OnNewDay -= CheckExpiredOrders;
        }
    }

    public override void OnWorkerPunchOut()
    {
        base.OnWorkerPunchOut();
        if (_currentAction != null)
        {
            _currentAction.Exit(_worker);
            _currentAction = null;
        }
        _currentPlan = null;
    }

    /// <summary>
    /// Utilisé (notamment via UI ou Interactions) pour déposer une commande BuyOrder auprès de ce manager.
    /// Traitera la commande lors du Tick (Execute).
    /// </summary>
    public bool PlaceBuyOrder(BuyOrder order)
    {
        if (order == null || order.Quantity <= 0) return false;

        _activeOrders.Add(order);
        Debug.Log($"<color=cyan>[JobLogisticsManager]</color> Commande reçue : {order.Quantity}x {order.ItemToTransport.ItemName} pour {order.Destination.BuildingName}. Jours restants : {order.RemainingDays}");
        return true;
    }

    /// <summary>
    /// Dépôt d'une commande de fabrication (locale).
    /// </summary>
    public bool PlaceCraftingOrder(CraftingOrder order)
    {
        if (order == null || order.Quantity <= 0) return false;

        _activeCraftingOrders.Add(order);
        Debug.Log($"<color=cyan>[JobLogisticsManager]</color> Commande Craft reçue : {order.Quantity}x {order.ItemToCraft.ItemName}. Jours restants : {order.RemainingDays}");

        // Vérification globale des besoins par rapport à l'inventaire + commandes en cours
        if (_workplace is CraftingBuilding crafting)
        {
            CheckCraftingIngredients(crafting);
        }

        return true;
    }

    /// <summary>
    /// Utilisé pour déposer physiquement une commande de Transport auprès de ce manager (ie: TransporterBuilding).
    /// </summary>
    public bool PlaceTransportOrder(TransportOrder order)
    {
        if (order == null || order.Quantity <= 0) return false;

        _activeTransportOrders.Add(order);
        Debug.Log($"<color=cyan>[JobLogisticsManager]</color> Commande Transport reçue : {order.Quantity}x {order.ItemToTransport.ItemName} pour {order.Destination.BuildingName}.");
        return true;
    }

    /// <summary>
    /// Utilisé par les JobTransporter pour récupérer une commande de transport simple.
    /// Renvoie la première de la liste (FIFO).
    /// </summary>
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

    /// <summary>
    /// Utilisé par les JobCrafter pour récupérer une commande de fabrication à effectuer.
    /// Retourne la commande dont la deadline est la plus proche, ou la première de la liste.
    /// </summary>
    public CraftingOrder GetNextAvailableCraftingOrder()
    {
        if (_activeCraftingOrders.Count == 0) return null;

        CraftingOrder nextOrder = _activeCraftingOrders[0];
        foreach(var order in _activeCraftingOrders)
        {
            if(order.RemainingDays < nextOrder.RemainingDays)
            {
                nextOrder = order;
            }
        }
        return nextOrder;
    }

    /// <summary>
    /// Met à jour la progression d'une commande.
    /// Si complétée, elle est retirée de la liste.
    /// </summary>
    public void UpdateOrderProgress(BuyOrder order, int deliveredAmount)
    {
        if (!_activeOrders.Contains(order)) return;

        bool completed = order.RecordDelivery(deliveredAmount);
        if (completed)
        {
            _activeOrders.Remove(order);
            Debug.Log($"<color=green>[JobLogisticsManager]</color> Commande {order.Quantity}x {order.ItemToTransport.ItemName} COMPLÉTÉE.");
        }
    }

    /// <summary>
    /// Met à jour la progression d'une commande de Transport.
    /// Si complétée, elle est retirée de la liste.
    /// </summary>
    public void UpdateTransportOrderProgress(TransportOrder order, int deliveredAmount)
    {
        if (!_activeTransportOrders.Contains(order)) return;

        bool completed = order.RecordDelivery(deliveredAmount);

        // NOUVEAU: Mettre à jour la commande client instantanément au drop!
        if (order.AssociatedBuyOrder != null)
        {
            // RECORD HERE, ONCE! The instance is shared across Client, Supplier, and Transporter!
            order.AssociatedBuyOrder.RecordDelivery(deliveredAmount);

            var clientLogistics = order.Destination?.Jobs.OfType<JobLogisticsManager>().FirstOrDefault();
            if (clientLogistics != null)
            {
                // Le client se met à jour. On ne l'oblige PLUS à répondre car c'est LE FOURNISSEUR qui doit clôturer SON ticket de commande localement
                clientLogistics.OnItemsDeliveredByTransporter(order.AssociatedBuyOrder, deliveredAmount);
                
                // --- FIX: Le TRANSPORTEUR prévient le FOURNISSEUR que la commande a été honorée ---
                var supplierLogistics = order.AssociatedBuyOrder.Source?.Jobs.OfType<JobLogisticsManager>().FirstOrDefault();
                if (supplierLogistics != null)
                {
                    supplierLogistics.AcknowledgeDeliveryProgress(order.AssociatedBuyOrder, deliveredAmount);
                }
            }
        }

        if (completed)
        {
            _activeTransportOrders.Remove(order);
            Debug.Log($"<color=green>[JobLogisticsManager]</color> Commande Transport {order.Quantity}x {order.ItemToTransport.ItemName} COMPLÉTÉE.");
        }
    }

    /// <summary>
    /// Met à jour la progression d'une commande de fabrication.
    /// Si complétée, elle est retirée de la liste et un BuyOrder (transport) est placé
    /// auprès d'un TransporterBuilding pour livrer les items au client.
    /// </summary>
    public void UpdateCraftingOrderProgress(CraftingOrder order, int craftedAmount)
    {
        if (!_activeCraftingOrders.Contains(order)) return;

        bool completed = order.RecordCraft(craftedAmount);
        if (completed)
        {
            _activeCraftingOrders.Remove(order);
            Debug.Log($"<color=green>[JobLogisticsManager]</color> Commande Craft {order.Quantity}x {order.ItemToCraft.ItemName} COMPLÉTÉE.");
        }
    }



    /// <summary>
    /// Cherche un TransporterBuilding dans la ville.
    /// </summary>
    private CommercialBuilding FindTransporterBuilding()
    {
        if (BuildingManager.Instance == null) return null;

        foreach (var b in BuildingManager.Instance.allBuildings)
        {
            if (b is TransporterBuilding) return b as CommercialBuilding;
        }
        return null;
    }

    /// <summary>
    /// Vérifie si l'une des commandes est expirée et applique les conséquences sociales.
    /// Appelé chaque nouveau jour via OnNewDay.
    /// </summary>
    private void CheckExpiredOrders()
    {
        if (TimeManager.Instance == null) return;

        CheckExpiredBuyOrders();
        CheckExpiredCraftingOrders();
    }

    /// <summary>
    /// Appelé lorsque le worker arrive au travail (Punch In).
    /// Si le workplace est un ShopBuilding, on vérifie l'inventaire.
    /// </summary>
    public void OnWorkerPunchIn()
    {
        if (!IsOwnerOrOnSchedule()) return;

        if (_workplace is ShopBuilding shop)
        {
            CheckShopInventory(shop);
        }
        else if (_workplace is CraftingBuilding crafting)
        {
            CheckCraftingIngredients(crafting);
        }
    }

    /// <summary>
    /// Vérifie si le worker est le propriétaire OU est dans ses heures de travail.
    /// Le propriétaire peut agir à tout moment.
    /// </summary>
    private bool IsOwnerOrOnSchedule()
    {
        if (_worker == null || _workplace == null) return false;

        // Le propriétaire individuel peut toujours agir
        if (_workplace.Owner == _worker) return true;

        // Sinon, vérifier si on est dans les heures de travail
        if (_worker.CharacterSchedule != null)
        {
            return _worker.CharacterSchedule.CurrentActivity == ScheduleActivity.Work;
        }

        return false;
    }

    /// <summary>
    /// Scanne l'inventaire du shop et passe des commandes si des items manquent.
    /// Appelé à chaque nouveau jour via OnNewDay.
    /// </summary>
    private void CheckShopInventory(ShopBuilding shop)
    {
        var entries = shop.ShopEntries;

        Debug.Log($"<color=cyan>[Logistics]</color> {_worker?.CharacterName ?? "?"} vérifie l'inventaire de {shop.BuildingName} ({entries.Count} types d'items).");

        foreach (var entry in entries)
        {
            var itemSO = entry.Item;
            int maxStock = entry.MaxStock > 0 ? entry.MaxStock : 5;
            int currentStock = shop.GetStockCount(itemSO);
            
            // Calcul de la quantité déjà en cours de commande (pending + en livraison)
            int alreadyOrdered = 0;
            foreach (var buyOrder in _placedBuyOrders)
            {
                if (buyOrder.ItemToTransport == itemSO)
                {
                    alreadyOrdered += buyOrder.Quantity - buyOrder.DeliveredQuantity;
                }
            }

            int virtualStock = currentStock + alreadyOrdered;

            if (virtualStock >= maxStock)
            {
                Debug.Log($"<color=cyan>[Logistics]</color>   ✓ {itemSO.ItemName}: {currentStock} (physique) + {alreadyOrdered} (commandé) / {maxStock} — stock suffisant.");
                continue;
            }

            Debug.Log($"<color=yellow>[Logistics]</color>   ✗ {itemSO.ItemName}: {virtualStock}/{maxStock} — stock virtuel bas, commande nécessaire...");
            int quantityToOrder = maxStock - virtualStock;
            RequestStock(itemSO, quantityToOrder);
        }
    }

    /// <summary>
    /// Pour chaque commande de fabrication active, vérifie si on a les ingrédients.
    /// Si non, place des commandes pour les obtenir.
    /// </summary>
    private void CheckCraftingIngredients(CraftingBuilding building)
    {
        Dictionary<ItemSO, int> totalNeeded = new Dictionary<ItemSO, int>();

        foreach (var order in _activeCraftingOrders)
        {
            var recipe = order.ItemToCraft.CraftingRecipe;
            if (recipe == null) continue;

            foreach (var ingredient in recipe)
            {
                if (!totalNeeded.ContainsKey(ingredient.Item))
                {
                    totalNeeded[ingredient.Item] = 0;
                }
                totalNeeded[ingredient.Item] += ingredient.Amount * order.Quantity;
            }
        }

        foreach (var kvp in totalNeeded)
        {
            ItemSO item = kvp.Key;
            int needed = kvp.Value;
            int possessed = building.GetItemCount(item);
            
            // Calcul de la quantité déjà en cours de commande (pending + en livraison)
            int alreadyOrdered = 0;
            foreach (var buyOrder in _placedBuyOrders)
            {
                if (buyOrder.ItemToTransport == item)
                {
                    alreadyOrdered += buyOrder.Quantity - buyOrder.DeliveredQuantity;
                }
            }

            if (possessed + alreadyOrdered < needed)
            {
                int quantityToOrder = needed - (possessed + alreadyOrdered);
                Debug.Log($"<color=yellow>[Logistics]</color> Ingrédient manquant (Global) : {item.ItemName} ({possessed + alreadyOrdered}/{needed}). Placement d'une commande de {quantityToOrder}x...");
                RequestStock(item, quantityToOrder);
            }
        }
    }

    /// <summary>
    /// Demande du stock (BuyOrder) auprès d'un fournisseur.
    /// </summary>
    private void RequestStock(ItemSO itemSO, int quantityToOrder)
    {
        var supplier = FindSupplierFor(itemSO);
        if (supplier == null)
        {
            Debug.LogWarning($"<color=orange>[Logistics]</color>   Aucun fournisseur trouvé pour {itemSO.ItemName}.");
            return;
        }

        var supplierLogistics = supplier.Jobs.OfType<JobLogisticsManager>().FirstOrDefault();
        if (supplierLogistics == null || supplierLogistics.Worker == null)
        {
            Debug.LogWarning($"<color=orange>[Logistics]</color>   {supplier.BuildingName} n'a pas de LogisticsManager assigné.");
            return;
        }

        // --- NOUVEAU: Vérifier si notre building n'a pas DÉJÀ une commande non placée pour ce même item et ce même fournisseur ---
        var existingPending = _placedBuyOrders.FirstOrDefault(o => o.ItemToTransport == itemSO && o.Source == supplier && !o.IsPlaced);
        if (existingPending != null)
        {
            existingPending.AddQuantity(quantityToOrder);
            Debug.Log($"<color=cyan>[Logistics]</color>   ➕ {itemSO.ItemName}: Ajout de {quantityToOrder}x à la commande en attente pour {supplier.BuildingName}.");
            return;
        }

        // Si la commande est déjà placée chez le fournisseur (IsPlaced == true), on préfère en créer une nouvelle
        // pour que le worker aille physiquement signer le nouveau contrat.
        // Les risques de boucle infinie sont désormais gérés par CheckCraftingIngredients (qui soustrait les pending/placed).

        var buyOrder = new BuyOrder(
            itemSO,
            quantityToOrder,
            supplier,
            _workplace,
            3,
            _workplace.Owner,
            null
        );

        _placedBuyOrders.Add(buyOrder);
        _pendingOrders.Enqueue(new PendingOrder(buyOrder, supplier));
        Debug.Log($"<color=cyan>[Logistics]</color>   📦 Enregistrement d'une commande d'achat (BuyOrder) de {quantityToOrder}x {itemSO.ItemName} auprès de {supplier.BuildingName}.");
    }

    /// <summary>
    /// Appelé par GoapAction_GatherStorageItems lorsqu'un item ramassé est rangé dans le bâtiment.
    /// Valide simplement le rangement physique sans toucher aux BuyOrders (elles sont complétées à la livraison).
    /// </summary>
    public void OnItemGathered(ItemSO itemSO)
    {
        // Ne gère plus l'économie ici (la BuyOrder a déjà été complétée par le livreur lors du drop)
        // Peut être utilisé pour des statistiques ou du logging local.
        Debug.Log($"<color=gray>[Logistics]</color> L'item {itemSO.ItemName} a été physiquement déplacé de la zone de livraison vers le stockage.");
    }

    /// <summary>
    /// Appelé par le Logistics Manager fournisseur (via le Transporter) lorsque le livreur a déposé les items chez nous.
    /// Met à jour notre commande d'achat locale et notifie le boss.
    /// </summary>
    public void OnItemsDeliveredByTransporter(BuyOrder clientOrder, int amount)
    {
        var myOrder = _placedBuyOrders.FirstOrDefault(o => o == clientOrder);
        if (myOrder != null)
        {
            // myOrder and clientOrder are the EXACT SAME instance. 
            // The Transporter already called RecordDelivery(amount). Just check for completion!
            if (myOrder.IsCompleted)
            {
                _placedBuyOrders.Remove(myOrder);
                Debug.Log($"<color=green><h2>[ECONOMY]</h2></color> <color=yellow>CONGRATULATIONS !</color> La commande de {myOrder.Quantity}x {myOrder.ItemToTransport.ItemName} a été entièrement livrée à {myOrder.Destination.BuildingName} !");
                
                // --- Social Reward: Le client remercie le fournisseur. ---
                Character clientBoss = myOrder.ClientBoss;
                Character supplierBoss = myOrder.Source?.Owner;
                
                if (clientBoss != null && supplierBoss != null && clientBoss != supplierBoss)
                {
                    if (clientBoss.CharacterRelation != null) clientBoss.CharacterRelation.UpdateRelation(supplierBoss, 5);
                }
            }
        }
    }

    /// <summary>
    /// Appelé par le client lorsqu'il a physiquement reçu un item lié à une de nos commandes actives.
    /// <summary>
    /// Appelé par le client lorsqu'il a physiquement reçu un item lié à une de nos commandes actives.
    /// Diminue la demande attendue et nettoie la commande terminées.
    /// </summary>
    public void AcknowledgeDeliveryProgress(BuyOrder clientOrder, int amount = 1)
    {
        // On cherche dans NOS _activeOrders la commande exacte
        var myOrder = _activeOrders.FirstOrDefault(o => o == clientOrder);
        if (myOrder != null)
        {
            // Already incremented by Transporter!
            if (myOrder.IsCompleted)
            {
                _activeOrders.Remove(myOrder);
                Debug.Log($"<color=green>[JobLogisticsManager]</color> 🤝 Le client a acquitté avoir reçu {myOrder.Quantity}x {myOrder.ItemToTransport.ItemName} !");

                // --- Social Reward: Le fournisseur est heureux du business complété. ---
                Character clientBoss = myOrder.ClientBoss;
                Character supplierBoss = myOrder.Source?.Owner;
                
                if (supplierBoss != null && clientBoss != null && supplierBoss != clientBoss)
                {
                    if (supplierBoss.CharacterRelation != null) supplierBoss.CharacterRelation.UpdateRelation(clientBoss, 5);
                }
            }
        }

        // --- FIX: Nettoyer la TransportOrder associée de notre liste locale pour éviter les boucles d'expédition infinies ---
        var linkedTransportOrder = _placedTransportOrders.FirstOrDefault(t => t.AssociatedBuyOrder == clientOrder);
        if (linkedTransportOrder != null)
        {
            // FIX: The transporter already recorded the physical delivery inside its own UpdateTransportOrderProgress.
            // Since this object is passed by reference, calling RecordDelivery again here would double-count the item!
            if (linkedTransportOrder.IsCompleted)
            {
                _placedTransportOrders.Remove(linkedTransportOrder);
                Debug.Log($"<color=gray>[JobLogisticsManager]</color> TransportOrder {linkedTransportOrder.Quantity}x {linkedTransportOrder.ItemToTransport.ItemName} retirée du suivi fournisseur.");
            }
        }
    }

    /// <summary>
    /// Annule et retire définitivement une TransportOrder de la file active du TransporterBuilding.
    /// Appelé lorsqu'une commande est physiquement irréalisable (ex: items disparus).
    /// </summary>
    public void CancelActiveTransportOrder(TransportOrder order)
    {
        if (order != null && _activeTransportOrders.Contains(order))
        {
            _activeTransportOrders.Remove(order);
            Debug.Log($"<color=orange>[JobLogisticsManager]</color> TransportOrder de {order.Quantity}x {order.ItemToTransport.ItemName} retirée définitivement de la file active (Échec).");
        }
    }

    /// <summary>
    /// Appelé par un transporteur lorsqu'il ne trouve pas un objet qui lui était pourtant réservé 
    /// (ex: objet volé, détruit, ou despawn).
    /// </summary>
    public void ReportMissingReservedItem(TransportOrder order)
    {
        if (order == null || !_placedTransportOrders.Contains(order)) return;

        Debug.LogWarning($"<color=orange>[JobLogisticsManager]</color> 🚨 Le transporteur a signalé des items réservés manquants pour {order.Quantity}x {order.ItemToTransport.ItemName}. Annulation de la commande physique pour forcer le recalcul logistics.");
        
        // On libère ce qui n'a pas été trouvé (le garbage collector ou l'inventaire s'en chargera, 
        // mais au niveau de l'ordre, on le clean pour qu'il soit retenté proprement)
        order.ReservedItems.Clear();

        // On annule la TransportOrder. Si c'était lié à une BuyOrder interne, on retire l'ID d'association
        // pour que ProcessActiveBuyOrders puisse relancer une nouvelle tentative de dispatch sur les stocks *réellement* restants.
        if (order.AssociatedBuyOrder != null)
        {
            // On considère que ce "dispatch" a échoué puisqu'on annule l'ordre physiquement
            int amountToRecover = order.Quantity - order.DeliveredQuantity;
            
            // On informe la BuyOrder qu'elle n'est plus "dispatchée" pour cette quantité, forçant le fournisseur à réessayer.
            order.AssociatedBuyOrder.CancelDispatch(amountToRecover);
        }
        
        _placedTransportOrders.Remove(order);

        // Retirer aussi de la file d'attente si elle y est encore
        var newQueue = new Queue<PendingOrder>(_pendingOrders.Where(p => p.TransportOrder != order));
        _pendingOrders = newQueue;
    }

    /// <summary>
    /// Traite les BuyOrders reçues par ce bâtiment en tant que fournisseur.
    /// Si l'inventaire est suffisant -> génère un TransportOrder vers le client.
    /// Si insuffisant et craftable -> lance un CraftingOrder interne.
    /// </summary>
    private void ProcessActiveBuyOrders()
    {
        // On récupère tous les ItemInstances qui sont *déjà* réservés par des commandes de Transport ou de Shop en cours.
        HashSet<ItemInstance> globallyReservedItems = new HashSet<ItemInstance>();
        
        foreach (var tOrder in _placedTransportOrders)
            foreach (var item in tOrder.ReservedItems)
                globallyReservedItems.Add(item);
                
        foreach (var bOrder in _placedBuyOrders)
            foreach (var item in bOrder.ReservedItems)
                globallyReservedItems.Add(item);

        // Traiter de la fin vers le début au cas où on voudrait les retirer (Actuellement géré par UpdateOrderProgress via livraison Transporter)
        for (int i = _activeOrders.Count - 1; i >= 0; i--)
        {
            var buyOrder = _activeOrders[i];
            
            // Important: we recompute remaining against actual dispatch vs quantity
            // To handle cancellation and missing items properly, a TransportOrder failure allows re-dispatch,
            // because CancelDispatch() physically lowered the count when the items were lost.
            int remainingToDispatch = buyOrder.Quantity - buyOrder.DispatchedQuantity;
            if (remainingToDispatch <= 0) continue;

            // On ne check plus _pendingOrders, on vérifie _placedTransportOrders pour éviter des doublons infinis
            if (_placedTransportOrders.Any(t => t.ItemToTransport == buyOrder.ItemToTransport && t.Destination == buyOrder.Destination && !t.IsPlaced))
            {
                continue;
            }

            // Récupérer les items physiques dans l'inventaire qui ne sont pas encore réservés
            var physicallyAvailableInstances = _workplace.Inventory
                .Where(inst => inst.ItemSO == buyOrder.ItemToTransport && !globallyReservedItems.Contains(inst))
                .ToList();
            
            if (physicallyAvailableInstances.Count >= remainingToDispatch)
            {
                // Stock suffisant, on réserve et on prépare l'expédition
                var transporter = FindTransporterBuilding();
                if (transporter == null)
                {
                    Debug.LogWarning($"<color=orange>[Logistics]</color> Aucun TransporterBuilding trouvé pour expédier {buyOrder.ItemToTransport.ItemName} à {buyOrder.Destination.BuildingName}.");
                    continue; 
                }

                var transportOrder = new TransportOrder(
                    buyOrder.ItemToTransport,
                    remainingToDispatch,
                    _workplace,
                    buyOrder.Destination,
                    buyOrder
                );

                // Assignation explicite des instances physiques réservées
                for (int j = 0; j < remainingToDispatch; j++)
                {
                    ItemInstance instanceToReserve = physicallyAvailableInstances[j];
                    transportOrder.ReserveItem(instanceToReserve);
                    buyOrder.ReserveItem(instanceToReserve);
                    globallyReservedItems.Add(instanceToReserve); // Ajouter au set global pour la boucle suivante
                }

                buyOrder.RecordDispatch(remainingToDispatch);

                _placedTransportOrders.Add(transportOrder); // Suivi local pour réessayer si échec
                _pendingOrders.Enqueue(new PendingOrder(transportOrder, transporter));
                Debug.Log($"<color=cyan>[Logistics]</color>   🚚 Expédition de {remainingToDispatch}x {buyOrder.ItemToTransport.ItemName} vers {buyOrder.Destination.BuildingName} préparée avec réservation physique stricte.");
            }
            else
            {
                // Stock insuffisant ou partiellement insuffisant
                if (_workplace.RequiresCraftingFor(buyOrder.ItemToTransport))
                {
                    // Eviter de spammer les CraftingOrders si on en a déjà une en cours
                    bool craftInProgress = _activeCraftingOrders.Any(c => c.ItemToCraft == buyOrder.ItemToTransport);
                    if (!craftInProgress)
                    {
                        int actuallyAvailableStock = physicallyAvailableInstances.Count;
                        int safeAvailable = Mathf.Max(0, actuallyAvailableStock);
                        int quantityToCraft = remainingToDispatch - safeAvailable;
                        
                        var craftOrder = new CraftingOrder(
                            buyOrder.ItemToTransport,
                            quantityToCraft,
                            buyOrder.RemainingDays,
                            _workplace.Owner,
                            buyOrder.Destination
                        );
                        PlaceCraftingOrder(craftOrder);
                        Debug.Log($"<color=cyan>[Logistics]</color>   🔨 Génération d'un ordre de craft interne pour honorer la BuyOrder de {buyOrder.Destination.BuildingName}.");
                    }
                }
            }
        }
    }

    private CommercialBuilding FindSupplierFor(ItemSO item)
    {
        if (BuildingManager.Instance == null) return null;

        foreach (var b in BuildingManager.Instance.allBuildings)
        {
            if (b == _workplace || !(b is CommercialBuilding commBuilding)) continue;

            // Utilisation de la méthode globale ProducesItem introduite pour respecter SOLID / OCP
            if (commBuilding.ProducesItem(item))
            {
                return commBuilding;
            }
        }
        return null;
    }

    private void CheckExpiredBuyOrders()
    {
        if (_activeOrders.Count == 0) return;

        List<BuyOrder> expiredOrders = new List<BuyOrder>();

        foreach (var order in _activeOrders)
        {
            order.DecreaseRemainingDays();
            if (order.RemainingDays <= 0)
            {
                expiredOrders.Add(order);
            }
        }

        foreach (var expired in expiredOrders)
        {
            _activeOrders.Remove(expired);
            Debug.Log($"<color=red>[JobLogisticsManager]</color> Commande {expired.Quantity}x {expired.ItemToTransport.ItemName} EXPIRÉE.");

            // Appliquer les conséquences sociales
            if (_workplace != null && _workplace.Owner != null)
            {
                Character workplaceBoss = _workplace.Owner;

                if (expired.ClientBoss != null && expired.ClientBoss.IsAlive())
                {
                    expired.ClientBoss.CharacterRelation?.UpdateRelation(workplaceBoss, -25);
                }
            }
        }
    }

    private void CheckExpiredCraftingOrders()
    {
        if (_activeCraftingOrders.Count == 0) return;

        List<CraftingOrder> expiredOrders = new List<CraftingOrder>();

        foreach (var order in _activeCraftingOrders)
        {
            order.DecreaseRemainingDays();
            if (order.RemainingDays <= 0)
            {
                expiredOrders.Add(order);
            }
        }

        foreach (var expired in expiredOrders)
        {
            _activeCraftingOrders.Remove(expired);
            Debug.Log($"<color=red>[JobLogisticsManager]</color> Commande Craft {expired.Quantity}x {expired.ItemToCraft.ItemName} EXPIRÉE.");

            // Appliquer les conséquences sociales si le bâtiment et boss sont configurés
            if (_workplace != null && _workplace.Owner != null)
            {
                Character workplaceBoss = _workplace.Owner;

                if (expired.ClientBoss != null && expired.ClientBoss.IsAlive())
                {
                    // Le patron qui n'a pas reçu sa commande déteste le boss de l'artisan
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

    /// <summary>
    /// Ré-injecte dans la file d'attente les commandes qui ont échoué lors de l'interaction physique
    /// (ex: le fournisseur était occupé).
    /// </summary>
    private void RetryUnplacedOrders()
    {
        // Réessayer les BuyOrders
        foreach (var order in _placedBuyOrders)
        {
            if (!order.IsPlaced && !order.IsCompleted)
            {
                bool alreadyInQueue = _pendingOrders.Any(p => p.Type == OrderType.Buy && p.BuyOrder == order);
                if (!alreadyInQueue)
                {
                    Debug.Log($"<color=yellow>[Logistics]</color> {_worker.CharacterName} : La BuyOrder de {order.Quantity}x {order.ItemToTransport.ItemName} pour {order.Source.BuildingName} avait échoué. On retente.");
                    EnqueuePendingOrder(new PendingOrder(order, order.Source));
                }
            }
        }

        // Réessayer les TransportOrders
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
                        Debug.Log($"<color=yellow>[Logistics]</color> {_worker.CharacterName} : La TransportOrder de {order.Quantity}x {order.ItemToTransport.ItemName} vers {order.Destination.BuildingName} avait échoué. On retente.");
                        EnqueuePendingOrder(new PendingOrder(order, transporter));
                    }
                }
            }
        }
    }

    public override void Execute()
    {
        if (_workplace == null) return;

        // V?rifier les commandes qui n'ont pas pu être physiquement passées (ex: Cible occupée)
        RetryUnplacedOrders();

        // Evaluer nos propres engagements envers les autres (BuyOrders reçues)
        ProcessActiveBuyOrders();


        // Si on a une action en cours, l'exécuter
        if (_currentAction != null)
        {
            // Vérifier que l'action est encore valide
            if (!_currentAction.IsValid(_worker))
            {
                Debug.Log($"<color=orange>[JobLogistics]</color> {_worker.CharacterName} : action {_currentAction.ActionName} invalide, replanification...");
                _currentAction.Exit(_worker);
                _currentAction = null;
                _currentPlan = null;
                return;
            }

            _currentAction.Execute(_worker);

            if (_currentAction.IsComplete)
            {
                Debug.Log($"<color=cyan>[JobLogistics]</color> {_worker.CharacterName} : action {_currentAction.ActionName} terminée.");
                _currentAction.Exit(_worker);
                _currentAction = null;
                _currentPlan = null; // Forcer la replanification
            }
            return;
        }

        // Pas d'action en cours → Planifier
        PlanNextActions();
    }

    private void PlanNextActions()
    {
        var worldState = new Dictionary<string, bool>
        {
            { "hasPendingOrders", HasPendingOrders },
            { "isIdling", false }
        };

        _availableActions = new List<GoapAction>
        {
            new GoapAction_PlaceOrder(this),
            new GoapAction_GatherStorageItems(this),
            new GoapAction_IdleInCommercialBuilding(_workplace as CommercialBuilding)
        };

        GoapGoal targetGoal;
        if (HasPendingOrders)
        {
            targetGoal = new GoapGoal("ProcessOrders", new Dictionary<string, bool> { { "hasPendingOrders", false } }, priority: 1);
        }
        else
        {
            targetGoal = new GoapGoal("Idle", new Dictionary<string, bool> { { "isIdling", true } }, priority: 1);
        }

        _logisticsGoal = targetGoal;
        
        var validActions = _availableActions.Where(a => a.IsValid(_worker)).ToList();
        
        _currentPlan = GoapPlanner.Plan(worldState, validActions, targetGoal);

        if (_currentPlan != null && _currentPlan.Count > 0)
        {
            _currentAction = _currentPlan.Dequeue();
            Debug.Log($"<color=green>[JobLogistics]</color> {_worker.CharacterName} : nouveau plan ! Première action → {_currentAction.ActionName}");
        }
        else
        {
            Debug.Log($"<color=orange>[JobLogistics]</color> {_worker.CharacterName} : impossible de planifier.");
        }
    }

}
