using System.Collections.Generic;
using UnityEngine;

public class BagInstance : EquipmentInstance
{
    [Header("Runtime Storage")]
    [SerializeField] private Inventory _inventory;

    // Getter pour accéder à l'inventaire global du sac
    public Inventory Inventory => _inventory;

    public BagInstance(ItemSO data) : base(data)
    {
    }

    /// <summary>
    /// Initialise l'inventaire du sac en déléguant la création des slots à la classe Inventory.
    /// </summary>
    public void InitializeBagCapacity(int customCapacity = 0)
    {
        if (ItemSO is BagSO bagData)
        {
            // 1. Déterminer la capacité finale
            int finalCapacity = (customCapacity > 0) ? customCapacity : bagData.Capacity;

            // 2. Créer l'objet Inventory
            // Le constructeur d'Inventory appellera lui-même InitializeItemSlots(finalCapacity)
            _inventory = new Inventory(this, finalCapacity);

            Debug.Log($"<color=cyan>[BagInstance]</color> {ItemSO.ItemName} : Inventaire généré ({finalCapacity} slots).");
        }
        else
        {
            Debug.LogError($"<color=red>[BagInstance Error]</color> L'ItemSO de {CustomizedName} n'est pas un BagSO !");
        }
    }
}