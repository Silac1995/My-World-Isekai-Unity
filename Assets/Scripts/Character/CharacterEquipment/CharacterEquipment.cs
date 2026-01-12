using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D.Animation;

public class CharacterEquipment : MonoBehaviour
{
    [SerializeField] private Character character;
    // Cet événement sera déclenché chaque fois qu'un équipement change
    public event Action OnEquipmentChanged;

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
        // 1. GESTION DES ARMES
        if (itemInstance is WeaponInstance weapon)
        {
            EquipWeapon(weapon);
            OnEquipmentChanged?.Invoke();
            return;
        }

        // 2. GESTION DES WEARABLES (Sacs inclus)
        if (itemInstance is WearableInstance wearable)
        {
            // On récupère le SO typé (WearableSO ou BagSO qui en hérite)
            if (wearable.ItemSO is WearableSO data)
            {
                // --- CAS PARTICULIER : LE SAC ---
                // On vérifie soit le type d'enum, soit la classe de l'instance
                if (data.WearableType == WearableType.Bag || wearable is BagInstance)
                {
                    if (wearable is BagInstance bag)
                    {
                        EquipBag(bag);
                        OnEquipmentChanged?.Invoke();
                    }
                    else
                    {
                        Debug.LogError($"[Equip] L'item {data.ItemName} est marqué comme Bag mais l'instance n'est pas un BagInstance!");
                    }
                    return;
                }

                // --- CAS GÉNÉRAL : COUCHES D'ÉQUIPEMENT ---
                EquipmentLayer targetLayer = GetTargetLayer(data.EquipmentLayer);

                if (targetLayer != null)
                {
                    if (targetLayer.IsAlreadyEquipped(wearable)) return;

                    Debug.Log($"<color=green>[Equip]</color> {data.ItemName} vers {data.EquipmentLayer}");
                    targetLayer.Equip(wearable);
                    OnEquipmentChanged?.Invoke();
                }
            }
        }
    }

    // Petite méthode pour préparer la suite (Gestion des mains gauche/droite par ex)
    private void EquipWeapon(WeaponInstance weapon)
    {
        Debug.Log($"<color=red>[Equip-Weapon]</color> {weapon.ItemSO.ItemName} équipée !");
        // Ta logique d'équipement d'arme viendra ici (ex: weaponLayer.Equip(weapon))
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
                SpriteResolver[] resolvers = socket.GetComponentsInChildren<SpriteResolver>();
                foreach (var res in resolvers)
                {
                    res.SetCategoryAndLabel(_bag.ItemSO.CategoryName, res.GetLabel());
                }

                // 2. Mise à jour sélective des couleurs Primary et Secondary uniquement
                SpriteRenderer[] renderers = socket.GetComponentsInChildren<SpriteRenderer>();

                foreach (SpriteRenderer sRenderer in renderers)
                {
                    string goName = sRenderer.gameObject.name;

                    // On ne modifie QUE les GameObjects nommés spécifiquement
                    if (goName == "Color_Primary")
                    {
                        if (_bag.HavePrimaryColor())
                        {
                            sRenderer.color = _bag.PrimaryColor;
                        }
                    }
                    else if (goName == "Color_Secondary")
                    {
                        if (_bag.HaveSecondaryColor())
                        {
                            sRenderer.color = _bag.SecondaryColor;
                        }
                    }
                    // Color_Main, MainColor et Line sont ignorés et gardent leur couleur d'origine
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

    public void Unequip(WearableLayerEnum layerType, WearableType slotType)
    {
        if (slotType == WearableType.Bag || layerType == WearableLayerEnum.Bag)
        {
            UnequipBag();
            OnEquipmentChanged?.Invoke();
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
            OnEquipmentChanged?.Invoke();

            // 3. On fait tomber l'instance qu'on a sauvegardée
            character.DropItem(instanceToDrop);

            Debug.Log($"<color=orange>[Unequip]</color> {instanceToDrop.ItemSO.ItemName} retiré et jeté.");
        }
    }

    // Logique basée sur l'Enum EquipmentLayerEnum
    private EquipmentLayer GetTargetLayer(WearableLayerEnum layerType)
    {
        switch (layerType)
        {
            case WearableLayerEnum.Underwear:
                return underwearLayer;
            case WearableLayerEnum.Clothing:
                return clothingLayer;
            case WearableLayerEnum.Armor:
                return armorLayer;
            case WearableLayerEnum.Bag:
                return null; // Le sac n'a pas de composant EquipmentLayer dédié
            default:
                return null;
        }
    }

    /// <summary>
    /// Vérifie si le personnage possède actuellement un conteneur équipé (Sac).
    /// </summary>
    public bool HaveInventory()
    {
        // On vérifie si le sac existe ET s'il possède bien un inventaire initialisé
        return _bag != null && _bag.Inventory != null;
    }

    /// <summary>
    /// Retourne l'inventaire du sac équipé. Renvoie null si aucun sac n'est présent.
    /// </summary>
    public Inventory GetInventory()
    {
        if (!HaveInventory())
        {
            return null;
        }

        return _bag.Inventory;
    }

    /// <summary>
    /// Vérifie si le torse/poitrine du personnage est exposé.
    /// Retourne True si aucun vêtement de type "Shirt" n'est équipé dans les 3 couches.
    /// </summary>
    public bool IsChestExposed()
    {
        bool hasUnderwearShirt = underwearLayer != null && underwearLayer.GetInstance(WearableType.Armor) != null;
        bool hasClothingShirt = clothingLayer != null && clothingLayer.GetInstance(WearableType.Armor) != null;
        bool hasArmorShirt = armorLayer != null && armorLayer.GetInstance(WearableType.Armor) != null;

        // Exposé si aucune couche n'a de Shirt
        return !hasUnderwearShirt && !hasClothingShirt && !hasArmorShirt;
    }

    /// <summary>
    /// Vérifie si les parties intimes inférieures sont exposées.
    /// Retourne True si aucun vêtement de type "Pants" n'est équipé dans les 3 couches.
    /// Note : C'est techniquement identique à ta logique actuelle de IsNaked().
    /// </summary>
    public bool IsGroinExposed()
    {
        // On réutilise la logique des Pants
        bool hasUnderwearPants = underwearLayer != null && underwearLayer.GetInstance(WearableType.Pants) != null;
        bool hasClothingPants = clothingLayer != null && clothingLayer.GetInstance(WearableType.Pants) != null;
        bool hasArmorPants = armorLayer != null && armorLayer.GetInstance(WearableType.Pants) != null;

        return !hasUnderwearPants && !hasClothingPants && !hasArmorPants;
    }

    /// <summary>
    /// Vérifie la nudité totale (Haut et Bas).
    /// </summary>
    public bool IsFullyNaked()
    {
        return IsChestExposed() && IsGroinExposed();
    }

    /// <summary>
    /// Vérifie si le personnage est "nu" au niveau des jambes.
    /// Retourne True si aucun pantalon n'est équipé dans les 3 couches (Underwear, Clothing, Armor).
    /// </summary>
    public bool IsNaked()
    {
        // On vérifie le slot Pants dans chaque couche.
        // Si l'une des couches retourne une instance non nulle, le perso n'est pas nu.
        bool hasUnderwearPants = underwearLayer != null && underwearLayer.GetInstance(WearableType.Pants) != null;
        bool hasClothingPants = clothingLayer != null && clothingLayer.GetInstance(WearableType.Pants) != null;
        bool hasArmorPants = armorLayer != null && armorLayer.GetInstance(WearableType.Pants) != null;

        // Le personnage est nu s'il n'a AUCUN des trois
        return !hasUnderwearPants && !hasClothingPants && !hasArmorPants;
    }
}