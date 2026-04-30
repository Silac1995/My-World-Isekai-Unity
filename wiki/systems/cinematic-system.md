---
type: system
title: "Cinematic System"
tags: [cinematic, scripted-scene, dialogue, server-authority, phase-1, wip]
created: 2026-04-30
updated: 2026-04-30
sources: []
related:
  - "[[character]]"
  - "[[dialogue]]"
  - "[[scripted-speech]]"
  - "[[social]]"
  - "[[network]]"
  - "[[kevin]]"
status: wip
confidence: high
primary_agent: null
secondary_agents:
  - character-social-architect
  - character-system-specialist
owner_code_path: "Assets/Scripts/Cinematics/"
depends_on:
  - "[[character]]"
  - "[[scripted-speech]]"
  - "[[network]]"
depended_on_by: []
---

# Cinematic System

## Summary

Server-side runtime for Fire Emblem / Persona / Vandal Hearts style scripted cinematic scenes. A cinematic is an ordered list of typed steps (Speak / Move / Wait / Trigger in Phase 1) authored as a `CinematicSceneSO` ScriptableObject and executed by a `CinematicDirector` coroutine. Bound actors carry an `IsCinematicActor` flag that gates combat damage and player input for the duration of the scene. The system supersedes (without replacing yet) the legacy `DialogueManager` / `DialogueSO` primitive, which becomes inline content of one `SpeakStep` after Phase 4 migration.

**Phase 1 status:** server-side / single-player foundation shipped. No networking, persistence, or editor tools yet — those are Phase 2 / 3 / 4.

## Purpose

Author story moments, cutscenes, quest handoffs, tutorial sequences, and fully-NPC scripted exchanges as data (ScriptableObjects + polymorphic step subclasses) rather than code. Provide a server-authoritative runtime so co-op sessions don't race ahead of each other (current `DialogueManager` race is an open question in [[dialogue]]). Centralise actor invincibility / input-lock so combat, AI, and player controllers all read one flag.

## Responsibilities

