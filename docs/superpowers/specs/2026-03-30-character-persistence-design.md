# Character Persistence System — Design Spec

**Date:** 2026-03-30
**Status:** Approved
**Scope:** Portable character profiles, party serialization, multiplayer save/load flows, cross-world identity

### Dependencies

- **CharacterArchetype system** — currently in development (feature/character-archetype-system branch). NPCs are spawned from `archetypeId`, which requires the archetype spawning pipeline to be functional.
- **NGO 2.10+** — Netcode for GameObjects, already integrated.

---

## 1. Overview

The Character Persistence System enables fully portable character profiles that travel across worlds. A character profile is an independent local file containing all character state — stats, equipment, inventory, skills, abilities, relationships, traits, needs, party members, and more. Players can load their character into any world (solo or multiplayer) and bring everything with them.

### Core Mental Model

- **Solo mode is a hosted server** — there is no separate solo code path
- **The profile file is the player's "cartridge"** — independent from any world save
- **Save checkpoints are intentional** — bed/sleep in solo, portal gate in multiplayer
- **Crash = revert** — no autosave, no disconnect save; the player reverts to their last checkpoint
- **Visual state is never saved** — reconstructed from archetype + visual seed (Spine 2D agnostic)

---

## 2. Character Identity

### Two Separate GUIDs

| Field | Purpose | Lifetime |
|-------|---------|----------|
| `CharacterGuid` | Unique identity of this character. Primary key for profile files, relationship lookups, party membership, and `Character.FindByUUID()`. | Permanent — generated once at character creation, never changes |
| `OriginWorldGuid` | The world where this character was born. Metadata for tracking provenance and scoping relationships. | Permanent — set when the character first spawns into a world |

**Naming note:** The existing codebase uses `CharacterId` (backed by `NetworkCharacterId`, a `NetworkVariable<FixedString64Bytes>`) which is currently auto-generated via `Guid.NewGuid().ToString("N")` in `OnNetworkSpawn`. Going forward, `CharacterId` is the canonical runtime field name (no rename needed). `CharacterGuid` in this spec refers to the same value. On profile import, the saved `characterGuid` value **overrides** the auto-generated ID so identity is preserved across sessions.

**OriginWorldGuid assignment:** Characters are always created inside a running world (since solo = hosted server). The `OriginWorldGuid` is set at spawn time from the current world's `WorldGuid`. It is never set at the character selection screen — character creation starts the world first, then spawns the character.

### Profile File Path

`Profiles/{characterGuid}.json`

### World Identity

Each world (solo or hosted) has a `WorldGuid` generated once at world creation, stored in `GameSaveData` metadata. When a client joins a host, they receive the host's `WorldGuid`.

---

## 3. Save Contract Hierarchy

### Three Interfaces, Three Scopes

#### `ICharacterSaveData<T>` — Character-Portable (UPDATED)

The primary contract for character subsystems. Already exists at `Assets/Scripts/Character/SaveLoad/ICharacterSaveData.cs` but needs restructuring.

**Note:** CLAUDE.md rule 20 references `ICharacterData` — this spec uses the existing `ICharacterSaveData<T>` name from the codebase. CLAUDE.md should be updated to match.

Since C# does not support open generic type queries (`GetComponentsInChildren<ICharacterSaveData<>>()` is invalid), we introduce a **non-generic base interface** for discovery, with the generic interface extending it:

```csharp
/// Non-generic base for discovery by CharacterDataCoordinator.
public interface ICharacterSaveData
{
    string SaveKey { get; }
    int LoadPriority { get; }
    string SerializeToJson();
    void DeserializeFromJson(string json);
}

/// Typed interface that subsystems actually implement.
public interface ICharacterSaveData<T> : ICharacterSaveData
{
    T Serialize();
    void Deserialize(T data);
}
```

