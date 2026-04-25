---
name: debug-tools
description: Covers the full debug/dev-tools infrastructure — DevModeManager (hosts global shortcuts Ctrl+Click interior-select / Alt+Click building-select / Space+LMB spawn / ESC cancel), DevModePanel, DevSpawnModule (Spawn tab), DevSelectionModule (Select tab, IDevAction plug-ins, generalized to InteractableObject, dual-mask interior + building), the Inspect tab (DevInspectModule, IInspectorView, CharacterInspectorView, CharacterSubTab hierarchy, StorageFurnitureInspectorView), CharacterAIDebugFormatter, DevInspectTabBuilder + DevStorageFurnitureInspectorBuilder one-shot Editor scripts for wiring the Inspect prefab, and legacy diagnostic scripts (UI_CharacterDebugScript, MapControllerDebugUI). Use when creating, extending, or debugging any dev tool, diagnostic panel, or in-editor/in-game inspection flow.
---

# Debug Tools System

The debug-tools system spans two families of tooling that coexist in the project:

1. **Legacy diagnostic scripts** — self-contained MonoBehaviours (e.g., `UI_CharacterDebugScript`, `MapControllerDebugUI`, `UI_CommercialBuildingDebugScript`) that each manage their own UI independently and have no shared registration.

2. **Dev Mode god tool** — a host-only togglable admin panel (`DevModePanel`) with a module registry, click arbitration, and a growing library of pluggable modules. The procedural details for Dev Mode's activation, chat commands, and Spawn + Select modules live in `.agent/skills/dev-mode/SKILL.md`. This file focuses on the Inspect tab and serves as the overview skill for the full debug-tools domain.

## When to use this skill

- When adding a new inspection view for a new interactable type (e.g., WorldItem, Building).
- When adding a new sub-tab to the Character inspector.
- When extending `CharacterAIDebugFormatter` with new AI diagnostic strings.
- When creating a new legacy diagnostic script (UI overlay, per-entity readout).
- When deciding which family a new debug tool belongs to (standalone vs. Dev Mode module).
- When debugging why the Inspect tab shows "no view" or the wrong sub-tab.

---

## The Inspect Tab Architecture

The Inspect tab is the third Dev Mode module. It shows **read-only runtime information** for the currently selected `InteractableObject`. Host-only. No RPCs, no state mutation.

### 1. Dispatch Contract — `IInspectorView`

```csharp
public interface IInspectorView
{
    bool CanInspect(InteractableObject target);
    void SetTarget(InteractableObject target);
    void Clear();
}
```

**Rule:** Every inspector view implements this interface. Views are MonoBehaviours.

`DevInspectModule` is the root dispatcher:

- Calls `GetComponentsInChildren<IInspectorView>(true)` at `Awake` to discover all views in its hierarchy — **no manual registration required**.
- Subscribes to `DevSelectionModule.OnInteractableSelectionChanged`.
- On each selection change: iterates views, calls `CanInspect(target)` on each, activates the first match, deactivates the rest. Shows a placeholder GameObject when no view matches.
- All `CanInspect` / `SetTarget` / `Clear` calls are wrapped in `try/catch` — a broken view never crashes the module.

### 2. Character Inspector — `CharacterInspectorView`

`CharacterInspectorView : MonoBehaviour, IInspectorView`

- `CanInspect(target)` returns `target is CharacterInteractable`.
- Derives the `Character` via `((CharacterInteractable)target).Character`.
- Owns an array of `SubTabEntry` (10 entries: button + content GameObject + `CharacterSubTab` component reference).
- `Update()` calls `activeSubTab.Refresh(character)` each frame — only the visible sub-tab pays the render cost.
- Tab switching deactivates the old content GO, activates the new one.

### 3. Sub-Tab Base — `CharacterSubTab`

```csharp
public abstract class CharacterSubTab : MonoBehaviour
{
    public void Refresh(Character c);             // public entry; wraps RenderContent in try/catch
    public virtual void Clear();                  // resets TMP_Text to placeholder text

    protected abstract string RenderContent(Character c);  // override in each subclass
}
```

**Rule:** Every `RenderContent` override uses `StringBuilder` + `<color=…>` rich text. If the method throws, `Refresh` writes a red `⚠ Error: <message>` line to the content TMP — the tab never goes blank on exception.

