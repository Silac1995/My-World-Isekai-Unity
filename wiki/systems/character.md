---
type: system
title: "Character"
tags: [character, facade, gameplay, tier-1]
created: 2026-04-18
updated: 2026-04-24
sources: []
related:
  - "[[combat]]"
  - "[[ai]]"
  - "[[party]]"
  - "[[social]]"
  - "[[items]]"
  - "[[save-load]]"
  - "[[network]]"
  - "[[visuals]]"
  - "[[kevin]]"
status: stable
confidence: high
primary_agent: character-system-specialist
secondary_agents:
  - character-social-architect
  - combat-gameplay-architect
owner_code_path: "Assets/Scripts/Character/"
depends_on:
  - "[[network]]"
  - "[[save-load]]"
depended_on_by:
  - "[[combat]]"
  - "[[ai]]"
  - "[[party]]"
  - "[[social]]"
  - "[[items]]"
  - "[[jobs-and-logistics]]"
  - "[[dialogue]]"
  - "[[player-ui]]"
---

# Character

## Summary
The Character is the single entity model for **every** humanoid in the game — player and NPC. The root `Character.cs` script is a **facade** that holds typed references to every subsystem (combat, needs, movement, equipment, party, interaction, job, stats, etc.). A player is exactly like an NPC: same prefab, same subsystems, differing only in which controller (PlayerController vs NPCController) is active at the moment.

## Purpose
Give the project one substitutable "living being" type so any gameplay feature (combat, dialogue, AI, party, schedule, save/load, networking) has a single surface to target. Enforce strict modular decomposition — each concern lives on its own child GameObject — while keeping a central dependency graph through the facade.

## Responsibilities
- Hosting the facade `Character.cs` with typed getters for every subsystem.
- Owning the lifecycle (`SetUnconscious`, `Die`, `WakeUp`) and the availability check `IsFree(out CharacterBusyReason)`.
- Switching between Player and NPC control (`SwitchToPlayer` / `SwitchToNPC`).
- Announcing lifecycle events (`OnIncapacitated`, `OnDeath`, `OnWakeUp`, `OnCombatStateChanged`) that subsystems inheriting from `CharacterSystem` react to independently.
- Driving the `CharacterAction` pipeline — the shared gameplay-effect layer for players and NPCs.

**Non-responsibilities** (common misconceptions):
- Does **not** implement any gameplay math — delegated to [[character-stats]], [[combat]], etc.
- Does **not** cache references across subsystems — each subsystem `[SerializeField]`s siblings it needs, never goes through the facade at runtime.
- Does **not** own visuals — see [[visuals]].
- Does **not** own save serialization directly — delegates to [[save-load]] via `ICharacterSaveData<T>` providers.

## Key classes / files

### Facade & lifecycle
| File | Role |
|------|------|
| [Character.cs](../../Assets/Scripts/Character/Character.cs) | Root facade. Holds every subsystem getter; owns incapacitation/death. |
| [CharacterActions.cs](../../Assets/Scripts/Character/CharacterActions/CharacterActions.cs) | Hosts `CharacterAction` lifecycle + server RPCs (`RequestDespawnServerRpc`, `RequestCraftServerRpc`, `RequestHarvestServerRpc`, `RequestItemDropServerRpc`, furniture place/pickup). |

### Controllers
| File | Role |
|------|------|
| [PlayerController.cs](../../Assets/Scripts/Character/CharacterControllers/PlayerController.cs) | Human input + HUD binding. |
| [NPCController.cs](../../Assets/Scripts/Character/CharacterControllers/NPCController.cs) | BT + GOAP driver. |

