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

    [Header("Combat")]
    [SerializeField] private WeaponInstance _weapon;
    [SerializeField] private GameObject _weaponSocket; // Le point d'attache visuel de l'arme

    public WeaponInstance CurrentWeapon => _weapon;

    // Tes couches assignées manuellement via [SerializeReference]
    [SerializeReference] private UnderwearLayer underwearLayer;
    [SerializeReference] private ClothingLayer clothingLayer;
    [SerializeReference] private ArmorLayer armorLayer;
    // --- NOUVELLES VARIABLES POUR LE SAC ---
    [Header("Global Accessories")]
    [SerializeField] private Bag _bagScript;
    [SerializeField] private BagInstance _bag;
    [SerializeField] private List<GameObject> _bagSockets;

    // Getters publics
    public UnderwearLayer UnderwearLayer => underwearLayer;
    public ClothingLayer ClothingLayer => clothingLayer;
    public ArmorLayer ArmorLayer => armorLayer;
    public bool HasBagEquipped() => _bag != null;
    public BagInstance GetBagInstance() => _bag;
    public Bag BagScript => _bagScript;

    private void Start()
    {
        UpdateBagVisual(_bag != null);
        UpdateWeaponVisual(); // Cette méthode gère déjà le cas null proprement maintenant
    }

    /// <summary>
    /// Met à jour l'état visuel du socket d'arme et informe le système de combat.
    /// </summary>
    private void UpdateWeaponVisual()
    {
        // On considère qu'on a une arme UNIQUEMENT si l'instance ET son SO existent
        bool hasValidWeapon = _weapon != null && _weapon.ItemSO != null;

        // 1. GESTION DU SOCKET VISUEL
        if (_weaponSocket != null)
        {
            // Si l'arme est invalide ou absente, on DISABLE le socket
            _weaponSocket.SetActive(hasValidWeapon);

            if (hasValidWeapon)
            {
                SyncWeaponVisualToSocket();
            }
        }

        // 2. GESTION DE LA LOGIQUE DE COMBAT
        if (character != null)
        {
            CharacterCombat combat = character.CharacterCombat;
            if (combat != null)
            {
                // On envoie l'arme (sera null si invalide, ce qui remettra l'animator en Civil)
                combat.OnWeaponChanged(hasValidWeapon ? _weapon : null);
            }
        }
    }

    private void SyncWeaponVisualToSocket()
    {
        // Double sécurité : on vérifie le SO avant d'accéder à CategoryName
        if (_weapon == null || _weapon.ItemSO == null) return;

        SpriteResolver[] resolvers = _weaponSocket.GetComponentsInChildren<SpriteResolver>();
        foreach (var res in resolvers)
        {
            res.SetCategoryAndLabel(_weapon.ItemSO.CategoryName, res.GetLabel());
        }
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
            // On vérifie si c'est déjà l'arme équipée
            if (_weapon == weapon) return;

            // Utilisation de la méthode dédiée
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
        // 1. Mise à jour de la donnée
        _weapon = weapon;
        Debug.Log($"<color=red>[Equip-Weapon]</color> {weapon.ItemSO.ItemName} équipée !");

        // 2. Mise à jour de TOUTE la chaîne (Visuel + Animator)
        UpdateWeaponVisual();
    }
    /// <summary>
    /// Déséquipe l'arme actuelle et repasse en mode civil.
    /// </summary>
    public void UnequipWeapon()
    {
        if (_weapon == null) return;

        character.DropItem(_weapon);
        _weapon = null;

        UpdateWeaponVisual(); // Désactive le socket + remet l'animator civil
        OnEquipmentChanged?.Invoke();
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

        // --- ÉTAPE DE NETTOYAGE ---
        // Avant d'afficher le nouveau sac, on s'assure que l'ancien nettoie ses armes
        if (!show || _bag == null)
        {
            ClearAllWeaponVisualsOnBag();
            _bagScript = null;
        }

        bool shouldActuallyShow = show && _bag != null;

        foreach (GameObject socket in _bagSockets)
        {
            if (socket == null) continue;

            // Si on cache le sac, on désactive le socket
            socket.SetActive(shouldActuallyShow);

            if (shouldActuallyShow)
            {
                // On récupère le nouveau script de sac
                _bagScript = socket.GetComponent<Bag>();

                if (_bagScript != null)
                {
                    _bagScript.RefreshAnchors();

                    // Initialisation des sprites du sac (Resolvers)
                    SpriteResolver[] resolvers = socket.GetComponentsInChildren<SpriteResolver>();
                    foreach (var res in resolvers)
                    {
                        res.SetCategoryAndLabel(_bag.ItemSO.CategoryName, res.GetLabel());
                    }

                    // Initialisation des couleurs
                    ApplyBagColors(socket);
                }
            }
        }

        // Une fois le nouveau sac prêt, on affiche les armes qu'il contient
        if (shouldActuallyShow)
        {
            UpdateWeaponVisualOnBag();
        }
    }

    // Extraction de la logique de couleur pour plus de clarté
    private void ApplyBagColors(GameObject socket)
    {
        SpriteRenderer[] renderers = socket.GetComponentsInChildren<SpriteRenderer>();
        foreach (SpriteRenderer sRenderer in renderers)
        {
            string goName = sRenderer.gameObject.name;
            if (goName == "Color_Primary" && _bag.HavePrimaryColor())
                sRenderer.color = _bag.PrimaryColor;
            else if (goName == "Color_Secondary" && _bag.HaveSecondaryColor())
                sRenderer.color = _bag.SecondaryColor;
        }
    }
    /// <summary>
    /// Détruit tous les visuels d'armes actuellement fixés sur le sac.
    /// </summary>
    public void ClearAllWeaponVisualsOnBag()
    {
        if (_bagScript == null) return;

        List<Transform> anchors = _bagScript.GetAllWeaponAnchors();
        foreach (Transform anchor in anchors)
        {
            if (anchor == null) continue;
            foreach (Transform child in anchor)
            {
                Destroy(child.gameObject);
            }
        }
    }
    /// <summary>
    /// Rafraîchit l'affichage des armes sur le sac en utilisant les anchors détectés.
    /// </summary>
    public void UpdateWeaponVisualOnBag()
    {
        // 1. Sécurités de base
        if (_bagScript == null || !HaveInventory()) return;

        // 2. On récupère la liste des slots d'armes de l'inventaire
        // On filtre pour n'avoir que les armes
        List<ItemInstance> weaponsInInventory = new List<ItemInstance>();
        foreach (var slot in GetInventory().ItemSlots)
        {
            if (slot is WeaponSlot && !slot.IsEmpty())
            {
                weaponsInInventory.Add(slot.ItemInstance);
            }
        }

        // 3. On récupère les points d'ancrage visuels sur le prefab du sac
        List<Transform> anchors = _bagScript.GetAllWeaponAnchors();

        // 4. Nettoyage et Instanciation
        for (int i = 0; i < anchors.Count; i++)
        {
            Transform anchor = anchors[i];

            // On détruit l'ancien visuel s'il existe
            foreach (Transform child in anchor)
            {
                Destroy(child.gameObject);
            }

            // Si on a une arme correspondante dans l'inventaire pour cet index d'anchor
            if (i < weaponsInInventory.Count)
            {
                CreateWeaponVisual(weaponsInInventory[i], anchor);
            }
        }
    }

    private void CreateWeaponVisual(ItemInstance weapon, Transform anchor)
    {
        GameObject visualPrefab = weapon.ItemPrefab;
        if (visualPrefab == null) return;

        // 1. Instanciation SANS parent d'abord (très important pour le calcul de matrice propre)
        GameObject instantiatedWeapon = Instantiate(visualPrefab);
        instantiatedWeapon.name = "Visual_" + weapon.ItemSO.ItemName;

        // 2. Initialisation des visuels (Sprites/Library)
        weapon.InitializePrefab(instantiatedWeapon);

        // 3. On laisse le Bag gérer le parentage ET le skinning d'un seul bloc
        if (_bagScript != null)
        {
            _bagScript.InitializeWeaponBones(instantiatedWeapon, anchor);
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