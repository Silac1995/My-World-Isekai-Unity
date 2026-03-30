# World Save Menu — Design Spec

**Date:** 2026-03-30
**Status:** Approved
**Scope:** World selection/creation UI, character selection UI, world-to-character association, loading flow, delete functionality

---

## 1. Overview

A menu flow that lets the player start a game by picking or creating a world, then picking or creating a character. The world and character are independent save files that connect through a `WorldAssociation` list on the character profile. Save triggers (bed/sleep, portal gate) save both world and character together.

### Flow

```
Main Menu → "Start Game" → World Select → Character Select → Load Into Game
```

---

## 2. World Save Data

### GUID-Based World Files

Switch from slot-based to GUID-based world saves:
- **File path:** `Worlds/{worldGuid}.json` (replaces `Worlds/world_{slot}.json`)
- `WorldGuid` generated at world creation via `Guid.NewGuid().ToString("N")`
- `worldSeed` (string) added to `SaveSlotMetadata` for procedural generation

### SaveSlotMetadata (Updated)

```csharp
[System.Serializable]
public class SaveSlotMetadata
{
    public string worldGuid;              // replaces slotIndex as primary key
    public string displayName;            // kept from existing code (same as worldName)
    public string worldName;              // display name
    public string worldSeed;              // seed for procedural generation (stored but inert until procedural gen system exists)
    public int worldVersion = 1;          // for future save format migration
    public float totalPlaytimeSeconds;
    public string timestamp;              // last saved
    public bool isEmpty = true;

    // slotIndex kept for backward compat but no longer the primary key
    public int slotIndex;
}
```

**Note:** `displayName` is kept for backward compatibility with any existing code that references it. New code should use `worldName`.

### SaveFileHandler Changes

- `WorldSlotPath(int slot)` → `WorldPath(string worldGuid)` returning `Worlds/{worldGuid}.json`
- `WriteWorldAsync(int slot, ...)` → `WriteWorldAsync(string worldGuid, ...)`
- `ReadWorldAsync(int slot)` → `ReadWorldAsync(string worldGuid)`
- `DeleteWorldAsync(int slot)` → `DeleteWorldAsync(string worldGuid)`
- `WorldSlotExists(int slot)` → `WorldExists(string worldGuid)`
- Add `GetAllWorlds()` — scans `Worlds/` folder, returns `List<GameSaveData>`. If directory doesn't exist, returns empty list. Corrupt files are skipped with a `Debug.LogWarning`. Same defensive pattern as `GetAllProfiles()`. **Performance note:** This deserializes full `GameSaveData` objects. For large worlds this could be slow — if it becomes a bottleneck, consider a lightweight metadata index file in the future.

### SaveManager Changes

`SaveManager` needs to be updated to use `worldGuid` instead of slot index for all save/load operations:
- `SaveWorldAsync()` signature changes from `SaveWorldAsync()` (using stored slot) to `SaveWorldAsync(string worldGuid)` (or reads from a runtime `CurrentWorldGuid` property)
- `LoadWorldAsync(int slot)` → `LoadWorldAsync(string worldGuid)`
- Add `public string CurrentWorldGuid { get; private set; }` — set during `LoadWorldAsync()` and world creation, read by `CharacterDataCoordinator` when building `WorldAssociation` entries
- `RegisterPredefinedMaps()` is already called inside `LoadWorldAsync()` — this does not change
- `SaveHostPlayerProfileOnShutdown()` must also call `SaveWorldAsync()` to persist the world alongside the host's character profile

### World Save Triggers

World saves at the same time as character saves:
- Bed/sleep
- Portal gate (outbound and return)
- Host shutdown

`SaveManager.SaveWorldAsync()` is called alongside `CharacterDataCoordinator.SaveLocalProfileAsync()`.

---

## 3. Character-to-World Association

### WorldAssociation on CharacterProfileSaveData

