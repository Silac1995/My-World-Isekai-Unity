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

    [Header("Sockets (2D Animation Containers)")]
    [SerializeField] protected List<GameObject> headSockets;
    [SerializeField] protected List<GameObject> chestSockets;
    [SerializeField] protected List<GameObject> glovesSockets;
    [SerializeField] protected List<GameObject> legsSockets;
    [SerializeField] protected List<GameObject> bootsSockets;

    protected System.Collections.Generic.Dictionary<WearableType, EquipmentInstance> currentEquipment = new();

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

        // --- LOGIQUE DE SET ---
        Unequip(type);
        SetInstance(type, newInstance);

        // --- LOGIQUE VISUELLE ---
        List<GameObject> targetList = GetSocketList(type);

        if (targetList == null || targetList.Count == 0)
        {
            Debug.LogWarning($"[EquipmentLayer] Aucun socket trouvé pour le type {type} sur {gameObject.name}");
            return;
        }

        foreach (GameObject socket in targetList)
        {
            if (socket == null) continue;

            socket.SetActive(true);

            // 1. Mise à jour du Sprite (Catégorie du sac dans la Library)
            if (socket.TryGetComponent(out SpriteResolver resolver))
            {
                // On utilise le CategoryName défini dans le BagSO/EquipmentSO
                resolver.SetCategoryAndLabel(data.CategoryName, resolver.GetLabel());
            }

            // 2. Application de la couleur
            if (socket.TryGetComponent(out SpriteRenderer sRenderer))
            {
                // On vérifie si c'est une instance de sac pour la couleur ou une instance normale
                sRenderer.color = newInstance.HavePrimaryColor() ? newInstance.PrimaryColor : Color.white;
            }
        }

        RefreshAllVisuals();
    }

    public void Unequip(WearableType type)
    {
        EquipmentInstance oldItem = GetInstance(type);

        if (oldItem != null)
        {
            Debug.Log($"<color=orange>[Unequip]</color> Retrait de <b>{oldItem.ItemSO.ItemName}</b> du slot <b>{type}</b>");
        }

        SetInstance(type, null);
        ToggleSockets(type, false);
        RefreshAllVisuals();
    }

    private void ToggleSockets(WearableType type, bool isActive)
    {
        List<GameObject> targetList = GetSocketList(type);
        if (targetList == null) return;

        foreach (GameObject socket in targetList)
        {
            if (socket != null) socket.SetActive(isActive);
        }
    }

    // --- Helpers de recherche ---

    private List<GameObject> GetSocketList(WearableType type) => type switch
    {
        WearableType.Helmet => headSockets,
        WearableType.Armor => chestSockets,
        WearableType.Gloves => glovesSockets,
        WearableType.Pants => legsSockets,
        WearableType.Boots => bootsSockets,
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

    private void RefreshSlotVisual(WearableType type)
    {
        EquipmentInstance currentItem = GetInstance(type);
        List<GameObject> sockets = GetSocketList(type);

        if (sockets == null || sockets.Count == 0) return;

        bool hasItem = currentItem != null;

        foreach (GameObject socket in sockets)
        {
            if (socket == null) continue;
            socket.SetActive(hasItem);

            if (hasItem && socket.TryGetComponent(out SpriteRenderer sRenderer))
            {
                // Appliquer la couleur si elle existe, sinon blanc
                sRenderer.color = currentItem.HavePrimaryColor() ? currentItem.PrimaryColor: Color.white;

                if (socket.TryGetComponent(out SpriteResolver resolver))
                {
                    resolver.SetCategoryAndLabel(currentItem.ItemSO.CategoryName, resolver.GetLabel());
                }
            }
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