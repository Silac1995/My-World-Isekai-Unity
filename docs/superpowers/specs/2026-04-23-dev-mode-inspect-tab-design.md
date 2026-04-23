# Dev-Mode Inspect Tab — Design Spec

**Date:** 2026-04-23
**Owner:** Kevin
**Status:** Design — pending user approval

---

## 1. Goal

Add an **Inspect** tab to the Dev-Mode panel that displays read-only runtime information about the currently selected `InteractableObject`. The tab dispatches to a per-type view so it can be extended later to cover `WorldItem` and `Building` inspectors without refactoring the plumbing.

**Scope for this spec:** ship the full **Character** inspector today. Widen selection to accept any `InteractableObject` so future views drop in, but do **not** implement the Item or Building views yet.

## 2. Non-goals

- No mutation. Pure read-only inspector. All edits continue to go through existing `IDevAction` entries on the Select tab.
- No client-side support. Host-only, same envelope as the rest of Dev-Mode.
- No networked replication of the inspector UI.
- No refactor of non-AI character subsystems. Inspector binds to existing public getters as-is.

## 3. User-facing behaviour

1. Open Dev-Mode (F3 or `/devmode on`), click the new **Inspect** root tab.
2. Switch to the Select tab once, arm selection, click an NPC in-world → selection state is now shared with the Inspect tab.
3. Come back to Inspect: the Character inspector is active, showing the 10 sub-tabs described below.
4. Click any sub-tab button to switch categories. Data refreshes every frame while that sub-tab is visible.
5. If nothing is selected, the Inspect tab shows a neutral "No selection" placeholder.
6. If a future `WorldItem` or `Building` interactable is selected, the Character inspector deactivates; the matching view (when it exists) activates. If none matches, the placeholder shows.

## 4. Sub-tab breakdown (Character inspector)

| # | Sub-tab | Contents |
|---|---------|----------|
| 1 | **Identity** | `CharacterBio` (name, gender, age, race), `CharacterId`, `Archetype.name`, `OriginWorldGuid`, `IsAbandoned` + `FormerPartyLeaderId`, `BusyReason`, life/state flags (`IsAlive`/`IsDead`/`IsUnconscious`/`IsBuilding`), `IsPlayer()` / NPC |
| 2 | **Stats** | Full `CharacterStats` (Health/Stamina/Mana/Initiative/Strength/Agility/Dexterity/Intelligence/Endurance/Charisma/PhysicalPower/Speed/DodgeChance/Accuracy/ManaRegenRate/StaminaRegenRate/CriticalHitChance/MoveSpeed) + `CharacterCombatLevel` (current level, XP, unassigned stat points, level history) |
| 3 | **Skills & Traits** | `CharacterSkills.Skills` (name/level/XP per skill) + `CharacterTraits` personality (`GetAggressivity`, `GetSociability`, `GetLoyalty`, `CanCreateCommunity`) |
| 4 | **Needs** | `CharacterNeeds.AllNeeds` — each formatted with `GetUrgency()` and `IsActive()`, colour-coded grey / amber / red (same scheme as `UI_CharacterDebugScript`) |
| 5 | **AI** | Current `CharacterAction`, `NPCController.GetBehaviourStackNames()` (Current + Queue), `CharacterInteraction.CurrentTarget` + `IsInteracting`, `NavMeshAgent` state (stopped / path / off-mesh), `BusyReason`, work-phase + job GOAP Goal/Action vs. Life GOAP, `BehaviourTree.DebugCurrentNode`, Life GOAP current action. Powered by the extracted `CharacterAIDebugFormatter`. |
| 6 | **Combat** | `CharacterCombat` (`IsInBattle`, combat mode, `KnownStyles`, `CurrentCombatStyleExpertise`, `PlannedTarget`, `CurrentBattleManager`) + `CharacterStatusManager.ActiveEffects` |
| 7 | **Social** | `CharacterRelation.Relationships` (per entry: other name, `RelationType`, `RelationValue`) + `CharacterCommunity` (current community + role) + `CharacterMentorship` (mentor, apprentices, `IsCurrentlyTeaching`) |
| 8 | **Economy** | `CharacterWallet.GetAllBalances()` + `CharacterJob` (`CurrentJob`, `ActiveJobs`, `IsWorking`) + `CharacterWorkLog` (current shift units per JobType, career units, workplaces) |
| 9 | **Knowledge** | `CharacterBookKnowledge` (books read / knowledge entries) + `CharacterSchedule` (slots, current slot) |
| 10 | **Inventory** | `CharacterEquipment` equipped slots + inventory contents |

Each sub-tab writes a single formatted string into one `TMP_Text` inside a `ScrollRect` — no per-field text bindings, no per-stat prefab wiring.

## 5. Architecture

### 5.1 Selection generalization

`DevSelectionModule` gains a parallel, additive surface. Existing API is preserved for back-compat with every existing `IDevAction`.

