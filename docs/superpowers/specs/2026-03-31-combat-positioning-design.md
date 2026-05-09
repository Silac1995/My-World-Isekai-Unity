# Combat Positioning & Movement Rework Design

**Date:** 2026-03-31  
**Status:** Approved  
**Scope:** BattleManager combat positioning, engagement grouping, character movement, and facing direction

## Problem

The current combat system has three issues:

1. **Facing bug (1v5 flip-flop)** — When multiple attackers target a defender, the defender's facing direction oscillates rapidly because three competing systems fight over it: `CombatAILogic.UpdateFlip()`, `CharacterVisual.LateUpdate()` (via `_lookTarget`), and `SetPlannedTarget()` which overwrites `_lookTarget` through `SetActionIntent → SetLookTarget`. Each attacker calling `SetPlannedTarget(defender)` changes the *defender's* facing, not just the attacker's.

2. **Messy positioning** — The current `CombatFormation` uses fixed concentric ring slots (4 melee + 8 mid-range + 12 ranged) assigned deterministically by `GetInstanceID()`. Characters teleport to rigid slots, producing mechanical, grid-like placement that lacks organic combat feel.

3. **Proximity-based engagements** — `CombatEngagementCoordinator` groups characters by physical distance (10m merge radius) rather than targeting relationships. This creates nonsensical groupings where characters fighting different opponents get lumped together simply because they're nearby.

## Design Goals

- **Tactical brawl feel** — Organic movement with loose formation discipline (blend of Fire Emblem real-time and Mount & Blade group combat)
- **Relationship-based engagements** — Engagements form from who-targets-whom, not proximity
- **Single facing authority** — A character's facing is controlled only by their own target selection
- **Ranged realism** — Ranged characters keep distance but do NOT flee when approached (they can be hit)
- **Dynamic positions** — No fixed "home" slots; positions evolve naturally through combat

## Part 1: Facing Bug Fix

### Principle

A character's facing direction is determined **solely** by their own chosen target. When ATK 2 calls `SetPlannedTarget(defender)`, it changes ATK 2's facing — never the defender's.

### Changes

1. **Remove cross-character facing interference** — `SetActionIntent()` must only call `SetLookTarget()` on the *acting character*, never on the target. Currently the call chain `SetPlannedTarget → SetActionIntent → SetLookTarget` can overwrite another character's `_lookTarget`.

2. **Single facing source** — Remove `CombatAILogic.UpdateFlip()` competing with `CharacterVisual.LateUpdate()`. Consolidate to one system: `CharacterVisual.LateUpdate()` reads the character's own `PlannedTarget` position and orients accordingly. No other system writes facing during combat.

3. **Facing ownership** — Only the character's own AI (via `CombatAILogic.DecideAbilityOrAttack`) can change who they face, by changing their own `PlannedTarget`. External systems (other characters targeting this one) must never modify facing state.

## Part 2: Engagement System Rework

### Core Concept

Engagements are built from the **targeting graph** — who targets whom — rather than physical proximity. The system evaluates all active targeting relationships each tick and forms/merges/splits engagements accordingly.

### Rules

**Rule 1 — FORM:** Two characters mutually targeting each other create an engagement. Mutual targeting is *required* — one-way targeting does not form an engagement.

**Rule 2 — JOIN:** If a character targets someone who is already in an engagement, they join that engagement (regardless of whether their target reciprocates within the engagement context).

**Rule 3 — SPLIT:** When a subgroup within an engagement forms a self-contained targeting cluster (their targets are all within the subgroup), they split into their own engagement. Example: In a 5-person engagement {A, B, C, D, E}, if B and C both retarget E and E is targeting one of B/C, the subgroup {B, C, E} splits off, leaving {A, D}.

**Rule 4 — FOLLOW:** When engagements split, characters follow their target. If F was in Engagement 1 targeting B, and B splits into Engagement 2, F moves to Engagement 2.

**Rule 5 — TARGET DEATH:** When a character's target dies, the AI immediately auto-acquires a new target. If the new target is in an engagement, the character joins it. If the new target is unengaged and reciprocates, a new engagement forms.

### Unengaged Characters

No mutual targeting = no engagement. An unengaged character who has chosen a target will **follow that target at a distance** — melee follows at melee range distance, ranged moves into weapon range. They face their chosen target and wait for the target to reciprocate, at which point the engagement snaps into existence. An unengaged character with no target barely moves.

### Algorithm