```csharp
// Added to CharacterProfileSaveData
public List<WorldAssociation> worldAssociations = new List<WorldAssociation>();

[System.Serializable]
public class WorldAssociation
{
    public string worldGuid;
    public string worldName;
    public string lastMapId;
    public float positionX, positionY, positionZ;
    public string lastPlayed;   // ISO 8601 timestamp
}
```

### How It Works

- When a character saves in a world, `CharacterDataCoordinator.SaveLocalProfileAsync()` reads `SaveManager.Instance.CurrentWorldGuid` and updates (or adds) the `WorldAssociation` entry on the exported profile before writing to disk. This logic lives in `SaveLocalProfileAsync()`, not in `ExportProfile()` itself, since `ExportProfile()` is a pure snapshot operation that shouldn't depend on global state.
- The entry stores the character's current map ID and position
- A character accumulates entries for every world they've visited
- On the character selection screen: check if any `worldAssociations[].worldGuid` matches the selected world
  - **Match:** Show "Has a save from this world" — on load, resume at that entry's position
  - **No match:** Spawn at default spawn point

---

## 4. UI Structure

All panels live in the existing **MainMenuScene** as children of the Main Menu Canvas. Uses Canvas UI (not UI Toolkit) with TMP for text, matching existing project patterns.

### Panel 1: World Select Panel

- Shown when "Start Game" is clicked from Main Menu
- Scrollable list of world entries
- Each entry displays: **world name** + **last played timestamp**
- Each entry has a **Select** button and a **Delete** button
- **"Create New World"** button
- **"Back"** button to return to Main Menu
- **Empty state:** If no worlds exist, the list area shows a message like "No worlds yet" and only the "Create New World" button is visible

### Panel 2: Create World Popup

- Modal/popup over the world select panel
- **World name** input field (TMP_InputField)
- **Seed** input field (optional — auto-generates random seed if left empty)
- **"Create"** and **"Cancel"** buttons
- On create: generates `worldGuid`, writes empty `GameSaveData` to disk, adds entry to list

### Panel 3: Character Select Panel

- Replaces the world select panel after a world is picked
- Shows **selected world name** at the top
- Scrollable list of character entries
- Each entry displays: **character name**
- If character has a `WorldAssociation` matching this world: shows **"Has a save from this world"**
- Each entry has a **Select** button and a **Delete** button
- **"Create Random Character"** button (name input + confirm)
- **"Back"** button to return to world select
- **Empty state:** If no characters exist, the list area shows "No characters yet" and only the "Create Random Character" button is visible

### Delete Confirmation Popup

- Reusable popup: "Are you sure you want to delete {name}?"
- **"Yes"** and **"No"** buttons
- Used for both world deletion and character deletion
- On confirm: deletes the file from disk, removes entry from list
- **Orphaned references:** Deleting a world does NOT clean up `WorldAssociation` entries on character profiles. Those entries become dormant (point to a world that no longer exists) and are harmless — the character selection screen simply won't show "Has a save from this world" for a deleted world. Same applies to deleting a character — no world files are affected.

---

## 5. Loading Flow

After the player selects a world + character:

1. **Fade screen** via `ScreenFadeManager`
2. **Load game scene** (async scene load)
3. **Load world save** — `SaveManager.LoadWorldAsync(worldGuid)` restores all ISaveable world systems (TimeManager, CommunityTracker, WorldOffsetAllocator, BuildingInteriorRegistry). `RegisterPredefinedMaps()` is called automatically inside `LoadWorldAsync()` — no separate step needed.
4. **Spawn character prefab** from `archetypeId` in the character profile
5. **Import character profile** — `CharacterDataCoordinator.ImportProfile()` restores all subsystems in priority order
6. **Spawn party NPCs** from `partyMembers` list, import each profile
7. **Position the character:**
   - Look up `worldAssociations` for an entry matching `worldGuid`
   - **If found:** warp character to saved `lastMapId` + position
   - **If not found:** spawn at a default spawn point (predefined Transform in the scene)
8. **Switch to player** — `Character.SwitchToPlayer()` activates PlayerController + HUD
9. **Fade in**

