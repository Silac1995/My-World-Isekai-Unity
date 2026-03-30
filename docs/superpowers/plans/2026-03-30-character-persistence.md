# Character Persistence System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement fully portable character profiles that travel across worlds with all subsystem state, party NPC serialization, and abandoned NPC reclaim mechanics.

**Architecture:** ICharacterSaveData<T> typed contract with non-generic base for discovery. CharacterDataCoordinator orchestrates priority-ordered export/import. CharacterProfileSaveData is the portable JSON file containing all character + party NPC state.

**Tech Stack:** Unity 6, NGO 2.10+, Newtonsoft.Json, C# async/await

**Spec:** `docs/superpowers/specs/2026-03-30-character-persistence-design.md`

**Recommended agent:** `.claude/agents/character-system-specialist.md` for all character subsystem tasks.

---

## File Structure

### Create
| File | Purpose |
|------|---------|
| `Assets/Scripts/Character/SaveLoad/ICharacterSaveData.cs` | Rewrite: non-generic base + generic interface |
| `Assets/Scripts/Character/SaveLoad/CharacterSaveDataBase.cs` | Abstract helper base class providing JSON bridge methods |
| `Assets/Scripts/Character/SaveLoad/ProfileSaveData/ProfileSaveData.cs` | DTO for CharacterProfile |
| `Assets/Scripts/Character/SaveLoad/ProfileSaveData/SkillsSaveData.cs` | DTO for CharacterSkills |
| `Assets/Scripts/Character/SaveLoad/ProfileSaveData/AbilitiesSaveData.cs` | DTO for CharacterAbilities |
| `Assets/Scripts/Character/SaveLoad/ProfileSaveData/EquipmentSaveData.cs` | DTO for CharacterEquipment + nested inventory |
| `Assets/Scripts/Character/SaveLoad/ProfileSaveData/NeedsSaveData.cs` | DTO for CharacterNeeds |
| `Assets/Scripts/Character/SaveLoad/ProfileSaveData/TraitsSaveData.cs` | DTO for CharacterTraits |
| `Assets/Scripts/Character/SaveLoad/ProfileSaveData/RelationSaveData.cs` | DTO for CharacterRelation (world-scoped) |
| `Assets/Scripts/Character/SaveLoad/ProfileSaveData/PartySaveData.cs` | DTO for CharacterParty |
| `Assets/Scripts/Character/SaveLoad/ProfileSaveData/CommunitySaveData.cs` | DTO for CharacterCommunity |
| `Assets/Scripts/Character/SaveLoad/ProfileSaveData/JobSaveData.cs` | DTO for CharacterJob |
| `Assets/Scripts/Character/SaveLoad/ProfileSaveData/ScheduleSaveData.cs` | DTO for CharacterSchedule |
| `Assets/Scripts/Character/SaveLoad/ProfileSaveData/MapTrackerSaveData.cs` | DTO for CharacterMapTracker |
| `Assets/Scripts/Character/SaveLoad/ProfileSaveData/CombatSaveData.cs` | DTO for CharacterCombat |
| `Assets/Scripts/Character/Abandoned/ReclaimNPCInteraction.cs` | IInteractionProvider for reclaim action (CharacterSystem subclass) |

### Modify
| File | What Changes |
|------|-------------|
| `Assets/Scripts/Core/SaveLoad/CharacterProfileSaveData.cs` | Add characterGuid, originWorldGuid, archetypeId; remove profileId |
| `Assets/Scripts/Core/SaveLoad/GameSaveData.cs` | Add worldGuid to SaveSlotMetadata |
| `Assets/Scripts/Core/SaveLoad/SaveFileHandler.cs` | Rename profileId param to characterGuid |
| `Assets/Scripts/Character/SaveLoad/CharacterDataCoordinator.cs` | Full rewrite: priority-ordered import/export, party serialization |
| `Assets/Scripts/Character/Character.cs` | Add OriginWorldGuid, update FindByUUID for duplicates, add FindAbandonedByFormerLeader |
| `Assets/Scripts/Character/CharacterStats/CharacterStats.cs` | Migrate ISaveable → ICharacterSaveData<StatsSaveData> |
| `Assets/Scripts/Character/CharacterBookKnowledge.cs` | Migrate ISaveable → ICharacterSaveData<BookKnowledgeSaveData> |
| `Assets/Scripts/Character/CharacterProfile/CharacterProfile.cs` | Implement ICharacterSaveData<ProfileSaveData> |
| `Assets/Scripts/Character/CharacterSkills/CharacterSkills.cs` | Implement ICharacterSaveData<SkillsSaveData> |
| `Assets/Scripts/Character/CharacterAbilities/CharacterAbilities.cs` | Implement ICharacterSaveData<AbilitiesSaveData> |
| `Assets/Scripts/Character/CharacterEquipment/CharacterEquipment.cs` | Implement ICharacterSaveData<EquipmentSaveData> |
| `Assets/Scripts/Character/CharacterNeeds/CharacterNeeds.cs` | Implement ICharacterSaveData<NeedsSaveData> |
| `Assets/Scripts/Character/CharacterTraits/CharacterTraits.cs` | Implement ICharacterSaveData<TraitsSaveData> |
| `Assets/Scripts/Character/CharacterRelation/CharacterRelation.cs` | Implement ICharacterSaveData<RelationSaveData> |
| `Assets/Scripts/Character/CharacterParty/CharacterParty.cs` | Implement ICharacterSaveData<PartySaveData>, add disband-on-disconnect, abandonment flagging |
| `Assets/Scripts/Character/CharacterCommunity/CharacterCommunity.cs` | Implement ICharacterSaveData<CommunitySaveData> |
| `Assets/Scripts/Character/CharacterJob/CharacterJob.cs` | Implement ICharacterSaveData<JobSaveData> |
| `Assets/Scripts/Character/CharacterSchedule/CharacterSchedule.cs` | Implement ICharacterSaveData<ScheduleSaveData> |
| `Assets/Scripts/Character/Components/CharacterMapTracker.cs` | Implement ICharacterSaveData<MapTrackerSaveData> |
| `Assets/Scripts/Character/CharacterCombat/CharacterCombat.cs` | Implement ICharacterSaveData<CombatSaveData> |
| `Assets/Scripts/World/MapSystem/MapSaveData.cs` | Add abandoned NPC fields to HibernatedNPCData |
| `CLAUDE.md` | Update rule 20: ICharacterData → ICharacterSaveData<T> |

---

## Task 1: Foundation — Save Contract Interfaces

**Files:**
- Rewrite: `Assets/Scripts/Character/SaveLoad/ICharacterSaveData.cs`
- Create: `Assets/Scripts/Character/SaveLoad/CharacterSaveDataBase.cs`

**Ref:** Spec Section 3

- [ ] **Step 1: Rewrite ICharacterSaveData.cs with non-generic base + generic interface**

```csharp
// Assets/Scripts/Character/SaveLoad/ICharacterSaveData.cs
using Newtonsoft.Json;

/// <summary>
/// Non-generic base interface for discovery by CharacterDataCoordinator.
/// CharacterSystems implement ICharacterSaveData<T> (generic) instead of this directly.
/// </summary>
public interface ICharacterSaveData
{
    string SaveKey { get; }
    int LoadPriority { get; }
    string SerializeToJson();
    void DeserializeFromJson(string json);
}

/// <summary>
/// Typed save data contract for character subsystems.
/// Each subsystem defines its own DTO type T and implements Serialize/Deserialize.
/// The non-generic methods are bridged automatically by CharacterSaveDataBase<T>.
/// </summary>
public interface ICharacterSaveData<T> : ICharacterSaveData
{
    T Serialize();
    void Deserialize(T data);
}
```

