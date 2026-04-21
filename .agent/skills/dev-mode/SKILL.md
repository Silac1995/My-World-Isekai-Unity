---
name: dev-mode
description: Host-only god-mode developer tool. F3 toggle (editor/dev), /devmode on|off in chat (release). Modules: Spawn (click-to-spawn NPCs with full configuration), Select (click-to-select Characters + IDevAction plug-in; first action assigns building ownership).
---

# Dev Mode System

## 1. Purpose

Dev Mode is a host-only, god-mode developer tool that layers a togglable admin panel and input-gate over the normal gameplay loop. It exists so developers — and the host of a multiplayer session when testing — can spawn NPCs, poke at state, and iterate on content without restarting the session or hacking around player-controlled input. The very first slice delivers the toggle/gate infrastructure, the chat command surface, and a single module: **Dev Spawn** (click-to-spawn fully configured NPCs). Future modules (freecam, item grant, teleport, time-of-day, Assign Job) plug into the same registry. Dev Mode never ships to clients and is explicitly locked behind build flags + a chat command in release builds.

## 2. Activation Rules

| Build / Context | Unlocked at Awake? | F3 Toggle | `/devmode on\|off` | Notes |
|---|---|---|---|---|
| Unity Editor | Yes | Yes | Yes | Always unlocked for developer iteration |
| `DEVELOPMENT_BUILD` | Yes | Yes | Yes | Unlocked automatically via `#if UNITY_EDITOR \|\| DEVELOPMENT_BUILD` |
| Release build | No | No (locked) | Yes (unlocks + enables) | Host must explicitly type `/devmode on` once to unlock the session |
| Client (any build) | N/A | No | Logs "host-only" and no-ops | Dev Mode is server-authoritative and host-only |

**Unlock vs. Enable:**
- *Unlock* is a session-level gate. Once unlocked, the host can freely toggle.
- *Enable* is the live panel state. `/devmode off` calls `Disable()` (keeps session unlocked). `Lock()` is a full teardown that re-locks the session.

## 3. Public API

`DevModeManager.Instance` — singleton, host-only. Clients never see it do anything.

**Properties**

| Property | Type | Meaning |
|---|---|---|
| `IsUnlocked` | `bool` | Session gate. True in editor/dev builds or after `/devmode on` in release. |
| `IsEnabled` | `bool` | Current panel state. True while the dev panel is open and input is suppressed. |
| `SuppressPlayerInput` | `static bool` | Global read used by `PlayerController` and `PlayerInteractionDetector` to suppress gameplay action inputs. Mirrors `IsEnabled` on the active instance. WASD movement is still allowed (god-mode flying); only right-click move, TAB target, Space attack, and E interact are blocked. |
| `GodModeMovementSpeed` | `static const float` | WASD movement speed (units/second) used by `PlayerController` while dev mode is active. Default `17f`. Edit the constant to tune. |

**Events**

| Event | Signature | When it fires |
|---|---|---|
| `OnDevModeChanged` | `Action<bool isEnabled>` | Whenever `IsEnabled` flips. All dev-mode modules subscribe to react (show/hide panel, clear ghost visuals, etc.). |

**Methods**

| Method | Behavior |
|---|---|
| `Unlock()` | Sets `IsUnlocked = true`. Called automatically in editor/dev builds; called by `/devmode on` in release. |
| `Lock()` | Full teardown. Forces `IsEnabled = false`, fires `OnDevModeChanged(false)`, sets `IsUnlocked = false`. Used when the host explicitly wants to re-lock the session. |
| `TryEnable()` | No-op on clients. On host: requires `IsUnlocked`, otherwise logs a warning. Sets `IsEnabled = true` and fires event. |
| `Disable()` | Sets `IsEnabled = false`, fires event. Keeps session unlocked — this is what `/devmode off` uses so the host doesn't have to re-unlock. |
| `TryToggle()` | Flips `IsEnabled` via `TryEnable()` / `Disable()`. Bound to F3. |

## 4. Chat Commands

Entry point: `DevChatCommands.Handle(string rawInput)`. Called from `UI_ChatBar` whenever a chat message starts with `/`.

