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
        if (IsServer) return; // The server does this locally

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
            // optional: clear all
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
    // This event will be triggered each time an equipment changes
    public event Action OnEquipmentChanged;

    // Compatibility shortcut for existing code
    private Character character => _character;

    [Header("Combat")]
    [SerializeField] private WeaponInstance _weapon;
    [SerializeField] private GameObject _weaponSocket; // The visual attachment point of the weapon

    public WeaponInstance CurrentWeapon => _weapon;
    public bool HasWeaponInHands => _weapon != null;

    // Your layers assigned manually via [SerializeReference]
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

    // The toast system is now centralized via UI_Toast
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

    // Public getters
    public UnderwearLayer UnderwearLayer => underwearLayer;
    public ClothingLayer ClothingLayer => clothingLayer;
    public ArmorLayer ArmorLayer => armorLayer;
    public bool HasBagEquipped() => _bag != null;
    public BagInstance GetBagInstance() => _bag;
    public Bag BagScript => _bagScript;

    private void Start()
    {
        UpdateBagVisual(_bag != null);
        UpdateWeaponVisual(); // This method now already handles the null case cleanly
    }

    /// <summary>
    /// Updates the visual state of the weapon socket and notifies the combat system.
    /// </summary>
    private void UpdateWeaponVisual()
    {
        // We consider that we have a weapon ONLY if the instance AND its SO exist
        bool hasValidWeapon = _weapon != null && _weapon.ItemSO != null;

        // 1. VISUAL SOCKET HANDLING
        if (_weaponSocket != null)
        {
            // If the weapon is invalid or absent, we DISABLE the socket
            _weaponSocket.SetActive(hasValidWeapon);

            if (hasValidWeapon)
            {
                SyncWeaponVisualToSocket();
            }
        }

        // 2. COMBAT LOGIC HANDLING
        if (character != null)
        {
            CharacterCombat combat = character.CharacterCombat;
            if (combat != null)
            {
                // We send the weapon (will be null if invalid, which puts the animator back to Civil)
                combat.OnWeaponChanged(hasValidWeapon ? _weapon : null);
            }
        }
    }

    private void SyncWeaponVisualToSocket()
    {
        // Double safety: we check the SO before accessing CategoryName
        if (_weapon == null || _weapon.ItemSO == null) return;

        SpriteResolver[] resolvers = _weaponSocket.GetComponentsInChildren<SpriteResolver>();
        foreach (var res in resolvers)
        {
            res.SetCategoryAndLabel(_weapon.ItemSO.CategoryName, res.GetLabel());
        }
    }
    /// <summary>
    /// Forces the visual deactivation of all bag sockets.
    /// Useful if you want to clear the visuals without touching the data.
    /// </summary>
    public void DisableBagVisuals()
    {
        UpdateBagVisual(false);
    }

    public void Equip(ItemInstance itemInstance)
    {
        // 1. WEAPON HANDLING
        if (itemInstance is WeaponInstance weapon)
        {
            // We check if it's already the equipped weapon
            if (_weapon == weapon) return;

            // Use the dedicated method
            EquipWeapon(weapon);

            OnEquipmentChanged?.Invoke();
            return;
        }

        // 2. WEARABLES HANDLING (Bags included)
        if (itemInstance is WearableInstance wearable)
        {
            // We retrieve the typed SO (WearableSO or BagSO which inherits from it)
            if (wearable.ItemSO is WearableSO data)
            {
                // --- SPECIAL CASE: THE BAG ---
                // We check either the enum type or the class of the instance
                if (data.WearableType == WearableType.Bag || wearable is BagInstance)
                {
                    if (wearable is BagInstance bag)
                    {
                        EquipBag(bag);
                        OnEquipmentChanged?.Invoke();
                    }
                    else
                    {
                        Debug.LogError($"[Equip] Item {data.ItemName} is marked as Bag but the instance is not a BagInstance!");
                    }
                    return;
                }

                // --- GENERAL CASE: EQUIPMENT LAYERS ---
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

    // Small method to prepare what follows (Left/right hand handling for example)
    private void EquipWeapon(WeaponInstance weapon)
    {
        // 1. Data update
        _weapon = weapon;
        Debug.Log($"<color=red>[Equip-Weapon]</color> {weapon.ItemSO.ItemName} equipped!");

        // 2. Update the WHOLE chain (Visual + Animator)
        UpdateWeaponVisual();
        UpdateNetworkSlot(0, weapon);
    }
    /// <summary>
    /// Unequips the current weapon and goes back to civilian mode.
    /// </summary>
    public void UnequipWeapon()
    {
        if (_weapon == null) return;

        character.DropItem(_weapon);
        _weapon = null;

        UpdateWeaponVisual(); // Deactivates the socket + resets the animator to civilian
        UpdateNetworkSlot(0, null);
        OnEquipmentChanged?.Invoke();
    }

    private void EquipBag(BagInstance newBag)
    {
        // If a bag is already equipped, we could unequip it here
        if (_bag != null)
        {
            // Logic to put the old bag back into the inventory or on the ground
        }

        _bag = newBag;
        UpdateBagVisual(true);
        UpdateNetworkSlot(1, newBag);
        Debug.Log($"<color=green>[Equip-Bag]</color> {newBag.ItemSO.ItemName} equipped on the global slot.");
    }

    /// <summary>
    /// Removes the current bag, updates the visuals and spawns the item on the ground.
    /// </summary>
    public void UnequipBag()
    {
        if (_bag == null)
        {
            Debug.LogWarning("[Unequip] No bag is equipped.");
            return;
        }

        Debug.Log($"<color=orange>[Unequip-Bag]</color> Removing: <b>{_bag.ItemSO.ItemName}</b>");

        // 1. We ask the character to drop the item physically into the world
        // This method must handle the spawn of the WorldItem prefab with the _bag instance
        character.DropItem(_bag);

        // 2. We clean up the reference and hide the visuals
        _bag = null;
        UpdateBagVisual(false);
        UpdateNetworkSlot(1, null);
    }

    private void UpdateBagVisual(bool show)
    {
        if (_bagSockets == null || _bagSockets.Count == 0) return;

        // --- CLEANUP STEP ---
        // Before displaying the new bag, we make sure the old one cleans up its weapons
        if (!show || _bag == null)
        {
            ClearAllWeaponVisualsOnBag();
            _bagScript = null;
        }

        bool shouldActuallyShow = show && _bag != null;

        foreach (GameObject socket in _bagSockets)
        {
            if (socket == null) continue;

            // If we hide the bag, we deactivate the socket
            socket.SetActive(shouldActuallyShow);

            if (shouldActuallyShow)
            {
                // We retrieve the new bag script
                _bagScript = socket.GetComponent<Bag>();

                if (_bagScript != null)
                {
                    _bagScript.RefreshAnchors();

                    // Initialize the bag sprites (Resolvers)
                    SpriteResolver[] resolvers = socket.GetComponentsInChildren<SpriteResolver>();
                    foreach (var res in resolvers)
                    {
                        res.SetCategoryAndLabel(_bag.ItemSO.CategoryName, res.GetLabel());
                    }

                    // Initialize the colors
                    ApplyBagColors(socket);
                }
            }
        }

        // Once the new bag is ready, we display the weapons it contains
        if (shouldActuallyShow)
        {
            UpdateWeaponVisualOnBag();
        }
    }

    // Extraction of the color logic for clarity
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
    /// Destroys all weapon visuals currently attached to the bag.
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
    /// Refreshes the display of weapons on the bag using the detected anchors.
    /// </summary>
    public void UpdateWeaponVisualOnBag()
    {
        // 1. Basic safeties
        if (_bagScript == null || !HaveInventory()) return;

        // 2. We retrieve the list of weapon slots from the inventory
        // We filter to only have weapons
        List<ItemInstance> weaponsInInventory = new List<ItemInstance>();
        foreach (var slot in GetInventory().ItemSlots)
        {
            if (slot is WeaponSlot && !slot.IsEmpty())
            {
                weaponsInInventory.Add(slot.ItemInstance);
            }
        }

        // 3. We retrieve the visual anchor points on the bag prefab
        List<Transform> anchors = _bagScript.GetAllWeaponAnchors();

        // 4. Cleanup and Instantiation
        for (int i = 0; i < anchors.Count; i++)
        {
            Transform anchor = anchors[i];

            // We destroy the old visual if it exists
            foreach (Transform child in anchor)
            {
                Destroy(child.gameObject);
            }

            // If we have a matching weapon in the inventory for this anchor index
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

        // 1. Instantiate WITHOUT parent first (very important for clean matrix calculation)
        GameObject instantiatedWeapon = Instantiate(visualPrefab);
        instantiatedWeapon.name = "Visual_" + weapon.ItemSO.ItemName;

        // 2. Initialize the visuals (Sprites/Library)
        weapon.InitializePrefab(instantiatedWeapon);

        // 3. We let the Bag handle parenting AND skinning in a single block
        if (_bagScript != null)
        {
            _bagScript.InitializeWeaponBones(instantiatedWeapon, anchor);
        }
    }


    /// <summary>
    /// Removes a specific piece of equipment based on its Layer and its Slot.
    /// Handles the destruction of the instance, the freeing of the slot and the visual update.
    /// </summary>
    /// <param name="layerType">The layer concerned (Underwear, Clothing, Armor).</param>
    /// <param name="slotType">The body part to free (Helmet, Armor, Boots, etc.).</param>
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
            // 1. We retrieve the instance BEFORE emptying the slot
            EquipmentInstance instanceToDrop = targetLayer.GetInstance(slotType);

            if (instanceToDrop == null) return;

            // 2. We empty the slot (visual + data)
            targetLayer.Unequip(slotType);
            UpdateNetworkSlot(GetSlotId(layerType, slotType), null);
            OnEquipmentChanged?.Invoke();

            // 3. We drop the instance we saved
            character.DropItem(instanceToDrop);

            Debug.Log($"<color=orange>[Unequip]</color> {instanceToDrop.ItemSO.ItemName} removed and dropped.");
        }
    }

    // Logic based on the EquipmentLayerEnum
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
                return null; // The bag does not have a dedicated EquipmentLayer component
            default:
                return null;
        }
    }

    /// <summary>
    /// Checks whether the character currently has a container equipped (Bag).
    /// </summary>
    public bool HaveInventory()
    {
        // We check that the bag exists AND that it has a properly initialized inventory
        return _bag != null && _bag.Inventory != null;
    }

    /// <summary>
    /// Returns the inventory of the equipped bag. Returns null if no bag is present.
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
    /// Checks if the inventory has at least one empty slot FOR basic objects (wood, stone).
    /// If the character has no inventory, returns false.
    /// </summary>
    public bool HasFreeSpaceForMisc()
    {
        if (!HaveInventory()) return false;
        
        return GetInventory().HasFreeSpaceForMisc();
    }

    /// <summary>
    /// Dynamically checks if there is space for a given object type (Weapon, Wearable, Misc).
    /// </summary>
    public bool HasFreeSpaceForItemSO(ItemSO itemSO)
    {
        if (!HaveInventory()) return false;
        
        return GetInventory().HasFreeSpaceForItemSO(itemSO);
    }

    /// <summary>
    /// Determines whether the character can still carry this item (either in their bag, or in their hands).
    /// </summary>
    public bool CanCarryItemAnyMore(ItemInstance itemInstance)
    {
        if (itemInstance == null) return false;

        // 1. Check whether there is space in the bag
        if (HaveInventory() && GetInventory().HasFreeSpaceForItem(itemInstance))
        {
            return true;
        }

        // 2. Check whether the hands are free
        var handsController = character.CharacterVisual?.BodyPartsController?.HandsController;
        if (handsController != null && handsController.AreHandsFree())
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Checks whether the character's torso/chest is exposed.
    /// Returns True if no "Shirt" type clothing is equipped in any of the 3 layers.
    /// </summary>
    public bool IsChestExposed()
    {
        bool hasUnderwearShirt = underwearLayer != null && underwearLayer.GetInstance(WearableType.Armor) != null;
        bool hasClothingShirt = clothingLayer != null && clothingLayer.GetInstance(WearableType.Armor) != null;
        bool hasArmorShirt = armorLayer != null && armorLayer.GetInstance(WearableType.Armor) != null;

        // Exposed if no layer has a Shirt
        return !hasUnderwearShirt && !hasClothingShirt && !hasArmorShirt;
    }

    /// <summary>
    /// Checks whether the lower private parts are exposed.
    /// Returns True if no "Pants" type clothing is equipped in any of the 3 layers.
    /// Note: This is technically identical to your current IsNaked() logic.
    /// </summary>
    public bool IsGroinExposed()
    {
        // We reuse the Pants logic
        bool hasUnderwearPants = underwearLayer != null && underwearLayer.GetInstance(WearableType.Pants) != null;
        bool hasClothingPants = clothingLayer != null && clothingLayer.GetInstance(WearableType.Pants) != null;
        bool hasArmorPants = armorLayer != null && armorLayer.GetInstance(WearableType.Pants) != null;

        return !hasUnderwearPants && !hasClothingPants && !hasArmorPants;
    }

    /// <summary>
    /// Checks for total nudity (Top and Bottom).
    /// </summary>
    public bool IsFullyNaked()
    {
        return IsChestExposed() && IsGroinExposed();
    }

    /// <summary>
    /// Checks whether the character is "naked" at the legs level.
    /// Returns True if no pants are equipped in any of the 3 layers (Underwear, Clothing, Armor).
    /// </summary>
    public bool IsNaked()
    {
        // We check the Pants slot in each layer.
        // If any of the layers returns a non-null instance, the character is not naked.
        bool hasUnderwearPants = underwearLayer != null && underwearLayer.GetInstance(WearableType.Pants) != null;
        bool hasClothingPants = clothingLayer != null && clothingLayer.GetInstance(WearableType.Pants) != null;
        bool hasArmorPants = armorLayer != null && armorLayer.GetInstance(WearableType.Pants) != null;

        // The character is naked if they have NONE of the three
        return !hasUnderwearPants && !hasClothingPants && !hasArmorPants;
    }

    /// <summary>
    /// Returns the material of the equipped boots, checking armor > clothing > underwear layers in priority order.
    /// Returns ItemMaterial.None if no boots are equipped in any layer.
    /// </summary>
    public ItemMaterial GetFootMaterial()
    {
        var boots = armorLayer?.GetInstance(WearableType.Boots)
                 ?? clothingLayer?.GetInstance(WearableType.Boots)
                 ?? underwearLayer?.GetInstance(WearableType.Boots);

        if (boots != null)
            return boots.ItemSO.Material;

        return ItemMaterial.None;
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
    /// Tries to take an object specifically into the hands (via HandsController).
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
    /// Physically drops the object currently held in the hands.
    /// </summary>
    public void DropItemFromHand()
    {
        var handsController = _character.CharacterVisual?.BodyPartsController?.HandsController;
        if (handsController != null && handsController.IsCarrying)
        {
            ItemInstance droppedItem = handsController.DropCarriedItem();
            if (droppedItem != null)
            {
                // Instead of queueing an action (which would be cancelled by the death/combat animation)
                // We physically spawn the object on the ground instantly.
                CharacterDropItem.ExecutePhysicalDrop(_character, droppedItem);
            }
        }
    }

    /// <summary>
    /// Removes a specific object from the bag's inventory and physically drops it on the ground.
    /// </summary>
    public bool DropItemFromInventory(ItemInstance itemToDrop)
    {
        if (itemToDrop == null) return false;

        Inventory inventory = GetInventory();
        if (inventory != null)
        {
            // We try to remove the item from the inventory
            if (inventory.RemoveItem(itemToDrop, _character))
            {
                // The item was successfully removed, we spawn it in the world
                CharacterDropItem.ExecutePhysicalDrop(_character, itemToDrop);

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
