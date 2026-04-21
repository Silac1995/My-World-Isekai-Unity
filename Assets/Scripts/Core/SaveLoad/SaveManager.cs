// Assets/Scripts/Core/SaveLoad/SaveManager.cs
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using MWI.WorldSystem;
using MWI.Weather;
using MWI.Terrain;

public class SaveManager : MonoBehaviour
{
    public static SaveManager Instance { get; private set; }

    // ── Save/Load state machine ──────────────────────────────────────
    public enum SaveLoadState { Idle, Saving, Loading }
    public SaveLoadState CurrentState { get; set; } = SaveLoadState.Idle;

    // ── Settling-based readiness ─────────────────────────────────────
    /// <summary>
    /// True once all ISaveables have finished registering (no new registration
    /// within 0.5 s). Systems that need to trigger a save should wait for this.
    /// </summary>
    public bool IsReady { get; private set; }
    public event Action OnReady;
    private Coroutine _settlingCoroutine;

    /// <summary>
    /// The unique GUID of the currently loaded world instance.
    /// Generated once at world creation, persisted across save/load cycles.
    /// Used by subsystems (e.g., CharacterRelation) to scope data to a specific world.
    /// </summary>
    public string CurrentWorldGuid { get; set; }

    /// <summary>
    /// The display name of the currently loaded world, set from metadata on load.
    /// </summary>
    public string CurrentWorldName { get; set; }

    // World Saveables registers here (e.g. TimeManager, BuildingManager).
    // Character components do NOT register here.
    private readonly List<ISaveable> worldSaveables = new List<ISaveable>();

    public event Action OnSaveStarted;
    public event Action<string> OnSaveCompleted;
    public event Action<string> OnLoadStarted;
    public event Action OnLoadCompleted;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnApplicationQuit()
    {
        SaveHostPlayerProfileOnShutdown();
    }

    private void OnDestroy()
    {
        SaveHostPlayerProfileOnShutdown();
    }

    private bool _hostProfileSaved;

    /// <summary>
    /// On host shutdown, save the host's player character profile to disk.
    /// Uses a guard flag to prevent double-saving from both OnApplicationQuit and OnDestroy.
    /// </summary>
    private void SaveHostPlayerProfileOnShutdown()
    {
        if (_hostProfileSaved) return;

        var networkManager = Unity.Netcode.NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsServer) return;

        var localClient = networkManager.LocalClient;
        if (localClient == null || localClient.PlayerObject == null) return;

        var character = localClient.PlayerObject.GetComponent<Character>();
        if (character == null || !character.IsPlayer()) return;

        var coordinator = character.GetComponent<CharacterDataCoordinator>();
        if (coordinator != null)
        {
            _ = coordinator.SaveLocalProfileAsync();
            Debug.Log("<color=green>[SaveManager]</color> Host player profile saved on shutdown.");
        }

        if (!string.IsNullOrEmpty(CurrentWorldGuid))
        {
            _ = SaveWorldDirectAsync();
            Debug.Log("<color=green>[SaveManager]</color> World save triggered on shutdown.");
        }

