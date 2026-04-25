using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class GameSessionManager : MonoBehaviour
{
    public static GameSessionManager Instance { get; private set; }

    public static bool AutoStartNetwork = false;
    public static bool IsHost = true;
    public static string TargetIP = "127.0.0.1";
    public static ushort TargetPort = 7777;

    public static string SelectedPlayerRace = "Human";
    [SerializeField] private List<RaceSO> _availableRaces = new List<RaceSO>();
    [SerializeField] private RaceSO _defaultFallbackRace;

    public System.Collections.Generic.IReadOnlyList<RaceSO> AvailableRaces => _availableRaces;
    public RaceSO GetRace(string raceName)
    {
        RaceSO race = _availableRaces.Find(r => r.name == raceName);
        if (race == null)
        {
            Debug.LogWarning($"[GameSession] Race '{raceName}' not found in available races. Using fallback.");
            return _defaultFallbackRace;
        }
        return race;
    }

    private Dictionary<ulong, string> _pendingClientRaces = new Dictionary<ulong, string>();
    private void Awake()
    {
        // No DontDestroyOnLoad — GameSessionManager is recreated fresh each scene.
        // Static flags (AutoStartNetwork, IsHost, etc.) survive across scenes.
        // This ensures we always reference the current scene's NetworkManager.
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        _callbacksRegistered = false; // Always re-register with this scene's NetworkManager
    }

    private bool _callbacksRegistered;

    private void Start()
    {
        EnsureCallbacksRegistered();
        CheckAutoStart();
    }

    private void OnEnable()
    {
        // Re-check on enable — handles DontDestroyOnLoad surviving scene reloads
        EnsureCallbacksRegistered();
        CheckAutoStart();
    }

    /// <summary>
    /// Resets callback state so they re-register on next call to EnsureCallbacksRegistered.
    /// Call after NetworkManager.Shutdown() since shutdown clears all callbacks.
    /// </summary>
    public void ResetCallbacks()
    {
        _callbacksRegistered = false;
    }

    public void EnsureCallbacksRegistered()
    {
        if (_callbacksRegistered || NetworkManager.Singleton == null) return;
        _callbacksRegistered = true;

        NetworkManager.Singleton.NetworkConfig.ConnectionApproval = true;

        // ConnectionApprovalCallback is a single-delegate (not multicast) — set, don't +=
        NetworkManager.Singleton.ConnectionApprovalCallback = ApprovalCheck;

        NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnect;
        NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnect;
    }

    public void CheckAutoStart()
    {
        if (AutoStartNetwork)
        {
            AutoStartNetwork = false;
            StartCoroutine(WaitAndStartNetwork());
        }
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.ConnectionApprovalCallback -= ApprovalCheck;
            NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnect;
        }
    }

    private void HandleClientConnected(ulong clientId)
    {
        if (clientId == NetworkManager.Singleton.LocalClientId && !IsHost)
        {
            Debug.Log("<color=cyan>[GameSession]</color> Connected to Server");
        }

        if (NetworkManager.Singleton.IsServer)
        {
            if (_pendingClientRaces.TryGetValue(clientId, out string requestedRace))
            {
                _pendingClientRaces.Remove(clientId);

                Vector3 spawnPos = SpawnManager.Instance != null ? SpawnManager.Instance.DefaultSpawnPosition : Vector3.zero;
                Quaternion spawnRot = SpawnManager.Instance != null ? SpawnManager.Instance.DefaultSpawnRotation : Quaternion.identity;

                // Custom manual spawn via loaded Race Data
                RaceSO requestedRaceSO = Resources.Load<RaceSO>($"Data/Races/{requestedRace}") ?? Resources.Load<RaceSO>("Data/Races/Human");

                // Needs the visual prefab associated with the race, or a default fallback
                GameObject visualPrefab = requestedRaceSO != null && requestedRaceSO.character_prefabs != null && requestedRaceSO.character_prefabs.Count > 0 
                                          ? requestedRaceSO.character_prefabs[0] 
                                          : NetworkManager.Singleton.NetworkConfig.PlayerPrefab;

                GameObject playerObj = Instantiate(visualPrefab, spawnPos, spawnRot);

                if (playerObj.TryGetComponent(out Character character))
                {
                    character.NetworkRaceId.Value = new Unity.Collections.FixedString64Bytes(requestedRace);

                    // Pre-generate deterministic name so all clients see the same one
                    GenderType gender = character.CharacterBio != null && character.CharacterBio.IsMale ? GenderType.Male : GenderType.Female;
                    if (requestedRaceSO != null && requestedRaceSO.NameGenerator != null)
                        character.NetworkCharacterName.Value = new Unity.Collections.FixedString64Bytes(requestedRaceSO.NameGenerator.GenerateName(gender));

                    character.NetworkVisualSeed.Value = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
                }

                if (playerObj.TryGetComponent(out Unity.Netcode.NetworkObject netObj))
                {
                    netObj.SpawnAsPlayerObject(clientId, true);
                    Debug.Log($"<color=cyan>[GameSession]</color> Spawned PlayerObject for Client {clientId} with race {requestedRace}");
                }
            }
        }
    }

    private void HandleClientDisconnect(ulong clientId)
    {
        // clientId is LocalClientId when we fail to connect or disconnect
        if (clientId == NetworkManager.Singleton.LocalClientId && !IsHost)
        {
            ShowToast("Disconnected or failed to reach host.", MWI.UI.Notifications.ToastType.Error);
        }
        
        if (NetworkManager.Singleton.IsServer)
        {
            _pendingClientRaces.Remove(clientId);
        }
    }

    private System.Collections.IEnumerator WaitAndStartNetwork()
    {
        // Wait briefly in real-time to guarantee the scene's NavMesh is fully established and objects are Awoken
        yield return new WaitForSecondsRealtime(0.1f);

        if (IsHost)
        {
            StartSolo();
        }
        else
        {
            JoinMultiplayer();
        }
    }

    private void ApprovalCheck(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
    {
        // Diagnostic: scan the server's spawned NetworkObjects for broken entries BEFORE
        // NGO runs its SynchronizeNetworkObjects loop. Any destroyed-but-still-tracked
        // NetworkObject with a null NetworkManagerOwner field will NRE inside
        // NetworkObject.Serialize during sync and kill the whole client approval.
        // Purge them here so the join succeeds and we log which one was broken.
        PurgeBrokenSpawnedNetworkObjects();

        // Approve all connections
        response.Approved = true;

        // Disable automatic player spawning so we can instantiate custom prefabs with custom settings!
        response.CreatePlayerObject = false;

        string requestedRace = "Human";
        if (request.Payload != null && request.Payload.Length > 0)
        {
            requestedRace = System.Text.Encoding.ASCII.GetString(request.Payload);
        }

        // Store it for when the client fully connects
        _pendingClientRaces[request.ClientNetworkId] = requestedRace;
    }

    // Cached reflection accessors for NGO internals.
    // `NetworkManagerOwner` is the internal field that becomes null when a NetworkObject
    // is "half-spawned" — in the spawn list but missing its manager reference. The public
    // `NetworkObject.NetworkManager` property silently falls back to `NetworkManager.Singleton`
    // so it does NOT expose this state. We read the field directly + also try to invoke
    // `Serialize` on each entry so any NRE-inducing combination is caught.
    private static System.Reflection.FieldInfo s_networkManagerOwnerField;
    private static System.Reflection.MethodInfo s_serializeMethod;
    private static System.Reflection.FieldInfo GetNetworkManagerOwnerField()
    {
        if (s_networkManagerOwnerField != null) return s_networkManagerOwnerField;
        s_networkManagerOwnerField = typeof(NetworkObject).GetField(
            "NetworkManagerOwner",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        return s_networkManagerOwnerField;
    }
    private static System.Reflection.MethodInfo GetSerializeMethod()
    {
        if (s_serializeMethod != null) return s_serializeMethod;
        s_serializeMethod = typeof(NetworkObject).GetMethod(
            "Serialize",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(ulong), typeof(bool) },
            modifiers: null);
        return s_serializeMethod;
    }

    /// <summary>
    /// Iterates the server's spawned NetworkObjects and removes any entry that would
    /// make <c>NetworkObject.Serialize</c> NRE during client connection sync.
    /// Two cases catch it:
    /// 1. The GameObject/NetworkObject component has been destroyed (Unity fake-null).
    /// 2. The internal <c>NetworkManagerOwner</c> field is null — which makes the
    ///    <c>NetworkManagerOwner.DistributedAuthorityMode</c> access at
    ///    NetworkObject.cs:3182 throw. The public <c>NetworkManager</c> property on
    ///    NetworkObject falls back to <c>NetworkManager.Singleton</c> and so does
    ///    NOT reveal this state — we reflect into the field directly.
    /// Logs each purged entry with id + name so the underlying despawn/reparenting
    /// bug can be traced to its source.
    /// </summary>
    private void PurgeBrokenSpawnedNetworkObjects()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;

        var spawned = nm.SpawnManager?.SpawnedObjects;
        var spawnedList = nm.SpawnManager?.SpawnedObjectsList;
        if (spawned == null || spawned.Count == 0) return;

        var ownerField = GetNetworkManagerOwnerField();
        var serializeMethod = GetSerializeMethod();

        // Track the NetworkObject reference too — NGO's client-sync iterates SpawnedObjectsList
        // (a HashSet<NetworkObject>) NOT the SpawnedObjects dict, so removing only from the dict
        // leaves the broken entry in the iteration source and the NRE still fires.
        System.Collections.Generic.List<(ulong id, string reason, string name, NetworkObject no)> broken = null;

        // Snapshot the dict keys first to allow mutation inside the loop.
        var keys = new System.Collections.Generic.List<ulong>(spawned.Keys);
        foreach (var id in keys)
        {
            if (!spawned.TryGetValue(id, out var no)) continue;
            string reason = null;
            string noName = "<null>";

            // Unity's overloaded ==null catches both real null and destroyed fake-null.
            if (no == null)
            {
                reason = "NetworkObject reference is null (destroyed?)";
            }
            else
            {
                noName = no.name;
                if (!no.gameObject)
                {
                    reason = "GameObject has been destroyed";
                }
                else if (ownerField != null)
                {
                    try
                    {
                        var owner = ownerField.GetValue(no) as NetworkManager;
                        if (owner == null)
                        {
                            reason = "NetworkManagerOwner field is null";
                        }
                    }
                    catch (System.Exception e)
                    {
                        reason = $"Exception reading NetworkManagerOwner: {e.Message}";
                    }
                }

                // Final catch-all: actually invoke the same Serialize NGO calls during sync.
                // Any field combination that NRE's there gets flagged even if our explicit
                // checks missed it. Serialize has no side effects beyond building the struct.
                if (reason == null && serializeMethod != null)
                {
                    try
                    {
                        serializeMethod.Invoke(no, new object[] { NetworkManager.ServerClientId, false });
                    }
                    catch (System.Reflection.TargetInvocationException tie)
                    {
                        var inner = tie.InnerException ?? tie;
                        reason = $"Serialize probe threw {inner.GetType().Name}: {inner.Message}";
                    }
                    catch (System.Exception e)
                    {
                        reason = $"Serialize probe threw {e.GetType().Name}: {e.Message}";
                    }
                }
            }

            if (reason != null)
            {
                (broken ??= new System.Collections.Generic.List<(ulong, string, string, NetworkObject)>()).Add((id, reason, noName, no));
            }
        }

        // Defense-in-depth sweep: SpawnedObjectsList may contain entries the dict scan
        // missed entirely (Spawn() that populated the HashSet but failed to add to the dict,
        // Object.Destroy without proper Despawn, etc.). Probe every HashSet entry with the
        // SAME checks we use on the dict — null, destroyed gameObject, null NetworkManagerOwner,
        // and the Serialize() probe that NGO's actual sync would invoke.
        System.Collections.Generic.List<(NetworkObject no, string reason, string name)> orphanedListEntries = null;
        if (spawnedList != null)
        {
            foreach (var listEntry in spawnedList)
            {
                string reason = null;
                string entryName = "<null>";

                if (listEntry == null)
                {
                    reason = "HashSet entry is null (destroyed?)";
                }
                else
                {
                    entryName = listEntry.name;
                    if (!listEntry.gameObject)
                    {
                        reason = "HashSet entry GameObject destroyed";
                    }
                    else if (ownerField != null)
                    {
                        try
                        {
                            var owner = ownerField.GetValue(listEntry) as NetworkManager;
                            if (owner == null)
                            {
                                reason = "HashSet entry NetworkManagerOwner is null";
                            }
                        }
                        catch (System.Exception e)
                        {
                            reason = $"HashSet entry Exception reading NetworkManagerOwner: {e.Message}";
                        }
                    }

                    if (reason == null && serializeMethod != null)
                    {
                        try
                        {
                            serializeMethod.Invoke(listEntry, new object[] { NetworkManager.ServerClientId, false });
                        }
                        catch (System.Reflection.TargetInvocationException tie)
                        {
                            var inner = tie.InnerException ?? tie;
                            reason = $"HashSet entry Serialize probe threw {inner.GetType().Name}: {inner.Message}";
                        }
                        catch (System.Exception e)
                        {
                            reason = $"HashSet entry Serialize probe threw {e.GetType().Name}: {e.Message}";
                        }
                    }
                }

                if (reason != null)
                {
                    (orphanedListEntries ??= new System.Collections.Generic.List<(NetworkObject, string, string)>()).Add((listEntry, reason, entryName));
                }
            }
        }

        if (broken == null && orphanedListEntries == null)
        {
            Debug.Log($"[GameSession] Pre-sync scan: {spawned.Count} spawned NetworkObjects (HashSet count: {spawnedList?.Count ?? -1}), none broken. If Serialize still NRE's, the problem isn't in SpawnedObjects — investigate scene NOs or parenting.");
            return;
        }

        if (broken != null)
        {
            foreach (var entry in broken)
            {
                Debug.LogWarning(
                    $"[GameSession] Purging broken spawned NetworkObject id={entry.id} name='{entry.name}' reason='{entry.reason}'. " +
                    "This would have NRE'd NetworkObject.Serialize during client-sync. Trace the despawn/reparenting bug at the source.");
                spawned.Remove(entry.id);
                // CRITICAL: NGO's SceneEventData.AddSpawnedNetworkObjects iterates SpawnManager.SpawnedObjectsList
                // (a HashSet<NetworkObject>), not SpawnedObjects (the dict). Removing only from the dict leaves
                // the broken entry in the iteration source and Serialize still NRE's during client sync.
                spawnedList?.Remove(entry.no);
            }
        }

        if (orphanedListEntries != null)
        {
            foreach (var orphan in orphanedListEntries)
            {
                Debug.LogWarning(
                    $"[GameSession] Purging orphaned SpawnedObjectsList entry name='{orphan.name}' reason='{orphan.reason}'. " +
                    "This would have NRE'd NetworkObject.Serialize during client-sync. Trace the despawn/reparenting bug at the source.");
                spawnedList.Remove(orphan.no);
            }
        }
    }

    /// <summary>
    /// Transport tuning for content-heavy worlds. The default
    /// <see cref="Unity.Netcode.Transports.UTP.UnityTransport.MaxPacketQueueSize"/> of 128
    /// overflows on client connect when the server blasts the initial snapshot for
    /// a loaded save (many scene-placed NetworkObjects + spawned buildings + NPCs +
    /// WorldItems). Overflow drops spawn packets → clients log
    /// "Receive queue is full" followed by "[Deferred OnSpawn] ... NetworkObject was
    /// not received within 10s" 10 seconds later. Set a high queue + long spawn
    /// timeout at session start so every entry point (Solo/Host/Client) benefits.
    /// Values chosen empirically to cover a 28-root scene + ~50 spawned dynamic
    /// NetworkObjects with plenty of headroom; bump higher if you hit the warning
    /// again with a larger save.
    /// </summary>
    private const int TRANSPORT_MAX_PACKET_QUEUE_SIZE = 4096;
    private const float NETWORK_SPAWN_TIMEOUT_SECONDS = 30f;

    private void ApplyTransportTuning()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;

        nm.NetworkConfig.SpawnTimeout = NETWORK_SPAWN_TIMEOUT_SECONDS;

        var transport = nm.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
        if (transport != null)
        {
            transport.MaxPacketQueueSize = TRANSPORT_MAX_PACKET_QUEUE_SIZE;
        }
    }

    public void StartSolo()
    {
        ApplyTransportTuning();

        var transport = NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
        if (transport != null)
        {
            transport.SetConnectionData("127.0.0.1", 7777, "0.0.0.0");
        }

        NetworkManager.Singleton.NetworkConfig.ConnectionData = System.Text.Encoding.ASCII.GetBytes(SelectedPlayerRace);

        if (NetworkManager.Singleton.StartHost())
        {
            Debug.Log("<color=green>[GameSession]</color> Started Solo / Host Mode");
        }
    }

    public void JoinMultiplayer()
    {
        if (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsHost)
        {
            NetworkManager.Singleton.Shutdown();
            ShowToast("Resetting connection... Try joining again.", MWI.UI.Notifications.ToastType.Warning);
            return;
        }

        ApplyTransportTuning();

        var transport = NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
        if (transport != null)
        {
            string cleanIP = TargetIP.Contains(":") ? TargetIP.Split(':')[0] : TargetIP;
            transport.SetConnectionData(cleanIP, TargetPort);
        }

        NetworkManager.Singleton.NetworkConfig.ConnectionData = System.Text.Encoding.ASCII.GetBytes(SelectedPlayerRace);

        // Show the loading overlay and spin up the driver BEFORE StartClient — the driver
        // hooks NetworkManager.OnClientStarted in its OnEnable, so it must exist before the
        // event fires. The driver self-destructs on connect/disconnect/cancel.
        MWI.UI.Loading.LoadingOverlay.Instance?.Show("Joining game…");
        var loadingDriverGo = new GameObject("NetworkConnectionLoadingDriver");
        var loadingDriver = loadingDriverGo.AddComponent<MWI.UI.Loading.NetworkConnectionLoadingDriver>();
        loadingDriver.RegisterCancelHandler();

        if (NetworkManager.Singleton.StartClient())
        {
            Debug.Log("<color=cyan>[GameSession]</color> Started Client Mode");
        }
        else
        {
            ShowToast("Failed to start client.", MWI.UI.Notifications.ToastType.Error);
            MWI.UI.Loading.LoadingOverlay.Instance?.ShowFailure("Failed to start client");
        }
    }

    [Header("UI Notifications")]
    [SerializeField] private MWI.UI.Notifications.ToastNotificationChannel _generalToastChannel;

    private void ShowToast(string message, MWI.UI.Notifications.ToastType type)
    {
        if (_generalToastChannel != null)
        {
            _generalToastChannel.Raise(new MWI.UI.Notifications.ToastNotificationPayload(
                message: message,
                type: type,
                duration: 4f,
                icon: null
            ));
        }
        else
        {
            Debug.LogWarning("[GameSession] Toast channel not assigned in Inspector.");
        }
    }
}
