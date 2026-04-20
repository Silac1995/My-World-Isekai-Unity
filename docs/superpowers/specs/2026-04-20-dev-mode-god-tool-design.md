# Dev Mode (God Tool) — Design

**Date:** 2026-04-20
**Status:** Draft — awaiting implementation plan
**Scope:** First slice of a Minecraft-Creative-style developer tool. Delivers a mode shell (toggle, input gate, panel) and a single populated module (Spawn).

---

## 1. Goals

- Provide a host-only developer tool that can be toggled on and off at runtime.
- First populated module: **Spawn** — click a point in the world to drop a fully configurable NPC (race, prefab, personality, behavioral traits, combat styles, skills).
- Establish an extensible shell so future slices (item grant, teleport, time slider, freecam, invulnerability, pause, NPC editor) plug in as additional modules without reworking the core.
- Keep the feature completely stripped or locked behind a chat command in shipping builds.

## 2. Non-Goals (for this slice)

- Free camera.
- Simulation pause / time slider.
- Ghost preview marker or cursor sprite swap while armed.
- Select-NPC → edit workflows.
- Client dev-mode authority (clients can request but the current slice locks to host only).
- Job assignment at spawn (jobs require a `CommercialBuilding` workplace — deferred to a dedicated "Assign Job" module that can pair a job type with a workplace picker).

## 3. Activation Rules

| Context | Default state | How to enter |
|---|---|---|
| Unity Editor | Unlocked | Press **F3** |
| `DEVELOPMENT_BUILD` | Unlocked | Press **F3** |
| Release build | **Locked** | Type `/devmode on` in `UI_ChatBar` (host only). After the first unlock in a session, F3 also works. `/devmode off` disables but does not relock. |
| Client (any build) | N/A | F3 and `/devmode on` both log "Dev mode is host-only" and do nothing. |

## 4. Architecture

### 4.1 Components

```
Assets/Scripts/Debug/DevMode/
  DevModeManager.cs           Singleton. Input gate, state, event bus. DontDestroyOnLoad.
  DevModePanel.cs             Root panel script. Tab registry.
  DevChatCommands.cs          Static. Parses /-prefixed input from UI_ChatBar.
  Modules/
    DevSpawnModule.cs         Spawn tab logic (this slice).
    DevSpawnRow.cs            Reusable "row" component: [Dropdown][Level int][X].

Assets/Resources/UI/
  DevModePanel.prefab         Loaded lazily by DevModeManager on first enable.

.agent/skills/dev-mode/
  SKILL.md                    Per project rules 21 & 28.
```

### 4.2 Cross-system communication

- `DevModePanel` and each `DevSpawn*` module subscribe to `DevModeManager.OnDevModeChanged`.
- Modules never call each other directly. All shared state lives on `DevModeManager`.
- `PlayerController` and `PlayerInteractionDetector` read `DevModeManager.SuppressPlayerInput` (static passthrough of `IsEnabled`) and early-out when `true`.
- `UI_ChatBar.OnSubmitChat` routes `/`-prefixed lines to `DevChatCommands.Handle` before falling through to `CharacterSpeech.Say`.

### 4.3 Scene bootstrapping

`DevModeManager` is added as a component on the same GameObject as `SpawnManager`. Both share a host-only, `DontDestroyOnLoad` lifecycle, so this avoids an extra prefab and keeps the debug infrastructure co-located.

## 5. `DevModeManager` API

```csharp
public class DevModeManager : MonoBehaviour
{
    public static DevModeManager Instance { get; private set; }

    public bool IsUnlocked { get; private set; }       // gate for F3 / commands
    public bool IsEnabled  { get; private set; }       // currently on/off
    public static bool SuppressPlayerInput => Instance != null && Instance.IsEnabled;

    public event Action<bool> OnDevModeChanged;        // (isEnabled)

    public void Unlock();                              // arms the feature (sets IsUnlocked = true)
    public void Lock();                                // fully relocks: disables AND clears IsUnlocked. Used only by explicit teardown (e.g. scene unload or an explicit /devmode lock if added later). `/devmode off` uses Disable() instead, so the user doesn't need to retype /devmode on.
    public bool TryEnable();                           // requires IsUnlocked && IsHost
    public void Disable();                             // hides panel, turns IsEnabled = false, preserves IsUnlocked
    public bool TryToggle();
}
```

