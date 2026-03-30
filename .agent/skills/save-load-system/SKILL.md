# Save/Load System

## Purpose
Portable character profiles that travel across worlds. Characters are independent local JSON files loaded into any session.

## Architecture

### Interfaces
- `ICharacterSaveData` (non-generic) — base for coordinator discovery: `SaveKey`, `LoadPriority`, `SerializeToJson()`, `DeserializeFromJson()`
- `ICharacterSaveData<T>` (generic) — typed contract subsystems implement: `Serialize()`, `Deserialize(T)`
- `IOfflineCatchUp` — macro-simulation catch-up (orthogonal to save/load)
- `ISaveable` — world-scoped saves only (TimeManager, CommunityTracker, etc.)

### Key Classes
- `CharacterDataCoordinator` — orchestrates export/import, lives on root Character GO
- `CharacterProfileSaveData` — the portable profile DTO (characterGuid, originWorldGuid, componentStates, partyMembers)
- `SaveFileHandler` — atomic async file I/O to `Profiles/{characterGuid}.json`
- `CharacterSaveDataHelper` — static JSON bridge for ICharacterSaveData<T>

### Load Priority Order
| Priority | Systems |
|----------|---------|
| 0 | CharacterProfile |
| 10 | CharacterStats |
| 20 | CharacterSkills, CharacterAbilities |
| 30 | CharacterEquipment |
| 40 | CharacterNeeds, CharacterTraits |
| 50 | CharacterRelation, CharacterBookKnowledge |
| 60 | CharacterParty, CharacterCommunity, CharacterJob, CharacterSchedule |
| 70 | CharacterMapTracker, CharacterCombat |

## GUID-Based World Saves
- World files stored as `Worlds/{worldGuid}.json` (replaces slot-based `world_0.json`)
- `SaveFileHandler.GetAllWorlds()` scans the `Worlds/` directory and returns all available world saves
- `SaveManager.CurrentWorldGuid` and `CurrentWorldName` track the active world at runtime

## WorldAssociation
- `CharacterProfileSaveData.worldAssociations` tracks the character's position per world (keyed by world GUID)
- Updated in `CharacterDataCoordinator.SaveLocalProfileAsync()` whenever the character saves
- Used by character selection UI to show whether a character has a save from the selected world

## Active Map NPC Snapshots
- `MapController.SnapshotActiveNPCs()` serializes live NPCs on active maps without despawning them
- `MapController.ActiveControllers` — static `HashSet` that tracks currently active map controllers
- `MapController.PendingSnapshots` — stores NPC snapshots for init-time consumption during save/load
- Ensures NPCs on active (non-hibernated) maps persist through save/load cycles

## GameLauncher
- Singleton orchestrator for the full game load sequence
- Sets `GameSessionManager` flags, loads the target scene, waits for player spawn
- Imports the character profile, positions the player via `WorldAssociation`, spawns party NPCs
- Entry point: `GameLauncher.Launch()`

## Save Triggers
- Bed/sleep: saves both world + character
- Map transition: saves both world + character
- Host shutdown: saves both world + character
- All triggers go through `SaveManager.SaveWorldAsync()` + `CharacterDataCoordinator.SaveLocalProfileAsync()`
- Crash/disconnect: no save — revert to last checkpoint

## World Save Menu Flow
- Main Menu → World Select → Character Select → `GameLauncher.Launch()`
- **World creation:** name + optional seed
- **Character creation:** random race/gender/visual (placeholder for future customization)
- **Deletion:** confirmation popup for both worlds and characters via `DeleteConfirmPopup`

## Adding a New Saveable Subsystem
1. Create a DTO class in `Assets/Scripts/Character/SaveLoad/ProfileSaveData/`
2. Implement `ICharacterSaveData<YourDTO>` on the subsystem
3. Set `SaveKey` (unique string) and `LoadPriority` (see table above)
4. Implement `Serialize()` and `Deserialize()`
5. Add bridge methods: `string ICharacterSaveData.SerializeToJson() => CharacterSaveDataHelper.SerializeToJson(this);`
6. Test via ContextMenu on CharacterDataCoordinator: Debug Save/Load

## Abandoned NPC System
- When party leader disconnects: NPCs flagged `IsAbandoned` with `FormerPartyLeaderId`
- Duplicate NPCs can coexist (portal copy + abandoned copy)
- Reclaim interaction: abandoned NPC destroyed, portal copy stays
- `FindByUUID()` prefers non-abandoned; `FindAbandonedByFormerLeader()` for reclaim

## File Locations
- `Assets/Scripts/Character/SaveLoad/ICharacterSaveData.cs`
- `Assets/Scripts/Character/SaveLoad/CharacterSaveDataBase.cs`
- `Assets/Scripts/Character/SaveLoad/CharacterDataCoordinator.cs`
- `Assets/Scripts/Character/SaveLoad/ProfileSaveData/` — all DTOs
- `Assets/Scripts/Core/SaveLoad/CharacterProfileSaveData.cs`
- `Assets/Scripts/Core/SaveLoad/SaveFileHandler.cs`
- `Assets/Scripts/Core/SaveLoad/GameSaveData.cs`
- `Assets/Scripts/Character/Abandoned/ReclaimNPCInteraction.cs`
- `Assets/Scripts/Core/GameLauncher.cs`
- `Assets/Scripts/Core/SaveLoad/WorldAssociation.cs`
- `Assets/Scripts/UI/WorldSelect/` — world select UI panel, world entry, world creation
- `Assets/Scripts/UI/CharacterSelect/` — character select UI panel, character entry, character creation
- `Assets/Scripts/UI/Common/DeleteConfirmPopup.cs`

## Dependencies
- Newtonsoft.Json
- NGO 2.10+
- CharacterArchetype system (for archetypeId-based spawning)
