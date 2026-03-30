// Assets/Scripts/Core/SaveLoad/SaveManager.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using MWI.WorldSystem;

public class SaveManager : MonoBehaviour
{
    public static SaveManager Instance { get; private set; }

    [SerializeField] private int currentWorldSlot = 0;

    /// <summary>
    /// The unique GUID of the currently loaded world instance.
    /// Generated once at world creation, persisted across save/load cycles.
    /// Used by subsystems (e.g., CharacterRelation) to scope data to a specific world.
    /// </summary>
    public string CurrentWorldGuid { get; private set; }

    // World Saveables registers here (e.g. TimeManager, BuildingManager).
    // Character components do NOT register here.
    private readonly List<ISaveable> worldSaveables = new List<ISaveable>();

    public event Action OnSaveStarted;
    public event Action<int> OnSaveCompleted;
    public event Action<int> OnLoadStarted;
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
            _hostProfileSaved = true;
            Debug.Log("<color=green>[SaveManager]</color> Host player profile saved on shutdown.");
        }
    }

    public void RegisterWorldSaveable(ISaveable s) { if (!worldSaveables.Contains(s)) worldSaveables.Add(s); }
    public void UnregisterWorldSaveable(ISaveable s) => worldSaveables.Remove(s);

    public async Task SaveWorldAsync(int slot = -1)
    {
        if (slot < 0) slot = currentWorldSlot;
        OnSaveStarted?.Invoke();

        var data = new GameSaveData();
        data.metadata.slotIndex = slot;
        data.metadata.worldName = "My World";
        data.metadata.timestamp = DateTime.Now.ToString("o");
        data.metadata.isEmpty = false;

        // Ensure the world has a persistent GUID
        if (string.IsNullOrEmpty(CurrentWorldGuid))
        {
            CurrentWorldGuid = Guid.NewGuid().ToString("N");
        }
        data.metadata.worldGuid = CurrentWorldGuid;

        foreach (var s in worldSaveables)
        {
            data.worldStates[s.SaveKey] = JsonConvert.SerializeObject(s.CaptureState());
        }

        await SaveFileHandler.WriteWorldAsync(slot, data);
        currentWorldSlot = slot;
        OnSaveCompleted?.Invoke(slot);
        
        Debug.Log($"<color=green>[SaveManager]</color> World Slot {slot} saved successfully.");
    }

    public async Task LoadWorldAsync(int slot = -1)
    {
        if (slot < 0) slot = currentWorldSlot;
        OnLoadStarted?.Invoke(slot);

        var data = await SaveFileHandler.ReadWorldAsync(slot);
        if (data == null)
        {
            Debug.LogWarning($"[SaveManager] World Slot {slot} empty or corrupt -- starting fresh.");
            return;
        }

        // Restore the world's persistent GUID
        CurrentWorldGuid = data.metadata.worldGuid;

        foreach (var s in worldSaveables)
        {
            if (!data.worldStates.TryGetValue(s.SaveKey, out string json)) continue;
            var stateType = s.CaptureState().GetType();
            var state = JsonConvert.DeserializeObject(json, stateType);
            s.RestoreState(state);
        }

        currentWorldSlot = slot;
        RegisterPredefinedMaps();
        OnLoadCompleted?.Invoke();
        Debug.Log($"<color=green>[SaveManager]</color> World Slot {slot} loaded successfully.");
    }

    private void RegisterPredefinedMaps()
    {
        // Find all hand-placed MapControllers in the scene
        var allMaps = UnityEngine.Object.FindObjectsByType<MapController>(FindObjectsSortMode.None);
        
        foreach (var map in allMaps)
        {
            if (!map.IsPredefinedMap) continue;

            var tracker = MWI.WorldSystem.CommunityTracker.Instance;
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