Subsystems implement `ICharacterSaveData<T>`. The non-generic `SerializeToJson()` / `DeserializeFromJson()` methods are provided by a default helper or base class that wraps `JsonConvert.SerializeObject(Serialize())` and `Deserialize(JsonConvert.DeserializeObject<T>(json))`. The coordinator discovers subsystems via `GetComponentsInChildren<ICharacterSaveData>()` (non-generic).

- `SaveKey` — unique key in the `componentStates` dictionary (e.g., `"CharacterStats"`)
- `LoadPriority` — integer for ordered restoration (lower = loaded first)
- `Serialize()` / `Deserialize(T)` — typed DTO operations used by the subsystem itself
- `SerializeToJson()` / `DeserializeFromJson(string)` — string operations used by the coordinator

#### `IOfflineCatchUp` — Macro-Simulation (UNCHANGED)

Already exists at `Assets/Scripts/Character/SaveLoad/IOfflineCatchUp.cs`. Subsystems that change over time during map hibernation implement this. Orthogonal to save/load.

```csharp
public interface IOfflineCatchUp
{
    void CalculateOfflineDelta(float elapsedDays);
}
```

#### `ISaveable` — World-Scoped (UNCHANGED)

Stays as-is for world systems (TimeManager, CommunityTracker, etc.). Character subsystems do NOT use this — they use `ICharacterSaveData<T>` instead.

---

## 4. Load Priority Tiers

| Priority | Systems | Reason |
|----------|---------|--------|
| 0 | CharacterProfile (race, gender, age, bio, visual seed, archetype) | Identity must exist before anything else |
| 10 | CharacterStats (all stats, HP/MP/Stamina) | Stats before anything that references them |
| 20 | CharacterSkills, CharacterAbilities | Capabilities before equipment validation |
| 30 | CharacterEquipment (includes nested Inventory in bag) | Gear depends on stats/skills |
| 40 | CharacterNeeds, CharacterTraits | State that may reference other systems |
| 50 | CharacterRelation, CharacterBookKnowledge | Social data, can load late |
| 60 | CharacterParty, CharacterCommunity, CharacterJob, CharacterSchedule | World-contextual but still portable |
| 70 | CharacterMapTracker, CharacterCombat | Position and combat state |

---

## 5. CharacterProfileSaveData (UPDATED)

```csharp
[System.Serializable]
public class CharacterProfileSaveData
{
    public int profileVersion = 1;

    // Identity
    public string characterGuid;        // replaces profileId
    public string originWorldGuid;      // world where character was born
    public string characterName;
    public string archetypeId;          // CharacterArchetype SO for spawning

    public string timestamp;

    // All subsystem states, keyed by ICharacterSaveData.SaveKey
    // Note: Dictionary<string,string> is not Unity-Inspector-serializable.
    // This DTO is JSON-only (serialized via Newtonsoft.Json), never displayed in Inspector.
    public Dictionary<string, string> componentStates = new();

    // Party NPC members (fully serialized, players excluded)
    public List<CharacterProfileSaveData> partyMembers = new();
}
```

---

## 6. CharacterDataCoordinator (UPGRADED)

The coordinator becomes the central brain for all profile operations. It lives on the **root GameObject** alongside `Character.cs` (via `[RequireComponent(typeof(Character))]`) because it is a facade-level orchestrator, not a subsystem — this is an intentional exception to the "subsystems on child GameObjects" rule.

### Migration Strategy

The existing `CharacterStats` and `CharacterBookKnowledge` currently implement `ISaveable`. These will be migrated to `ICharacterSaveData<T>` and their `ISaveable` implementations removed. Migration is **big-bang within this feature branch** — once `CharacterDataCoordinator` is upgraded to use `ICharacterSaveData`, the old `ISaveable` path for character subsystems is removed. The coordinator will NOT support both interfaces simultaneously. World-scoped `ISaveable` systems (TimeManager, CommunityTracker, etc.) are unaffected.

### Export Flow (`ExportProfile()`)

