---
type: system
title: "Combat"
tags: [combat, gameplay, multiplayer, tier-1]
created: 2026-04-18
updated: 2026-04-18
sources: []
related:
  - "[[character]]"
  - "[[character-stats]]"
  - "[[character-movement]]"
  - "[[character-mentorship]]"
  - "[[items]]"
  - "[[kevin]]"
status: stable
confidence: high
primary_agent: combat-gameplay-architect
secondary_agents:
  - character-system-specialist
  - network-specialist
owner_code_path: "Assets/Scripts/BattleManager/"
depends_on:
  - "[[character]]"
  - "[[character-stats]]"
  - "[[character-movement]]"
  - "[[items]]"
  - "[[network]]"
depended_on_by:
  - "[[ai]]"
  - "[[character-mentorship]]"
  - "[[player-ui]]"
---

# Combat

## Summary
Turn-paced, initiative-driven combat for melee, ranged, ability, and spell actions. A single `BattleManager` orchestrates two opposing teams, delegates spatial concerns to `CombatEngagementCoordinator` + `BattleZoneController`, and ticks per-character `Initiative` until each participant is ready to act. Per-character logic (intent, targeting, attack execution) lives in `CharacterCombat`, shared between players and NPCs via `CombatAILogic`. Network authority is server-side with owner prediction; visual-only features (ground circles, target crosshair) are local to the owner.

## Purpose
Drive all damaging interactions in the game — character-vs-character, character-vs-destructible (`IDamageable`), and ability/spell casting — under one ticked, authoritative lifecycle that both players and NPCs plug into without branching logic.

## Responsibilities
- Creating, orchestrating, and tearing down battles (`BattleManager`).
- Grouping participants into spatial sub-fights and formations (`CombatEngagementCoordinator`, `CombatEngagement`, `EngagementGroup`, `CombatFormation`).
- Ticking per-character Initiative and awarding ready-to-act opportunities.
- Resolving intents (`PlannedAction`, `PlannedTarget`) into executed actions.
- Computing damage from `WeaponSO` + `CombatStyleSO` + `CharacterStats` primary/tertiary values.
- Granting combat XP proportional to HP removed, level-balanced, bonus on kill.
- Applying knockback without letting movement systems override it mid-window.
- Running the 9-trigger passive ability system on server events (damage, kill, dodge, battle-start, low-HP, etc.).
- Broadcasting visual-only combat state (ground circles, fade, target indicator) to owners/observers.

**Non-responsibilities** (common misconceptions):
- Not responsible for learning abilities — that's [[character-mentorship]] and books (see [[items]]).
- Not responsible for weapon data — weapon stats live in [[items]] (`WeaponSO`).
- Not responsible for stat math — all primary/secondary/tertiary math is in [[character-stats]].
- Not responsible for pathfinding — only for positioning intent; actual movement delegates to [[character-movement]] and [[ai]].

## Key classes / files

### Orchestration layer
| File | Role |
|------|------|
| [BattleManager.cs](../../Assets/Scripts/BattleManager/BattleManager.cs) | `NetworkBehaviour` — supreme battle entity. Ticks initiative, polls victory, hands out engagement slots. |
| [BattleTeam.cs](../../Assets/Scripts/BattleManager/BattleTeam.cs) | Roster of characters on one side; detects full elimination. |
| [BattleZoneController.cs](../../Assets/Scripts/BattleManager/BattleZoneController.cs) | Dynamic `BoxCollider` + `NavMeshModifierVolume` + `LineRenderer` boundary. |
| [BattleZoneOutline.cs](../../Assets/Scripts/BattleManager/BattleZoneOutline.cs) | Visual outline for the zone boundary. |
| [CombatEngagementCoordinator.cs](../../Assets/Scripts/BattleManager/CombatEngagementCoordinator.cs) | Targeting-graph grouping; merges/splits sub-fights. |
| [CombatEngagement.cs](../../Assets/Scripts/BattleManager/CombatEngagement.cs) | One spatial sub-fight; holds two `EngagementGroup`s. |
| [EngagementGroup.cs](../../Assets/Scripts/BattleManager/EngagementGroup.cs) | Per-team slice of an engagement; owns a `CombatFormation`. |
| [CombatFormation.cs](../../Assets/Scripts/BattleManager/CombatFormation.cs) | Organic role-based positioning within a group. |

