# World Save Menu Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement a world selection/creation menu and character selection menu that lets players pick or create a world, pick or create a character, and load into the game with full save/load support.

**Architecture:** GUID-based world saves replace slot-based system. WorldAssociation on character profiles tracks position per world. GameLauncher orchestrates the full load sequence. Three critical bug fixes ensure NPC identity and relationship persistence.

**Tech Stack:** Unity 6, NGO 2.10+, Canvas UI + TMP, Newtonsoft.Json, async/await

**Spec:** `docs/superpowers/specs/2026-03-30-world-save-menu-design.md`

---

## File Structure

### Create
| File | Purpose |
|------|---------|
| `Assets/Scripts/Core/SaveLoad/WorldAssociation.cs` | WorldAssociation data class |
| `Assets/Scripts/Core/GameLauncher.cs` | Orchestrates load sequence: scene, world, character, positioning |
| `Assets/Scripts/UI/WorldSelect/WorldSelectPanel.cs` | World list UI panel |
| `Assets/Scripts/UI/WorldSelect/WorldSelectEntry.cs` | Single world entry in list |
| `Assets/Scripts/UI/WorldSelect/CreateWorldPopup.cs` | World creation modal |
| `Assets/Scripts/UI/CharacterSelect/CharacterSelectPanel.cs` | Character list UI panel |
| `Assets/Scripts/UI/CharacterSelect/CharacterSelectEntry.cs` | Single character entry in list |
| `Assets/Scripts/UI/CharacterSelect/CreateCharacterPopup.cs` | Random character creation popup |
| `Assets/Scripts/UI/Common/DeleteConfirmPopup.cs` | Reusable delete confirmation popup |

### Modify
| File | What Changes |
|------|-------------|
| `Assets/Scripts/Core/SaveLoad/GameSaveData.cs` | Add worldSeed, worldVersion to SaveSlotMetadata |
| `Assets/Scripts/Core/SaveLoad/CharacterProfileSaveData.cs` | Add worldAssociations list |
| `Assets/Scripts/Core/SaveLoad/SaveFileHandler.cs` | GUID-based world paths, GetAllWorlds() |
| `Assets/Scripts/Core/SaveLoad/SaveManager.cs` | GUID-based save/load, CurrentWorldGuid property, world save on shutdown |
| `Assets/Scripts/Character/SaveLoad/CharacterDataCoordinator.cs` | Update SaveLocalProfileAsync to set WorldAssociation |
| `Assets/Scripts/UI/MainMenu.cs` | Wire Start Game to WorldSelectPanel, translate French comments |
| `Assets/Scripts/World/MapSystem/MapController.cs` | Fix CharacterId on WakeUp, add active-map NPC snapshot |
| `Assets/Scripts/Character/Character.cs` | Add static OnCharacterSpawned event |
| `Assets/Scripts/Character/CharacterRelation/CharacterRelation.cs` | Subscribe to OnCharacterSpawned, resolve dormant relationships |

---

## Task 1: Critical Bug Fix — NPC CharacterId Restoration on WakeUp

**Files:**
- Modify: `Assets/Scripts/World/MapSystem/MapController.cs`

**Ref:** Spec Section 6a

This is the most critical fix — without it, NPC identities break on every map wake-up. Must be done first.

- [ ] **Step 1: Read MapController.cs WakeUp() method**

Find the NPC spawn loop (~lines 714-799). Note where `NetworkRaceId`, `NetworkCharacterName`, and `NetworkVisualSeed` are set before `netObj.Spawn()`.

- [ ] **Step 2: Add NetworkCharacterId restoration**

In the NPC spawn loop, after the existing NetworkVariable assignments (around line 778) and before `netObj.Spawn()` (around line 796), add:

```csharp
if (!string.IsNullOrEmpty(npcData.CharacterId))
    spawnedChar.NetworkCharacterId.Value = new Unity.Collections.FixedString64Bytes(npcData.CharacterId);
```

- [ ] **Step 3: Compile and verify**

- [ ] **Step 4: Commit**

```bash
git commit -m "fix: restore NPC CharacterId on map WakeUp to preserve identity across hibernation"
```

---

## Task 2: Critical Bug Fix — Active Map NPC Snapshot on Save

**Files:**
- Modify: `Assets/Scripts/World/MapSystem/MapController.cs`
- Modify: `Assets/Scripts/Core/SaveLoad/SaveManager.cs`

**Ref:** Spec Section 6b

- [ ] **Step 1: Read MapController.cs Hibernate() method**

Find the NPC serialization loop (~lines 460-536). This is the pattern we need to replicate without the despawn step.

- [ ] **Step 2: Add a SnapshotNPCs() method to MapController**

Create a public method that serializes live NPCs into `MapSaveData` WITHOUT despawning them:

```csharp
/// <summary>
/// Captures a snapshot of all live NPCs on this map into MapSaveData
/// without despawning them. Called during world save when the map is active.
/// </summary>
public MapSaveData SnapshotActiveNPCs()
{
    var saveData = new MapSaveData
    {
        MapId = MapId,
        LastHibernationTime = TimeManager.Instance.CurrentDay + TimeManager.Instance.CurrentTime01
    };

    // Reuse the same NPC serialization logic from Hibernate()
    // but WITHOUT despawning. Use physics overlap or tracked NPC list.
    // Iterate all Characters in map bounds, skip players, serialize each.

    return saveData;
}
```

The exact implementation should mirror the serialization loop in `Hibernate()` (lines 460-536) — same `HibernatedNPCData` construction, same fields captured — but skip the despawn step at lines 527-534.