1. Collect all `ICharacterSaveData` (non-generic) implementations via `GetComponentsInChildren<ICharacterSaveData>()`
2. For each: call `SerializeToJson()`, store in `componentStates[saveKey]`
3. Set `characterGuid`, `originWorldGuid`, `archetypeId`, `characterName`, `timestamp`
4. If this character has a party (`CharacterParty.IsInParty`):
   - Iterate `CharacterParty.PartyData.MemberIds`
   - **Skip player characters** — players own their own profiles
   - For each NPC member: find via `Character.FindByUUID()`, call their `ExportProfile()` recursively
   - Add to `partyMembers` list
5. Return the complete `CharacterProfileSaveData`

### Import Flow (`ImportProfile()`)

1. Set identity fields on `Character` (characterId, originWorldGuid, characterName). The saved `characterGuid` **overrides** the auto-generated `CharacterId` to preserve identity.
2. Collect all `ICharacterSaveData` (non-generic) via `GetComponentsInChildren<ICharacterSaveData>()`, **sort by `LoadPriority` ascending**
3. For each, in priority order:
   - Look up `componentStates[saveKey]`
   - Call `DeserializeFromJson(json)` on the subsystem
   - If key missing in save data: log warning, subsystem keeps its default state (new subsystem added since save was made)
4. Log warning if `componentStates` has keys with no matching subsystem (removed/renamed subsystem — version mismatch)

### Version Handling

`profileVersion` tracks the save format version. On load:
- Missing keys default to fresh subsystem state (subsystem added after the save was made)
- Extra keys are logged and ignored (subsystem removed/renamed since save)
- If a subsystem's DTO shape changes between versions, the subsystem's `DeserializeFromJson()` is responsible for handling backward compatibility (e.g., try-catch with fallback to defaults, or version-aware deserialization)

### Party Member Spawning (on import into a world)

1. For each entry in `partyMembers`:
   - Check if an NPC with the same `characterGuid` already exists in this world AND is flagged `IsAbandoned`
   - **Regardless of whether an abandoned duplicate exists**: spawn a new Character prefab using `archetypeId`
   - Call `ImportProfile()` on the spawned NPC
   - Form party via `CharacterParty.CreateParty()` / `JoinParty()` on server
2. Two copies of the same NPC can coexist — the abandoned one and the freshly spawned one. This is intentional.

### Local Disk Operations

- `SaveLocalProfileAsync()` — exports + writes to `Profiles/{characterGuid}.json`
- `LoadLocalProfileAsync(characterGuid)` — reads from disk + calls `ImportProfile()`

---

## 7. Save Triggers

| Context | Trigger | What Happens |
|---------|---------|-------------|
| **Solo (bed/sleep)** | Player uses a bed | `CharacterDataCoordinator.SaveLocalProfileAsync()` runs on server |
| **Host shutdown** | Host closes game or stops server | Host's own profile saves (same as bed save — host is treated as a solo player). Connected clients receive no save. |
| **Multiplayer outbound** | Player enters portal gate to join another world | Profile saved to local disk **before** connecting to host |
| **Multiplayer return** | Player enters portal gate to return to solo world | Host serializes current state, sends to client via ClientRpc, client writes to local disk (overwrites previous checkpoint) |
| **Crash / disconnect** | Connection lost, game closed, ALT+F4 | **No save** — player reverts to last checkpoint (bed or portal gate) |
| **Game launch** | Player selects a profile from character selection | Profile loaded from `Profiles/` folder, world loaded, character + party spawned |

**File write safety:** All profile writes use `SaveFileHandler`'s atomic write pattern (write to `.tmp` file, then rename on success). A crash mid-write cannot corrupt the profile — either the old file remains intact or the new file fully replaces it.

---

## 8. Multiplayer Flows

### Client Joins Host via Portal Gate