### Per-character layer
| File | Role |
|------|------|
| [CharacterCombat.cs](../../Assets/Scripts/Character/CharacterCombat/CharacterCombat.cs) | `CharacterSystem` — local combat state: mode, planned action/target, weapon, attack execution. |
| [CombatStyleAttack.cs](../../Assets/Scripts/Character/CharacterCombat/CombatStyleAttack.cs) | Hitbox spawned by melee animation events; applies damage (including `IDamageable`). |
| [CombatStyleExpertise.cs](../../Assets/Scripts/Character/CharacterCombat/CombatStyleExpertise.cs) | Per-weapon-type mastery + XP tracking on the character. |
| [CombatTacticalPacer.cs](../../Assets/Scripts/Character/CharacterCombat/CombatTacticalPacer.cs) | Waiting-state movement (post-attack step-back, unengaged follow). |
| [Projectile.cs](../../Assets/Scripts/Character/CharacterCombat/Projectile.cs) | `Rigidbody`-based ranged projectile; damage + knockback on `OnTriggerEnter`. |

### Shared AI brain
| File | Role |
|------|------|
| [CombatAILogic.cs](../../Assets/Scripts/AI/CombatAILogic.cs) | Pure C# — single tick function used by both `PlayerController` and NPC drivers; sets intent, moves into range, executes when ready. |

### Abilities & status
| File | Role |
|------|------|
| [Assets/Scripts/Abilities/](../../Assets/Scripts/Abilities/) | `AbilitySO` hierarchy (`PhysicalAbilitySO`, `SpellSO`, `PassiveAbilitySO`), `Runtime/` instance classes, `Learning/` glue. |
| [Assets/Scripts/StatusEffect/](../../Assets/Scripts/StatusEffect/) | `PeriodicStatEffect`, `StatModifierEffect`, instance classes. Consumed by `CharacterStatusManager`. |

### Damage interface
| File | Role |
|------|------|
| [Combat/IDamageable.cs](../../Assets/Scripts/Combat/IDamageable.cs) | Interface for non-Character damageables (e.g. doors). `CombatStyleAttack` checks it as a fallback. |

### Visual-only (local)
| File | Role |
|------|------|
| [BattleCircleManager.cs](../../Assets/Scripts/BattleManager/BattleCircleManager.cs) | `CharacterSystem` on the Character prefab; only active for `IsOwner`. Spawns/despawns ground-circle decals. |
| [BattleGroundCircle.cs](../../Assets/Scripts/BattleManager/BattleGroundCircle.cs) | `MonoBehaviour` on a prefab. One `DecalProjector`; fades in on spawn, dims on incapacitation, fades out on cleanup. |

## Public API / entry points

**Battle lifecycle**
- `BattleManager.StartBattle(initiator, target)` — server-only. Creates teams, zone, coordinator.
- `BattleManager.EndBattle()` — wraps `LeaveBattle` in try/catch to quarantine UI exceptions.
- `BattleManager.PerformBattleTick()` — drives `UpdateInitiativeTick` on every active participant.

**Per-character**
- `CharacterCombat.SetActionIntent(action, target)` — sets `PlannedAction` + `PlannedTarget`, syncs look target, informs coordinator.
- `CharacterCombat.SetPlannedTarget(character)` — target-only redirect (click/TAB) without cancelling queued action.
- `CharacterCombat.IsReadyToAct` — initiative bar full.
- `CharacterCombat.ConsumeInitiative()` — reset to 0 after successful attack.
- `CharacterCombat.Attack(target)` / `CharacterCombat.UseAbility(slotIndex, target)` — entry points for execution. Both follow Owner-predict → Server-validate → Broadcast RPC.
- `CharacterCombat.JoinBattle(BattleManager)` / `LeaveBattle()` — registration.
- `CharacterCombat.OnBattleJoined` / `OnBattleLeft` — events (on all clients).

**Ability casting**
- `CharacterAbilities` component owns 6 active + 4 passive slots.
- `AbilityInstance.CanUse()` / `TryTrigger()` — resource + cooldown + (for passives) trigger condition.

