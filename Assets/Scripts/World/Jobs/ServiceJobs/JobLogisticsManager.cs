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

        // V?rification des ingrédients dans l'inventaire du bâtiment (ou StorageZone)
        if (_workplace != null)
        {
            var recipe = order.ItemToCraft.CraftingRecipe;
            foreach (var ingredient in recipe)
            {
                int needed = ingredient.Amount * order.Quantity;
                int available = _workplace.GetItemCount(ingredient.Item);

                if (available < needed)
                {
                    int toOrder = needed - available;
                    Debug.Log($"<color=yellow>[Logistics]</color> Ingrédient manquant pour {order.ItemToCraft.ItemName} : {ingredient.Item.ItemName} ({available}/{needed}). Placement d'une commande...");
                    
                    RequestStock(ingredient.Item, toOrder);
                }
            }
        }

        _activeCraftingOrders.Add(order);
        Debug.Log($"<color=cyan>[JobLogisticsManager]</color> Commande Craft reçue : {order.Quantity}x {order.ItemToCraft.ItemName}. Jours restants : {order.RemainingDays}");
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

            if (!shop.NeedsRestock(itemSO, maxStock))
            {
                Debug.Log($"<color=cyan>[Logistics]</color>   ✓ {itemSO.ItemName}: {currentStock}/{maxStock} — stock suffisant.");
                continue;
            }

            Debug.Log($"<color=yellow>[Logistics]</color>   ✗ {itemSO.ItemName}: {currentStock}/{maxStock} — stock bas, commande nécessaire...");
            int quantityToOrder = maxStock - currentStock;
            RequestStock(itemSO, quantityToOrder);
        }
    }

    /// <summary>
    /// Pour chaque commande de fabrication active, vérifie si on a les ingrédients.
    /// Si non, place des commandes pour les obtenir.
    /// </summary>
    private void CheckCraftingIngredients(CraftingBuilding building)
    {
        foreach (var order in _activeCraftingOrders)
        {
            var recipe = order.ItemToCraft.CraftingRecipe;
            if (recipe == null) continue;

            foreach (var ingredient in recipe)
            {
                int needed = ingredient.Amount * order.Quantity;
                int possessed = building.GetItemCount(ingredient.Item);

                if (possessed < needed)
                {
                    int quantityToOrder = needed - possessed;
                    RequestStock(ingredient.Item, quantityToOrder);
                }
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

        // Vérifier si une BuyOrder est déjà en cours chez le fournisseur pour cet item
        bool alreadyOrdered = supplierLogistics.ActiveOrders.Any(o => o.ItemToTransport == itemSO && o.Destination == _workplace);
        if (alreadyOrdered)
        {
            Debug.Log($"<color=cyan>[Logistics]</color>   ⏳ {itemSO.ItemName}: BuyOrder déjà en cours chez {supplier.BuildingName}.");
            return;
        }

        // --- NOUVEAU: Vérifier si notre building n'a pas DÉJÀ une commande non placée pour ce même item et ce même fournisseur ---
        bool alreadyPendingPlacement = _placedBuyOrders.Any(o => o.ItemToTransport == itemSO && o.Source == supplier && !o.IsPlaced);
        if (alreadyPendingPlacement)
        {
            Debug.Log($"<color=cyan>[Logistics]</color>   ⏳ {itemSO.ItemName}: Une commande est déjà en attente d'être passée physiquement à {supplier.BuildingName}.");
            return;
        }

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
    /// Valide notre réception et prévient le fournisseur.
    /// </summary>
    public void OnItemGathered(ItemSO itemSO)
    {
        // On cherche la plus vieille commande non complétée pour cet item
        var placedOrder = _placedBuyOrders.FirstOrDefault(o => o.ItemToTransport == itemSO && !o.IsCompleted);
        if (placedOrder != null)
        {
            placedOrder.RecordDelivery(1);
            
            // Notifier le fournisseur que la livraison est bien reçue
            if (placedOrder.Source != null)
            {
                var sourceManager = placedOrder.Source.Jobs.OfType<JobLogisticsManager>().FirstOrDefault();
                if (sourceManager != null)
                {
                    sourceManager.AcknowledgeDeliveryProgress(placedOrder);
                }
            }

            if (placedOrder.IsCompleted)
            {
                _placedBuyOrders.Remove(placedOrder);
                Debug.Log($"<color=green>[JobLogisticsManager]</color> ✅ La commande de {placedOrder.Quantity}x {itemSO.ItemName} a été entièrement reçue et intégrée !");
            }
        }
    }

    /// <summary>
    /// Appelé par le client lorsqu'il a physiquement reçu et rangé un item lié à une de nos commandes actives.
    /// Diminue la demande attendue et nettoie la commande terminées.
    /// </summary>
    public void AcknowledgeDeliveryProgress(BuyOrder clientOrder)
    {
        // On cherche dans NOS _activeOrders la commande qui correspond à la sienne (même item, même target)
        var myOrder = _activeOrders.FirstOrDefault(o => o.ItemToTransport == clientOrder.ItemToTransport && o.Destination == clientOrder.Destination);
        if (myOrder != null)
        {
            myOrder.RecordDelivery(1);
            if (myOrder.IsCompleted)
            {
                _activeOrders.Remove(myOrder);
                Debug.Log($"<color=green>[JobLogisticsManager]</color> 🤝 Le client a acquitté avoir reçu toute la commande de {myOrder.Quantity}x {myOrder.ItemToTransport.ItemName} !");
            }
        }
    }

    /// <summary>
    /// Traite les BuyOrders reçues par ce bâtiment en tant que fournisseur.
    /// Si l'inventaire est suffisant -> génère un TransportOrder vers le client.
    /// Si insuffisant et craftable -> lance un CraftingOrder interne.
    /// </summary>
    private void ProcessActiveBuyOrders()
    {
        // Traiter de la fin vers le début au cas où on voudrait les retirer (Actuellement géré par UpdateOrderProgress via livraison Transporter)
        for (int i = _activeOrders.Count - 1; i >= 0; i--)
        {
            var buyOrder = _activeOrders[i];
            
            int remainingToDispatch = buyOrder.Quantity - buyOrder.DeliveredQuantity - buyOrder.DispatchedQuantity;
            if (remainingToDispatch <= 0) continue;

            // On ne check plus _pendingOrders, on vérifie _placedTransportOrders pour éviter des doublons infinis
            if (_placedTransportOrders.Any(t => t.ItemToTransport == buyOrder.ItemToTransport && t.Destination == buyOrder.Destination && !t.IsPlaced))
            {
                continue;
            }

            int availableStock = _workplace.GetItemCount(buyOrder.ItemToTransport);
            
            if (availableStock >= remainingToDispatch)
            {
                // Stock suffisant, on prépare l'expédition
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
                    buyOrder.Destination
                );

                buyOrder.RecordDispatch(remainingToDispatch);

                _placedTransportOrders.Add(transportOrder); // Suivi local pour réessayer si échec
                _pendingOrders.Enqueue(new PendingOrder(transportOrder, transporter));
                Debug.Log($"<color=cyan>[Logistics]</color>   🚚 Expédition de {remainingToDispatch}x {buyOrder.ItemToTransport.ItemName} vers {buyOrder.Destination.BuildingName} préparée.");
            }
            else
            {
                // Stock insuffisant
                if (_workplace.RequiresCraftingFor(buyOrder.ItemToTransport))
                {
                    // Eviter de spammer les CraftingOrders si on en a déjà une en cours
                    bool craftInProgress = _activeCraftingOrders.Any(c => c.ItemToCraft == buyOrder.ItemToTransport);
                    if (!craftInProgress)
                    {
                        int quantityToCraft = remainingToDispatch - availableStock;
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