- Storing scripted scene data (`CinematicSceneSO`, `CinematicStep` polymorphic hierarchy, `RoleSlot`, `RoleSelectorSO`).
- Resolving abstract roles to live `Character` references at scene start (`Cinematics.TryPlay` → `slot.Selector.Resolve`).
- Iterating steps end-to-end via the `OnEnter / OnTick / IsComplete / OnExit` contract.
- Marking and clearing the per-character `IsCinematicActor` flag.
- Routing scene-driven actor movement through `CharacterAction` (rule #22 — player↔NPC parity).
- Logging at branching points per rule #27; defensive try/catch around step callbacks per rule #31.

**Non-responsibilities** (Phase 1 — see Phase 2/3/4 for what's added later):

- Does **not** network the runtime — Phase 1 director is a plain `MonoBehaviour`. Phase 2 promotes to `NetworkBehaviour`.
- Does **not** persist scene history — Phase 2 adds `CinematicWorldState : ISaveable` + `ICharacterSaveData<CinematicHistorySaveData>`.
- Does **not** gate by eligibility / `PlayMode` — Phase 2 adds `CinematicRegistry`.
- Does **not** replace the legacy `DialogueManager` — Phase 4 migration phase.

## Key classes / files

| File | Role |
|------|------|
| [CinematicSceneSO.cs](../../Assets/Scripts/Cinematics/Core/CinematicSceneSO.cs) | Top-level scene asset. `_sceneId` GUID, `_roles`, `[SerializeReference] _steps`. |
| [ICinematicStep.cs](../../Assets/Scripts/Cinematics/Core/ICinematicStep.cs) | Interface + `CinematicStep` `[Serializable]` abstract base. |
| [CinematicContext.cs](../../Assets/Scripts/Cinematics/Core/CinematicContext.cs) | Runtime context. `BoundRoles`, `TriggeringPlayer`, `OtherParticipant`, `GetActor`. |
| [CinematicDirector.cs](../../Assets/Scripts/Cinematics/Core/CinematicDirector.cs) | Per-scene coroutine runner. Phase 1 plain MonoBehaviour. |
| [Cinematics.cs](../../Assets/Scripts/Cinematics/Core/Cinematics.cs) | Public static facade. `TryPlay(scene, player, otherParticipant?)`. |
| [Steps/](../../Assets/Scripts/Cinematics/Steps/) | `WaitStep`, `SpeakStep`, `DialogueStep`, `MoveActorStep`, `TriggerStep` (Phase 1 set). `DialogueStep` holds a `List<CinematicDialogueLine>` inline — same authoring shape as legacy `DialogueSO._lines`, but each line names its speaker by Role Id (string matching a Cast role) instead of by 1-based participant index. No external asset; lines belong to the step. |
| [Roles/](../../Assets/Scripts/Cinematics/Roles/) | `RoleSlot`, `RoleSelectorSO`, plus 5 Phase 1 selectors: `Selector_TriggeringPlayer`, `Selector_OtherParticipant`, `Selector_CharacterByName`, `Selector_CharacterById`, `Selector_PartyMember`. |
| [Effects/](../../Assets/Scripts/Cinematics/Effects/) | `CinematicEffectSO`, `Effect_RaiseEvent` (Phase 1 set). |
| [Actions/CharacterAction_CinematicMoveTo.cs](../../Assets/Scripts/Cinematics/Actions/CharacterAction_CinematicMoveTo.cs) | `CharacterAction` subclass for `MoveActorStep`. |
| [Editor/CinematicStepDrawer.cs](../../Assets/Scripts/Cinematics/Editor/CinematicStepDrawer.cs) | Editor-only `CustomPropertyDrawer` for `[SerializeReference] CinematicStep` lists. Renders an inline type-picker dropdown so designers can pick step types when authoring scenes. Phase 4 replaces with full Cinematic Scene Editor. |
| [CharacterCinematicState.cs](../../Assets/Scripts/Character/CharacterCinematicState/CharacterCinematicState.cs) | Per-Character subsystem. `IsCinematicActor`, `_playedSceneIds`, `_pendingSceneIds`. |

## Public API / entry points

- `Cinematics.TryPlay(CinematicSceneSO scene, Character triggeringPlayer, Character otherParticipant = null) : bool` — server-side only; returns false on missing scene/player, unbindable required role, or client-call in a networked session.
- `Character.CharacterCinematicState` — facade property; read by combat, AI, input gates.
- `CharacterCinematicState.IsCinematicActor` (bool getter) — true while bound in an active scene.
- `CinematicDirector.Abort(CinematicEndReason reason)` — externally cancellable (defensive use; not normally called in Phase 1).

## Data flow

```
Server-side trigger (Cinematics.TryPlay)
       │
       ├── Validate non-null scene + player; IsServer guard
       │
       ├── For each RoleSlot in scene._roles:
       │     ├── slot.Selector.Resolve(ctx) → Character
       │     ├── null + required → ABORT (return false, nothing flagged)
       │     └── null + optional → skip silently
       │
       ├── For each bound role:
       │     └── actor.CharacterCinematicState.MarkActiveActor(sceneId, roleId)
       │
       ├── Spawn CinematicDirector under "CinematicDirectors/" container
       └── director.Initialize(scene, ctx); director.RunScene()

Director.RunScene (coroutine)
       │
       └── For each step in scene.Steps:
             ├── try { step.OnEnter(ctx); }
             ├── while !IsComplete:
             │     ├── try { step.OnTick(ctx, dt); isComplete = step.IsComplete(ctx); }
             │     └── yield return null   ← OUTSIDE try/catch (C# constraint)
             └── try { step.OnExit(ctx); }

Director.EndScene (Completed | Aborted)
       │
       └── For each bound actor:
             ├── actor.CharacterCinematicState.ClearActiveActor()
             └── if Completed: MarkSceneCompleted(sceneId) + RemovePendingScene(sceneId)
       Director GameObject destroyed
```

Movement step path (rule #22 compliance):

```
MoveActorStep.OnEnter
       └── new CharacterAction_CinematicMoveTo(actor, target, stoppingDist, timeoutSec)
       └── actor.CharacterActions.ExecuteAction(action)   ← standard action lane
              │
              ├── action.OnStart() → CharacterMovement.SetDestination(target)
              ├── action.WatchArrival coroutine: distance check per frame; FinishOnce on arrival
              └── ActionTimerRoutine: Duration=timeoutSec safety net; OnApplyEffect+Finish on timeout
       └── _action.OnActionFinished += handler  →  step._actionFinished = true  →  IsComplete
```

## Dependencies

### Upstream
- [[character]] — `CharacterCinematicState` is a `CharacterSystem` subsystem on the Character GameObject hierarchy. The facade (`Character.CharacterCinematicState` property) wires through the existing capability registry.
- [[scripted-speech]] — `SpeakStep` calls `Character.CharacterSpeech.SayScripted(text, speed, onTypingFinished)`. No new bubble code.
- [[network]] — Phase 1 server-only via `NetworkManager.IsServer` guard. Phase 2 layers in NetworkVariable + RPCs per the spec.

### Downstream
- [[player-ui]] — Phase 4 will route advance-press input here from `PlayerController` per rule #33.
- [[combat_system]] — `CharacterCombat.TakeDamage` skips damage when target `IsCinematicActor`. Single-line gate at the top of the method.
- [[social]] — `CharacterInteraction.OnTalk` integration is Phase 2 (Talk-driven scenes via `Surface_OnInteractionAction`).

## State & persistence

**Phase 1**: no persistence. `_playedSceneIds` + `_pendingSceneIds` are in-memory `HashSet<string>` per Character.

**Phase 2** (planned):
- `CinematicWorldState : ISaveable` → `Worlds/{worldGuid}.json`. Holds `_playedOncePerWorld`. New world = fresh history (rule #20 compliant).
- `CharacterCinematicState : ICharacterSaveData<CinematicHistorySaveData>` → character profile (player) / `HibernatedNPCData.ProfileData` (NPC). Travels across worlds for `OncePerPlayer` mode.

## Known gotchas / edge cases

- **`Time` namespace clash inside `MWI.Cinematics`.** The C# resolver searches the enclosing namespace tree before applied `using` directives, and the sibling `MWI.Time` namespace shadows `UnityEngine.Time`. Cinematic files alias via `using UTime = UnityEngine.Time;` and reference `UTime.time` / `UTime.deltaTime`. Match this convention in any new file, or fully-qualify `UnityEngine.Time`. (Affected files: `WaitStep`, `SpeakStep`, `CharacterAction_CinematicMoveTo`, `CinematicDirector`.)
- **Empty timeline elements.** Adding entries with `+` produces null `[SerializeReference]` slots in Unity's default Inspector. The custom drawer at `Assets/Scripts/Cinematics/Editor/CinematicStepDrawer.cs` adds a visible type-picker dropdown next to each entry. If the dropdown is missing, verify the Editor folder is compiled (Unity should pick up the `Editor` folder convention automatically).
- **`Display Name` on a RoleSlot is the *role's* label, not the character's name.** Falls back to `Role Id` if empty. The character's actual name is read from `Character.CharacterName` at runtime via placeholder substitution.
- **`IsCinematicActor` not networked in Phase 1.** Server-only bool. Combat/input gates work because authority lives server-side. Client-side visuals (animator state, HUD) cannot react. Phase 2 fix.
- **`Cinematics.TryPlay` from a client** is blocked via `NetworkManager.IsServer` guard. Solo / non-networked builds (`!IsListening`) bypass the check.
- **Step instances are SHARED across simultaneous plays of the same `CinematicSceneSO`.** Phase 1 has no guard. Phase 2's registry adds PlayMode check; for Phase 1 don't double-trigger the same scene.
- **`MoveActorStep` non-blocking moves orphan the action on step exit** (intentional — non-blocking means "let it run in background"). Phase 2 will register orphans on `CinematicContext` for scene-abort mass-cancel.
- **`SpeakStep` typing-finished safety timeout** (length-aware: `5s + 0.15s/char`) protects against unwired `_speechBubbleStack`. If you see `typing-finished callback timed out` warnings, fix the prefab.
- **Required role unbindable → scene aborts before any actor flagged.** Two-phase commit pattern (resolve all → flag all) prevents leaked `IsCinematicActor=true` if a required role can't bind.
- **`Character.CharacterCinematicState` may be null on legacy NPCs without the subsystem.** All gates null-check defensively; legacy characters fall through to normal damage / input behavior.
- **AI BT yield gate is missing in Phase 1** (deferred to Phase 2). Phase 1 only binds player avatars (via `Selector_TriggeringPlayer`); since the player isn't on the BT, this is fine. NPCs as actors require Phase 2.

## Open questions / TODO

- [ ] Phase 2: `Selector_OtherParticipant`, `Selector_NearestArchetype`, `Selector_RandomInRadius`, `Selector_SpecificCharacter`.
- [ ] Phase 2: `CinematicRegistry` for eligibility / PlayMode bookkeeping.
- [ ] Phase 2: `Surface_OnInteractionAction` (Talk-driven scenes), `Surface_OnSpatialZone`, `Surface_OnSceneStart`.
- [ ] Phase 2: NetworkVariable promotion for `IsCinematicActor` + advance-press protocol via ServerRpc/ClientRpc.
- [ ] Phase 2: AI BT yield gate when subsystem flag is true.
- [ ] Phase 3: `ChoiceStep`, `ParallelStep`, `CameraFocusStep`, `ExecuteActionStep`.
- [ ] Phase 3: Action recipes (`Recipe_Sleep`, `Recipe_DestroyHarvestable`, `Recipe_IssueOrder`, etc.), effects (`Effect_GiveQuest`, `Effect_RemoveQuest`, VFX, SFX, animations), eligibility rules.
- [ ] Phase 4: Custom Cinematic Scene Editor + Browser + Validator. `DialogueManager` migration.

## Change log

- 2026-04-30 — Phase 1 foundation shipped: 4 step types (Wait/Speak/Move/Trigger), 1 selector (TriggeringPlayer), 1 effect (RaiseEvent), `CharacterCinematicState` subsystem, `Cinematics.TryPlay` facade, combat + input gates on `IsCinematicActor`. — Claude / [[kevin]]
- 2026-04-30 — Time namespace clash fix: aliased `UnityEngine.Time` as `UTime` in 4 files to avoid shadowing by sibling `MWI.Time` namespace. — Claude / [[kevin]]
- 2026-04-30 — Added `CinematicStepDrawer` Editor `CustomPropertyDrawer` so designers can pick concrete step types inline when authoring `[SerializeReference]` step lists. Phase 4 will replace with full Cinematic Scene Editor window. — Claude / [[kevin]]
- 2026-04-30 — Added two more Phase 1 selectors (`Selector_OtherParticipant`, `Selector_CharacterByName`) to close the multi-actor authoring gap. The legacy `DialogueManager._testParticipants` 1-indexed pattern is now expressible via named roles + selectors (see SKILL.md mapping table). Added `[ContextMenu("Play in Active Scene")]` on `CinematicSceneSO` so designers trigger test cinematics directly from the Inspector — no DebugScript or DevModeManager hookup required for Phase 1 verification. `DebugScript` marked `[Obsolete]`; new debug features should extend `DevModeManager` modules. — Claude / [[kevin]]
- 2026-04-30 — Added `Selector_PartyMember` so multi-actor cinematics can bind a player's party companions by index. Index 0 = party leader (the player), 1+ = followers. Pairs with `Selector_OtherParticipant` to fill all 5 roles in a "player + 3 companions + NPC" scene without manual scene refs. Worked example added to SKILL.md. — Claude / [[kevin]]
- 2026-04-30 — Added `Selector_CharacterById` so designers can reference predefined / main / story-critical characters by their persistent UUID (the `Profiles/{guid}.json` filename). Rename-resilient + localization-safe + unambiguous when multiple characters share a name. SKILL.md adds a name-vs-UUID comparison table and "where to find a UUID" guidance. — Claude / [[kevin]]
- 2026-04-30 — Relaxed `Cinematics.TryPlay` to allow null `triggeringPlayer` so NPC-to-NPC scenes can fire without a player anchor. Caller passes any two `Character`s (or just an `overrideOrigin` Vector3). Players nearby still see actor NPCs animate via existing `CharacterMovement` + `CharacterSpeech` replication — bystander handling is free. — Claude / [[kevin]]
- 2026-04-30 — Replaced the short-lived `DialogueScriptStep` (which wrapped an external `DialogueSO`) with `DialogueStep`, the canonical multi-line speak step. `DialogueStep` holds a `List<CinematicDialogueLine>` inline; each line names its speaker by Role Id (string matching a Cast role) — same authoring shape as legacy `DialogueSO._lines` but role-id-based, no external asset, no 1-indexed mapping table. Auto-advance + safety timeout match `SpeakStep`. The wrapper-around-DialogueSO approach was the wrong mental model — designers should author cinematic lines inside the cinematic, not via a separate detached asset. — Claude / [[kevin]]

## Sources

- [docs/superpowers/specs/2026-04-30-cinematic-system-design.md](../../docs/superpowers/specs/2026-04-30-cinematic-system-design.md) — full design spec
- [docs/superpowers/plans/2026-04-30-cinematic-system-phase-1-foundation.md](../../docs/superpowers/plans/2026-04-30-cinematic-system-phase-1-foundation.md) — Phase 1 implementation plan
- [.agent/skills/cinematic-system/SKILL.md](../../.agent/skills/cinematic-system/SKILL.md) — procedural source of truth (authoring, troubleshooting, extending)
- [Assets/Scripts/Cinematics/](../../Assets/Scripts/Cinematics/) — implementation
- [Assets/Scripts/Character/CharacterCinematicState/CharacterCinematicState.cs](../../Assets/Scripts/Character/CharacterCinematicState/CharacterCinematicState.cs) — per-character subsystem
- 2026-04-30 conversation with [[kevin]] — design + Phase 1 execution.