**Damage**
- `Character.TakeDamage(damage, attacker)` — centralized for standard hits, DoTs, spells. Awards XP proportional to HP depleted.
- `IDamageable.TakeDamage(damage, attacker)` — for non-character targets.

## Data flow

```
Player input or NPC AI
       │
       ▼
CharacterCombat.SetActionIntent(action, target)
       │
       ▼
CombatAILogic.Tick(target)  ← shared player/NPC brain
       │  (move into range if needed)
       │
       ├─ IsReadyToAct? ──► ExecuteAction (Owner-predict)
       │                           │
       │                           ▼
       │                   Server validates
       │                           │
       │                           ▼
       │                   Broadcast RPC
       │                           │
       │                           ▼
       │                   CombatStyleAttack / Projectile / Ability
       │                           │
       │                           ▼
       │                   target.TakeDamage  ──►  CharacterStats.Health
       │                           │                     │
       │                           ▼                     ▼
       │                   XP proportional to     Status effects
       │                   HP depleted           applied if any
       │                           │
       │                           ▼
       │                   CharacterCombatLevel.LevelUp if threshold
       │
       └─ Else ──► CombatTacticalPacer.GetTacticalDestination
```

Battle-wide tick:
```
BattleManager.Update()
       │
       ├─ Poll BattleTeam.IsTeamEliminated → end if true
       │
       └─ PerformBattleTick()
               │
               └─ for each participant: CharacterCombat.UpdateInitiativeTick(amount)
                       │
                       └─ fires OnInitiativeFull → PassiveAbility trigger
```

**Authority model** (server-authoritative per [[network]]):
- `BattleManager` is a `NetworkBehaviour`; spawns server-side.
- Initiative, planned actions, damage, XP, status effect application are all server-side.
- Client owners predict their own attack animation; server validates then broadcasts.
- Target indicator, ground circles, zone outline are **visual-only on owner**.

## Dependencies

### Upstream (this system needs)
- [[character]] — the facade that owns `CharacterCombat`.
- [[character-stats]] — Initiative primary stat; PhysicalPower, MoveSpeed, DodgeChance, CriticalHitChance tertiary stats; HP/Stamina/Mana.
- [[character-movement]] — knockback via `ApplyKnockback`, NavMesh disable/re-enable window, range positioning.
- [[items]] — `WeaponSO` (DamageType, MaxDurability, MaxSharpness, MagazineSize), `WeaponInstance` (Sharpness, ChargeProgress, CurrentAmmo), `CombatStyleSO` hierarchy (data lives in `Assets/Resources/Data/CombatStyle/`, prefabs in `Assets/Prefabs/CombatStyles/`).
- [[network]] — server authority, owner prediction, RPC broadcast pattern.
- [[character-mentorship]] — ability learning gates what abilities can be used in combat.

### Downstream (systems that need this)
- [[ai]] — NPC behaviour trees hand combat decisions to `CombatAILogic`.
- [[player-ui]] — `UI_CombatActionMenu`, `UI_CombatExpBar`, `UI_HealthBar`, `UI_PlayerTargeting` consume combat events.
- [[character-mentorship]] — XP awards and kill counts feed mentorship progression.
- [[world]] — `DoorHealth` implements `IDamageable` to consume combat damage.

## State & persistence

### Runtime
- `BattleManager` is spawned server-side on `StartBattle`, destroyed on `EndBattle`.
- `CharacterCombat` instance state: `CombatMode`, `PlannedAction`, `PlannedTarget`, Initiative (mirrored in `CharacterStats`), current weapon.
- `CombatEngagementCoordinator._activeEngagements`, `CombatEngagement._targetingGraph` — rebuild every battle.
- `BattleCircleManager._circles` — owner-local dictionary; no net sync.

### Persisted
- **Combat-earned stats** persist through [[character-stats]] and the character profile (see [[save-load]]).
- `CombatLevelEntry` history, unspent stat points, and `CombatStyleExpertise` XP all save via `ICharacterSaveData<T>` on the relevant character systems.
- **Nothing about in-progress battles persists** — if a player leaves mid-combat (portal gate / bed), the battle ends server-side; abandoned NPCs revert to their schedule per [[ai]].

## Known gotchas / edge cases

