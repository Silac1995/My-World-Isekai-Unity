using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D.Animation;

public class EquipmentLayer : MonoBehaviour
{
    [Header("Current Instances")]
    [SerializeField] protected EquipmentInstance head;
    [SerializeField] protected EquipmentInstance chest;
    [SerializeField] protected EquipmentInstance gloves;
    [SerializeField] protected EquipmentInstance legs;
    [SerializeField] protected EquipmentInstance boots;

    [Header("Sockets (Handlers)")]
    // On remplace les listes par des GameObjects uniques
    [SerializeField] protected GameObject headSocket;
    [SerializeField] protected GameObject chestSocket;
    [SerializeField] protected GameObject glovesSocket;
    [SerializeField] protected GameObject legsSocket;
    [SerializeField] protected GameObject bootsSocket;

    protected Dictionary<WearableType, EquipmentInstance> currentEquipment = new();

    private void Start()
    {
        RefreshAllVisuals();
    }

    public void Equip(EquipmentInstance newInstance)
    {
        if (newInstance == null) return;

        WearableSO data = newInstance.ItemSO as WearableSO;
        if (data == null) return;

        WearableType type = data.WearableType;

        // 1. Logique de données
        Unequip(type);
        SetInstance(type, newInstance);

        // 2. Logique Visuelle
        RefreshSlotVisual(type);
    }

    public void Unequip(WearableType type)
    {
        SetInstance(type, null);

        GameObject socket = GetSocket(type);
        if (socket != null) socket.SetActive(false);

        // Pas besoin de rafraîchir tout le visuel ici, le SetActive(false) suffit
    }
    private void RefreshSlotVisual(WearableType type)
    {
        EquipmentInstance currentItem = GetInstance(type);
        GameObject socket = GetSocket(type);

        if (socket == null) return;

        bool hasItem = currentItem != null;
        socket.SetActive(hasItem);

        if (hasItem)
        {
            // On cherche le handler de base. Peu importe que ce soit Chest ou Pants !
            if (socket.TryGetComponent(out WearableHandlerBase handler))
            {
                handler.Initialize(currentItem.ItemSO.SpriteLibraryAsset);
                handler.SetLibraryCategory(currentItem.ItemSO.CategoryName);

                if (currentItem.HavePrimaryColor())
                    handler.SetPrimaryColor(currentItem.PrimaryColor);

                if (currentItem.HaveSecondaryColor())
                    handler.SetSecondaryColor(currentItem.SecondaryColor);
            }
            else
            {
                // Sécurité pour les items sans script Handler (ex: chapeau simple)
                ApplyGenericVisuals(socket, currentItem);
            }
        }
    }

    private void ApplyGenericVisuals(GameObject socket, EquipmentInstance item)
    {
        // On traite le parent et ses enfants Line/Color_Main/etc.
        SpriteResolver[] resolvers = socket.GetComponentsInChildren<SpriteResolver>(true);
        foreach (var res in resolvers)
        {
            res.SetCategoryAndLabel(item.ItemSO.CategoryName, res.GetLabel());
            res.ResolveSpriteToSpriteRenderer();
        }

        // Application de la couleur sur les enfants spécifiques
        if (item.HavePrimaryColor())
        {
            foreach (Transform child in socket.transform)
            {
                if (child.name == "Color_Primary" && child.TryGetComponent(out SpriteRenderer sr))
                    sr.color = item.PrimaryColor;
            }
        }
    }

    // --- Helpers de recherche ---

    private GameObject GetSocket(WearableType type) => type switch
    {
        WearableType.Helmet => headSocket,
        WearableType.Armor => chestSocket,
        WearableType.Gloves => glovesSocket,
        WearableType.Pants => legsSocket,
        WearableType.Boots => bootsSocket,
        _ => null
    };

    public EquipmentInstance GetInstance(WearableType type) => type switch
    {
        WearableType.Helmet => head,
        WearableType.Armor => chest,
        WearableType.Gloves => gloves,
        WearableType.Pants => legs,
        WearableType.Boots => boots,
        _ => null
    };

    private void SetInstance(WearableType type, EquipmentInstance inst)
    {
        if (inst == null)
            currentEquipment.Remove(type);
        else
            currentEquipment[type] = inst;

        switch (type)
        {
            case WearableType.Helmet: head = inst; break;
            case WearableType.Armor: chest = inst; break;
            case WearableType.Gloves: gloves = inst; break;
            case WearableType.Pants: legs = inst; break;
            case WearableType.Boots: boots = inst; break;
        }
    }

    /// <summary>
    /// Synchronise l'état visuel de tous les sockets avec les instances actuellement équipées.
    /// Utile au spawn ou après un chargement.
    /// </summary>
    public void RefreshAllVisuals()
    {
        Debug.Log($"<color=cyan>[Refresh]</color> Rafraîchissement visuel complet pour <b>{gameObject.name}</b>");

        // On boucle sur toutes les valeurs de l'Enum EquipmentType
        foreach (WearableType type in System.Enum.GetValues(typeof(WearableType)))
        {
            RefreshSlotVisual(type);
        }
    }

    public bool IsAlreadyEquipped(EquipmentInstance newInstance)
    {
        if (newInstance.ItemSO is WearableSO data)
        {
            // On vérifie si le slot (ex: Helmet) contient déjà cette instance précise
            if (currentEquipment.ContainsKey(data.WearableType))
            {
                return currentEquipment[data.WearableType] == newInstance;
            }
        }
        return false;
    }
        
}