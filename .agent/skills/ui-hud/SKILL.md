---
name: ui-hud
description: Procedural skill for authoring, modifying, and debugging any closable UI window (full-screen panel, modal, side drawer, popup, confirmation dialog, sub-window, …) following the Prefab-Variant-of-UI_WindowBase convention and the PlayerUI singleton façade.
---

# UI HUD

This skill captures the procedural recipe for adding or modifying any closable UI window. It enforces the architecture defined in [wiki/systems/player-hud.md](../../wiki/systems/player-hud.md) and rule #39 of CLAUDE.md.

The HUD has ONE base prefab — [Assets/UI/Player HUD/UI_WindowBase.prefab](../../Assets/UI/Player%20HUD/UI_WindowBase.prefab) — and **every closable UI window** (regardless of size, modality, or whether it's a top-level panel or a sub-window opened from inside another) is a Prefab Variant of it. The defining trait is **"has a close affordance"** — if the user can dismiss it, it's a window. The singleton façade is [PlayerUI](../../Assets/Scripts/UI/PlayerUI.cs); gameplay code never instantiates windows directly, never holds prefab references, never SetActive's a window, and parent windows never hold direct refs to sub-windows — every closable surface routes through `PlayerUI.Instance.Open<Name>Window(...)`.

## When to use this skill

- Creating any new closable UI surface — top-level panel (deposit / inventory / dialogue / quest log / shop / craft) OR a sub-window opened from inside an existing window (confirmation dialog, item detail popup, settings sub-pane, …).
- Modifying an existing window's content (adding rows, fields, buttons, sub-window triggers).
- Debugging "I tapped E / clicked X and nothing happened" symptoms.
- Reviewing a PR that touches `PlayerUI.cs`, `UI_WindowBase.*`, or any `UI_*` script under `Assets/Scripts/UI/`.
- Authoring a window programmatically via MCP `script-execute` (the 2026-05-16 SafeFurniture scaffold is the canonical example).
- Deciding whether a new UI prefab counts as a "window" (subject to rule #39) or a "leaf" (exempt). **Litmus test**: does the element have a Button that calls `CloseWindow` / `SetActive(false)` to dismiss itself? If yes → window, must be a Variant. If it disappears only when its parent closes or via a timer → leaf, plain MonoBehaviour prefab.

## The Player HUD Architecture

```
+-----------------------------+
|   PlayerUI (singleton)      |   <-- scene-resident, lives on UI_PlayerHUD GameObject
|   - [SF] _storagePanel      |
|   - [SF] _safePanel         |
|   - [SF] _shopBuyPanel      |
|   - OpenStoragePanel(...)   |
|   - OpenSafePanel(...)      |
|   - OpenShopBuyPanel(...)   |
+--------------+--------------+
               |
               | children (scene-resident, SetActive(false) by default)
               v
+--------------+--------------+
|  UI_StoragePanel  : UI_WindowBase  -> variant of UI_WindowBase.prefab
|  UI_SafePanel     : UI_WindowBase  -> variant of UI_WindowBase.prefab
|  UI_ShopBuyPanel  : UI_WindowBase  -> variant of UI_WindowBase.prefab
+-----------------------------+
               ^
               | inherits chrome
               |
+--------------+--------------+
|  UI_WindowBase.prefab       |
|  (Canvas + Background +     |
|   GraphicRaycaster +        |
|   CanvasScaler + _btnClose) |
+-----------------------------+
```

### 1. Singleton façade — PlayerUI

`PlayerUI.Instance` is the only entry-point for opening any closable window.

**Rule:** Never call `window.gameObject.SetActive(true)` or `window.OpenWindow()` from gameplay code or from a parent window. Always go through `PlayerUI.Instance.Open<Name>Window(...)`. This keeps the wiring discoverable, the warning-log diagnostic centralised, the z-order managed in one place, and the façade flat (sub-windows are siblings under PlayerUI, never grand-children of another window).

For every new closable window:
- Add `[SerializeField] private MWI.UI.<Category>.UI_<Name>Window _<name>Window;` to `PlayerUI`.
- Add `public void Open<Name>Window(<contextArgs>)` and `public void Close<Name>Window()`.
- Inside `Open<Name>Window`, **always** include a null-guard with a directive warning:
  ```csharp
  if (_<name>Window == null)
  {
      Debug.LogWarning("<color=orange>[PlayerUI]</color> Open<Name>Window called but _<name>Window SerializeField is null — author the prefab (variant of UI_WindowBase.prefab) and wire it to PlayerUI._<name>Window in the Inspector.");
      return;
  }
  ```
  Without this, a null SerializeField silently breaks the feature with zero diagnostic surface.

### 2. The Variant convention

**Rule:** every closable UI window prefab MUST be a Prefab Variant of `Assets/UI/Player HUD/UI_WindowBase.prefab`. This applies to top-level windows AND to sub-windows opened from inside another window. The window-vs-leaf litmus test is at the top of this skill.

**Asset placement:** `Assets/UI/Player HUD/UI_<Name>Window.prefab`. NOT `Assets/Prefabs/`. NOT `Assets/UI/`. Specifically the `Player HUD` subfolder.

**Backing script:** `public sealed class UI_<Name>Window : UI_WindowBase`. The base auto-wires the inherited `_buttonClose` Button in `Awake` and exposes `OpenWindow` / `CloseWindow`. Your subclass:
- Adds its own `[SerializeField]` content fields.
- Overrides `Awake` only if you need defensive Canvas/GraphicRaycaster auto-provisioning (mirror `UI_StorageFurniturePanel.Awake` for the pattern) — call `base.Awake()` first.
- Provides `Initialize(<contextArgs>)` that wires subscriptions + calls `OpenWindow()`.
- Overrides `CloseWindow()` to unsubscribe + clear state, then calls `base.CloseWindow()` last.
- Adds `OnDisable()` and `OnDestroy()` belt-and-braces unbind (rule #16).
- If this window opens a sub-window, calls `PlayerUI.Instance.Open<SubName>Window(...)` — never holds a direct `[SerializeField]` reference to the sub-window itself.

**Wire the inherited `_buttonClose`** at prefab-authoring time to the variant's close button. The base prefab supplies the field; the variant supplies the Button instance.

### 3. Authoring a new window via MCP (Roslyn `script-execute`)

The canonical example is the 2026-05-16 SafeFurniture scaffold. The full pattern is:

```csharp
// 1. Load base.
var baseWindow = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/UI/Player HUD/UI_WindowBase.prefab");

// 2. Instantiate as variant editing context.
GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(baseWindow);
root.name = "UI_<Name>Window";

// 3. Add the window script as a Variant override.
var t = System.Type.GetType("MWI.UI.<Category>.UI_<Name>Window, Assembly-CSharp", false);
var script = root.AddComponent(t) as MonoBehaviour;

// 4. CRITICAL: fix the inherited Canvas configuration.
//    UI_WindowBase.prefab ships with the Canvas in ScreenSpaceCamera mode, but with
//    RectTransform scale=(0,0,0) — every variant MUST override scale to (1,1,1) or
//    the entire window renders at zero scale (invisible).
//    Don't change renderMode — keep ScreenSpaceCamera. Don't add a CanvasScaler
//    if one's already present from the base.
var canvas = root.GetComponentInChildren<Canvas>(true);
var canvasRt = canvas.GetComponent<RectTransform>();
canvasRt.localScale = Vector3.one;
canvasRt.localRotation = Quaternion.identity;
canvasRt.localPosition = Vector3.zero;
// worldCamera stays null — UI_WindowBase.Awake assigns Camera.main at runtime.

// 5. Add content under the inherited Canvas (canvas.transform), NOT under the root.
//    Parenting content under the variant root would create a second Canvas context
//    and re-trigger the 2026-05-16 "two-Canvas invisible window" bug.
//    Title, close button, row container, status label, ... all under canvas.transform.

// 6. Wire SerializeFields via reflection (private fields → BindingFlags.NonPublic):
SetPrivateField(script, "_titleLabel", titleTmp);
SetPrivateField(script, "_rowContainer", rowContainerRt);
SetPrivateField(script, "_buttonClose", closeBtn);   // inherited from UI_WindowBase

// 7. DO NOT override Awake in the new script to add another Canvas. The inherited
//    Canvas is the sole render surface. If your script overrides Awake for other
//    reasons, the first line MUST be `base.Awake();` — otherwise the inherited
//    _buttonClose wiring AND the Camera.main → worldCamera assignment in
//    UI_WindowBase.Awake don't run, and the window won't render.

// 8. Default window deactivated.
root.SetActive(false);

// 9. Save as variant.
var asset = PrefabUtility.SaveAsPrefabAsset(root, "Assets/UI/Player HUD/UI_<Name>Window.prefab");
UnityEngine.Object.DestroyImmediate(root);
AssetDatabase.SaveAssets();
AssetDatabase.Refresh();

// 10. Verify variant relationship.
var src = PrefabUtility.GetCorrespondingObjectFromSource(savedAsset);
Debug.Assert(src.name == "UI_WindowBase");
```

For the SerializeField helper:
```csharp
private static void SetPrivateField(object target, string fieldName, object value)
{
    System.Type t = target.GetType();
    FieldInfo f = null;
    while (t != null && f == null)
    {
        f = t.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        t = t.BaseType;
    }
    f.SetValue(target, value);
}
```

**Why reflection** — `[SerializeField] private` fields aren't accessible via the public API. Roslyn-driven authoring needs to reach into the private field. The walk-up-base-classes loop is required because inherited fields (e.g. `_buttonClose` from `UI_WindowBase`) live on the base type.

### 4. Wiring `PlayerUI._<name>Panel` at scene authoring

This is the step a designer would normally do in the Inspector. From MCP it's:

```csharp
// 1. Must be in Edit mode — Play-mode wiring is volatile.
if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isPlaying)
    return "BLOCKED: exit Play mode first.";

// 2. Find PlayerUI in active scene.
var scene = SceneManager.GetActiveScene();
PlayerUI player = null;
foreach (var root in scene.GetRootGameObjects())
{
    player = root.GetComponentInChildren<PlayerUI>(true);
    if (player != null) break;
}

// 3. Instantiate panel prefab as child, keep prefab connection.
var panelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/UI/Player HUD/UI_<Name>Panel.prefab");
GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(panelPrefab, player.transform);
instance.SetActive(false);

// 4. Wire via SerializedObject so the change persists to the scene file.
var so = new SerializedObject(player);
var prop = so.FindProperty("_<name>Panel");
prop.objectReferenceValue = instance.GetComponent(panelType);
so.ApplyModifiedPropertiesWithoutUndo();

// 5. Save scene.
EditorUtility.SetDirty(player);
EditorSceneManager.MarkSceneDirty(scene);
EditorSceneManager.SaveScene(scene);
```

### 5. Nested non-window prefabs (rows / tiles)

Leaf UI elements (rows in a list, tiles in a grid, popup widgets) are NOT panels and are NOT variants of `UI_WindowBase.prefab`. Author them as standalone prefabs in the same folder (`Assets/UI/Player HUD/UI_<Name>Row.prefab`).

The parent panel references its row prefab via a `[SerializeField] private UI_<Name>Row _rowPrefab;` field and `Instantiate`s rows at runtime under a `_rowContainer` RectTransform with a `VerticalLayoutGroup` + `ContentSizeFitter`.

Row scripts:
- Are `public sealed class UI_<Name>Row : MonoBehaviour` (no `UI_WindowBase` inheritance).
- Live in `Assets/Scripts/UI/<Category>/UI_<Name>Row.cs`.
- Take all their data via `Initialize(...)` callbacks — NEVER hold references to gameplay singletons (`SafeFurniture`, `CharacterWallet`, …) directly. The parent panel passes balance getters + submit callbacks; the row stays decoupled and reusable.
- Use `RemoveAllListeners()` before `AddListener()` to be re-Init-safe.
- Implement `OnDestroy()` to defensively `RemoveAllListeners()` (rule #16).

The canonical example is [UI_SafeCurrencyRow.cs](../../Assets/Scripts/UI/Furniture/UI_SafeCurrencyRow.cs).

## Existing components

- **[UI_WindowBase](../../Assets/Scripts/UI/UI_WindowBase.cs)** — abstract base. Owns `_buttonClose` wiring + `OpenWindow` / `CloseWindow`.
- **[UI_WindowBase.prefab](../../Assets/UI/Player%20HUD/UI_WindowBase.prefab)** — base prefab every variant inherits.
- **[PlayerUI](../../Assets/Scripts/UI/PlayerUI.cs)** — singleton façade.
- **[UI_StorageFurniturePanel.cs](../../Assets/Scripts/UI/WorldUI/UI_StorageFurniturePanel.cs) + [.prefab](../../Assets/UI/Player%20HUD/UI_StorageFurniturePanel.prefab)** — canonical example. Reference shape: subscribe to `OnInventoryChanged` + bag/hands events, 1Hz auto-close poll on `IsCharacterInInteractionZone`, ESC close handler.
- **[UI_SafePanel.cs](../../Assets/Scripts/UI/Furniture/UI_SafePanel.cs) + [.prefab](../../Assets/UI/Player%20HUD/UI_SafePanel.prefab)** — most-recently-shipped panel (2026-05-16). Demonstrates the row-prefab-nested-inside-panel pattern.
- **[UI_SafeCurrencyRow.cs](../../Assets/Scripts/UI/Furniture/UI_SafeCurrencyRow.cs) + [.prefab](../../Assets/UI/Player%20HUD/UI_SafeCurrencyRow.prefab)** — canonical non-window leaf prefab.
- **[UI_InteractionMenu.cs](../../Assets/Scripts/UI/WorldUI/UI_InteractionMenu.cs)** — hold-E radial menu (separate system; renders `InteractableObject.GetHoldInteractionOptions` results).

## Common gotchas

- **Null SerializeField silently breaks the feature.** A window whose `_<name>Window` SerializeField on `PlayerUI` is null causes `Open<Name>Window` to silently no-op. The diagnostic warning IS the diagnosis — train your eye to scan for `[PlayerUI]` orange warnings in the Console.
- **Inherited Canvas RectTransform `scale=(0,0,0)` invisibility.** `UI_WindowBase.prefab` ships with the inherited Canvas's RectTransform at `scale=(0,0,0)`. Every variant MUST override this to `scale=(1,1,1)`, or the entire window renders at zero scale and is invisible in Game view. (2026-05-16 SafeFurniture incident.) The MCP authoring recipe in step 4 above does this automatically; if you author by hand, remember to set it.

- **NEVER put a `ContentSizeFitter` on `Panel_Main_Background` or any other fixed-size frame.** Unity runs `ContentSizeFitter` in a layout-rebuild pass that fires AFTER `LateUpdate` and BEFORE rendering — so every script-side write to the frame's `sizeDelta` (from `Awake`, `OnEnable`, `Initialize`, even per-frame `LateUpdate` guards) gets clobbered within the same frame. The base `UI_WindowBase.prefab` historically shipped with one on Panel_Main_Background; we removed it (2026-05-16). If you author a new variant by hand, double-check the Inspector — if you see `ContentSizeFitter` on the frame, REMOVE it. **`ContentSizeFitter` IS correct on `ScrollView/Viewport/Content`** (vertical-fit only) because Content needs to grow with row count for scrolling to trigger. Anywhere else on a window: bug. Diagnostic grep: `GetComponentsInChildren<ContentSizeFitter>(true)` walks the hierarchy and prints owners — run this whenever a "fixed-size" element collapses to (0,0) mid-frame.
- **Don't add a Canvas in your variant's `Awake`.** The inherited Canvas child (under the variant root, named "Canvas") is the sole render surface. Calling `GetComponent<Canvas>()` on the variant root returns null because the Canvas lives on a child, so an `AddComponent<Canvas>()` fallback creates a SECOND Canvas at runtime — with conflicting render-mode / sorting state. The 2026-05-16 SafeFurniture "visible in Scene view, invisible in Game view" bug was exactly this. Parent your variant's content under the inherited Canvas child, not under the variant root.
- **Canvas renderMode must remain `ScreenSpaceCamera`.** Don't change it to Overlay or WorldSpace in a variant. The project convention is `ScreenSpaceCamera` everywhere (per rule #39), with `Camera.main` assigned at runtime by `UI_WindowBase.Awake`. Changing the mode breaks the convention and the camera assignment becomes a no-op.
- **`Camera.main` must exist before any UI_WindowBase variant Awakes.** If the main-tagged camera spawns later than the HUD, `UI_WindowBase.Awake` will assign `worldCamera = null` (Camera.main returns null) and log an orange `[UI_WindowBase]` warning. Either ensure the camera is in the scene from the start, or assign `canvas.worldCamera` explicitly when the camera is created.
- **Play-mode wiring is volatile.** Always wire in Edit mode. From MCP, gate on `EditorApplication.isPlaying` first.
- **`Time.unscaledDeltaTime` may resolve to `MWI.Time`** when the window's namespace is `MWI.UI.<Category>`. Fully qualify as `UnityEngine.Time.unscaledDeltaTime` to disambiguate. Rule #26 still mandates unscaled time for UI.
- **`NetworkObject.OnNetworkDespawn` is NOT a public event** in this NGO version — it's an override on `NetworkBehaviour`. Don't subscribe windows to it. Use the `Update` null-guard on the target reference instead (1-frame latency between despawn and close is imperceptible).
- **Variant override drift.** Edits to `UI_WindowBase.prefab` propagate to variants UNLESS the variant has explicitly overridden the property. Use "Revert Override" in the Inspector to re-inherit.
- **Don't put window state on a NetworkBehaviour field.** Windows are pure client-side UI. They subscribe to authoritative NetworkVariable / ClientRpc broadcast state on OTHER components (`SafeFurnitureNetworkSync._networkBalances`, `CharacterWallet.OnBalanceChanged`).

## Test checklist before claiming done

1. Tap E on the target → panel opens.
2. Click the close button (top-right X) → panel closes.
3. Press ESC → panel closes.
4. Walk out of the InteractionZone → panel auto-closes within 1 second.
5. Type into any input field → submit button enables when amount > 0 and ≤ source balance.
6. Submit a transaction → success path repaints labels via `OnBalanceChanged`; failure path shows a transient status toast.
7. Console clean — no errors, no orange `[PlayerUI]` warnings.
8. Multiplayer late-joiner: host opens the panel + mutates state → client joins late, opens the same panel → sees the mutated state (rule #19b MANDATORY).
9. Two players opening the same panel simultaneously → both succeed; concurrent transactions race-resolve to one success + one `insufficient-*` toast.

---

## Combat action bar prefab structure (2026-05-17)

Combat HUD shipped a multi-cluster action bar + Items sub-window + chrome leaves. The Items sub-window is the canonical example of a `UI_WindowBase` Prefab Variant whose anchor point follows a sibling element (the Items button on the action bar) — pattern reusable for any "popover from a HUD button."

### File layout

- `Assets/UI/Player HUD/UI_CombatItemsWindow.prefab` — Variant of `UI_WindowBase.prefab` (rule #39). Backing script `UI_CombatItemsWindow : UI_WindowBase`. Anchored above-right of the Items button (~y=74 from bottom-right).
- `Assets/UI/Player HUD/UI_CombatItemRow.prefab` — leaf row prefab (no close button → NOT a UI_WindowBase variant). Instantiated by `UI_CombatItemsWindow` under its `ScrollView/Viewport/Content`.
- `Assets/UI/Player HUD/Combat/UI_CombatAbilitySlot.prefab` — leaf, ×6 instances inside `UI_CombatActionMenu`'s abilities cluster.
- `Assets/UI/Player HUD/Combat/UI_CombatInitiativeBar.prefab` — leaf, single instance inside `UI_CombatActionMenu._menuContainer`.
- `Assets/UI/Player HUD/Combat/UI_CombatQueuedLabel.prefab` — leaf, single instance above the initiative bar.

### Leaf-inside-HUD pattern (new — distinct from "leaf-inside-window")

The action bar (`UI_CombatActionMenu`) is itself NOT a UI_WindowBase variant — it's a leaf HUD element (no close button; shown/hidden by `IsInBattle` state). But it has its own leaf children (init bar, queued label, ability slots) that live inside `_menuContainer` and are initialized via the parent's `Initialize(character)` call. This is the canonical "HUD-with-sub-leaves" structure:

- Top-level HUD element: `UI_CombatActionMenu` — owns the `_menuContainer` toggled by lifecycle state (`IsInBattle`).
- Sub-leaves: `UI_CombatInitiativeBar`, `UI_CombatQueuedLabel`, `UI_CombatAbilitySlot[6]` — authored as children of `_menuContainer`, wired as `SerializeField` references on the parent.
- Initialization: parent's `Initialize(character)` calls `subElement.Initialize(character)` on each.

This pattern is appropriate when the children share lifecycle with the parent (all show/hide together) and don't need their own close affordance. **Litmus**: if you'd never want the queued label visible without the action bar visible, it's a leaf child, not a sibling window.

### PlayerUI surface

- `[SerializeField] private UI_CombatItemsWindow _combatItemsWindow;` (added 2026-05-17 alongside `_combatActionMenu`)
- `OpenCombatItemsWindow(Character)` / `CloseCombatItemsWindow()` / `IsCombatItemsWindowOpen` / `ToggleCombatItemsWindow(Character)`
- Null-guard warning per rule #39 — surfaces when the SerializeField isn't wired in the scene.

### Hotkey ownership

UI button onClick handlers and PlayerController hotkeys call the **same** `CharacterCombat` / `CharacterAbilities` / `PlayerUI` methods. No parallel input paths. The 1-9 row-select hotkeys live inside `UI_CombatItemsWindow.Update` (window-scoped only) — PlayerController suppresses its global 1-6 ability binding when `PlayerUI.IsCombatItemsWindowOpen`.

### Related docs

- Plan: [docs/superpowers/plans/2026-05-17-combat-action-bar.md](../../../docs/superpowers/plans/2026-05-17-combat-action-bar.md)
- Prefab checklist (active TODO): [docs/superpowers/plans/2026-05-17-combat-action-bar-prefab-authoring.md](../../../docs/superpowers/plans/2026-05-17-combat-action-bar-prefab-authoring.md)


---

## Click-to-popup pattern (2026-05-19)

`UI_CharacterEquipment` introduces a **shared popup component** pattern. The window
hosts one `UI_EquipmentActionPopup` child and reuses it for every item-bearing
cell click (worn mini-cells, bag cells, special-slot cards). The popup is fed a
state-aware `List<EquipmentVerb>` per cell — see the verb matrix in the [design spec](../../docs/superpowers/specs/2026-05-19-character-equipment-ui-rework-design.md) §5.

**Pattern shape:**

- Window root owns one popup instance (SerializeField).
- Each cell type (worn / bag / special-slot) calls `window.OpenPopupForXxxCell(this)`.
- The window builds the verb list for that state and calls `popup.Show(anchor, title, subtitle, verbs, onVerbSelected)`.
- Popup dismisses on ESC, click-outside, or button-click. Single instance — clicking a new cell while the popup is open replaces the content.
- Verb selection callback maps the `EquipmentVerbId` enum to a server RPC call (`CharacterActions.RequestEquipmentVerbServerRpc`).

**When to reuse**: any new window that needs per-cell contextual actions (storage
panel right-click menus, party-member context menus, future inventory grids).
Lift the popup component to a more general location if a third user appears.

**Files** (under `Assets/Scripts/UI/Equipment/`):

- `UI_EquipmentActionPopup.cs` — shared popup component. Also defines `EquipmentVerb` struct + `EquipmentVerbId` enum.
- `UI_EquipmentWornCell.cs` — leaf, paper-doll mini-cell.
- `UI_EquipmentBagCell.cs` — leaf, bag-grid cell.
- `UI_EquipmentSpecialSlotCard.cs` — leaf, top-row Weapon/Hands/Bag card.
- `UI_CharacterEquipment.cs` — window root.


---

## UGUI gotchas surfaced 2026-05-19 (Equipment UI rework)

Documented from playtest-driven debugging during the equipment UI rework. Apply these to any new closable window authored programmatically.

### 1. `Panel_Main_Background` inherits 50%-alpha black

The base `UI_WindowBase.prefab` ships with `Panel_Main_Background.Image.color = RGBA(0, 0, 0, 0.5)`. Variants that author content under it WITHOUT overriding the color end up with a semi-transparent panel — content with brighter colors looks "detached" / "floating" because the panel chrome barely registers visually.

**Fix at variant authoring time**: set `panelImg.color = new Color(0.08f, 0.10f, 0.14f, 1f)` (or another opaque dark) on `Panel_Main_Background`. The whole panel area then reads as one coherent surface.

### 2. `LayoutGroup.childControl*` defaults IGNORE `LayoutElement`

New `HorizontalLayoutGroup` / `VerticalLayoutGroup` instances default to `childControlWidth=false` and `childControlHeight=false`. With these off, the layout group **does not apply `LayoutElement.preferredWidth/Height`** — children render at their RectTransform `sizeDelta`. For runtime-instantiated GameObjects via `new GameObject(...)`, that `sizeDelta` defaults to `(100, 100)`.

The symptom is "I set `LayoutElement.preferredHeight = 24` but the cell renders at 100×100". Verify with: `cell.GetComponent<LayoutElement>().preferredHeight` vs `cell.GetComponent<RectTransform>().rect.size` — if they disagree, the layout group is ignoring the LayoutElement.

**Fix**: explicitly set `layoutGroup.childControlWidth = true; layoutGroup.childControlHeight = true;` on any layout group authored programmatically. Belt-and-suspenders: also set the children's `RectTransform.sizeDelta` directly to the desired values so they default-correctly even if a future edit flips the layout flag.

### 3. Card-label overflow into adjacent regions

When a card (with its own `VerticalLayoutGroup` for stacked labels) is itself constrained to a small height by a parent layout group (e.g. 48px row), but the card's own VerticalLayoutGroup has `childControlHeight = false`, the labels keep their `sizeDelta=(100,100)` default and OVERFLOW the card DOWNWARD into whatever sits below (in our case the body region). Visually it looks like the cards "go under the next frame".

**Fix**: card VerticalLayoutGroup needs `childControlHeight = true + childForceExpandHeight = true` (labels share parent height equally) OR `childControlHeight = true` with explicit `LayoutElement.preferredHeight` per label.

### 4. `Button_Close` is a canvas-center sibling of Panel_Main_Background

The inherited `Button_Close` lives at Canvas level (sibling of `Panel_Main_Background`), not inside the panel. Its `anchoredPosition` is in canvas-center coordinates and must be computed per variant to land on the panel's top-right corner. For a centered panel of size W×H + button size S + margin M:

```
anchoredPosition = (W/2 - S/2 - M, H/2 - S/2 - M)
```

Reference values:
- UI_SafePanel (~560-wide panel): `(268, 228)`
- UI_CharacterEquipment (720×520 panel): `(338, 238)`

Reparenting Button_Close UNDER Panel_Main_Background is also valid (anchored top-right of the panel rect) but requires `btnClose.SetAsLastSibling()` so it renders on top of authored content.

### 5. `RectangleContainsScreenPoint(rt, mouse, null)` mis-computes under ScreenSpaceCamera

The `null` camera argument only works correctly for `ScreenSpaceOverlay`. Under `ScreenSpaceCamera` (the project convention per rule #39), passing null produces wrong results — any click is reported as "outside the rect".

The equipment popup's click-outside-to-dismiss originally used this signature → popup hid on EVERY click → popup buttons never registered because `Hide()` ran in `Update()` BEFORE the EventSystem could route the click to the button.

**Fix**: use `EventSystem.current.IsPointerOverGameObject()` instead. Works in any canvas mode without needing a camera reference.

### 6. Scene + UI_PlayerHUD.prefab nesting + dedupe

`UI_PlayerHUD.prefab` nests each panel (UI_CharacterEquipment, UI_SafePanel, UI_StorageFurniturePanel, etc.) as a child INSIDE the prefab. When the scene loads its UI_PlayerHUD instance, the nested children come with it automatically.

If a scene-wire script then does `PrefabUtility.InstantiatePrefab(panelPrefab, playerUI.transform)`, it creates a SECOND copy of the panel as a direct PlayerUI child — duplicate. The scene-wire script must dedupe (check `PrefabUtility.IsAnyPrefabInstanceRoot` + `GetOutermostPrefabInstanceRoot` to distinguish nested vs added-root) OR not add a new instance at all (just re-point `_xxxPanel` to the existing nested one).

Also: deleting + recreating a nested panel prefab WITHOUT updating UI_PlayerHUD.prefab leaves the nested reference broken (missing-GUID warning). Either preserve the .meta file when re-authoring, or open UI_PlayerHUD.prefab via `PrefabUtility.LoadPrefabContents` + replace the broken nested child with the new prefab.

---

## World-anchored HUD leaf pattern (2026-05-19)

A HUD leaf that needs to follow a world-space entity (the local player, a target NPC, a quest marker, …) on-screen — staying in HUD space rather than world-space rendering. Canonical pattern: `Camera.WorldToScreenPoint(anchor.position + worldOffset)` each frame, then `RectTransformUtility.ScreenPointToLocalPointInRectangle(parentCanvasRect, sp, uiCam, out lp)` to convert to canvas-local coords, then `anchoredPosition = Vector2.Lerp(current, lp + screenOffsetPx, lerpSpeed * Time.unscaledDeltaTime)`.

**Canonical implementations:**
- `SpeechBubbleInstance` — bubbles parented under `HUDSpeechBubbleLayer.Local.ContentRoot`, lerped with `_positionLerpSpeed * Time.unscaledDeltaTime`.
- `QuestWorldMarkerRenderer` — quest icons clamped to screen edges; the canonical robust version that handles all three Canvas render modes (`Overlay` → null uiCam; `ScreenSpaceCamera` / `WorldSpace` → `canvas.worldCamera`).
- `UI_Action_ProgressBar` — local player's action progress bar (reworked 2026-05-19 from a fixed bottom-of-screen bar to a head-anchored bar that follows the player; both the bar and the action-name text move together using shared world projection).
- `UI_RemoteActionIndicator` + `RemoteActionIndicatorLayer` (2026-05-19, unified) — single indicator type for ALL characters (local player + NPCs + remote players). The singleton manager spawns one indicator per `Character` via `Character.OnCharacterSpawned/Despawned` static events; each indicator follows its target's head, fades with distance to the local player (skipped when `isLocalPlayer = true`), and shows a Radial360 progress arc + a short action-name label above the badge. PlayerPrefs-backed toggle `MWI.HUD.ShowRemoteActionBars` (default ON — flips remote indicators only, local player always visible), changed via `/togglebars` chat command.

**Rules of the pattern:**

1. **`unscaledDeltaTime` for the lerp**, always. HUD position is real-time, not simulation-time. At `GameSpeedController = 0x` (pause) the position must still smoothly catch up; at `5x` it must not whiplash.
2. **Lazy-resolve `Camera.main`.** Cache null-check on first miss and try again next frame — the local player's camera can be momentarily absent during portal-gate return, character respawn, or first-frame initialization on a freshly-joined client. Mirror `SpeechBubbleInstance.Update`'s pattern.
3. **`uiCam` depends on Canvas renderMode.** `Overlay` → pass null. `ScreenSpaceCamera` / `WorldSpace` → pass `canvas.worldCamera`. Either branch produces incorrect coordinates if you swap them. Use the QuestWorldMarkerRenderer one-liner: `(canvas.renderMode == RenderMode.ScreenSpaceOverlay) ? null : canvas.worldCamera`.
4. **Snap on (re-)activation.** When the leaf transitions from inactive → active (e.g. `OnActionStarted` enables the bar after `OnActionFinished` disabled it last action), call the projection once with the lerp bypassed — otherwise it lerps in from its last-known on-screen position, which may be far from the new anchor.
5. **Off-screen sentinel.** If `sp.z < 0` (anchor behind camera), skip the update — don't try to project. Decide explicitly whether to hide the leaf (CanvasGroup alpha to 0), let it sit at its last position, or clamp to a screen edge (QuestWorldMarkerRenderer chose the last).
6. **Anchors of the leaf RectTransform** should be `(0,0)`/`(0,0)` so `anchoredPosition` is in canvas-local pixels from the parent rect's pivot. If the parent rect's pivot is `(0,0)`, then `anchoredPosition` is directly the screen-pixel-equivalent position — and `ScreenPointToLocalPointInRectangle` returns that value directly. Pivot of the leaf itself controls where the leaf sits relative to the projected world point (e.g. pivot `(0.5, 0)` puts the leaf's bottom-center on the projected point — useful for bars that should sit ABOVE the head).
7. **`worldOffset`** (in Unity units) lets you target a body part above the character's root — head, shoulder, weapon tip. Rule #32: 11 Unity units ≈ 1.67m, so head-level is ~`Vector3.up * 12`.

**When to use vs not:**
- Use for any HUD element that semantically "belongs to" a specific world entity and must show in HUD space (action progress, casting cast bar, status icons, damage flash). Local-player-only versions can use a single scene-resident GameObject SerializeField on `PlayerUI` (current pattern of `UI_Action_ProgressBar`); per-character versions (NPC casting bars, party-member overhead UI) need a per-character instance manager mirroring `SpeechBubbleStack`.
- Do NOT use for elements that don't need to track any world entity (fixed quest log, fixed minimap, fixed inventory bar). Those are static HUD leaves anchored to a screen corner.
- Do NOT use for elements that ARE world-space objects (overhead nameplate sprites baked into the character, world-space damage text). Those skip the canvas entirely and use a world-space Canvas or a particle system.


---

## Extended lessons — 2026-05-19 Equipment UI playtest cycle

These add to the 6 gotchas above and capture process-level mistakes from a multi-iteration rework. Read before starting any new prefab work via MCP Roslyn.

### 7. Verify Application.dataPath = your git working tree BEFORE any MCP scene/prefab op

Unity's MCP-Roslyn tooling reads files from `Application.dataPath` (the project the running Editor opened). Your git operations may target a worktree at a different path. The two CAN drift — bad symptoms:

- `assets-refresh` reports success but your changes don't appear in the Editor.
- `script-execute` reports "no UI_CharacterEquipment instances found" when there clearly are in your branch.
- `Type.GetType("Your.New.Type, Assembly-CSharp")` returns null even though the .cs is committed.

**Diagnostic at start of any MCP session that will touch scenes/prefabs:**

```csharp
Debug.Log($"Unity dataPath: {Application.dataPath}");
// Compare to your git worktree path. If they differ, MCP changes won't land where your git work is.
```

If they differ: stop, decide whether to (a) merge your branch into the branch Unity is on, (b) close Unity and reopen on the right path, or (c) do destructive prefab work directly in Unity's tree and commit there.

### 8. Verify the active scene is the right one

Unity remembers the last-active scene per Editor session. If the user pressed Play and then Stop, the active scene may have switched back to whatever opened first (often MainMenuScene), not the scene you were editing. Scripts that do `SceneManager.GetActiveScene()` then find no PlayerUI / no nothing because they're looking at the wrong scene.

**Always log the active scene + open the expected one explicitly:**

```csharp
Scene scene = SceneManager.GetActiveScene();
Debug.Log($"Active scene: {scene.name} at {scene.path}");
if (scene.name != "GameScene")
    scene = EditorSceneManager.OpenScene("Assets/Scenes/GameScene.unity", OpenSceneMode.Single);
```

### 9. Grep for existing implementations before authoring new

Before adding `CharacterAction_X`, `UI_X`, `IX` — grep the codebase for related verbs/nouns. The project may already have the action under a slightly different name. The 2026-05-19 rework planned `CharacterAction_UseItem` but `CharacterUseConsumableAction` already existed with the correct duration + animator trigger + removal flow. Reinvention wasted authoring + delete + commit cycles.

Pattern: `grep -rn "<verb>" Assets/Scripts/Character/CharacterActions/` before authoring any new action.

### 10. Initialize must NOT auto-open

UI scripts often expose `Initialize(target)` for data binding. PlayerUI calls `Initialize` during character setup. If `Initialize` also calls `OpenWindow()`, the window pops at game start.

**Pattern**: split into two methods:
- `Initialize(target)` — bind data, subscribe events, paint initial state. Does NOT change visibility.
- `InitializeAndOpen(target)` — convenience wrapper for user-driven open paths (`PlayerUI.OpenXxxWindow`). Calls `Initialize` then `OpenWindow`.

PlayerUI's character-setup pass calls `Initialize`. User-driven open paths call `InitializeAndOpen`.

### 11. `CharacterAction.Duration = 0` means "instant, no feedback"

Zero-duration actions run `OnApplyEffect` in the same frame as `OnStart`. No animator trigger window, no progress bar visibility. For actions the player intuitively expects to "take time" (eat, drink, use potion, equip, swap), set `Duration` to 1.0–2.0 seconds and trigger an animator state in `OnStart`. The progress bar will engage automatically because `CharacterActions.GetActionProgress()` reads `Duration` for non-Continuous actions.

Reference: `CharacterUseConsumableAction(character, item) : base(character, 1.5f)` + `animator.SetTrigger("Trigger_Consume")` in `OnStart`.

### 12. Don't assume a hotkey is wired to a window without reading `PlayerController`

Spec / plan / agent description may CLAIM "Tab opens equipment" but the PlayerController source may use Tab for something else (targeting, focus cycle, etc.). Always read `PlayerController.cs` and look for the actual `Input.GetKeyDown(KeyCode.Xxx)` calls before claiming a window has a hotkey. If the keybind doesn't exist, the window opens only via its HUD button.

### 13. Prefab GUID stability when re-authoring

`AssetDatabase.DeleteAsset(path)` + `SaveAsPrefabAsset(go, path)` creates a NEW prefab with a NEW GUID. Any other prefab that nested the old one (e.g. `UI_PlayerHUD.prefab` nesting `UI_CharacterEquipment.prefab`) now has a broken nested reference (missing-GUID warning on Editor load, no instance at runtime).

**Two options to preserve GUID:**

1. **Overwrite without delete** — call `PrefabUtility.SaveAsPrefabAsset(newRoot, path)` on a path that already exists. Unity preserves the .meta file → same GUID → nested references resolve. This is the default behavior when the path is already occupied.
2. **Repoint nesters explicitly** — after delete+recreate, open each prefab that nested the old one via `PrefabUtility.LoadPrefabContents`, find the broken nested child, replace with new `PrefabUtility.InstantiatePrefab(newPrefab, parent)`, `SaveAsPrefabAsset` the nester.

For routine prefab edits that don't restructure the hierarchy, prefer Option 1 — open, modify, save. Only delete-recreate when the structure needs a full rebuild AND you commit to repointing all nesters.

### 14. Stop Play mode before any prefab/scene MCP op

Most MCP scripts that edit prefabs or scenes have `if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isPlaying) return BLOCKED`. The Editor console may be FLOODED with game logs at play time, hiding your "[ScriptName] BLOCKED" message.

**Diagnostic**: if `git status` shows clean working tree after a "[Saved]" Debug.Log claim, the script aborted before the SaveAsPrefabAsset call. Check play mode state first.

### 15. Two derived UI_WindowBase components on the same GameObject

When you author a Variant of UI_WindowBase.prefab and `AddComponent<YourWindow>()` (where YourWindow inherits UI_WindowBase), the GameObject now has TWO UI_WindowBase-derived components: the base's own instance AND your derived one. Both call `Awake()` (your derived calls `base.Awake()`). Both have `_buttonClose` SerializeField — the base's is wired (inherited from prefab), yours is null.

Behavior: close button works (the base instance's wiring fires). Inspector shows two "Window Base" headers — confusing but not broken.

**If this matters for clarity**: remove the base UI_WindowBase component from the variant before adding the derived one (`Object.DestroyImmediate(root.GetComponent<UI_WindowBase>())` before `root.AddComponent<YourWindow>()`). But this loses the wired `_buttonClose` — you must re-wire it on the derived component via reflection.

### 16. Working-tree-clean diagnostic for MCP write ops

After running an MCP script that claims to have modified a file (saved prefab, edited scene), run `git status --short`. If the tree is clean despite the [Saved] log, the script aborted before the write — most commonly because of the play-mode gate (#14) or a wrong-active-scene issue (#8).

### Programmatic prefab authoring pre-flight checklist

Before running a new "author prefab via Roslyn" script:

1. **Active scene right?** `Debug.Log(SceneManager.GetActiveScene().name)` first; open the correct scene if not.
2. **Editor in Edit mode?** First line of script: `if (EditorApplication.isPlaying) { LogError + return; }`.
3. **dataPath = git worktree?** `Debug.Log(Application.dataPath)` early on; bail if mismatched.
4. **Existing classes exist?** Grep for similar verbs/nouns in the relevant folder before authoring a new MonoBehaviour or CharacterAction.
5. **Existing prefab in `UI_PlayerHUD.prefab`?** If you're adding a new window panel, check whether it's already nested there (don't double-instantiate in the scene).
6. **`childControlWidth + childControlHeight = true`** on every LayoutGroup you create programmatically, OR set children's `RectTransform.sizeDelta` explicitly. Never trust `LayoutElement.preferredWidth/Height` alone without `childControl*` enabled on the parent.
7. **Set Image.color explicitly on Panel_Main_Background** — default inherited alpha is 0.5.
8. **Compute `Button_Close.anchoredPosition`** = `(panelW/2 - btnW/2 - margin, panelH/2 - btnH/2 - margin)` if you keep it at canvas level.
9. **Initialize ≠ Open** — two separate entry points for "bind data" vs "show window".
10. **Don't use `RectangleContainsScreenPoint(..., null)`** under ScreenSpaceCamera — use `EventSystem.IsPointerOverGameObject()`.
11. **Test path**: stop play → run script → check console for `[ScriptName] DONE` → check `git status --short` shows the expected files modified → press Play to verify.