**Note:** `MapController` does not currently have static `ActiveControllers` or `PendingSnapshots` collections. These must be created:
- `public static HashSet<MapController> ActiveControllers` — populated in `OnEnable`/`OnDisable`
- `public static Dictionary<string, MapSaveData> PendingSnapshots` — populated during `LoadWorldAsync`, consumed during map initialization

- [ ] **Step 3: Make MapController implement ISaveable**

Or, alternatively, have `SaveManager` iterate all active `MapController` instances and call `SnapshotActiveNPCs()` during `SaveWorldAsync()`. The snapshot data should be stored in the `GameSaveData.worldStates` dictionary keyed by `"MapController_{MapId}"`.

- [ ] **Step 4: Update SaveManager.SaveWorldAsync() to capture active map snapshots**

After iterating all registered `ISaveable` systems, also iterate all active `MapController` instances:

```csharp
// After existing ISaveable serialization loop:
foreach (var mc in MapController.ActiveControllers)
{
    if (mc.IsActive) // has players, not hibernated
    {
        var snapshot = mc.SnapshotActiveNPCs();
        data.worldStates[$"MapSnapshot_{mc.MapId}"] = JsonConvert.SerializeObject(snapshot);
    }
}
```

- [ ] **Step 5: Update SaveManager.LoadWorldAsync() to restore active map snapshots**

During load, find snapshot entries and feed them to the corresponding MapControllers so NPCs are spawned on wake-up:

```csharp
// After existing ISaveable restoration loop:
foreach (var key in data.worldStates.Keys)
{
    if (key.StartsWith("MapSnapshot_"))
    {
        string mapId = key.Substring("MapSnapshot_".Length);
        var snapshot = JsonConvert.DeserializeObject<MapSaveData>(data.worldStates[key]);
        // Store for MapController to consume when it initializes
        MapController.PendingSnapshots[mapId] = snapshot;
    }
}
```

MapController should check `PendingSnapshots` during initialization and spawn NPCs from the snapshot data.

- [ ] **Step 6: Compile and verify**

- [ ] **Step 7: Commit**

```bash
git commit -m "fix: snapshot active map NPCs on world save to prevent NPC loss on reload"
```

---

## Task 3: Critical Bug Fix — Dormant Relationship Activation

**Files:**
- Modify: `Assets/Scripts/Character/Character.cs`
- Modify: `Assets/Scripts/Character/CharacterRelation/CharacterRelation.cs`

**Ref:** Spec Section 6c

- [ ] **Step 1: Add static OnCharacterSpawned event to Character.cs**

In the `#region Events` section (~line 185), add a static event:

```csharp
// Static event for global spawn notification (used by relationship resolution)
public static event Action<Character> OnCharacterSpawned;
```

In `OnNetworkSpawn()` (~line 339), fire it after initialization is complete (at the end of the method):

```csharp
OnCharacterSpawned?.Invoke(this);
```

- [ ] **Step 2: Subscribe CharacterRelation to OnCharacterSpawned**

In `CharacterRelation`, subscribe in `OnEnable()` and unsubscribe in `OnDisable()`:

```csharp
protected override void OnEnable()
{
    base.OnEnable();
    Character.OnCharacterSpawned += HandleCharacterSpawned;
}

protected override void OnDisable()
{
    Character.OnCharacterSpawned -= HandleCharacterSpawned;
    base.OnDisable();
}
```

- [ ] **Step 3: Implement HandleCharacterSpawned**

```csharp
private void HandleCharacterSpawned(Character spawnedCharacter)
{
    if (_dormantRelationships == null || _dormantRelationships.Count == 0) return;

    for (int i = _dormantRelationships.Count - 1; i >= 0; i--)
    {
        var dormant = _dormantRelationships[i];
        if (dormant.targetCharacterId == spawnedCharacter.CharacterId)
        {
            // Resolve dormant entry into a live Relationship
            // Use existing AddRelationship or direct list manipulation
            // matching the pattern in Deserialize()
            _dormantRelationships.RemoveAt(i);
        }
    }
}
```

Adapt to match the actual `Relationship` class constructor and `_relationships` list structure in the file.

- [ ] **Step 4: Compile and verify**

- [ ] **Step 5: Commit**

```bash
git commit -m "fix: resolve dormant relationships when NPCs spawn via OnCharacterSpawned event"
```

---

## Task 4: Foundation — WorldAssociation + Data Model Updates

**Files:**
- Create: `Assets/Scripts/Core/SaveLoad/WorldAssociation.cs`
- Modify: `Assets/Scripts/Core/SaveLoad/CharacterProfileSaveData.cs`
- Modify: `Assets/Scripts/Core/SaveLoad/GameSaveData.cs`

**Ref:** Spec Sections 2, 3

- [ ] **Step 1: Create WorldAssociation.cs**

```csharp
// Assets/Scripts/Core/SaveLoad/WorldAssociation.cs

[System.Serializable]
public class WorldAssociation
{
    public string worldGuid;
    public string worldName;
    public string lastMapId;
    public float positionX, positionY, positionZ;
    public string lastPlayed;
}
```

- [ ] **Step 2: Add worldAssociations to CharacterProfileSaveData**

Read `Assets/Scripts/Core/SaveLoad/CharacterProfileSaveData.cs`, then add:

```csharp
// After partyMembers list:
public List<WorldAssociation> worldAssociations = new List<WorldAssociation>();
```

- [ ] **Step 3: Update SaveSlotMetadata in GameSaveData.cs**

Read `Assets/Scripts/Core/SaveLoad/GameSaveData.cs`, then add to `SaveSlotMetadata`:

```csharp
public string worldSeed;
public int worldVersion = 1;
```

- [ ] **Step 4: Compile and verify**

- [ ] **Step 5: Commit**

```bash
git commit -m "feat(save): add WorldAssociation data model and update SaveSlotMetadata"
```

---

## Task 5: Foundation — SaveFileHandler GUID-Based World Saves

**Files:**
- Modify: `Assets/Scripts/Core/SaveLoad/SaveFileHandler.cs`

**Ref:** Spec Section 2

- [ ] **Step 1: Read SaveFileHandler.cs fully**

- [ ] **Step 2: Replace slot-based world methods with GUID-based**

```csharp
// Replace:
public static string WorldSlotPath(int slot) => Path.Combine(WorldSaveDir, $"world_{slot}.json");

// With:
public static string WorldPath(string worldGuid) => Path.Combine(WorldSaveDir, $"{worldGuid}.json");
```

Update all world methods to use `string worldGuid` parameter instead of `int slot`:
- `WriteWorldAsync(string worldGuid, GameSaveData data)`
- `ReadWorldAsync(string worldGuid)`
- `DeleteWorldAsync(string worldGuid)`
- `WorldExists(string worldGuid)`

Keep the same atomic write pattern (.tmp swap).

- [ ] **Step 3: Add GetAllWorlds()**

```csharp
public static List<GameSaveData> GetAllWorlds()
{
    var worlds = new List<GameSaveData>();
    if (!Directory.Exists(WorldSaveDir)) return worlds;

    foreach (string file in Directory.GetFiles(WorldSaveDir, "*.json"))
    {
        try
        {
            string json = File.ReadAllText(file);
            var world = JsonConvert.DeserializeObject<GameSaveData>(json);
            if (world != null) worlds.Add(world);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SaveFileHandler] Failed to read world {file}: {e.Message}");
        }
    }

    return worlds;
}
```

- [ ] **Step 4: Compile — SaveManager will break (expected, fixed in Task 6)**

- [ ] **Step 5: Commit**

```bash
git commit -m "feat(save): switch SaveFileHandler to GUID-based world saves with GetAllWorlds()"
```

---

## Task 6: Foundation — SaveManager GUID Migration + World Save on Shutdown

**Files:**
- Modify: `Assets/Scripts/Core/SaveLoad/SaveManager.cs`

**Ref:** Spec Sections 2, 7

- [ ] **Step 1: Read SaveManager.cs fully**

Note `SaveWorldAsync()`, `LoadWorldAsync()`, `currentWorldSlot`, `CurrentWorldGuid`, and `SaveHostPlayerProfileOnShutdown()`.

- [ ] **Step 2: Replace slot-based with GUID-based**

- Replace `private int currentWorldSlot` with `public string CurrentWorldGuid { get; set; }` (public setter needed by GameLauncher for new worlds)
- Add `public string CurrentWorldName { get; set; }` — set during `LoadWorldAsync()` from metadata, and by GameLauncher on new world creation
- Update `SaveWorldAsync()` to use `CurrentWorldGuid`:
  - Remove slot parameter
  - Use `SaveFileHandler.WriteWorldAsync(CurrentWorldGuid, data)` instead of slot-based
  - Keep all ISaveable serialization logic unchanged
- Update `LoadWorldAsync()` to accept `string worldGuid`:
  - Use `SaveFileHandler.ReadWorldAsync(worldGuid)` instead of slot-based
  - Set `CurrentWorldGuid = worldGuid` after successful load
  - Keep ISaveable restoration and `RegisterPredefinedMaps()` unchanged
- Update all internal references from slot to GUID
- Update events: `OnSaveCompleted` and `OnLoadStarted` may need signature changes (string instead of int)

- [ ] **Step 3: Add world save to host shutdown**

In `SaveHostPlayerProfileOnShutdown()`, add a world save call:

```csharp
// Save world alongside profile
if (!string.IsNullOrEmpty(CurrentWorldGuid))
{
    _ = SaveWorldAsync();
}
```

- [ ] **Step 4: Fix any remaining compile errors from the slot→GUID migration**

Search for all callers of `SaveWorldAsync` and `LoadWorldAsync` in the codebase and update them.

- [ ] **Step 5: Compile and verify**

- [ ] **Step 6: Commit**

```bash
git commit -m "feat(save): migrate SaveManager from slot-based to GUID-based world saves"
```

---

## Task 7: WorldAssociation Integration in CharacterDataCoordinator

**Files:**
- Modify: `Assets/Scripts/Character/SaveLoad/CharacterDataCoordinator.cs`

**Ref:** Spec Section 3

- [ ] **Step 1: Read CharacterDataCoordinator.cs**

Find `SaveLocalProfileAsync()` and `ExportProfile()`.

- [ ] **Step 2: Update SaveLocalProfileAsync() to set WorldAssociation**

After calling `ExportProfile()` and before writing to disk, update the world association:

```csharp
public async Task SaveLocalProfileAsync()
{
    var profile = ExportProfile();
    if (string.IsNullOrEmpty(profile.characterGuid)) return;

    // Update WorldAssociation for current world
    string currentWorldGuid = SaveManager.Instance?.CurrentWorldGuid;
    if (!string.IsNullOrEmpty(currentWorldGuid))
    {
        var association = profile.worldAssociations.Find(w => w.worldGuid == currentWorldGuid);
        if (association == null)
        {
            association = new WorldAssociation();
            profile.worldAssociations.Add(association);
        }

        association.worldGuid = currentWorldGuid;
        association.worldName = SaveManager.Instance?.CurrentWorldName ?? "";
        association.lastPlayed = System.DateTime.Now.ToString("o");

        // Get current position from CharacterMapTracker
        if (_character.TryGet<CharacterMapTracker>(out var tracker))
        {
            association.lastMapId = tracker.CurrentMapID.Value.ToString();
            association.positionX = _character.transform.position.x;
            association.positionY = _character.transform.position.y;
            association.positionZ = _character.transform.position.z;
        }
    }

    await SaveFileHandler.WriteProfileAsync(profile.characterGuid, profile);
    Debug.Log($"<color=cyan>[CharacterDataCoordinator]</color> Profile '{profile.characterName}' ({profile.characterGuid}) saved to disk.");
}
```