        _hostProfileSaved = true;
    }

    /// <summary>
    /// Resets all runtime state for a fresh session. Called when returning to main menu.
    /// Clears ISaveable registrations, readiness state, current world, and save/load state.
    /// </summary>
    public void ResetForNewSession()
    {
        worldSaveables.Clear();
        IsReady = false;
        CurrentState = SaveLoadState.Idle;
        CurrentWorldGuid = null;
        CurrentWorldName = null;
        _hostProfileSaved = false;
        if (_settlingCoroutine != null)
        {
            StopCoroutine(_settlingCoroutine);
            _settlingCoroutine = null;
        }
        MapController.PendingSnapshots.Clear();
        MapController.ActiveControllers.Clear();

        // Destroy DontDestroyOnLoad singletons so fresh ones are created from the next GameScene.
        // Without this, stale world data from the previous session bleeds into the next.
        DestroySingleton(MapRegistry.Instance);
        DestroySingleton(WorldOffsetAllocator.Instance);
        DestroySingleton(BuildingInteriorRegistry.Instance);

        // Destroy NetworkManager — NGO auto-applies DontDestroyOnLoad, causing duplicates on scene reload
        if (Unity.Netcode.NetworkManager.Singleton != null)
        {
            Destroy(Unity.Netcode.NetworkManager.Singleton.gameObject);
        }

        Region.ClearRegistry();
        TerrainTypeRegistry.Clear();

        Debug.Log("<color=green>[SaveManager]</color> Reset for new session — world singletons destroyed.");
    }

    private void DestroySingleton(MonoBehaviour singleton)
    {
        if (singleton != null && singleton.gameObject != null)
        {
            Destroy(singleton.gameObject);
        }
    }

    public int WorldSaveableCount => worldSaveables.Count;

    public void RegisterWorldSaveable(ISaveable s)
    {
        if (!worldSaveables.Contains(s)) worldSaveables.Add(s);
        ResetSettlingTimer();
        Debug.Log($"<color=green>[SaveManager]</color> Registered ISaveable '{s.SaveKey}'. Count: {worldSaveables.Count}");
    }

    public void UnregisterWorldSaveable(ISaveable s) => worldSaveables.Remove(s);

    private void ResetSettlingTimer()
    {
        IsReady = false;
        if (_settlingCoroutine != null) StopCoroutine(_settlingCoroutine);
        _settlingCoroutine = StartCoroutine(SettlingRoutine());
    }

    private IEnumerator SettlingRoutine()
    {
        yield return new WaitForSecondsRealtime(0.5f);
        IsReady = true;
        OnReady?.Invoke();
        _settlingCoroutine = null;
        Debug.Log($"<color=green>[SaveManager]</color> All ISaveables settled. IsReady=true. Count: {worldSaveables.Count}");
    }

    // Delay between status updates so the player can read each step
    private static readonly WaitForSecondsRealtime _statusDelay = new WaitForSecondsRealtime(0.2f);

    // ── Orchestrated save flow ────────────────────────────────────────
    /// <summary>
    /// Single entry point for all save triggers (bed checkpoints, portal gates, etc.).
    /// Freezes the game, shows an overlay, saves character profile first then world,
    /// and unfreezes when done. Starts its own coroutine — callers just call RequestSave().
    /// </summary>
    public void RequestSave(Character playerCharacter)
    {
        if (CurrentState != SaveLoadState.Idle)
        {
            Debug.LogWarning("<color=yellow>[SaveManager]</color> RequestSave ignored — already in state: " + CurrentState);
            return;
        }
        StartCoroutine(OrchestratedSaveRoutine(playerCharacter));
    }

    private IEnumerator OrchestratedSaveRoutine(Character playerCharacter)
    {

        CurrentState = SaveLoadState.Saving;
        Time.timeScale = 0f;

        ScreenFadeManager.Instance?.ShowOverlay(0.7f, "Saving...");
        ScreenFadeManager.Instance?.ClearWarnings();
        yield return _statusDelay;

        // ── 1. Save character profile FIRST (most important) ─────────
        var coordinator = playerCharacter != null
            ? playerCharacter.GetComponent<CharacterDataCoordinator>()
            : null;

        if (coordinator != null)
        {
            ScreenFadeManager.Instance?.UpdateStatus("Saving character profile...");
            yield return _statusDelay;
            Task profileTask = null;
            try
            {
                profileTask = coordinator.SaveLocalProfileAsync();
            }
            catch (Exception ex)
            {
                Debug.LogError($"<color=red>[SaveManager]</color> Character profile save threw immediately: {ex.Message}\n{ex.StackTrace}");
                ScreenFadeManager.Instance?.ShowWarning("Failed to save character profile!");
            }

            if (profileTask != null)
            {
                while (!profileTask.IsCompleted) yield return null;

                if (profileTask.IsFaulted)
                {
                    Debug.LogError($"<color=red>[SaveManager]</color> Character profile save faulted: {profileTask.Exception}");
                    ScreenFadeManager.Instance?.ShowWarning("Failed to save character profile!");
                }
                else
                {
                    Debug.Log("<color=green>[SaveManager]</color> Character profile saved successfully.");
                }
            }
        }

        // ── 2. Ensure world GUID exists ──────────────────────────────
        if (string.IsNullOrEmpty(CurrentWorldGuid))
        {
            CurrentWorldGuid = Guid.NewGuid().ToString("N");
        }

        OnSaveStarted?.Invoke();

        // ── 3. Snapshot buildings on active maps ─────────────────────
        ScreenFadeManager.Instance?.UpdateStatus("Syncing buildings...");
        yield return _statusDelay;
        foreach (var mc in MapController.ActiveControllers.ToArray())
        {
            if (mc == null || string.IsNullOrEmpty(mc.MapId)) continue;
            try
            {
                mc.SnapshotActiveBuildings();
            }
            catch (Exception ex)
            {
                Debug.LogError($"<color=red>[SaveManager]</color> Building snapshot failed for map '{mc.MapId}': {ex.Message}\n{ex.StackTrace}");
                ScreenFadeManager.Instance?.ShowWarning($"Buildings snapshot failed: {mc.MapId}");
            }
        }

        // ── 4. Serialize ISaveables ──────────────────────────────────
        var data = new GameSaveData();
        data.metadata.worldGuid = CurrentWorldGuid;
        data.metadata.worldName = !string.IsNullOrEmpty(CurrentWorldName) ? CurrentWorldName : "My World";
        data.metadata.timestamp = DateTime.Now.ToString("o");
        data.metadata.isEmpty = false;

        var jsonSettings = new JsonSerializerSettings
        {
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore
        };

        Debug.Log($"<color=green>[SaveManager]</color> Serializing {worldSaveables.Count} registered ISaveable system(s)...");
        foreach (var s in worldSaveables)
        {
            ScreenFadeManager.Instance?.UpdateStatus($"Saving {s.SaveKey}...");
            yield return _statusDelay;
            try
            {
                data.worldStates[s.SaveKey] = JsonConvert.SerializeObject(s.CaptureState(), jsonSettings);
                Debug.Log($"<color=green>[SaveManager]</color>   Captured '{s.SaveKey}'.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"<color=red>[SaveManager]</color> FAILED to capture '{s.SaveKey}': {ex.Message}\n{ex.StackTrace}");
                ScreenFadeManager.Instance?.ShowWarning($"Failed: {s.SaveKey}");
            }
        }

        // ── 5. Snapshot NPCs on active maps ──────────────────────────
        ScreenFadeManager.Instance?.UpdateStatus("Saving NPCs...");
        yield return _statusDelay;
        foreach (var mc in MapController.ActiveControllers.ToArray())
        {
            if (mc == null || string.IsNullOrEmpty(mc.MapId)) continue;
            try
            {
                var snapshot = mc.SnapshotActiveNPCs();
                if (snapshot.HibernatedNPCs.Count > 0)
                {
                    data.worldStates[$"MapSnapshot_{mc.MapId}"] = JsonConvert.SerializeObject(snapshot, jsonSettings);
                    Debug.Log($"<color=green>[SaveManager]</color> Captured NPC snapshot for active map '{mc.MapId}': {snapshot.HibernatedNPCs.Count} NPCs.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"<color=red>[SaveManager]</color> NPC snapshot failed for map '{mc.MapId}': {ex.Message}\n{ex.StackTrace}");
                ScreenFadeManager.Instance?.ShowWarning($"NPC snapshot failed: {mc.MapId}");
            }
        }

        // ── 6. Write world file to disk ──────────────────────────────
        ScreenFadeManager.Instance?.UpdateStatus("Writing world file...");
        yield return _statusDelay;
        Task writeTask = null;
        try
        {
            writeTask = SaveFileHandler.WriteWorldAsync(CurrentWorldGuid, data);
        }
        catch (Exception ex)
        {
            Debug.LogError($"<color=red>[SaveManager]</color> WriteWorldAsync threw immediately: {ex.Message}\n{ex.StackTrace}");
            ScreenFadeManager.Instance?.ShowWarning("Failed to write world file!");
        }

        if (writeTask != null)
        {
            while (!writeTask.IsCompleted) yield return null;

            if (writeTask.IsFaulted)
            {
                Debug.LogError($"<color=red>[SaveManager]</color> WriteWorldAsync faulted: {writeTask.Exception}");
                ScreenFadeManager.Instance?.ShowWarning("Failed to write world file!");
            }
        }

        OnSaveCompleted?.Invoke(CurrentWorldGuid);
        Debug.Log($"<color=green>[SaveManager]</color> World '{CurrentWorldName}' ({CurrentWorldGuid}) saved successfully.");

        // ── 7. Brief "Save complete!" hold, then unfreeze ────────────
        ScreenFadeManager.Instance?.UpdateStatus("Save complete!");
        yield return new WaitForSecondsRealtime(0.5f);

        Time.timeScale = 1f;
        ScreenFadeManager.Instance?.HideOverlay(0.3f);
        CurrentState = SaveLoadState.Idle;
    }

    // ── Direct save (no overlay / no freeze) — shutdown only ─────────
    private async Task SaveWorldDirectAsync()
    {
        // Ensure the world has a persistent GUID
        if (string.IsNullOrEmpty(CurrentWorldGuid))
        {
            CurrentWorldGuid = Guid.NewGuid().ToString("N");
        }

        OnSaveStarted?.Invoke();

        var data = new GameSaveData();
        data.metadata.worldGuid = CurrentWorldGuid;
        data.metadata.worldName = !string.IsNullOrEmpty(CurrentWorldName) ? CurrentWorldName : "My World";
        data.metadata.timestamp = DateTime.Now.ToString("o");
        data.metadata.isEmpty = false;

        // Sync live buildings on active maps into CommunityData BEFORE capturing ISaveable states.
        // This ensures MapRegistry.CaptureState() includes buildings placed since last hibernation.
        foreach (var mc in MapController.ActiveControllers.ToArray())
        {
            if (mc != null && !string.IsNullOrEmpty(mc.MapId))
                mc.SnapshotActiveBuildings();
        }

        // Settings to handle Unity types (Vector3.normalized creates self-referencing loops)
        var jsonSettings = new JsonSerializerSettings
        {
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore
        };

        Debug.Log($"<color=green>[SaveManager]</color> Serializing {worldSaveables.Count} registered ISaveable system(s)...");
        foreach (var s in worldSaveables)
        {
            try
            {
                data.worldStates[s.SaveKey] = JsonConvert.SerializeObject(s.CaptureState(), jsonSettings);
                Debug.Log($"<color=green>[SaveManager]</color>   Captured '{s.SaveKey}'.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"<color=red>[SaveManager]</color> FAILED to capture '{s.SaveKey}': {ex.Message}\n{ex.StackTrace}");
            }
        }

        // Snapshot live NPCs on active maps so they persist through save/load.
        // NPCs are serialized into MapSaveData snapshots WITHOUT despawning.
        foreach (var mc in MapController.ActiveControllers.ToArray())
        {
            if (mc == null || string.IsNullOrEmpty(mc.MapId)) continue;

            var snapshot = mc.SnapshotActiveNPCs();
            if (snapshot.HibernatedNPCs.Count > 0)
            {
                data.worldStates[$"MapSnapshot_{mc.MapId}"] = JsonConvert.SerializeObject(snapshot, jsonSettings);
                Debug.Log($"<color=green>[SaveManager]</color> Captured NPC snapshot for active map '{mc.MapId}': {snapshot.HibernatedNPCs.Count} NPCs.");
            }
        }

        await SaveFileHandler.WriteWorldAsync(CurrentWorldGuid, data);
        OnSaveCompleted?.Invoke(CurrentWorldGuid);

        Debug.Log($"<color=green>[SaveManager]</color> World '{CurrentWorldName}' ({CurrentWorldGuid}) saved successfully.");
    }

    public async Task LoadWorldAsync(string worldGuid)
    {
        if (string.IsNullOrEmpty(worldGuid))
        {
            Debug.LogError("[SaveManager] LoadWorldAsync called with null or empty worldGuid.");
            return;
        }

        OnLoadStarted?.Invoke(worldGuid);

        var data = await SaveFileHandler.ReadWorldAsync(worldGuid);
        if (data == null)
        {
            Debug.LogWarning($"[SaveManager] World '{worldGuid}' empty or corrupt -- starting fresh.");
            return;
        }

        // Restore the world's persistent GUID and name
        CurrentWorldGuid = worldGuid;
        CurrentWorldName = data.metadata.worldName;

        foreach (var s in worldSaveables)
        {
            if (!data.worldStates.TryGetValue(s.SaveKey, out string json)) continue;
            var stateType = s.CaptureState().GetType();
            var state = JsonConvert.DeserializeObject(json, stateType);
            s.RestoreState(state);
        }

        // Extract active map NPC snapshots and store them for MapController to consume on init.
        // This ensures NPCs that were live (not hibernated) at save time are restored on reload.
        MapController.PendingSnapshots.Clear();
        foreach (var key in data.worldStates.Keys)
        {
            if (key.StartsWith("MapSnapshot_"))
            {
                string mapId = key.Substring("MapSnapshot_".Length);
                var snapshot = JsonConvert.DeserializeObject<MapSaveData>(data.worldStates[key]);
                if (snapshot != null)
                {
                    MapController.PendingSnapshots[mapId] = snapshot;
                    Debug.Log($"<color=cyan>[SaveManager]</color> Loaded pending NPC snapshot for map '{mapId}': {snapshot.HibernatedNPCs.Count} NPCs.");
                }
            }
        }

        RegisterPredefinedMaps();
        OnLoadCompleted?.Invoke();
        Debug.Log($"<color=green>[SaveManager]</color> World '{CurrentWorldName}' ({worldGuid}) loaded successfully.");
    }

    private void RegisterPredefinedMaps()
    {
        // Find all hand-placed MapControllers in the scene
        var allMaps = UnityEngine.Object.FindObjectsByType<MapController>(FindObjectsSortMode.None);
        
        foreach (var map in allMaps)
        {
            if (!map.IsPredefinedMap) continue;

            var tracker = MWI.WorldSystem.MapRegistry.Instance;
            if (tracker == null) continue; // Safety check

            // If this map has no existing save entry, create a default one
            if (tracker.GetCommunity(map.MapId) == null)
            {
                var newCommunity = new MWI.WorldSystem.CommunityData()
                {
                    MapId = map.MapId,
                    IsPredefinedMap = true,
                    Tier = MWI.WorldSystem.CommunityTier.RoamingCamp,
                    // Inherit biome resource pool from the MapController's BiomeDefinition
                    ResourcePools = map.Biome != null 
                        ? BuildResourcePool(map.Biome, map) 
                        : new List<MWI.WorldSystem.ResourcePoolEntry>(),
                    ConstructedBuildings = new List<MWI.WorldSystem.BuildingSaveData>()
                };

                tracker.AddCommunity(newCommunity);
                Debug.Log($"<color=cyan>[SaveManager]</color> Registered predefined map '{map.MapId}'.");
            }
            else
            {
                Debug.Log($"<color=cyan>[SaveManager]</color> Predefined map '{map.MapId}' already has save data. Skipping.");
            }
        }
    }

    private List<MWI.WorldSystem.ResourcePoolEntry> BuildResourcePool(BiomeDefinition biome, MapController map)
    {
        var pool = new List<MWI.WorldSystem.ResourcePoolEntry>();
        
        var boxCollider = map.GetComponent<BoxCollider>();
        if (boxCollider == null) return pool;

        float mapArea = boxCollider.bounds.size.x * boxCollider.bounds.size.z;

        foreach (var entry in biome.Harvestables)
        {
            pool.Add(new MWI.WorldSystem.ResourcePoolEntry()
            {
                ResourceId = entry.ResourceId,
                MaxAmount = mapArea * biome.HarvestableDensity * entry.Weight, // using Weight as defined in HarvestableEntry
                CurrentAmount = mapArea * biome.HarvestableDensity * entry.Weight,
                LastHarvestedDay = 0
            });
        }
        return pool;
    }
}
