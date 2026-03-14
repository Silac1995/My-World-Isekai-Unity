using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SaveManager : MonoBehaviour
{
    public static SaveManager Instance { get; private set; }

    [SerializeField] private int currentSlot = 0;

    private readonly List<ISaveable> saveables = new List<ISaveable>();

    // -- Events -------------------------------------------------------------
    public event Action       OnSaveStarted;
    public event Action<int>  OnSaveCompleted;   // slot index
    public event Action<int>  OnLoadStarted;     // slot index
    public event Action       OnLoadCompleted;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // -- Project Hook: Sleep Trigger ----------------------------------------

    /// <summary>
    /// Call this from CharacterSleepAction.OnApplyEffect or OnFinish.
    /// Restricted to Player and Host.
    /// </summary>
    public async Task SaveOnSleep(Character character)
    {
        if (!character.IsPlayer()) return;
        if (!IsHost()) return;
        
        await SaveAsync();
    }

    // -- Registration -------------------------------------------------------

    public void Register(ISaveable s)   { if (!saveables.Contains(s)) saveables.Add(s); }
    public void Unregister(ISaveable s) => saveables.Remove(s);

    // -- Save ---------------------------------------------------------------

    public async Task SaveAsync(int slot = -1)
    {
        if (slot < 0) slot = currentSlot;
        OnSaveStarted?.Invoke();

        var data = new GameSaveData();
        data.metadata.slotIndex = slot;
        data.metadata.sceneName = SceneManager.GetActiveScene().name;
        data.metadata.timestamp = DateTime.Now.ToString("o");
        data.metadata.isEmpty   = false;

        foreach (var s in saveables)
            data.systemStates[s.SaveKey] = JsonConvert.SerializeObject(s.CaptureState());

        await SaveFileHandler.WriteAsync(slot, data);
        currentSlot = slot;
        OnSaveCompleted?.Invoke(slot);
    }

    // -- Load ---------------------------------------------------------------

    public async Task LoadAsync(int slot = -1)
    {
        if (slot < 0) slot = currentSlot;
        OnLoadStarted?.Invoke(slot);

        var data = await SaveFileHandler.ReadAsync(slot);
        if (data == null)
        {
            Debug.LogWarning($"[SaveManager] Slot {slot} empty or corrupt -- starting fresh.");
            return;
        }

        data = MigrateIfNeeded(data);

        foreach (var s in saveables)
        {
            if (!data.systemStates.TryGetValue(s.SaveKey, out string json)) continue;
            var stateType = s.CaptureState().GetType();
            var state     = JsonConvert.DeserializeObject(json, stateType);
            s.RestoreState(state);
        }

        currentSlot = slot;
        OnLoadCompleted?.Invoke();
    }

    // -- Migration ----------------------------------------------------------

    private GameSaveData MigrateIfNeeded(GameSaveData data)
    {
        // Increment cases as your schema evolves.
        return data;
    }

    // -- Networking ---------------------------------------------------------

    private bool IsHost() => /* Implement based on Multiplayer skill check */;
}