Note: `SaveManager.Instance.CurrentWorldName` may not exist yet — check the actual property name. May need to read it from `GameSaveData.metadata.worldName`.

- [ ] **Step 3: Compile and verify**

- [ ] **Step 4: Commit**

```bash
git commit -m "feat(save): update CharacterDataCoordinator to set WorldAssociation on save"
```

---

## Task 8: World Save Triggers — Save World Alongside Character

**Files:**
- Modify: `Assets/Scripts/Character/AIBehaviour/SleepBehaviour.cs`
- Search and modify: Portal gate / map transition scripts (find via grep for portal, teleport, or map transition)

**Ref:** Spec Section 2 (World Save Triggers)

- [ ] **Step 1: Read SleepBehaviour.cs**

Find where the character profile save was added (in the previous character persistence work).

- [ ] **Step 2: Add world save call alongside character save in SleepBehaviour**

Where `CharacterDataCoordinator.SaveLocalProfileAsync()` is called, also call `SaveManager.Instance.SaveWorldAsync()`:

```csharp
if (IsServer && _character.IsPlayer())
{
    var coordinator = _character.GetComponent<CharacterDataCoordinator>();
    if (coordinator != null)
        _ = coordinator.SaveLocalProfileAsync();

    // Save world alongside character
    if (SaveManager.Instance != null)
        _ = SaveManager.Instance.SaveWorldAsync();
}
```

- [ ] **Step 3: Find portal gate / map transition scripts**

Search for portal gate, teleport, or multiplayer transition scripts:
```
grep -r "portal\|PortalGate\|MapTransition\|teleport" Assets/Scripts/ --include="*.cs" -l
```

- [ ] **Step 4: Add world + character save to portal gate transition**

At the portal gate outbound trigger (before connecting to another world), add the same save pattern: save character profile + save world. This ensures the pre-portal checkpoint includes both world and character state.

- [ ] **Step 5: Compile and verify**

- [ ] **Step 6: Commit**

```bash
git commit -m "feat(save): save world alongside character on bed/sleep and portal gate triggers"
```

---

## Task 9: GameLauncher — Load Sequence Orchestrator

**Files:**
- Create: `Assets/Scripts/Core/GameLauncher.cs`

**Ref:** Spec Section 5

- [ ] **Step 1: Read GameSessionManager.cs to understand network setup**

Note the static flags: `AutoStartNetwork`, `IsHost`, `SelectedPlayerRace`.

- [ ] **Step 2: Create GameLauncher.cs**

```csharp
// Assets/Scripts/Core/GameLauncher.cs
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Orchestrates the full game load sequence:
/// scene load → world restore → character spawn → positioning → player switch.
/// Called from the character selection UI after both world and character are chosen.
/// </summary>
public class GameLauncher : MonoBehaviour
{
    public static GameLauncher Instance { get; private set; }

    [SerializeField] private string _gameSceneName = "GameScene";
    [SerializeField] private Transform _defaultSpawnPoint;

    // Set by the menu flow before calling Launch()
    public string SelectedWorldGuid { get; set; }
    public string SelectedCharacterGuid { get; set; }
    public bool IsNewWorld { get; set; }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void Launch()
    {
        // Set network flags for solo/host mode
        GameSessionManager.AutoStartNetwork = true;
        GameSessionManager.IsHost = true;

        // Load game scene, then continue setup
        SceneManager.sceneLoaded += OnGameSceneLoaded;
        SceneManager.LoadScene(_gameSceneName);
    }

    private async void OnGameSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != _gameSceneName) return;
        SceneManager.sceneLoaded -= OnGameSceneLoaded;

        // Wait for NetworkManager to be ready
        await WaitForNetworkReady();

        // 1. Load world save
        if (!IsNewWorld && !string.IsNullOrEmpty(SelectedWorldGuid))
        {
            await SaveManager.Instance.LoadWorldAsync(SelectedWorldGuid);
        }
        else if (!string.IsNullOrEmpty(SelectedWorldGuid))
        {
            SaveManager.Instance.CurrentWorldGuid = SelectedWorldGuid;
        }

        // 2. Fade out
        if (ScreenFadeManager.Instance != null)
            ScreenFadeManager.Instance.FadeOut(0.5f);
        await Task.Delay(500);

        // 3. Load character profile
        var profileData = await SaveFileHandler.ReadProfileAsync(SelectedCharacterGuid);
        if (profileData == null)
        {
            Debug.LogError("[GameLauncher] Failed to load character profile!");
            return;
        }

        // 4. Spawn character — bypass GameSessionManager's auto-spawn by using manual spawn
        // GameSessionManager sets CreatePlayerObject = false, so we control spawning.
        // Load the archetype prefab and spawn it as player object.
        var prefab = SpawnManager.Instance.GetCharacterPrefab(profileData.archetypeId);
        if (prefab == null) prefab = SpawnManager.Instance.DefaultCharacterPrefab;
        var charObj = Instantiate(prefab);
        var netObj = charObj.GetComponent<NetworkObject>();
        netObj.SpawnAsPlayerObject(NetworkManager.Singleton.LocalClientId, true);

        // 5. Import profile into the spawned character
        var character = charObj.GetComponent<Character>();
        var coordinator = charObj.GetComponent<CharacterDataCoordinator>();
        coordinator.ImportProfile(profileData);

        // 6. Spawn party NPCs from profile
        foreach (var partyMember in profileData.partyMembers)
        {
            var npcPrefab = SpawnManager.Instance.GetCharacterPrefab(partyMember.archetypeId);
            if (npcPrefab == null) npcPrefab = SpawnManager.Instance.DefaultCharacterPrefab;
            var npcObj = Instantiate(npcPrefab);
            npcObj.GetComponent<NetworkObject>().Spawn(true);
            var npcCoordinator = npcObj.GetComponent<CharacterDataCoordinator>();
            npcCoordinator.ImportProfile(partyMember);
        }

        // 7. Position character based on WorldAssociation
        var association = profileData.worldAssociations?.Find(w => w.worldGuid == SelectedWorldGuid);
        if (association != null && !string.IsNullOrEmpty(association.lastMapId))
        {
            // Resume at saved position
            var pos = new Vector3(association.positionX, association.positionY, association.positionZ);
            character.GetComponentInChildren<CharacterMovement>()?.Warp(pos);
        }
        else if (_defaultSpawnPoint != null)
        {
            character.GetComponentInChildren<CharacterMovement>()?.Warp(_defaultSpawnPoint.position);
        }

        // 8. Switch to player
        character.SwitchToPlayer();

        // 9. Fade in
        if (ScreenFadeManager.Instance != null)
            ScreenFadeManager.Instance.FadeIn(0.5f);

        Debug.Log($"<color=green>[GameLauncher]</color> Game launched with world {SelectedWorldGuid} and character {SelectedCharacterGuid}");
    }

    private async Task WaitForNetworkReady()
    {
        while (Unity.Netcode.NetworkManager.Singleton == null || !Unity.Netcode.NetworkManager.Singleton.IsServer)
        {
            await Task.Yield();
        }
    }
}
```

