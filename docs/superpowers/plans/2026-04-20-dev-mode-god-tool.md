# Dev Mode (God Tool) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a host-only, toggle-able debug/dev mode whose first module is click-to-spawn NPCs configured with race, prefab, personality, behavioral traits, combat styles, and skills.

**Architecture:** `DevModeManager` (singleton) owns runtime state and the F3 input gate. `DevModePanel` is a Resources-loaded prefab with one tab per module; `DevSpawnModule` is the first tab. A chat-command hook in `UI_ChatBar` unlocks/toggles in shipping builds. Input gating on `PlayerController` / `PlayerInteractionDetector` routes clicks to dev mode when active. `SpawnManager` is extended to accept optional dev-picked configuration.

**Tech Stack:** Unity 2022+, C#, Unity UI (uGUI with TMP), Netcode for GameObjects (host authority), ScriptableObjects for data.

**Spec:** [docs/superpowers/specs/2026-04-20-dev-mode-god-tool-design.md](../specs/2026-04-20-dev-mode-god-tool-design.md)

**Testing approach:** No automated test suite exists in this Unity project. Each task includes manual verification steps executed in Play mode. `Debug.Log` statements at branching points are required (project rule 27) and must stay in.

**World scale reminder (rule 32):** 11 Unity units = 1.67 m. Scatter math in the plan uses Unity units.

---

## File Structure

### Files created
- `Assets/Scripts/Debug/DevMode/DevModeManager.cs` — singleton, state machine, F3 input, panel lifecycle
- `Assets/Scripts/Debug/DevMode/DevModePanel.cs` — panel root, tab registry, hides/shows on event
- `Assets/Scripts/Debug/DevMode/DevChatCommands.cs` — static command parser
- `Assets/Scripts/Debug/DevMode/Modules/DevSpawnModule.cs` — spawn tab logic
- `Assets/Scripts/Debug/DevMode/Modules/DevSpawnRow.cs` — reusable row prefab script (dropdown + level + X)
- `Assets/Resources/UI/DevModePanel.prefab` — Unity prefab (manual authoring)
- `Assets/Resources/UI/DevSpawnRow.prefab` — Unity prefab for a multi-entry row
- `.agent/skills/dev-mode/SKILL.md` — system documentation (project rules 21, 28)

### Files modified
- `Assets/Scripts/SpawnManager.cs` — drop `isPlayer`, extend `SpawnCharacter` signature
- `Assets/Scripts/Character/CharacterCombat/CharacterCombat.cs` — add `UnlockCombatStyle(style, level)` overload
- `Assets/Scripts/UI/UI_ChatBar.cs` — route `/`-prefixed lines to `DevChatCommands.Handle`
- `Assets/Scripts/DebugScript.cs` — remove character-spawn UI wiring (keep item/furniture)
- `Assets/Scripts/Character/CharacterControllers/PlayerController.cs` — input gating
- `Assets/Scripts/Character/PlayerInteractionDetector.cs` — input gating
- `.claude/agents/debug-tools-architect.md` — extend with dev-mode architecture

---

## Phase 1 — SpawnManager API Refactor (Foundation)

### Task 1: Remove `isPlayer` from public `SpawnManager.SpawnCharacter` API

**Scope clarification (important):** The public `SpawnCharacter(...)` entry point drops `bool isPlayer` because every caller (DebugScript, and the forthcoming dev-mode module) spawns NPCs. However, the internal `InitializeSpawnedCharacter(...)` **keeps** its `bool isPlayerObject` parameter because `Character.OnNetworkSpawn` at `Character.cs:397` passes `NetworkObject.IsPlayerObject` through it — that call is authoritative for real player spawns over the network and must continue to switch the character to player mode. Dropping `isPlayerObject` from `InitializeSpawnedCharacter` would silently break player spawns on all networked clients.

`SetupInteractionDetector` likewise keeps its `bool isPlayer` parameter.

**Files:**
- Modify: `Assets/Scripts/SpawnManager.cs:167-216` (SpawnCharacter) and `:209` (internal call)
- Modify: `Assets/Scripts/DebugScript.cs:148-167` (SpawnCharacters call)

**Pre-check — call-site audit (spec §13.1):**

- [ ] **Step 1.1: Grep call sites**

Run Grep for BOTH `SpawnCharacter\s*\(` AND `InitializeSpawnedCharacter\s*\(` across `Assets/`.

Expected:
- `Assets\Scripts\DebugScript.cs:160` → `SpawnManager.Instance.SpawnCharacter(...)`
- `Assets\Scripts\SpawnManager.cs:209` → internal `if (!InitializeSpawnedCharacter(character, race, isPlayer, personality))`
- `Assets\Scripts\SpawnManager.cs:218` → method definition
- `Assets\Scripts\Character\Character.cs:397` → `SpawnManager.Instance.InitializeSpawnedCharacter(this, networkRace, isPlayerObject, isLocalOwner);` — MUST remain unchanged after this task.

If any additional caller surfaces, STOP and update the plan.

- [ ] **Step 1.2: Change the PUBLIC `SpawnCharacter` signature only**

In `Assets/Scripts/SpawnManager.cs`, change:

```csharp
public Character SpawnCharacter(Vector3 pos, RaceSO race, GameObject visualPrefab, bool isPlayer, CharacterPersonalitySO personality = null)
```

to:

```csharp
public Character SpawnCharacter(Vector3 pos, RaceSO race, GameObject visualPrefab, CharacterPersonalitySO personality = null)
```

Leave `InitializeSpawnedCharacter` and `SetupInteractionDetector` untouched in this step.

- [ ] **Step 1.3: Update the internal call inside `SpawnCharacter`**

Inside the method body, the existing line 209:

```csharp
if (!InitializeSpawnedCharacter(character, race, isPlayer, personality))
```

becomes:

```csharp
// Public SpawnManager.SpawnCharacter always spawns NPCs. The networked player-spawn path
// runs through Character.OnNetworkSpawn → InitializeSpawnedCharacter(isPlayerObject: true)
// and does not go through this method.
if (!InitializeSpawnedCharacter(character, race, isPlayerObject: false, personality: personality))
```

The offline fallback branch still returns a valid NPC. The networked player spawn path (`Character.cs:397`) is untouched and keeps passing its own `isPlayerObject`.

- [ ] **Step 1.4: Update `DebugScript.cs` call site**

In `Assets/Scripts/DebugScript.cs:158-167`, change:

```csharp
for (int i = 0; i < number; i++)
{
    SpawnManager.Instance.SpawnCharacter(
        pos: pos,
        race: selectedRace,
        visualPrefab: selectedCharacterDefaultPrefab,
        isPlayer: isPlayerToggle.isOn && i == 0
    );
}
```

to:

```csharp
for (int i = 0; i < number; i++)
{
    SpawnManager.Instance.SpawnCharacter(
        pos: pos,
        race: selectedRace,
        visualPrefab: selectedCharacterDefaultPrefab
    );
}
```

Leave the `isPlayerToggle` field alone — Task 14 removes it with the rest of the character-spawn UI.

- [ ] **Step 1.5: Verify `Character.cs:397` is still valid**

Re-grep: `SpawnManager.Instance.InitializeSpawnedCharacter` in `Assets/Scripts/Character/Character.cs` should still compile because the 4-arg positional call `(this, networkRace, isPlayerObject, isLocalOwner)` matches the unchanged `InitializeSpawnedCharacter(Character, RaceSO, bool isPlayerObject, bool isLocalOwner = false, CharacterPersonalitySO personality = null)` signature. Do not modify Character.cs.

- [ ] **Step 1.6: Compile and manual verification**

Unity recompile clean. Enter Play mode as host. Open the existing DebugScript panel, click Spawn — an NPC appears.

If a network session was already running, verify the local player character (spawned via the session flow, not DebugScript) still shows up as the player (has PlayerController, receives WASD input). This proves `Character.OnNetworkSpawn`'s `SwitchToPlayer` path still fires.

