using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative replication layer for <see cref="StorageFurniture"/>.
///
/// **Why this is a sibling NetworkBehaviour instead of moving the contents into
/// StorageFurniture itself:** the <see cref="Furniture"/> base class is shared
/// by ChairFurniture, CraftingStation, TimeClockFurniture, Bed, etc. Most of
/// those don't need networking, so promoting <see cref="Furniture"/> to a
/// NetworkBehaviour would force a ripple of changes across the whole hierarchy.
/// We keep <see cref="StorageFurniture"/> as a plain MonoBehaviour and add a
/// dedicated sync component on the same GameObject. The GameObject already has
/// a <c>NetworkObject</c> from the <c>Furniture_prefab</c> base — we reuse it.
///
/// **Authority model.** Server is the only writer; <c>NetworkVariableWritePermission.Server</c>
/// enforces this at the NGO layer. Clients only deserialize the list and mirror
/// the resulting state into their local <c>StorageFurniture._itemSlots</c>. Strict-first
/// slot priority (the wearable→misc→any cascade in <c>StorageFurniture.AddItem</c>)
/// runs server-side only — clients just see the resulting layout.
///
/// **Sync strategy.** Sparse rewrite on every mutation: when the server's
/// <see cref="StorageFurniture.OnInventoryChanged"/> fires, we clear the
/// <see cref="_networkSlots"/> NetworkList and re-add one entry per non-empty
/// slot (carrying the slot's index so clients can place it back into the
/// matching slot). O(Capacity) per change but capacity is bounded — 32 in the
/// authored Crate prefab. Acceptable; if a future profile shows hot churn we
/// can switch to delta diffs.
///
/// **Empty-slot representation.** Empty slots are simply absent from the list.
/// The list is sparse; clients reconstruct slots by clearing all of them, then
/// writing each entry into <c>_itemSlots[entry.SlotIndex]</c>.
///
/// **Late joiners.** NetworkList auto-syncs its current contents to a newly
/// connected client during the spawn handshake. We additionally call
/// <see cref="ApplyFullStateOnClient"/> in <c>OnNetworkSpawn</c> for clients
/// because the very first <c>OnListChanged</c> event may have been delivered
/// before the local <c>StorageFurniture</c> was ready to receive updates (its
/// <c>Awake</c> initializes <c>_itemSlots</c>). One explicit pass after both
/// components are alive guarantees parity.
/// </summary>
[RequireComponent(typeof(StorageFurniture))]
public class StorageFurnitureNetworkSync : NetworkBehaviour
{
    [SerializeField] private StorageFurniture _storage;

    private NetworkList<NetworkStorageSlotEntry> _networkSlots;

    /// <summary>
    /// Replicated owner-assigned role for this storage. Authored 2026-05-08 as
    /// part of the unified storage-role system (see
    /// <c>wiki/projects/management-panel-followups.md</c> §1). Default is
    /// <see cref="StorageRoleType.None"/> at construction; on first server
    /// spawn we seed it from <see cref="StorageFurniture.InitialRole"/>.
    /// Owner-driven mutations route through <see cref="SetRoleServer"/>.
    /// Clients mirror the value into <see cref="StorageFurniture.ApplyRoleFromNetwork"/>
    /// on every change so consumers can read <c>storage.Role</c> without
    /// knowing about this NetworkBehaviour sibling.
    /// </summary>
    private NetworkVariable<StorageRoleType> _networkRole;

    /// <summary>
    /// Reusable buffer for rewriting client-side slots in
    /// <see cref="ApplyFullStateOnClient"/> — avoids one allocation per change.
    /// </summary>
    private readonly List<(int slotIndex, ItemInstance instance)> _scratchEntries =
        new List<(int slotIndex, ItemInstance instance)>(32);

    private void Reset()
    {
        _storage = GetComponent<StorageFurniture>();
    }

