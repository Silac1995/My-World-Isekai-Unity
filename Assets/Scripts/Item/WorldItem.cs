using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;
using Unity.Collections;

[RequireComponent(typeof(UnityEngine.Rendering.SortingGroup))]
public class WorldItem : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private Transform _visualRoot; // Drag the "Visual" object of your prefab here
    [SerializeField] private NavMeshObstacle _navMeshObstacle;

    [Header("Data")]
    [SerializeField] private ItemInstance _itemInstance;
    [SerializeField] private ItemInteractable _itemInteractable;
    
    public ItemInstance ItemInstance => _itemInstance;
    public ItemInteractable ItemInteractable => _itemInteractable;
    public UnityEngine.Rendering.SortingGroup SortingGroup { get; private set; }
    public bool IsBeingCarried { get; set; } = false;

    [SerializeField]
    private NetworkVariable<NetworkItemData> _networkItemData = new NetworkVariable<NetworkItemData>(
        new NetworkItemData(),
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    [SerializeField]
    private NetworkVariable<bool> _obstacleActive = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public void SetNetworkData(NetworkItemData data)
    {
        _networkItemData.Value = data;
    }

    private void Awake()
    {
        SortingGroup = GetComponent<UnityEngine.Rendering.SortingGroup>();
        if (_navMeshObstacle == null) _navMeshObstacle = GetComponent<NavMeshObstacle>();
    }

    public override void OnNetworkSpawn()
    {
        _networkItemData.OnValueChanged += OnItemDataChanged;
        _obstacleActive.OnValueChanged += OnObstacleActiveChanged;

        // Non-server peers must NOT simulate physics: the server owns this item
        // (Ownership=1 in prefab) and replicates its transform via NetworkTransform.
        // A non-kinematic Rigidbody on the client would fight the replicated position
        // and produce visual desync.
        if (!IsServer)
        {
            if (TryGetComponent(out Rigidbody rb))
                rb.isKinematic = true;
        }

        // Late-joiner: apply current obstacle state immediately
        if (_obstacleActive.Value && _navMeshObstacle != null)
            _navMeshObstacle.enabled = true;

        // Apply data if joining late as a client
        if (IsClient && !IsServer)
        {
            ApplyNetworkData(_networkItemData.Value);
        }
    }

    public override void OnNetworkDespawn()
    {
        _networkItemData.OnValueChanged -= OnItemDataChanged;
        _obstacleActive.OnValueChanged -= OnObstacleActiveChanged;
    }

    private void OnItemDataChanged(NetworkItemData previousValue, NetworkItemData newValue)
    {
        if (IsClient && !IsServer)
        {
            ApplyNetworkData(newValue);
        }
    }

    private void OnObstacleActiveChanged(bool previousValue, bool newValue)
    {
        if (_navMeshObstacle != null) _navMeshObstacle.enabled = newValue;
    }

    private void ApplyNetworkData(NetworkItemData data)
    {
        if (data.ItemId.IsEmpty)
        {
            Debug.LogWarning($"<color=orange>[WorldItem]</color> ApplyNetworkData: ItemId is empty on {gameObject.name}. Data may not have replicated yet.");
            return;
        }

        string id = data.ItemId.ToString();
        ItemSO[] allItems = Resources.LoadAll<ItemSO>("Data/Item");
        ItemSO so = System.Array.Find(allItems, match => match.ItemId == id);

        if (so != null)
        {
            Debug.Log($"<color=cyan>[WorldItem]</color> ApplyNetworkData: Found SO '{so.ItemName}' for ID '{id}'. Creating instance type: {so.InstanceType.Name}");
            ItemInstance instance = so.CreateInstance();
            JsonUtility.FromJsonOverwrite(data.JsonData.ToString(), instance);
            instance.ItemSO = so;
            Initialize(instance);
        }
        else
        {
            Debug.LogError($"<color=red>[WorldItem]</color> Could not find ItemSO with ID: '{id}' in Resources/Data/Item. Total SOs loaded: {allItems.Length}");
            foreach (var item in allItems)
                Debug.Log($"  - Available SO: '{item.ItemId}' ({item.ItemName}) [{item.GetType().Name}]");
        }
    }

    public void Initialize(ItemInstance instance)
    {
        _itemInstance = instance;

        if (_itemInstance != null && _itemInstance.ItemSO != null)
        {
            // 1. Instantiate the visual (the item's prefab)
            GameObject prefabVisual = _itemInstance.ItemSO.ItemPrefab;
            if (prefabVisual != null)
            {
                AttachVisualPrefab(prefabVisual);
            }

            // 2. APPLY COLORS AND LIBRARY
            // Grab the handler that was just instantiated inside the Visual
            WearableHandlerBase handler = GetComponentInChildren<WearableHandlerBase>();

            if (handler != null)
            {
                // Use the handler to apply the instance data
                handler.Initialize(_itemInstance.ItemSO.SpriteLibraryAsset);
                handler.SetLibraryCategory(_itemInstance.ItemSO.CategoryName);

                if (_itemInstance is EquipmentInstance eq)
                {
                    if (eq.HavePrimaryColor()) handler.SetPrimaryColor(eq.PrimaryColor);
                    if (eq.HaveSecondaryColor()) handler.SetSecondaryColor(eq.SecondaryColor);
                    // If you have a main color (Main)
                    handler.SetMainColor(Color.white); // Or eq.MainColor if you have added it
                }
            }
            else
            {
                // Fallback: if it's not an equipment with handler (e.g. an apple)
                _itemInstance.InitializeWorldPrefab(gameObject);
            }

            ApplyShadowCastingFromItemSO();
        }
    }

    /// <summary>
    /// Applies ShadowCastingMode to every Renderer under this WorldItem based on the ItemSO
    /// override. Called after visual attachment so both wearables and simple items inherit
    /// the correct setting. receiveShadows is left to prefab authoring - this method does
    /// not touch it.
    /// </summary>
    private void ApplyShadowCastingFromItemSO()
    {
        if (_itemInstance == null || _itemInstance.ItemSO == null) return;

        UnityEngine.Rendering.ShadowCastingMode mode = _itemInstance.ItemSO.CastsShadow
            ? UnityEngine.Rendering.ShadowCastingMode.On
            : UnityEngine.Rendering.ShadowCastingMode.Off;

        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            renderers[i].shadowCastingMode = mode;
        }
    }

    private void AttachVisualPrefab(GameObject prefab)
    {
        if (_visualRoot == null)
        {
            Debug.LogError($"[WorldItem] _visualRoot (the Visual object) is not assigned on {gameObject.name}");
            return;
        }

        // Clean up the previous visuals if any
        foreach (Transform child in _visualRoot)
        {
            Destroy(child.gameObject);
        }

        // Instantiate the input prefab inside the Visual
        GameObject go = Instantiate(prefab, _visualRoot);

        // Reset transforms to make sure it is properly centered
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
    }

    /// <summary>
    /// Canonical server-side WorldItem spawn path. Every spawn in the project routes here.
    ///
    /// Prefab resolution: instance.ItemSO.WorldItemPrefab first (per-item authored shell);
    /// falls back to SpawnManager.Instance.DefaultItemPrefab (generic shell) when null.
    /// This lets debug/crafting spawns work without requiring every ItemSO to author a prefab,
    /// while authored items still use their own visuals.
    ///
    /// NGO order (mandatory): Instantiate → Initialize(instance) → netObj.Spawn(true) →
    /// ParentToContainingMap → SetNetworkData. SetNetworkData must come AFTER Spawn so
    /// the NetworkVariable write triggers OnValueChanged for already-connected clients.
    ///
    /// ejectImpulse/ejectTorque: optional Rigidbody impulse applied post-spawn (crafting,
    /// debug scatter). Only applied when the prefab has a Rigidbody component.
    /// </summary>
    public static WorldItem SpawnWorldItem(
        ItemInstance instance,
        Vector3 position,
        Quaternion? rotation = null,
        Vector3? ejectImpulse = null,
        Vector3? ejectTorque = null)
    {
        if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsServer)
        {
            Debug.LogError($"<color=red>[WorldItem]</color> SpawnWorldItem can only be called by the Server!");
            return null;
        }

        if (instance == null || instance.ItemSO == null) return null;

        GameObject prefab = instance.ItemSO.WorldItemPrefab;
        if (prefab == null && SpawnManager.Instance != null)
            prefab = SpawnManager.Instance.DefaultItemPrefab;

        if (prefab == null)
        {
            Debug.LogWarning($"<color=orange>[WorldItem]</color> No WorldItemPrefab on '{instance.ItemSO.ItemName}' and no DefaultItemPrefab on SpawnManager — item not spawned.");
            return null;
        }

        GameObject worldItemGo = Object.Instantiate(prefab, position, rotation ?? Quaternion.identity);
        worldItemGo.name = $"WorldItem_{instance.ItemSO.ItemName}";

        if (!worldItemGo.TryGetComponent(out WorldItem worldItem))
        {
            Debug.LogError($"<color=red>[WorldItem]</color> Prefab for '{instance.ItemSO.ItemName}' has no WorldItem component!");
            Object.Destroy(worldItemGo);
            return null;
        }

        worldItem.Initialize(instance);

        if (worldItemGo.TryGetComponent(out NetworkObject netObj))
        {
            netObj.Spawn(true);
            ParentToContainingMap(worldItemGo);
        }
        else
        {
            Debug.LogWarning($"<color=orange>[WorldItem]</color> Prefab for '{instance.ItemSO.ItemName}' missing NetworkObject component!");
        }

        // Set NetworkData AFTER Spawn so OnValueChanged fires for already-connected clients.
        worldItem._networkItemData.Value = new NetworkItemData
        {
            ItemId = new FixedString64Bytes(instance.ItemSO.ItemId),
            JsonData = new FixedString4096Bytes(JsonUtility.ToJson(instance))
        };

        if (ejectImpulse.HasValue && worldItemGo.TryGetComponent(out Rigidbody rb))
        {
            rb.AddForce(ejectImpulse.Value, ForceMode.Impulse);
            if (ejectTorque.HasValue)
                rb.AddTorque(ejectTorque.Value);
        }

        return worldItem;
    }

    /// <summary>
    /// Convenience overload — creates a default instance from the SO and delegates to the
    /// canonical ItemInstance path. Use the ItemInstance overload directly when you need to
    /// set colors, durability, or other per-instance state before spawning.
    /// </summary>
    public static WorldItem SpawnWorldItem(ItemSO itemSO, Vector3 position, Quaternion? rotation = null)
    {
        if (itemSO == null) return null;
        return SpawnWorldItem(itemSO.CreateInstance(), position, rotation);
    }

    /// <summary>
    /// Server-side. Reparents a freshly-spawned WorldItem GameObject under the MapController
    /// whose trigger bounds contain its current world position. Looked up via
    /// MapController.GetAnyMapAtPosition so interiors and exteriors both qualify. Falls back
    /// silently to scene root if the spawn happens outside any registered map (open world).
    /// Must be called AFTER NetworkObject.Spawn so the parent change is replicated to clients.
    /// Called by SpawnWorldItem — exposed public for any future external spawn path.
    /// </summary>
    public static void ParentToContainingMap(GameObject worldItemGo)
    {
        var map = MWI.WorldSystem.MapController.GetAnyMapAtPosition(worldItemGo.transform.position);
        if (map != null)
        {
            worldItemGo.transform.SetParent(map.transform, worldPositionStays: true);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestInteractServerRpc(ulong interactorNetworkObjectId)
    {
        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(interactorNetworkObjectId, out NetworkObject interactorNetObj))
            return;

        Character interactor = interactorNetObj.GetComponent<Character>();
        if (interactor == null || _itemInstance == null) return;

        NetworkItemData itemData = _networkItemData.Value;

        // Wearables: execute on server via CharacterEquipAction.
        // This writes to NetworkList<NetworkEquipmentSyncData> (server-write only)
        // which auto-syncs the visual to all clients.
        if (_itemInstance is WearableInstance wearable)
        {
            var action = new CharacterEquipAction(interactor, wearable);
            interactor.CharacterActions.ExecuteAction(action);
            NetworkObject.Despawn(true);
            return;
        }

        // Non-wearables (misc, furniture, etc.): the pickup must run on the owning client
        // because inventory/hands operations are client-authoritative.
        if (interactorNetObj.IsOwnedByServer)
        {
            // Host: execute directly — only despawn if pickup succeeds
            if (interactor.CharacterEquipment != null && interactor.CharacterEquipment.PickUpItem(_itemInstance))
            {
                NetworkObject.Despawn(true);
            }
            else
            {
                Debug.LogWarning($"<color=orange>[WorldItem]</color> Host failed to pick up {_itemInstance.ItemSO.ItemName} (inventory full / hands full).");
            }
        }
        else
        {
            // Remote client: send item data to the owning client, then despawn.
            // We despawn optimistically — the client will handle the item.
            if (interactor.CharacterActions != null)
                interactor.CharacterActions.ReceiveItemPickupClientRpc(itemData);
            NetworkObject.Despawn(true);
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        // Server-authoritative: only the server decides when an item locks into the navmesh.
        if (!IsServer) return;
        if (_obstacleActive.Value) return;
        if (_itemInstance == null || _itemInstance.ItemSO == null) return;
        if (!_itemInstance.ItemSO.BlocksPathing) return;

        // Setting the NetworkVariable propagates to every peer; OnObstacleActiveChanged
        // enables their local NavMeshObstacle.
        _obstacleActive.Value = true;
    }
}

public struct NetworkItemData : INetworkSerializable
{
    public FixedString64Bytes ItemId;
    public FixedString4096Bytes JsonData;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref ItemId);
        serializer.SerializeValue(ref JsonData);
    }
}