- [ ] **Step 1.7: Commit**

```bash
git add Assets/Scripts/SpawnManager.cs Assets/Scripts/DebugScript.cs
git commit -m "refactor(spawn): drop isPlayer from public SpawnCharacter API

SpawnManager.SpawnCharacter always spawns NPCs. The networked player
spawn path via Character.OnNetworkSpawn continues to pass isPlayerObject
through the unchanged InitializeSpawnedCharacter signature."
```

---

### Task 2: Extend `SpawnCharacter` with dev-mode config params

**Files:**
- Modify: `Assets/Scripts/SpawnManager.cs` (SpawnCharacter signature, new pending-config dictionary, InitializeSpawnedCharacter tail)

- [ ] **Step 2.1: Extend `SpawnCharacter` signature**

In `SpawnManager.cs`, replace the Task 1 `SpawnCharacter` signature with:

```csharp
public Character SpawnCharacter(
    Vector3 pos,
    RaceSO race,
    GameObject visualPrefab,
    CharacterPersonalitySO personality = null,
    CharacterBehavioralTraitsSO traits = null,
    List<(CombatStyleSO style, int level)> combatStyles = null,
    List<(SkillSO skill, int level)> skills = null)
```

**Do NOT change `InitializeSpawnedCharacter`'s public signature.** Its `(Character, RaceSO, bool isPlayerObject, bool isLocalOwner = false, CharacterPersonalitySO personality = null)` form must stay stable so `Character.cs:397` continues to compile and work. The dev-mode extras travel through a side-channel (pending dictionary) introduced in Step 2.2.

Required `using` additions at the top of the file:

```csharp
using System.Collections.Generic;
```

(`System.Linq` is already present.)

- [ ] **Step 2.2: Add a pending-config dictionary**

Add a private nested struct and field to `SpawnManager`:

```csharp
private struct PendingDevConfig
{
    public CharacterBehavioralTraitsSO Traits;
    public List<(CombatStyleSO style, int level)> CombatStyles;
    public List<(SkillSO skill, int level)> Skills;
}

private readonly Dictionary<ulong, PendingDevConfig> _pendingDevConfig = new Dictionary<ulong, PendingDevConfig>();
```

Inside `SpawnCharacter`, locate the line `if (characterPrefabObj.TryGetComponent(out Unity.Netcode.NetworkObject netObj))` inside the `IsServer` branch. Just before `netObj.Spawn(true);`, add:

```csharp
if ((traits != null) || (combatStyles != null && combatStyles.Count > 0) || (skills != null && skills.Count > 0))
{
    _pendingDevConfig[netObj.NetworkObjectId] = new PendingDevConfig
    {
        Traits = traits,
        CombatStyles = combatStyles,
        Skills = skills
    };
}
```

Also, for the offline (non-networked) branch at the bottom of `SpawnCharacter` that calls `InitializeSpawnedCharacter` directly, the traits/combat/skills apply inline. Update that call to pass `personality`, and thread the extras through a small helper. Simplest form — after `InitializeSpawnedCharacter(character, race, false, personality)` returns `true`, inline-apply the dev extras:

```csharp
if (!InitializeSpawnedCharacter(character, race, isPlayerObject: false, personality: personality))
{
    Destroy(characterPrefabObj);
    return null;
}
ApplyDevExtras(character, traits, combatStyles, skills);
```

Add the helper:

```csharp
private void ApplyDevExtras(
    Character character,
    CharacterBehavioralTraitsSO traits,
    List<(CombatStyleSO style, int level)> combatStyles,
    List<(SkillSO skill, int level)> skills)
{
    if (character == null) return;

    if (traits != null && character.CharacterTraits != null)
    {
        character.CharacterTraits.behavioralTraitsProfile = traits;
        Debug.Log($"<color=cyan>[Spawn]</color> Dev-mode: {character.CharacterName} trait overridden to {traits.name}");
    }

    if (combatStyles != null && character.CharacterCombat != null)
    {
        foreach (var entry in combatStyles)
        {
            if (entry.style == null) continue;
            character.CharacterCombat.UnlockCombatStyle(entry.style, entry.level);
            Debug.Log($"<color=cyan>[Spawn]</color> Dev-mode: {character.CharacterName} combat style {entry.style.StyleName} L{entry.level}");
        }
    }

    if (skills != null && character.CharacterSkills != null)
    {
        foreach (var entry in skills)
        {
            if (entry.skill == null) continue;
            character.CharacterSkills.AddSkill(entry.skill, entry.level);
            Debug.Log($"<color=cyan>[Spawn]</color> Dev-mode: {character.CharacterName} skill {entry.skill.SkillName} L{entry.level}");
        }
    }
}
```

- [ ] **Step 2.3: Drain the pending dictionary in `InitializeSpawnedCharacter`**

At the end of `InitializeSpawnedCharacter`, just before `return true;`, add:

```csharp
// --- DEV-MODE OVERRIDES ---
// Applies any pending dev config recorded by SpawnCharacter just before network spawn.
// Only fires for networked spawns where SpawnCharacter returned before InitializeSpawnedCharacter
// ran (Character.OnNetworkSpawn is the caller). Offline spawns apply via ApplyDevExtras inline.
if (character.IsSpawned && character.NetworkObject != null
    && _pendingDevConfig.TryGetValue(character.NetworkObject.NetworkObjectId, out var pending))
{
    _pendingDevConfig.Remove(character.NetworkObject.NetworkObjectId);
    ApplyDevExtras(character, pending.Traits, pending.CombatStyles, pending.Skills);
}
```

NOTE: `UnlockCombatStyle(style, level)` is introduced in Task 3. The compile will fail until that overload exists — keep Task 3 as the very next commit so the tree compiles.

- [ ] **Step 2.4: Keep the networked non-dev path intact**

Trace both branches of `SpawnCharacter`:
1. Server branch with network — calls `netObj.Spawn(true)` and returns. `InitializeSpawnedCharacter` runs later via `Character.OnNetworkSpawn`. The pending dictionary bridges the gap.
2. Offline branch — calls `InitializeSpawnedCharacter` inline, then `ApplyDevExtras` inline. Pending dictionary never populated for this branch.

Dictionary lookup is keyed by `NetworkObjectId`; guard with `character.IsSpawned` first (already in the snippet). For `Character.cs:397`'s networked *player* path, no dev config is ever queued (only dev-mode calls to `SpawnCharacter` populate the dict), so the lookup fails silently and player init proceeds unchanged.

- [ ] **Step 2.5: Compile — expect `UnlockCombatStyle(style, level)` error**

Let the compile fail with "no overload for UnlockCombatStyle with 2 args". That proves Task 3 is needed. Do NOT commit until Task 3 lands.

---

### Task 3: Add `CharacterCombat.UnlockCombatStyle(CombatStyleSO, int level)` overload

**Naming rationale:** The existing method at `CharacterCombat.cs:849` is `UnlockCombatStyle(CombatStyleSO style)` — not `AddCombatStyle`. To keep naming consistent and avoid two similarly-named methods, the new overload is `UnlockCombatStyle(style, int level)`. The existing `UnlockCombatStyle(style)` is already called from `CharacterMentorship.cs:445` and stays untouched.

**Files:**
- Modify: `Assets/Scripts/Character/CharacterCombat/CharacterCombat.cs` (immediately after existing `UnlockCombatStyle(CombatStyleSO style)` at line ~849)

- [ ] **Step 3.1: Locate existing method**

Open `Assets/Scripts/Character/CharacterCombat/CharacterCombat.cs`. Search for `public void UnlockCombatStyle(`. One match at line ~849, body:

