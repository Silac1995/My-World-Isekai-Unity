---
name: cinematic-system
description: Server-side runtime for scripted cinematic scenes — polymorphic step model, role binding, director coroutine, IsCinematicActor flag, public TryPlay facade. Phase 1 foundation.
---

# Cinematic System (Phase 1)

Server-side runtime for Fire Emblem / Persona / Vandal Hearts style scripted scenes. A cinematic is an ordered list of typed steps (Speak / Move / Wait / Trigger in Phase 1) authored as a `CinematicSceneSO` ScriptableObject, executed by a `CinematicDirector` coroutine. Bound actors are flagged `IsCinematicActor` and are invincible + input-locked while a scene runs.

**Phase 1 status:** server-side / single-player foundation. **Not** yet networked, persisted, or editor-tooled — those land in Phase 2/3/4.

## When to use this skill

- Authoring a `CinematicSceneSO` asset in `Assets/Resources/Data/Cinematics/`.
- Triggering a scene from code via `Cinematics.TryPlay(...)`.
- Adding a new step type (`CinematicStep` subclass).
- Adding a new role selector (`RoleSelectorSO` subclass).
- Adding a new in-timeline effect (`CinematicEffectSO` subclass).
- Debugging a stuck or misbehaving scene.

## Core types

| Type | Path | Role |
|------|------|------|
| `CinematicSceneSO` | `Assets/Scripts/Cinematics/Core/CinematicSceneSO.cs` | Top-level scene asset. Holds identity (`SceneId` GUID), trigger metadata, role list, `[SerializeReference] List<CinematicStep>` timeline. |
| `ICinematicStep` / `CinematicStep` | `Assets/Scripts/Cinematics/Core/ICinematicStep.cs` | Step contract: `OnEnter / OnTick / OnExit / IsComplete`. New step types subclass `CinematicStep`. |
| `CinematicContext` | `Assets/Scripts/Cinematics/Core/CinematicContext.cs` | Runtime context threaded through every step callback. `BoundRoles`, `TriggeringPlayer`, `OtherParticipant`, `GetActor(roleId)`. |
| `CinematicDirector` | `Assets/Scripts/Cinematics/Core/CinematicDirector.cs` | Per-scene `MonoBehaviour` (Phase 2 promotes to `NetworkBehaviour`). `Initialize(scene, ctx)` + `RunScene()` start the step-loop coroutine. |
| `Cinematics` | `Assets/Scripts/Cinematics/Core/Cinematics.cs` | Public static facade. `Cinematics.TryPlay(scene, triggeringPlayer, otherParticipant?) : bool`. |
| `RoleSlot` + `RoleSelectorSO` | `Assets/Scripts/Cinematics/Roles/` | Role binding. `Selector_TriggeringPlayer` is the only Phase 1 selector; Phase 2 adds `Selector_OtherParticipant` and others. |
| `CharacterCinematicState` | `Assets/Scripts/Character/CharacterCinematicState/CharacterCinematicState.cs` | Per-Character subsystem. `IsCinematicActor` flag (Phase 1 local bool; Phase 2 NetworkVariable), `_playedSceneIds` + `_pendingSceneIds` history. |
| `CharacterAction_CinematicMoveTo` | `Assets/Scripts/Cinematics/Actions/CharacterAction_CinematicMoveTo.cs` | `CharacterAction` subclass for `MoveActorStep`. Routes through `CharacterActions.ExecuteAction` per rule #22. |

## Phase 1 step catalogue

| Step | What it does | Key fields |
|------|--------------|------------|
| `WaitStep` | Sim-time delay. | `_durationSec` |
| `SpeakStep` | Speaker role says a line. Phase 1 auto-advances 1.5s after typing finishes. Length-aware safety timeout. Supports `[role:X].getName` placeholders. | `_speakerRoleId`, `_lineText`, `_typingSpeedOverride` |
| `MoveActorStep` | Actor walks to a target (role / world position). Routes through `CharacterAction_CinematicMoveTo`. Blocking by default. | `_actorRoleId`, `_targetMode`, `_targetRoleId` / `_targetPos`, `_stoppingDist`, `_blocking`, `_timeoutSec` |
| `TriggerStep` | Fires a `CinematicEffectSO` and/or a `UnityEvent`. Fire-and-forget. | `_effect`, `_eventHook` |

## How to trigger a cinematic

```csharp
using MWI.Cinematics;

// Server-side only. Returns false if scene/player null, required role unbindable,
// or called from a client in a networked session.
var scene = Resources.Load<CinematicSceneSO>("Data/Cinematics/Test_FirstMeeting");
bool started = Cinematics.TryPlay(scene, playerCharacter);
```

`TryPlay` resolves all roles (hard-fails on required + unbound, silently skips optional + unbound), marks every bound actor as `IsCinematicActor=true`, spawns a `CinematicDirector` GameObject under `CinematicDirectors/`, and starts the step loop.

## How to author a scene asset (Phase 1, no editor window yet)

1. Project window → right-click in `Assets/Resources/Data/Cinematics/` → `Create → MWI → Cinematics → Scene`.
2. Inspector:
   - **Identity**: leave `_sceneId` GUID, set `_displayName`.
   - **Cast**: add `RoleSlot` entries — pick `_roleId`, drop a `RoleSelectorSO` asset (e.g. `Selector_TriggeringPlayer.asset`) into `_selector`.
   - **Timeline**: `_steps` uses `[SerializeReference]` — click "+" then pick a step type from the dropdown. Configure inline.

## How to add a new step type