Sub-tab content is displayed in a TMP_Text component inside a ScrollRect. Only the active sub-tab calls `Refresh` each frame.

### 4. The 10 Character Sub-Tabs

| Index | Class | Content |
|-------|-------|---------|
| 0 | `IdentitySubTab` | Name / Gender / Age / Race / Archetype / CharacterId / OriginWorld + state flags (BusyReason, alive/unconscious/building/player, party membership, abandoned flag + former leader id) |
| 1 | `StatsSubTab` | CharacterCombatLevel (Level / XP / unassigned points / history count) + all 18 CharacterStats fields |
| 2 | `SkillsTraitsSubTab` | Behavioural profile **name** (SO asset) + numeric traits (Aggressivity / Sociability / Loyalty / CanCreateCommunity). Personality: Name + Description + Compatible / Incompatible lists (colour-coded). CharacterSkills.Skills list |
| 3 | `NeedsSubTab` | CharacterNeeds.AllNeeds with urgency, active status, and color coding |
| 4 | `AISubTab` | `CharacterAIDebugFormatter.FormatAll(c)` — one-liner delegation |
| 5 | `CombatSubTab` | CharacterCombat (IsInBattle / IsCombatMode / PlannedTarget / CurrentBattleManager / KnownStyles) + CharacterStatusManager.ActiveEffects |
| 6 | `SocialSubTab` | Relationships rendered as `Name — Type (±value) [met/unmet]` with value colour-coded (green positive, red negative, grey zero). + CharacterCommunity + CharacterMentorship.IsCurrentlyTeaching |
| 7 | `EconomySubTab` | Wallet enumerates every known `CurrencyId` (static-readonly fields via reflection) always showing balance, even at 0. Job block (CurrentJob / ActiveJobs / IsWorking). Work Log per-JobType summary (iterates `Enum.GetValues(typeof(JobType))`) + dedicated flat **Workplaces** list sorted by UnitsWorked desc, each entry showing name / JobType tag / score (units) / shifts / day range |
| 8 | `KnowledgeSubTab` | CharacterBookKnowledge (ToString placeholder) + fully-rendered Schedule: CurrentActivity, TimeManager CurrentHour, list of every ScheduleEntry as `HHh–HHh · Activity · priority N`, active-now entry highlighted green with `◆ active` tag |
| 9 | `InventorySubTab` | CharacterEquipment (initial ToString() placeholder) |

### 4b. Storage Furniture Inspector — `StorageFurnitureInspectorView`

`StorageFurnitureInspectorView : MonoBehaviour, IInspectorView` (`Assets/Scripts/Debug/DevMode/Inspect/StorageFurnitureInspectorView.cs`).

- `CanInspect(target)` returns `target is FurnitureInteractable fi && fi.Furniture is StorageFurniture`. Selection works for free — every storage prefab already exposes a `FurnitureInteractable`, which is an `InteractableObject` and is therefore picked up by `DevSelectionModule.TrySelectAtCursor`.
- `SetTarget` caches the `StorageFurniture` and the underlying `Furniture` (header label).
- `Update()` re-renders the slot listing every frame (cheap — only the active view runs while it is the inspector match).
- Renders: capacity (filled / total), `IsLocked`, `IsFull`, then per-slot `[index] <SlotType> — <item display name>`. Slot type label maps `WeaponSlot/WearableSlot/MiscSlot/AnySlot` to `Weapon/Wearable/Misc/Any`. Item display name prefers `ItemInstance.CustomizedName`, falls back to `ItemSO.ItemName`.
- Read-only by design — the view never mutates inventory, mirroring the rest of the Inspect tab.

The view GameObject is added to the prefab by the **`DevStorageFurnitureInspectorBuilder`** Editor utility (menu `Tools/DevMode/Build Storage Furniture Inspector`, with destructive rebuild variant). It places the new GO under `DevModePanel/ContentRoot/InspectContent/Views`, sibling to `CharacterInspectorView`, and wires the `_headerLabel` + `_content` serialized fields. `DevInspectModule` auto-discovers it at `Awake` via `GetComponentsInChildren<IInspectorView>(true)` — no further wiring required.

### 5. `CharacterAIDebugFormatter` — Shared AI Debug Strings

Static class with helpers:

- `FormatAction(Character c)` — current CharacterAction
- `FormatBehaviourStack(Character c)` — full behaviour stack
- `FormatInteraction(Character c)` — current interaction
- `FormatAgent(Character c)` — NavMesh agent state
- `FormatBusyReason(Character c)` — enum value + explanation
- `FormatWorkPhaseGoap(Character c)` — work-phase GOAP goal
- `FormatBt(Character c)` — BT tick info
- `FormatLifeGoap(Character c)` — life-goal GOAP state
- `FormatAll(Character c)` — composes all of the above

**Rule:** `CharacterAIDebugFormatter` is the **single source of truth** for AI debug strings. Both `UI_CharacterDebugScript` (legacy per-entity overlay) and `AISubTab` (Inspect panel tab) delegate to it. Extending `FormatAll` automatically updates both consumers.

### 6. Global Shortcuts (Ctrl+Click / Alt+Click / Space+LMB / ESC)

Global shortcuts live on **`DevModeManager`** — not on the individual tab modules — so they keep working regardless of which tab's content is currently active (tab content GameObjects are `SetActive(false)` when the user switches away, which would otherwise suspend their `Update` loop).

| Input | Action | Gate |
|-------|--------|------|
| Ctrl + Left-Click | **Interior select** — raycasts `RigidBody + Furniture` (default) and selects the first matching `InteractableObject` (Character fallback). Used for chests, beds, crafting stations, NPCs, players. | DevMode enabled + pointer not over UI + no text input focused + Alt/Space not also held |
| Alt + Left-Click | **Building select** — raycasts `Building` (default) and selects the building's `InteractableObject`. Bypasses the interior pick when the user explicitly wants the building shell. | DevMode enabled + pointer not over UI + no text input focused + Ctrl/Space not also held |
| Space + Left-Click | Spawn at cursor using the panel's current Spawn config | DevMode enabled + pointer not over UI + no text input focused + Ctrl/Alt not also held |
| Escape | Clear any `SelectedInteractable` + disarm all armed toggles | DevMode enabled + no text input focused |

**Wiring:** `DevModeManager.EnsurePanel` caches the panel's modules via `GetComponentInChildren<DevSelectionModule>(true)` / `GetComponentInChildren<DevSpawnModule>(true)` (`includeInactive: true` — critical, since non-current tabs are deactivated). `Update` runs `HandleGlobalShortcuts()` every frame while `IsEnabled`.

**Dispatch entry points on the modules (all public):**

- `DevSelectionModule.TrySelectAtCursor(out string label)` — interior raycast (`_selectableLayerMask`, default `RigidBody + Furniture`) + sets selection. Returns `false` if raycast missed.
- `DevSelectionModule.TrySelectBuildingAtCursor(out string label)` — building raycast (`_buildingLayerMask`, default `Building`) + sets selection. Returns `false` if raycast missed.
- `DevSelectionModule.ClearSelection()` — clears selection + fires change events.
- `DevSelectionModule.DisarmToggle()` / `IsArmed` — armed-toggle control.
- `DevSpawnModule.TrySpawnAtCursor()` — raycasts the Environment layer + spawns via existing `SpawnAt` path.
- `DevSpawnModule.DisarmToggle()` / `IsArmed`.

**Mutex:** Ctrl, Alt, and Space are handled as mutually exclusive on the same click — any combination of them fires nothing. Prevents accidental destructive spawn from a fat-finger and avoids ambiguity between interior / building picks. The per-module armed click-loops also short-circuit when any of the three modifiers is held so they never double-fire with the shortcut.

**Text-input guard:** `IsTextInputFocused()` returns true if `EventSystem.current.currentSelectedGameObject` has a `TMP_InputField` / `InputField` component. All shortcuts skip when true so typing `Space` in the Count field doesn't spawn.

### 7. `DevSelectionModule` — Extended Surface

The Select tab's `DevSelectionModule` was generalized to work with `InteractableObject` (not just `Character`). The following surface was **added** (back-compat paths preserved):

| New member | Purpose |
|-----------|---------|
| `InteractableObject SelectedInteractable { get; }` | The currently selected interactable (superset of character). |
| `event Action<InteractableObject> OnInteractableSelectionChanged` | Fires on any interactable selection change. `DevInspectModule` subscribes here. |
| `void SetSelectedInteractable(InteractableObject io)` | Replaces the interactable selection. |