- [ ] **Step 2: Create CharacterSaveDataBase helper (not a MonoBehaviour — a static utility or extension approach)**

Since CharacterSystems already inherit from `CharacterSystem : NetworkBehaviour`, they cannot also inherit from a base class. Instead, provide default interface method implementations or a static helper:

```csharp
// Assets/Scripts/Character/SaveLoad/CharacterSaveDataBase.cs
using Newtonsoft.Json;

/// <summary>
/// Static helper for ICharacterSaveData<T> JSON bridge methods.
/// Subsystems call these in their explicit ICharacterSaveData implementations.
/// </summary>
public static class CharacterSaveDataHelper
{
    public static string SerializeToJson<T>(ICharacterSaveData<T> saveable)
    {
        return JsonConvert.SerializeObject(saveable.Serialize());
    }

    public static void DeserializeFromJson<T>(ICharacterSaveData<T> saveable, string json)
    {
        var data = JsonConvert.DeserializeObject<T>(json);
        if (data != null)
            saveable.Deserialize(data);
        else
            Debug.LogWarning($"[CharacterSaveDataHelper] Deserialization returned null for {saveable.SaveKey}. JSON may be malformed.");
    }
}
```

- [ ] **Step 3: Verify the existing IOfflineCatchUp.cs is unchanged (read-only check)**

No changes needed. Confirm it still exists at `Assets/Scripts/Character/SaveLoad/IOfflineCatchUp.cs`.

- [ ] **Step 4: Compile and verify no errors**

Run: Unity Editor → check Console for compilation errors. Use MCP `console-get-logs` tool.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Character/SaveLoad/ICharacterSaveData.cs Assets/Scripts/Character/SaveLoad/CharacterSaveDataBase.cs
git commit -m "feat(save): rewrite ICharacterSaveData with non-generic base + static JSON helper"
```

---

## Task 2: Foundation — Update CharacterProfileSaveData & GameSaveData

**Files:**
- Modify: `Assets/Scripts/Core/SaveLoad/CharacterProfileSaveData.cs`
- Modify: `Assets/Scripts/Core/SaveLoad/GameSaveData.cs`
- Modify: `Assets/Scripts/Core/SaveLoad/SaveFileHandler.cs`

**Ref:** Spec Sections 2, 5

- [ ] **Step 1: Update CharacterProfileSaveData**

Read current file first, then replace contents:

```csharp
// Assets/Scripts/Core/SaveLoad/CharacterProfileSaveData.cs
using System.Collections.Generic;

/// <summary>
/// Portable character profile — the "cartridge" that travels across worlds.
/// Saved to Profiles/{characterGuid}.json via SaveFileHandler.
/// Serialized via Newtonsoft.Json (Dictionary not Unity-Inspector-serializable).
/// </summary>
[System.Serializable]
public class CharacterProfileSaveData
{
    public int profileVersion = 1;

    // Identity
    public string characterGuid;
    public string originWorldGuid;
    public string characterName;
    public string archetypeId;

    public string timestamp;

    // All subsystem states, keyed by ICharacterSaveData.SaveKey
    public Dictionary<string, string> componentStates = new Dictionary<string, string>();

    // Party NPC members (fully serialized, players excluded)
    public List<CharacterProfileSaveData> partyMembers = new List<CharacterProfileSaveData>();
}
```

- [ ] **Step 2: Add WorldGuid to GameSaveData.SaveSlotMetadata**

Read `GameSaveData.cs`, then add `worldGuid` field:

```csharp
// In SaveSlotMetadata class, add:
public string worldGuid;  // Unique GUID for this world instance, generated once at world creation
```

- [ ] **Step 3: Update SaveFileHandler — rename profileId to characterGuid**

Read `SaveFileHandler.cs`, then:
- Rename `ProfilePath(string profileId)` → `ProfilePath(string characterGuid)`
- Rename parameter in `WriteProfileAsync(string profileId, ...)` → `WriteProfileAsync(string characterGuid, ...)`
- Rename parameter in `ReadProfileAsync(string profileId)` → `ReadProfileAsync(string characterGuid)`
- Rename parameter in `DeleteProfileAsync(string profileId)` → `DeleteProfileAsync(string characterGuid)`
- Rename parameter in `ProfileExists(string profileId)` → `ProfileExists(string characterGuid)`
- Update the file path format: `$"profile_{profileId}.json"` → `$"{characterGuid}.json"`

- [ ] **Step 4: Fix CharacterDataCoordinator compile break**

The `CharacterDataCoordinator` references `profileId` — update it to use `characterGuid` temporarily (just rename the variable references). The full rewrite happens in Task 4, but it must compile between tasks.

- [ ] **Step 5: Add WorldGuid generation to world creation flow**

Find where new worlds are created (likely `SaveManager` or game startup). Add:
```csharp
if (string.IsNullOrEmpty(metadata.worldGuid))
    metadata.worldGuid = Guid.NewGuid().ToString("N");
```

Also add a runtime accessor so subsystems can read the current world's GUID (e.g., a static property on `SaveManager` or a `WorldIdentity` singleton).

- [ ] **Step 6: Compile and verify no errors**

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/Core/SaveLoad/CharacterProfileSaveData.cs Assets/Scripts/Core/SaveLoad/GameSaveData.cs Assets/Scripts/Core/SaveLoad/SaveFileHandler.cs
git commit -m "feat(save): update CharacterProfileSaveData with GUIDs, add WorldGuid to GameSaveData"
```

---

## Task 3: Foundation — Character.cs Identity & Lookup Updates

**Files:**
- Modify: `Assets/Scripts/Character/Character.cs`

**Ref:** Spec Sections 2, 9

- [ ] **Step 1: Read Character.cs to understand current identity fields**

