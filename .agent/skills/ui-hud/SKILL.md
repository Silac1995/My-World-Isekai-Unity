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
