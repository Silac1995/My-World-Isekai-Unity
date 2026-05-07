# Commercial-building management panel — polymorphic tab architecture

This is a focused refactor brief, NOT an end-to-end feature. The work is small,
prerequisite to a larger feature (Phase 2b shop sell/buy system, separate
session), and independently valuable for every future CommercialBuilding subtype
that wants its own owner-only admin UI.

## Why this matters now

Today `ManagementFurniture.Use(character)` opens `UI_OwnerHiringPanel.Show(building)`
for owners. The hiring panel is a single-purpose UI for picking applicants for
job slots.

Several upcoming features want to add MORE owner-only admin surfaces to
`CommercialBuilding` subtypes:

- `ShopBuilding` (Phase 2b — separate session, unblocked by this work):
  Catalog tab (items + prices), Shelves tab (which storages are sell-shelves),
  Cashiers tab (per-cashier till + Withdraw to wallet → future Treasury).
- `CraftingBuilding` (future): Recipes tab, Input-Materials tab, Quality-tier tab.
- `HarvestingBuilding` (future): Harvest-Rules tab, Yield-Storage tab.
- `FarmingBuilding` (future): Crop-Plan tab, Tool-Storage tab.
- A future `BankBuilding` / `TavernBuilding` / etc.

The naive approach — type-discriminated branching inside the panel — violates
rule #10 ("add features via interfaces and abstract classes, never by modifying
existing logic") and forces every new subtype to come back and edit a central
`UI_OwnerHiringPanel.BuildTabs()` `if (building is X)` chain.

The right pattern matches the project's existing convention. `CommercialBuilding`
already exposes a `virtual InitializeJobs()` that subtypes override to declare
their job slots — `ShopBuilding` adds JobVendor + JobLogisticsManager,
`CraftingBuilding` adds JobCrafter + JobLogisticsManager, etc. The base panel
never knows the difference. **Tabs should follow the same pattern.**

## Goal

Refactor the existing `UI_OwnerHiringPanel` into a generic tabbed shell
(`UI_OwnerManagementPanel`) driven by a virtual method on `CommercialBuilding`.
Every commercial building gets at minimum a Hiring tab (matching today's
behavior bit-for-bit). Subtypes append their own tabs by overriding the virtual
method.

After the refactor lands, adding a new subtype's admin UI is a one-method
override on the building class plus one new tab view class — **no edits to
`UI_OwnerManagementPanel`**.

## Target API

```csharp
// In UI namespace (e.g., Assets/Scripts/UI/Management/IManagementTab.cs)
public interface IManagementTab
{
    string Name { get; }
    IManagementTabView CreateView();
}

public interface IManagementTabView
{
    GameObject Root { get; }   // or similar — the prefab/instance the panel parents
    void OnTabActivated();     // when the user clicks the tab
    void OnTabDeactivated();   // when the user switches away
    void Dispose();            // unsubscribe events, free resources (rule #16)
}

// On CommercialBuilding base
public virtual IReadOnlyList<IManagementTab> GetManagementTabs()
{
    return new IManagementTab[] { new HiringTab(this) };
}

// Subtype override pattern (example for a future subtype — DO NOT ship in this
// session; only the base + Hiring extraction ships here)
public override IReadOnlyList<IManagementTab> GetManagementTabs()
{
    var tabs = new List<IManagementTab>(base.GetManagementTabs());
    tabs.Add(new RecipesTab(this));
    return tabs;
}

// Generic panel
public class UI_OwnerManagementPanel : MonoBehaviour
{
    public static void Show(CommercialBuilding building) { … }

    private void BuildTabs(CommercialBuilding building)
    {
        foreach (var tab in building.GetManagementTabs())
            AddTab(tab.Name, tab.CreateView());
    }
}
```

## Scope IN

1. New interfaces `IManagementTab` + `IManagementTabView` (UI namespace).
2. New `virtual IReadOnlyList<IManagementTab> GetManagementTabs()` on
   `CommercialBuilding` base, returning a single Hiring tab.
3. New generic `UI_OwnerManagementPanel` (replaces `UI_OwnerHiringPanel` as the
   entry point). Tabbed shell + tab body container + tab header buttons.
4. Extract the existing hiring UI into a `HiringTab : IManagementTab` +
   `HiringTabView : IManagementTabView`. Behavior must match
   `UI_OwnerHiringPanel` bit-for-bit — same applicant list, same Hire button,
   same network paths, same notifications.