**Important:** The exact character spawn integration depends heavily on how `GameSessionManager.HandleClientConnected()` spawns players. The implementer must read that method and adapt. The existing flow spawns a player object with race data — this needs to be extended to use the profile's `archetypeId` and then call `ImportProfile()` on the spawned character.

- [ ] **Step 3: Compile and verify**

- [ ] **Step 4: Commit**

```bash
git commit -m "feat: create GameLauncher to orchestrate world+character load sequence"
```

---

## Task 10: UI — DeleteConfirmPopup (Reusable)

**Files:**
- Create: `Assets/Scripts/UI/Common/DeleteConfirmPopup.cs`

**Ref:** Spec Section 4

- [ ] **Step 1: Create DeleteConfirmPopup.cs**

```csharp
// Assets/Scripts/UI/Common/DeleteConfirmPopup.cs
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Reusable delete confirmation popup.
/// Show(name, onConfirm) displays "Are you sure you want to delete {name}?"
/// </summary>
public class DeleteConfirmPopup : MonoBehaviour
{
    [SerializeField] private TMP_Text _messageText;
    [SerializeField] private Button _yesButton;
    [SerializeField] private Button _noButton;

    private Action _onConfirm;

    private void Awake()
    {
        _yesButton.onClick.AddListener(OnYesClicked);
        _noButton.onClick.AddListener(OnNoClicked);
        gameObject.SetActive(false);
    }

    public void Show(string name, Action onConfirm)
    {
        _messageText.text = $"Are you sure you want to delete \"{name}\"?";
        _onConfirm = onConfirm;
        gameObject.SetActive(true);
    }

    private void OnYesClicked()
    {
        _onConfirm?.Invoke();
        gameObject.SetActive(false);
    }

    private void OnNoClicked()
    {
        gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        _yesButton.onClick.RemoveListener(OnYesClicked);
        _noButton.onClick.RemoveListener(OnNoClicked);
    }
}
```

- [ ] **Step 2: Create the UI prefab in Unity Editor**

Using MCP tools, create a Canvas child with:
- Background overlay (semi-transparent black)
- Panel with message text (TMP), Yes button, No button
- Save as prefab at `Assets/UI/Menu/DeleteConfirmPopup.prefab`

- [ ] **Step 3: Commit**

```bash
git commit -m "feat(ui): create reusable DeleteConfirmPopup"
```

---

## Task 11: UI — WorldSelectPanel + WorldSelectEntry

**Files:**
- Create: `Assets/Scripts/UI/WorldSelect/WorldSelectPanel.cs`
- Create: `Assets/Scripts/UI/WorldSelect/WorldSelectEntry.cs`

**Ref:** Spec Section 4, Panel 1

- [ ] **Step 1: Create WorldSelectEntry.cs**

```csharp
// Assets/Scripts/UI/WorldSelect/WorldSelectEntry.cs
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class WorldSelectEntry : MonoBehaviour
{
    [SerializeField] private TMP_Text _worldNameText;
    [SerializeField] private TMP_Text _lastPlayedText;
    [SerializeField] private Button _selectButton;
    [SerializeField] private Button _deleteButton;

    private string _worldGuid;
    private Action<string> _onSelect;
    private Action<string, string> _onDelete; // guid, name

    public void Setup(string worldGuid, string worldName, string lastPlayed,
                      Action<string> onSelect, Action<string, string> onDelete)
    {
        _worldGuid = worldGuid;
        _worldNameText.text = worldName;
        _lastPlayedText.text = lastPlayed;
        _onSelect = onSelect;
        _onDelete = onDelete;

        _selectButton.onClick.AddListener(() => _onSelect?.Invoke(_worldGuid));
        _deleteButton.onClick.AddListener(() => _onDelete?.Invoke(_worldGuid, worldName));
    }

    private void OnDestroy()
    {
        _selectButton.onClick.RemoveAllListeners();
        _deleteButton.onClick.RemoveAllListeners();
    }
}
```