`Awake` seeds `IsUnlocked`:
- `#if UNITY_EDITOR || DEVELOPMENT_BUILD` → `IsUnlocked = true`
- Release builds → `IsUnlocked = false`

`Update` polls `Input.GetKeyDown(KeyCode.F3)` and calls `TryToggle()`.

Panel prefab is loaded from `Resources/UI/DevModePanel` on first `TryEnable`, then reused.

## 6. Spawn Module

### 6.1 UI layout

```
DevModePanel
└── Tab: Spawn
    ├── [Race dropdown]
    ├── [Character prefab dropdown]
    ├── [Personality dropdown]         ("Random" + all CharacterPersonalitySO)
    ├── [Behavioral Traits dropdown]   ("Random" + all CharacterBehavioralTraitsSO)
    │
    ├── Combat Styles
    │   └── rows: [Style dropdown][Level int][X]
    │   └── [ + Add Combat Style ]
    │
    ├── Skills
    │   └── rows: [Skill dropdown][Level int][X]
    │   └── [ + Add Skill ]
    │
    └── [Count __] [Armed: Click to spawn ☐]
```

### 6.2 Data sources

| Dropdown | Source |
|---|---|
| Race | `GameSessionManager.Instance.AvailableRaces` (current DebugScript pattern) |
| Prefab | `selectedRace.character_prefabs` |
| Personality | `Resources.LoadAll<CharacterPersonalitySO>("Data/Personnality")` |
| Behavioral Traits | `Resources.LoadAll<CharacterBehavioralTraitsSO>("Data/Behavioural Traits")` |
| Combat Style | `Resources.LoadAll<CombatStyleSO>("Data/CombatStyle")` |
| Skill | `Resources.LoadAll<SkillSO>("Data/Skills")` |

### 6.3 Click-to-spawn flow

When the **Armed** toggle is on, `DevSpawnModule.Update()`:

1. If `Mouse0` not pressed this frame → return.
2. If `EventSystem.current.IsPointerOverGameObject()` → return (click landed on the dev panel).
3. Build ray: `Camera.main.ScreenPointToRay(Input.mousePosition)`.
4. `Physics.Raycast(ray, out hit, 500f, LayerMask.GetMask("Environment"))`. No hit → `Debug.LogWarning` and return.
5. Let `N = Mathf.Max(1, Count.value)`; `radius = 4f * Mathf.Sqrt(N)` Unity units (see §6.4 for rule-32 reasoning).
6. For each of N spawns:
   - `anchor = hit.point`
   - If N > 1: add random XZ offset within `radius`.
   - Resolve "Random" dropdown values per-spawn (so 5 spawns with Personality=Random yield 5 rolls).
   - Call `SpawnManager.SpawnCharacter(anchor, race, prefab, personality, traits, combatStyles, skills)`.

`Armed` stays on after a spawn (sticky). **ESC** or clicking the toggle again disarms. Disabling dev mode also disarms.

### 6.4 Count & scatter

- N=1 → spawn at exact `hit.point`.
- N>1 → scatter on XZ within `radius = 4f * Mathf.Sqrt(N)` Unity units. Per project rule 32 (11 units = 1.67 m), this gives ≈ 61 cm for N=1, ≈ 1.9 m for N=10, ≈ 6.1 m for N=100. Roughly constant density appropriate for humanoid NPC footprint.

## 7. `SpawnManager` Changes

### 7.1 Remove `isPlayer`

Per user request: dev-mode always spawns NPCs. All `isPlayer` parameters and branches in `SpawnCharacter`, `SetupInteractionDetector`, and `InitializeSpawnedCharacter` are removed. Player spawn remains the responsibility of the normal session flow (not via `SpawnManager.SpawnCharacter` with `isPlayer=true`).

### 7.2 Extended `SpawnCharacter` signature

