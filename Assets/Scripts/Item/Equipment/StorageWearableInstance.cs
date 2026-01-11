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
    public virtual void InitializeStorage(int miscCapacity, int weaponCapacity)
    {
        // On utilise les noms explicites pour plus de clarté
        _inventory = new Inventory(this, miscCapacity, weaponCapacity);
        Debug.Log($"<color=cyan>[Storage]</color> {ItemSO.ItemName} : " +
                  $"{miscCapacity} Misc Slots, {weaponCapacity} Weapon Slots.");
    }
}