- [ ] **Step 2: Create WorldSelectPanel.cs**

```csharp
// Assets/Scripts/UI/WorldSelect/WorldSelectPanel.cs
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class WorldSelectPanel : MonoBehaviour
{
    [SerializeField] private Transform _worldListContainer;
    [SerializeField] private GameObject _worldEntryPrefab;
    [SerializeField] private Button _createWorldButton;
    [SerializeField] private Button _backButton;
    [SerializeField] private TMP_Text _emptyStateText;
    [SerializeField] private CreateWorldPopup _createWorldPopup;
    [SerializeField] private DeleteConfirmPopup _deleteConfirmPopup;
    [SerializeField] private CharacterSelectPanel _characterSelectPanel;

    private List<GameSaveData> _worlds = new List<GameSaveData>();

    private void OnEnable()
    {
        RefreshWorldList();
    }

    private void Awake()
    {
        _createWorldButton.onClick.AddListener(OnCreateWorldClicked);
        _backButton.onClick.AddListener(OnBackClicked);
    }

    public void RefreshWorldList()
    {
        // Clear existing entries
        foreach (Transform child in _worldListContainer)
            Destroy(child.gameObject);

        _worlds = SaveFileHandler.GetAllWorlds();

        if (_worlds.Count == 0)
        {
            _emptyStateText.gameObject.SetActive(true);
            _emptyStateText.text = "No worlds yet";
        }
        else
        {
            _emptyStateText.gameObject.SetActive(false);
            foreach (var world in _worlds)
            {
                var entry = Instantiate(_worldEntryPrefab, _worldListContainer)
                    .GetComponent<WorldSelectEntry>();
                entry.Setup(
                    world.metadata.worldGuid,
                    world.metadata.worldName,
                    world.metadata.timestamp,
                    OnWorldSelected,
                    OnWorldDeleteRequested
                );
            }
        }
    }

    private void OnWorldSelected(string worldGuid)
    {
        _characterSelectPanel.Show(worldGuid,
            _worlds.Find(w => w.metadata.worldGuid == worldGuid)?.metadata.worldName ?? "");
        gameObject.SetActive(false);
    }

    private void OnWorldDeleteRequested(string worldGuid, string worldName)
    {
        _deleteConfirmPopup.Show(worldName, () =>
        {
            _ = SaveFileHandler.DeleteWorldAsync(worldGuid);
            RefreshWorldList();
        });
    }

    private void OnCreateWorldClicked()
    {
        _createWorldPopup.Show(OnWorldCreated);
    }

    private void OnWorldCreated(string worldGuid, string worldName)
    {
        // After creation, go to character select for this new world
        _characterSelectPanel.Show(worldGuid, worldName, isNewWorld: true);
        gameObject.SetActive(false);
    }

    private void OnBackClicked()
    {
        gameObject.SetActive(false);
        // Return to main menu (parent handles this)
    }

    private void OnDestroy()
    {
        _createWorldButton.onClick.RemoveListener(OnCreateWorldClicked);
        _backButton.onClick.RemoveListener(OnBackClicked);
    }
}
```

- [ ] **Step 3: Compile and verify**

- [ ] **Step 4: Commit**

```bash
git commit -m "feat(ui): create WorldSelectPanel and WorldSelectEntry"
```

---

## Task 12: UI — CreateWorldPopup

**Files:**
- Create: `Assets/Scripts/UI/WorldSelect/CreateWorldPopup.cs`

**Ref:** Spec Section 4, Panel 2 + Section 5 (New World Creation Flow)

- [ ] **Step 1: Create CreateWorldPopup.cs**

```csharp
// Assets/Scripts/UI/WorldSelect/CreateWorldPopup.cs
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Newtonsoft.Json;

public class CreateWorldPopup : MonoBehaviour
{
    [SerializeField] private TMP_InputField _worldNameInput;
    [SerializeField] private TMP_InputField _seedInput;
    [SerializeField] private Button _createButton;
    [SerializeField] private Button _cancelButton;

    private Action<string, string> _onCreated; // worldGuid, worldName

    private void Awake()
    {
        _createButton.onClick.AddListener(OnCreateClicked);
        _cancelButton.onClick.AddListener(OnCancelClicked);
        gameObject.SetActive(false);
    }

    public void Show(Action<string, string> onCreated)
    {
        _onCreated = onCreated;
        _worldNameInput.text = "";
        _seedInput.text = "";
        gameObject.SetActive(true);
    }

    private async void OnCreateClicked()
    {
        string worldName = _worldNameInput.text.Trim();
        if (string.IsNullOrEmpty(worldName)) return;

        string worldGuid = Guid.NewGuid().ToString("N");
        string seed = string.IsNullOrEmpty(_seedInput.text.Trim())
            ? UnityEngine.Random.Range(0, int.MaxValue).ToString()
            : _seedInput.text.Trim();

        var saveData = new GameSaveData
        {
            metadata = new SaveSlotMetadata
            {
                worldGuid = worldGuid,
                worldName = worldName,
                displayName = worldName,
                worldSeed = seed,
                isEmpty = false,
                timestamp = DateTime.Now.ToString("o")
            }
        };

        await SaveFileHandler.WriteWorldAsync(worldGuid, saveData);

        gameObject.SetActive(false);
        _onCreated?.Invoke(worldGuid, worldName);
    }

    private void OnCancelClicked()
    {
        gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        _createButton.onClick.RemoveListener(OnCreateClicked);
        _cancelButton.onClick.RemoveListener(OnCancelClicked);
    }
}
```

