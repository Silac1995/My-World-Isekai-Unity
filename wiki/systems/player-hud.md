---
type: system
title: "Player HUD"
tags: [ui, hud, player, window, prefab-variant]
created: 2026-05-16
updated: 2026-05-16
sources:
  - "Assets/Scripts/UI/UI_WindowBase.cs"
  - "Assets/Scripts/UI/PlayerUI.cs"
  - "Assets/UI/Player HUD/UI_WindowBase.prefab"
  - ".agent/skills/ui-hud/SKILL.md"
related:
  - "[[ui-storage-furniture-panel]]"
  - "[[ui-safe-panel]]"
  - "[[ui-shop-buy-panel]]"
  - "[[ui-interaction-menu]]"
  - "[[commercial-treasury]]"
status: stable
confidence: high
primary_agent: ui-hud-specialist
secondary_agents:
  - building-furniture-specialist
owner_code_path: "Assets/Scripts/UI/"
depends_on:
  - "[[character]]"
  - "[[interactable-proximity]]"
depended_on_by:
  - "[[commercial-treasury]]"
  - "[[storage-furniture]]"
  - "[[cashier]]"
---

# Player HUD

## Summary
The Player HUD is the single client-side UI layer that surfaces world state to the local owning player. **Every closable UI window** — full-screen panels, half-screen modals, side drawers, mid-screen popups, confirmation dialogs, dismissable tooltips, sub-windows opened from inside another window — lives as a child of one scene-resident `UI_PlayerHUD` GameObject and is opened/closed exclusively through the [PlayerUI](../../Assets/Scripts/UI/PlayerUI.cs) singleton façade. Every closable window inherits its chrome (Canvas + Background + close-button wiring) from a single base prefab — [UI_WindowBase.prefab](../../Assets/UI/Player%20HUD/UI_WindowBase.prefab) — via Unity Prefab Variants. No window re-implements the Canvas / GraphicRaycaster / CanvasScaler trio. The defining trait of "is a window for this system" is **"has a close affordance"** — not size, modality, or layout.

## Purpose
Three problems would be unsolved without this system:

1. **Consistency of window chrome.** Every closable window needs the same Canvas setup, sorting order, scale mode, raycaster, and "click X to close" button wiring. Re-authoring those on every window is error-prone and visually inconsistent.
2. **Single entry-point for opening UI.** Gameplay code (`Furniture.OnInteract`, `CharacterAction` ClientRpcs, hold-E menu options, parent-window code that opens a sub-window) must be able to open the right window without each call site knowing prefab paths or Inspector wiring details. `PlayerUI.Instance.Open<Name>Window(...)` is that entry-point.
3. **Discoverable diagnostics when wiring breaks.** When a designer forgets to assign a window's `[SerializeField]` on `PlayerUI`, the Open call must log a directive warning so the diagnosis takes 5 seconds instead of an hour.

## Responsibilities
- Own the [UI_WindowBase.prefab](../../Assets/UI/Player%20HUD/UI_WindowBase.prefab) chrome base every closable window inherits.
- Host every `UI_*<Window>` script extending [UI_WindowBase](../../Assets/Scripts/UI/UI_WindowBase.cs).
- Expose `Open<Name>Window(...)` / `Close<Name>Window()` on `PlayerUI` as the singleton public surface.
- Log directive warnings when a window's SerializeField is null.
- Keep the façade **flat**: every closable window is a sibling under PlayerUI, never a grand-child of another window. Sub-windows opened from inside another window go through `PlayerUI.Instance.Open<Name>Window(...)`, not via a direct child reference.
- Coordinate window z-order (each window's Canvas has its own `sortingOrder`; PlayerUI assumes windows don't overlap by default).

**Non-responsibilities** (common misconceptions):
- Not responsible for input-driven character control — that's in [[player-controller]] / `PlayerController` (rule #33).
- Not responsible for the world-space interaction menu (the hold-E radial menu) — that's [[ui-interaction-menu]], a separate `UI_InteractionMenu` script driven by `InteractableObject.GetHoldInteractionOptions`.
- Not responsible for screen-space UI that isn't player-driven (loading screens, splash, debug overlays) — those live in their own scenes / canvases outside the HUD hierarchy.
- **Not responsible for non-window leaf prefabs**: rows / tiles / list items / badges / auto-fading tooltips. Those are `Instantiate`d as children of a window's `_rowContainer` at runtime and have no close affordance. **Litmus test**: if the element has a Button that calls `CloseWindow` / `SetActive(false)` on itself, it is a window and rule #39 applies; if it disappears only when its parent window closes or via a timer, it is a leaf and rule #39 does not apply.

