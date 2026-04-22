using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using MWI.Terrain;

[RequireComponent(typeof(FurnitureGrid))]
[RequireComponent(typeof(FurnitureManager))]
public class Room : Zone
{
    [Header("Room Info")]
    [SerializeField] protected string _roomName;

    [Header("Terrain")]
    [SerializeField] private TerrainType _floorTerrainType;
    [SerializeField] private bool _isExposed;

    public TerrainType FloorTerrainType => _floorTerrainType;
    public bool IsExposed => _isExposed;

    protected FurnitureManager _furnitureManager;

    // Networked ownership & residency lists (store Character UUIDs)
    protected NetworkList<FixedString64Bytes> _ownerIds;
    protected NetworkList<FixedString64Bytes> _residentIds;

    public event Action<string> OnResidentAdded;
    public event Action<string> OnResidentRemoved;

    public string RoomName => _roomName;

    public IEnumerable<Character> Owners
    {
        get
        {
            for (int i = 0; i < _ownerIds.Count; i++)
            {
                Character c = Character.FindByUUID(_ownerIds[i].ToString());
                if (c != null) yield return c;
            }
        }
    }

    public IEnumerable<Character> Residents
    {
        get
        {
            for (int i = 0; i < _residentIds.Count; i++)
            {
                Character c = Character.FindByUUID(_residentIds[i].ToString());
                if (c != null) yield return c;
            }
        }
    }

    public int OwnerCount => _ownerIds.Count;
    public int ResidentCount => _residentIds.Count;

    /// <summary>
    /// Raw owner UUIDs. Unlike <see cref="Owners"/>, this does NOT call FindByUUID,
    /// so it includes IDs whose Character is hibernated on another map. Use for
    /// serialization paths (BuildingSaveData) where the live Character may not exist.
    /// </summary>
    public IEnumerable<string> OwnerIds
    {
        get
        {
            for (int i = 0; i < _ownerIds.Count; i++)
                yield return _ownerIds[i].ToString();
        }
    }

    /// <summary>
    /// Raw resident UUIDs. See <see cref="OwnerIds"/> for rationale.
    /// </summary>
    public IEnumerable<string> ResidentIds
    {
        get
        {
            for (int i = 0; i < _residentIds.Count; i++)
                yield return _residentIds[i].ToString();
        }
    }

    public virtual bool IsResident(Character character)
    {
        if (character == null) return false;
        return ContainsId(_residentIds, character.CharacterId);
    }

    public bool IsOwner(Character character)
    {
        if (character == null) return false;
        return ContainsId(_ownerIds, character.CharacterId);
    }

    public FurnitureManager FurnitureManager => _furnitureManager;
    public FurnitureGrid Grid => _furnitureManager != null ? _furnitureManager.Grid : null;

    protected override void Awake()
    {
        // NetworkList must be initialized before base.Awake / OnNetworkSpawn
        _ownerIds = new NetworkList<FixedString64Bytes>();
        _residentIds = new NetworkList<FixedString64Bytes>();

        base.Awake();
        _furnitureManager = GetComponent<FurnitureManager>();

        if (_boxCollider != null && _furnitureManager.Grid != null)
        {
            if (_furnitureManager.Grid.IsInitialized)
            {
                // Grid was pre-baked in the editor — restore runtime 2D array from serialized data
                _furnitureManager.Grid.RestoreFromSerializedData();
            }
            else
            {
                // No pre-baked data — initialize at runtime (legacy path)
                _furnitureManager.Grid.Initialize(_boxCollider);
            }
        }
        else
        {
            Debug.LogError($"<color=red>[Room]</color> {_roomName} requires a BoxCollider to define its area and initialize the FurnitureGrid.");
        }

        _furnitureManager.LoadExistingFurniture();
    }