- [ ] **Step 2: Commit**

```bash
git commit -m "feat(ui): create CreateWorldPopup for new world creation"
```

---

## Task 13: UI — CharacterSelectPanel + CharacterSelectEntry + CreateCharacterPopup

**Files:**
- Create: `Assets/Scripts/UI/CharacterSelect/CharacterSelectPanel.cs`
- Create: `Assets/Scripts/UI/CharacterSelect/CharacterSelectEntry.cs`
- Create: `Assets/Scripts/UI/CharacterSelect/CreateCharacterPopup.cs`

**Ref:** Spec Section 4, Panel 3 + Section 5 (New Character Creation Flow)

- [ ] **Step 1: Create CharacterSelectEntry.cs**

Same pattern as `WorldSelectEntry` but with character data:
- Shows character name
- Shows "Has a save from this world" if WorldAssociation matches
- Select and Delete buttons

- [ ] **Step 2: Create CharacterSelectPanel.cs**

```csharp
public class CharacterSelectPanel : MonoBehaviour
{
    [SerializeField] private Transform _characterListContainer;
    [SerializeField] private GameObject _characterEntryPrefab;
    [SerializeField] private Button _createCharacterButton;
    [SerializeField] private Button _backButton;
    [SerializeField] private TMP_Text _worldNameText;
    [SerializeField] private TMP_Text _emptyStateText;
    [SerializeField] private CreateCharacterPopup _createCharacterPopup;
    [SerializeField] private DeleteConfirmPopup _deleteConfirmPopup;

    private string _selectedWorldGuid;
    private string _selectedWorldName;
    private bool _isNewWorld;

    public void Show(string worldGuid, string worldName, bool isNewWorld = false)
    {
        _selectedWorldGuid = worldGuid;
        _selectedWorldName = worldName;
        _isNewWorld = isNewWorld;
        _worldNameText.text = $"World: {worldName}";
        gameObject.SetActive(true);
        RefreshCharacterList();
    }

    private void RefreshCharacterList()
    {
        foreach (Transform child in _characterListContainer)
            Destroy(child.gameObject);

        var profiles = SaveFileHandler.GetAllProfiles();

        if (profiles.Count == 0)
        {
            _emptyStateText.gameObject.SetActive(true);
            _emptyStateText.text = "No characters yet";
        }
        else
        {
            _emptyStateText.gameObject.SetActive(false);
            foreach (var profile in profiles)
            {
                bool hasWorldSave = profile.worldAssociations != null &&
                    profile.worldAssociations.Exists(w => w.worldGuid == _selectedWorldGuid);

                var entry = Instantiate(_characterEntryPrefab, _characterListContainer)
                    .GetComponent<CharacterSelectEntry>();
                entry.Setup(
                    profile.characterGuid,
                    profile.characterName,
                    hasWorldSave,
                    OnCharacterSelected,
                    OnCharacterDeleteRequested
                );
            }
        }
    }

    private void OnCharacterDeleteRequested(string charGuid, string charName)
    {
        _deleteConfirmPopup.Show(charName, () =>
        {
            _ = SaveFileHandler.DeleteProfileAsync(charGuid);
            RefreshCharacterList();
        });
    }

    private void OnCharacterSelected(string characterGuid)
    {
        // Launch the game via GameLauncher
        GameLauncher.Instance.SelectedWorldGuid = _selectedWorldGuid;
        GameLauncher.Instance.SelectedCharacterGuid = characterGuid;
        GameLauncher.Instance.IsNewWorld = _isNewWorld;
        GameLauncher.Instance.Launch();
    }
}
```

- [ ] **Step 3: Create CreateCharacterPopup.cs**

Random character creation:
- Name input field
- On create: generate random race, gender, visual seed, stats
- Write profile to disk
- Callback to refresh list

```csharp
private async void OnCreateClicked()
{
    string charName = _nameInput.text.Trim();
    if (string.IsNullOrEmpty(charName)) return;

    string charGuid = Guid.NewGuid().ToString("N");

    // Random race from available RaceSOs
    var races = Resources.LoadAll<RaceSO>("Data/Race");
    var race = races.Length > 0 ? races[UnityEngine.Random.Range(0, races.Length)] : null;

    // Randomize identity
    var race = races.Length > 0 ? races[UnityEngine.Random.Range(0, races.Length)] : null;
    bool isMale = UnityEngine.Random.value > 0.5f;
    int visualSeed = UnityEngine.Random.Range(0, int.MaxValue);

    var profile = new CharacterProfileSaveData
    {
        characterGuid = charGuid,
        characterName = charName,
        archetypeId = "Human", // Default archetype — determines prefab to spawn
        timestamp = DateTime.Now.ToString("o")
    };

    // Store randomized identity in CharacterProfile component state
    var profileData = new ProfileSaveData
    {
        raceId = race != null ? race.name : "",
        gender = isMale ? 0 : 1,
        visualSeed = visualSeed,
        archetypeId = "Human"
    };
    profile.componentStates["CharacterProfile"] = Newtonsoft.Json.JsonConvert.SerializeObject(profileData);

    await SaveFileHandler.WriteProfileAsync(charGuid, profile);

    gameObject.SetActive(false);
    _onCreated?.Invoke(charGuid);
}
```

