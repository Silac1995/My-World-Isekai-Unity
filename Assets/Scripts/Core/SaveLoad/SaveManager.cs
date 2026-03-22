// Assets/Scripts/Core/SaveLoad/SaveManager.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

public class SaveManager : MonoBehaviour
{
    public static SaveManager Instance { get; private set; }

    [SerializeField] private int currentWorldSlot = 0;
    
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

        foreach (var s in worldSaveables)
        {
            if (!data.worldStates.TryGetValue(s.SaveKey, out string json)) continue;
            var stateType = s.CaptureState().GetType();
            var state = JsonConvert.DeserializeObject(json, stateType);
            s.RestoreState(state);
        }

        currentWorldSlot = slot;
        OnLoadCompleted?.Invoke();
        Debug.Log($"<color=green>[SaveManager]</color> World Slot {slot} loaded successfully.");
    }
}