5. Update `ManagementFurniture.Use(character)` to call
   `UI_OwnerManagementPanel.Show(building)` instead of
   `UI_OwnerHiringPanel.Show(building)`.
6. `UI_OwnerHiringPanel` either deleted or kept as a thin redirect to
   `UI_OwnerManagementPanel.Show(building)` for backward compatibility — author
   to decide based on call-site count.
7. Owner-only gate preserved (existing `building.Owner != character` check in
   `ManagementFurniture` keeps working — the new panel inherits this gate).
8. Updated `wiki/systems/` page for management UI (rule #29b).
9. Updated `.agent/skills/` skill files for any modified system (rule #28).

## Scope OUT

- Shop-specific tabs (Catalog / Shelves / Cashiers) — **Phase 2b owns these**.
  Do NOT ship them in this session.
- Crafting / Harvesting / Farming tabs — separate per-subtype future work.
- Cross-tab communication channel ("tab A wants tab B to refresh") — defer to
  the first subtype that actually needs it. Phase 2b doesn't.
- New owner-only ServerRpc patterns — Phase 2b will introduce shop-specific
  ones; this refactor preserves whatever owner-gating already exists in hiring.
- Treasury — separate session.

## Existing infrastructure to leverage

- `Assets/Scripts/World/Furniture/ManagementFurniture.cs` — owner desk +
  toast on non-owner. The `Use(character)` method is the entry point.
- `Assets/Scripts/UI/UI_OwnerHiringPanel.cs` — current single-purpose UI to be
  extracted into a tab.
- `Assets/Scripts/World/Buildings/CommercialBuilding.cs` — base class. Already
  has `_jobs` + `virtual InitializeJobs()` patterns to mirror.
- `MWI.UI.Notifications.UI_Toast.Show(...)` — toast API for any user-facing
  warnings (e.g., not-owner rejection).

## Network considerations

The panel itself is client-only UI. **All mutations driven from tabs go through
ServerRpc**, exactly as the existing hiring flow does today. The refactor must
preserve this — no panel code talks to authoritative state directly. Each tab
view is responsible for its own ServerRpc calls + replicated-state subscriptions.

Multiplayer scenarios to verify (rule #19):
- Host opens panel as owner → all tabs render, hiring functions.
- Client opens panel as owner → all tabs render, hiring functions via ServerRpc.
- Non-owner (any peer) tries to open panel → `ManagementFurniture.Use` shows
  the existing "Only the owner can use this management desk" toast and bails
  before the panel opens. Behavior identical to today.
- Owner switches characters mid-session (portal-gate return) → panel closes
  cleanly if the previous character was the owner; new character opens fresh
  if they're the new owner.

## Specialists to pull in

- `building-furniture-specialist` — `CommercialBuilding` base method
  placement, `ManagementFurniture.Use` rewire, subtype-friendly virtual.
- `network-validator` — verify owner-gate carries through after refactor;
  no new net traffic introduced; existing hiring ServerRpcs unaffected.

## Workflow

1. `superpowers:brainstorming` — gather context, present design, commit spec
   to `docs/superpowers/specs/2026-05-XX-management-panel-tab-architecture-design.md`.
2. `superpowers:writing-plans` — wave-based implementation plan. Likely
   small: ~3-5 tasks (interfaces → base virtual → generic panel → hiring
   extract → entry-point rewire → tests).
3. Execute via the specialists above.

## Out-of-scope guard rails

If the parallel session is tempted to add shop tabs while it's in there —
**don't**. Phase 2b owns those. The parallel session's PR should leave
`ShopBuilding.GetManagementTabs()` returning `base.GetManagementTabs()` (i.e.,
just Hiring) until Phase 2b lands its `ShopCatalogTab` / `ShopShelvesTab` /
`ShopCashiersTab` types.

This guarantees the parallel session and Phase 2b can both ship without merge
conflicts on `ShopBuilding.cs`.

## Project rules — read first

CLAUDE.md at project root, 35 mandatory rules. Critical for this work:
- #10 (Open/Closed — features via interfaces/abstract classes, never by
  modifying existing logic).
- #14 (Dependency Injection — use existing abstractions).
- #16 (Unsubscribe events / clean up coroutines in OnDestroy / Dispose).
- #18 / #19 (server authority, multiplayer matrix).
- #28 (SKILL.md must be updated for any modified system).
- #29b (wiki/systems/ pages must be updated for architectural changes).
- #34 (no per-frame allocations; tab views should not poll).