## Key classes / files
| File | Role |
|------|------|
| [UI_WindowBase.cs](../../Assets/Scripts/UI/UI_WindowBase.cs) | Abstract-ish base for every panel script. Owns `_buttonClose` wiring + `OpenWindow` / `CloseWindow`. |
| [UI_WindowBase.prefab](../../Assets/UI/Player%20HUD/UI_WindowBase.prefab) | Base prefab every panel variant inherits. Provides Canvas + GraphicRaycaster + CanvasScaler + Panel_Main_Background Image. |
| [PlayerUI.cs](../../Assets/Scripts/UI/PlayerUI.cs) | Scene-resident singleton (`PlayerUI.Instance`). Holds one `[SerializeField] private UI_<Name>Panel _xxxPanel;` per panel + matching `Open<Name>Panel` / `Close<Name>Panel` methods. |
| [UI_StorageFurniturePanel.cs](../../Assets/Scripts/UI/WorldUI/UI_StorageFurniturePanel.cs) + [.prefab](../../Assets/UI/Player%20HUD/UI_StorageFurniturePanel.prefab) | Canonical example of a panel + variant pair. Mirror its lifecycle when building new panels. |
| [UI_SafePanel.cs](../../Assets/Scripts/UI/Furniture/UI_SafePanel.cs) + [.prefab](../../Assets/UI/Player%20HUD/UI_SafePanel.prefab) | Most-recently-shipped example (2026-05-16). Demonstrates the Variant-of-UI_WindowBase pattern plus a per-row nested prefab pattern. |
| [UI_SafeCurrencyRow.cs](../../Assets/Scripts/UI/Furniture/UI_SafeCurrencyRow.cs) + [.prefab](../../Assets/UI/Player%20HUD/UI_SafeCurrencyRow.prefab) | Example of a leaf (non-window) UI prefab — stored in the same folder, NOT a variant of UI_WindowBase. |

## Public API / entry points
On [PlayerUI](../../Assets/Scripts/UI/PlayerUI.cs):
- `PlayerUI.Instance` — singleton accessor. Null until the scene's `UI_PlayerHUD` GameObject is initialised.
- `OpenStoragePanel(StorageFurniture, Character)` / `CloseStoragePanel()`.
- `OpenSafePanel(SafeFurniture, Character)` / `CloseSafePanel()`.
- `OpenShopBuyPanel(Cashier, Character)`.
- `OnSafeOperationResult(SafeFurniture, bool, string)` — failure-toast hook called by `SafeFurnitureNetworkSync.OperationResultClientRpc`.

On every panel ([UI_WindowBase](../../Assets/Scripts/UI/UI_WindowBase.cs) base):
- `OpenWindow()` — `SetActive(true)`. Override in subclass for per-open logic.
- `CloseWindow()` — `SetActive(false)`. Override + call `base.CloseWindow()` last for cleanup.
- Inherited `[SerializeField] private Button _buttonClose;` — auto-wired in `Awake` to `CloseWindow`. Must be assigned at prefab-authoring time on the variant.

## Data flow

Standard panel-open flow:

```
[Gameplay event] (e.g. Furniture.OnInteract, ClientRpc, hold-E menu Action)
  → PlayerUI.Instance.Open<Name>Panel(contextArgs)
  → null-guard on _<name>Panel SerializeField (warns + returns if null)
  → _<name>Panel.Initialize(contextArgs)
    → subscribe to authoritative state events (NetworkVariable, OnXxxChanged)
    → build rows / populate labels
    → OpenWindow() → SetActive(true)
[User clicks close button]
  → inherited _buttonClose.onClick → UI_WindowBase.CloseWindow
  → subclass override unsubscribes + clears rows
  → base.CloseWindow() → SetActive(false)
[Auto-close on out-of-zone — for furniture panels]
  → 1Hz poll in subclass Update calls IsCharacterInInteractionZone
  → CloseWindow() if out
```

## Dependencies

