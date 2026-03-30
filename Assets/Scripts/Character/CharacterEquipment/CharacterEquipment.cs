using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D.Animation;
using Unity.Netcode;
using Unity.Collections;

public class CharacterEquipment : CharacterSystem, ICharacterSaveData<EquipmentSaveData>
{
    private NetworkList<NetworkEquipmentSyncData> _networkEquipment;

    protected override void Awake()
    {
        base.Awake();
        _networkEquipment = new NetworkList<NetworkEquipmentSyncData>(
            null,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _networkEquipment.OnListChanged += OnEquipmentListChanged;

        if (IsClient && !IsServer)
        {
            FullSyncFromNetwork();
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _networkEquipment.OnListChanged -= OnEquipmentListChanged;
    }

    private void UpdateNetworkSlot(ushort slotId, ItemInstance instance)
    {
        if (!IsServer) return;

        for (int i = 0; i < _networkEquipment.Count; i++)
        {
            if (_networkEquipment[i].SlotId == slotId)
            {
                _networkEquipment.RemoveAt(i);
                break;
            }
        }

        if (instance != null && instance.ItemSO != null)
        {
            _networkEquipment.Add(new NetworkEquipmentSyncData
            {
                SlotId = slotId,
                ItemId = new FixedString64Bytes(instance.ItemSO.ItemId),
                JsonData = new FixedString4096Bytes(JsonUtility.ToJson(instance))
            });
        }
    }

    private void OnEquipmentListChanged(NetworkListEvent<NetworkEquipmentSyncData> changeEvent)
    {
        if (IsServer) return; // Le serveur fait ça de manière locale

        if (changeEvent.Type == NetworkListEvent<NetworkEquipmentSyncData>.EventType.Add ||
            changeEvent.Type == NetworkListEvent<NetworkEquipmentSyncData>.EventType.Insert ||
            changeEvent.Type == NetworkListEvent<NetworkEquipmentSyncData>.EventType.Value)
        {
            ApplyEquipmentData(changeEvent.Value);
        }
        else if (changeEvent.Type == NetworkListEvent<NetworkEquipmentSyncData>.EventType.Remove ||
                 changeEvent.Type == NetworkListEvent<NetworkEquipmentSyncData>.EventType.RemoveAt)
        {
            RemoveEquipmentData(changeEvent.Value.SlotId);
        }
        else if (changeEvent.Type == NetworkListEvent<NetworkEquipmentSyncData>.EventType.Clear)
        {
            // optionnel: clear all
        }
    }

    private void FullSyncFromNetwork()
    {
        foreach (var data in _networkEquipment)
        {
            ApplyEquipmentData(data);
        }
    }

    private void ApplyEquipmentData(NetworkEquipmentSyncData data)
    {
        string id = data.ItemId.ToString();
        ItemSO[] allItems = Resources.LoadAll<ItemSO>("Data/Item");
        ItemSO so = Array.Find(allItems, match => match.ItemId == id);

        if (so != null)
        {
            ItemInstance instance = so.CreateInstance();
            JsonUtility.FromJsonOverwrite(data.JsonData.ToString(), instance);
            instance.ItemSO = so;

            ApplyEquipmentVisually(data.SlotId, instance);
        }
    }

    private void ApplyEquipmentVisually(ushort slotId, ItemInstance instance)
    {
        if (slotId == 0 && instance is WeaponInstance weapon)
        {
            _weapon = weapon;
            UpdateWeaponVisual();
            OnEquipmentChanged?.Invoke();
        }
        else if (slotId == 1 && instance is BagInstance bag)
        {
            _bag = bag;
            UpdateBagVisual(true);
            OnEquipmentChanged?.Invoke();
        }
        else if (instance is WearableInstance wearable && wearable.ItemSO is WearableSO so)
        {
            EquipmentLayer targetLayer = GetTargetLayer(so.EquipmentLayer);
            if (targetLayer != null)
            {
                targetLayer.Equip(wearable);
                OnEquipmentChanged?.Invoke();
            }
        }
    }

    private void RemoveEquipmentData(ushort slotId)
    {
        if (slotId == 0)
        {
            _weapon = null;
            UpdateWeaponVisual();
            OnEquipmentChanged?.Invoke();
        }
        else if (slotId == 1)
        {
            _bag = null;
            UpdateBagVisual(false);
            OnEquipmentChanged?.Invoke();
        }
        else
        {
            WearableLayerEnum layer = WearableLayerEnum.Underwear;
            if (slotId >= 300) layer = WearableLayerEnum.Armor;
            else if (slotId >= 200) layer = WearableLayerEnum.Clothing;

            int typeVal = slotId % 100;
            WearableType type = (WearableType)typeVal;

            EquipmentLayer targetLayer = GetTargetLayer(layer);
            if (targetLayer != null)
            {
                targetLayer.Unequip(type);
                OnEquipmentChanged?.Invoke();
            }
        }
    }

    private ushort GetSlotId(WearableLayerEnum layer, WearableType type)
    {
        int layerOffset = layer switch {
            WearableLayerEnum.Underwear => 100,
            WearableLayerEnum.Clothing => 200,
            WearableLayerEnum.Armor => 300,
            _ => 1000
        };
        return (ushort)(layerOffset + (int)type);
    }
    // Cet événement sera déclenché chaque fois qu'un équipement change
    public event Action OnEquipmentChanged;

    // Raccourci de compatibilité pour le code existant
    private Character character => _character;

    [Header("Combat")]
    [SerializeField] private WeaponInstance _weapon;
    [SerializeField] private GameObject _weaponSocket; // Le point d'attache visuel de l'arme

    public WeaponInstance CurrentWeapon => _weapon;
    public bool HasWeaponInHands => _weapon != null;

    // Tes couches assignées manuellement via [SerializeReference]
    [SerializeReference] private UnderwearLayer underwearLayer;
    [SerializeReference] private ClothingLayer clothingLayer;
    [SerializeReference] private ArmorLayer armorLayer;
    [Header("Global Accessories")]
    [SerializeField] private Bag _bagScript;
    [SerializeField] private BagInstance _bag;
    [SerializeField] private List<GameObject> _bagSockets;

    [Header("Notifications")]
    [SerializeField] private MWI.UI.Notifications.NotificationChannel _inventoryNotificationChannel;
    [SerializeField] private MWI.UI.Notifications.ToastNotificationChannel _toastChannel;

    // Le systeme de toasts est maintenant centralisé via UI_Toast
    // private ToastNotificationChannel _toastChannel;

    public void InitializeNotifications(MWI.UI.Notifications.NotificationChannel inventoryChannel, MWI.UI.Notifications.ToastNotificationChannel toastChannel = null)
    {
        _inventoryNotificationChannel = inventoryChannel;
        _toastChannel = toastChannel;
    }

    public void ClearNotifications()
    {
        _inventoryNotificationChannel = null;
        _toastChannel = null;
    }

    public void ClearInventoryNotification()
    {
        if (_inventoryNotificationChannel != null)
        {
            _inventoryNotificationChannel.Clear();
        }
    }

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

                    EquipmentInstance existingInstance = targetLayer.GetInstance(data.WearableType);
                    if (existingInstance != null)
                    {
                        character.DropItem(existingInstance);
                    }

                    Debug.Log($"<color=green>[Equip]</color> {data.ItemName} vers {data.EquipmentLayer}");
                    targetLayer.Equip(wearable);
                    UpdateNetworkSlot(GetSlotId(data.EquipmentLayer, data.WearableType), wearable);
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
        UpdateNetworkSlot(0, weapon);
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
        UpdateNetworkSlot(0, null);
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
        UpdateNetworkSlot(1, newBag);
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
        UpdateNetworkSlot(1, null);
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
            UpdateNetworkSlot(GetSlotId(layerType, slotType), null);
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
    /// Vérifie si l'inventaire posséde au moins un slot vide POUR des objets basiques (bois, pierre).
    /// Si le personnage n'a pas d'inventaire, retourne faux.
    /// </summary>
    public bool HasFreeSpaceForMisc()
    {
        if (!HaveInventory()) return false;
        
        return GetInventory().HasFreeSpaceForMisc();
    }

    /// <summary>
    /// Vérifie dynamiquement s'il y a de la place pour un type d'objet donné (Arme, Wearable, Misc).
    /// </summary>
    public bool HasFreeSpaceForItemSO(ItemSO itemSO)
    {
        if (!HaveInventory()) return false;
        
        return GetInventory().HasFreeSpaceForItemSO(itemSO);
    }

    /// <summary>
    /// Détermine si le personnage peut encore porter cet item (soit dans son sac, soit dans ses mains).
    /// </summary>
    public bool CanCarryItemAnyMore(ItemInstance itemInstance)
    {
        if (itemInstance == null) return false;

        // 1. Vérifier s'il y a de la place dans le sac
        if (HaveInventory() && GetInventory().HasFreeSpaceForItem(itemInstance))
        {
            return true;
        }

        // 2. Vérifier si les mains sont libres
        var handsController = character.CharacterVisual?.BodyPartsController?.HandsController;
        if (handsController != null && handsController.AreHandsFree())
        {
            return true;
        }

        return false;
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

    /// <summary>
    /// Checks if the character possesses at least one item of the specified ItemSO (in inventory or hands).
    /// </summary>
    public bool HasItemSO(ItemSO itemSO)
    {
        if (itemSO == null) return false;

        var inventory = GetInventory();
        if (inventory != null && inventory.HasAnyItemSO(new List<ItemSO> { itemSO }))
        {
            return true;
        }

        var handsController = _character.CharacterVisual?.BodyPartsController?.HandsController;
        if (handsController != null && handsController.CarriedItem != null && handsController.CarriedItem.ItemSO == itemSO)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Searches inventory and hands for a KeyInstance whose LockId matches
    /// and whose Tier meets or exceeds the required tier.
    /// Returns the first match or null.
    /// </summary>
    public KeyInstance FindKeyForLock(string lockId, int requiredTier = 0)
    {
        if (string.IsNullOrEmpty(lockId)) return null;

        // Check bag inventory first
        var inventory = GetInventory();
        if (inventory != null)
        {
            foreach (var slot in inventory.ItemSlots)
            {
                if (slot.IsEmpty()) continue;
                if (slot.ItemInstance is KeyInstance key &&
                    key.LockId == lockId &&
                    key.KeyData != null &&
                    key.KeyData.Tier >= requiredTier)
                {
                    return key;
                }
            }
        }

        // Check hands
        var handsController = _character.CharacterVisual?.BodyPartsController?.HandsController;
        if (handsController != null &&
            handsController.CarriedItem is KeyInstance handKey &&
            handKey.LockId == lockId &&
            handKey.KeyData != null &&
            handKey.KeyData.Tier >= requiredTier)
        {
            return handKey;
        }

        return null;
    }

    /// <summary>
    /// Tente de prendre un objet spécifiquement dans les mains (via HandsController).
    /// </summary>
    public bool CarryItemInHand(ItemInstance item)
    {
        var handsController = _character.CharacterVisual?.BodyPartsController?.HandsController;
        if (handsController != null && handsController.AreHandsFree())
        {
            return handsController.CarryItem(item);
        }
        return false;
    }

    /// <summary>
    /// Fait tomber physiquement l'objet actuellement tenu dans les mains.
    /// </summary>
    public void DropItemFromHand()
    {
        var handsController = _character.CharacterVisual?.BodyPartsController?.HandsController;
        if (handsController != null && handsController.IsCarrying)
        {
            ItemInstance droppedItem = handsController.DropCarriedItem();
            if (droppedItem != null)
            {
                // Au lieu de mettre en file d'attente une action (qui serait annulée par l'animation de mort/combat)
                // On spawn physiquement l'objet au sol instantanément.
                CharacterDropItem.ExecutePhysicalDrop(_character, droppedItem, false);
            }
        }
    }

    /// <summary>
    /// Retire un objet spécifique de l'inventaire du sac et le fait tomber physiquement au sol.
    /// </summary>
    public bool DropItemFromInventory(ItemInstance itemToDrop)
    {
        if (itemToDrop == null) return false;

        Inventory inventory = GetInventory();
        if (inventory != null)
        {
            // On tente de retirer l'item de l'inventaire
            if (inventory.RemoveItem(itemToDrop, _character))
            {
                // L'item a été retiré avec succès, on le fait spawner dans le monde
                CharacterDropItem.ExecutePhysicalDrop(_character, itemToDrop, false);

                if (_toastChannel != null && _character.IsPlayer() && _character.IsOwner)
                {
                    _toastChannel.Raise(new MWI.UI.Notifications.ToastNotificationPayload(
                        message: $"Dropped {itemToDrop.ItemSO.ItemName}",
                        type: MWI.UI.Notifications.ToastType.Warning,
                        duration: 3f,
                        icon: itemToDrop.ItemSO.Icon
                    ));
                }

                return true;
            }
        }

        return false;
    }

    public bool PickUpItem(ItemInstance item)
    {
        if (item == null) return false;

        var inventory = GetInventory();
        bool inventoryAdded = false;
        
        if (inventory != null && inventory.AddItem(item, _character))
        {
            if (_inventoryNotificationChannel != null)
                _inventoryNotificationChannel.Raise();
                
            if (_toastChannel != null && _character.IsPlayer())
            {
                _toastChannel.Raise(new MWI.UI.Notifications.ToastNotificationPayload(
                    message: $"Picked up {item.ItemSO.ItemName}",
                    type: MWI.UI.Notifications.ToastType.Info,
                    duration: 3f,
                    icon: item.ItemSO.Icon
                ));
            }
                
            return true;
        }

        bool carriedInHand = CarryItemInHand(item);
        
        if (carriedInHand && _toastChannel != null && _character.IsPlayer())
        {
            _toastChannel.Raise(new MWI.UI.Notifications.ToastNotificationPayload(
                message: $"Carrying {item.ItemSO.ItemName}",
                type: MWI.UI.Notifications.ToastType.Info,
                duration: 3f,
                icon: item.ItemSO.Icon
            ));
        }
        
        return carriedInHand;
    }

    protected override void HandleIncapacitated(Character character)
    {
        DropItemFromHand();
    }

    protected override void HandleCombatStateChanged(bool inCombat)
    {
        if (inCombat) DropItemFromHand();
    }

    // --- ICharacterSaveData IMPLEMENTATION ---
    public string SaveKey => "CharacterEquipment";
    public int LoadPriority => 30;

    public EquipmentSaveData Serialize()
    {
        var data = new EquipmentSaveData();

        // Weapon (slot 0)
        if (_weapon != null && _weapon.ItemSO != null)
        {
            data.equippedItems.Add(new EquipmentSlotSaveEntry
            {
                slotId = 0,
                itemId = _weapon.ItemSO.ItemId,
                jsonData = JsonUtility.ToJson(_weapon)
            });
        }

        // Bag (slot 1) — serialize the bag itself, inventory items are handled separately
        if (_bag != null && _bag.ItemSO != null)
        {
            data.equippedItems.Add(new EquipmentSlotSaveEntry
            {
                slotId = 1,
                itemId = _bag.ItemSO.ItemId,
                jsonData = JsonUtility.ToJson(_bag)
            });

            // Bag inventory items — each item stored individually to handle polymorphism
            if (_bag.Inventory != null)
            {
                var slots = _bag.Inventory.ItemSlots;
                for (int i = 0; i < slots.Count; i++)
                {
                    if (!slots[i].IsEmpty() && slots[i].ItemInstance.ItemSO != null)
                    {
                        data.bagInventoryItems.Add(new InventorySlotSaveEntry
                        {
                            slotIndex = i,
                            itemId = slots[i].ItemInstance.ItemSO.ItemId,
                            jsonData = JsonUtility.ToJson(slots[i].ItemInstance)
                        });
                    }
                }
            }
        }

        // Wearable layers (underwear=100+, clothing=200+, armor=300+)
        SerializeLayer(underwearLayer, WearableLayerEnum.Underwear, data.equippedItems);
        SerializeLayer(clothingLayer, WearableLayerEnum.Clothing, data.equippedItems);
        SerializeLayer(armorLayer, WearableLayerEnum.Armor, data.equippedItems);

        return data;
    }

    private void SerializeLayer(EquipmentLayer layer, WearableLayerEnum layerEnum, List<EquipmentSlotSaveEntry> entries)
    {
        if (layer == null) return;

        foreach (WearableType type in System.Enum.GetValues(typeof(WearableType)))
        {
            if (type == WearableType.Bag) continue; // Bags are handled separately

            EquipmentInstance instance = layer.GetInstance(type);
            if (instance != null && instance.ItemSO != null)
            {
                entries.Add(new EquipmentSlotSaveEntry
                {
                    slotId = (int)GetSlotId(layerEnum, type),
                    itemId = instance.ItemSO.ItemId,
                    jsonData = JsonUtility.ToJson(instance)
                });
            }
        }
    }

    public void Deserialize(EquipmentSaveData data)
    {
        if (data == null) return;

        // Cache all ItemSOs once for lookup
        ItemSO[] allItems = Resources.LoadAll<ItemSO>("Data/Item");

        // Restore equipped items (weapon, bag shell, wearable layers)
        foreach (var entry in data.equippedItems)
        {
            ItemSO so = System.Array.Find(allItems, match => match.ItemId == entry.itemId);
            if (so == null)
            {
                Debug.LogWarning($"[CharacterEquipment.Deserialize] ItemSO not found for id '{entry.itemId}' (slot {entry.slotId}). Skipping.");
                continue;
            }

            ItemInstance instance = so.CreateInstance();
            JsonUtility.FromJsonOverwrite(entry.jsonData, instance);
            instance.ItemSO = so;

            ushort slotId = (ushort)entry.slotId;

            if (slotId == 0 && instance is WeaponInstance weapon)
            {
                _weapon = weapon;
                UpdateWeaponVisual();
                UpdateNetworkSlot(0, weapon);
            }
            else if (slotId == 1 && instance is BagInstance bag)
            {
                _bag = bag;
                // Re-initialize bag capacity so the inventory has proper slots
                bag.InitializeBagCapacity();
                UpdateBagVisual(true);
                UpdateNetworkSlot(1, bag);
            }
            else if (instance is WearableInstance wearable && wearable.ItemSO is WearableSO wearableSO)
            {
                EquipmentLayer targetLayer = GetTargetLayer(wearableSO.EquipmentLayer);
                if (targetLayer != null)
                {
                    targetLayer.Equip(wearable);
                    UpdateNetworkSlot(slotId, wearable);
                }
            }
        }

        // Restore bag inventory items after the bag itself is equipped
        if (_bag != null && _bag.Inventory != null && data.bagInventoryItems.Count > 0)
        {
            var inventorySlots = _bag.Inventory.ItemSlots;

            foreach (var invEntry in data.bagInventoryItems)
            {
                if (invEntry.slotIndex < 0 || invEntry.slotIndex >= inventorySlots.Count)
                {
                    Debug.LogWarning($"[CharacterEquipment.Deserialize] Bag inventory slot index {invEntry.slotIndex} out of range (capacity: {inventorySlots.Count}). Skipping item '{invEntry.itemId}'.");
                    continue;
                }

                ItemSO itemSO = System.Array.Find(allItems, match => match.ItemId == invEntry.itemId);
                if (itemSO == null)
                {
                    Debug.LogWarning($"[CharacterEquipment.Deserialize] ItemSO not found for bag inventory item '{invEntry.itemId}'. Skipping.");
                    continue;
                }

                ItemInstance itemInstance = itemSO.CreateInstance();
                JsonUtility.FromJsonOverwrite(invEntry.jsonData, itemInstance);
                itemInstance.ItemSO = itemSO;

                inventorySlots[invEntry.slotIndex].ItemInstance = itemInstance;
            }
        }

        OnEquipmentChanged?.Invoke();
    }

    // Non-generic bridge (explicit interface impl)
    string ICharacterSaveData.SerializeToJson() => CharacterSaveDataHelper.SerializeToJson(this);
    void ICharacterSaveData.DeserializeFromJson(string json) => CharacterSaveDataHelper.DeserializeFromJson(this, json);
}

public struct NetworkEquipmentSyncData : INetworkSerializable, IEquatable<NetworkEquipmentSyncData>
{
    public ushort SlotId; // 0=Weapon, 1=Bag, 100+ = Underwear, 200+ = Clothing, 300+ = Armor
    public FixedString64Bytes ItemId;
    public FixedString4096Bytes JsonData;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref SlotId);
        serializer.SerializeValue(ref ItemId);
        serializer.SerializeValue(ref JsonData);
    }

    public bool Equals(NetworkEquipmentSyncData other)
    {
        return SlotId == other.SlotId && ItemId == other.ItemId && JsonData == other.JsonData;
    }
}
