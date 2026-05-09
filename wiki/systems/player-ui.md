---
type: system
title: "Player UI"
tags: [ui, hud, tier-2]
created: 2026-04-19
updated: 2026-04-19
sources: []
related:
  - "[[character]]"
  - "[[combat]]"
  - "[[dialogue]]"
  - "[[items]]"
  - "[[party]]"
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

## Change log
- 2026-04-19 — Stub with full 49-file concern map. — Claude / [[kevin]]

## Sources
- [.agent/skills/player_ui/SKILL.md](../../.agent/skills/player_ui/SKILL.md)
- [.agent/skills/notification-system/SKILL.md](../../.agent/skills/notification-system/SKILL.md)
- [.agent/skills/toast-notification/SKILL.md](../../.agent/skills/toast-notification/SKILL.md)
- [.agent/skills/tooltip-system/SKILL.md](../../.agent/skills/tooltip-system/SKILL.md)
- `Assets/Scripts/UI/` (49 files).
- Root [CLAUDE.md](../../CLAUDE.md) rules #22, #25, #26.