| Command | Effect |
|---|---|
| `/devmode on` | Host-only. Calls `Unlock()` then `TryEnable()`. On a client, logs a host-only warning and swallows. |
| `/devmode off` | Host-only. Calls `Disable()` (does NOT re-lock). |
| Unknown `/command` | Logs a warning (`Unknown dev command`) and swallows — never crashes, never passes through to chat. |

## 5. Input Gating Contract

While `DevModeManager.IsEnabled == true`, the two input-reading components selectively suppress gameplay actions but keep movement live:

- `PlayerController.Update()` — reads WASD into `_inputDir` as usual, then **skips the gameplay-action block** (right-click move, TAB target, combat-command auto-assignment, Space attack) when `SuppressPlayerInput` is true. `_isCrouching` is also forced false. The character keeps moving via WASD; `Move()` swaps the speed argument to `DevModeManager.GodModeMovementSpeed` so god-mode flying feels brisk.
- `PlayerInteractionDetector.Update()` — early-outs after the proximity refresh. Nearby-target tracking still updates (so the prompt restores instantly when you exit dev mode), but the E-key press path is blocked.

`SuppressPlayerInput` is a **static** on `DevModeManager` so hot-path Updates don't dereference the instance every frame.

## 6. Panel & Module Registry Pattern

`DevModePanel` is the panel root, loaded lazily from `Resources/UI/DevModePanel` on first enable. It owns a `ContentRoot` Transform under which each module GameObject lives as a child.

**Registration is self-service** — there is no central `RegisterModule(...)` API. Each module:
1. Is a `MonoBehaviour` on a child of `ContentRoot` in the `DevModePanel` prefab.
2. Subscribes to `DevModeManager.OnDevModeChanged` in its own `OnEnable` / `Start`.
3. Unsubscribes in `OnDisable` / `OnDestroy`.
4. Shows/hides its own UI in response to the event.

**To add a new module:**

1. Create a new `MonoBehaviour` in `Assets/Scripts/Debug/DevMode/Modules/`.
2. Subscribe to `DevModeManager.OnDevModeChanged` and implement show/hide.
3. In the `DevModePanel` prefab, add a child GameObject under `ContentRoot` with your script attached.
4. Wire `[SerializeField]` refs (dropdowns, buttons, etc.).
5. Unsubscribe cleanly in `OnDisable` / `OnDestroy`.

No edit to `DevModeManager` or `DevModePanel` is required to add a module.

### Click arbitration

`DevModeManager` exposes a single-slot click consumer: `ActiveClickConsumer` (MonoBehaviour), `OnClickConsumerChanged` (event), `SetClickConsumer(x)`, `ClearClickConsumer(x)`. Armed dev modules MUST claim the slot when arming and release when disarming, and MUST gate their click loop on `ActiveClickConsumer == this`. Subscribing to `OnClickConsumerChanged` lets a module auto-disarm when another claims the slot — so arming Select flips Spawn off, and vice versa.

## 7. Dev Spawn Module Details

`DevSpawnModule` — the first shipping module. Lets the host click anywhere on the `Environment` layer to spawn fully configured NPCs.

**Configuration UI**

| Field | Widget |
|---|---|
| Race | TMP Dropdown |
| Prefab | TMP Dropdown (filtered by race) |
| Personality | TMP Dropdown |
| Behavioral Trait | TMP Dropdown |
| Combat Styles | Multi-entry row list (`DevSpawnRow` per entry — combat style dropdown + level input) |
| Skills | Multi-entry row list (`DevSpawnRow` per entry — skill dropdown + level input) |
| Count | TMP InputField (integer, default 1) |
| Armed | Toggle |

**Click flow**

1. Host clicks on an `Environment`-layer collider. Ray is cast from the mouse.
2. For `N = count` characters: compute a scatter offset around the click point. **Scatter radius = `4 * sqrt(N)` Unity units** (per project rule 32, 11 units = 1.67 m, so ~0.6 m per unit of radius). Individual offsets are random within the disk.
3. For each character, `SpawnManager.SpawnCharacter(...)` is invoked with the configured race/prefab/personality/trait/armed flag.
4. Dev-mode extras (combat styles + levels, skills + levels) are passed to `SpawnManager` via a **server-only `Dictionary<ulong, PendingDevConfig>`** keyed on NetworkObjectId.
5. `SpawnManager.ApplyDevExtras(...)` fires post-spawn on the server, applying combat styles via `CharacterCombat.UnlockCombatStyle(style, level)` and skills via the existing `CharacterSkills` API.