**Note:** `ScreenFadeManager` is assumed to already have a compatible API for fade in/out. No changes expected.

### New World Creation Flow

1. Player enters world name (+ optional seed)
2. Generate `worldGuid = Guid.NewGuid().ToString("N")`
3. If seed is empty, auto-generate a random seed
4. Create `GameSaveData` with metadata: `worldGuid`, `worldName`, `worldSeed`, `isEmpty = false`, `timestamp`
5. Write to `Worlds/{worldGuid}.json`
6. Proceed to character select

### New Character Creation Flow (Random)

1. Player enters a character name
2. Generate `characterGuid = Guid.NewGuid().ToString("N")`
3. Randomize: race (from available RaceSOs), gender, visual seed, base stats, `archetypeId` (use a default human archetype — the archetype determines the prefab to spawn)
4. Create `CharacterProfileSaveData` with identity fields and default component states
5. Write to `Profiles/{characterGuid}.json`
6. Proceed to load

---

## 6. Map Structure

- **Premade MapControllers** exist in the game scene as scene objects. They are always loaded. Their state (NPCs, buildings, resources) is captured/restored by the world save via `CommunityTracker`, `HibernatedNPCData`, etc.
- **Dynamic MapControllers** (from community promotion, building interiors) are reconstructed from save data. `CommunityTracker.RestoreState()` + `WorldOffsetAllocator.RestoreState()` + `BuildingInteriorRegistry` handle spawning these.
- **World seed** drives procedural generation of dynamic map locations. Premade maps are unaffected by seed.

---

## 7. Files to Create / Modify

### Create
- `Assets/Scripts/UI/WorldSelect/WorldSelectPanel.cs` — world list UI panel
- `Assets/Scripts/UI/WorldSelect/WorldSelectEntry.cs` — single world entry in the list
- `Assets/Scripts/UI/WorldSelect/CreateWorldPopup.cs` — world creation modal
- `Assets/Scripts/UI/CharacterSelect/CharacterSelectPanel.cs` — character list UI panel
- `Assets/Scripts/UI/CharacterSelect/CharacterSelectEntry.cs` — single character entry in the list
- `Assets/Scripts/UI/CharacterSelect/CreateCharacterPopup.cs` — random character creation
- `Assets/Scripts/UI/Common/DeleteConfirmPopup.cs` — reusable delete confirmation
- `Assets/Scripts/Core/SaveLoad/WorldAssociation.cs` — WorldAssociation data class
- `Assets/Scripts/Core/GameLauncher.cs` — orchestrates the load sequence (scene load, world load, character spawn, positioning). Sets `GameSessionManager.AutoStartNetwork = true` and `GameSessionManager.IsHost = true` before loading, replacing the network setup currently in `MainMenu.StartSolo()`. This flow always assumes solo/host mode (multiplayer lobby is out of scope).
- UI Prefabs in `Assets/UI/Menu/` for all panels

### Modify
- `Assets/Scripts/Core/SaveLoad/CharacterProfileSaveData.cs` — add `worldAssociations` list
- `Assets/Scripts/Core/SaveLoad/GameSaveData.cs` — add `worldSeed` to metadata, deprecate `slotIndex`
- `Assets/Scripts/Core/SaveLoad/SaveFileHandler.cs` — GUID-based world paths, `GetAllWorlds()`
- `Assets/Scripts/Core/SaveLoad/SaveManager.cs` — use `worldGuid` instead of slot index, store current worldGuid at runtime
- `Assets/Scripts/Character/SaveLoad/CharacterDataCoordinator.cs` — update `ExportProfile()` to set `WorldAssociation` on save
- `Assets/Scripts/UI/MainMenu.cs` — wire "Start Game" button to open WorldSelectPanel. Also translate existing French comments to English per CLAUDE.md rule 23.

---

## 8. Out of Scope

- Character creation/customization screen (future spec — "Create Random Character" is the placeholder)
- Multiplayer lobby / server browser
- World seed procedural generation logic (this spec only stores the seed)
- Save file encryption or anti-cheat
- Cloud save sync