    private void Start()
    {
        // Re-scan after every sibling's Awake has finished. Nested prefab children (e.g. a
        // CraftingStation inside Room_Main of a Forge) are sometimes not yet visible to
        // GetComponentsInChildren<Furniture>() during Awake, depending on network/prefab
        // spawn order — which left Furnitures empty and made CraftingBuilding.GetCraftableItems()
        // return [], breaking supplier lookup in the logistics chain. LoadExistingFurniture
        // replaces the list and RegisterFurniture is idempotent, so re-calling is safe.
        if (_furnitureManager != null) _furnitureManager.LoadExistingFurniture();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // On clients, Awake() fires before NGO sets the network position (e.g. interior at y=5000).
        // The grid origin calculated in Awake is wrong. Recalculate now that the transform is correct.
        if (IsClient && !IsServer && _furnitureManager != null && _furnitureManager.Grid != null && _furnitureManager.Grid.IsInitialized)
        {
            _furnitureManager.Grid.RestoreFromSerializedData();
        }

        // Same race as the Awake scan: on clients the networked furniture children may be spawned
        // after Room.Awake has run. Rescan on network spawn too.
        if (_furnitureManager != null) _furnitureManager.LoadExistingFurniture();
    }

    public bool IsPointInsideRoom(Vector3 point)
    {
        if (_boxCollider == null) return false;
        return _boxCollider.bounds.Contains(point);
    }

    public void AddOwner(Character owner)
    {
        if (owner == null || !IsServer) return;
        string uuid = owner.CharacterId;
        if (string.IsNullOrEmpty(uuid) || ContainsId(_ownerIds, uuid)) return;
        _ownerIds.Add(new FixedString64Bytes(uuid));
    }

    public void RemoveOwner(Character owner)
    {
        if (owner == null || !IsServer) return;
        RemoveId(_ownerIds, owner.CharacterId);
    }

    public virtual bool AddResident(Character resident)
    {
        if (resident == null || !IsServer) return false;
        string uuid = resident.CharacterId;
        if (string.IsNullOrEmpty(uuid) || ContainsId(_residentIds, uuid)) return false;
        _residentIds.Add(new FixedString64Bytes(uuid));
        OnResidentAdded?.Invoke(uuid);
        return true;
    }

    public virtual bool RemoveResident(Character resident)
    {
        if (resident == null || !IsServer) return false;
        string uuid = resident.CharacterId;
        if (RemoveId(_residentIds, uuid))
        {
            OnResidentRemoved?.Invoke(uuid);
            return true;
        }
        return false;
    }

    #region NetworkList Helpers

    protected static bool ContainsId(NetworkList<FixedString64Bytes> list, string uuid)
    {
        if (string.IsNullOrEmpty(uuid)) return false;
        var fs = new FixedString64Bytes(uuid);
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] == fs) return true;
        }
        return false;
    }

    protected static bool RemoveId(NetworkList<FixedString64Bytes> list, string uuid)
    {
        if (string.IsNullOrEmpty(uuid)) return false;
        var fs = new FixedString64Bytes(uuid);
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] == fs)
            {
                list.RemoveAt(i);
                return true;
            }
        }
        return false;
    }

    #endregion

    #region Furniture Tag Queries

    public virtual bool HasFurnitureWithTag(FurnitureTag tag)
    {
        if (_furnitureManager == null) return false;
        foreach (var f in _furnitureManager.Furnitures)
        {
            if (f != null && f.FurnitureTag == tag) return true;
        }
        return false;
    }

    public virtual IEnumerable<Furniture> GetFurnitureByTag(FurnitureTag tag)
    {
        if (_furnitureManager == null) yield break;
        foreach (var f in _furnitureManager.Furnitures)
        {
            if (f != null && f.FurnitureTag == tag) yield return f;
        }
    }

    public virtual IEnumerable<T> GetFurnitureOfType<T>() where T : Furniture
    {
        if (_furnitureManager == null) yield break;
        foreach (var f in _furnitureManager.Furnitures)
        {
            if (f is T typed) yield return typed;
        }
    }

    #endregion
}
