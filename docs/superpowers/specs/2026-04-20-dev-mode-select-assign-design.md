# Dev Mode — Select & Assign-Building Design

**Date:** 2026-04-20
**Status:** Draft — awaiting implementation plan
**Depends on:** [2026-04-20 dev-mode god tool](2026-04-20-dev-mode-god-tool-design.md) (already shipped)
**Scope:** Second slice of the dev-mode god tool. Adds a Select tab with click-to-select for characters and a pluggable action system. First action: assign a building as owner to the selected character.

---

## 1. Goals

- Extend the dev-mode panel with a second tab (**Select**) that owns cross-cutting selection state and dispatches context-sensitive actions.
- First concrete action: with a character selected, click a building to make that character the owner (`SetOwner`). Works on both `CommercialBuilding` and `ResidentialBuilding`.
- Establish an `IDevAction` pattern so future actions (worker assignment, resident assignment, teleport, set needs, despawn, etc.) plug in without modifying the Select module.
- Introduce a minimal click-arbitration mechanism on `DevModeManager` so only one module consumes a given click — future modules scale without cross-module coupling.

## 2. Non-Goals (for this slice)

- Visual selection indicator on the selected character or picked building (outline shader, UI marker). Label-only.
- Worker-assignment (`AssignWorker(character, job)`) with job picker — deferred to an "Assign Job" action.
- Resident-assignment (`AddResident`) as a distinct action — `ResidentialBuilding.SetOwner` already implicitly adds as resident, so owner-assignment covers the most-common dev case.
- Building-first flow ("pick building, then assign a character to it"). Character-first only.
- Multi-character selection, selection groups.
- Client-side dev actions — host-only as today.
- Item selection — deferred to a future slice as stated in the user's broader ambition.

## 3. Architecture

### 3.1 Components

```
Assets/Scripts/Debug/DevMode/
  DevModeManager.cs                        (extended: click-consumer slot)
  Modules/
    DevSpawnModule.cs                      (refactored: uses click-consumer)
    DevSelectionModule.cs                  (new — Select tab controller)
    Actions/
      IDevAction.cs                        (new — interface)
      DevActionAssignBuilding.cs           (new — first action)

Assets/Resources/UI/
  DevModePanel.prefab                      (restructured: tab bar + SpawnTab + SelectTab)

.agent/skills/dev-mode/
  SKILL.md                                 (updated)
```

### 3.2 Cross-system communication

- `DevSelectionModule` owns the authoritative `SelectedCharacter` state and raises `OnSelectionChanged` when it changes.
- Action scripts (MonoBehaviour implementing `IDevAction`) hold a `[SerializeField] DevSelectionModule _selection` reference, subscribe to `OnSelectionChanged` in `OnEnable`, and re-evaluate `IsAvailable(sel)` to enable/disable their own button. They never read each other.
- `DevModeManager` is the single coordination point for click consumption — no direct references between Spawn and Select modules.

## 4. `DevModeManager` — click-consumer extension

Today each click-reading module checks its own Armed toggle independently; arming both Spawn and Select would make both fire on the same click. The extension adds a single-slot coordinator:

```csharp
public MonoBehaviour ActiveClickConsumer { get; private set; }
public event Action OnClickConsumerChanged;

public void SetClickConsumer(MonoBehaviour consumer)   // null allowed
public void ClearClickConsumer(MonoBehaviour consumer) // only clears if consumer was the owner
```

Contract for click-reading modules:

1. Gate their click loop on `if (DevModeManager.Instance.ActiveClickConsumer != this) return;`.
2. When arming → `SetClickConsumer(this)`. Previous consumer loses the slot; `OnClickConsumerChanged` fires so it can auto-disarm its own toggle.
3. When disarming → `ClearClickConsumer(this)`.
4. Subscribe to `OnClickConsumerChanged`; if the new value isn't `this`, disarm self (idempotent toggle-off).

`DevSpawnModule` is updated to use this contract. Its observable UX is unchanged. `DevSelectionModule` uses the same contract from day one. Any future click-driven module (freecam hit-test, teleport destination picker, etc.) plugs in the same way.