Read the full file (it's ~942 lines). Note `NetworkCharacterId`, `CharacterId` property, `FindByUUID()`, and `OnNetworkSpawn()`.

- [ ] **Step 2: Add OriginWorldGuid field**

Add near the other network identity fields (~line 160):

```csharp
// Origin world where this character was born — set once, travels with profile
private string _originWorldGuid;
public string OriginWorldGuid
{
    get => _originWorldGuid;
    set => _originWorldGuid = value;
}
```

- [ ] **Step 3: Add abandoned NPC fields**

Add near the identity section:

```csharp
// Abandoned NPC tracking — set when a party leader disconnects
private bool _isAbandoned;
public bool IsAbandoned
{
    get => _isAbandoned;
    set => _isAbandoned = value;
}

private string _formerPartyLeaderId;
public string FormerPartyLeaderId
{
    get => _formerPartyLeaderId;
    set => _formerPartyLeaderId = value;
}

private string _formerPartyLeaderWorldGuid;
public string FormerPartyLeaderWorldGuid
{
    get => _formerPartyLeaderWorldGuid;
    set => _formerPartyLeaderWorldGuid = value;
}
```

- [ ] **Step 4: Update FindByUUID to prefer non-abandoned matches**

Replace the existing `FindByUUID` method:

```csharp
public static Character FindByUUID(string uuid)
{
    if (string.IsNullOrEmpty(uuid)) return null;

    Character fallback = null;
    foreach (Character c in FindObjectsByType<Character>(FindObjectsSortMode.None))
    {
        if (c.CharacterId == uuid)
        {
            if (!c.IsAbandoned) return c;
            fallback = c;
        }
    }
    return fallback; // Return abandoned only if no non-abandoned match
}
```

- [ ] **Step 5: Add FindAbandonedByFormerLeader method**

```csharp
public static List<Character> FindAbandonedByFormerLeader(string formerLeaderId)
{
    var results = new List<Character>();
    foreach (Character c in FindObjectsByType<Character>(FindObjectsSortMode.None))
    {
        if (c.IsAbandoned && c.FormerPartyLeaderId == formerLeaderId)
            results.Add(c);
    }
    return results;
}
```

- [ ] **Step 6: Compile and verify**

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/Character/Character.cs
git commit -m "feat(save): add OriginWorldGuid, abandoned NPC fields, duplicate-aware FindByUUID"
```

---

## Task 4: CharacterDataCoordinator Upgrade

**Files:**
- Modify: `Assets/Scripts/Character/SaveLoad/CharacterDataCoordinator.cs`

**Ref:** Spec Section 6

- [ ] **Step 1: Read current CharacterDataCoordinator.cs**

- [ ] **Step 2: Rewrite CharacterDataCoordinator with priority-ordered import/export**

```csharp
// Assets/Scripts/Character/SaveLoad/CharacterDataCoordinator.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Newtonsoft.Json;
using Unity.Netcode;

[RequireComponent(typeof(Character))]
public class CharacterDataCoordinator : NetworkBehaviour
{
    private Character _character;

    private void Awake()
    {
        _character = GetComponent<Character>();
    }

    /// <summary>
    /// Bundles all ICharacterSaveData states into a CharacterProfileSaveData.
    /// Party NPC members are serialized recursively. Player members are skipped.
    /// </summary>
    public CharacterProfileSaveData ExportProfile()
    {
        var saveables = GetComponentsInChildren<ICharacterSaveData>(true);

        var profile = new CharacterProfileSaveData
        {
            characterGuid = _character.CharacterId,
            originWorldGuid = _character.OriginWorldGuid,
            characterName = _character.CharacterName,
            archetypeId = _character.Archetype != null
                ? _character.Archetype.name
                : "",
            timestamp = DateTime.Now.ToString("o")
        };

        foreach (var s in saveables)
        {
            try
            {
                profile.componentStates[s.SaveKey] = s.SerializeToJson();
            }
            catch (Exception e)
            {
                Debug.LogError($"<color=red>[CharacterDataCoordinator]</color> Failed to serialize {s.SaveKey}: {e.Message}");
            }
        }

        // Serialize party NPC members (skip players)
        if (_character.TryGet<CharacterParty>(out var party) && party.IsInParty && party.IsPartyLeader)
        {
            foreach (string memberId in party.PartyData.MemberIds)
            {
                if (memberId == _character.CharacterId) continue;

                Character member = Character.FindByUUID(memberId);
                if (member == null) continue;
                if (member.IsPlayer()) continue; // Players own their own profiles

                var memberCoordinator = member.GetComponent<CharacterDataCoordinator>();
                if (memberCoordinator != null)
                {
                    profile.partyMembers.Add(memberCoordinator.ExportProfile());
                }
            }
        }

        return profile;
    }

    /// <summary>
    /// Injects a loaded CharacterProfileSaveData into this character's systems.
    /// Restores in LoadPriority order (lower = first).
    /// </summary>
    public void ImportProfile(CharacterProfileSaveData data)
    {
        // 1. Restore identity
        _character.CharacterName = data.characterName;
        _character.OriginWorldGuid = data.originWorldGuid;

        // Override auto-generated CharacterId with saved GUID
        if (!string.IsNullOrEmpty(data.characterGuid) && IsServer)
        {
            _character.NetworkCharacterId.Value = data.characterGuid;
        }

        // 2. Collect and sort by priority
        var saveables = GetComponentsInChildren<ICharacterSaveData>(true)
            .OrderBy(s => s.LoadPriority)
            .ToList();

        // 3. Restore each subsystem in priority order
        var restoredKeys = new HashSet<string>();
        foreach (var s in saveables)
        {
            if (data.componentStates.TryGetValue(s.SaveKey, out string json))
            {
                try
                {
                    s.DeserializeFromJson(json);
                    restoredKeys.Add(s.SaveKey);
                }
                catch (Exception e)
                {
                    Debug.LogError($"<color=red>[CharacterDataCoordinator]</color> Failed to deserialize {s.SaveKey}: {e.Message}");
                }
            }
            else
            {
                Debug.LogWarning($"<color=orange>[CharacterDataCoordinator]</color> No saved data for {s.SaveKey} — subsystem keeps defaults.");
            }
        }

        // 4. Warn about orphaned keys (subsystems removed/renamed since save)
        foreach (var key in data.componentStates.Keys)
        {
            if (!restoredKeys.Contains(key))
            {
                Debug.LogWarning($"<color=orange>[CharacterDataCoordinator]</color> Save data key '{key}' has no matching subsystem — possibly removed or renamed.");
            }
        }

        Debug.Log($"<color=cyan>[CharacterDataCoordinator]</color> Profile imported for {data.characterName} ({restoredKeys.Count}/{saveables.Count} subsystems restored).");
    }

    // --- Local Disk Helpers ---

    public async Task SaveLocalProfileAsync()
    {
        var profile = ExportProfile();
        if (string.IsNullOrEmpty(profile.characterGuid)) return;

        await SaveFileHandler.WriteProfileAsync(profile.characterGuid, profile);
        Debug.Log($"<color=cyan>[CharacterDataCoordinator]</color> Profile {profile.characterGuid} saved locally.");
    }

    public async Task LoadLocalProfileAsync(string characterGuid)
    {
        var data = await SaveFileHandler.ReadProfileAsync(characterGuid);
        if (data != null)
        {
            ImportProfile(data);
        }
        else
        {
            Debug.LogWarning($"<color=orange>[CharacterDataCoordinator]</color> Profile {characterGuid} not found on local disk.");
        }
    }

    // --- DEBUG ---
    [ContextMenu("Debug: Save Local Profile")]
    private void DebugSaveProfile() => _ = SaveLocalProfileAsync();

    [ContextMenu("Debug: Load Local Profile")]
    private void DebugLoadProfile()
    {
        string characterGuid = _character.CharacterId;
        _ = LoadLocalProfileAsync(characterGuid);
    }

    [ContextMenu("Debug: Log All SaveKeys")]
    private void DebugLogSaveKeys()
    {
        var saveables = GetComponentsInChildren<ICharacterSaveData>(true)
            .OrderBy(s => s.LoadPriority);
        foreach (var s in saveables)
        {
            Debug.Log($"[SaveKey] Priority={s.LoadPriority} Key={s.SaveKey}");
        }
    }
}
```

- [ ] **Step 3: Compile and verify**

Compilation will have warnings/errors if `_character.IsPlayer` or `_character.Archetype` don't exist yet. Check and adapt to actual property names.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Character/SaveLoad/CharacterDataCoordinator.cs
git commit -m "feat(save): rewrite CharacterDataCoordinator with priority-ordered import/export and party serialization"
```

---

## Task 5: Migrate CharacterStats from ISaveable to ICharacterSaveData

**Files:**
- Modify: `Assets/Scripts/Character/CharacterStats/CharacterStats.cs`

**Ref:** Spec Section 6 (Migration Strategy), Section 11

- [ ] **Step 1: Read CharacterStats.cs fully**

Note the existing `ISaveable` implementation: `SaveKey`, `CaptureState()`, `RestoreState()`, and the `StatsSaveData` struct.

- [ ] **Step 2: Replace ISaveable with ICharacterSaveData<StatsSaveData>**

Changes:
- Remove `: ISaveable` from class declaration
- Add `: ICharacterSaveData<StatsSaveData>` (the class already extends `CharacterSystem`)
- Keep `StatsSaveData` struct as-is
- Replace `CaptureState()` with `Serialize()` returning `StatsSaveData`
- Replace `RestoreState(object)` with `Deserialize(StatsSaveData)`
- Add `SaveKey` property returning `"CharacterStats"`
- Add `LoadPriority` property returning `10`
- Add explicit `ICharacterSaveData` bridge methods:

```csharp
public string SaveKey => "CharacterStats";
public int LoadPriority => 10;

public StatsSaveData Serialize()
{
    // Same logic as old CaptureState(), but returns StatsSaveData directly
    return new StatsSaveData { /* existing field assignments */ };
}

public void Deserialize(StatsSaveData data)
{
    // Same logic as old RestoreState(), but takes typed param
}

// Non-generic bridge (explicit interface impl)
string ICharacterSaveData.SerializeToJson() => CharacterSaveDataHelper.SerializeToJson(this);
void ICharacterSaveData.DeserializeFromJson(string json) => CharacterSaveDataHelper.DeserializeFromJson(this, json);
```

- [ ] **Step 3: Unregister from SaveManager if CharacterStats was registered as ISaveable**

Check if CharacterStats registers itself with `SaveManager`. If so, remove that registration — character subsystems no longer use the world save path.

- [ ] **Step 4: Compile and verify**

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Character/CharacterStats/CharacterStats.cs
git commit -m "refactor(save): migrate CharacterStats from ISaveable to ICharacterSaveData<StatsSaveData>"
```

---

## Task 6: Migrate CharacterBookKnowledge from ISaveable to ICharacterSaveData

**Files:**
- Modify: `Assets/Scripts/Character/CharacterBookKnowledge.cs`

**Ref:** Spec Section 6, Section 11

- [ ] **Step 1: Read CharacterBookKnowledge.cs fully**

Note the existing ISaveable implementation and BookReadingEntry class.

- [ ] **Step 2: Create BookKnowledgeSaveData DTO and replace ISaveable**

```csharp
[System.Serializable]
public class BookKnowledgeSaveData
{
    public List<BookReadingEntry> readingLog = new List<BookReadingEntry>();
}
```

Replace ISaveable with `ICharacterSaveData<BookKnowledgeSaveData>`:
- `SaveKey => "CharacterBookKnowledge"`
- `LoadPriority => 50`
- `Serialize()` returns new `BookKnowledgeSaveData { readingLog = _readingLog }`
- `Deserialize(BookKnowledgeSaveData data)` restores `_readingLog = data.readingLog`
- Add explicit bridge methods

- [ ] **Step 3: Compile and verify**

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Character/CharacterBookKnowledge.cs
git commit -m "refactor(save): migrate CharacterBookKnowledge from ISaveable to ICharacterSaveData"
```

---

## Task 7: CharacterProfile — Save Identity Data (Priority 0)

**Files:**
- Create: `Assets/Scripts/Character/SaveLoad/ProfileSaveData/ProfileSaveData.cs`
- Modify: `Assets/Scripts/Character/CharacterProfile/CharacterProfile.cs`

**Ref:** Spec Section 11 — Priority 0

- [ ] **Step 1: Read CharacterProfile.cs to understand current fields**

Key fields: `_personality: CharacterPersonalitySO`. Also check what identity data lives on `Character.cs` (race, gender, bio, visual seed) since CharacterProfile needs to save those.

- [ ] **Step 2: Create ProfileSaveData DTO**

```csharp
// Assets/Scripts/Character/SaveLoad/ProfileSaveData/ProfileSaveData.cs

[System.Serializable]
public class ProfileSaveData
{
    public string raceId;
    public int gender;          // GenderType as int
    public int age;
    public string biography;
    public int visualSeed;
    public string archetypeId;
    public string personalityId; // CharacterPersonalitySO asset name
}
```

- [ ] **Step 3: Implement ICharacterSaveData<ProfileSaveData> on CharacterProfile**

```csharp
public string SaveKey => "CharacterProfile";
public int LoadPriority => 0;

public ProfileSaveData Serialize()
{
    return new ProfileSaveData
    {
        raceId = _character.Race != null ? _character.Race.name : "",
        gender = (int)_character.Gender,
        age = _character.CharacterBio != null ? _character.CharacterBio.Age : 0,
        biography = _character.CharacterBio != null ? _character.CharacterBio.Biography : "",
        visualSeed = _character.VisualSeed,
        archetypeId = _character.Archetype != null ? _character.Archetype.name : "",
        personalityId = _personality != null ? _personality.name : ""
    };
}

public void Deserialize(ProfileSaveData data)
{
    // Restore race, gender, bio, visual seed from saved data
    // Load RaceSO by name from Resources if needed
    // Load personality SO by name from Resources if needed
}

string ICharacterSaveData.SerializeToJson() => CharacterSaveDataHelper.SerializeToJson(this);
void ICharacterSaveData.DeserializeFromJson(string json) => CharacterSaveDataHelper.DeserializeFromJson(this, json);
```

**Important:** The exact fields available on `Character.cs` (Race, Gender, CharacterBio, VisualSeed) must be verified by reading the file. Adapt the DTO and Serialize/Deserialize to match actual property names.

- [ ] **Step 4: Compile and verify**

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Character/SaveLoad/ProfileSaveData/ProfileSaveData.cs Assets/Scripts/Character/CharacterProfile/CharacterProfile.cs
git commit -m "feat(save): implement ICharacterSaveData for CharacterProfile (priority 0)"
```

---

## Task 8: CharacterSkills + CharacterAbilities (Priority 20)

**Files:**
- Create: `Assets/Scripts/Character/SaveLoad/ProfileSaveData/SkillsSaveData.cs`
- Create: `Assets/Scripts/Character/SaveLoad/ProfileSaveData/AbilitiesSaveData.cs`
- Modify: `Assets/Scripts/Character/CharacterSkills/CharacterSkills.cs`
- Modify: `Assets/Scripts/Character/CharacterAbilities/CharacterAbilities.cs`

**Ref:** Spec Section 11 — Priority 20

- [ ] **Step 1: Read CharacterSkills.cs and CharacterAbilities.cs fully**

Note the `_skills: List<SkillInstance>`, `NetworkSkillSyncData`, and for abilities: `_knownPhysicalAbilities`, `_knownSpells`, `_knownPassives`, `_activeSlots`, `_passiveSlots`.

- [ ] **Step 2: Create SkillsSaveData DTO**

```csharp
// Assets/Scripts/Character/SaveLoad/ProfileSaveData/SkillsSaveData.cs
using System.Collections.Generic;

[System.Serializable]
public class SkillsSaveData
{
    public List<SkillSaveEntry> skills = new List<SkillSaveEntry>();
}

[System.Serializable]
public class SkillSaveEntry
{
    public string skillId;  // SkillSO asset name
    public int level;
    public int currentXP;
    public int totalXP;
}
```

- [ ] **Step 3: Implement ICharacterSaveData<SkillsSaveData> on CharacterSkills**

- SaveKey: `"CharacterSkills"`, LoadPriority: `20`
- Serialize: iterate `_skills`, map each to `SkillSaveEntry`
- Deserialize: recreate skill instances from saved data, load SkillSO by name

- [ ] **Step 4: Create AbilitiesSaveData DTO**

```csharp
// Assets/Scripts/Character/SaveLoad/ProfileSaveData/AbilitiesSaveData.cs
using System.Collections.Generic;

[System.Serializable]
public class AbilitiesSaveData
{
    public List<string> knownPhysicalAbilityIds = new List<string>();
    public List<string> knownSpellIds = new List<string>();
    public List<string> knownPassiveIds = new List<string>();
    public List<AbilitySlotEntry> activeSlots = new List<AbilitySlotEntry>();
    public List<AbilitySlotEntry> passiveSlots = new List<AbilitySlotEntry>();
}

[System.Serializable]
public class AbilitySlotEntry
{
    public int slotIndex;
    public string abilityId; // empty string = empty slot
}
```

- [ ] **Step 5: Implement ICharacterSaveData<AbilitiesSaveData> on CharacterAbilities**

- SaveKey: `"CharacterAbilities"`, LoadPriority: `20`
- Serialize: collect ability SO names from known lists and slot arrays
- Deserialize: load ability SOs by name, reconstruct instances, populate slots

- [ ] **Step 6: Compile and verify**

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/Character/SaveLoad/ProfileSaveData/SkillsSaveData.cs Assets/Scripts/Character/SaveLoad/ProfileSaveData/AbilitiesSaveData.cs Assets/Scripts/Character/CharacterSkills/CharacterSkills.cs Assets/Scripts/Character/CharacterAbilities/CharacterAbilities.cs
git commit -m "feat(save): implement ICharacterSaveData for CharacterSkills and CharacterAbilities (priority 20)"
```

---

## Task 9: CharacterEquipment + Nested Inventory (Priority 30)

**Files:**
- Create: `Assets/Scripts/Character/SaveLoad/ProfileSaveData/EquipmentSaveData.cs`
- Modify: `Assets/Scripts/Character/CharacterEquipment/CharacterEquipment.cs`

**Ref:** Spec Section 11 — Priority 30, Note on Inventory

- [ ] **Step 1: Read CharacterEquipment.cs fully**

Note: `_weapon: WeaponInstance`, equipment layers (`underwearLayer`, `clothingLayer`, `armorLayer`), `_bag: BagInstance`, and `NetworkEquipmentSyncData` structure (SlotId, ItemId, JsonData).

- [ ] **Step 2: Read the EquipmentLayer.cs, Inventory.cs, and ItemInstance.cs hierarchy**

Understand how items are serialized (the network sync already uses `JsonData` via `JsonUtility`). The save system should use the same serialization approach.

- [ ] **Step 3: Create EquipmentSaveData DTO**

```csharp
// Assets/Scripts/Character/SaveLoad/ProfileSaveData/EquipmentSaveData.cs
using System.Collections.Generic;

[System.Serializable]
public class EquipmentSaveData
{
    public List<EquipmentSlotSaveEntry> equippedItems = new List<EquipmentSlotSaveEntry>();
}

[System.Serializable]
public class EquipmentSlotSaveEntry
{
    public int slotId;       // Same slot ID convention as NetworkEquipmentSyncData
    public string itemId;    // ItemSO asset name
    public string jsonData;  // Full ItemInstance JSON (includes inventory for bags)
}
```

- [ ] **Step 4: Implement ICharacterSaveData<EquipmentSaveData> on CharacterEquipment**

- SaveKey: `"CharacterEquipment"`, LoadPriority: `30`
- Serialize: iterate all equipped items (weapon, bag, wearable layers), create EquipmentSlotSaveEntry for each using the same JSON format as NetworkEquipmentSyncData
- Deserialize: for each entry, load ItemSO by itemId, create ItemInstance via `ItemSO.CreateInstance()`, overwrite via `JsonUtility.FromJsonOverwrite(jsonData, instance)`, then equip
- **Bag inventory is nested**: When a BagInstance is serialized, its `Inventory` field (containing all `ItemSlot` entries) is included in the JSON

- [ ] **Step 5: Compile and verify**

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Character/SaveLoad/ProfileSaveData/EquipmentSaveData.cs Assets/Scripts/Character/CharacterEquipment/CharacterEquipment.cs
git commit -m "feat(save): implement ICharacterSaveData for CharacterEquipment with nested inventory (priority 30)"
```

---

## Task 10: CharacterNeeds + CharacterTraits (Priority 40)

**Files:**
- Create: `Assets/Scripts/Character/SaveLoad/ProfileSaveData/NeedsSaveData.cs`
- Create: `Assets/Scripts/Character/SaveLoad/ProfileSaveData/TraitsSaveData.cs`
- Modify: `Assets/Scripts/Character/CharacterNeeds/CharacterNeeds.cs`
- Modify: `Assets/Scripts/Character/CharacterTraits/CharacterTraits.cs`

**Ref:** Spec Section 11 — Priority 40

- [ ] **Step 1: Read CharacterNeeds.cs and CharacterTraits.cs fully**

For Needs: `_allNeeds: List<CharacterNeed>` — each need has a type and current value.
For Traits: `behavioralTraitsProfile: CharacterBehavioralTraitsSO` with aggressivity, sociability, loyalty, canCreateCommunity.

- [ ] **Step 2: Create NeedsSaveData DTO**

```csharp
// Assets/Scripts/Character/SaveLoad/ProfileSaveData/NeedsSaveData.cs
using System.Collections.Generic;

[System.Serializable]
public class NeedsSaveData
{
    public List<NeedSaveEntry> needs = new List<NeedSaveEntry>();
}

[System.Serializable]
public class NeedSaveEntry
{
    public string needType; // Class name or enum
    public float value;
}
```

- [ ] **Step 3: Implement ICharacterSaveData<NeedsSaveData> on CharacterNeeds**

- SaveKey: `"CharacterNeeds"`, LoadPriority: `40`
- Serialize: iterate `_allNeeds`, save each need's type identifier and current value
- Deserialize: match saved entries to existing needs by type, restore values

- [ ] **Step 4: Create TraitsSaveData DTO**

```csharp
// Assets/Scripts/Character/SaveLoad/ProfileSaveData/TraitsSaveData.cs

[System.Serializable]
public class TraitsSaveData
{
    public string behavioralTraitsProfileId; // SO asset name
    // If traits can be modified at runtime, add individual float overrides here
}
```

- [ ] **Step 5: Implement ICharacterSaveData<TraitsSaveData> on CharacterTraits**

- SaveKey: `"CharacterTraits"`, LoadPriority: `40`

- [ ] **Step 6: Compile and verify**

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/Character/SaveLoad/ProfileSaveData/NeedsSaveData.cs Assets/Scripts/Character/SaveLoad/ProfileSaveData/TraitsSaveData.cs Assets/Scripts/Character/CharacterNeeds/CharacterNeeds.cs Assets/Scripts/Character/CharacterTraits/CharacterTraits.cs
git commit -m "feat(save): implement ICharacterSaveData for CharacterNeeds and CharacterTraits (priority 40)"
```

---

## Task 11: CharacterRelation — World-Scoped Relationships (Priority 50)

**Files:**
- Create: `Assets/Scripts/Character/SaveLoad/ProfileSaveData/RelationSaveData.cs`
- Modify: `Assets/Scripts/Character/CharacterRelation/CharacterRelation.cs`

**Ref:** Spec Sections 10, 11 — Priority 50

- [ ] **Step 1: Read CharacterRelation.cs fully**

Note: `_relationships: List<Relationship>`, with fields: `RelatedCharacter`, `RelationValue`, `RelationType`, `HasMet`.

- [ ] **Step 2: Create RelationSaveData DTO with world-scoped entries**

```csharp
// Assets/Scripts/Character/SaveLoad/ProfileSaveData/RelationSaveData.cs
using System.Collections.Generic;

[System.Serializable]
public class RelationSaveData
{
    public List<RelationshipSaveEntry> relationships = new List<RelationshipSaveEntry>();
}

[System.Serializable]
public class RelationshipSaveEntry
{
    public string targetCharacterId;
    public string targetWorldGuid;
    public int relationshipType;  // RelationshipType enum as int
    public int relationValue;     // Verify against CharacterRelation.cs — may be float
    public bool hasMet;
}
```

- [ ] **Step 3: Implement ICharacterSaveData<RelationSaveData> on CharacterRelation**

- SaveKey: `"CharacterRelation"`, LoadPriority: `50`
- Serialize: iterate `_relationships`, for each get `RelatedCharacter.CharacterId` and the current world's `WorldGuid` (from GameSaveData or a runtime accessor)
- Deserialize: restore relationships as dormant data; active character references are resolved lazily when the target NPC exists in the current world
- **Key consideration**: Relationships with NPCs not present in the current world remain as dormant entries (characterId + worldGuid stored, but `RelatedCharacter` reference is null)

- [ ] **Step 4: Compile and verify**

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Character/SaveLoad/ProfileSaveData/RelationSaveData.cs Assets/Scripts/Character/CharacterRelation/CharacterRelation.cs
git commit -m "feat(save): implement ICharacterSaveData for CharacterRelation with world-scoped entries (priority 50)"
```

---

## Task 12: CharacterParty, CharacterCommunity, CharacterJob, CharacterSchedule (Priority 60)

**Files:**
- Create: `Assets/Scripts/Character/SaveLoad/ProfileSaveData/PartySaveData.cs`
- Create: `Assets/Scripts/Character/SaveLoad/ProfileSaveData/CommunitySaveData.cs`
- Create: `Assets/Scripts/Character/SaveLoad/ProfileSaveData/JobSaveData.cs`
- Create: `Assets/Scripts/Character/SaveLoad/ProfileSaveData/ScheduleSaveData.cs`
- Modify: `Assets/Scripts/Character/CharacterParty/CharacterParty.cs`
- Modify: `Assets/Scripts/Character/CharacterCommunity/CharacterCommunity.cs`
- Modify: `Assets/Scripts/Character/CharacterJob/CharacterJob.cs`
- Modify: `Assets/Scripts/Character/CharacterSchedule/CharacterSchedule.cs`

**Ref:** Spec Section 11 — Priority 60

This is the largest task. Each subsystem is small individually but there are four.

- [ ] **Step 1: Read all four subsystem files**

- [ ] **Step 2: Create PartySaveData DTO**

```csharp
[System.Serializable]
public class PartySaveData
{
    public string partyId;
    public bool isLeader;
    public int followMode; // PartyFollowMode as int
}
```

- [ ] **Step 3: Implement ICharacterSaveData<PartySaveData> on CharacterParty**

- SaveKey: `"CharacterParty"`, LoadPriority: `60`
- Note: Party formation is handled by CharacterDataCoordinator after all profiles are imported. This save data just records the role/mode.

- [ ] **Step 4: Create CommunitySaveData DTO**

```csharp
[System.Serializable]
public class CommunitySaveData
{
    public string communityMapId;
    public string role; // or enum as int
}
```

- [ ] **Step 5: Implement ICharacterSaveData<CommunitySaveData> on CharacterCommunity**

- SaveKey: `"CharacterCommunity"`, LoadPriority: `60`

- [ ] **Step 6: Create JobSaveData DTO**

```csharp
[System.Serializable]
public class JobSaveData
{
    public string jobType;           // Job type identifier
    public string workplaceBuildingId; // Building GUID
    public int jobState;             // Current state as int
}
```

- [ ] **Step 7: Implement ICharacterSaveData<JobSaveData> on CharacterJob**

- SaveKey: `"CharacterJob"`, LoadPriority: `60`
- Note: Workplace reference is resolved by building GUID on load — may be null if building doesn't exist in current world

- [ ] **Step 8: Create ScheduleSaveData DTO**

```csharp
using System.Collections.Generic;

[System.Serializable]
public class ScheduleSaveData
{
    public List<ScheduleEntrySaveData> entries = new List<ScheduleEntrySaveData>();
}

[System.Serializable]
public class ScheduleEntrySaveData
{
    public int activity;   // ScheduleActivity as int
    public int startHour;
    public int endHour;
    public string anchorMapId;
    public float anchorX, anchorY, anchorZ;
}
```

- [ ] **Step 9: Implement ICharacterSaveData<ScheduleSaveData> on CharacterSchedule**

- SaveKey: `"CharacterSchedule"`, LoadPriority: `60`

- [ ] **Step 10: Compile and verify**

- [ ] **Step 11: Commit**

```bash
git add Assets/Scripts/Character/SaveLoad/ProfileSaveData/PartySaveData.cs Assets/Scripts/Character/SaveLoad/ProfileSaveData/CommunitySaveData.cs Assets/Scripts/Character/SaveLoad/ProfileSaveData/JobSaveData.cs Assets/Scripts/Character/SaveLoad/ProfileSaveData/ScheduleSaveData.cs Assets/Scripts/Character/CharacterParty/CharacterParty.cs Assets/Scripts/Character/CharacterCommunity/CharacterCommunity.cs Assets/Scripts/Character/CharacterJob/CharacterJob.cs Assets/Scripts/Character/CharacterSchedule/CharacterSchedule.cs
git commit -m "feat(save): implement ICharacterSaveData for Party, Community, Job, Schedule (priority 60)"
```

---

## Task 13: CharacterMapTracker + CharacterCombat (Priority 70)

**Files:**
- Create: `Assets/Scripts/Character/SaveLoad/ProfileSaveData/MapTrackerSaveData.cs`
- Create: `Assets/Scripts/Character/SaveLoad/ProfileSaveData/CombatSaveData.cs`
- Modify: `Assets/Scripts/Character/Components/CharacterMapTracker.cs`
- Modify: `Assets/Scripts/Character/CharacterCombat/CharacterCombat.cs`

**Ref:** Spec Section 11 — Priority 70

- [ ] **Step 1: Read CharacterMapTracker.cs and CharacterCombat.cs**

- [ ] **Step 2: Create MapTrackerSaveData DTO**

```csharp
[System.Serializable]
public class MapTrackerSaveData
{
    public string currentMapId;
    public float positionX, positionY, positionZ;
}
```

- [ ] **Step 3: Implement ICharacterSaveData<MapTrackerSaveData> on CharacterMapTracker**

- SaveKey: `"CharacterMapTracker"`, LoadPriority: `70`
- Note: CharacterMapTracker extends `NetworkBehaviour`, not `CharacterSystem`. It still implements `ICharacterSaveData<T>` directly.

- [ ] **Step 4: Create CombatSaveData DTO**

```csharp
using System.Collections.Generic;

[System.Serializable]
public class CombatSaveData
{
    public List<CombatStyleSaveEntry> knownStyles = new List<CombatStyleSaveEntry>();
    public string preferredStyleId; // Current style SO name
}

[System.Serializable]
public class CombatStyleSaveEntry
{
    public string styleId;  // CombatStyle SO name
    public int expertise;   // Expertise level
}
```

- [ ] **Step 5: Implement ICharacterSaveData<CombatSaveData> on CharacterCombat**

- SaveKey: `"CharacterCombat"`, LoadPriority: `70`

- [ ] **Step 6: Compile and verify**

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/Character/SaveLoad/ProfileSaveData/MapTrackerSaveData.cs Assets/Scripts/Character/SaveLoad/ProfileSaveData/CombatSaveData.cs Assets/Scripts/Character/Components/CharacterMapTracker.cs Assets/Scripts/Character/CharacterCombat/CharacterCombat.cs
git commit -m "feat(save): implement ICharacterSaveData for MapTracker and Combat (priority 70)"
```

---

## Task 14: Abandoned NPC System — Flagging & HibernatedNPCData

**Files:**
- Modify: `Assets/Scripts/World/MapSystem/MapSaveData.cs`
- Modify: `Assets/Scripts/Character/CharacterParty/CharacterParty.cs`

**Ref:** Spec Sections 8, 9

- [ ] **Step 1: Read MapSaveData.cs to see HibernatedNPCData structure**

- [ ] **Step 2: Add abandoned NPC fields to HibernatedNPCData**

```csharp
// Add to HibernatedNPCData:
public bool IsAbandoned;
public string FormerPartyLeaderId;
public string FormerPartyLeaderWorldGuid;
```

- [ ] **Step 3: Update MapController hibernation serialization**

When serializing NPCs to `HibernatedNPCData`, copy the abandoned fields from `Character`:
```csharp
IsAbandoned = character.IsAbandoned,
FormerPartyLeaderId = character.FormerPartyLeaderId,
FormerPartyLeaderWorldGuid = character.FormerPartyLeaderWorldGuid
```

And when deserializing (wake-up), restore them back to the spawned Character.

- [ ] **Step 4: Add party disband on leader disconnect to CharacterParty**

In `CharacterParty`, handle the `OnClientDisconnectCallback` (or wherever client disconnect is handled). When a party leader disconnects without portal gate:

```csharp
// Server-side: When party leader disconnects
public void HandleLeaderDisconnected()
{
    if (!IsServer || !IsInParty || !IsPartyLeader) return;

    // Flag all NPC members as abandoned
    foreach (string memberId in _partyData.MemberIds)
    {
        if (memberId == _character.CharacterId) continue;
        Character member = Character.FindByUUID(memberId);
        if (member == null || member.IsPlayer) continue;

        member.IsAbandoned = true;
        member.FormerPartyLeaderId = _character.CharacterId;
        member.FormerPartyLeaderWorldGuid = _character.OriginWorldGuid;
    }

    DisbandParty();
}
```

- [ ] **Step 5: Wire disconnect detection**

Find where client disconnection is handled (likely in a NetworkManager callback or Character.OnNetworkDespawn) and call `HandleLeaderDisconnected()` when a player-controlled character despawns.

- [ ] **Step 6: Compile and verify**

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/World/MapSystem/MapSaveData.cs Assets/Scripts/Character/CharacterParty/CharacterParty.cs
git commit -m "feat(save): add abandoned NPC flagging on leader disconnect and HibernatedNPCData fields"
```

---

## Task 15: Reclaim NPC Interaction

**Files:**
- Create: `Assets/Scripts/Character/Abandoned/ReclaimNPCInteraction.cs`

**Ref:** Spec Section 9 — Reclaim Interaction

- [ ] **Step 1: Read InteractionOption.cs to understand the actual API**

Read `Assets/Scripts/Interactable/InteractionOption.cs` and `Assets/Scripts/Interactable/IInteractionProvider.cs` to verify field names and constructor patterns.

- [ ] **Step 2: Create ReclaimNPCInteraction as a CharacterSystem + IInteractionProvider**

Must be a `CharacterSystem` (not plain `MonoBehaviour`) so it registers in the capability system and is discoverable via `Character.GetAll<IInteractionProvider>()`.

```csharp
// Assets/Scripts/Character/Abandoned/ReclaimNPCInteraction.cs
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Exposes a "Reclaim" interaction on abandoned NPCs.
/// Only visible to the former party leader who abandoned this NPC.
/// On execution: the abandoned NPC is despawned/destroyed.
/// </summary>
public class ReclaimNPCInteraction : CharacterSystem, IInteractionProvider
{
    public List<InteractionOption> GetInteractionOptions(Character interactor)
    {
        var options = new List<InteractionOption>();

        if (!_character.IsAbandoned) return options;
        if (interactor.CharacterId != _character.FormerPartyLeaderId) return options;

        // Use the actual InteractionOption API — verify constructor from InteractionOption.cs
        // Adapt field names (Name vs Label, Action vs OnSelected) to match codebase
        options.Add(new InteractionOption("Reclaim", () => RequestReclaimServerRpc()));

        return options;
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestReclaimServerRpc(ServerRpcParams rpcParams = default)
    {
        if (!_character.IsAbandoned) return;

        Debug.Log($"<color=cyan>[ReclaimNPC]</color> Reclaimed abandoned NPC {_character.CharacterName}");

        _character.GetComponent<NetworkObject>()?.Despawn(true);
    }
}
```

**Important:** Adapt the `InteractionOption` constructor to match the actual API found in Step 1. The ServerRpc pattern ensures only the server can despawn.

- [ ] **Step 2: Compile and verify**

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Character/Abandoned/ReclaimNPCInteraction.cs
git commit -m "feat(save): add ReclaimNPCInteraction for abandoned NPC reclaim mechanic"
```

---

## Task 16: Save Trigger Wiring

**Files:**
- Modify: Bed interaction script (find via grep for sleep/bed action)
- Modify: `Assets/Scripts/Core/SaveLoad/SaveManager.cs` (host shutdown)

**Ref:** Spec Section 7

- [ ] **Step 1: Find the bed/sleep interaction**

Search for the existing sleep action (likely `CharacterSleepAction` or similar in `Assets/Scripts/Character/CharacterActions/`). This is where solo save triggers.

- [ ] **Step 2: Wire bed save trigger**

In the sleep action's `OnApplyEffect()` or completion callback, add:

```csharp
// Server-only: save player profile on sleep
if (IsServer && _character.IsPlayer())
{
    var coordinator = _character.GetComponent<CharacterDataCoordinator>();
    if (coordinator != null)
        _ = coordinator.SaveLocalProfileAsync();
}
```

- [ ] **Step 3: Wire host shutdown save**

In `SaveManager` or the application quit handler, add profile save for the host's player character:

```csharp
// On host shutdown, save the host's profile (same as bed save)
private void OnApplicationQuit()
{
    if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
    {
        // Find the host's player character and save
        // Implementation depends on how the local player character is tracked
    }
}
```

- [ ] **Step 4: Compile and verify**

- [ ] **Step 5: Commit**

```bash
git add <modified files>
git commit -m "feat(save): wire save triggers for bed/sleep and host shutdown"
```

---

## Task 17: Profile Scanning for Game Launch

**Files:**
- Create or modify: A profile listing utility (can be a static method on `SaveFileHandler` or `CharacterDataCoordinator`)

**Ref:** Spec Section 12

- [ ] **Step 1: Add profile scanning to SaveFileHandler**

```csharp
/// <summary>
/// Scans the Profiles/ directory and returns metadata for each saved profile.
/// Used by the character selection screen to display available characters.
/// </summary>
public static List<CharacterProfileSaveData> GetAllProfiles()
{
    var profiles = new List<CharacterProfileSaveData>();
    if (!Directory.Exists(ProfileSaveDir)) return profiles;

    foreach (string file in Directory.GetFiles(ProfileSaveDir, "*.json"))
    {
        try
        {
            string json = File.ReadAllText(file);
            var profile = JsonConvert.DeserializeObject<CharacterProfileSaveData>(json);
            if (profile != null) profiles.Add(profile);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SaveFileHandler] Failed to read profile {file}: {e.Message}");
        }
    }

    return profiles;
}
```

- [ ] **Step 2: Compile and verify**

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Core/SaveLoad/SaveFileHandler.cs
git commit -m "feat(save): add profile scanning for character selection screen"
```

---

## Task 18: Integration — End-to-End Save/Load Verification

**Files:**
- No new files — verification only

**Ref:** Spec Sections 6, 7

- [ ] **Step 1: Enter Play Mode as Host in Unity Editor**

Use MCP `editor-application-set-state` to start play mode.

- [ ] **Step 2: Use ContextMenu "Debug: Log All SaveKeys"**

Right-click CharacterDataCoordinator in Inspector → Debug: Log All SaveKeys.
Verify all 15 subsystems appear in priority order (0, 10, 20, 20, 30, 40, 40, 50, 50, 60, 60, 60, 60, 70, 70).

- [ ] **Step 3: Use ContextMenu "Debug: Save Local Profile"**

Save the player's profile. Check `Application.persistentDataPath/Profiles/` for the generated JSON file.

- [ ] **Step 4: Verify JSON file contents**

Read the saved JSON and verify:
- `characterGuid` is a valid GUID
- `componentStates` has all 15 expected SaveKeys
- `partyMembers` is empty (if no party) or populated (if party exists)
- Each component state deserializes without error

- [ ] **Step 5: Use ContextMenu "Debug: Load Local Profile"**

Load the profile back. Check console for:
- "Profile imported for {name} (15/15 subsystems restored)"
- No errors or warnings

- [ ] **Step 6: Verify state roundtrip**

Check that character stats, equipment, position, etc. are the same after save+load.

- [ ] **Step 7: Commit any fixes found during verification**

---

## Task 19: Documentation — SKILL.md and CLAUDE.md Updates

**Files:**
- Create: `.agent/skills/save-load-system/SKILL.md` (update if exists)
- Modify: `CLAUDE.md`

**Ref:** Spec Section 14, CLAUDE.md rules 21, 28

- [ ] **Step 1: Update CLAUDE.md rule 20**

Change `ICharacterData` reference to `ICharacterSaveData<T>`:

```
20. The Character system must be decoupled from the World/Server state. Characters must be serialized as independent local files (e.g., .json) that can be loaded into any session (Solo or Multiplayer). When in Multiplayer, all inventory and stat changes must be saved back to the player's local character file upon portal gate return or at bed checkpoints. Use `ICharacterSaveData<T>` interface to ensure each subsystem provides typed, priority-ordered serialization for the portable character profile.
```

- [ ] **Step 2: Update or create save-load-system SKILL.md**

Document:
- The `ICharacterSaveData` / `ICharacterSaveData<T>` contract
- `CharacterSaveDataHelper` static helper
- `CharacterDataCoordinator` export/import flows
- `CharacterProfileSaveData` structure
- Save triggers (bed, portal gate)
- How to add a new saveable subsystem (step-by-step)
- Abandoned NPC system
- File paths and conventions

- [ ] **Step 3: Commit**

```bash
git add CLAUDE.md .agent/skills/save-load-system/SKILL.md
git commit -m "docs: update CLAUDE.md rule 20 and save-load-system SKILL.md for character persistence"
```

---

## Deferred: Multiplayer Network Transport

**Ref:** Spec Section 8

The following flows are defined in the spec but deferred to the portal gate implementation spec:
- Client sending `CharacterProfileSaveData` to host via NGO connection approval payload / fragmented RPC
- Host sending updated profile back to client via ClientRpc on portal return
- Host spawning party NPCs from the received profile

These require the portal gate system to exist first. The coordinator's `ExportProfile()` / `ImportProfile()` methods are ready — the transport layer just needs to call them.

---

## Summary

| Task | Description | Priority | Files |
|------|-------------|----------|-------|
| 1 | Save contract interfaces | Foundation | 2 files |
| 2 | CharacterProfileSaveData + GameSaveData + SaveFileHandler + WorldGuid | Foundation | 3 files |
| 3 | Character.cs identity + abandoned fields + FindByUUID | Foundation | 1 file |
| 4 | CharacterDataCoordinator rewrite | Foundation | 1 file |
| 5 | Migrate CharacterStats | Migration | 1 file |
| 6 | Migrate CharacterBookKnowledge | Migration | 1 file |
| 7 | CharacterProfile save (P0) | Subsystem | 2 files |
| 8 | CharacterSkills + CharacterAbilities (P20) | Subsystem | 4 files |
| 9 | CharacterEquipment + Inventory (P30) | Subsystem | 2 files |
| 10 | CharacterNeeds + CharacterTraits (P40) | Subsystem | 4 files |
| 11 | CharacterRelation (P50) | Subsystem | 2 files |
| 12 | Party, Community, Job, Schedule (P60) | Subsystem | 8 files |
| 13 | MapTracker + Combat (P70) | Subsystem | 4 files |
| 14 | Abandoned NPC flagging + HibernatedNPCData | Multiplayer | 2 files |
| 15 | Reclaim NPC interaction | Multiplayer | 1 file |
| 16 | Save trigger wiring (bed/sleep, host shutdown) | Integration | 2 files |
| 17 | Profile scanning for game launch | Integration | 1 file |
| 18 | End-to-end verification | Testing | 0 files |
| 19 | SKILL.md + CLAUDE.md docs | Documentation | 2 files |

**Total: 19 tasks, ~42 files touched**

**Recommended execution:** Tasks 1-4 are sequential (foundation). Tasks 5-13 can be parallelized (independent subsystem implementations). Tasks 14-15 depend on Task 3. Tasks 16-17 depend on Task 4. Task 18 depends on all prior tasks. Task 19 can run in parallel with Task 18.