```csharp
public Character SpawnCharacter(
    Vector3 pos,
    RaceSO race,
    GameObject visualPrefab,
    CharacterPersonalitySO personality = null,
    CharacterBehavioralTraitsSO traits = null,
    List<(CombatStyleSO style, int level)> combatStyles = null,
    List<(SkillSO skill, int level)> skills = null);
```

`null` on any optional argument preserves the current random/default behavior. `InitializeSpawnedCharacter` is updated to receive and apply these.

### 7.3 `CharacterCombat.AddCombatStyle(CombatStyleSO, int level)`

New overload that constructs `new CombatStyleExpertise(style, level, 0f)` using the existing save-restore constructor and appends to `_knownStyles` (dedup on style SO, as today).

`CharacterSkills.AddSkill(SkillSO, int startingLevel)` already exists — no change needed.

## 8. Chat Command Integration

### 8.1 `UI_ChatBar.OnSubmitChat` change

Before the existing speech path:

```csharp
string trimmed = text?.Trim();
if (!string.IsNullOrEmpty(trimmed) && trimmed.StartsWith("/"))
{
    DevChatCommands.Handle(trimmed);
    // clear + deactivate input as today
    return;
}
// fall through to CharacterSpeech.Say
```

### 8.2 `DevChatCommands.Handle`

```csharp
public static class DevChatCommands
{
    public static void Handle(string rawInput);
}
```

- Splits on whitespace. First token (minus the leading `/`) is the command.
- `devmode` → requires host authority. On client → `Debug.LogWarning("Dev mode is host-only")`.
  - `devmode on` → `DevModeManager.Instance.Unlock()` then `TryEnable()`.
  - `devmode off` → `DevModeManager.Instance.Disable()`.
  - No args / unknown args → print usage.
- Unknown command → `Debug.LogWarning` and swallow (do not broadcast as speech).

## 9. Input Gating

While `DevModeManager.IsEnabled` is true on the host:

- `PlayerController.Update()` early-outs on mouse reads.
- `PlayerInteractionDetector` early-outs on mouse reads.
- Player WASD is also suppressed (acceptable — god mode implies the player stops controlling the character).

Implementation: both files add `if (DevModeManager.SuppressPlayerInput) return;` at the top of each input-reading method.

**Click ordering** — `DevSpawnModule.Update()` and `PlayerController.Update()` both read `Input.GetMouseButtonDown(0)` in the same frame. Because both `PlayerController` and `PlayerInteractionDetector` early-out on `SuppressPlayerInput`, there is no race: when dev mode is on, only `DevSpawnModule` responds to the click. No explicit execution-order requirement is needed. The `IsPointerOverGameObject()` check in §6.3 additionally prevents UI clicks from spawning; UI click events propagate through Unity's EventSystem which is independent of the MonoBehaviour Update loop and is unaffected by the gate.

## 10. Multiplayer Validation Matrix

| Scenario | Expected |
|---|---|
| Host presses F3 (editor/dev) | Panel opens on host only. |
| Host presses F3 (release) | Ignored until `/devmode on` has been typed once. |
| Client presses F3 | No-op. Log "Dev mode is host-only" at most. |
| Client types `/devmode on` | Log "host-only"; nothing else. |
| Host types `/devmode on` then `/devmode off` | Panel toggles. Remains unlocked for the session (F3 works afterward). |
| Host spawns NPC with Personality=Brave | Brave applied server-side. **Client replication is a known pre-existing gap** (§12). |
| Host spawns NPC with Skill Leadership L5 | NPC exists on host; Leadership L5 replicates via the existing `NetworkList` in `CharacterSkills`. |
| Host spawns NPC with Combat Style Bow L10 | Applied server-side. Replication to clients happens through the save/load pathway on reconnect; live sync is a follow-up. |
| Host dev mode ON, client dev mode OFF | Host's player input is suppressed for the host only; client controls normally. |
| Host spawns with Count=10, Personality=Random | 10 NPCs scattered within `radius = 4 * sqrt(10) ≈ 12.6` Unity units (≈ 1.9 m) of `hit.point`. Personality rolled per NPC. |

