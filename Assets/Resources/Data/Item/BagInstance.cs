using System.Collections.Generic;
using UnityEngine;

public class BagInstance : EquipmentInstance
{
    [Header("Runtime Storage")]
    [SerializeField] private List<ItemSlot> itemSlots = new List<ItemSlot>();

    // Getter pour accéder au contenu du sac depuis l'inventaire ou l'UI
    public List<ItemSlot> ItemSlots => itemSlots;


    public BagInstance(ItemSO data) : base(data)
    {
    }

    /// <summary>
    /// Initialise la capacité. 
    /// Si 'customCapacity' est égal à 0 ou moins, on utilise la capacité du BagSO.
    /// </summary>
    /// <param name="customCapacity">Capacité basée sur le niveau de l'artisan</param>
    /// 
    public void InitializeBagCapacity(int customCapacity = 0)
    {
        if (ItemSO is BagSO bagData)
        {
            // Déterminer la capacité finale
            // Si l'artisan a boosté le sac, on prend customCapacity, sinon celle du SO
            int finalCapacity = (customCapacity > 0) ? customCapacity : bagData.Capacity;

            // On initialise les slots
            itemSlots = new List<ItemSlot>(finalCapacity);

            for (int i = 0; i < finalCapacity; i++)
            {
                itemSlots.Add(new ItemSlot());
            }

            Debug.Log($"[BagInstance] {CustomizedName} créé avec une capacité de {finalCapacity} " +
                      (customCapacity > 0 ? "(Bonus Artisan appliqué!)" : "(Valeur par défaut)"));
        }
        else
        {
            Debug.LogError($"[BagInstance] L'ItemSO de {this.CustomizedName} n'est pas un BagSO !");
        }
    }
}