## 5. `DevSelectionModule`

### 5.1 Panel UI (Select tab content)

```
SelectTab
├── Header: "Selection"
├── Toggle: "Select Character (click to pick)"
├── Label: "Selected: <name or '—'>"
├── Button: "Clear Selection"
├── Separator
├── Header: "Actions"
└── ActionsContainer
    └── (one child per IDevAction — first: AssignBuildingAction)
```

No Select-Building toggle. Buildings are picked only as part of an action, guided by that action's prompt.

### 5.2 Public API

```csharp
public class DevSelectionModule : MonoBehaviour
{
    public Character SelectedCharacter { get; private set; }
    public event Action OnSelectionChanged;

    public void SetSelectedCharacter(Character c);  // also fires OnSelectionChanged
    public void ClearSelection();                   // clears character + disarms
}
```

### 5.3 Click flow for character selection

`Update()` runs each frame when dev mode is enabled:

1. If `DevModeManager.Instance.ActiveClickConsumer != this` → return.
2. If `KeyCode.Escape` pressed → disarm self (set toggle false, `ClearClickConsumer`). Return.
3. If `!Input.GetMouseButtonDown(0)` → return.
4. If `EventSystem.current.IsPointerOverGameObject()` → return (click was UI).
5. Raycast: `Camera.main.ScreenPointToRay(Input.mousePosition)`, `Physics.Raycast(ray, out hit, 500f, ~0)` (all-layers).
6. Miss → `Debug.LogWarning("<color=orange>[DevSelect]</color> Raycast missed — click into the world.");` stay armed.
7. Hit → `Character c = hit.collider.GetComponentInParent<Character>();`.
   - `c == null` → `Debug.LogWarning("<color=orange>[DevSelect]</color> Click missed a Character.");` stay armed.
   - `c != null` → `SetSelectedCharacter(c)`, auto-disarm toggle, `ClearClickConsumer`, log `Debug.Log($"<color=cyan>[DevSelect]</color> Selected: {c.CharacterName}");`.

### 5.4 Target eligibility

Any component of type `Character` qualifies. This includes:

- NPCs (both scene-authored and dev-spawned).
- The host's own player character.
- Remote player Characters (replicated on the host's side — the host sees them as regular Characters).

No "is player?" filter. A later slice may add an exclude-self toggle if near-click ambiguity on the host's own character becomes annoying.

### 5.5 Arming + disarming

- Toggle ON → `DevModeManager.Instance.SetClickConsumer(this)`.
- Toggle OFF → if `ActiveClickConsumer == this`, `ClearClickConsumer(this)`.
- `OnClickConsumerChanged` handler → if `ActiveClickConsumer != this` and toggle is on, flip toggle off (idempotent).
- Dev mode disabled → `OnDevModeChanged(false)` listener flips toggle off and clears state if held.

## 6. `IDevAction` interface

```csharp
public interface IDevAction
{
    string Label { get; }                          // button text
    bool IsAvailable(DevSelectionModule sel);      // drives button enabled/disabled
    void Execute(DevSelectionModule sel);          // invoked by button click
}
```

Actions are MonoBehaviours attached as children of the SelectTab's ActionsContainer. Each action owns its own button visual and subscribes to `OnSelectionChanged` to refresh availability. `Execute` is arbitrary per action — it can run synchronously, open a sub-prompt, enter a new armed state, etc.

## 7. `DevActionAssignBuilding`

MonoBehaviour implementing `IDevAction`.

### 7.1 Contract

- `Label` → `"Assign Building as Owner"`.
- `IsAvailable(sel)` → `sel != null && sel.SelectedCharacter != null`.
- `Execute(sel)`:
  1. Capture `sel.SelectedCharacter` into a local `_pendingCharacter`.
  2. `DevModeManager.Instance.SetClickConsumer(this)`.
  3. Button label flips to `"Pick a building… (ESC to cancel)"`; button is visually disabled.
  4. Enters internal armed state (`_waitingForBuildingPick = true`).

### 7.2 Click loop (`Update` while armed)

1. If `ActiveClickConsumer != this` → silently exit armed state (another module claimed the click). Restore label.
2. If ESC → cancel, release consumer, restore label. Log `[DevAction] Assign Building: cancelled.`
3. If `!Input.GetMouseButtonDown(0)` → return.
4. If `EventSystem.current.IsPointerOverGameObject()` → return.
5. Raycast: `Physics.Raycast(ray, out hit, 500f, LayerMask.GetMask("Building"))`.
   - If the `"Building"` layer returned -1 on Start (missing from project layer config), log error and abort the whole action.
6. Miss → log warning, stay armed.
7. Hit → `Building b = hit.collider.GetComponentInParent<Building>();`.
   - Null (defensive; shouldn't happen given the layer filter) → stay armed.
   - `CommercialBuilding commercial` → `commercial.SetOwner(_pendingCharacter, null)`.
   - `ResidentialBuilding residential` → `residential.SetOwner(_pendingCharacter)`.
   - Other `Building` subtype → log warning `"<class> does not support SetOwner"`, stay armed so user can click a valid one.
8. Success → log `[DevAction] {_pendingCharacter.CharacterName} set as owner of {b.buildingName}.`, release consumer, restore label. Keep `SelectedCharacter` untouched so the user can chain further actions.

### 7.3 Host authority

- `Building.SetOwner` on both subtypes is already server-only (defensive `IsServer` gates inside each).
- Dev mode itself is host-only, so this action is unreachable from clients regardless. Double gate.

## 8. `DevModePanel` — tab infrastructure

Minimal extension:

```csharp
public class DevModePanel : MonoBehaviour
{
    [SerializeField] private GameObject _contentRoot;

    [Serializable] public struct TabEntry
    {
        public Button TabButton;
        public GameObject Content;
    }

    [SerializeField] private List<TabEntry> _tabs = new List<TabEntry>();

    // Start wires each button's onClick to SwitchTab(index), activates first tab.
    // SwitchTab(i) → for each tab, SetActive(index == i).
}
```

The prefab gains a tab bar directly inside `ContentRoot`, with two buttons (`Spawn`, `Select`) and two content GameObjects (`SpawnTab`, `SelectTab`). The existing Spawn UI is re-parented under `SpawnTab` without content changes. Only one tab's content is active at a time; selection state on the Select module persists across tab switches because the `DevSelectionModule` GameObject stays active via its data (the `SelectTab` GameObject may go inactive, but the state is a MonoBehaviour field on the tab itself; fields survive deactivation).

## 9. Raycast Layer Strategy

| Target | Raycast layers | Filter |
|---|---|---|
| Character pick | `~0` (all) | `GetComponentInParent<Character>()` |
| Building pick | `LayerMask.GetMask("Building")` | `GetComponentInParent<Building>()` |

Rationale: Characters may live on various child collider layers (rigidbody, interaction, awareness); component-based filtering is robust and cheap at dev-tool scale. Buildings have a dedicated `Building` layer with a `BoxCollider` on the prefab, so layer filtering is precise and avoids accidentally ray-hitting furniture or props inside.

## 10. Multiplayer Validation Matrix

| Scenario | Expected |
|---|---|
| Host opens panel, switches to Select tab, arms toggle, clicks an NPC | NPC selected on host. Label updates. `[DevSelect] Selected:` log. |
| Host selects NPC, clicks Assign Building, clicks a `CommercialBuilding` | `SetOwner(character, null)` runs on host. Building owner replicates to clients per existing building sync. |
| Host selects NPC, clicks Assign Building, clicks a `ResidentialBuilding` | `SetOwner(character)` runs; character is also added as resident per existing `SetOwner` behavior. |
| Host selects own player Character | Selection accepted (no self-filter this slice). |
| Host selects a remote client's player Character | Selection accepted on host side; `SetOwner` on their character applies server-side and replicates. |
| Host arms Spawn (in Spawn tab), switches to Select tab, arms Select | Spawn toggle auto-disarms via `OnClickConsumerChanged`. Only Select consumes next click. |
| Host arms Select, switches back to Spawn tab and arms Spawn | Select toggle auto-disarms. Reverse direction works the same. |
| Host arms Assign Building, presses ESC | Action cancels, consumer released, button label restored. No state mutation. |
| Host re-selects a different Character while a building pick is pending | The pending `_pendingCharacter` is already captured, so Assign Building still targets the original. This is intentional — the action is a transaction once started. |
| Client opens dev panel | Host-only gate already blocks. Select tab never renders for clients. |
| Host clicks empty space while Select armed | Warning logged, stays armed. |

## 11. File Plan

### 11.1 New files

- `Assets/Scripts/Debug/DevMode/Modules/DevSelectionModule.cs`
- `Assets/Scripts/Debug/DevMode/Modules/Actions/IDevAction.cs`
- `Assets/Scripts/Debug/DevMode/Modules/Actions/DevActionAssignBuilding.cs`

### 11.2 Modified files

- `Assets/Scripts/Debug/DevMode/DevModeManager.cs` — add `ActiveClickConsumer`, `OnClickConsumerChanged`, `SetClickConsumer`, `ClearClickConsumer`.
- `Assets/Scripts/Debug/DevMode/DevModePanel.cs` — add `TabEntry` struct + `_tabs` list + `SwitchTab(int)` + wiring in `Start`.
- `Assets/Scripts/Debug/DevMode/Modules/DevSpawnModule.cs` — refactor armed click loop to use `ActiveClickConsumer` contract; subscribe to `OnClickConsumerChanged` for auto-disarm.
- `Assets/Resources/UI/DevModePanel.prefab` — restructure: insert tab bar, re-parent existing Spawn UI under new `SpawnTab`, add `SelectTab` with its controls and `DevSelectionModule` + `DevActionAssignBuilding`, populate `_tabs` list in the `DevModePanel` component.
- `.agent/skills/dev-mode/SKILL.md` — new Select Tab section, `IDevAction` recipe, updated click arbitration note, extended known limitations.

## 12. Pre-Implementation Checklist

1. **Verify the `"Building"` layer exists in Tags & Layers.** `LayerMask.NameToLayer("Building")` must return ≥ 0. If missing, `DevActionAssignBuilding` logs an error and no-ops. The user confirmed this layer exists on building prefabs.
2. **Audit: does any existing building's BoxCollider sit on a different layer than the root?** If so, the raycast-then-`GetComponentInParent<Building>()` chain still works, but verify during implementation.
3. **Prefab restructure risk.** Re-parenting existing Spawn UI under a new `SpawnTab` child may break SerializeField references in `DevSpawnModule` if any use absolute-path references. All references are direct GameObject drags, so re-parenting under `SpawnTab` should not break them — verify after prefab edit.
4. **Existing Select Character armed toggle style.** Reuse the same TMP Toggle visuals as the spawn Armed toggle for consistency.

## 13. Known Limitations

Documented in `.agent/skills/dev-mode/SKILL.md`:

1. **No visual selection indicator** — the selected character is shown by label only in the panel. Follow-up slice can add a world-space outline (shader-based per rule 25) or a UI marker.
2. **No undo** — assigning a new owner to a building replaces the previous owner via the existing `SetOwner` path. No confirmation dialog.
3. **Character-first flow only** — building-first ("click building, pick character") symmetric inverse deferred.
4. **Single-character selection** — no groups, no multi-select.
5. **No exclude-self filter** — clicking on the host's own character selects it. Add a toggle if this becomes annoying.
6. **Worker/resident/job as distinct actions** deferred. Residential secondary owners (`AddOwner`) also deferred.
7. **Item selection and actions** — not in this slice; deferred to a future "Select Item" module.
8. **Client-side dev actions** — still out of scope.

## 14. Post-Implementation Tasks

- **Rule 21 / 28:** update `.agent/skills/dev-mode/SKILL.md` as listed in §11.2.
- **Rule 29:** update `.claude/agents/debug-tools-architect.md` with the Select tab + `IDevAction` pattern. No new agent needed.
- **Rule 18:** run the multiplayer validation matrix in §10 during Play-mode verification.
