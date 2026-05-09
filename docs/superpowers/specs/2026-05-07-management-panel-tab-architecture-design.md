# Commercial-Building Management Panel — Polymorphic Tab Architecture

> **Status**: Design — pending implementation plan.
> **Author**: Claude (brainstorming with Kevin), 2026-05-07.
> **Brief**: [docs/superpowers/briefs/2026-05-07-commercial-building-management-panel-architecture-brief.md](../briefs/2026-05-07-commercial-building-management-panel-architecture-brief.md).
> **Ships in**: this session.
> **Defers to follow-up phases**: per-job hiring data-model rewrite, universal Storage tab + `StorageRole` taxonomy.

---

## 1. Goal

Refactor the existing `UI_OwnerHiringPanel` (a single-purpose owner UI) into a polymorphic tabbed shell `UI_OwnerManagementPanel` driven by a `virtual GetManagementTabs()` method on `CommercialBuilding`. After the refactor, every `CommercialBuilding` subtype can append its own owner-only admin tabs by overriding the virtual — no edits to the panel itself, no edits to the entry points (`ManagementFurniture` / `CharacterJob.GetInteractionOptions`).

**Out-of-scope (deferred):**

- Per-job hiring (`_isHiring` building-wide bool → per-`Job` flag). Touches `BuildingManager.FindAvailableJob`, `InteractionAskForJob.CanExecute`, `CharacterJob.GetInteractionOptions` Section A, sign auto-format. Own design + plan.
- Universal Storage tab + `StorageRole` taxonomy (replacing `_toolStorageFurniture` + `_sellShelves`). Touches Phase 2b's just-landed shop code, `JobFarmer`, `CanPunchOut`. Own design + plan.
- Sign-furniture rework (Help Wanted sign becomes its own readable furniture type).
- Phase 2b shop tabs (Catalog / Shelves / Cashiers) — Phase 2b's responsibility, lands on top of this refactor's `GetManagementTabs()` virtual.

This refactor is **the polymorphic foundation only**. Hiring tab behaviour matches today's network paths bit-for-bit; only the UI body is simplified per Kevin's instruction (toggle-only, no sign edit, no job list display).

---

## 2. Architecture

### 2.1 New namespace

`MWI.UI.Management` — all new types live here.

### 2.2 New files

```
Assets/Scripts/UI/Management/
├── IManagementTab.cs              spec interface (Name + CreateView factory)
├── IManagementTabView.cs          view interface (Root + Activate/Deactivate/Dispose lifecycle)
├── UI_OwnerManagementPanel.cs     generic tabbed shell (singleton, replaces UI_OwnerHiringPanel)
├── HiringTab.cs                   IManagementTab — built-in for every CommercialBuilding
└── HiringTabView.cs               IManagementTabView MonoBehaviour — on the HiringTab prefab
```

### 2.3 Modified files

| File | Change |
|------|--------|
| `Assets/Scripts/World/Buildings/CommercialBuilding.cs` | Add `public virtual IReadOnlyList<MWI.UI.Management.IManagementTab> GetManagementTabs()` returning `[ new HiringTab(this) ]`. No new fields. |
| `Assets/Scripts/World/Furniture/ManagementFurniture.cs:54` | `UI_OwnerHiringPanel.Show(building)` → `UI_OwnerManagementPanel.Show(building)`. |
| `Assets/Scripts/Character/CharacterJob/CharacterJob.cs:678` | Same call swap; menu entry label `"Manage Hiring..."` → `"Manage..."` (now generic). |

### 2.4 Deleted files

| File | Reason |
|------|--------|
| `Assets/Scripts/UI/PlayerHUD/UI_OwnerHiringPanel.cs` | Replaced by `UI_OwnerManagementPanel` + `HiringTabView`. UI fields split: toggle migrates, sign-edit + job-list display dropped. |
| `Assets/Resources/UI/UI_OwnerHiringPanel.prefab` | Decommissioned. New prefabs replace it. |