## 11. File Plan

### 11.1 New files

- `Assets/Scripts/Debug/DevMode/DevModeManager.cs`
- `Assets/Scripts/Debug/DevMode/DevModePanel.cs`
- `Assets/Scripts/Debug/DevMode/DevChatCommands.cs`
- `Assets/Scripts/Debug/DevMode/Modules/DevSpawnModule.cs`
- `Assets/Scripts/Debug/DevMode/Modules/DevSpawnRow.cs`
- `Assets/Resources/UI/DevModePanel.prefab`
- `.agent/skills/dev-mode/SKILL.md`

### 11.2 Modified files

- `Assets/Scripts/SpawnManager.cs` — drop `isPlayer`, extend `SpawnCharacter` signature, thread new params into `InitializeSpawnedCharacter`.
- `Assets/Scripts/Character/CharacterCombat/CharacterCombat.cs` — add `AddCombatStyle(CombatStyleSO, int level)` overload.
- `Assets/Scripts/UI/UI_ChatBar.cs` — route `/`-prefixed lines to `DevChatCommands.Handle`.
- `Assets/Scripts/DebugScript.cs` — remove character-spawn UI wiring; keep item/furniture buttons (folded into dev-mode tabs in a later slice).
- `Assets/Scripts/Character/CharacterControllers/PlayerController.cs` — input gating.
- `Assets/Scripts/Character/PlayerInteractionDetector.cs` — input gating.

## 12. Known Limitations

Documented explicitly in `.agent/skills/dev-mode/SKILL.md`:

1. **Personality and Behavioral Traits replication.** Both are currently set server-side with no dedicated `NetworkVariable`. Late-joining clients or clients that spawn the dev-built NPC after the fact may not see the custom personality/trait. Follow-up slice should add a small `NetworkVariable<FixedString64Bytes>` for each, resolving on the client via `Resources.LoadAll`.
2. **Combat style live sync.** `CharacterCombat` only serializes styles via save data; there is no `NetworkList` equivalent like `CharacterSkills` has. Dev-spawned combat styles are visible on host, and rebuild correctly from save on reconnect, but live client sync is deferred.
3. **Skills** already replicate via the existing `NetworkList<NetworkSkillSyncData>` — no gap.
4. **Jobs** are out of scope (require workplace picker) — deferred to a future "Assign Job" module.
5. **Freecam, pause, invulnerability, item grant, teleport** are out of scope — future modules.
6. **Client dev mode** is out of scope — all dev actions are host-only for this slice.

## 13. Pre-Implementation Checklist

Planning phase must verify these before coding begins:

1. **`SpawnCharacter(..., isPlayer: true)` call-site audit.** Grep the codebase for every call passing `isPlayer: true` and document a migration target for each. If any call site genuinely depends on spawn-as-player semantics, either keep a separate `SpawnPlayerCharacter` method or route it through the normal session flow. No silent drops.
2. **"Environment" layer coverage.** Confirm every valid spawn surface (terrain, building floors, interior walkable, bridges) is actually on the `Environment` layer. If gaps exist, surface the layer mask as a `[SerializeField]` on `DevSpawnModule` (default = `Environment`) so future adjustments don't require recompile.
3. **Prefab wiring.** Decide whether the `DevModePanel` prefab is authored fresh or migrated from the existing `DebugScript` panel. Fresh is cleaner; migration preserves existing item/furniture buttons for the transitional slice.

## 14. Post-Implementation Tasks

Per project rules:

- **Rule 21 / 28:** create `.agent/skills/dev-mode/SKILL.md` with purpose, API, activation rules, module registry, integration points, limitations, and extension instructions.
- **Rule 29:** update `.claude/agents/debug-tools-architect.md` with the dev-mode architecture (new modules follow the registry pattern on `DevModePanel`, SpawnModule is the reference implementation). No new agent — this falls within `debug-tools-architect`'s domain.
- **Rule 18:** run the network validation checklist from `NETWORK_ARCHITECTURE.md` against the matrix in §10.