### Subsystem child GameObjects (each is its own page)
| Subsystem | Folder | Page |
|---|---|---|
| Stats | `Character/CharacterStats/` | [[character-stats]] |
| Needs | `Character/CharacterNeeds/` | [[character-needs]] |
| Skills | `Character/CharacterSkills/` | [[character-skills]] |
| Abilities | `Character/CharacterAbilities/` | (child of [[combat]] — [[combat-abilities]]) |
| Traits | `Character/CharacterTraits/` | [[character-traits]] |
| Equipment | `Character/CharacterEquipment/` | [[character-equipment]] (child of [[items]]) |
| Combat | `Character/CharacterCombat/` | [[character-combat]] (child of [[combat]]) |
| Movement | `Character/CharacterMovement/` | [[character-movement]] |
| Interaction | `Character/CharacterInteraction/` | [[character-interaction]] (child of [[social]]) |
| Invitation | `Character/CharacterInvitation/` | [[character-invitation]] (child of [[social]]) |
| Mentorship | (see SKILL.md) | [[character-mentorship]] (child of [[social]]) |
| Bio | `Character/CharacterBio/` | [[character-bio]] |
| Visuals / Body parts | `Character/CharacterBodyPartsController/` | (child of [[visuals]]) |
| Animation sync | `Character/AnimationSync/` | (child of [[visuals]]) |
| Schedule | `Character/CharacterSchedule/` | [[character-schedule]] (child of [[ai]]) |
| Job | `Character/CharacterJob/` | [[character-job]] (child of [[jobs-and-logistics]]) |
| Party | `Character/CharacterParty/` | [[character-party]] (child of [[party]]) |
| Relation | `Character/CharacterRelation/` | [[character-relation]] (bidirectional link to [[social]]) |
| Progression | `Character/CharacterProgression/` | [[character-progression]] |
| Profile | `Character/CharacterProfile/` | [[character-profile]] (child of [[save-load]]) |
| Speech | `Character/CharacterSpeech/` | [[character-speech]] (child of [[dialogue]]) |
| Book knowledge | `Character/CharacterBookKnowledge.cs` | [[character-book-knowledge]] |
| Community | `Character/CharacterCommunity/` | [[character-community]] (adapter — see [[world]] `world-community`) |
| Blueprints | `Character/CharacterBlueprints/` | [[character-blueprints]] |
| Archetype | `Character/Archetype/` | [[character-archetype]] (stub — code on feature branch) |
| Terrain | `Character/CharacterTerrain/` | [[character-terrain]] (stub — code on feature branch) |
| GOAP controller | `Character/CharacterGoapController.cs` | (child of [[ai]] — [[ai-goap]]) |
| Interaction detector | `Character/CharacterInteractionDetector.cs` | (child of [[social]] — [[character-interaction]]) |
| Pathing memory | `Character/CharacterPathingMemory.cs` | (child of [[ai]] — [[ai-pathing]]) |

Cross-cutting scripts sitting on the root:
- `CharacterAnimator.cs`, `CharacterAwareness.cs`, `CharacterBlink.cs`, `CharacterLocations.cs`.

## Public API / entry points

Availability:
- `character.IsFree(out CharacterBusyReason reason)` — ultimate gate before any interaction / order / dialogue.

Lifecycle:
- `character.SetUnconscious(bool)`
- `character.Die()`
- `character.WakeUp()`
- Events: `OnIncapacitated`, `OnDeath`, `OnWakeUp`, `OnCombatStateChanged`.

Control switch:
- `character.SwitchToPlayer()` — swaps controllers, binds `UI_PlayerHUD`.
- `character.SwitchToNPC()` — reverts to AI driver, re-enables NavMeshAgent.

Subsystem access (facade):
- Typed getters only — `character.CharacterCombat`, `character.CharacterMovement`, `character.Stats`, `character.PathingMemory`, etc. Never `GetComponent<T>()` on the character root from gameplay code.

Actions:
- `character.CharacterActions.StartAction(action)` — shared pipeline; players and NPCs queue through it. See [[kevin]] architectural preference ("shared gameplay action layer").

## Data flow

```
Input (player) or AI tick (NPC)
        │
        ▼
  Controller (Player or NPC)
        │
        ▼
  CharacterActions.StartAction(action)
        │                 │
        │                 ├──► Fires OnActionStarted ─► subsystems stop movement, set `isDoingAction`
        │                 │
        │                 ▼
        │          action.Execute()
        │                 │
        │                 ▼
        │          action.OnApplyEffect() — hits subsystems or ServerRpcs
        │                 │
        │                 ▼
        │          OnActionFinished ─► cooldown, resume navigation
        │
        ▼
  Character facade delegates to the relevant subsystem
        │
        ▼
  Subsystem mutates its own state; fires its own events
```

