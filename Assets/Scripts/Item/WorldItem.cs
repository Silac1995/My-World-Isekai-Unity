using UnityEngine;
using Unity.Netcode;
using Unity.Collections;

[RequireComponent(typeof(UnityEngine.Rendering.SortingGroup))]
public class WorldItem : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private Transform _visualRoot; // Glisse l'objet "Visual" de ton prefab ici

    [Header("Data")]
    [SerializeField] private ItemInstance _itemInstance;
    [SerializeField] private ItemInteractable _itemInteractable;
    
    public ItemInstance ItemInstance => _itemInstance;
    public ItemInteractable ItemInteractable => _itemInteractable;
    public UnityEngine.Rendering.SortingGroup SortingGroup { get; private set; }
    public bool IsBeingCarried { get; set; } = false;
    public bool FreezeOnGround { get; set; } = false;

    [SerializeField]
    private NetworkVariable<NetworkItemData> _networkItemData = new NetworkVariable<NetworkItemData>(
        new NetworkItemData(),
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
    }

    public override void OnNetworkSpawn()
    {
        _networkItemData.OnValueChanged += OnItemDataChanged;

        // Apply data if joining late as a client
        if (IsClient && !IsServer)
        {
            ApplyNetworkData(_networkItemData.Value);
        }
    }

    public override void OnNetworkDespawn()
    {
        _networkItemData.OnValueChanged -= OnItemDataChanged;
    }

    private void OnItemDataChanged(NetworkItemData previousValue, NetworkItemData newValue)
    {
        if (IsClient && !IsServer)
        {
            ApplyNetworkData(newValue);
        }
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
            // 1. Instanciation du visuel (le prefab de l'item)
            GameObject prefabVisual = _itemInstance.ItemSO.ItemPrefab;
            if (prefabVisual != null)
            {
                AttachVisualPrefab(prefabVisual);
            }

            // 2. APPLICATION DES COULEURS ET LIBRARY
            // On recupere le handler qui vient d'etre instancie dans le Visual
            WearableHandlerBase handler = GetComponentInChildren<WearableHandlerBase>();

            if (handler != null)
            {
                // On utilise le handler pour appliquer les donnees de l'instance
                handler.Initialize(_itemInstance.ItemSO.SpriteLibraryAsset);
                handler.SetLibraryCategory(_itemInstance.ItemSO.CategoryName);

                if (_itemInstance is EquipmentInstance eq)
                {
                    if (eq.HavePrimaryColor()) handler.SetPrimaryColor(eq.PrimaryColor);
                    if (eq.HaveSecondaryColor()) handler.SetSecondaryColor(eq.SecondaryColor);
                    // Si tu as une couleur principale (Main)
                    handler.SetMainColor(Color.white); // Ou eq.MainColor si tu l'as ajoutee
                }
            }
            else
            {
                // Fallback : Si ce n'est pas un equipement avec handler (ex: une pomme)
                _itemInstance.InitializeWorldPrefab(gameObject);
            }
        }
    }

    private void AttachVisualPrefab(GameObject prefab)
    {
        if (_visualRoot == null)
        {
            Debug.LogError($"[WorldItem] _visualRoot (l'objet Visual) n'est pas assigne sur {gameObject.name}");
            return;
        }

        // Nettoyage des anciens visuels s'il y en a
        foreach (Transform child in _visualRoot)
        {
            Destroy(child.gameObject);
        }

        // Instanciation du prefab d'entree a l'interieur du Visual
        GameObject go = Instantiate(prefab, _visualRoot);

        // Reset des transforms pour etre sur qu'il soit bien centre
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
    }

    /// <summary>
    /// Instancie le WorldItem prefab de l'ItemSO et l'initialise dans le monde.
    /// Utilisé quand on veut drop un item au sol (ex: deposit).
    /// </summary>
    public static WorldItem SpawnWorldItem(ItemSO itemSO, Vector3 position)
    {
        if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsServer)
        {
            Debug.LogError($"<color=red>[WorldItem]</color> SpawnWorldItem can only be called by the Server!");
            return null;
        }

        GameObject prefab = itemSO.WorldItemPrefab;
        if (prefab == null)
        {
            Debug.LogWarning($"<color=orange>[Gather]</color> Pas de WorldItemPrefab sur {itemSO.ItemName}, item non spawné.");
            return null;
        }

        GameObject worldItemGo = Object.Instantiate(prefab, position, Quaternion.identity);
        worldItemGo.name = $"WorldItem_{itemSO.ItemName}";

        ItemInstance instance = itemSO.CreateInstance();

        if (worldItemGo.TryGetComponent(out WorldItem worldItem))
        {
            worldItem.Initialize(instance);

            if (worldItemGo.TryGetComponent(out NetworkObject netObj))
            {
                netObj.Spawn(true);
            }
            else
            {
                Debug.LogWarning($"<color=orange>[Gather]</color> WorldItemPrefab for {itemSO.ItemName} missing NetworkObject component!");
            }

            // Setup Network Data after Spawn so OnValueChanged fires on connected clients
            worldItem._networkItemData.Value = new NetworkItemData
            {
                ItemId = new FixedString64Bytes(itemSO.ItemId),
                JsonData = new FixedString4096Bytes(JsonUtility.ToJson(instance))
            };

            return worldItem;
        }
        else
        {
            Debug.LogError($"<color=red>[Gather]</color> Le prefab de {itemSO.ItemName} n'a pas de composant WorldItem !");
            Object.Destroy(worldItemGo);
            return null;
        }
    }

    /// <summary>
    /// Instancie le WorldItem prefab en utilisant une instance existante (pour préserver sa durabilité, couleurs, etc).
    /// </summary>
    public static WorldItem SpawnWorldItem(ItemInstance instance, Vector3 position)
    {
        if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsServer)
        {
            Debug.LogError($"<color=red>[WorldItem]</color> SpawnWorldItem can only be called by the Server!");
            return null;
        }

        if (instance == null || instance.ItemSO == null) return null;

        GameObject prefab = instance.ItemSO.WorldItemPrefab;
        if (prefab == null)
        {
            Debug.LogWarning($"<color=orange>[Gather]</color> Pas de WorldItemPrefab sur {instance.ItemSO.ItemName}, item non spawné.");
            return null;
        }

        GameObject worldItemGo = Object.Instantiate(prefab, position, Quaternion.identity);
        worldItemGo.name = $"WorldItem_{instance.ItemSO.ItemName}";

        if (worldItemGo.TryGetComponent(out WorldItem worldItem))
        {
            worldItem.Initialize(instance);

            if (worldItemGo.TryGetComponent(out NetworkObject netObj))
            {
                netObj.Spawn(true);
            }
            else
            {
                Debug.LogWarning($"<color=orange>[Gather]</color> WorldItemPrefab for {instance.ItemSO.ItemName} missing NetworkObject component!");
            }

            // Setup Network Data after Spawn so OnValueChanged fires on connected clients
            worldItem._networkItemData.Value = new NetworkItemData
            {
                ItemId = new FixedString64Bytes(instance.ItemSO.ItemId),
                JsonData = new FixedString4096Bytes(JsonUtility.ToJson(instance))
            };

            return worldItem;
        }
        else
        {
            Debug.LogError($"<color=red>[Gather]</color> Le prefab de {instance.ItemSO.ItemName} n'a pas de composant WorldItem !");
            Object.Destroy(worldItemGo);
            return null;
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
            // Host: execute directly
            if (interactor.CharacterEquipment != null)
                interactor.CharacterEquipment.PickUpItem(_itemInstance);
            NetworkObject.Despawn(true);
        }
        else
        {
            // Remote client: send item data to the owning client, then despawn
            if (interactor.CharacterActions != null)
                interactor.CharacterActions.ReceiveItemPickupClientRpc(itemData);
            NetworkObject.Despawn(true);
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (FreezeOnGround)
        {
            if (TryGetComponent(out Rigidbody rb))
            {
                rb.isKinematic = true;
                FreezeOnGround = false; // Disable it so if it is carried again, it doesn't freeze immediately
                Debug.Log($"<color=white>[WorldItem]</color> {gameObject.name} ground freeze engaged.");
            }
        }
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