    private void Awake()
    {
        if (_storage == null) _storage = GetComponent<StorageFurniture>();

        // The NetworkList must be constructed in Awake so it exists before
        // NGO's spawn handshake reads its initial value. Capacity-hint of 8
        // is just an initial allocation — NetworkList resizes as needed.
        _networkSlots = new NetworkList<NetworkStorageSlotEntry>(
            null,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        // Same constraint applies to the role NetworkVariable — must exist
        // before the spawn handshake. Default value is None; the server
        // overwrites it from StorageFurniture.InitialRole in OnNetworkSpawn.
        _networkRole = new NetworkVariable<StorageRoleType>(
            StorageRoleType.None,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (_storage == null)
        {
            Debug.LogError($"<color=red>[StorageFurnitureNetworkSync]</color> {name}: missing StorageFurniture sibling — sync layer cannot run.");
            return;
        }

        if (IsServer)
        {
            // Subscribe BEFORE the initial rebuild so any server-side mutation
            // that lands during/after this frame propagates correctly.
            _storage.OnInventoryChanged += HandleServerInventoryChanged;

            // First rebuild syncs the spawn-time state. _itemSlots was built
            // in StorageFurniture.Awake() (which runs before OnNetworkSpawn
            // on the same GameObject), so this is safe.
            RebuildNetworkListFromStorage();

            // Seed the role NetworkVariable from the inspector-authored default.
            // Save-load runs AFTER OnNetworkSpawn — if a save entry has a non-default
            // role, MapController.RestoreStorageFurnitureContents will call
            // SetRoleServer(saved.Role) which overwrites this seed. So this line is
            // safe whether or not a save is being applied.
            _networkRole.Value = _storage.InitialRole;
            // Mirror the seed into the local StorageFurniture so server-side reads
            // of storage.Role return the right value before any client connects.
            _storage.ApplyRoleFromNetwork(_networkRole.Value);
            // Subscribe to value changes so server-side mutations (via SetRoleServer)
            // also fire OnRoleChanged on the host's StorageFurniture.
            _networkRole.OnValueChanged += HandleRoleChanged;
        }
        else
        {
            // Client side: subscribe to the NetworkList event, then run an
            // explicit catch-up pass for late-joiner safety.
            _networkSlots.OnListChanged += HandleClientListChanged;
            ApplyFullStateOnClient();

            // Subscribe to role changes + apply current value once for late-join parity.
            _networkRole.OnValueChanged += HandleRoleChanged;
            _storage.ApplyRoleFromNetwork(_networkRole.Value);
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && _storage != null)
        {
            _storage.OnInventoryChanged -= HandleServerInventoryChanged;
        }
        else if (_networkSlots != null)
        {
            _networkSlots.OnListChanged -= HandleClientListChanged;
        }

        if (_networkRole != null)
        {
            _networkRole.OnValueChanged -= HandleRoleChanged;
        }

        base.OnNetworkDespawn();
    }

    private void HandleRoleChanged(StorageRoleType prev, StorageRoleType next)
    {
        if (_storage == null) return;
        _storage.ApplyRoleFromNetwork(next);
    }

    /// <summary>
    /// Server-only — write the new role to the replicated <see cref="_networkRole"/>.
    /// Clients pick up the change via <see cref="HandleRoleChanged"/>. No-op on
    /// non-server peers (the NetworkVariable's write permission also rejects).
    /// Called by <see cref="CommercialBuilding.TrySetStorageRoleServerRpc"/> after
    /// owner-validation.
    /// </summary>
    public void SetRoleServer(StorageRoleType newRole)
    {
        if (!IsServer || _networkRole == null) return;
        if (_networkRole.Value == newRole) return;
        _networkRole.Value = newRole;
    }

    // =========================================================================
    // Player UI dispatch — client→server routing for store/take actions
    // =========================================================================
    //
    // Why these RPCs exist: CharacterStoreInFurnitureAction / CharacterTakeFromFurnitureAction
    // mutate StorageFurniture._itemSlots, which is server-authoritative (replicated via the
    // NetworkList in this component). CharacterActions.ExecuteAction runs the action's
    // OnApplyEffect locally on the calling peer — fine when the caller is the server (host
    // or NPC GOAP), but a non-server client just mutates its own local copy and the server
    // never learns. The next NetworkList sync from server then overwrites the client's
    // local change. These RPCs let the player UI hand the request to the server, where
    // ExecuteAction can run authoritatively and the result replicates back normally.
    //
    // Source identification: items are referenced by SLOT INDEX in their source container,
    // never by direct ItemInstance reference (ItemInstance is a plain C# object — not
    // network-serializable). The server reads the slot index against its authoritative
    // copy of the inventory or chest, so a stale client view can't trick the server into
    // operating on a phantom item.

    /// <summary>
    /// Player UI: store the item the character is currently carrying in hands into this storage.
    ///
    /// <para><b>Item payload:</b> the client supplies the ItemSO id + JSON of the item it
    /// believes is in its hands. Bag-inventory contents (and the hand slot) are NOT in
    /// <c>CharacterEquipment._networkEquipment</c>, so the server cannot read the client's
    /// hands authoritatively — the client is the source of truth and supplies the identity.
    /// Server reconstructs the <see cref="ItemInstance"/> via
    /// <see cref="ItemSO.CreateInstance"/> + <c>JsonUtility.FromJsonOverwrite</c>, validates
    /// the chest, then queues <see cref="CharacterStoreInFurnitureAction"/> with
    /// <see cref="CharacterStoreInFurnitureAction.SourceHands"/> so the action's
    /// remote-client path fires the ack ClientRpc that clears the owner's hands.</para>
    /// </summary>
    [Rpc(SendTo.Server)]
    public void RequestStoreFromHandsServerRpc(NetworkBehaviourReference characterRef, FixedString64Bytes itemId, FixedString4096Bytes itemJson)
    {
        if (_storage == null) return;
        if (!characterRef.TryGet(out Character character) || character == null) return;
        if (character.CharacterActions == null) return;
        if (character.CharacterActions.CurrentAction != null) return;

        ItemInstance item = TryReconstructStoredItem(itemId, itemJson);
        if (item == null) return;

        if (_storage.IsLocked) return;
        if (!_storage.HasFreeSpaceForItem(item)) return;

        var action = new CharacterStoreInFurnitureAction(character, item, _storage, CharacterStoreInFurnitureAction.SourceHands);
        character.CharacterActions.ExecuteAction(action);
    }

    /// <summary>
    /// Player UI: store an item from a specific bag inventory slot into this storage.
    ///
    /// <para><b>Item payload:</b> the client supplies the ItemSO id + JSON of the item at
    /// <paramref name="bagSlotIndex"/> on its OWN bag (bag-inventory contents aren't
    /// server-replicated, so the server's shadow copy is empty for remote clients —
    /// the slot index alone is insufficient). Server reconstructs the
    /// <see cref="ItemInstance"/> from the payload, validates the chest, and queues
    /// <see cref="CharacterStoreInFurnitureAction"/> with the slot index. The action's
    /// remote-client path fires the ack ClientRpc that removes from the owner's bag
    /// at <paramref name="bagSlotIndex"/>; the host path looks up the slot directly
    /// in the server-side bag (which IS host's bag) and removes that ItemInstance.</para>
    /// </summary>
    [Rpc(SendTo.Server)]
    public void RequestStoreFromBagServerRpc(NetworkBehaviourReference characterRef, int bagSlotIndex, FixedString64Bytes itemId, FixedString4096Bytes itemJson)
    {
        if (_storage == null) return;
        if (!characterRef.TryGet(out Character character) || character == null) return;
        if (character.CharacterActions == null) return;
        if (character.CharacterActions.CurrentAction != null) return;
        if (bagSlotIndex < 0) return; // basic sanity; remote-client bag bounds are owner-side

        ItemInstance item = TryReconstructStoredItem(itemId, itemJson);
        if (item == null) return;

        if (_storage.IsLocked) return;
        if (!_storage.HasFreeSpaceForItem(item)) return;

        var action = new CharacterStoreInFurnitureAction(character, item, _storage, bagSlotIndex);
        character.CharacterActions.ExecuteAction(action);
    }

    /// <summary>
    /// Server-side helper: rebuild an <see cref="ItemInstance"/> from the RPC payload sent
    /// by a player-UI store request. Resolves the ItemSO via Resources, runs the same
    /// <c>CreateInstance</c> + <c>JsonUtility.FromJsonOverwrite</c> dance as
    /// <see cref="WorldItem.ApplyNetworkData"/> and <see cref="TryDeserializeEntry"/>
    /// (preserves polymorphism — <see cref="WeaponInstance"/> / <see cref="WearableInstance"/>
    /// / etc. — because the concrete type comes from the SO factory). Returns null if the
    /// SO can't be resolved or the JSON fails to parse; the caller logs and bails.
    /// </summary>
    private static ItemInstance TryReconstructStoredItem(FixedString64Bytes itemId, FixedString4096Bytes itemJson)
    {
        if (itemId.IsEmpty) return null;
        string id = itemId.ToString();
        try
        {
            ItemSO[] all = Resources.LoadAll<ItemSO>("Data/Item");
            ItemSO so = System.Array.Find(all, x => x != null && x.ItemId == id);
            if (so == null)
            {
                Debug.LogError($"<color=red>[StorageFurnitureNetworkSync]</color> Cannot resolve ItemSO id '{id}' from Resources/Data/Item for store request.");
                return null;
            }
            ItemInstance inst = so.CreateInstance();
            JsonUtility.FromJsonOverwrite(itemJson.ToString(), inst);
            inst.ItemSO = so;
            return inst;
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            Debug.LogError($"<color=red>[StorageFurnitureNetworkSync]</color> Failed to reconstruct stored item id '{id}'.");
            return null;
        }
    }

    /// <summary>
    /// Player UI: take an item from the given chest slot into the character's bag (preferred)
    /// or hands (fallback). Server reads the slot from this storage's authoritative ItemSlots.
    /// </summary>
    [Rpc(SendTo.Server)]
    public void RequestTakeServerRpc(NetworkBehaviourReference characterRef, int chestSlotIndex, bool preferInventory)
    {
        if (_storage == null) return;
        if (!characterRef.TryGet(out Character character) || character == null) return;
        if (chestSlotIndex < 0 || chestSlotIndex >= _storage.Capacity) return;
        var slot = _storage.GetItemSlot(chestSlotIndex);
        if (slot == null || slot.IsEmpty() || slot.ItemInstance == null) return;
        if (character.CharacterActions == null) return;
        if (character.CharacterActions.CurrentAction != null) return;

        var action = new CharacterTakeFromFurnitureAction(character, slot.ItemInstance, _storage, preferInventory);
        character.CharacterActions.ExecuteAction(action);
    }

    // =========================================================================
    // Server side
    // =========================================================================

    private void HandleServerInventoryChanged()
    {
        if (!IsServer) return;
        RebuildNetworkListFromStorage();
    }

    /// <summary>
    /// Server-only. Rewrites <see cref="_networkSlots"/> to mirror the current
    /// <see cref="StorageFurniture.ItemSlots"/> state. Clears the list and
    /// re-adds one entry per non-empty slot. Empty slots are not represented.
    /// </summary>
    private void RebuildNetworkListFromStorage()
    {
        if (_networkSlots == null || _storage == null) return;

        _networkSlots.Clear();

        var slots = _storage.ItemSlots;
        if (slots == null) return;

        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            if (slot == null || slot.IsEmpty() || slot.ItemInstance == null) continue;
            var inst = slot.ItemInstance;
            if (inst.ItemSO == null) continue;

            string json;
            try
            {
                json = JsonUtility.ToJson(inst);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                Debug.LogError($"<color=red>[StorageFurnitureNetworkSync]</color> {name}: failed to serialize item '{inst.ItemSO.ItemName}' at slot {i} — entry skipped.");
                continue;
            }

            _networkSlots.Add(new NetworkStorageSlotEntry
            {
                SlotIndex = (ushort)i,
                ItemId = new FixedString64Bytes(inst.ItemSO.ItemId),
                JsonData = new FixedString4096Bytes(json)
            });
        }
    }

    // =========================================================================
    // Client side
    // =========================================================================

    private void HandleClientListChanged(NetworkListEvent<NetworkStorageSlotEntry> change)
    {
        if (IsServer) return; // host already has authoritative state locally
        // Every event type triggers a full rebuild — sparse rewrites mean Add /
        // RemoveAt / Clear all imply "the canonical list contents have moved".
        // Doing a full rebuild is simpler than per-event branching and matches
        // the O(Capacity) cost we already pay on the server. Crucially, this
        // also covers EventType.RemoveAt + EventType.Clear (see the network
        // skill's NetworkList event-type fan-out gotcha).
        ApplyFullStateOnClient();
    }

    /// <summary>
    /// Client-only. Walks the entire NetworkList, deserializes each entry into
    /// a fresh <see cref="ItemInstance"/>, and pushes the result into the local
    /// <see cref="StorageFurniture"/> via
    /// <see cref="StorageFurniture.ApplySyncedSlotsFromNetwork"/>. Fires
    /// <c>OnInventoryChanged</c> via the StorageFurniture API so visual displays
    /// rebuild on this peer.
    /// </summary>
    private void ApplyFullStateOnClient()
    {
        if (_storage == null || _networkSlots == null) return;

        _scratchEntries.Clear();

        int capacity = _storage.Capacity;
        for (int i = 0; i < _networkSlots.Count; i++)
        {
            var entry = _networkSlots[i];

            if (entry.SlotIndex >= capacity)
            {
                Debug.LogWarning(
                    $"<color=orange>[StorageFurnitureNetworkSync]</color> {name}: server entry targets slot {entry.SlotIndex} but local capacity is {capacity}. " +
                    $"Capacity mismatch usually means the prefab's per-type capacity ints diverge between server and client. Entry skipped.");
                continue;
            }

            ItemInstance inst = TryDeserializeEntry(entry);
            if (inst == null) continue;

            _scratchEntries.Add((entry.SlotIndex, inst));
        }

        _storage.ApplySyncedSlotsFromNetwork(_scratchEntries);
    }

    /// <summary>
    /// Mirrors <see cref="WorldItem.ApplyNetworkData"/>: resolves the ItemSO
    /// via Resources, calls <c>CreateInstance</c>, JSON-overwrites the
    /// instance, then re-binds <c>ItemSO</c> (lost during JsonOverwrite — same
    /// caveat the WorldItem path documents).
    /// </summary>
    private ItemInstance TryDeserializeEntry(NetworkStorageSlotEntry entry)
    {
        if (entry.ItemId.IsEmpty)
        {
            Debug.LogWarning($"<color=orange>[StorageFurnitureNetworkSync]</color> {name}: entry has empty ItemId — skipped.");
            return null;
        }

        string id = entry.ItemId.ToString();
        try
        {
            ItemSO[] all = Resources.LoadAll<ItemSO>("Data/Item");
            ItemSO so = System.Array.Find(all, x => x != null && x.ItemId == id);
            if (so == null)
            {
                Debug.LogError($"<color=red>[StorageFurnitureNetworkSync]</color> {name}: cannot resolve ItemSO id '{id}' from Resources/Data/Item. Available={all.Length}");
                return null;
            }

            ItemInstance inst = so.CreateInstance();
            JsonUtility.FromJsonOverwrite(entry.JsonData.ToString(), inst);
            inst.ItemSO = so; // FromJsonOverwrite wipes the SO ref — restore it.
            return inst;
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            Debug.LogError($"<color=red>[StorageFurnitureNetworkSync]</color> {name}: failed to deserialize entry id '{id}' — skipped.");
            return null;
        }
    }
}

/// <summary>
/// One replicated slot's worth of state. Sparse: empty slots are not represented
/// in the parent NetworkList. Carries enough to recreate the <see cref="ItemInstance"/>
/// on the client (same trick as <c>NetworkItemData</c> on <c>WorldItem</c>).
/// </summary>
public struct NetworkStorageSlotEntry : INetworkSerializable, System.IEquatable<NetworkStorageSlotEntry>
{
    public ushort SlotIndex;
    public FixedString64Bytes ItemId;
    public FixedString4096Bytes JsonData;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref SlotIndex);
        serializer.SerializeValue(ref ItemId);
        serializer.SerializeValue(ref JsonData);
    }

    public bool Equals(NetworkStorageSlotEntry other)
    {
        return SlotIndex == other.SlotIndex && ItemId.Equals(other.ItemId) && JsonData.Equals(other.JsonData);
    }

    public override bool Equals(object obj) => obj is NetworkStorageSlotEntry other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            int h = SlotIndex.GetHashCode();
            h = (h * 397) ^ ItemId.GetHashCode();
            h = (h * 397) ^ JsonData.GetHashCode();
            return h;
        }
    }
}