```csharp
// new
public InteractableObject SelectedInteractable { get; private set; }
public event Action<InteractableObject> OnInteractableSelectionChanged;

// existing — now derived from SelectedInteractable
public Character SelectedCharacter { get; private set; }
public event Action<Character> OnSelectionChanged;
```

- Click raycast widens: after hit, walk parents with `hit.collider.GetComponentInParent<InteractableObject>()`.
- On hit → set `SelectedInteractable`, fire `OnInteractableSelectionChanged`.
- If the interactable is a `CharacterInteractable` (or otherwise resolves to a `Character`), set `SelectedCharacter` and fire `OnSelectionChanged`.
- Deselect (escape / click-off) clears both.

### 5.2 Inspector dispatch

```csharp
public interface IInspectorView
{
    bool CanInspect(InteractableObject target);
    void SetTarget(InteractableObject target);
    void Clear();
}
```

- `DevInspectModule` is the MonoBehaviour on the Inspect tab Content root.
- On Awake: `GetComponentsInChildren<IInspectorView>(includeInactive: true)` — no manual registration.
- Subscribes to `DevSelectionModule.OnInteractableSelectionChanged`.
- On selection change: iterate views, first `CanInspect` wins → activate its GO + call `SetTarget`, deactivate all others. If none match, show placeholder.
- On null selection: `Clear()` on the last-active view, show placeholder.

### 5.3 CharacterInspectorView + sub-tabs

```csharp
public sealed class CharacterInspectorView : MonoBehaviour, IInspectorView { … }

public abstract class CharacterSubTab : MonoBehaviour
{
    [SerializeField] protected TMP_Text _content;
    public abstract void Refresh(Character c);
}
```

- View owns the tab-bar Buttons[] and the CharacterSubTab[] (one per category, assigned in the prefab).
- Tracks `_activeIndex`. Button click → SetActive the matching content, deactivate others.
- `Update()`: if `_target != null && _subTabs[_activeIndex] != null` → `_subTabs[_activeIndex].Refresh(_target)` inside a `try/catch`. Only the visible sub-tab runs per frame.
- 10 concrete sub-tab classes (~40–80 lines each) formatting their own category.

### 5.4 AI formatter extraction

`UI_CharacterDebugScript` currently mixes TMP binding + formatting. The formatting is lifted into:

```csharp
public static class CharacterAIDebugFormatter
{
    public static string FormatAction(Character c);
    public static string FormatBehaviourStack(Character c);
    public static string FormatInteraction(Character c);
    public static string FormatAgent(Character c);
    public static string FormatBusyReason(Character c);
    public static string FormatWorkPhaseGoap(Character c);
    public static string FormatBtAndLifeGoap(Character c);
    public static string FormatAll(Character c); // composes all of the above
}
```

- `AISubTab.Refresh(c)` → `_content.text = CharacterAIDebugFormatter.FormatAll(c);`.
- `UI_CharacterDebugScript.Update` is rewritten to call the same formatters (per field). Behaviour is identical; dedup only.

### 5.5 Defensive wrapping

Each `CharacterSubTab.Refresh` is wrapped by the orchestrator:

```csharp
try { _subTabs[i].Refresh(_target); }
catch (Exception e)
{
    Debug.LogException(e, this);
    _subTabs[i].SetErrorMessage($"⚠ {_subTabs[i].GetType().Name} failed — see console");
}
```

One broken getter cannot blank the inspector. Matches CLAUDE.md rule 31.

## 6. File layout

```
Assets/Scripts/Debug/DevMode/Inspect/
├── DevInspectModule.cs
├── IInspectorView.cs
├── CharacterInspectorView.cs
├── CharacterAIDebugFormatter.cs
└── SubTabs/
    ├── CharacterSubTab.cs           // abstract
    ├── IdentitySubTab.cs
    ├── StatsSubTab.cs
    ├── SkillsTraitsSubTab.cs
    ├── NeedsSubTab.cs
    ├── AISubTab.cs
    ├── CombatSubTab.cs
    ├── SocialSubTab.cs
    ├── EconomySubTab.cs
    ├── KnowledgeSubTab.cs
    └── InventorySubTab.cs
```

Modifications to existing files:
- `Assets/Scripts/Debug/DevMode/DevSelectionModule.cs` — add `SelectedInteractable` + event, widen raycast, keep `SelectedCharacter` derived.
- `Assets/Scripts/UI/WorldUI/UI_CharacterDebugScript.cs` — replace inline formatting with calls into `CharacterAIDebugFormatter`. No behaviour change.
- `Assets/Resources/UI/DevModePanel.prefab` — add Inspect tab entry (Button + Content) to the outer `DevModePanel._tabs`.

## 7. Prefab structure

Everything under the new Inspect Content root is prefab-built once, then driven in code. Minimal editor work:

```
InspectContent (GameObject, holds DevInspectModule)
├── Placeholder (TMP_Text "No selection" — shown when no view matches)
└── Views
    └── CharacterInspectorView (GameObject, holds CharacterInspectorView component)
        ├── TabBar
        │   ├── Btn_Identity, Btn_Stats, …, Btn_Inventory   (10 Buttons)
        └── SubTabContents
            ├── Identity   (ScrollRect + Viewport + Content_TMP_Text, holds IdentitySubTab)
            ├── Stats      (same structure, holds StatsSubTab)
            ├── … (8 more)
            └── Inventory  (same, holds InventorySubTab)
```

- Each sub-tab GameObject has: one `ScrollRect` with Vertical-only, one `Viewport + Mask`, one Content-area `TMP_Text` (assigned to `_content` on the sub-tab script).
- `TabBar` button layout: single row if it fits, otherwise a 5×2 grid (~60–80 px vertical). Authoring choice, not code-constrained.
- No separate prefab per sub-tab — they're plain children. Easier to iterate in the Inspector.

## 8. Update cadence & performance

- Only the visible sub-tab's `Refresh` runs per frame.
- Sub-tab formatting uses `StringBuilder`. A single string assignment to TMP is fine at 60 FPS — our data volume is tiny compared to what `UI_CharacterDebugScript` already does.
- No allocations in hot paths worth optimising — this is a debug tool.

## 9. Network & multiplayer

- Host-only, gated by `DevModeManager` as today. Clients never run the inspector.
- All reads are local; no RPCs.
- For NPCs owned server-side on the host, getters return live values. Selecting a remote player's avatar on host reads the host-side state (correct for the authority model). The inspector is not intended to debug non-host clients.

## 10. Error handling

- `try/catch` around each sub-tab `Refresh`; log via `Debug.LogException`, surface a red one-line error into the sub-tab content.
- `SelectedInteractable` validated each frame for nullity (destroyed GameObject → `Clear()` + placeholder).
- No silent swallows. Rule 31 (CLAUDE.md) is respected.

## 11. Testing plan

Manual verification (no automated tests — this is tooling):

1. **Selection round-trip** — arm Select, click an NPC, confirm old IDevActions still fire (back-compat).
2. **Inspect activation** — click Inspect tab; confirm Character view shows, placeholder is hidden.
3. **Each sub-tab renders** — step through all 10 tabs on a living NPC, confirm every section has content and no red error lines.
4. **Subsystem failure isolation** — deliberately null out `CharacterWallet` in the Inspector on a running character; confirm Economy tab shows the red error line and the other 9 tabs keep working.
5. **Selection change** — select a different NPC, confirm the view updates, no stale data.
6. **Deselect** — cancel selection, confirm placeholder returns.
7. **AI tab parity** — open the in-world `UI_CharacterDebugScript` next to the AI sub-tab, confirm identical output (regression check for the extraction).
8. **Player vs NPC** — inspect the local player's avatar, confirm the Agent / BT / GOAP fields show `PLAYER (Manual)` / N/A as today.

## 12. Extension path (future)

To add a `WorldItemInspectorView` later:
1. Create `Assets/Scripts/Debug/DevMode/Inspect/WorldItemInspectorView.cs` implementing `IInspectorView`; have `CanInspect` return `target is ItemInteractable`.
2. Drop a sibling GameObject under `Views/` in the Inspect prefab with its own sub-tab layout (or a single content pane).
3. Done — `DevInspectModule` picks it up on the next Awake; no registration, no orchestrator changes.

Same recipe for `BuildingInspectorView` once the building interactable surface is settled.

## 13. Open questions

None blocking. Authoring choice on `TabBar` row layout (single row vs. 5×2 grid) is deferred to prefab build time.

## 14. Rule alignment check

- **CLAUDE.md #9 (SRP):** each sub-tab class has exactly one formatting concern.
- **CLAUDE.md #10 (Open/Closed):** new inspectors are added by creating new `IInspectorView` implementations — no edits to `DevInspectModule` required.
- **CLAUDE.md #12 (interface segregation):** `IInspectorView` is tiny on purpose; `CharacterSubTab` isolates per-category formatting.
- **CLAUDE.md #13 (DIP):** `DevInspectModule` depends on `IInspectorView`, not on concrete views.
- **CLAUDE.md #22 (player ↔ NPC parity):** inspector is pure read; nothing violates the shared-action rule.
- **CLAUDE.md #28 (SKILL.md):** `.agent/skills/debug-tools/SKILL.md` (or equivalent) will be created/updated at implementation time, listing the new Inspect tab + public IInspectorView surface.
- **CLAUDE.md #29 (agents):** existing `debug-tools-architect` agent already covers Dev-Mode; its file will be updated to mention the Inspect tab + `IInspectorView` pattern.
- **CLAUDE.md #29b (wiki):** an architecture page under `wiki/systems/` for Dev-Mode (or the existing one) will be updated at implementation time.
- **CLAUDE.md #31 (defensive coding):** per-sub-tab `try/catch` with logging is in-scope.