**Why a pending-config dict?** `SpawnCharacter` is an async network spawn — we don't have the instance yet when we configure it. The dict is populated on the main thread before spawn and drained in the spawn callback by NetworkObjectId.

## Select Tab

Click-to-select for Characters + pluggable actions via the `IDevAction` interface.

### DevSelectionModule

Attached to the SelectTab GameObject in `DevModePanel.prefab`. Public API:

- `Character SelectedCharacter { get; }` — the currently selected Character, or null.
- `event Action OnSelectionChanged` — fires on any change (including to/from null).
- `void SetSelectedCharacter(Character c)` — replaces the selection.
- `void ClearSelection()` — sets selection to null.

Selection is cleared automatically on `SceneManager.sceneUnloaded` and on `DevModeManager.OnDevModeChanged(false)` — prevents stale references.

Click flow: armed toggle claims the click slot; next click raycasts against the `RigidBody` layer (exposed as `[SerializeField] LayerMask _characterLayerMask`, resolved to `GetMask("RigidBody")` at `Start` if left at 0), then applies a `GetComponentInParent<Character>()` filter. The layer scopes the pick to the character's body collider so awareness/interaction zones can't produce false hits. Accepts any `Character` — player or NPC, local or remote-replicated.

### IDevAction

Plug-in interface for Select-tab actions. Each action is a MonoBehaviour parented under the Select tab's ActionsContainer.

```csharp
public interface IDevAction
{
    string Label { get; }
    bool IsAvailable(DevSelectionModule sel);
    void Execute(DevSelectionModule sel);
}
```

Action recipe:
1. Create `MyDevAction : MonoBehaviour, IDevAction` under `Assets/Scripts/Debug/DevMode/Modules/Actions/`.
2. Hold `[SerializeField] DevSelectionModule _selection; [SerializeField] Button _button; [SerializeField] TMP_Text _buttonLabel;`.
3. In `Start`, wire the button click to `OnButtonClicked` and subscribe to `_selection.OnSelectionChanged` to refresh the button's interactable state via `IsAvailable`.
4. If the action needs to consume additional clicks (e.g., pick a second target), use `DevModeManager.SetClickConsumer(this)` while armed and the standard click-loop pattern.
5. Add the action GameObject as a child of the SelectTab's `ActionsContainer` in the prefab.

### DevActionAssignBuilding (first action)

Enabled when a Character is selected. On `Execute`, claims the click slot and waits for the next `LayerMask.GetMask("Building")` hit. Dispatches polymorphically:

- `CommercialBuilding` → `SetOwner(character, null)` (makes character the boss).
- `ResidentialBuilding` → `SetOwner(character)` (sets primary owner; character also becomes resident).

ESC cancels. Another module claiming the click slot also cancels. Selection is preserved after success so further actions can chain.

## 8. Integration Points

Files touched by the dev-mode slice:

| File | Role |
|---|---|
| `Assets/Scripts/Debug/DevMode/DevModeManager.cs` | Singleton — state, events, input gate read |
| `Assets/Scripts/Debug/DevMode/DevModePanel.cs` | Panel prefab root, lazy-loads from `Resources/UI/DevModePanel` |
| `Assets/Scripts/Debug/DevMode/DevChatCommands.cs` | `/devmode on\|off` parser; single `Handle(rawInput)` entry point |
| `Assets/Scripts/Debug/DevMode/Modules/DevSpawnModule.cs` | Click-to-spawn module UI + click handler |
| `Assets/Scripts/Debug/DevMode/Modules/DevSpawnRow.cs` | Multi-entry row (combat style or skill + level) |
| `Assets/Resources/UI/DevModePanel.prefab` | Panel prefab with `ContentRoot` and modules as children |
| `Assets/Resources/UI/DevSpawnRow.prefab` | Reusable row prefab for combat style / skill lists |
| `Assets/Scripts/SpawnManager.cs` | Extended `SpawnCharacter` signature + `PendingDevConfig` dict + `ApplyDevExtras` server path |
| `Assets/Scripts/Character/CharacterCombat/CharacterCombat.cs` | `UnlockCombatStyle(style, level)` overload used by dev mode |
| `Assets/Scripts/UI/UI_ChatBar.cs` | Routes `/`-prefixed messages to `DevChatCommands.Handle` |
| `Assets/Scripts/Character/CharacterControllers/PlayerController.cs` | Input gate — checks `SuppressPlayerInput` |
| `Assets/Scripts/Character/PlayerInteractionDetector.cs` | Input gate — early-out on `SuppressPlayerInput` |