### Upstream (this system needs)
- [[character]] — the panel binds to the local owner Character for input/wallet/etc.
- [[interactable-proximity]] — panels that need out-of-zone auto-close use `InteractableObject.IsCharacterInInteractionZone` (rule #36).

### Downstream (systems that open panels)
- [[storage-furniture]] — `StorageFurniture.OnInteract → PlayerUI.OpenStoragePanel`.
- [[commercial-treasury]] — `SafeFurniture.OnInteract → PlayerUI.OpenSafePanel`.
- [[cashier]] — `Cashier`-driven `CashierNetSync.OpenBuyPanelClientRpc → PlayerUI.OpenShopBuyPanel`.
- Hold-E action menu options ([[ui-interaction-menu]]) — same `PlayerUI.Open*` entry-points for any furniture verb.

## State & persistence

- **Runtime state**: each panel instance is a child of `UI_PlayerHUD` in the scene; activated/deactivated via `SetActive`. State references (`_safe`, `_customer`, current rows) live on the panel script during `OpenWindow`-`CloseWindow` lifecycle. Cleared in `CloseWindow` to release references.
- **Persisted state**: NONE. Panel state is purely runtime. Scene file persists the panel hierarchy + SerializeField wiring (`PlayerUI._xxxPanel` references) and nothing else.
- **Network state**: panels do not host NetworkBehaviour fields. They subscribe to authoritative state replicated elsewhere (e.g. `SafeFurnitureNetworkSync._networkBalances`, `CharacterWallet.OnBalanceChanged`).

## Known gotchas / edge cases

- **Null SerializeField silently breaks the feature.** A window whose `_<name>Window` SerializeField on `PlayerUI` is null causes `Open<Name>Window` to silently no-op (post the null-guard added per rule #39). The diagnosis is a `[PlayerUI]` warning in the Console — train your eye to scan for it. See the 2026-05-16 incident on `_safePanel`.
- **Canvas renderMode must be `ScreenSpaceCamera`.** Every UI_WindowBase variant — including the base prefab itself — uses `RenderMode.ScreenSpaceCamera`, never `ScreenSpaceOverlay` or `WorldSpace`. The `worldCamera` field is null on the prefab asset (prefabs can't reference scene cameras) and `UI_WindowBase.Awake` walks the variant's Canvas tree to assign `Camera.main` at runtime. If `Camera.main` is null when Awake fires, the base logs an orange `[UI_WindowBase]` warning — that's the diagnosis surface for "Camera tagged MainCamera missing in scene".
- **Don't add a Canvas/GraphicRaycaster in your variant's `Awake`.** The inherited Canvas child (from `UI_WindowBase.prefab`) already supplies them. The 2026-05-16 SafeFurniture incident: `UI_SafePanel.Awake` was calling `GetComponent<Canvas>()` on the root, finding null (the inherited Canvas is on a child GameObject named "Canvas"), and `AddComponent<Canvas>()` was creating a SECOND Canvas on the root — resulting in 2 Canvases at runtime with conflicting render-mode/sorting state, and the panel "visible in Scene view but invisible in Game view". The fix was to delete the auto-provisioning entirely and parent the variant's content under the inherited Canvas child.
- **`UI_WindowBase.prefab` ships with RectTransform `scale=(0,0,0)` on the inherited Canvas.** This is a known historical default — every variant must explicitly set `scale=(1,1,1)` on its Canvas RectTransform via override, otherwise the entire window renders at zero scale (invisible). The MCP authoring recipe in `.agent/skills/ui-hud/SKILL.md` does this automatically; if you author the variant by hand in the Editor, REMEMBER to override the scale.
- **Variant override drift.** When you change `UI_WindowBase.prefab` (e.g. resize the close button), every variant inherits the change BUT any property the variant has explicitly overridden stays overridden. Use Unity's "Revert Override" on the variant to re-inherit after a base edit.
- **Play-mode wiring is volatile.** Wiring `PlayerUI._<name>Window` while in Play mode does not persist — Unity reverts on exit. Always wire in Edit mode (or via `SerializedObject.ApplyModifiedPropertiesWithoutUndo + EditorSceneManager.SaveScene` from an Editor script).
- **Out-of-zone auto-close needs `Time.unscaledDeltaTime`.** If a window uses `Time.deltaTime`, it pauses when the GameSpeedController is at 0x (e.g. menu open). UI must use unscaled time (rule #26). Watch for the `MWI.Time` namespace shadowing `UnityEngine.Time` — fully qualify if your file's namespace causes resolution to pick `MWI.Time`.

## Open questions / TODO
- [ ] Should non-furniture screen-space HUD elements (health bars, minimap, quest tracker) also be `UI_WindowBase` variants? Today they're authored ad-hoc. Decide before the next HUD overhaul.
- [ ] Z-order policy when multiple panels can be open at once (e.g. storage + map). Today: each variant has its own Canvas sortingOrder; no coordinator. Revisit if panels start overlapping.

## Change log
- 2026-05-16 — created; documents the UI_WindowBase Prefab Variant convention formalised by Kevin during the SafeFurniture deposit/withdraw UI feature — claude
- 2026-05-16 — broadened scope from "player-facing panel / modal / full-screen" to "any closable UI window" per Kevin clarification ("a window/ui that can be closed should always be a child of Window_base"). Added litmus test for the window-vs-leaf boundary and the flat-façade rule (sub-windows open through PlayerUI, not via parent-window child refs) — claude
- 2026-05-16 — locked in `RenderMode.ScreenSpaceCamera` as the universal Canvas convention per Kevin directive ("storage panel SHOULD BE in screen space camera as well. every window base is in screen space camera"). Migrated UI_WindowBase.prefab (was Overlay), UI_StorageFurniturePanel.prefab (was WorldSpace), UI_SafePanel.prefab (was Overlay) to ScreenSpaceCamera with CanvasScaler ScaleWithScreenSize @1920x1080 + GraphicRaycaster + sortingOrder=50 + scale=(1,1,1). UI_WindowBase.Awake now assigns Camera.main to worldCamera at runtime — prefab assets can't reference scene cameras. Added gotcha notes for the zero-scale RectTransform inherited from the base + the don't-add-a-second-Canvas-in-Awake trap — claude

## Sources
- [UI_WindowBase.cs](../../Assets/Scripts/UI/UI_WindowBase.cs)
- [PlayerUI.cs](../../Assets/Scripts/UI/PlayerUI.cs)
- [UI_WindowBase.prefab](../../Assets/UI/Player%20HUD/UI_WindowBase.prefab)
- [.agent/skills/ui-hud/SKILL.md](../../.agent/skills/ui-hud/SKILL.md) — procedural how-to (authoring a new panel via MCP)
- [CLAUDE.md rule #39](../../CLAUDE.md#ui-hud-prefab-architecture)