- **Knockback override** — `PlayerController.Move()` must respect `_character.CharacterMovement.IsKnockedBack`, otherwise it re-enables the NavMeshAgent mid-window and zeros the knockback. (SKILL.md §11.)
- **Generic animation override** — every `CharacterAction` used in combat must set `ShouldPlayGenericActionAnimation => false`, or the "busy" animation clobbers the specific attack trigger. (SKILL.md §3 NOTE.)
- **Target indicator during battle** — `ClearSelection` must redirect to `PlannedTarget` / `GetBestTargetFor` fallback rather than clearing; clearing mid-fight breaks the crosshair color feedback.
- **Robust teardown** — `LeaveBattle` calls wrapped in try/catch so a throwing `PlayerUI` doesn't abort the shutdown script. `BattleManager.OnDestroy` unsubscribes all character events.
- **PlannedTarget closure** — attack closure is `() => Attack(_characterCombat.PlannedTarget)` (dynamic), not a captured target. Without this, retargeting after queuing an attack hits the wrong character.
- **Stamina out-of-breath** — fully depleting stamina applies the Out of Breath status (initiative fills slower, −70% physical damage). Removed when stamina hits max. Requires `_outOfBreathEffect` assigned on `CharacterStatusManager`.
- **Server-only passive evaluation** — `IsServer` guard in `CharacterAbilities.TryTriggerPassives` prevents client-side desync. Passive pages listening on `OnDamageTaken` etc. must respect this.
- **`Assets/Scripts/CombatStyles/` is an empty placeholder** — all CombatStyleSO types live under `Assets/Resources/Data/CombatStyle/`; prefabs under `Assets/Prefabs/CombatStyles/`. See `## Open questions` below.

## Open questions / TODO

- [ ] `Assets/Scripts/CombatStyles/` is empty. Either delete the folder or populate it. Logged as a discovery — not blocking combat functionality.
- [ ] Abilities learning currently flows through [[character-mentorship]] + book items. Are there plans to add crafting-based ability unlock, or will it stay mentorship + items? (Q6 deferred.)
- [ ] [[items]] page should describe `WeaponSO`/`CombatStyleSO`/`WeaponInstance` hierarchy so combat.md can link to it cleanly instead of listing it here. Flag when items.md is written.
- [ ] Sub-pages not yet written — see linking plan below.

## Child sub-pages (to be written in Batch 2)

- [[combat-battle-manager]] — orchestration layer deep dive (BattleManager, teams, zone, tick loop).
- [[combat-engagement]] — engagement grouping math and formation layer.
- [[combat-damage]] — damage formula, weapon/style layering, damage types, `IDamageable`.
- [[combat-ai-logic]] — `CombatAILogic` shared brain, phase 1/2/3.
- [[combat-abilities]] — active (physical/spell), passive (9 triggers), support, stat restores.
- [[combat-status-effect]] — `PeriodicStatEffect`, `StatModifierEffect`, application lifecycle.
- [[combat-circle-indicators]] — visual-only owner-local ground circles.

## Change log
- 2026-04-18 — Initial documentation pass (wiki bootstrap). Based on current SKILL.md + code read on `docs/llm-wiki-bootstrap`. — Claude / [[kevin]]

## Sources
- [.agent/skills/combat_system/SKILL.md](../../.agent/skills/combat_system/SKILL.md) — procedural how-to (primary operational source).
- [.claude/agents/combat-gameplay-architect.md](../../.claude/agents/combat-gameplay-architect.md) — specialist agent, architecture overview.
- [BattleManager.cs](../../Assets/Scripts/BattleManager/BattleManager.cs) — supreme orchestrator.
- [CharacterCombat.cs](../../Assets/Scripts/Character/CharacterCombat/CharacterCombat.cs) — per-character combat state.
- [CombatAILogic.cs](../../Assets/Scripts/AI/CombatAILogic.cs) — shared player/NPC combat brain.
- [Assets/Scripts/Abilities/](../../Assets/Scripts/Abilities/) — ability definitions and runtime.
- [Assets/Scripts/StatusEffect/](../../Assets/Scripts/StatusEffect/) — status effect definitions.
- [Combat/IDamageable.cs](../../Assets/Scripts/Combat/IDamageable.cs) — damageable interface for non-characters.
- 2026-04-18 conversation with [[kevin]] — tier-1 classification, parent+children split, CombatStyles ambiguity resolved (data layer only).
