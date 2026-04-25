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
        }
        else
        {
            // Client side: subscribe to the NetworkList event, then run an
            // explicit catch-up pass for late-joiner safety.
            _networkSlots.OnListChanged += HandleClientListChanged;
            ApplyFullStateOnClient();
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

        base.OnNetworkDespawn();
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
