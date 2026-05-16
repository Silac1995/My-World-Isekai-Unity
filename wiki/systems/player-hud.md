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
The Player HUD is the single client-side UI layer that surfaces world state to the local owning player. Every player-facing panel (safe deposit/withdraw, storage chest, shop buy, building management, …) lives as a child of one scene-resident `UI_PlayerHUD` GameObject and is opened/closed exclusively through the [PlayerUI](../../Assets/Scripts/UI/PlayerUI.cs) singleton facade. Every panel inherits its window chrome (Canvas + Background + close-button wiring) from a single base prefab — [UI_WindowBase.prefab](../../Assets/UI/Player%20HUD/UI_WindowBase.prefab) — via Unity Prefab Variants. No panel re-implements the Canvas / GraphicRaycaster / CanvasScaler trio.

## Purpose
Three problems would be unsolved without this system:

1. **Consistency of window chrome.** Every panel needs the same Canvas setup, sorting order, scale mode, raycaster, and "click X to close" button wiring. Re-authoring those on every panel is error-prone and visually inconsistent.
2. **Single entry-point for opening UI.** Gameplay code (`Furniture.OnInteract`, `CharacterAction` ClientRpcs, hold-E menu options) must be able to open the right panel without each call site knowing prefab paths or Inspector wiring details. `PlayerUI.Instance.Open<Name>Panel(...)` is that entry-point.
3. **Discoverable diagnostics when wiring breaks.** When a designer forgets to assign a panel's `[SerializeField]` on `PlayerUI`, the Open call must log a directive warning so the diagnosis takes 5 seconds instead of an hour.

## Responsibilities
- Own the [UI_WindowBase.prefab](../../Assets/UI/Player%20HUD/UI_WindowBase.prefab) chrome base every panel inherits.
- Host every `UI_*Panel` script extending [UI_WindowBase](../../Assets/Scripts/UI/UI_WindowBase.cs).
- Expose `Open<Name>Panel(...)` / `Close<Name>Panel()` on `PlayerUI` as the singleton public surface.
- Log directive warnings when a panel's SerializeField is null.
- Coordinate panel z-order (each panel's Canvas has its own `sortingOrder`; PlayerUI assumes panels don't overlap by default).

**Non-responsibilities** (common misconceptions):
- Not responsible for input-driven character control — that's in [[player-controller]] / `PlayerController` (rule #33).
- Not responsible for the world-space interaction menu (the hold-E radial menu) — that's [[ui-interaction-menu]], a separate `UI_InteractionMenu` script driven by `InteractableObject.GetHoldInteractionOptions`.
- Not responsible for screen-space UI that isn't player-driven (loading screens, splash, debug overlays) — those live in their own scenes / canvases outside the HUD hierarchy.

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

- **Null SerializeField silently breaks the feature.** A panel whose `_<name>Panel` SerializeField on `PlayerUI` is null causes `Open<Name>Panel` to silently no-op (post the null-guard added per rule #39). The diagnosis is a `[PlayerUI]` warning in the Console — train your eye to scan for it. See the 2026-05-16 incident on `_safePanel`.
- **Variant override drift.** When you change `UI_WindowBase.prefab` (e.g. resize the close button), every variant inherits the change BUT any property the variant has explicitly overridden stays overridden. Use Unity's "Revert Override" on the variant to re-inherit after a base edit.
- **Play-mode wiring is volatile.** Wiring `PlayerUI._<name>Panel` while in Play mode does not persist — Unity reverts on exit. Always wire in Edit mode (or via `SerializedObject.ApplyModifiedPropertiesWithoutUndo + EditorSceneManager.SaveScene` from an Editor script).
- **Don't inherit twice.** Subclassing both `UI_WindowBase` AND adding a manual Canvas/GraphicRaycaster on the variant root is redundant (the base prefab already provides them). Doing this causes z-order conflicts and double-raycasts. Use the inherited chrome.
- **Out-of-zone auto-close needs `Time.unscaledDeltaTime`.** If a panel uses `Time.deltaTime`, it pauses when the GameSpeedController is at 0x (e.g. menu open). UI must use unscaled time (rule #26). Watch for the `MWI.Time` namespace shadowing `UnityEngine.Time` — fully qualify if your file's namespace causes resolution to pick `MWI.Time`.

## Open questions / TODO
- [ ] Should non-furniture screen-space HUD elements (health bars, minimap, quest tracker) also be `UI_WindowBase` variants? Today they're authored ad-hoc. Decide before the next HUD overhaul.
- [ ] Z-order policy when multiple panels can be open at once (e.g. storage + map). Today: each variant has its own Canvas sortingOrder; no coordinator. Revisit if panels start overlapping.

## Change log
- 2026-05-16 — created; documents the UI_WindowBase Prefab Variant convention formalised by Kevin during the SafeFurniture deposit/withdraw UI feature — claude

## Sources
- [UI_WindowBase.cs](../../Assets/Scripts/UI/UI_WindowBase.cs)
- [PlayerUI.cs](../../Assets/Scripts/UI/PlayerUI.cs)
- [UI_WindowBase.prefab](../../Assets/UI/Player%20HUD/UI_WindowBase.prefab)
- [.agent/skills/ui-hud/SKILL.md](../../.agent/skills/ui-hud/SKILL.md) — procedural how-to (authoring a new panel via MCP)
- [CLAUDE.md rule #39](../../CLAUDE.md#ui-hud-prefab-architecture)