1. In solo world, player approaches portal gate
2. **Profile saves to local disk** (checkpoint before leaving)
3. Client connects to host server
4. Client sends `CharacterProfileSaveData` to host via **NGO connection approval payload** (for initial data) combined with a follow-up **reliable fragmented RPC** for the full profile if it exceeds payload limits. The exact transport mechanism is an implementation detail — the key constraint is that the full profile (which can be large with inventory + party NPCs) must arrive reliably before the host spawns the character.
5. Host spawns a Character prefab using `archetypeId`
6. Host calls `ImportProfile()` to restore the client's state
7. Host spawns each NPC from `partyMembers` list, calls `ImportProfile()` on each
8. Party is re-formed on server via `CharacterParty.CreateParty()` + `JoinParty()`
9. Client receives host's `WorldGuid` for relationship scoping

### Client Returns via Portal Gate (Clean Exit)

1. Client interacts with portal gate in host's world
2. Host calls `ExportProfile()` on the client's character (includes current party NPCs, any new recruits)
3. Host sends the updated profile to client via ClientRpc
4. Client writes to local disk — **overwrites** the pre-portal checkpoint with updated state
5. Host despawns client character + client's party NPCs
6. Client loads back into their solo world with the new profile

### Client Disconnects / Crashes (No Portal Gate)

1. Client character despawns immediately from host world
2. `CharacterParty.DisbandParty()` is called on server — party disbands
3. **Player members** in the party simply become partyless — no special handling
4. **NPC party members** remain in the host's world as independent NPCs, flagged:
   - `IsAbandoned = true`
   - `FormerPartyLeaderId = client's CharacterGuid`
   - `FormerPartyLeaderWorldGuid = client's OriginWorldGuid`
5. Client's local profile is **not updated** — they revert to the pre-portal checkpoint
6. When client relaunches the game and loads their profile, they have everything as it was before they took the portal (including the checkpoint copies of abandoned NPCs)

### Client Reconnects to Same Host World

1. Client joins via portal gate with their checkpoint profile (which includes copies of the NPCs they abandoned)
2. Host spawns client character + party NPCs from profile normally
3. The abandoned NPCs with matching `characterGuid` still exist in the world — **two copies coexist**
4. Client can find abandoned NPCs and use the **"Reclaim"** interaction to merge them (see Section 9)

---

## 9. Abandoned NPC & Reclaim Mechanic

### Abandonment Data

When a party leader disconnects/crashes, each NPC party member gets flagged:

```csharp
// Stored on the NPC, persisted in HibernatedNPCData if map hibernates
bool IsAbandoned;
string FormerPartyLeaderId;         // CharacterGuid of the client who owned them
string FormerPartyLeaderWorldGuid;  // OriginWorldGuid of the client
```

### Duplicate Coexistence

When the client returns to the host world, their profile spawns fresh copies of the abandoned NPCs. The world now has two NPCs with the same `characterGuid`:
- **Portal copy** — spawned from the client's profile, in the client's party
- **Abandoned copy** — has been living independently in the host world since the crash

This is intentional. `Character.FindByUUID()` API changes:
- `FindByUUID(string id)` — unchanged behavior, returns the **non-abandoned** match (or first match if none are abandoned). Existing callers continue to work.
- `FindAbandonedByFormerLeader(string formerLeaderId)` — new method, returns all abandoned NPCs whose `FormerPartyLeaderId` matches. Used exclusively by the reclaim interaction flow.

### Reclaim Interaction

- The abandoned NPC exposes a **"Reclaim"** `InteractionOption` via `IInteractionProvider`
- **Only visible** to the character whose `CharacterGuid` matches `FormerPartyLeaderId`
- On execution:
  1. The abandoned NPC is despawned/destroyed
  2. The client's portal copy remains — it is already the authoritative version with the client's data
  3. Abandoned NPC is removed from any world tracking/registries
- **Data loss is intentional:** Any progress the abandoned NPC made while living independently in the host world (items gained, relationships formed, stat changes) is silently discarded. The client's portal copy is the authoritative version. This is a deliberate gameplay design choice — the client's save file is the source of truth for their NPCs.
- If client **never reclaims** and leaves via portal gate: portal copy goes home in the profile, abandoned copy stays in the host world indefinitely

---

## 10. Relationship Scoping

### Save Data Structure