## 9. Known Limitations

Explicit list of what this first slice does **not** cover. These are follow-up work, not bugs.

- **Personality IS applied server-side** on both networked and offline spawns (fixed post-review — `PendingDevConfig.Personality` now carries the dev-picked value into `InitializeSpawnedCharacter`). **Behavioral trait replication** to late-joining / non-host clients is still a known gap — the server applies the value on the spawned Character, but no dedicated `NetworkVariable` replicates it across all clients. A late-joiner sees defaults until the next full save round-trip. Follow-up slice.
- **Combat style live sync** — only the server applies styles via `UnlockCombatStyle(style, level)`. Reconnecting clients rebuild state from save data. A live `NetworkList<>`-based equivalent for combat styles is future work.
- **Skills do replicate** — `CharacterSkills` already owns `NetworkList<NetworkSkillSyncData>`, so skill levels granted by dev-mode propagate correctly to all clients. No additional work needed.
- **Jobs are deliberately excluded** from this slice. Assigning a job requires a `CommercialBuilding` workplace; it will ship as its own "Assign Job" module that lets the host target a building after spawning.
- **Freecam, sim-pause, invulnerability, item grant, teleport** — all future modules. None of them exist yet.
- **Client dev-mode** — out of scope. Dev Mode is host-only today. Giving clients any of this power needs a separate design pass for trust and replication.
- **Panel has no ScrollView** — plain `Transform` containers. Long combat-style / skill lists will overflow vertically. A scroll pass is deferred.
- **Prefab's TMP Dropdowns and InputFields use minimal default visuals** — no custom arrow sprite, no custom checkmark. Visual polish is deferred; functionality is the priority for this slice.
- **No visual selection indicator** — the selected character is shown by label only in the panel. Follow-up slice can add a world-space outline (shader-based per rule 25) or a UI marker.
- **No undo on ownership assignment** — the new owner replaces the previous via `SetOwner`'s existing semantics. No confirmation dialog.
- **Character-first flow only** — "click building first, then assign a character to it" is deferred.
- **No multi-character selection** — one at a time.
- **No exclude-self filter** — clicking on the host's own character selects it. Add a toggle if this becomes annoying.
- **Worker/resident/job actions** — deferred. Assign Building sets ownership only.
- **Item selection + actions** — not in this slice.

## 10. Extension Notes (Follow-Up Modules)

Planned modules, each to be added as a new child GameObject under `DevModePanel.ContentRoot` with its script subscribing to `OnDevModeChanged`:

| Module | Notes |
|---|---|
| Freecam | Detach camera from player, WASD + mouse look, speed slider. Must not interact with `Time.timeScale`. |
| Sim-pause | Toggles `GameSpeedController` to 0 without breaking UI (project rule 26: UI uses unscaled time). |
| NPC selection / edit | Click existing NPC -> panel showing needs, stats, inventory; edit live. |
| Item grant | Dropdown + count -> add directly to target inventory via `CharacterAction`. |
| Teleport | Click on map or enter coords -> teleport selected character. |
| Time-of-day slider | Drives `TimeManager.CurrentTime01` directly. |
| Assign Job | Select existing NPC, then click `CommercialBuilding` to assign. |

All of them follow the same contract: one MonoBehaviour per tab, self-register by subscribing to `DevModeManager.OnDevModeChanged`, unsubscribe in `OnDisable`/`OnDestroy`, no edits to `DevModeManager` or `DevModePanel` required.
