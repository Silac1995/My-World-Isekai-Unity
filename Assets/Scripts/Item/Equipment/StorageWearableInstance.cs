using UnityEngine;

[System.Serializable]
public abstract class StorageWearableInstance : WearableInstance
{
    [Header("Runtime Storage")]
    [SerializeField] protected Inventory _inventory;

    public Inventory Inventory => _inventory;

    protected StorageWearableInstance(ItemSO data) : base(data)
    {
    }

    /// <summary>
    /// Initialise l'inventaire. Cette méthode est commune à tous les objets de stockage.
    /// </summary>
    public virtual void InitializeStorage(int capacity)
    {
        // On passe 'this' en tant que StorageWearableInstance
        _inventory = new Inventory(this, capacity);
        Debug.Log($"<color=cyan>[Storage]</color> {ItemSO.ItemName} initialisé avec {capacity} slots.");
    }
}