```csharp
[System.Serializable]
public class RelationshipSaveEntry
{
    public string targetCharacterId;
    public string targetWorldGuid;
    public int relationshipType;        // enum as int
    public float value;
    // ... additional relationship fields
}
```

### Scoping Rules

- Relationships are keyed by `targetCharacterId + targetWorldGuid`
- **Same template character in different worlds = different relationship targets**
  - Main Character A in World-X and Main Character A in World-Y are separate entries
- When loading into a world, relationships only "activate" when both the `targetCharacterId` exists AND `targetWorldGuid` matches the current world
- All relationships are carried in the profile as dormant data — nothing is ever discarded
- Cross-world relationships formed during visits persist (e.g., befriending NPCs in a host's world)
- UI can organize relationships with tabs per world (presentation concern, not in this spec's scope)

### Recruited NPCs

If a client recruits an NPC from World-B and brings them home to World-A via portal gate:
- The relationship with that NPC is keyed with `targetWorldGuid: World-B`
- The NPC itself now lives in World-A as a party member
- The relationship remains valid because the NPC's `characterGuid` still matches

---

## 11. Subsystem Save Breakdown

### Saved (implements `ICharacterSaveData<T>`)

| Subsystem | SaveKey | DTO Contents | LoadPriority | IOfflineCatchUp? |
|-----------|---------|-------------|-------------|-----------------|
| CharacterProfile | `"CharacterProfile"` | Race, gender, age, bio, visual seed, archetype ID | 0 | No |
| CharacterStats | `"CharacterStats"` | All primary/tertiary stats, current HP/MP/Stamina | 10 | Yes (regen) |
| CharacterSkills | `"CharacterSkills"` | Unlocked skills, levels, XP per skill | 20 | Yes (passive XP from jobs) |
| CharacterAbilities | `"CharacterAbilities"` | Learned abilities, cooldown states | 20 | No |
| CharacterEquipment | `"CharacterEquipment"` | All equipped items as serialized ItemInstances (weapon, bag with nested inventory, wearable layers) | 30 | No |
| CharacterNeeds | `"CharacterNeeds"` | Current value per need type (hunger, social, sleep, etc.) | 40 | Yes (decay) |
| CharacterTraits | `"CharacterTraits"` | Personality traits, preferences | 40 | No |
| CharacterRelation | `"CharacterRelation"` | List of RelationshipSaveEntry (targetCharacterId + targetWorldGuid + type + value) | 50 | No |
| CharacterBookKnowledge | `"CharacterBookKnowledge"` | Reading progress per contentId (already implemented via ISaveable — migrate to ICharacterSaveData) | 50 | No |
| CharacterParty | `"CharacterParty"` | Party ID, role (leader/member), follow mode | 60 | No |
| CharacterCommunity | `"CharacterCommunity"` | Community membership, role, home community ID | 60 | No |
| CharacterJob | `"CharacterJob"` | Current job type, workplace building ID, job state | 60 | Yes (yields) |
| CharacterSchedule | `"CharacterSchedule"` | Sleep/work/free hours, anchor map IDs + positions (schedule-specific locations, e.g., workplace, hangout spot) | 60 | No |
| CharacterMapTracker | `"CharacterMapTracker"` | Current map ID, current position. **Note:** "home" concept is owned by CharacterSchedule (home anchor), not MapTracker. MapTracker only tracks where the character is right now. | 70 | No |
| CharacterCombat | `"CharacterCombat"` | Combat style preference, kill stats | 70 | No |

### Not Saved (Intentionally)

| Subsystem | Reason |
|-----------|--------|
| CharacterVisual | Reconstructed from archetype + visual seed. Spine 2D agnostic. |
| CharacterActions | Transient action state — recalculates on load |
| CharacterGoapController | AI planner state — recalculates on load |
| CharacterMovement | NavMesh state — reconstructed from position |
| CharacterInvitation | Pending invitations are transient |
| CharacterMentorship | Active teaching sessions are transient |

### Note on Inventory

Inventory lives on `StorageWearableInstance` (the equipped bag), not as its own `CharacterSystem`. The `CharacterEquipment` serialization handles the bag's inventory contents as part of the equipped bag's ItemInstance data. There is no separate `"CharacterInventory"` SaveKey — inventory is nested inside `"CharacterEquipment"`.

---

## 12. Game Launch & Character Selection

1. Game launches → character selection screen
2. Scan `Profiles/` folder for all `*.json` files
3. Deserialize each into `CharacterProfileSaveData` (read name, archetype, timestamp for display)
4. Player selects a profile (or creates a new character)
5. Game starts as a hosted server (solo mode)
6. Character spawned from `archetypeId`, `ImportProfile()` restores all state
7. Party NPCs spawned from `partyMembers`, each gets `ImportProfile()`
8. Party re-formed on server
9. Player is in-game with everything restored

---

## 13. Key Architectural Decisions

1. **`ICharacterSaveData<T>` over `ISaveable` for characters** — typed DTOs, load ordering, clear separation from world saves
2. **`CharacterGuid` and `OriginWorldGuid` as separate attributes** — GUIDs are already globally unique, no composite key needed
3. **Relationships scoped by `targetCharacterId + targetWorldGuid`** — prevents bleed between world instances of the same character template
4. **Duplicate NPCs allowed** — abandoned + portal copies coexist with same `characterGuid`; manual reclaim interaction to merge
5. **No autosave** — intentional checkpoint system (bed/portal gate)
6. **No visual state saved** — everything visual reconstructed from archetype + seed, enabling Spine 2D migration
7. **Solo = hosted server** — no separate code path, portal gate connects to different servers
8. **Profile is the source of truth** — client's copy always wins on reclaim (portal data overrides abandoned data)
9. **Players skip party serialization** — only NPC party members are serialized; players own their own profiles

---

## 14. Files To Create / Modify

### Create
- Character identity GUID generation (on `Character.cs` or `CharacterProfile`)
- World GUID generation (on `GameSaveData` or world creation flow)
- `FormerPartyLeader` data fields on NPC characters
- Reclaim `IInteractionProvider` implementation
- ~15 save data DTOs (one per subsystem listed in Section 11)
- Character selection screen (UI — out of scope for this spec, noted for future)

### Modify
- `ICharacterSaveData<T>` — add `SaveKey` and `LoadPriority` members
- `CharacterProfileSaveData` — add `characterGuid`, `originWorldGuid`, `archetypeId`; remove old `profileId`
- `CharacterDataCoordinator` — upgrade Export/Import flows with priority ordering, party serialization, validation logging
- `CharacterParty` — handle disband on leader disconnect, flag abandoned NPCs
- `Character.FindByUUID()` — handle duplicate `characterGuid` (abandoned vs. active)
- `CharacterBookKnowledge` — migrate from `ISaveable` to `ICharacterSaveData<T>`
- `CharacterStats` — migrate from `ISaveable` to `ICharacterSaveData<T>`
- `HibernatedNPCData` — add abandoned NPC fields
- `SaveManager` / `GameSaveData` — add `WorldGuid`
- `SaveFileHandler` — update `WriteProfileAsync` / `ReadProfileAsync` to accept `characterGuid` instead of `profileId`. This is a clean-slate change — no existing profile files need migration as the system is not yet in production.
- `CLAUDE.md` rule 20 — update `ICharacterData` reference to `ICharacterSaveData<T>`

---

## 15. Out of Scope

- Character selection UI (separate spec)
- Portal gate implementation (separate spec — this spec only defines what happens at save/load boundaries)
- Relationship UI with world tabs (presentation concern)
- Save file encryption or anti-cheat (future concern)
- Cloud save sync (future concern)
- Character deletion flow
- **Profile validation / anti-cheat** — in this version, the host trusts client profile data as-is. A malicious client could send fabricated stats, items, or abilities. Server-side validation of incoming profiles is a future concern and will be addressed in a separate anti-cheat spec.