### 2.5 Resources prefabs (designer-authored, or via Unity MCP at execution time)

| Path | Contents |
|------|---------|
| `Resources/UI/UI_OwnerManagementPanel` | Title label, tab header bar (RectTransform parent), tab header pill prefab reference, tab body container (RectTransform), close button, dismiss-overlay button. |
| `Resources/UI/Management/HiringTab` | Toggle-hiring button + label. (Body is intentionally minimal.) |

The old `Resources/UI/UI_OwnerHiringPanel.prefab` is decommissioned; its inspector slots are split between the two new prefabs.

### 2.6 Out-of-blast-radius

- `Assets/Scripts/World/Buildings/CommercialBuildings/ShopBuilding.cs` — **zero edits.** Inherits `GetManagementTabs()` from base, returns `[HiringTab]`. Phase 2b lands its overrides on top.

---

## 3. Components

### 3.1 `IManagementTab` — spec interface

```csharp
namespace MWI.UI.Management
{
    /// <summary>
    /// Spec for one tab in the owner management panel. Plain C# class — no Unity lifecycle.
    /// Constructed once per panel-open by CommercialBuilding.GetManagementTabs(); creates its
    /// view on demand via CreateView().
    /// </summary>
    public interface IManagementTab
    {
        string Name { get; }                  // header pill label, e.g. "Hiring"
        IManagementTabView CreateView();      // factory — instantiates and binds the view MonoBehaviour
    }
}
```

### 3.2 `IManagementTabView` — view interface

```csharp
namespace MWI.UI.Management
{
    public interface IManagementTabView
    {
        GameObject Root { get; }     // the instantiated GameObject the panel re-parents under its body
        void OnTabActivated();       // user clicked the header pill / panel just opened on this tab
        void OnTabDeactivated();     // user switched away — pause subscriptions if expensive
        void Dispose();              // panel closing or rebinding — unsubscribe events, free refs (rule #16)
    }
}
```

**Lifecycle order:** `CreateView` → `Activated` (initial) → (`Deactivated` / `Activated` cycles per pill click) → `Dispose` on panel close.

### 3.3 `UI_OwnerManagementPanel` — generic shell

Singleton, lazy-instantiated under the main Canvas. Mirrors the pattern in `UI_DisplayTextReader` and the old `UI_OwnerHiringPanel`.

```csharp
public class UI_OwnerManagementPanel : MonoBehaviour
{
    private const string PrefabResourcePath = "UI/UI_OwnerManagementPanel";
    private static UI_OwnerManagementPanel _instance;

    [Header("Header")]
    [SerializeField] private TextMeshProUGUI _titleLabel;          // "{building.BuildingName}"
    [Header("Tabs")]
    [SerializeField] private RectTransform _tabHeaderRoot;         // header bar — pills go here
    [SerializeField] private GameObject _tabHeaderPillPrefab;      // toggle-able button + label
    [SerializeField] private RectTransform _tabBodyRoot;           // active tab Root reparented under here
    [Header("Close")]
    [SerializeField] private Button _closeButton;
    [SerializeField] private Button _dismissOverlay;

    private CommercialBuilding _building;
    private struct Entry { public IManagementTabView View; public Button Pill; public TextMeshProUGUI PillLabel; }
    private readonly List<Entry> _spawned = new List<Entry>(4);
    private Entry? _activeEntry;

    public static void Show(CommercialBuilding building) { /* lazy-instantiate + ShowInternal */ }
    private void ShowInternal(CommercialBuilding building) { /* defense-in-depth + rebuild or warm-path */ }
    private void SwitchTo(int index) { /* OnTabDeactivated old + OnTabActivated new */ }
    private void Close() { /* Dispose all + Destroy pills + SetActive(false) */ }
}
```

**Defense-in-depth owner gate** in `ShowInternal`: if `building.Owner != localCharacter`, fail-silent (`Debug.LogWarning`) and bail. Today's authoritative gate is at the call sites (`ManagementFurniture.Use` toast + `CharacterJob.GetInteractionOptions` ownership condition); this panel-level check catches future call sites that forget.