Each tick, the engagement coordinator:
1. Collects all active targeting pairs (attacker → target)
2. Identifies mutual pairs (A → B AND B → A)
3. Builds connected components from mutual pairs + join edges (one-way targeting into existing engagement)
4. Compares new components against existing engagements
5. Creates, merges, or splits engagements as needed
6. Characters whose engagement changed receive position reassignment

## Part 3: Spatial Positioning

### Soft Anchor Zone

Each engagement has a **center anchor point** set where the engagement first formed. Characters can drift organically within the zone but are gently pulled back if they stray beyond a **leash radius** (~15m). The fight stays roughly where it began without feeling locked to a grid.

### Organic Positioning (replaces CombatFormation slots)

Instead of fixed ring slots, characters position themselves based on role and relationship:

- **Melee characters** position close to their target, on opposite sides of the engagement center from their team's opponents. After attacking, they back off slightly to create a natural gap (dynamic spacing) — they do NOT return to a fixed home position. Their position evolves organically over the course of the fight.

- **Ranged characters** position further from opponents, behind their melee allies relative to the engagement center. They maintain weapon-range distance from their target. They shoot from their current position without approaching.

- **Initial approach** — When an engagement forms, both sides walk toward each other. Melee closes most of the gap; ranged stops at their weapon's preferred distance.

### Outnumbering & Circling

When one side of an engagement outnumbers the other **2:1 or greater**, melee characters on the larger side slowly **orbit/circle** around the outnumbered enemies, creating a natural surrounding effect. This is purely visual/positional — it doesn't affect combat mechanics.

## Part 4: Movement Behaviors by State

### Waiting (Initiative Charging)

- **Idle sway:** Subtle random drifting (~0.5-1m radius) around current position. Perlin noise or smooth random for natural feel.
- **Tactical circling:** If outnumbering 2:1+, melee characters slowly orbit the outnumbered side.
- Stay within soft anchor leash radius.

### Attacking (Turn Active)

- **Melee:** Advance into strike range of target, perform attack animation, then back off slightly to create a reset gap. No fixed return position — new "home" is wherever they end up after the step-back.
- **Ranged:** Shoot from current position. No movement during attack turn.

### Unengaged (No Mutual Target)

- **Has a target (one-way):** Follow target at appropriate distance (melee range or weapon range). Face the target.
- **No target:** Barely move. Stay near starting position.

### Target Died

- AI immediately picks a new target.
- Join the new target's engagement (if any) or form a new one (if mutual).
- Begin closing distance toward new engagement position.

## Part 5: Ranged Character Rules

1. Ranged characters position further from opponents (back of engagement, weapon-range distance).
2. When their target approaches them to attack, **they do NOT run or kite**. They hold ground and take the hit.
3. They only reposition to maintain range **after their own attack turn**, not reactively.
4. This ensures ranged characters can actually be killed in melee — they are not infinitely evasive.

## Part 6: Scope of Changes

### Heavy Rework

| File | Change |
|------|--------|
| `CombatEngagementCoordinator.cs` | Replace proximity-based grouping with relationship-based targeting graph algorithm |
| `CombatFormation.cs` | Replace fixed ring slots with dynamic organic positioning based on role and relationship |
| `CombatTacticalPacer.cs` | Rework for dynamic spacing, idle sway, tactical circling, approach/step-back behavior |
| `CharacterVisual.cs` (facing) | Single source of truth — character faces own target only, remove competing systems |
| `CombatAILogic.cs` (facing) | Remove `UpdateFlip()` competing with CharacterVisual. Facing delegated entirely to CharacterVisual |

### Keep / Light Touch

| File | Change |
|------|--------|
| `BattleManager.cs` | Tick system, initiative, team management unchanged. Coordinator API calls may change signatures |
| `CombatEngagement.cs` | Two-group structure stays. Internal position assignment changes to use new organic positioning |
| `EngagementGroup.cs` | Member tracking and center calculation stays |
| `CharacterCombat.cs` | Action/intent system stays. `SetPlannedTarget` decoupled from affecting other characters' facing |

## Network Considerations

- Engagement grouping runs **server-side only**. Clients receive engagement assignments via existing sync mechanisms.
- Facing direction is already synced via `_netIsFacingRight` NetworkVariable — the fix only changes what *drives* the value, not how it syncs.
- Position movement uses existing NavMesh pathfinding and NetworkTransform sync. No new network infrastructure needed.
- All movement behaviors (idle sway, circling, approach) are server-authoritative position changes that replicate through NetworkTransform.