- [ ] **Step 4: Compile and verify**

- [ ] **Step 5: Commit**

```bash
git commit -m "feat(ui): create CharacterSelectPanel, CharacterSelectEntry, and CreateCharacterPopup"
```

---

## Task 14: UI — Wire MainMenu + Create Prefabs

**Files:**
- Modify: `Assets/Scripts/UI/MainMenu.cs`
- Create UI prefabs via MCP

**Ref:** Spec Sections 4, 8

- [ ] **Step 1: Read MainMenu.cs**

- [ ] **Step 2: Translate French comments to English** (CLAUDE.md rule 23)

- [ ] **Step 3: Wire "Start Game" button to WorldSelectPanel**

Replace or augment `StartSolo()` to show the WorldSelectPanel instead of directly loading the game scene:

```csharp
[SerializeField] private WorldSelectPanel _worldSelectPanel;

public void StartGame()
{
    _worldSelectPanel.gameObject.SetActive(true);
    // Hide main menu buttons or the main menu panel
}
```

Update the button reference: `btnStartSolo` should call `StartGame()` instead of `StartSolo()`.

- [ ] **Step 4: Create UI prefabs in Unity Editor**

Using MCP tools or manual setup, create prefabs for:
- WorldSelectPanel (with ScrollRect, entry container, buttons)
- WorldSelectEntry (name text, timestamp text, select/delete buttons)
- CharacterSelectPanel (world name header, ScrollRect, entry container, buttons)
- CharacterSelectEntry (name text, world association text, select/delete buttons)
- CreateWorldPopup (name input, seed input, create/cancel buttons)
- CreateCharacterPopup (name input, create/cancel buttons)
- DeleteConfirmPopup (message text, yes/no buttons)

Place all in `Assets/UI/Menu/`.

- [ ] **Step 5: Wire prefab references in the MainMenuScene**

Add all panels as children of the MainMenu Canvas. Wire SerializeField references.

- [ ] **Step 6: Compile, enter play mode, test the full flow**

- [ ] **Step 7: Commit**

```bash
git commit -m "feat(ui): wire MainMenu to WorldSelectPanel, create UI prefabs, translate French comments"
```

---

## Task 15: Integration — End-to-End Verification

**Files:**
- No new files — verification only

- [ ] **Step 1: Test world creation flow**

1. Enter play mode
2. Click "Start Game" → World Select Panel appears
3. Click "Create New World" → enter name + optional seed → Create
4. Verify JSON file created in `Application.persistentDataPath/Worlds/`
5. Character Select Panel appears

- [ ] **Step 2: Test character creation flow**

1. Click "Create Random Character" → enter name → Create
2. Verify JSON file created in `Application.persistentDataPath/Profiles/`
3. Game loads

- [ ] **Step 3: Test save + reload**

1. In-game, use bed/sleep to trigger save
2. Verify both world and character JSON files updated
3. Quit play mode
4. Re-enter play mode → Start Game → select same world → select same character
5. Verify "Has a save from this world" text appears
6. Load → verify character resumes at saved position

- [ ] **Step 4: Test NPC identity preservation**

1. Load a world with NPCs
2. Leave the map (NPCs hibernate) → return (NPCs wake up)
3. Verify NPC CharacterIds are preserved (check logs or inspector)

- [ ] **Step 5: Test delete functionality**

1. Create a throwaway world → delete it → verify file removed
2. Create a throwaway character → delete it → verify file removed

- [ ] **Step 6: Commit any fixes found during verification**

---

## Task 16: Documentation — Update SKILL.md

**Files:**
- Modify: `.agent/skills/save-load-system/SKILL.md`

- [ ] **Step 1: Update SKILL.md with world save menu details**

Add sections covering:
- GUID-based world saves (replacing slot system)
- WorldAssociation for cross-world character tracking
- GameLauncher load sequence
- World + character save triggers (save together)
- Active map NPC snapshots
- OnCharacterSpawned event for dormant relationship resolution

- [ ] **Step 2: Commit**

```bash
git commit -m "docs: update save-load-system SKILL.md with world save menu details"
```

---

## Summary

| Task | Description | Files |
|------|-------------|-------|
| 1 | Bug fix: NPC CharacterId on WakeUp | 1 file |
| 2 | Bug fix: Active map NPC snapshot on save | 2 files |
| 3 | Bug fix: Dormant relationship activation | 2 files |
| 4 | WorldAssociation + data model updates | 3 files |
| 5 | SaveFileHandler GUID-based world saves | 1 file |
| 6 | SaveManager GUID migration + shutdown save | 1 file |
| 7 | WorldAssociation in CharacterDataCoordinator | 1 file |
| 8 | World save trigger (bed/sleep) | 1 file |
| 9 | GameLauncher orchestrator | 1 file |
| 10 | DeleteConfirmPopup (reusable) | 1 file |
| 11 | WorldSelectPanel + WorldSelectEntry | 2 files |
| 12 | CreateWorldPopup | 1 file |
| 13 | CharacterSelectPanel + Entry + CreatePopup | 3 files |
| 14 | Wire MainMenu + create UI prefabs | 1 file + prefabs |
| 15 | End-to-end verification | 0 files |
| 16 | Documentation | 1 file |

**Total: 16 tasks, ~22 files**

**Execution order:** Tasks 1-3 are independent bug fixes (can parallel). Tasks 4-8 are sequential foundation. Task 9 depends on 6. Tasks 10-13 are sequential UI (each builds on previous). Task 14 depends on 10-13. Task 15 depends on all. Task 16 can parallel with 15.
