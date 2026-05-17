---
type: system
title: "Owner Management Panel — Polymorphic Tabs"
tags: [building, ui, owner, management, tabs, tier-2]
created: 2026-05-07
updated: 2026-05-17
sources: []
related:
  - "[[commercial-building]]"
  - "[[help-wanted-and-hiring]]"
  - "[[character-job]]"
status: stable
confidence: high
primary_agent: building-furniture-specialist
secondary_agents:
  - character-system-specialist
owner_code_path: "Assets/Scripts/UI/Management/"
depends_on:
  - "[[commercial-building]]"
depended_on_by: []
---

# Owner Management Panel — Polymorphic Tabs

## Summary

`UI_OwnerManagementPanel` is the generic owner-only admin shell shared by every `CommercialBuilding` subtype. It hosts a header bar of clickable tab pills and a body container that swaps between `IManagementTabView` instances. Tab content is supplied polymorphically by `CommercialBuilding.GetManagementTabs()` (a virtual returning `[HiringTab]` on the base). Subtypes (`ShopBuilding`, `CraftingBuilding`, `FarmingBuilding`, …) override the virtual to append their own admin tabs without modifying the panel itself or the call sites that open it. The `MWI.UI.Management` namespace contains the entire surface: two interfaces (`IManagementTab` spec + `IManagementTabView` view) and one shell (`UI_OwnerManagementPanel`) plus the built-in `HiringTab` / `HiringTabView`.

## Purpose

