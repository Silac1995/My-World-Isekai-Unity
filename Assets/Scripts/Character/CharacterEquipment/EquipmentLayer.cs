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
    // We replace the lists with unique GameObjects
    [SerializeField] protected GameObject headSocket;
    [SerializeField] protected GameObject chestSocket;
    [SerializeField] protected GameObject glovesSocket;
    [SerializeField] protected GameObject legsSocket;
    [SerializeField] protected GameObject bootsSocket;

    protected Dictionary<WearableType, EquipmentInstance> currentEquipment = new();

    private void Start()
    {
        // We make sure that ALL sockets are instantiated with their correct m_BindPoses
        // while visually hiding the base equipment.
        HideAllSockets();
        RefreshAllVisuals();
    }

    private void HideAllSockets()
    {
        ToggleSocketVisibility(headSocket, false);
        ToggleSocketVisibility(chestSocket, false);
        ToggleSocketVisibility(glovesSocket, false);
        ToggleSocketVisibility(legsSocket, false);
        ToggleSocketVisibility(bootsSocket, false);
    }

    public void Equip(EquipmentInstance newInstance)
    {
        if (newInstance == null) return;

        WearableSO data = newInstance.ItemSO as WearableSO;
        if (data == null) return;

        WearableType type = data.WearableType;

        // 1. Data logic
        Unequip(type);
        SetInstance(type, newInstance);

        // 2. Visual logic
        RefreshSlotVisual(type);
    }

    public void Unequip(WearableType type)
    {
        SetInstance(type, null);

        GameObject socket = GetSocket(type);
        if (socket != null) ToggleSocketVisibility(socket, false);
    }
    private void RefreshSlotVisual(WearableType type)
    {
        EquipmentInstance currentItem = GetInstance(type);
        GameObject socket = GetSocket(type);

        if (socket == null) return;

        bool hasItem = currentItem != null;
        ToggleSocketVisibility(socket, hasItem);

        if (hasItem)
        {
            // We look for the base handler. It doesn't matter if it's Chest or Pants!
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
                // Safety for items without a Handler script (e.g. simple hat)
                ApplyGenericVisuals(socket, currentItem);
            }
        }
    }

    private void ApplyGenericVisuals(GameObject socket, EquipmentInstance item)
    {
        // We process the parent and its Line/Color_Main/etc. children
        SpriteResolver[] resolvers = socket.GetComponentsInChildren<SpriteResolver>(true);
        foreach (var res in resolvers)
        {
            res.SetCategoryAndLabel(item.ItemSO.CategoryName, res.GetLabel());
            res.ResolveSpriteToSpriteRenderer();
        }

        // Apply the color to the specific children
        if (item.HavePrimaryColor())
        {
            foreach (Transform child in socket.transform)
            {
                if (child.name == "Color_Primary" && child.TryGetComponent(out SpriteRenderer sr))
                    sr.color = item.PrimaryColor;
            }
        }
    }

    private void ToggleSocketVisibility(GameObject socket, bool isVisible)
    {
        if (socket == null) return;

        // FIX (Offset Bug): Instead of disabling the GameObject (which breaks the SpriteSkin of scaled NPCs),
        // we only disable the SpriteRenderers
        if (socket.TryGetComponent(out WearableHandlerBase handler))
        {
            handler.SetVisibility(isVisible);
        }
        else
        {
            // Fallback if there is no handler
            SpriteRenderer[] renderers = socket.GetComponentsInChildren<SpriteRenderer>(true);
            foreach (var sr in renderers)
            {
                sr.enabled = isVisible;
            }
        }
    }

    // --- Lookup helpers ---

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
    /// Synchronizes the visual state of all sockets with the currently equipped instances.
    /// Useful at spawn or after a load.
    /// </summary>
    public void RefreshAllVisuals()
    {
        Debug.Log($"<color=cyan>[Refresh]</color> Full visual refresh for <b>{gameObject.name}</b>");

        // We loop over all the values of the EquipmentType Enum
        foreach (WearableType type in System.Enum.GetValues(typeof(WearableType)))
        {
            RefreshSlotVisual(type);
        }
    }

    public bool IsAlreadyEquipped(EquipmentInstance newInstance)
    {
        if (newInstance.ItemSO is WearableSO data)
        {
            // We check if the slot (e.g. Helmet) already contains this exact instance
            if (currentEquipment.ContainsKey(data.WearableType))
            {
                return currentEquipment[data.WearableType] == newInstance;
            }
        }
        return false;
    }
        
}