1. Create `Assets/Scripts/Cinematics/Steps/MyStep.cs`.
2. Inherit from `CinematicStep`. Mark `[System.Serializable]`.
3. Override only what you need: `OnEnter / OnTick / OnExit / IsComplete`.
4. Use `[SerializeField]` for designer-facing fields. Use `ActorRoleId` (via `new ActorRoleId(_roleStringId)`) for actor references.
5. Add `Debug.Log` at OnEnter and any branching point per rule #27.
6. Try/catch fallible operations per rule #31. The director already wraps step callbacks, but defensive guards inside the step prevent cascade failure.
7. The step automatically appears in the `[SerializeReference]` dropdown on `CinematicSceneSO._steps`.

Example skeleton:

```csharp
[System.Serializable]
public class MyStep : CinematicStep
{
    [SerializeField] private string _someParam;

    public override void OnEnter(CinematicContext ctx) { /* … */ }
    public override bool IsComplete(CinematicContext ctx) => /* … */;
}
```

## How to add a new role selector

1. Create `Assets/Scripts/Cinematics/Roles/Selector_MyRule.cs`.
2. Inherit from `RoleSelectorSO`, add `[CreateAssetMenu(menuName = "MWI/Cinematics/Selectors/My Rule")]`.
3. Override `Resolve(CinematicContext ctx) → Character` (return null if unbindable).
4. Author an asset of the new SO and drop it into `RoleSlot._selector`.

## How to add a new effect

1. Create `Assets/Scripts/Cinematics/Effects/Effect_MyThing.cs`.
2. Inherit from `CinematicEffectSO`, add `[CreateAssetMenu(menuName = "MWI/Cinematics/Effects/My Thing")]`.
3. Override `Apply(CinematicContext ctx)`. Use `ctx.GetActor(roleId)` to access bound characters.
4. Designers reference the effect asset from a `TriggerStep._effect` field.

## Integration touchpoints (existing systems)

- **`Character.CharacterCinematicState`** — added in Phase 1, all systems read this for the `IsCinematicActor` flag.
- **`CharacterCombat.TakeDamage`** — skips damage when `target.CharacterCinematicState.IsCinematicActor` (Phase 1 server-side; Phase 2 promotes the flag to `NetworkVariable<bool>` so all clients respect it).
- **`PlayerController.Update`** — early-returns when `self.IsCinematicActor`, blocking movement / combat / sleep / hotkey input. UI input lives in other components and is unaffected.
- **`CharacterActions.ExecuteAction`** — the standard action lane. `MoveActorStep` enqueues through this per rule #22; future Phase 3 `ExecuteActionStep` will use the same lane.
- **Existing `DialogueManager` / `DialogueSO`** — untouched in Phase 1. Phase 4 migration wraps `DialogueManager.StartDialogue` to delegate through the cinematic system, but legacy assets keep working.

## Common gotchas

- **Cinematic stalls forever.** `SpeakStep` has a length-aware safety timeout (`PHASE1_TYPING_TIMEOUT_BASE_SEC + char_count * PHASE1_TYPING_TIMEOUT_PER_CHAR`). If you see the timeout warning, the speaker's `CharacterSpeech._speechBubbleStack` is probably unwired on the prefab.
- **`ExecuteAction` returns false.** The actor is already running another action. `MoveActorStep` logs a warning and instant-completes (skips the move) rather than hanging the cinematic. Sequence steps so actors are free at the moment a `MoveActorStep` runs.
- **`IsCinematicActor` not visible to clients.** Phase 1 limitation — the flag is a server-side bool. Phase 2 promotes to `NetworkVariable<bool>`. Until then, MP scenes work because all combat/input authority is on the server, but client-side visuals (animator, UI) cannot react to the flag.
- **Required role unbindable → scene aborts.** Set `RoleSlot._isOptional = true` for "skip-if-missing" roles. The Phase 1 plan only ships `Selector_TriggeringPlayer`; multi-actor scenes need `Selector_OtherParticipant` (Phase 2).
- **Rule #22 violation risk.** Never call `CharacterMovement.SetDestination` directly from a step. Always go through a `CharacterAction` subclass enqueued via `CharacterActions.ExecuteAction`.

## Phase 1 deferred to later phases

| Feature | Phase |
|---------|-------|
| `NetworkVariable<bool>` for `IsCinematicActor`, `ServerRpc` / `ClientRpc` for advance protocol | 2 |
| `CinematicWorldState` `ISaveable` + `ICharacterSaveData<CinematicHistorySaveData>` | 2 |
| `Surface_OnInteractionAction` (Talk), `Surface_OnSpatialZone`, `Surface_Scripted`, `Surface_Debug` | 2 |
| `CinematicRegistry` server-side service (eligibility queries, per-character runtime assignment) | 2 |
| 4 PlayModes (`OncePerWorld` / `OncePerPlayer` / `OncePerNpc` / `Repeatable`) | 2 |
| `Selector_OtherParticipant`, `Selector_NearestArchetype`, `Selector_RandomInRadius`, `Selector_SpecificCharacter` | 2 |
| AI BT yield gate on `IsCinematicActor` | 2 |
| `ChoiceStep`, `ParallelStep`, `CameraFocusStep`, `ExecuteActionStep` | 3 |
| 5 action recipes, 6 effects (incl. `Effect_GiveQuest` / `Effect_RemoveQuest`), 6 eligibility rules | 3 |
| Custom Unity Editor — Cinematic Scene Editor + Browser + Validator | 4 |
| `DialogueManager` migration | 4 |
| SKILL.md / wiki updates with phase-2-current details | rolling |

## See also

- Spec: `docs/superpowers/specs/2026-04-30-cinematic-system-design.md`
- Plan: `docs/superpowers/plans/2026-04-30-cinematic-system-phase-1-foundation.md`
- Architecture: `wiki/systems/cinematic-system.md`
- Legacy primitive: `.agent/skills/dialogue-system/SKILL.md` (Phase 4 wraps `DialogueManager` through this system)