Before this refactor, `UI_OwnerHiringPanel` mixed three responsibilities — hiring toggle, sign-text editing, and job-list display — into one MonoBehaviour. Adding a Phase 2b shop catalog/shelves/cashiers admin surface, or a future crop-plan editor for `FarmingBuilding`, would have required editing the panel for every subtype (rule #10 violation: Open/Closed Principle). The polymorphic refactor extracts a generic shell driven by a building-supplied tab list, so:

- Subtypes append their own tabs by overriding one virtual.
- The panel never knows about concrete tab types.
- Entry points (`ManagementFurniture.Use`, `CharacterJob.GetInteractionOptions` Section B) remain untouched when new tabs land.
- New tabs ship as plain C# spec class + MonoBehaviour view + Resources prefab — no panel edits, no call-site edits.

## Responsibilities

- Singleton-on-demand instantiation under the main Canvas (`Resources.Load` → `Instantiate` on first `Show`).
- Defense-in-depth owner gate before showing (re-resolves local `Character` via `NetworkManager.LocalClient.PlayerObject`; bails if `building.Owner != localCharacter`).
- Tab list materialisation by calling `building.GetManagementTabs()` on cold open.
- Tab header pill instantiation + click wiring.
- Tab body re-parenting (active tab `View.Root` is `SetActive(true)`; inactive tabs are `SetActive(false)`).
- Lifecycle dispatch: `OnTabActivated` / `OnTabDeactivated` on pill click; cascading `Dispose()` on close.
- Warm-path re-Show optimisation (same building → no rebuild, just `SetActive(true)` and re-activate first tab).
- ESC / Close-button / dismiss-overlay routing.

**Non-responsibilities:**

- **Does not** own any tab's behaviour or state. Each tab is a black box from the panel's perspective.
- **Does not** own ServerRpcs / NetworkVariables. All authoritative mutations route through the building's own API.
- **Does not** authorise actions. Owner-gate is defense-in-depth; the call-site gate (`ManagementFurniture.Use` toast + `CharacterJob.GetInteractionOptions` `OwnedBuilding != null`) is the authoritative guard.
- **Does not** persist anything. Pure UI state, per-peer.

## Key classes / files

| File | Role |
|------|------|
| [Assets/Scripts/UI/Management/IManagementTab.cs](../../Assets/Scripts/UI/Management/IManagementTab.cs) | Spec interface — `Name` (header pill label) + `CreateView()` factory. Plain C# class, no Unity lifecycle. |
| [Assets/Scripts/UI/Management/IManagementTabView.cs](../../Assets/Scripts/UI/Management/IManagementTabView.cs) | View interface — `Root` (GameObject) + `OnTabActivated` / `OnTabDeactivated` / `Dispose` lifecycle. |
| [Assets/Scripts/UI/Management/UI_OwnerManagementPanel.cs](../../Assets/Scripts/UI/Management/UI_OwnerManagementPanel.cs) | Generic tabbed shell (singleton). `Show(building)` lazy-instantiates and rebuilds tabs. |
| [Assets/Scripts/UI/Management/HiringTab.cs](../../Assets/Scripts/UI/Management/HiringTab.cs) | Built-in `IManagementTab` for every `CommercialBuilding` — base virtual returns `[HiringTab(this)]`. |
| [Assets/Scripts/UI/Management/HiringTabView.cs](../../Assets/Scripts/UI/Management/HiringTabView.cs) | `IManagementTabView` MonoBehaviour — toggle button + label, subscribes to `OnHiringStateChanged`. |
| [Assets/Resources/UI/UI_OwnerManagementPanel.prefab](../../Assets/Resources/UI/UI_OwnerManagementPanel.prefab) | Singleton-on-demand panel prefab — title, tab header bar, body container, close + overlay. |
| [Assets/Resources/UI/Management/HiringTab.prefab](../../Assets/Resources/UI/Management/HiringTab.prefab) | Hiring tab body — toggle button + label. Minimal by design. |
| [Assets/Resources/UI/Management/TabHeaderPill.prefab](../../Assets/Resources/UI/Management/TabHeaderPill.prefab) | Tab header pill — toggle-able button + label, instantiated once per tab. |

**Modified files (this refactor):**

| File | Change |
|------|--------|
| [Assets/Scripts/World/Buildings/CommercialBuilding.cs](../../Assets/Scripts/World/Buildings/CommercialBuilding.cs) | Added `public virtual IReadOnlyList<MWI.UI.Management.IManagementTab> GetManagementTabs()` returning `[HiringTab(this)]`. No new fields. |
| [Assets/Scripts/World/Furniture/ManagementFurniture.cs](../../Assets/Scripts/World/Furniture/ManagementFurniture.cs) | `UI_OwnerHiringPanel.Show(building)` → `UI_OwnerManagementPanel.Show(building)`. |
| [Assets/Scripts/Character/CharacterJob/CharacterJob.cs](../../Assets/Scripts/Character/CharacterJob/CharacterJob.cs) | Same call swap; menu entry label `"Manage Hiring..."` → `"Manage..."` (now generic). |

**Deleted files (this refactor):**

| File | Reason |
|------|--------|
| `Assets/Scripts/UI/PlayerHUD/UI_OwnerHiringPanel.cs` | Replaced by `UI_OwnerManagementPanel` + `HiringTabView`. |
| `Assets/Resources/UI/UI_OwnerHiringPanel.prefab` | Decommissioned; split between the two new prefabs. |

## Public API / entry points

- `MWI.UI.Management.UI_OwnerManagementPanel.Show(CommercialBuilding building)` — singleton-on-demand entry. Lazy-instantiates the prefab on first call; rebuilds tabs only when called with a different building.
- `CommercialBuilding.GetManagementTabs()` → `IReadOnlyList<IManagementTab>` (virtual). Returns `[HiringTab(this)]` on the base. Subtypes override and call `base.GetManagementTabs()` first.
- `IManagementTab.Name { get; }` — header pill label (e.g. `"Hiring"`).
- `IManagementTab.CreateView()` → `IManagementTabView` — factory; typically `Resources.Load` + `Instantiate` + `view.Bind(_building)`.
- `IManagementTabView.Root { get; }` — `GameObject` the panel re-parents under its body container.
- `IManagementTabView.OnTabActivated()` / `OnTabDeactivated()` / `Dispose()` — lifecycle hooks.

**Call sites (entry points):**

- `ManagementFurniture.Use(character)` — owner walks to in-world desk, presses E. Authoritative owner-gate + toast for non-owners. → `UI_OwnerManagementPanel.Show(building)`.
- `CharacterJob.GetInteractionOptions` Section B — fallback "Manage..." menu entry when interactor has `OwnedBuilding != null` and the building has no `ManagementFurniture` wired. → `UI_OwnerManagementPanel.Show(building)`.

## Data flow

### Cold open (first `Show` for a given building)

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
                │     pillLabel = pill.GetComponentInChildren<TextMeshProUGUI>()
                │     pillLabel.text = tab.Name
                │     pillButton = pill.GetComponent<Button>()
                │     pillButton.onClick += () => SwitchTo(tabIndex)
                │     _spawned.Add({ View = view, Pill = pill, PillButton = pillButton })
                ├─ SwitchTo(0)                                // activate first tab
                └─ gameObject.SetActive(true)
```

### Switch tab

```
SwitchTo(targetIndex)
        ├─ if (targetIndex out of range) return                    // bounds check
        ├─ if (_activeIndex == targetIndex) return                 // same tab — no-op
        ├─ if (_activeIndex >= 0 && _activeIndex < _spawned.Count):
        │     prev = _spawned[_activeIndex]
        │     prev.View.OnTabDeactivated()
        │     prev.View.Root.SetActive(false)
        │     SetPillSelected(prev.Pill, false)
        ├─ _activeIndex = targetIndex
        ├─ next = _spawned[targetIndex]
        ├─ next.View.Root.SetActive(true)
        ├─ next.View.OnTabActivated()
        └─ SetPillSelected(next.Pill, true)
```

### Re-Show (warm)

- **Same building:** skip rebuild. Re-activate first tab if hidden. `gameObject.SetActive(true)`. Note: `SwitchTo(0)` re-fires `OnTabActivated()` on the already-active tab (idempotence required — see Gotchas).
- **Different building:** for each `_spawned` entry, `View.Dispose()` → destroy pill GO. Clear list. Rebuild as cold path.

### Hiring toggle (preserved bit-for-bit from old panel)

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

**No new ServerRpcs introduced. No new NetworkVariables. No new ClientRpcs.** Pure UI re-shaping over the unchanged authoritative `_isHiring` surface (rule #18).

### Close / OnDestroy

Close → for each `_spawned`: `View.Dispose()` (unsubscribes + `Destroy(gameObject)`) → destroy pill GameObjects → clear list → `gameObject.SetActive(false)`. `_instance` stays around as the lazy singleton.

OnDestroy → `_instance = null` (when applicable) → cascade `Dispose` to remaining views → remove `_closeButton` / `_dismissOverlay` listeners (rule #16).

## Dependencies

### Upstream (this system needs)

- [[commercial-building]] — owns `GetManagementTabs()` virtual + `Owner` reference for defense-in-depth gate.
- [[help-wanted-and-hiring]] — provides `_isHiring` NetworkVariable + `OnHiringStateChanged` event consumed by `HiringTabView`; also documents `ManagementFurniture` (the in-world desk that opens this panel).
- [[character-job]] — fallback entry point (`GetInteractionOptions` Section B "Manage...").

### Downstream (systems that need this)

- (Phase 2b — parallel session) `ShopBuilding.GetManagementTabs()` will append Catalog / Shelves / Cashiers tabs.
- (Phase 2 — Per-job hiring) Will rewrite `HiringTabView` body to render per-`Job` toggles instead of building-wide toggle.
- (Phase 2 — Universal Storage tab) Will land as a base-class tab once `StorageRole` taxonomy ships.

## State & persistence

- **No persisted state.** Pure UI shell.
- **Runtime per-peer state:**
  - `_instance: UI_OwnerManagementPanel` — static lazy singleton.
  - `_spawned: List<Entry>` — runtime list of constructed tab entries (capacity 4). Cleared on close-and-different-building re-Show.
  - `Entry { IManagementTabView View, GameObject Pill, Button PillButton }` — per-tab tuple. The pill's label is read inline via `GetComponentInChildren<TextMeshProUGUI>` during construction; not stored on `Entry`.
  - `_activeIndex: int` — index into `_spawned`; `-1` means no active tab.
  - `_building: CommercialBuilding` — building this panel is currently bound to. Cleared on close.
- **No NetworkVariable, no NetworkBehaviour.** Plain MonoBehaviour. The panel never replicates; everything authoritative is on the building.

## Network rules + multiplayer matrix

| Mutation | Authority | RPC pattern | Status |
|----------|-----------|-------------|--------|
| `_isHiring` write | Server | client `TryOpenHiring`/`TryCloseHiring` → existing `[ServerRpc]` → server validates `Owner` → flip | **Unchanged** from pre-refactor |
| `_isHiring` read | Everyone | NetworkVariable replication + `OnHiringStateChanged` event | **Unchanged** |
| Panel state (`_instance`, `_spawned`, `_activeIndex`) | Per-peer (client-only) | None — pure UI | New, no wire traffic |

**No new ServerRpcs introduced. No new NetworkVariables.** The refactor is pure UI re-shaping (rule #18).

**Multiplayer matrix verification (rule #19):**

| Scenario | Test | Expected |
|----------|------|----------|
| Host owner opens panel | Host walks to desk, presses E | Panel opens, Hiring tab active, toggle reflects `IsHiring`. |
| Client owner opens panel | Client walks to desk, presses E | Panel opens locally only; UI is client-only. |
| Non-owner peer presses E on desk | Any non-owner | Toast "Only the owner can use this management desk." Panel never opens. |
| Owner toggles hiring (host) | Click toggle | `_isHiring` flips on host, replicates to all clients within 1 frame. Tab label updates on every peer with the panel open. |
| Owner toggles hiring (client) | Client clicks toggle | ServerRpc fires, host validates `Owner == caller`, flips, replicates back. Client sees updated label after RTT. |
| ServerRpc spoofing — non-owner-client crafts a `TryOpenHiringServerRpc` | (existing concern) | Host's `ValidateOwnerCaller` rejects (server-authoritative gate is on existing API, not the new panel). |
| Late joiner connects, opens panel | New client joins, opens panel afterward | NetworkVariable spawn payload carries current `_isHiring`; tab label is correct on first frame. |
| Owner switches characters via portal-gate, panel was open on old character | Owner returns from another map | Toggle click defensively re-resolves local Character → mismatch → `Debug.LogWarning` + bail. (Panel doesn't auto-close because `_building` reference is still valid; user re-opens for the new character.) |
| Building despawns mid-panel-open | Map hibernates / building deleted | `_building` becomes null on local peer; toggle click bails on null check. Panel can be closed normally via ESC/X/dismiss. |

## Known gotchas / edge cases

- **`FindFirstObjectByType<Canvas>()` fragility in multi-canvas scenes** — pre-existing pattern from the legacy `UI_OwnerHiringPanel`. The panel parents itself under the first Canvas it finds; if the scene has multiple canvases (e.g. world-space overlay + screen-space main HUD), the panel may attach to the wrong one. Acceptable in current single-canvas main HUD layout; flagged as known limitation. See Open questions for a hardening item.
- **Warm-path re-Show calls `OnTabActivated()` on the already-active tab** — same-building re-Show triggers `SwitchTo(0)`, which re-fires `OnTabActivated()` even when the tab is already active. Tab views MUST be idempotent on `OnTabActivated()` — no event-resubscription, no allocation, no side-effect-on-state-already-correct logic. Documented in `IManagementTabView` XML doc.
- **Defense-in-depth owner gate masks bugs in upstream gate** — the panel's `LogWarning` + bail behaviour never silently rewrites authoritative state, but it can hide a missing toast / missing call-site check. Bugs in upstream call sites still surface as "user clicked but panel never appeared" on the user side.
- **`GetManagementTabs()` allocates a per-call `IManagementTab[]`** — acceptable today (rule #34: panels open user-driven, not per-frame). If profiling shows frequent reopens, switch to a cached `IReadOnlyList` field rebuilt only when the tab list changes.
- **Subtype overrides MUST call `base.GetManagementTabs()`** — forgetting drops the Hiring tab silently. No compiler enforcement; relies on convention. The skill file flags this in the "Things to NOT do" section.
- **Tab `Dispose()` MUST unsubscribe (rule #16) before destroying its GameObject** — otherwise leaked event subscriptions accumulate across panel re-opens with new buildings.

## Open questions / TODO

- **Per-job hiring** (replace `_isHiring` building-wide bool with per-`Job` flag): own design + plan. Touches `BuildingManager.FindAvailableJob`, `InteractionAskForJob.CanExecute`, `CharacterJob.GetInteractionOptions:603` Section A "Apply for {title}" loop, sign auto-format. Adding the per-job-toggle UI is then a 5-line change to `HiringTabView`.
- **Universal Storage tab + StorageRole taxonomy** (replaces `_toolStorageFurniture` + Phase 2b's `_sellShelves`): own design + plan. Touches [[tool-storage]], `JobFarmer`, `CanPunchOut`, cashier consumers, and migrates Phase 2b's data. Coordinate with Phase 2b session-author.
- **Sign-furniture rework** (sign editing removed from panel): own design. Help Wanted sign becomes its own readable+editable furniture type — restores sign-text customization that this refactor removed from the management panel.
- **Canvas resolution hardening** (`FindFirstObjectByType` is fragile in multi-canvas scenes) — currently inherited from the legacy panel. Future hardening: cache a designer-set Canvas reference on a singleton `UIRoot` MonoBehaviour, fall back to current behaviour.
- **EditMode test scaffolding for `MWI.UI.Management`** — deferred per spec §8.2. Wire only if test coverage gap surfaces. Targets: `HiringTab.Name == "Hiring"`; `CommercialBuilding.GetManagementTabs()` returns 1 element of type `HiringTab` for an unsubclassed dummy; `HiringTab.CreateView()` returns null when prefab is absent.
- **Tab cross-communication** — defer to first subtype that needs it. Phase 2b doesn't.

## Change log

- 2026-05-17 — **Multi-owner gate fix.** `ManagementFurniture.OnInteract` (toast "Only the owner can use this management desk") and `UI_OwnerManagementPanel.ShowInternal` (defense-in-depth gate) both compared against `building.Owner` — the singular getter that returns only `_ownerIds[0]`. A secondary owner added via `CommercialBuilding.AddOwner` (the canonical multi-owner path — exposed by the dev console's `[DEV] Add Owner` button and reserved for future co-ownership flows) was incorrectly rejected by both gates. Fixed both call sites to route through `Room.IsOwner(Character)`, which compares against the full replicated `_ownerIds` `NetworkList<FixedString64Bytes>`. Network-safe: `_ownerIds` is server-write / everyone-read, late-joiners receive the full list on subscribe. Same multi-owner predicate the building's internal authority check (`CommercialBuilding.CanRequester...`) and `ResidentialBuilding.IsOwner` already use — this fix brings the furniture/panel auth into alignment with that pattern. — claude
- 2026-05-07 — Initial documentation of the polymorphic tab system. Foundation only; per-job hiring + universal Storage tab deferred to dedicated phases. — claude

## Sources

- [docs/superpowers/specs/2026-05-07-management-panel-tab-architecture-design.md](../../docs/superpowers/specs/2026-05-07-management-panel-tab-architecture-design.md)
- [docs/superpowers/plans/2026-05-07-management-panel-tab-architecture.md](../../docs/superpowers/plans/2026-05-07-management-panel-tab-architecture.md)
- [.agent/skills/management-panel/SKILL.md](../../.agent/skills/management-panel/SKILL.md)
- [Assets/Scripts/UI/Management/UI_OwnerManagementPanel.cs](../../Assets/Scripts/UI/Management/UI_OwnerManagementPanel.cs)
- [Assets/Scripts/UI/Management/IManagementTab.cs](../../Assets/Scripts/UI/Management/IManagementTab.cs)
- [Assets/Scripts/UI/Management/IManagementTabView.cs](../../Assets/Scripts/UI/Management/IManagementTabView.cs)
- [Assets/Scripts/UI/Management/HiringTab.cs](../../Assets/Scripts/UI/Management/HiringTab.cs)
- [Assets/Scripts/UI/Management/HiringTabView.cs](../../Assets/Scripts/UI/Management/HiringTabView.cs)
- [Assets/Scripts/World/Buildings/CommercialBuilding.cs](../../Assets/Scripts/World/Buildings/CommercialBuilding.cs)
- 2026-05-07 brainstorming + planning conversation with [[kevin]]
