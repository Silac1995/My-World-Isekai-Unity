using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Bâtiment de type Shop.
/// Nécessite un Vendeur pour vendre ses produits aux clients et un LogisticsManager pour le réapprovisionner.
/// Gère également la file d'attente (Queue) des clients.
/// </summary>
public class ShopBuilding : CommercialBuilding
{
    public override BuildingType BuildingType => BuildingType.Shop;

    [Header("Shop Settings")]
    [SerializeField] private List<ItemSO> _itemsToSell = new List<ItemSO>();
    
    // Inventaire réel du magasin
    private List<ItemInstance> _inventory = new List<ItemInstance>();

    // File d'attente des clients
    private Queue<Character> _customerQueue = new Queue<Character>();

    public IReadOnlyList<ItemSO> ItemsToSell => _itemsToSell;
    public IReadOnlyList<ItemInstance> Inventory => _inventory;
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
        var itemInstance = _inventory.Find(i => i.ItemData == requestedItem);
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
        return _inventory.Exists(i => i.ItemData == item);
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