**Tab header always visible** — even with one tab, the "Hiring" pill renders (Kevin's section-1 choice).

**Re-Show policy:**

- Same building: warm path. `gameObject.SetActive(true)`, no rebuild, no allocations.
- Different building: tear down (`view.Dispose()` per entry, destroy pills) + rebuild from scratch.

### 3.4 `HiringTab` + `HiringTabView`

```csharp
namespace MWI.UI.Management
{
    public sealed class HiringTab : IManagementTab
    {
        private readonly CommercialBuilding _building;
        public HiringTab(CommercialBuilding building) { _building = building; }
        public string Name => "Hiring";
        public IManagementTabView CreateView()
        {
            const string path = "UI/Management/HiringTab";
            var prefab = Resources.Load<HiringTabView>(path);
            if (prefab == null) { Debug.LogWarning($"[HiringTab] Missing prefab Resources/{path}"); return null; }
            var view = Object.Instantiate(prefab);
            view.Bind(_building);
            return view;
        }
    }

    public sealed class HiringTabView : MonoBehaviour, IManagementTabView
    {
        [SerializeField] private Button _toggleHiringButton;
        [SerializeField] private TextMeshProUGUI _toggleHiringLabel;

        private CommercialBuilding _building;
        public GameObject Root => gameObject;

        public void Bind(CommercialBuilding b) { /* hook OnHiringStateChanged + button click, Refresh() */ }
        public void OnTabActivated()   { /* no-op — view is live while bound */ }
        public void OnTabDeactivated() { /* no-op */ }
        public void Dispose() { /* unsubscribe + Destroy(gameObject) */ }

        // Click handler → existing TryOpenHiring/TryCloseHiring (unchanged ServerRpc paths).
        // OnHiringStateChanged handler → Refresh button label.
    }
}
```

**Bit-for-bit hiring behaviour:** same `TryOpenHiring`/`TryCloseHiring` calls, same `OnHiringStateChanged` subscription, same auto-refresh. The visible UI is just narrower.

**Body simplified per Kevin's instruction:** just toggle button + label. No status label (button text already says "Open Hiring" / "Close Hiring"). No job list. No sign-edit input — sign editing migrates to Kevin's future sign-furniture rework.

### 3.5 `CommercialBuilding.GetManagementTabs()`

```csharp
// On CommercialBuilding base
public virtual IReadOnlyList<MWI.UI.Management.IManagementTab> GetManagementTabs()
{
    return new MWI.UI.Management.IManagementTab[] { new MWI.UI.Management.HiringTab(this) };
}
```

Subtype override pattern (illustrative — not shipped here):

```csharp
// Future ShopBuilding override (Phase 2b owns this — DO NOT ship in this session)
public override IReadOnlyList<IManagementTab> GetManagementTabs()
{
    var tabs = new List<IManagementTab>(base.GetManagementTabs());
    tabs.Add(new ShopCatalogTab(this));
    tabs.Add(new ShopShelvesTab(this));
    tabs.Add(new ShopCashiersTab(this));
    return tabs;
}
```

**Allocation note (rule #34):** called on panel open, not per-frame — single allocation acceptable. If future profiling shows panels open frequently, swap to a cached `IReadOnlyList` field rebuilt only when subtype tab list changes.

---

## 4. Data flow + lifecycle

### 4.1 Open flow (cold)

```
Owner walks up to ManagementFurniture, presses E
        │
        ▼
ManagementFurniture.Use(character)
   ├─ if (!character.IsOwner)         return true       (remote-client gate)
   ├─ if (!character.IsPlayer())      return true       (NPCs silent-success)
   ├─ resolve building via GetComponentInParent<CommercialBuilding>()
   ├─ if (building.Owner != character) → UI_Toast "Only the owner..." + return true
   └─ UI_OwnerManagementPanel.Show(building)
        │
        ▼
   Show — first call (cold path)
        ├─ Resources.Load<UI_OwnerManagementPanel>("UI/UI_OwnerManagementPanel")
        ├─ Instantiate under main Canvas → _instance assigned
        └─ _instance.ShowInternal(building)
                │
                ▼
        ShowInternal
                ├─ defense-in-depth owner check (resolve local player → owner equality; bail-silent if mismatch)
                ├─ _titleLabel.text = building.BuildingName
                ├─ tabs = building.GetManagementTabs()        // [ HiringTab(building) ]
                ├─ for each tab:
                │     view = tab.CreateView()                 // Resources.Load + Instantiate (HiringTabView)
                │     view.Root.transform.SetParent(_tabBodyRoot, false)
                │     view.Root.SetActive(false)              // hide non-active by default
                │     pill = Instantiate(_tabHeaderPillPrefab, _tabHeaderRoot)
                │     pill.label.text = tab.Name
                │     pill.button.onClick += () => SwitchTo(tabIndex)
                │     _spawned.Add({pill, view})
                ├─ SwitchTo(0)                                // activate first tab
                └─ gameObject.SetActive(true)
```

### 4.2 Switch-tab flow

```
SwitchTo(targetIndex)
        ├─ if (_activeEntry == _spawned[targetIndex]) return       // same tab — no-op
        ├─ if (_activeEntry != null):
        │     _activeEntry.View.OnTabDeactivated()
        │     _activeEntry.View.Root.SetActive(false)
        │     SetPillSelected(_activeEntry.Pill, false)
        ├─ _activeEntry = _spawned[targetIndex]
        ├─ _activeEntry.View.Root.SetActive(true)
        ├─ _activeEntry.View.OnTabActivated()
        └─ SetPillSelected(_activeEntry.Pill, true)
```

### 4.3 Re-Show flow

- **Same building:** skip rebuild. Re-activate first tab if hidden. `gameObject.SetActive(true)`.
- **Different building:** for each `_spawned` entry, `View.Dispose()` → destroy pill GO. Clear list. Rebuild as cold path.

### 4.4 Hiring toggle flow (preserved bit-for-bit)

```
HiringTabView._toggleHiringButton clicked
        ├─ resolve local Character via NetworkManager.LocalClient
        ├─ if (_building.IsHiring) _building.TryCloseHiring(localCharacter)
        └─ else                    _building.TryOpenHiring(localCharacter)
                │   (existing client → ServerRpc → server validates Owner authority → flips _isHiring)
                ▼
        NetworkVariable replicates → OnValueChanged on every peer
                │
                ▼
        _building.OnHiringStateChanged event fires (server + clients)
                │
                ▼
        HiringTabView.HandleHiring(newVal) → updates _toggleHiringLabel.text
```

**No new ServerRpcs introduced. No new replicated state.** Existing `_isHiring` NetworkVariable + `OnHiringStateChanged` event continue to be the single source of truth.

### 4.5 Close flow

```
User presses ESC OR clicks _closeButton OR clicks _dismissOverlay
        │
        ▼
Close()
        ├─ for each _spawned entry: View.Dispose() (unsubscribes + destroys view GO)
        ├─ destroy all pill GameObjects
        ├─ _spawned.Clear() ; _activeEntry = null ; _building = null
        └─ gameObject.SetActive(false)
```

`_instance` stays around as the lazy singleton. Re-opens hit the warm path.

### 4.6 OnDestroy flow

```
OnDestroy
        ├─ if (_instance == this) _instance = null
        ├─ for each _spawned: View.Dispose()
        ├─ remove all button.onClick listeners (rule #16)
        └─ no further cleanup needed (subscriptions are per-view)
```

### 4.7 Lifecycle invariants (rule #16 compliance)

| Hook | Subscriptions established | Subscriptions torn down |
|------|---------------------------|-------------------------|
| `HiringTabView.Bind` | `_building.OnHiringStateChanged += HandleHiring`, `_toggleButton.onClick += OnToggle` | — |
| `HiringTabView.Dispose` | — | both above |
| `UI_OwnerManagementPanel.Awake` | `_closeButton.onClick`, `_dismissOverlay.onClick` | — |
| `UI_OwnerManagementPanel.OnDestroy` | — | both above + cascade `Dispose` to all views |

---

## 5. Network rules + multiplayer matrix

### 5.1 Authority + RPC table

| Mutation | Authority | RPC pattern | Status |
|----------|-----------|-------------|--------|
| `_isHiring` write | Server | client `TryOpenHiring`/`TryCloseHiring` → existing `[ServerRpc]` → server validates `Owner` → flip | **Unchanged** |
| `_isHiring` read | Everyone | NetworkVariable replication + `OnHiringStateChanged` event | **Unchanged** |
| Panel state (`_instance`, `_spawned`, `_activeEntry`) | Per-peer (client-only) | None — pure UI | New, but no new wire traffic |

**No new ServerRpcs introduced. No new NetworkVariables. No new ClientRpcs.** The refactor is pure UI re-shaping over an unchanged authoritative state surface (rule #18).

### 5.2 Multiplayer matrix verification (rule #19)

| Scenario | Test | Expected |
|----------|------|----------|
| Host owner opens panel | Host walks to desk, presses E | Panel opens, Hiring tab active, toggle reflects `IsHiring`. |
| Client owner opens panel | Client walks to desk, presses E | Panel opens locally only; UI is client-only. |
| Non-owner peer presses E on desk | Any non-owner | Toast "Only the owner can use this management desk." Panel never opens. |
| Owner toggles hiring (host) | Click pill | `_isHiring` flips on host, replicates to all clients within 1 frame. Tab label updates on every peer that has the panel open. |
| Owner toggles hiring (client) | Client clicks pill | ServerRpc fires, host validates `Owner == caller`, flips, replicates back. Client sees updated label after RTT. |
| ServerRpc spoofing — non-owner-client crafts a `TryOpenHiringServerRpc` | (existing concern) | Host's `ValidateOwnerCaller` rejects (server-authoritative gate is on existing API, not the new panel). |
| Late joiner connects with panel-open building | New client joins, opens panel afterward | NetworkVariable spawn payload carries current `_isHiring`; tab label is correct on first frame. |
| Owner switches characters via portal-gate, panel was open on old character | Owner returns from another map | Toggle click defensively re-resolves local Character → mismatch → `Debug.LogWarning` + bail. (Panel doesn't auto-close because `_building` reference is still valid; user re-opens for the new character.) |
| Building despawns mid-panel-open | Map hibernates / building deleted | `_building` becomes null on local peer; toggle click bails on null check. Panel can be closed normally via ESC/X/dismiss. |

---

## 6. Error handling

Per CLAUDE.md rule #31 — `try/catch` only for runtime-failable boundaries:

| Site | Pattern |
|------|---------|
| `Resources.Load` in `Show` + `CreateView` | `try/catch + Debug.LogException`; null-return path also handled. |
| `NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<Character>()` | wrapped in `try/catch` (existing pattern preserved from `UI_OwnerHiringPanel.ResolveLocalPlayerCharacter`). |
| All other paths (button clicks, tab switching, OnDestroy) | Plain null-checks; no exceptions expected. |

Per rule #27 — debug logs at every branching point:

- `Debug.LogWarning("[UI_OwnerManagementPanel] Show rejected — building null.")`
- `Debug.LogWarning("[UI_OwnerManagementPanel] Show rejected — local character is not the owner.")`
- `Debug.LogWarning("[UI_OwnerManagementPanel] Resources prefab missing at {path}.")`
- `Debug.LogWarning("[HiringTabView] Toggle rejected — could not resolve local player Character.")` (preserved from old code)
- `Debug.LogWarning("[HiringTabView] Building reference is null — was it destroyed?")` (defensive)

These all run on user-driven events (panel open / button click), never on per-frame ticks — no hot-path gating wrapper needed.

---

## 7. Performance (rule #34)

- **Panel open (cold):** 1× `Resources.Load`, 1× `Instantiate` for the panel + 1 per tab + 1 per pill. Allocation acceptable — user-event-driven.
- **Panel re-open (warm, same building):** zero new allocations. `SetActive(true)` only.
- **Tab switch:** zero allocations. SetActive flips + label updates.
- **`GetManagementTabs()`:** allocates a 1-element `IManagementTab[]` per call — only on cold open. If future profiling shows frequent reopens, switch to cached `IReadOnlyList` field; not justified today.
- **Per-frame:** `UI_OwnerManagementPanel.Update` reads `Input.GetKeyDown(Escape)` only when active (matches today's pattern). `HiringTabView` has no `Update`. Zero per-frame allocations.

---

## 8. Testing

### 8.1 Manual (Unity Play mode, multiplayer matrix)

1. Host-as-owner open/close panel + toggle hiring; verify replication on a connected client.
2. Client-as-owner open/close panel + toggle hiring; verify replication on host + other clients.
3. Non-owner peer attempts to use desk → toast + bail.
4. Two characters owning two buildings — switching between them rebuilds tabs (different-building re-Show path).
5. Late-joiner sees correct hiring state on first panel open.
6. ESC / X / dismiss-overlay all close the panel.

### 8.2 Edit-mode tests (NUnit) — small surface

- `HiringTab.Name == "Hiring"`.
- `CommercialBuilding.GetManagementTabs()` returns 1 element of type `HiringTab` for an unsubclassed concrete dummy. Verifies the virtual contract.
- `HiringTab.CreateView()` returns null when prefab is absent (verifies graceful handling).

If `Assets/Tests/EditMode/` doesn't yet wire `MWI.UI.Management` for testability, leave tests as a follow-up rather than block this PR.

### 8.3 No PlayMode tests

Panel is too UI-coupled to test cleanly without scenes. Manual matrix above covers it.

---

## 9. Backward compatibility

- `UI_OwnerHiringPanel.cs` deleted, prefab decommissioned. **Two call sites** (`ManagementFurniture`, `CharacterJob`) updated atomically in the same commit as the deletion → no transient broken state.
- No save/load schema change. Existing `BuildingSaveData` for hiring state is unchanged.
- **Phase 2b parallel session coordination:** their `ShopBuilding.GetManagementTabs()` (when they add it) inherits `[ HiringTab ]` from base + appends their own. Coordination via the shared base virtual = zero merge surface on `CommercialBuilding.cs` (their commits add `_sellShelves`/`_cashiers`/RPCs; ours adds `GetManagementTabs()`). Both land cleanly.

---

## 10. Wiki + SKILL update plan (rules #28 / #29b)

| Doc | Change |
|------|--------|
| `wiki/systems/help-wanted-and-hiring.md` | Update `Key classes / files` table to reflect deletion of `UI_OwnerHiringPanel.cs` + prefab + addition of `UI_OwnerManagementPanel` / `HiringTab` / `HiringTabView`. Update `Public API / entry points` (replace `UI_OwnerHiringPanel.Show` with `UI_OwnerManagementPanel.Show`). Add change-log entry. |
| `wiki/systems/commercial-building.md` | Add `GetManagementTabs()` virtual to public API. Add change-log entry. |
| `wiki/systems/management-panel-architecture.md` (NEW) | New page describing the polymorphic tab system — purpose, architecture, how subtypes extend, future tabs (per-job hiring + Storage tab) referenced as deferred. |
| `.agent/skills/help-wanted-and-hiring/SKILL.md` | Update entry-point / API reference section. |
| `.agent/skills/management-panel/SKILL.md` (NEW) | Procedural how-to for adding a new tab to a CommercialBuilding subtype. |

---

## 11. Risks + mitigations

| Risk | Mitigation |
|------|-----------|
| Phase 2b parallel session re-bases on top of this refactor | Brief explicitly notes: their `ShopBuilding.GetManagementTabs()` will use base + appends. Brief's "out-of-scope guard rails" already establishes coordination. |
| Players miss the dropped sign-edit feature mid-flight (between this refactor landing and the future sign-furniture rework) | Sign auto-format on `_isHiring` toggle still works (server-authoritative `HandleHiringStateChanged` is unchanged). Owners simply can't customize text via the panel; future sign-furniture rework restores it via the sign's own UI. Acceptable transient gap. |
| Defense-in-depth owner gate in panel masks bugs in upstream gate | The defense-in-depth only Debug.LogWarnings; never silently rewrites authoritative state. Bugs in upstream call sites still surface as toast-missing on the user side. |
| Resources prefab path typo | Both prefabs (`Resources/UI/UI_OwnerManagementPanel`, `Resources/UI/Management/HiringTab`) are accessed via `const string` paths — easily greppable. Build-time verification deferred (no asset-validation pipeline today). |
| `GetManagementTabs()` allocation on every cold open | Acceptable today (rule #34 — measure first). Profiler hook to add if users open panels in tight loops. |

---

## 12. Open questions / follow-ups

- **Per-job hiring rewrite** — replace `_isHiring` building-wide bool with per-`Job` `IsHiring`. Touches CommercialBuilding data model, `BuildingManager.FindAvailableJob`, `InteractionAskForJob.CanExecute`, `CharacterJob.GetInteractionOptions:603` (Section A "Apply for {title}" loop), `HandleHiringStateChanged` / `GetHelpWantedDisplayText` / `HandleVacancyChanged`. Own design + plan + multiplayer verification. Adding the per-job-toggle UI is then a 5-line change to `HiringTabView`.
- **Universal Storage tab + `StorageRole` taxonomy** — replaces `_toolStorageFurniture` (designer field) + `_sellShelves` (Phase 2b's just-landed list). Touches `JobFarmer`, `CanPunchOut`, cashier consumers, and migrates Phase 2b's data. Own design + plan; coordinate with Phase 2b session-author.
- **Sign-furniture rework** — Help Wanted sign becomes its own readable+editable furniture type (Kevin's plan). Restores sign-text customization that this refactor removes from the management panel.
- **Tab cross-communication** — defer to first subtype that needs it. Phase 2b doesn't.
- **EditMode test scaffolding for `MWI.UI.Management`** — wire only if test coverage gap surfaces.

---

## 13. Sources

- [docs/superpowers/briefs/2026-05-07-commercial-building-management-panel-architecture-brief.md](../briefs/2026-05-07-commercial-building-management-panel-architecture-brief.md) — original brief.
- [wiki/systems/help-wanted-and-hiring.md](../../../wiki/systems/help-wanted-and-hiring.md) — current hiring architecture.
- [wiki/systems/character-job.md](../../../wiki/systems/character-job.md) — job interaction options.
- [wiki/systems/commercial-building.md](../../../wiki/systems/commercial-building.md) — building base class.
- [wiki/systems/storage-furniture.md](../../../wiki/systems/storage-furniture.md) — referenced for future Storage-tab phase.
- [wiki/systems/tool-storage.md](../../../wiki/systems/tool-storage.md) — referenced for future Storage-tab phase.
- `Assets/Scripts/UI/PlayerHUD/UI_OwnerHiringPanel.cs` — current implementation.
- `Assets/Scripts/World/Buildings/CommercialBuilding.cs` — base class with `_isHiring` + `OnHiringStateChanged` + abstract `InitializeJobs()` pattern to mirror.
- `Assets/Scripts/World/Furniture/ManagementFurniture.cs` — entry point (line 54).
- `Assets/Scripts/Character/CharacterJob/CharacterJob.cs` — fallback entry point (line 678).
- 2026-05-07 brainstorming conversation with Kevin.

---

## 14. Change log

- 2026-05-07 — Initial design. Scope clarified mid-flight (per-job hiring + universal Storage tab + sign-edit removal) and partitioned: this refactor ships the polymorphic foundation + simplified Hiring tab; the deeper data-model changes are deferred to dedicated follow-up phases. — claude
