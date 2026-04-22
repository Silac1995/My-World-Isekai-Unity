using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Entrée de catalogue du shop : un item et sa quantité cible en stock.
/// </summary>
[System.Serializable]
public struct ShopItemEntry
{
    public ItemSO Item;
    public int MaxStock;
}

/// <summary>
/// Bâtiment de type Shop.
/// Nécessite un Vendeur pour vendre ses produits aux clients et un LogisticsManager pour le réapprovisionner.
/// Gère également la file d'attente (Queue) des clients.
///
/// Implements <see cref="IStockProvider"/> so <see cref="BuildingLogisticsManager.CheckStockTargets"/>
/// can drive shelf restocking through the same unified path as <see cref="CraftingBuilding"/>
/// input-material restocking.
/// </summary>
public class ShopBuilding : CommercialBuilding, IStockProvider
{
    public override BuildingType BuildingType => BuildingType.Shop;

    [Header("Shop Settings")]
    [SerializeField] private List<ShopItemEntry> _itemsToSell = new List<ShopItemEntry>();

    /// <inheritdoc/>
    public IEnumerable<StockTarget> GetStockTargets()
    {
        foreach (var entry in _itemsToSell)
        {
            if (entry.Item == null) continue;
            // Preserve the existing ShopBuilding default: treat zero/negative MaxStock as 5.
            int minStock = entry.MaxStock > 0 ? entry.MaxStock : 5;
            yield return new StockTarget(entry.Item, minStock);
        }
    }
    
    [Header("Work Positions")]
    [SerializeField] private Transform _vendorPoint;
    
    public Transform VendorPoint => _vendorPoint;
    
    // File d'attente des clients
    private Queue<Character> _customerQueue = new Queue<Character>();

    /// <summary>Liste des entrées de catalogue (ItemSO + MaxStock).</summary>
    public IReadOnlyList<ShopItemEntry> ShopEntries => _itemsToSell;

    /// <summary>Liste des ItemSO à vendre (raccourci pour la compatibilité).</summary>
    public IReadOnlyList<ItemSO> ItemsToSell => _itemsToSell.Select(e => e.Item).ToList();

    public int CustomersInQueue => _customerQueue.Count;

    protected override void InitializeJobs()
    {
        // Le shop a besoin d'un vendeur au comptoir
        _jobs.Add(new JobVendor());
        
        // Et d'un manager logistique pour passer les commandes de réapprovisionnement
        _jobs.Add(new JobLogisticsManager("Shop Manager"));

        Debug.Log($"<color=magenta>[Shop]</color> {buildingName} initialisé avec 1 Vendeur et 1 LogisticsManager.");
    }



    /// <summary>
    /// Seul le Vendeur va à son poste fixe (_vendorPoint).
    /// Les autres employés (Manager, etc.) utilisent le comportement par défaut (zone du bâtiment).
    /// </summary>
    public override Vector3 GetWorkPosition(Character worker)
    {
        if (worker.CharacterJob != null)
        {
            var currentJob = worker.CharacterJob.CurrentJob;

            if (currentJob is JobVendor && _vendorPoint != null)
                return _vendorPoint.position;
        }

        return base.GetWorkPosition(worker);
    }

    /// <summary>
    /// Récupère le gestionnaire logistique du shop pour y déposer/créer des BuyOrders.
    /// </summary>
    public JobLogisticsManager GetLogisticsManager()
    {
        foreach (var job in _jobs)
        {
            if (job is JobLogisticsManager manager) return manager;
        }
        return null;
    }

    /// <summary>
    /// Récupère le vendeur de ce shop.
    /// </summary>
    public JobVendor GetVendor()
    {
        foreach (var job in _jobs)
        {
            if (job is JobVendor vendor) return vendor;
        }
        return null;
    }

    /// <summary>
    /// Ajoute physiquement un objet à l'inventaire du magasin (ex: par un Transporter).
    /// </summary>
    public void AddToInventory(ItemInstance item)
    {
        if (item != null)
        {
            _inventory.Add(item);
        }
    }

    /// <summary>
    /// Retire et retourne un objet de l'inventaire lors d'une vente.
    /// </summary>
    public ItemInstance SellItem(ItemSO requestedItem)
    {
        var itemInstance = _inventory.Find(i => i.ItemSO == requestedItem);
        if (itemInstance != null)
        {
            _inventory.Remove(itemInstance);
            return itemInstance;
        }
        return null;
    }

    /// <summary>
    /// Vérifie si l'inventaire contient au moins un exemplaire de l'objet demandé.
    /// </summary>
    public bool HasItemInStock(ItemSO item)
    {
        return _inventory.Exists(i => i.ItemSO == item);
    }

    /// <summary>
    /// Retourne le nombre d'exemplaires de cet item dans l'inventaire.
    /// </summary>
    public int GetStockCount(ItemSO item)
    {
        return _inventory.Count(i => i.ItemSO == item);
    }

    /// <summary>
    /// Vérifie si le stock actuel est inférieur au maximum souhaité pour cet item.
    /// </summary>
    public bool NeedsRestock(ItemSO item, int maxStock)
    {
        return GetStockCount(item) < maxStock;
    }

    // ==========================================
    // GESTION DE LA FILE D'ATTENTE (QUEUE)
    // ==========================================

    /// <summary>
    /// Un client s'ajoute à la file d'attente du magasin.
    /// </summary>
    public void JoinQueue(Character customer)
    {
        if (customer != null && !_customerQueue.Contains(customer))
        {
            _customerQueue.Enqueue(customer);
            Debug.Log($"<color=magenta>[Shop]</color> {customer.CharacterName} a rejoint la file d'attente de {buildingName}. (En attente : {_customerQueue.Count})");
        }
    }

    /// <summary>
    /// Appelé par un Vendeur qui est prêt à servir le prochain client.
    /// </summary>
    public Character GetNextCustomer()
    {
        if (_customerQueue.Count > 0)
        {
            return _customerQueue.Dequeue();
        }
        return null;
    }

    /// <summary>
    /// Appelé lorsqu'un Vendeur termine son service. Si aucun autre vendeur n'est dispo,
    /// la file d'attente est entièrement vidée et les clients rentrent chez eux.
    /// </summary>
    public void ClearQueue()
    {
        if (_customerQueue.Count > 0)
        {
            Debug.Log($"<color=magenta>[Shop]</color> Le magasin {buildingName} ferme. {_customerQueue.Count} clients sont priés de rentrer chez eux.");
            
            // Pour tous les clients dans la file, on pourrait déclencher un event ou un changement de statut
            // pour qu'ils sachent qu'ils doivent arrêter de patienter (WaitInQueueBehaviour gérera l'éjection).
            _customerQueue.Clear();
        }
    }
}
