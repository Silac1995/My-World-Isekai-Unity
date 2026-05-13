---
type: system
title: "Player UI"
tags: [ui, hud, tier-2]
created: 2026-04-19
updated: 2026-05-13
sources: []
related:
  - "[[character]]"
  - "[[combat]]"
  - "[[dialogue]]"
  - "[[items]]"
  - "[[party]]"
  - "[[shops]]"
  - "[[tmp-inputfield-needs-text-subtree]]"
  - "[[kevin]]"
status: stable
confidence: medium
primary_agent: character-system-specialist
secondary_agents: []
owner_code_path: "Assets/Scripts/UI/"
depends_on:
  - "[[character]]"
depended_on_by: []
---

# Player UI

## Summary
The HUD and on-screen UI layer for the player: main menu, gameplay HUD, combat menu, dialogue prompts, inventory grid, party panel, health/XP bars, chat, notifications, invitation prompts, time UI, fade. 49 scripts grouped by concern. The architecture separates "HUD bound to the local player character" (e.g. `PlayerUI`) from "global/menu UI" (e.g. `MainMenu`, `ScreenFadeManager`).

## Purpose
Surface character state and gameplay actions to the player without coupling UI to gameplay logic. Players issue commands via HUD widgets that queue `CharacterAction`s — HUD **never** implements gameplay effects directly (project rule #22: shared action layer).

## Component map (by concern)

### HUD core
| File | Role |
|---|---|
| [PlayerUI.cs](../../Assets/Scripts/UI/PlayerUI.cs) | Local-player HUD binder; bound by `Character.SwitchToPlayer`. |
| [CharacterUI.cs](../../Assets/Scripts/UI/CharacterUI.cs) | Per-character HUD bootstrap. |
| [CharacterEquipmentUI.cs](../../Assets/Scripts/UI/CharacterEquipmentUI.cs) | Equipment slots view. |
| [UI_CharacterStats.cs](../../Assets/Scripts/UI/UI_CharacterStats.cs) | Character sheet / stat view. |
| [UI_CharacterRelations.cs](../../Assets/Scripts/UI/UI_CharacterRelations.cs) | Relations list. |
| [UI_HealthBar.cs](../../Assets/Scripts/UI/UI_HealthBar.cs) | Health indicator; shader-driven. |
| [UI_Action_ProgressBar.cs](../../Assets/Scripts/UI/UI_Action_ProgressBar.cs) | Action progress (crafting, building, etc.). |
| [UI_StorageFurniturePanel.cs](../../Assets/Scripts/UI/WorldUI/UI_StorageFurniturePanel.cs) | Storage-chest exchange window — canonical HUD-window pattern exemplar. |
| [UI_ShopBuyPanel.cs](../../Assets/Scripts/UI/Shop/UI_ShopBuyPanel.cs) + [UI_ShopBuyRow.cs](../../Assets/Scripts/UI/Shop/UI_ShopBuyRow.cs) | Player-facing shop buy panel — catalog rows + +/- stepper + Confirm. See [[shops]]. |

### Combat
| File | Role |
|---|---|
| [UI_CombatActionMenu.cs](../../Assets/Scripts/UI/UI_CombatActionMenu.cs) | Declare attack / ability intents. Validates PlannedTarget. |
| [UI_CombatExpBar.cs](../../Assets/Scripts/UI/UI_CombatExpBar.cs) | XP / level-up UI. |

### Dialogue / social
| File | Role |
|---|---|
| `UI/Dialogue/*` | Dialogue prompts, advance hints. |
| [UI_ChatBar.cs](../../Assets/Scripts/UI/UI_ChatBar.cs) | Multiplayer chat. |
| [UI_InvitationPrompt.cs](../../Assets/Scripts/UI/UI_InvitationPrompt.cs) | Accept/decline party/trade/lesson invites. |
| [UI_PartyMemberSlot.cs](../../Assets/Scripts/UI/UI_PartyMemberSlot.cs), [UI_PartyPanel.cs](../../Assets/Scripts/UI/UI_PartyPanel.cs) | Party roster. |

### World / time
| File | Role |
|---|---|
| [TimeUI.cs](../../Assets/Scripts/UI/TimeUI.cs) | Clock / day display (unscaled). |
| [UI_GameSpeedController.cs](../../Assets/Scripts/UI/UI_GameSpeedController.cs) | Player-facing speed dial. |
| [ScreenFadeManager.cs](../../Assets/Scripts/UI/ScreenFadeManager.cs) | Transition fades — real-time unscaled. |

### Buildings
| File | Role |
|---|---|
| `UI/Building/*` | Placement ghosts, contribute-material UI. |
| `UI/Crafting/*` | Crafting station UI. |

### Notifications
| File | Role |
|---|---|
| `UI/Notifications/*` | Toast + persistent notifications. |

### Menus / global
| File | Role |
|---|---|
| [MainMenu.cs](../../Assets/Scripts/UI/MainMenu.cs) | Session entry point. |
| `UI/Core/*` | Base UI behaviour + common widgets. |

## Rules

- **No gameplay logic in UI** (project rule #22) — HUD queues `CharacterAction`s; it never applies effects.
- **UI uses unscaled time** (project rule #26) — menus, HUD animations, real-time bars must be immune to [[game-speed-controller]] scale.
- **Shader-first dynamic visuals** (project rule #25) — fill amounts via shader/MPB, not `Image.fillAmount`.

## Open questions / TODO

- [ ] Document how each HUD rebinds during `Character.SwitchToPlayer` / `SwitchToNPC`.
- [ ] Networked UI state — what replicates vs what's local?
- [ ] A future HUD/UI agent may be created (see memory `project_future_agents`) — refer to it here when it exists.

## HUD-window pattern (canonical)

Every panel that opens on top of the HUD follows the same recipe (exemplars: `UI_StorageFurniturePanel`, `UI_ShopBuyPanel`):

1. **Inherits `UI_WindowBase`** — picks up auto-wired close-button binding + `OpenWindow/CloseWindow` lifecycle.
2. **Scene-embedded as a deactivated child of `UI_PlayerHUD/Canvas`** — NOT a runtime `Resources.Load + Instantiate`. The child inherits the HUD root Canvas's `ScreenSpaceOverlay` render mode for free; a per-panel Canvas with `overrideSorting=true sortingOrder=50` lets it sort above siblings.
3. **`PlayerUI` holds a SerializeField reference** (`_storagePanel`, `_shopBuyPanel`, etc.) + an `OpenXxxPanel(...)` / `CloseXxxPanel()` method pair that delegates to `panel.Initialize(...)` / `panel.CloseWindow()`.
4. **Trigger callers** (e.g. `CashierNetSync.OpenBuyPanelClientRpc`, `StorageFurniture.OnInteract`) call `PlayerUI.Instance.OpenXxxPanel(...)` — never `Resources.Load` directly.
5. **RectTransform** anchored centered with fixed size (storage / shop both use 720×540). ScrollRect Viewport stretches to fill its parent (`anchorMin (0,0) → anchorMax (1,1)`, pivot `(0,1)`); Content top-stretches in Viewport (`anchorMin (0,1) → anchorMax (1,1)`, pivot `(0.5, 1)`) with `VerticalLayoutGroup` + `ContentSizeFitter` for dynamic row stacks.
6. **Defensive `Awake` guard** in the script forces `Canvas + GraphicRaycaster` with `overrideSorting=true sortingOrder=50` so the panel renders/raycasts independently if the prefab override propagation misses these.

**The alternate pattern** (`UI_OwnerManagementPanel`-style: `Resources.Load` + `Instantiate(prefab, PlayerUI.Instance.HudCanvas.transform, false)`) is reserved for **modal popups** that appear on demand from many call sites and are not part of the persistent HUD tree.

**Authoring caveats** for HUD prefabs built via MCP / reflection:
- `TMP_InputField` needs its `Text Area / Text` subtree built manually — see [[tmp-inputfield-needs-text-subtree]].
- Canvas `renderMode` field is per-Canvas. On the prefab asset, leaving it at the default (often `WorldSpace`) is safe **only** when the panel is nested under another Canvas (the HUD root Canvas's mode wins for the render path). For standalone `Resources.Load` panels with no parent Canvas, the prefab Canvas must be `ScreenSpaceOverlay`.

## Change log
- 2026-05-13 — Added `UI_ShopBuyPanel` + `UI_ShopBuyRow` to the HUD windows list. New "HUD-window pattern" section consolidating the canonical recipe used by `UI_StorageFurniturePanel` and `UI_ShopBuyPanel`. Linked the [[tmp-inputfield-needs-text-subtree]] authoring gotcha. — Claude / [[kevin]]
- 2026-04-19 — Stub with full 49-file concern map. — Claude / [[kevin]]

## Sources
- [.agent/skills/player_ui/SKILL.md](../../.agent/skills/player_ui/SKILL.md)
- [.agent/skills/notification-system/SKILL.md](../../.agent/skills/notification-system/SKILL.md)
- [.agent/skills/toast-notification/SKILL.md](../../.agent/skills/toast-notification/SKILL.md)
- [.agent/skills/tooltip-system/SKILL.md](../../.agent/skills/tooltip-system/SKILL.md)
- `Assets/Scripts/UI/` (49 files).
- Root [CLAUDE.md](../../CLAUDE.md) rules #22, #25, #26.