```csharp
public void UnlockCombatStyle(CombatStyleSO style)
{
    if (style == null) return;
    if (!_knownStyles.Exists(s => s.Style == style))
    {
        _knownStyles.Add(new CombatStyleExpertise(style));
        Debug.Log($"<color=yellow>[Combat]<\color> Nouveau style débloqué : {style.StyleName}");
    }
}
```

- [ ] **Step 3.2: Add the overload immediately after**

```csharp
/// <summary>
/// Unlocks a known combat style at a specific starting level. Used by dev-mode spawn
/// and by save/load restore. XP starts at 0. No-op if the style is already known.
/// </summary>
public void UnlockCombatStyle(CombatStyleSO style, int level)
{
    if (style == null) return;
    if (_knownStyles.Exists(s => s.Style == style))
    {
        Debug.LogWarning($"<color=orange>[Combat]</color> {_character.CharacterName} already knows {style.StyleName} — ignoring dev-mode unlock.");
        return;
    }

    _knownStyles.Add(new CombatStyleExpertise(style, level, 0f));
    Debug.Log($"<color=yellow>[Combat]</color> {_character.CharacterName} learned {style.StyleName} at L{level} (dev-mode).");
}
```

- [ ] **Step 3.3: Compile and verify Task 2 now compiles**

Unity recompiles cleanly. No runtime behavior to test yet.

- [ ] **Step 3.4: Commit (bundles Tasks 2 + 3 since they must ship together)**

```bash
git add Assets/Scripts/SpawnManager.cs Assets/Scripts/Character/CharacterCombat/CharacterCombat.cs
git commit -m "feat(spawn): extend SpawnCharacter with dev-mode config

Optional personality/traits/combat/skills params thread into
InitializeSpawnedCharacter via a pending-dev-config dictionary keyed on
NetworkObjectId. Offline path applies via ApplyDevExtras inline. New
CharacterCombat.UnlockCombatStyle(style, level) overload supports the
combat path."
```

---

## Phase 2 — DevModeManager Core

### Task 4: Create `DevModeManager.cs`

**Files:**
- Create: `Assets/Scripts/Debug/DevMode/DevModeManager.cs`

- [ ] **Step 4.1: Ensure folder exists**

Use the `assets-create-folder` MCP tool on `Assets/Scripts/Debug/DevMode/` (creates `Debug` and `DevMode`). Then again on `Assets/Scripts/Debug/DevMode/Modules/` for Task 11+.

- [ ] **Step 4.2: Write `DevModeManager.cs`**

```csharp
using System;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Host-only dev/god mode controller. Toggles a debug panel, owns the IsEnabled
/// event, and gates player input while active.
///
/// Activation:
///   - Editor & development builds: IsUnlocked = true on Awake; F3 toggles.
///   - Release builds: IsUnlocked = false; /devmode on unlocks, then F3 works.
///   - Clients (non-host): F3 and /devmode both log "host-only" and do nothing.
///
/// Authority: all dev-mode actions are host-only. See spec §12 for known
/// replication gaps on personality/traits/combat.
/// </summary>
public class DevModeManager : MonoBehaviour
{
    public static DevModeManager Instance { get; private set; }

    [Tooltip("Prefab of the root dev-mode panel. Loaded from Resources/UI/DevModePanel if null.")]
    [SerializeField] private GameObject _panelPrefab;

    public bool IsUnlocked { get; private set; }
    public bool IsEnabled { get; private set; }

    /// <summary>
    /// True iff dev mode is currently active on this machine. Read by PlayerController
    /// and PlayerInteractionDetector to suppress player input while the god tool has focus.
    /// </summary>
    public static bool SuppressPlayerInput => Instance != null && Instance.IsEnabled;

    public event Action<bool> OnDevModeChanged;

    private GameObject _panelInstance;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        IsUnlocked = true;
        Debug.Log("<color=magenta>[DevMode]</color> Unlocked by default in Editor/Development build. Press F3 to toggle.");
#else
        IsUnlocked = false;
        Debug.Log("<color=magenta>[DevMode]</color> Locked in release build. Type /devmode on in chat to unlock.");
#endif
    }

    private void Update()
    {
        if (!IsUnlocked) return;
        if (Input.GetKeyDown(KeyCode.F3))
        {
            TryToggle();
        }
    }

    /// <summary>
    /// Arms the feature: makes F3 responsive. Safe to call repeatedly.
    /// </summary>
    public void Unlock()
    {
        if (!IsUnlocked)
        {
            IsUnlocked = true;
            Debug.Log("<color=magenta>[DevMode]</color> Unlocked.");
        }
    }

    /// <summary>
    /// Fully relocks: disables AND clears IsUnlocked. Only used by explicit teardown
    /// (scene unload, or an explicit admin command if added later). Chat /devmode off
    /// uses Disable() instead, so the host doesn't need to retype /devmode on.
    /// </summary>
    public void Lock()
    {
        Disable();
        IsUnlocked = false;
        Debug.Log("<color=magenta>[DevMode]</color> Fully locked.");
    }

    public bool TryEnable()
    {
        if (!IsUnlocked)
        {
            Debug.LogWarning("<color=orange>[DevMode]</color> Not unlocked — run /devmode on first.");
            return false;
        }

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && !NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning("<color=orange>[DevMode]</color> Dev mode is host-only.");
            return false;
        }

        if (IsEnabled) return true;

        EnsurePanel();
        if (_panelInstance != null) _panelInstance.SetActive(true);
        IsEnabled = true;
        OnDevModeChanged?.Invoke(true);
        Debug.Log("<color=magenta>[DevMode]</color> Enabled.");
        return true;
    }

    public void Disable()
    {
        if (!IsEnabled) return;
        IsEnabled = false;
        if (_panelInstance != null) _panelInstance.SetActive(false);
        OnDevModeChanged?.Invoke(false);
        Debug.Log("<color=magenta>[DevMode]</color> Disabled.");
    }

    public bool TryToggle()
    {
        if (IsEnabled) { Disable(); return true; }
        return TryEnable();
    }

    private void EnsurePanel()
    {
        if (_panelInstance != null) return;

        GameObject prefab = _panelPrefab;
        if (prefab == null)
        {
            prefab = Resources.Load<GameObject>("UI/DevModePanel");
            if (prefab == null)
            {
                Debug.LogError("<color=red>[DevMode]</color> DevModePanel prefab not found at Resources/UI/DevModePanel.");
                return;
            }
        }

        _panelInstance = Instantiate(prefab);
        _panelInstance.SetActive(false);
        DontDestroyOnLoad(_panelInstance);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
```

- [ ] **Step 4.3: Compile check**

Unity should recompile with no errors. The script references `Resources/UI/DevModePanel` which doesn't exist yet — that's fine, the error only fires at runtime when `TryEnable` runs.

- [ ] **Step 4.4: Commit**

```bash
git add Assets/Scripts/Debug/DevMode/DevModeManager.cs
git commit -m "feat(devmode): add DevModeManager singleton

Host-only. F3 toggles in Editor/Development builds. Unlock via chat
command in release. Broadcasts OnDevModeChanged and exposes
SuppressPlayerInput for input gating."
```

---

### Task 5: Attach `DevModeManager` component to scene

**Files:**
- Modify: Unity scene — attach component to the `SpawnManager` GameObject (and `DontDestroyOnLoad` persistence is via Awake on SpawnManager).

- [ ] **Step 5.1: Locate `SpawnManager` GameObject in scene**

Open the scene that contains `SpawnManager` (likely `Assets/Scenes/GameScene.unity`). Use `scene-list-opened` / `scene-open` MCP tools.

Use `gameobject-find` to locate any GameObject with a `SpawnManager` component. The GO name may not literally be "SpawnManager" — if `gameobject-find` by name fails, run it with the component filter or inspect the scene hierarchy via `scene-get-data`. Record the actual GO name before proceeding.

- [ ] **Step 5.2: Add `DevModeManager` component**

Use `gameobject-component-add` MCP tool with type name `DevModeManager` on the located GameObject.

