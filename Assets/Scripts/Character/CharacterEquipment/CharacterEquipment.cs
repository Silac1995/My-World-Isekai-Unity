using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D.Animation;

public class CharacterEquipment : MonoBehaviour
{
    [SerializeField] private Character character;

    public Character Character
    {
        get => character;
        set => character = value;
    }

    // Tes couches assignées manuellement via [SerializeReference]
    [SerializeReference] private UnderwearLayer underwearLayer;
    [SerializeReference] private ClothingLayer clothingLayer;
    [SerializeReference] private ArmorLayer armorLayer;
    // --- NOUVELLES VARIABLES POUR LE SAC ---
    [Header("Global Accessories")]
    [SerializeField] private BagInstance _bag;
    [SerializeField] private List<GameObject> _bagSockets;

    // Getters publics
    public UnderwearLayer UnderwearLayer => underwearLayer;
    public ClothingLayer ClothingLayer => clothingLayer;
    public ArmorLayer ArmorLayer => armorLayer;
    public bool HasBagEquipped() => _bag != null;
    public BagInstance GetBagInstance() => _bag;

    private void Start()
    {
        // On initialise le visuel du sac dès le début
        // Si _bag est null dans l'inspecteur, il cachera les sockets.
        // Si tu as mis un sac par défaut dans l'inspecteur, il l'affichera correctement.
        UpdateBagVisual(_bag != null);
    }

    /// <summary>
    /// Force la désactivation visuelle de tous les sockets du sac.
    /// Utile si tu veux vider le visuel sans toucher à la donnée.
    /// </summary>
    public void DisableBagVisuals()
    {
        UpdateBagVisual(false);
    }

    public void Equip(ItemInstance itemInstance)
    {
        // 1. CAS PARTICULIER : LE SAC
        if (itemInstance is BagInstance bagInstance)
        {
            EquipBag(bagInstance);
            return;
        }

        // 2. CAS GÉNÉRAL : LES COUCHES D'ÉQUIPEMENT
        if (itemInstance is EquipmentInstance equipmentInstance &&
            equipmentInstance.ItemSO is EquipmentSO equipmentData)
        {
            EquipmentLayer targetLayer = GetTargetLayer(equipmentData.EquipmentLayer);

            if (targetLayer != null)
            {
                if (targetLayer.IsAlreadyEquipped(equipmentInstance)) return;

                Debug.Log($"<color=green>[Equip]</color> Envoi de {equipmentData.ItemName} vers {equipmentData.EquipmentLayer}");
                targetLayer.Equip(equipmentInstance);
            }
        }
    }

    private void EquipBag(BagInstance newBag)
    {
        // Si un sac est déjà équipé, on pourrait le déséquiper ici
        if (_bag != null)
        {
            // Logique pour remettre l'ancien sac dans l'inventaire ou au sol
        }

        _bag = newBag;
        UpdateBagVisual(true);
        Debug.Log($"<color=green>[Equip-Bag]</color> {newBag.ItemSO.ItemName} équipé sur le slot global.");
    }

    /// <summary>
    /// Retire le sac actuel, met à jour le visuel et fait apparaître l'item au sol.
    /// </summary>
    public void UnequipBag()
    {
        if (_bag == null)
        {
            Debug.LogWarning("[Unequip] Aucun sac n'est équipé.");
            return;
        }

        Debug.Log($"<color=orange>[Unequip-Bag]</color> Retrait de : <b>{_bag.ItemSO.ItemName}</b>");

        // 1. On demande au personnage de faire tomber l'item physiquement dans le monde
        // Cette méthode doit gérer le spawn du prefab WorldItem avec l'instance _bag
        character.DropItem(_bag);

        // 2. On nettoie la référence et on cache les visuels
        _bag = null;
        UpdateBagVisual(false);
    }

    private void UpdateBagVisual(bool show)
    {
        if (_bagSockets == null || _bagSockets.Count == 0) return;

        bool shouldActuallyShow = show && _bag != null;

        foreach (GameObject socket in _bagSockets)
        {
            if (socket == null) continue;

            socket.SetActive(shouldActuallyShow);

            if (shouldActuallyShow)
            {
                // 1. Mise à jour des Sprites (Resolvers)
                // On en boucle aussi au cas où il y en aurait plusieurs
                SpriteResolver[] resolvers = socket.GetComponentsInChildren<SpriteResolver>();
                foreach (var res in resolvers)
                {
                    res.SetCategoryAndLabel(_bag.ItemSO.CategoryName, res.GetLabel());
                }

                // 2. Mise à jour de TOUTES les couleurs
                // On récupère TOUS les SpriteRenderers (corps, lanières, etc.)
                SpriteRenderer[] renderers = socket.GetComponentsInChildren<SpriteRenderer>();

                Color targetColor = _bag.HavePrimaryColor() ? _bag.PrimaryColor : Color.white;

                foreach (SpriteRenderer sRenderer in renderers)
                {
                    sRenderer.color = targetColor;
                    // Debug.Log($"[Visual] Couleur appliquée sur {sRenderer.gameObject.name}");
                }
            }
        }
    }

    /// <summary>
    /// Retire un équipement spécifique en fonction de sa couche (Layer) et de son emplacement (Slot).
    /// Gère la destruction de l'instance, la libération du slot et la mise à jour visuelle.
    /// </summary>
    /// <param name="layerType">La couche concernée (Underwear, Clothing, Armor).</param>
    /// <param name="slotType">La partie du corps à libérer (Helmet, Armor, Boots, etc.).</param>
    /// 

    public void Unequip(EquipmentLayerEnum layerType, EquipmentType slotType)
    {
        if (slotType == EquipmentType.Bag || layerType == EquipmentLayerEnum.Bag)
        {
            UnequipBag();
            return;
        }

        EquipmentLayer targetLayer = GetTargetLayer(layerType);

        if (targetLayer != null)
        {
            // 1. On récupère l'instance AVANT de vider le slot
            EquipmentInstance instanceToDrop = targetLayer.GetInstance(slotType);

            if (instanceToDrop == null) return;

            // 2. On vide le slot (visuel + data)
            targetLayer.Unequip(slotType);

            // 3. On fait tomber l'instance qu'on a sauvegardée
            character.DropItem(instanceToDrop);

            Debug.Log($"<color=orange>[Unequip]</color> {instanceToDrop.ItemSO.ItemName} retiré et jeté.");
        }
    }

    // Logique basée sur l'Enum EquipmentLayerEnum
    private EquipmentLayer GetTargetLayer(EquipmentLayerEnum layerType)
    {
        switch (layerType)
        {
            case EquipmentLayerEnum.Underwear:
                return underwearLayer;
            case EquipmentLayerEnum.Clothing:
                return clothingLayer;
            case EquipmentLayerEnum.Armor:
                return armorLayer;
            case EquipmentLayerEnum.Bag:
                return null; // Le sac n'a pas de composant EquipmentLayer dédié
            default:
                return null;
        }
    }
}