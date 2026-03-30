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

## Save Triggers
- Solo: bed/sleep only
- Multiplayer: portal gate (outbound saves before, return overwrites)
- Host shutdown: host profile saved
- Crash/disconnect: no save — revert to last checkpoint

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

## Dependencies
- Newtonsoft.Json
- NGO 2.10+
- CharacterArchetype system (for archetypeId-based spawning)