- [ ] **Step 5.3: Save scene**

Use `scene-save`.

- [ ] **Step 5.4: Play-mode verification**

Enter Play mode. Console should print:

> `<color=magenta>[DevMode]</color> Unlocked by default in Editor/Development build. Press F3 to toggle.`

Press F3. Expect:

> `<color=red>[DevMode]</color> DevModePanel prefab not found at Resources/UI/DevModePanel.`

(Panel doesn't exist yet — this proves F3 wiring works. Task 13 will create the prefab.)

- [ ] **Step 5.5: Commit (scene only)**

```bash
git add Assets/Scenes/GameScene.unity
git commit -m "chore(scene): attach DevModeManager to SpawnManager GameObject"
```

---

## Phase 3 — Chat Command Routing

### Task 6: Create `DevChatCommands.cs`

**Files:**
- Create: `Assets/Scripts/Debug/DevMode/DevChatCommands.cs`

- [ ] **Step 6.1: Write the parser**

```csharp
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Static command parser invoked by UI_ChatBar when the submitted text starts with "/".
/// All commands are host-only for now.
/// </summary>
public static class DevChatCommands
{
    /// <summary>
    /// Entry point. rawInput includes the leading "/". Returns silently (no speech)
    /// once handled — caller suppresses the Say() path when input starts with "/".
    /// </summary>
    public static void Handle(string rawInput)
    {
        if (string.IsNullOrWhiteSpace(rawInput) || rawInput.Length < 2 || rawInput[0] != '/') return;

        string body = rawInput.Substring(1).Trim();
        if (string.IsNullOrEmpty(body)) return;

        string[] parts = body.Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
        string cmd = parts[0].ToLowerInvariant();

        switch (cmd)
        {
            case "devmode":
                HandleDevmode(parts);
                break;
            default:
                Debug.LogWarning($"<color=orange>[DevChat]</color> Unknown command: /{cmd}");
                break;
        }
    }

    private static void HandleDevmode(string[] parts)
    {
        // Host check
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && !NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning("<color=orange>[DevChat]</color> Dev mode is host-only.");
            return;
        }

        if (DevModeManager.Instance == null)
        {
            Debug.LogError("<color=red>[DevChat]</color> DevModeManager is not present in the scene.");
            return;
        }

        if (parts.Length < 2)
        {
            Debug.Log("<color=magenta>[DevChat]</color> Usage: /devmode on | off");
            return;
        }

        string arg = parts[1].ToLowerInvariant();
        switch (arg)
        {
            case "on":
                DevModeManager.Instance.Unlock();
                DevModeManager.Instance.TryEnable();
                break;
            case "off":
                DevModeManager.Instance.Disable();
                break;
            default:
                Debug.Log("<color=magenta>[DevChat]</color> Usage: /devmode on | off");
                break;
        }
    }
}
```

- [ ] **Step 6.2: Compile check**

Should compile cleanly. No runtime behavior without the `UI_ChatBar` change.

- [ ] **Step 6.3: Commit**

```bash
git add Assets/Scripts/Debug/DevMode/DevChatCommands.cs
git commit -m "feat(devmode): add DevChatCommands static parser

Handles /devmode on|off with host authority check and DevModeManager
wiring. Unknown commands logged and swallowed."
```

---

### Task 7: Hook `DevChatCommands.Handle` into `UI_ChatBar`

**Files:**
- Modify: `Assets/Scripts/UI/UI_ChatBar.cs:78-121` (OnSubmitChat)

- [ ] **Step 7.1: Route slash commands before Say()**

Locate `OnSubmitChat(string text)` in `UI_ChatBar.cs`. Modify it so that after the whitespace/empty early-return but BEFORE the `_character.CharacterSpeech.Say(text)` call, a `/`-prefixed input is routed to the command parser.

Current structure (line 78 onwards):

```csharp
private void OnSubmitChat(string text)
{
    _lastSubmitFrame = Time.frameCount;

    if (string.IsNullOrWhiteSpace(text))
    {
        // ... clear + deactivate
        return;
    }

    if (_character != null)
    {
        if (_character.CharacterSpeech != null)
        {
            _character.CharacterSpeech.Say(text);
            // ... clear + deactivate
        }
        // ...
    }
}
```

Insert immediately after the `IsNullOrWhiteSpace` early-return:

```csharp
string trimmed = text.Trim();
if (trimmed.StartsWith("/"))
{
    DevChatCommands.Handle(trimmed);

    if (_inputField != null)
    {
        _inputField.text = string.Empty;
        _inputField.DeactivateInputField();
    }
    if (UnityEngine.EventSystems.EventSystem.current != null)
    {
        UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
    }
    return;
}
```

- [ ] **Step 7.2: Play-mode verification**

Enter Play mode. Focus the chat bar (Enter), type `/devmode off`, press Enter. Expect:

> `<color=magenta>[DevMode]</color> Disabled.` (only if previously enabled; otherwise nothing)

Type `/devmode on`, Enter. Expect:

> `<color=magenta>[DevMode]</color> Enabled.`
>
> …plus the not-found error for the prefab (still expected until Task 13).

Type `/unknown`, Enter. Expect:

> `<color=orange>[DevChat]</color> Unknown command: /unknown`

Chat bubble should NOT appear above the character for any `/`-prefixed line.

- [ ] **Step 7.3: Commit**

```bash
git add Assets/Scripts/UI/UI_ChatBar.cs
git commit -m "feat(chat): route /-prefixed lines to DevChatCommands

Slash commands bypass the Say() path. Input field clears and
deactivates like a normal submit."
```

---

## Phase 4 — Input Gating

### Task 8: Gate `PlayerController` input reads

**Files:**
- Modify: `Assets/Scripts/Character/CharacterControllers/PlayerController.cs:50-164` (Update)

- [ ] **Step 8.1: Add the gate**

Locate `protected override void Update()` at line ~50. After the `if (IsOwner)` opening brace (line ~52), add at the very top of the owner block:

```csharp
if (DevModeManager.SuppressPlayerInput)
{
    _inputDir = Vector3.zero;
    base.Update();
    Move();
    return;
}
```

Place it BEFORE the existing "Block player movement/action input if typing in any UI text field" check. The dev-mode gate supersedes the chat-field gate.

- [ ] **Step 8.2: Play-mode verification**

Enter Play mode. WASD moves the character normally. Press F3 (or `/devmode on`). Verify:
- Console: `<color=magenta>[DevMode]</color> Enabled.`
- WASD no longer moves the character.
- Right-click no longer issues a move command.
- Space no longer attacks.
- Pressing F3 again (or `/devmode off`) restores full control.

Multiplayer check (spec §10 rows "Host dev mode ON, client dev mode OFF"): if a second client is connected, the client's input is not suppressed (their `DevModeManager.Instance.IsEnabled` is false on their machine).

- [ ] **Step 8.3: Commit**

```bash
git add Assets/Scripts/Character/CharacterControllers/PlayerController.cs
git commit -m "feat(devmode): gate PlayerController input while dev mode active

Suppresses WASD, right-click move, TAB target, Space attack. Movement
returns to rest via base.Update() + Move() so NavMesh state is correct
when exiting dev mode."
```

---

### Task 9: Gate `PlayerInteractionDetector` input reads

**Files:**
- Modify: `Assets/Scripts/Character/PlayerInteractionDetector.cs:169-243` (Update)

- [ ] **Step 9.1: Add the gate**

Locate `private void Update()` at line ~169. After the existing ownership early-return (`if (Character.TryGetComponent(out Unity.Netcode.NetworkObject netObj) && netObj.IsSpawned && !netObj.IsOwner) return;`), add:

```csharp
if (DevModeManager.SuppressPlayerInput)
{
    return;
}
```

Placement: after the ownership check but BEFORE `UpdateClosestTarget()`. The prompt UI should still reflect proximity while dev mode is on; actually... re-read the spec: "E keys suppressed" is implied. The spec says "gate mouse reads," but the relevant reads here are E-key reads. Applying the gate before `UpdateClosestTarget()` is too aggressive — the nearby list still needs maintaining so exiting dev mode restores promptly.

Revised placement: after `UpdateClosestTarget()` and the chat-field gate, but BEFORE the E-key `if (Input.GetKeyDown(KeyCode.E))` block. Final insert point:

```csharp
// Prevent interacting if the player is currently typing in an input field (e.g. chat)
if (UnityEngine.EventSystems.EventSystem.current != null &&
    UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject != null &&
    UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject.GetComponent<TMPro.TMP_InputField>() != null)
{
    return;
}

// Dev mode suppresses gameplay input. Nearby-target tracking above still runs.
if (DevModeManager.SuppressPlayerInput)
{
    return;
}
```

- [ ] **Step 9.2: Play-mode verification**

Walk near an interactable (chest, NPC). Prompt appears. Press F3. Press E — nothing happens. Press F3 again — pressing E works.

- [ ] **Step 9.3: Commit**

```bash
git add Assets/Scripts/Character/PlayerInteractionDetector.cs
git commit -m "feat(devmode): gate PlayerInteractionDetector E key

Nearby-target tracking still runs so exiting dev mode restores the
prompt immediately. Only the E-press path is blocked."
```

---

## Phase 5 — DevModePanel + DevSpawnModule

### Task 10: Create `DevModePanel.cs`

**Files:**
- Create: `Assets/Scripts/Debug/DevMode/DevModePanel.cs`

- [ ] **Step 10.1: Write the script**

```csharp
using UnityEngine;

/// <summary>
/// Root of the dev-mode UI. Lives on the DevModePanel prefab. Listens to
/// DevModeManager.OnDevModeChanged to show/hide itself. Tabs (child modules)
/// self-register via Start() — no explicit registry needed for the first slice.
/// </summary>
public class DevModePanel : MonoBehaviour
{
    [SerializeField] private GameObject _contentRoot;

    private void OnEnable()
    {
        if (DevModeManager.Instance != null)
        {
            DevModeManager.Instance.OnDevModeChanged += HandleDevModeChanged;
            HandleDevModeChanged(DevModeManager.Instance.IsEnabled);
        }
    }

    private void OnDisable()
    {
        if (DevModeManager.Instance != null)
        {
            DevModeManager.Instance.OnDevModeChanged -= HandleDevModeChanged;
        }
    }

    private void HandleDevModeChanged(bool isEnabled)
    {
        if (_contentRoot != null)
        {
            _contentRoot.SetActive(isEnabled);
        }
    }
}
```

NOTE: The panel prefab itself is already enabled/disabled at the root by `DevModeManager.Disable()` / `TryEnable()`. `_contentRoot` exists so a fade animation or split of "persistent header" vs "tab content" can be added later without API change. For this slice, wire `_contentRoot` to the same GameObject that hosts the Spawn tab content.

- [ ] **Step 10.2: Commit**

```bash
git add Assets/Scripts/Debug/DevMode/DevModePanel.cs
git commit -m "feat(devmode): add DevModePanel root script"
```

---

### Task 11: Create `DevSpawnRow.cs`

**Files:**
- Create: `Assets/Scripts/Debug/DevMode/Modules/DevSpawnRow.cs`

Reusable row component for "Combat Styles" and "Skills" lists. Holds a dropdown, an int field, and a remove button. Exposes the selected index and level for the parent module to read during spawn.

- [ ] **Step 11.1: Write the script**

```csharp
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A single row in a multi-entry list (combat styles or skills).
/// Layout: [Dropdown][Level IntField][X Button]
/// </summary>
public class DevSpawnRow : MonoBehaviour
{
    [SerializeField] private TMP_Dropdown _dropdown;
    [SerializeField] private TMP_InputField _levelField;
    [SerializeField] private Button _removeButton;

    public event Action<DevSpawnRow> OnRemoveClicked;

    public int SelectedIndex => _dropdown != null ? _dropdown.value : -1;

    public int Level
    {
        get
        {
            if (_levelField == null) return 1;
            if (int.TryParse(_levelField.text, out int v)) return Mathf.Max(1, v);
            return 1;
        }
    }

    private void Awake()
    {
        if (_removeButton != null)
        {
            _removeButton.onClick.AddListener(HandleRemoveClicked);
        }
    }

    private void OnDestroy()
    {
        if (_removeButton != null)
        {
            _removeButton.onClick.RemoveListener(HandleRemoveClicked);
        }
    }

    /// <summary>
    /// Populates the dropdown with string options and defaults the level to 1.
    /// </summary>
    public void Populate(System.Collections.Generic.List<string> options, int defaultLevel = 1)
    {
        if (_dropdown != null)
        {
            _dropdown.ClearOptions();
            _dropdown.AddOptions(options);
            _dropdown.value = 0;
            _dropdown.RefreshShownValue();
        }
        if (_levelField != null)
        {
            _levelField.text = defaultLevel.ToString();
        }
    }

    private void HandleRemoveClicked()
    {
        OnRemoveClicked?.Invoke(this);
    }
}
```

- [ ] **Step 11.2: Commit**

```bash
git add Assets/Scripts/Debug/DevMode/Modules/DevSpawnRow.cs
git commit -m "feat(devmode): add DevSpawnRow reusable row script"
```

---

### Task 12: Create `DevSpawnModule.cs`

**Files:**
- Create: `Assets/Scripts/Debug/DevMode/Modules/DevSpawnModule.cs`

- [ ] **Step 12.1: Write the script**

```csharp
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Spawn tab of the dev-mode panel. Owns all dropdowns, multi-entry rows, the
/// Count field, and the Armed toggle. When armed, left-click on the Environment
/// layer spawns N configured NPCs at the cursor.
/// </summary>
public class DevSpawnModule : MonoBehaviour
{
    [Header("Core dropdowns")]
    [SerializeField] private TMP_Dropdown _raceDropdown;
    [SerializeField] private TMP_Dropdown _prefabDropdown;
    [SerializeField] private TMP_Dropdown _personalityDropdown;
    [SerializeField] private TMP_Dropdown _traitDropdown;

    [Header("Combat styles list")]
    [SerializeField] private Transform _combatStylesContainer;
    [SerializeField] private Button _addCombatStyleButton;

    [Header("Skills list")]
    [SerializeField] private Transform _skillsContainer;
    [SerializeField] private Button _addSkillButton;

    [Header("Count & Armed")]
    [SerializeField] private TMP_InputField _countField;
    [SerializeField] private Toggle _armedToggle;

    [Header("Row prefab")]
    [Tooltip("Prefab with DevSpawnRow script. Shared between combat and skill lists.")]
    [SerializeField] private DevSpawnRow _rowPrefab;

    // --- Cached catalogs (Resources) ---
    private List<RaceSO> _races = new List<RaceSO>();
    private List<GameObject> _racePrefabs = new List<GameObject>();
    private List<CharacterPersonalitySO> _personalities = new List<CharacterPersonalitySO>();
    private List<CharacterBehavioralTraitsSO> _traits = new List<CharacterBehavioralTraitsSO>();
    private List<CombatStyleSO> _combatStyles = new List<CombatStyleSO>();
    private List<SkillSO> _skills = new List<SkillSO>();

    private readonly List<DevSpawnRow> _combatRows = new List<DevSpawnRow>();
    private readonly List<DevSpawnRow> _skillRows = new List<DevSpawnRow>();

    private const int ENVIRONMENT_LAYER_MASK_FALLBACK = ~0; // used only if Environment layer missing
    private int _environmentLayerMask;

    private void Start()
    {
        LoadCatalogs();
        PopulateCoreDropdowns();
        WireListeners();
        SetupEnvironmentLayerMask();
    }

    private void OnDestroy()
    {
        UnwireListeners();
    }

    // ─── Catalog loading ─────────────────────────────────────────────

    private void LoadCatalogs()
    {
        _races.Clear();
        if (GameSessionManager.Instance != null && GameSessionManager.Instance.AvailableRaces != null)
        {
            _races.AddRange(GameSessionManager.Instance.AvailableRaces);
        }

        _personalities.Clear();
        _personalities.AddRange(Resources.LoadAll<CharacterPersonalitySO>("Data/Personnality"));

        _traits.Clear();
        _traits.AddRange(Resources.LoadAll<CharacterBehavioralTraitsSO>("Data/Behavioural Traits"));

        _combatStyles.Clear();
        _combatStyles.AddRange(Resources.LoadAll<CombatStyleSO>("Data/CombatStyle"));

        _skills.Clear();
        _skills.AddRange(Resources.LoadAll<SkillSO>("Data/Skills"));

        Debug.Log($"<color=cyan>[DevSpawn]</color> Catalogs loaded — races:{_races.Count} personalities:{_personalities.Count} traits:{_traits.Count} combat:{_combatStyles.Count} skills:{_skills.Count}");
    }

    private void PopulateCoreDropdowns()
    {
        if (_raceDropdown != null)
        {
            _raceDropdown.ClearOptions();
            var names = new List<string>();
            foreach (var r in _races) names.Add(r.raceName);
            _raceDropdown.AddOptions(names);
            _raceDropdown.value = 0;
            _raceDropdown.RefreshShownValue();
            RefreshPrefabDropdown();
        }

        if (_personalityDropdown != null)
        {
            var names = new List<string> { "Random" };
            foreach (var p in _personalities) names.Add(p.PersonalityName);
            _personalityDropdown.ClearOptions();
            _personalityDropdown.AddOptions(names);
            _personalityDropdown.value = 0;
            _personalityDropdown.RefreshShownValue();
        }

        if (_traitDropdown != null)
        {
            var names = new List<string> { "Random" };
            foreach (var t in _traits) names.Add(t.name);
            _traitDropdown.ClearOptions();
            _traitDropdown.AddOptions(names);
            _traitDropdown.value = 0;
            _traitDropdown.RefreshShownValue();
        }
    }

    private void RefreshPrefabDropdown()
    {
        if (_prefabDropdown == null) return;

        _racePrefabs.Clear();
        var names = new List<string>();

        if (_raceDropdown != null && _races.Count > 0)
        {
            var race = _races[Mathf.Clamp(_raceDropdown.value, 0, _races.Count - 1)];
            foreach (var prefab in race.character_prefabs)
            {
                if (prefab != null)
                {
                    _racePrefabs.Add(prefab);
                    names.Add(prefab.name);
                }
            }
        }

        _prefabDropdown.ClearOptions();
        _prefabDropdown.AddOptions(names);
        _prefabDropdown.value = 0;
        _prefabDropdown.RefreshShownValue();
    }

    // ─── Listener wiring ─────────────────────────────────────────────

    private void WireListeners()
    {
        if (_raceDropdown != null) _raceDropdown.onValueChanged.AddListener(HandleRaceChanged);
        if (_addCombatStyleButton != null) _addCombatStyleButton.onClick.AddListener(AddCombatRow);
        if (_addSkillButton != null) _addSkillButton.onClick.AddListener(AddSkillRow);
        if (_armedToggle != null) _armedToggle.onValueChanged.AddListener(HandleArmedChanged);

        if (DevModeManager.Instance != null)
        {
            DevModeManager.Instance.OnDevModeChanged += HandleDevModeChanged;
        }
    }

    private void UnwireListeners()
    {
        if (_raceDropdown != null) _raceDropdown.onValueChanged.RemoveListener(HandleRaceChanged);
        if (_addCombatStyleButton != null) _addCombatStyleButton.onClick.RemoveListener(AddCombatRow);
        if (_addSkillButton != null) _addSkillButton.onClick.RemoveListener(AddSkillRow);
        if (_armedToggle != null) _armedToggle.onValueChanged.RemoveListener(HandleArmedChanged);

        if (DevModeManager.Instance != null)
        {
            DevModeManager.Instance.OnDevModeChanged -= HandleDevModeChanged;
        }

        foreach (var row in _combatRows) if (row != null) row.OnRemoveClicked -= HandleCombatRowRemove;
        foreach (var row in _skillRows) if (row != null) row.OnRemoveClicked -= HandleSkillRowRemove;
    }

    private void HandleRaceChanged(int _) => RefreshPrefabDropdown();

    private void HandleDevModeChanged(bool isEnabled)
    {
        if (!isEnabled && _armedToggle != null && _armedToggle.isOn)
        {
            _armedToggle.isOn = false;
        }
    }

    // ─── Row management ───────────────────────────────────────────────

    private void AddCombatRow()
    {
        if (_rowPrefab == null || _combatStylesContainer == null) return;
        var row = Instantiate(_rowPrefab, _combatStylesContainer);
        var names = new List<string>();
        foreach (var s in _combatStyles) names.Add(s.StyleName);
        row.Populate(names, defaultLevel: 1);
        row.OnRemoveClicked += HandleCombatRowRemove;
        _combatRows.Add(row);
    }

    private void HandleCombatRowRemove(DevSpawnRow row)
    {
        _combatRows.Remove(row);
        row.OnRemoveClicked -= HandleCombatRowRemove;
        Destroy(row.gameObject);
    }

    private void AddSkillRow()
    {
        if (_rowPrefab == null || _skillsContainer == null) return;
        var row = Instantiate(_rowPrefab, _skillsContainer);
        var names = new List<string>();
        foreach (var s in _skills) names.Add(s.SkillName);
        row.Populate(names, defaultLevel: 1);
        row.OnRemoveClicked += HandleSkillRowRemove;
        _skillRows.Add(row);
    }

    private void HandleSkillRowRemove(DevSpawnRow row)
    {
        _skillRows.Remove(row);
        row.OnRemoveClicked -= HandleSkillRowRemove;
        Destroy(row.gameObject);
    }

    // ─── Click-to-spawn ──────────────────────────────────────────────

    private void SetupEnvironmentLayerMask()
    {
        int envLayer = LayerMask.NameToLayer("Environment");
        if (envLayer < 0)
        {
            Debug.LogWarning("<color=orange>[DevSpawn]</color> 'Environment' layer not found — falling back to all layers.");
            _environmentLayerMask = ENVIRONMENT_LAYER_MASK_FALLBACK;
        }
        else
        {
            _environmentLayerMask = 1 << envLayer;
        }
    }

    private void HandleArmedChanged(bool armed)
    {
        Debug.Log($"<color=cyan>[DevSpawn]</color> Armed: {armed}");
    }

    private void Update()
    {
        if (DevModeManager.Instance == null || !DevModeManager.Instance.IsEnabled) return;
        if (_armedToggle == null || !_armedToggle.isOn) return;

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            _armedToggle.isOn = false;
            return;
        }

        if (!Input.GetMouseButtonDown(0)) return;

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            return;
        }

        Camera cam = Camera.main;
        if (cam == null)
        {
            Debug.LogWarning("<color=orange>[DevSpawn]</color> Camera.main is null — cannot spawn.");
            return;
        }

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, 500f, _environmentLayerMask))
        {
            Debug.LogWarning("<color=orange>[DevSpawn]</color> Ray missed the Environment layer.");
            return;
        }

        SpawnAt(hit.point);
    }

    private void SpawnAt(Vector3 anchor)
    {
        if (_races.Count == 0 || _racePrefabs.Count == 0)
        {
            Debug.LogError("<color=red>[DevSpawn]</color> No race or prefab available.");
            return;
        }

        RaceSO race = _races[Mathf.Clamp(_raceDropdown.value, 0, _races.Count - 1)];
        GameObject prefab = _racePrefabs[Mathf.Clamp(_prefabDropdown.value, 0, _racePrefabs.Count - 1)];

        int n = 1;
        if (_countField != null && int.TryParse(_countField.text, out int parsed)) n = Mathf.Max(1, parsed);

        float radius = 4f * Mathf.Sqrt(n);

        for (int i = 0; i < n; i++)
        {
            Vector3 pos = anchor;
            if (n > 1)
            {
                Vector2 offset = Random.insideUnitCircle * radius;
                pos += new Vector3(offset.x, 0f, offset.y);
            }

            CharacterPersonalitySO personality = ResolvePersonality();
            CharacterBehavioralTraitsSO trait = ResolveTrait();
            List<(CombatStyleSO, int)> combatList = BuildCombatList();
            List<(SkillSO, int)> skillList = BuildSkillList();

            var character = SpawnManager.Instance.SpawnCharacter(
                pos: pos,
                race: race,
                visualPrefab: prefab,
                personality: personality,
                traits: trait,
                combatStyles: combatList,
                skills: skillList
            );

            if (character == null)
            {
                Debug.LogError($"<color=red>[DevSpawn]</color> Spawn {i} returned null.");
            }
        }

        Debug.Log($"<color=green>[DevSpawn]</color> Spawned {n} NPC(s) near {anchor} (radius {radius:F2}u).");
    }

    private CharacterPersonalitySO ResolvePersonality()
    {
        if (_personalityDropdown == null || _personalities.Count == 0) return null;
        int v = _personalityDropdown.value;
        if (v == 0) return null; // Random — SpawnManager handles it
        int idx = v - 1;
        return (idx >= 0 && idx < _personalities.Count) ? _personalities[idx] : null;
    }

    private CharacterBehavioralTraitsSO ResolveTrait()
    {
        if (_traitDropdown == null || _traits.Count == 0) return null;
        int v = _traitDropdown.value;
        if (v == 0) return null;
        int idx = v - 1;
        return (idx >= 0 && idx < _traits.Count) ? _traits[idx] : null;
    }

    private List<(CombatStyleSO, int)> BuildCombatList()
    {
        if (_combatRows.Count == 0) return null;
        var list = new List<(CombatStyleSO, int)>();
        foreach (var row in _combatRows)
        {
            if (row == null) continue;
            int i = row.SelectedIndex;
            if (i < 0 || i >= _combatStyles.Count) continue;
            list.Add((_combatStyles[i], row.Level));
        }
        return list.Count > 0 ? list : null;
    }

    private List<(SkillSO, int)> BuildSkillList()
    {
        if (_skillRows.Count == 0) return null;
        var list = new List<(SkillSO, int)>();
        foreach (var row in _skillRows)
        {
            if (row == null) continue;
            int i = row.SelectedIndex;
            if (i < 0 || i >= _skills.Count) continue;
            list.Add((_skills[i], row.Level));
        }
        return list.Count > 0 ? list : null;
    }
}
```

- [ ] **Step 12.2: Compile check**

Unity should compile. All references to `SpawnManager.SpawnCharacter` match the new signature from Task 2. Scripts `DevSpawnRow`, `DevModeManager` are both present.

- [ ] **Step 12.3: Commit**

```bash
git add Assets/Scripts/Debug/DevMode/Modules/DevSpawnModule.cs
git commit -m "feat(devmode): add DevSpawnModule

Spawn tab: race/prefab/personality/trait dropdowns, multi-entry combat
styles + skills rows, count field, armed toggle, click-to-spawn against
Environment layer with scatter radius 4*sqrt(N) per world scale."
```

---

### Task 13: Build `DevModePanel.prefab` and `DevSpawnRow.prefab`

**Files:**
- Create (authored in Unity): `Assets/Resources/UI/DevModePanel.prefab`
- Create (authored in Unity): `Assets/Resources/UI/DevSpawnRow.prefab`

This step is 100% Unity Editor authoring. Use MCP tools to make it reproducible.

- [ ] **Step 13.1: Ensure target folder**

Use `assets-create-folder` on `Assets/Resources/UI/`. The existing project already has `Assets/Resources/Data/` etc., so `Resources/` exists; only `UI/` may be new.

- [ ] **Step 13.2: Author `DevSpawnRow.prefab`**

Build a HUD-style row:

- Root: `GameObject` (name: `DevSpawnRow`) with:
  - `RectTransform` — width 500, height 40, anchor stretch-horizontal.
  - `HorizontalLayoutGroup` — padding 4, spacing 8, childForceExpandWidth off.
  - `DevSpawnRow` component.
- Child `Dropdown` (TMP_Dropdown) — preferred width 260. Assign to `_dropdown` field.
- Child `InputField - TextMeshPro` — preferred width 80, ContentType: IntegerNumber. Placeholder "Level". Assign to `_levelField`.
- Child `Button` — label "X", preferred width 40. Assign to `_removeButton`.

Save as `Assets/Resources/UI/DevSpawnRow.prefab`.

Use `assets-prefab-create` MCP tool. If manual authoring is needed (compound prefab), document each component add via `gameobject-component-add` + `gameobject-component-modify`.

- [ ] **Step 13.3: Author `DevModePanel.prefab`**

Root structure:

- Root `DevModePanel` (RectTransform + `Canvas` + `CanvasScaler` + `GraphicRaycaster` + `DevModePanel` script)
  - Canvas: Render Mode = Screen Space - Overlay, Sort Order = 500 (above normal HUD).
  - CanvasScaler: Scale With Screen Size, 1920×1080 reference, Match 0.5.
- Child `ContentRoot` (RectTransform, semi-opaque panel anchored top-left, width 600, height 800).
  - Assign to `_contentRoot` on `DevModePanel`.
  - Child `VerticalLayoutGroup` holding all controls in order per spec §6.1:
    - `Label: Race` + `TMP_Dropdown` → assign to `DevSpawnModule._raceDropdown`
    - `Label: Prefab` + `TMP_Dropdown` → `_prefabDropdown`
    - `Label: Personality` + `TMP_Dropdown` → `_personalityDropdown`
    - `Label: Trait` + `TMP_Dropdown` → `_traitDropdown`
    - `Header: Combat Styles`
    - `ScrollView` → content container → `_combatStylesContainer`
    - `Button: + Add Combat Style` → `_addCombatStyleButton`
    - `Header: Skills`
    - `ScrollView` → content container → `_skillsContainer`
    - `Button: + Add Skill` → `_addSkillButton`
    - `Label: Count` + `TMP_InputField (Integer)` → `_countField` (default text "1")
    - `Toggle: Armed: Click to spawn` → `_armedToggle`
- Add `DevSpawnModule` script to the ContentRoot or a dedicated child `SpawnTab`. Assign all `[SerializeField]` references, including `_rowPrefab` → drag the `DevSpawnRow.prefab` authored in 13.2.

Save as `Assets/Resources/UI/DevModePanel.prefab`.

- [ ] **Step 13.4: Play-mode verification**

Enter Play mode (as host). Press F3. Expected:
- Panel appears in top-left.
- Race, Prefab, Personality, Trait dropdowns populated (Personality/Trait show "Random" as first entry).
- Combat Styles + Skills lists empty, with `+ Add` buttons.
- Count field shows 1.
- Armed toggle off.

Click `+ Add Combat Style`. A row appears with 4 options (Barehands, Bow, Charging, Sword). Level 1.
Click X on the row. Row disappears.

Same for Skills (4 options: Basics, Leadership, Locksmith, Tailoring).

Arm the toggle. Click a floor tile in the world. NPC spawns. Console:

> `<color=green>[DevSpawn]</color> Spawned 1 NPC(s) near (x,y,z) (radius 4.00u).`

Set Count = 5. Click another spot. 5 NPCs scatter within ~9u.

Set Personality = Brave, add Combat Style = Bow L10, Skills = Leadership L5. Click. One NPC. Select it with TAB (after disabling dev mode to regain input) and inspect — Leadership L5 should be on its sheet.

Press ESC while armed — toggle disarms.

Press F3 — panel hides. All rows cleared? No — rows persist across toggles (intended; settings stick for the session).

- [ ] **Step 13.5: Commit prefabs**

```bash
git add Assets/Resources/UI/DevModePanel.prefab Assets/Resources/UI/DevSpawnRow.prefab Assets/Resources/UI/DevModePanel.prefab.meta Assets/Resources/UI/DevSpawnRow.prefab.meta
git commit -m "feat(devmode): add DevModePanel + DevSpawnRow prefabs

Screen-space overlay panel wired to DevSpawnModule with race/prefab/
personality/trait dropdowns, combat + skills row lists, count field,
and armed toggle."
```

---

## Phase 6 — Strip Legacy Character-Spawn UI

### Task 14: Remove character-spawn UI from `DebugScript.cs`

**Files:**
- Modify: `Assets/Scripts/DebugScript.cs` (lines 1-193)

- [ ] **Step 14.1: Remove fields and methods related to character spawn**

Delete:
- `[SerializeField] private TMP_Dropdown raceDropdown;`
- `[SerializeField] private TMP_Dropdown characterDefaultPrefab_dropdown;`
- `[SerializeField] private TMP_InputField spawnNumberInput;`
- `[SerializeField] private Toggle isPlayerToggle;`
- `[SerializeField] private Button spawnButton;`
- `private List<RaceSO> availableRaces ...`
- `private RaceSO selectedRace;`
- `private GameObject selectedCharacterDefaultPrefab;`
- Methods: `LoadRaces`, `OnRaceSelected`, `OnPrefabSelected`, `SpawnCharacters`.
- Listener wiring in `Start`: `raceDropdown.onValueChanged...`, `characterDefaultPrefab_dropdown.onValueChanged...`, `spawnButton.onClick.AddListener(SpawnCharacters);`
- The initial selection block inside `Start`.

Keep:
- `spawnItem`, `itemsSOList`, `testInstallFurnitureBtn`, `switchButton`, `debugPanel` wiring.
- Methods: `TogglePanel`, `LoadItems`, `OnSpawnItemClicked`, `TestInstallFurniture`.

- [ ] **Step 14.2: Clean-up unused usings**

Remove `System.Collections.Generic` if no longer needed (likely still needed for `availableItems`). Keep the rest.

- [ ] **Step 14.3: Unity scene hygiene**

Open the scene containing `DebugScript`. The Inspector will show "Missing (Game Object)" for the now-removed field references. Delete the orphan UI GameObjects (race dropdown, prefab dropdown, spawn-number input, isPlayer toggle, spawn button) to keep the scene clean — do not leave them disabled. The dev-mode panel is the long-term home for character spawn; leaving dead UI under `DebugScript` invites drift.

- [ ] **Step 14.4: Play-mode verification**

Enter Play mode. Old DebugScript panel still opens via its switch button. Spawn Item works. Test Install Furniture works. Character spawn controls are gone.

Press F3 / use chat. Dev-mode panel works independently.

- [ ] **Step 14.5: Commit**

```bash
git add Assets/Scripts/DebugScript.cs Assets/Scenes/GameScene.unity
git commit -m "refactor(debug): strip character-spawn UI from DebugScript

Character spawning moved to DevSpawnModule. DebugScript keeps item
spawn + furniture-placement test for a transitional slice."
```

---

## Phase 7 — Documentation & Agent Maintenance

### Task 15: Write `.agent/skills/dev-mode/SKILL.md`

**Files:**
- Create: `.agent/skills/dev-mode/SKILL.md`

- [ ] **Step 15.1: Draft the skill file**

Required by project rules 21 and 28. Structure per `.agent/skills/skill-creator/SKILL.md` template. Cover:

1. **Purpose** — host-only god-mode tool, first module is Spawn.
2. **Activation rules** — F3 (editor/dev), `/devmode on|off` (chat, host). `Lock()` vs `Disable()` distinction.
3. **Public API** — `DevModeManager.Instance` surface with method-by-method docs. `DevModeManager.SuppressPlayerInput` static read.
4. **Events** — `OnDevModeChanged(bool)` — who fires, who listens.
5. **Integration points** — `PlayerController` / `PlayerInteractionDetector` input gating; `UI_ChatBar` command routing; `SpawnManager` dev-config dictionary.
6. **Module registry pattern** — how to add a new tab: create a MonoBehaviour in `Debug/DevMode/Modules/`, wire `[SerializeField]` references, nest its root under `DevModePanel`'s ContentRoot. No code changes to `DevModePanel` needed unless exclusive tabs are introduced.
7. **Dependencies** — `SpawnManager`, `GameSessionManager`, `CharacterCombat`, `CharacterSkills`, `CharacterProfile`, `CharacterTraits`, `UI_ChatBar`.
8. **Known limitations** — list the six points from spec §12 verbatim.
9. **Extension notes** — follow-ups: freecam, pause, invulnerability, item grant, teleport, Assign-Job module, client request-ServerRpc path.

Location: `.agent/skills/dev-mode/SKILL.md` (not inside `Assets/`).

- [ ] **Step 15.2: Commit**

```bash
git add .agent/skills/dev-mode/SKILL.md
git commit -m "docs(skill): add dev-mode SKILL.md (rules 21, 28)"
```

---

### Task 16: Update `debug-tools-architect` agent definition

**Files:**
- Modify: `.claude/agents/debug-tools-architect.md`

- [ ] **Step 16.1: Read current agent file**

Read `.claude/agents/debug-tools-architect.md` fully.

- [ ] **Step 16.2: Append dev-mode architecture section**

Add a new section describing:
- `DevModeManager` as the single source of truth for the god-mode toggle.
- `DevModePanel` + module-per-tab pattern (with `DevSpawnModule` as the reference implementation).
- Chat-command path via `DevChatCommands.Handle`.
- `DevModeManager.SuppressPlayerInput` as the input gate contract.
- File locations.

Confirm `model: opus` frontmatter per project rule 29 & feedback_always_opus.md.

- [ ] **Step 16.3: Commit**

```bash
git add .claude/agents/debug-tools-architect.md
git commit -m "docs(agent): extend debug-tools-architect with dev-mode system

Documents DevModeManager, module-per-tab pattern, chat command path,
and input gating contract."
```

---

## Final Verification (Spec §10 Matrix)

After all tasks ship, run through each row of the spec's multiplayer validation matrix manually:

| # | Scenario | Command / Action | Expected |
|---|---|---|---|
| 1 | Host F3 (Editor) | Press F3 on host | Panel opens on host only |
| 2 | Host F3 (Release) | Press F3 before /devmode on | Nothing; console silent |
| 3 | Client F3 | Press F3 on client | Warning: "host-only" |
| 4 | Client /devmode on | Type in chat | Warning: "host-only" |
| 5 | Host /devmode on → /devmode off | Chat | Panel toggles; F3 still works after |
| 6 | Host spawn Brave NPC | UI click | Brave on host (client replication flagged) |
| 7 | Host spawn Leadership L5 | UI click | L5 visible on host AND client |
| 8 | Host spawn Bow L10 | UI click | L10 on host; save/reconnect on client |
| 9 | Host dev ON, client dev OFF | — | Client can still move normally |
| 10 | Count=10, Personality=Random | UI click | 10 NPCs in ~12.6u circle, mixed personalities |

Document results in the PR or follow-up commit message.

---

## Execution

Plan complete and saved to `docs/superpowers/plans/2026-04-20-dev-mode-god-tool.md`. Two execution options:

1. **Subagent-Driven (recommended)** — dispatch fresh subagent per task with checkpoint reviews.
2. **Inline Execution** — execute in this session with batch checkpoints.