**Cross-system call rule:** subsystem A calls subsystem B only via an **inspector-linked `[SerializeField]` reference** (project rule #17 in character_core SKILL). Dynamic facade lookups at runtime are forbidden (too brittle under hot reload and prefab edits).

## Dependencies

### Upstream
- [[network]] — Character is a `NetworkBehaviour` hierarchy; server-authoritative state with owner prediction.
- [[save-load]] — every subsystem implements `ICharacterSaveData<T>` where applicable.

### Downstream
- [[combat]], [[ai]], [[party]], [[social]], [[items]], [[jobs-and-logistics]], [[dialogue]], [[player-ui]] — all consume the Character facade.
- [[world]] — `CharacterMapTracker` and hibernation serialization round-trip through Character.

## State & persistence

- Runtime: all subsystems hold their own state; Character only holds `_isDead`, `_isUnconscious`, current controller.
- Persisted via [[save-load]]:
  - Each subsystem exposes typed save data via `ICharacterSaveData<T>`.
  - `CharacterDataCoordinator` walks them in priority order.
  - `CharacterProfileSaveData` is the portable profile (local .json), loadable into Solo or Multiplayer sessions.
- Hibernation: see [[world]] — `HibernatedNPCData` snapshot of the logical character when the map goes cold.

## Known gotchas / edge cases

- **Inspector-linking rule** — subsystems that `[SerializeField]` a sibling must be re-linked if any of them is replaced; otherwise null refs at runtime. Missing link = silent breakage.
- **Facade lookup at runtime forbidden** — use the `[SerializeField]` pattern, not `character.GetComponent<X>()`.
- **`IsFree` is the gate** — never call `character.StartInteraction` / `StartCombat` without checking it. Breaks dialogue, combat engagement, and GOAP scheduling.
- **Player/NPC switch timing** — `SwitchToPlayer()` rebinds `UI_PlayerHUD`. If called before the HUD exists, equipment notifications fail silently.
- **`ShouldPlayGenericActionAnimation`** — every combat action must override to `false` or the generic busy animation clobbers the specific trigger.
- **`CharacterInteractable` access** — use `character.CharacterInteractable` facade, never `GetComponent<CharacterInteractable>()` (it lives on a child GameObject).

## Open questions / TODO

- [ ] [[character-archetype]] and [[character-terrain]] folders are empty on `multiplayyer`; both pages are stubs tracked in [[TODO-post-merge]].
- [ ] No SKILL.md for `character-progression`, `character-profile`, `character-speech`, `character-body-parts`, `character-animation-sync`, `character-book-knowledge`, `character-community`, `character-blueprints`, `character-locations`. Tracked in [[TODO-skills]].
- [ ] `CharacterAnimator.cs` / `CharacterAwareness.cs` / `CharacterBlink.cs` sit on the root — should they migrate to child GameObjects for consistency with the subsystem pattern?

## Change log
- 2026-04-18 — Initial documentation pass (wiki bootstrap). — Claude / [[kevin]]
- 2026-04-24 — Added `RequestHarvestServerRpc` + `ApplyHarvestOnServer` helper on `CharacterActions`; documented the `IsSpawned && !IsServer` client-routing pattern for server-authoritative `OnApplyEffect`. Fixes client-triggered `WorldItem.SpawnWorldItem` error from `CharacterHarvestAction`. — Claude

## Sources
- [.agent/skills/character_core/SKILL.md](../../.agent/skills/character_core/SKILL.md)
- [.claude/agents/character-system-specialist.md](../../.claude/agents/character-system-specialist.md)
- [Character.cs](../../Assets/Scripts/Character/Character.cs)
- [CharacterActions.cs](../../Assets/Scripts/Character/CharacterActions/CharacterActions.cs)
- [PlayerController.cs](../../Assets/Scripts/Character/CharacterControllers/PlayerController.cs)
- [NPCController.cs](../../Assets/Scripts/Character/CharacterControllers/NPCController.cs)
- Root [CLAUDE.md](../../CLAUDE.md) — Character GameObject Hierarchy section.
- 2026-04-18 conversation with [[kevin]].