**Back-compat members (still exist):**

| Preserved member | Behavior |
|-----------------|---------|
| `Character SelectedCharacter { get; }` | Returns `SelectedInteractable as Character` (or null). |
| `event Action OnSelectionChanged` | Fires when `SelectedCharacter` changes. Existing `IDevAction` consumers still work. |
| `void SetSelectedCharacter(Character c)` | Calls `SetSelectedInteractable(c)`. |

The field `_characterLayerMask` was renamed `_selectableLayerMask` with `[FormerlySerializedAs("_characterLayerMask")]` to preserve serialized prefab data.

**Default layer masks:** when the serialized fields are left at zero (the prefab default), `ResolveLayerMask` ORs:

- `_selectableLayerMask` ← `RigidBody + Furniture` — the **interior** mask used by Ctrl+Click. **Building is intentionally excluded** so a building's shell collider doesn't block selection of the chest, bed, or NPC inside it.
- `_buildingLayerMask` ← `Building` — used by Alt+Click to explicitly pick a building.

Missing layer names are tolerated (skipped) so the project can drop a layer without breaking dev-mode selection. Override either field on the prefab to narrow the mask, or keep them at zero to use the runtime defaults.

---

## Adding a New `IInspectorView` (e.g., for WorldItem or Building)

1. Create `WorldItemInspectorView : MonoBehaviour, IInspectorView` under `Assets/Scripts/Debug/DevMode/Inspect/`.
2. Implement `CanInspect(target)` — return `target is WorldItemInteractable` (or whatever the interactable type is).
3. Implement `SetTarget(target)` — cache the target, populate your display.
4. Implement `Clear()` — reset to placeholder state.
5. In the `DevModePanel` prefab, add a child GameObject under `DevInspectModule`'s hierarchy with your script attached.
6. No edit to `DevInspectModule` is required — it auto-discovers via `GetComponentsInChildren<IInspectorView>(true)`.

**Priority:** `DevInspectModule` picks the first `CanInspect` match in component order. Order child GameObjects in the prefab to control priority.

**Reference example:** `StorageFurnitureInspectorView` (script) + `DevStorageFurnitureInspectorBuilder` (one-shot editor builder under `Assets/Editor/DevMode/`). The builder mirrors `DevInspectTabBuilder`'s helpers (`CreateUIGameObject`, `CreateTMPLabel`, `CreateScrollRect`) and saves the prefab via `PrefabUtility.SaveAsPrefabAsset`. Prefer this pattern for any non-trivial inspector view so prefab wiring stays reproducible and source-controlled.

## Adding a New `CharacterSubTab`

1. Create `MySubTab : CharacterSubTab` under `Assets/Scripts/Debug/DevMode/Inspect/SubTabs/`.
2. Override `protected string RenderContent(Character c)` — build and return a string (use `StringBuilder` + rich text).
3. In the `DevModePanel` prefab, add a child GameObject with: a `ScrollRect`, a `TMP_Text` content object, and your sub-tab script attached.
4. In `CharacterInspectorView`'s Inspector, add a new entry to `_subTabs[]`: wire the button, content GO, and sub-tab component.
5. No edit to `CharacterInspectorView`'s C# is required.

---

## Legacy Diagnostic Scripts (Standalone)

These scripts predate Dev Mode and remain independent. They self-manage their own UI.

| Script | Purpose | Update strategy |
|--------|---------|----------------|
| `UI_CharacterDebugScript` | Per-entity NPC state overlay — delegates AI strings to `CharacterAIDebugFormatter` | Every frame |
| `MapControllerDebugUI` | Per-map diagnostics — hibernation state, player tracking, NPC counts | `_refreshRate = 0.5f` with `Time.unscaledTime` |
| `UI_CommercialBuildingDebugScript` | Building diagnostics — owner, jobs, logistics orders, storage | Every frame |
| `DebugScript` | Character/item spawning UI — race dropdown, prefab selector, item spawner | On demand |

**Rule:** Do not refactor these into Dev Mode modules unless explicitly planned. They serve a different purpose (lightweight world-space overlays attached to entities) vs. the host-side god panel.

---

## File Locations

