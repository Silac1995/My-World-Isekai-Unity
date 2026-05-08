---
name: Management Panel — Adding a New Tab
description: How to add an owner-only admin tab to a CommercialBuilding subtype.
when_to_use: When implementing a new feature that needs an owner-facing management UI on a building (e.g., a recipe editor for CraftingBuilding, a crop-plan editor for FarmingBuilding, a catalog editor for ShopBuilding, etc.).
---

# Adding a New Management Tab

The owner management panel (`UI_OwnerManagementPanel`, namespace `MWI.UI.Management`) is a generic polymorphic shell. Every `CommercialBuilding` subtype gets a built-in `HiringTab` from the base `GetManagementTabs()` virtual; subtypes append their own tabs by overriding the virtual. The panel itself never changes.

Architecture reference: [wiki/systems/management-panel-architecture.md](../../wiki/systems/management-panel-architecture.md).

## Quick steps

1. **Create a `MyFeatureTab : IManagementTab` (plain C# class) in your feature's UI folder.**
   - Constructor takes the `CommercialBuilding` reference.
   - `Name` returns the header pill label (e.g. `"Catalog"`, `"Cashiers"`, `"Crops"`).
   - `CreateView()` does `Resources.Load<MyFeatureTabView>` + `Object.Instantiate` + `view.Bind(_building)` and returns the view.
   - Plain C# class — **no Unity lifecycle**. The view is the MonoBehaviour.

   ```csharp
   namespace MWI.UI.Management
   {
       public sealed class MyFeatureTab : IManagementTab
       {
           private readonly CommercialBuilding _building;
           public MyFeatureTab(CommercialBuilding b) { _building = b; }
           public string Name => "MyFeature";
           public IManagementTabView CreateView()
           {
               const string path = "UI/Management/MyFeatureTab";
               var prefab = Resources.Load<MyFeatureTabView>(path);
               if (prefab == null) { Debug.LogWarning($"[MyFeatureTab] Missing prefab Resources/{path}"); return null; }
               var view = Object.Instantiate(prefab);
               view.Bind(_building);
               return view;
           }
       }
   }
   ```

2. **Create a `MyFeatureTabView : MonoBehaviour, IManagementTabView`.**
   - `Root => gameObject`.
   - `Bind(CommercialBuilding b)` subscribes to the building's events and stores the reference.
   - `OnTabActivated()` / `OnTabDeactivated()` — usually no-ops if the view stays subscribed while bound. **Must be idempotent** — the panel re-fires `OnTabActivated()` on warm-path same-building re-Show.
   - `Dispose()` MUST unsubscribe from all events (rule #16) and `Destroy(gameObject)`.
   - All ServerRpcs go through the building's existing API. **NEVER add a panel-side ServerRpc.**

   ```csharp
   namespace MWI.UI.Management
   {
       public sealed class MyFeatureTabView : MonoBehaviour, IManagementTabView
       {
           [SerializeField] private Button _someButton;
           private CommercialBuilding _building;

           public GameObject Root => gameObject;

           public void Bind(CommercialBuilding b)
           {
               _building = b;
               _someButton.onClick.AddListener(HandleClick);
               // _building.OnSomeEvent += HandleEvent;
               Refresh();
           }

           public void OnTabActivated()   { /* no-op for live-while-bound views */ }
           public void OnTabDeactivated() { /* no-op */ }

           public void Dispose()
           {
               if (_someButton != null) _someButton.onClick.RemoveListener(HandleClick);
               // if (_building != null) _building.OnSomeEvent -= HandleEvent;
               _building = null;
               if (this != null) Destroy(gameObject);
           }

           private void HandleClick() { /* call _building.SomeServerAction(localCharacter) */ }
           private void Refresh()    { /* update labels from _building state */ }
       }
   }
   ```

3. **Build a `Resources/UI/Management/MyFeatureTab.prefab`** with the controls + a `MyFeatureTabView` component on the root GameObject. Wire the `[SerializeField]` references in the Inspector.

4. **In your subtype building, override `GetManagementTabs()`:**

   ```csharp
   public override IReadOnlyList<IManagementTab> GetManagementTabs()
   {
       var tabs = new List<IManagementTab>(base.GetManagementTabs());
       tabs.Add(new MyFeatureTab(this));
       return tabs;
   }
   ```

   **ALWAYS call `base.GetManagementTabs()` first** so the Hiring tab is preserved. Forgetting drops it silently — there is no compiler enforcement.

## Things to NOT do

- **Do NOT modify `UI_OwnerManagementPanel`** for your tab. The whole point of the polymorphic design is that the panel never knows your concrete tab type. If you find yourself wanting to edit the panel, rethink the approach — the contract is `IManagementTab` + `IManagementTabView`.
- **Do NOT add owner-gate logic in the tab.** The panel's defense-in-depth gate + the call sites' authoritative gate (`ManagementFurniture.Use` toast, `CharacterJob.GetInteractionOptions` ownership condition) already cover it.
- **Do NOT cache `Character` references across tab activations.** Re-resolve via `NetworkManager.LocalClient.PlayerObject` on each user-driven action — the local character can change across portal-gate / map transitions.
- **Do NOT add a panel-side ServerRpc.** All authoritative mutations route through the building's existing API (e.g. `_building.TrySetCatalogEntry(localCharacter, ...)`) which itself owns the `[Rpc(SendTo.Server)]` and the owner-validation gate. The panel is pure UI.
- **Do NOT make `OnTabActivated()` non-idempotent.** Warm-path re-Show calls it on the already-active tab. Re-subscribing events here will leak.
- **Do NOT skip `Dispose()` cleanup.** Tab views accumulate event subscriptions across panel re-opens with new buildings if `Dispose` doesn't unsubscribe (rule #16).
- **Do NOT load Resources prefab paths via string literal scattered across the codebase.** Use a `const string PrefabResourcePath` field in the tab class — easily greppable for refactoring.

## Sources

- [wiki/systems/management-panel-architecture.md](../../wiki/systems/management-panel-architecture.md)
- [docs/superpowers/specs/2026-05-07-management-panel-tab-architecture-design.md](../../docs/superpowers/specs/2026-05-07-management-panel-tab-architecture-design.md)
- [docs/superpowers/plans/2026-05-07-management-panel-tab-architecture.md](../../docs/superpowers/plans/2026-05-07-management-panel-tab-architecture.md)
- [.agent/skills/help-wanted-and-hiring/SKILL.md](../help-wanted-and-hiring/SKILL.md) — reference impl: `HiringTab` + `HiringTabView`.
