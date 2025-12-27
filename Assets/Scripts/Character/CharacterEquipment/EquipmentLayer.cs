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
    [SerializeField] protected BagInstance bag; // Ajouté

    [Header("Sockets (2D Animation Containers)")]
    [SerializeField] protected List<GameObject> headSockets;
    [SerializeField] protected List<GameObject> chestSockets;
    [SerializeField] protected List<GameObject> glovesSockets;
    [SerializeField] protected List<GameObject> legsSockets;
    [SerializeField] protected List<GameObject> bootsSockets;
    [SerializeField] protected List<GameObject> bagSockets; // Ajouté

    protected System.Collections.Generic.Dictionary<EquipmentType, EquipmentInstance> currentEquipment = new();

    private void Start()
    {
        RefreshAllVisuals();
    }

    public void Equip(EquipmentInstance newInstance)
    {
        // --- SECURITÉ DOUBLON ---
        if (IsAlreadyEquipped(newInstance))
        {
            Debug.Log($"[EquipmentLayer] {newInstance.ItemSO.ItemName} est déjà porté sur {gameObject.name}.");
            return;
        }

        EquipmentSO data = newInstance.ItemSO as EquipmentSO;
        if (data == null)
        {
            Debug.LogError($"[EquipmentLayer] L'objet {newInstance.CustomizedName} n'a pas de EquipmentSO valide !");
            return;
        }

        EquipmentType type = data.EquipmentType;

        Debug.Log($"<color=green>[Equip]</color> Tentative d'équipement de <b>{data.ItemName}</b> dans le slot <b>{type}</b> dans le gameobject <b>{gameObject.name}</b>");

        Unequip(type);
        SetInstance(type, newInstance);

        List<GameObject> targetList = GetSocketList(type);
        if (targetList == null || targetList.Count == 0)
        {
            Debug.LogWarning($"[EquipmentLayer] Aucun Socket trouvé pour le type {type} sur {gameObject.name} !");
            return;
        }

        foreach (GameObject socket in targetList)
        {
            if (socket == null) continue;

            socket.SetActive(true);

            // 1. Gestion du visuel (SpriteResolver)
            if (socket.TryGetComponent(out SpriteResolver resolver))
            {
                string currentLabel = resolver.GetLabel();
                resolver.SetCategoryAndLabel(data.CategoryName, currentLabel);
            }

            // 2. Gestion de la couleur personnalisée (SpriteRenderer)
            if (socket.TryGetComponent(out SpriteRenderer sRenderer))
            {
                if (newInstance.HaveCustomizedColor())
                {
                    sRenderer.color = newInstance.CustomizedColor;
                    Debug.Log($"[Visual] Couleur personnalisée appliquée au socket : {socket.name}");
                }
                else
                {
                    // On remet en blanc si l'item n'a pas de couleur spéciale
                    sRenderer.color = Color.white;
                }
            }
        }
        RefreshAllVisuals();
    }

    public void Unequip(EquipmentType type)
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

    private void ToggleSockets(EquipmentType type, bool isActive)
    {
        List<GameObject> targetList = GetSocketList(type);
        if (targetList == null) return;

        foreach (GameObject socket in targetList)
        {
            if (socket != null) socket.SetActive(isActive);
        }
    }

    // --- Helpers de recherche ---

    private List<GameObject> GetSocketList(EquipmentType type) => type switch
    {
        EquipmentType.Helmet => headSockets,
        EquipmentType.Armor => chestSockets,
        EquipmentType.Gloves => glovesSockets,
        EquipmentType.Pants => legsSockets,
        EquipmentType.Boots => bootsSockets,
        EquipmentType.Bag => bagSockets, // Ajouté
        _ => null
    };

    public EquipmentInstance GetInstance(EquipmentType type) => type switch
    {
        EquipmentType.Helmet => head,
        EquipmentType.Armor => chest,
        EquipmentType.Gloves => gloves,
        EquipmentType.Pants => legs,
        EquipmentType.Boots => boots,
        EquipmentType.Bag => bag, // Ajouté
        _ => null
    };

    private void SetInstance(EquipmentType type, EquipmentInstance inst)
    {
        // 1. Mise à jour du dictionnaire pour IsAlreadyEquipped
        if (inst == null)
            currentEquipment.Remove(type);
        else
            currentEquipment[type] = inst;

        // 2. Mise à jour des variables pour l'inspecteur
        switch (type)
        {
            case EquipmentType.Helmet: head = inst; break;
            case EquipmentType.Armor: chest = inst; break;
            case EquipmentType.Gloves: gloves = inst; break;
            case EquipmentType.Pants: legs = inst; break;
            case EquipmentType.Boots: boots = inst; break;
            case EquipmentType.Bag: bag = inst as BagInstance; break;
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
        foreach (EquipmentType type in System.Enum.GetValues(typeof(EquipmentType)))
        {
            RefreshSlotVisual(type);
        }
    }

    private void RefreshSlotVisual(EquipmentType type)
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
                sRenderer.color = currentItem.HaveCustomizedColor() ? currentItem.CustomizedColor : Color.white;

                if (socket.TryGetComponent(out SpriteResolver resolver))
                {
                    resolver.SetCategoryAndLabel(currentItem.ItemSO.CategoryName, resolver.GetLabel());
                }
            }
        }
    }

    public bool IsAlreadyEquipped(EquipmentInstance newInstance)
    {
        if (newInstance.ItemSO is EquipmentSO data)
        {
            // On vérifie si le slot (ex: Helmet) contient déjà cette instance précise
            if (currentEquipment.ContainsKey(data.EquipmentType))
            {
                return currentEquipment[data.EquipmentType] == newInstance;
            }
        }
        return false;
    }
        
}