```
Assets/Editor/DevMode/
  DevInspectTabBuilder.cs                  ← builds the Inspect tab hierarchy + 10 character sub-tabs
  DevStorageFurnitureInspectorBuilder.cs   ← adds the StorageFurnitureInspectorView GO under Views

Assets/Scripts/Debug/DevMode/
  DevModeManager.cs               ← now owns global shortcuts (Ctrl+Click / Alt+Click / Space+LMB / ESC)
  DevModePanel.cs
  DevChatCommands.cs
  Modules/
    DevSpawnModule.cs
    DevSpawnRow.cs
    DevSelectionModule.cs           ← generalized to InteractableObject; dual interior + building masks
    Actions/
      IDevAction.cs
      DevActionAssignBuilding.cs
  Inspect/
    IInspectorView.cs               ← dispatch contract
    DevInspectModule.cs             ← root dispatcher
    CharacterInspectorView.cs       ← character view (10 sub-tabs)
    StorageFurnitureInspectorView.cs ← storage view (chest / shelf / barrel / wardrobe slot listing)
    CharacterAIDebugFormatter.cs    ← shared AI debug strings
    SubTabs/
      CharacterSubTab.cs            ← abstract base
      IdentitySubTab.cs
      StatsSubTab.cs
      SkillsTraitsSubTab.cs
      NeedsSubTab.cs
      AISubTab.cs
      CombatSubTab.cs
      SocialSubTab.cs
      EconomySubTab.cs
      KnowledgeSubTab.cs
      InventorySubTab.cs

Assets/Scripts/UI/WorldUI/
  UI_CharacterDebugScript.cs        ← now delegates AI strings to CharacterAIDebugFormatter
  UI_CommercialBuildingDebugScript.cs

Assets/Scripts/World/MapSystem/
  MapControllerDebugUI.cs

Assets/Scripts/
  DebugScript.cs
```

## Known Gotchas

- **Sub-tab order in `_subTabs[]` is authoritative.** The tab buttons are index-matched to the array; mismatching them in the Inspector produces wrong labels on correct content.
- **`CanInspect` ordering.** `DevInspectModule` picks the first match. If two views both return `true` for the same target, only the one earlier in child order is used. Make `CanInspect` checks specific enough to avoid overlap.
- **`KnowledgeSubTab` — Schedule is fully rendered; BookKnowledge is still a placeholder.** Schedule now lists every `ScheduleEntry` with its hours range, activity, priority, and active-now flag. BookKnowledge still calls `ToString()` — refine once `CharacterBookKnowledge` exposes a richer public surface.
- **`InventorySubTab` is a placeholder.** Calls `CharacterEquipment.ToString()`. Refine once equipment slot enumeration is public.
- **Global shortcut ownership.** `Ctrl+Click`, `Alt+Click`, `Space+LMB`, and `ESC` are handled exclusively by `DevModeManager.HandleGlobalShortcuts` — not by the per-tab modules. If you find yourself adding shortcut logic to a tab module, stop: put it on `DevModeManager` instead, or the shortcut will silently stop working whenever that tab isn't the active one.
- **One-shot Editor builders.**
  - `Assets/Editor/DevMode/DevInspectTabBuilder.cs` — `[MenuItem("Tools/DevMode/Build Inspect Tab")]`. Builds the Inspect tab hierarchy + 10 character sub-tabs inside the DevModePanel prefab and wires every `SerializedObject` field. Idempotent; destructive `Rebuild` variant with confirmation dialog.
  - `Assets/Editor/DevMode/DevStorageFurnitureInspectorBuilder.cs` — `[MenuItem("Tools/DevMode/Build Storage Furniture Inspector")]`. Adds the `StorageFurnitureInspectorView` GameObject under `InspectContent/Views`, sibling to `CharacterInspectorView`, and wires `_headerLabel` + `_content`. Same idempotent + destructive pair.

  Prefer extending these builders when you add a new view or sub-tab — it keeps prefab regeneration reproducible and source-controlled.
- **Host-only.** `DevInspectModule`, like all Dev Mode modules, only runs on the host. Never add RPCs or server calls inside a sub-tab — read from local state only.
- **Legacy scripts are always compiled.** Only the Dev Mode family is behind `#if UNITY_EDITOR || DEVELOPMENT_BUILD` conditional-unlock (for the F3 auto-unlock path). The legacy scripts activate via `UI_SessionManager` checking `_isSolo`.
