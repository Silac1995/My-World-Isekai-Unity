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
| `RoleSlot` + `RoleSelectorSO` | `Assets/Scripts/Cinematics/Roles/` | Role binding. Phase 1 ships three selectors: `Selector_TriggeringPlayer`, `Selector_OtherParticipant`, `Selector_CharacterByName`. Phase 2 adds archetype + radius-based selectors. |
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

### From code (any server-side hook — quest reward, BT action, scripted event)

```csharp
using MWI.Cinematics;

// Server-side only. Returns false if scene/player null, required role unbindable,
// or called from a client in a networked session.
var scene = Resources.Load<CinematicSceneSO>("Data/Cinematics/Test_FirstMeeting");
bool started = Cinematics.TryPlay(scene, playerCharacter, otherParticipant: npcCharacter);
```

`TryPlay` resolves all roles (hard-fails on required + unbound, silently skips optional + unbound), marks every bound actor as `IsCinematicActor=true`, spawns a `CinematicDirector` GameObject under `CinematicDirectors/`, and starts the step loop. The `otherParticipant` argument feeds `CinematicContext.OtherParticipant` and resolves any role using `Selector_OtherParticipant` (typically the NPC the player is talking to).

### From the Inspector while in Play mode (Phase 1 quick test)

Right-click the `CinematicSceneSO` asset header in the Inspector → **`Play in Active Scene`**. The `[ContextMenu]` finds a player Character in the active scene to use as `TriggeringPlayer`, falls back to the first Character if no player is around, and calls `TryPlay`. No external scripts (DevModeManager modules, DebugScript, etc.) needed.

> **Don't use `DebugScript`** for cinematic testing — it's marked `[Obsolete]`. The new debug surface is `DevModeManager` (F3 toggle, module-based panel). Phase 4 will add a proper `DevCinematicModule` tab; until then the in-Inspector ContextMenu is the supported quick-test path.

## How character ↔ role binding works (vs. the legacy `DialogueManager` model)

Legacy `DialogueManager._testParticipants` is a flat `List<Character>`. Lines reference participants by **1-based index**: `_characterIndex = 1` means "participant[0] speaks". Designer drags Characters into the list to assign.

The cinematic system replaces this with **named roles + polymorphic selectors**:

| Legacy `DialogueManager` | New cinematic system |
|--------------------------|----------------------|
| `_testParticipants[0]` (1-indexed) | `_roles[0].RoleId = "Hero"` |
| `_testParticipants[1]` | `_roles[1].RoleId = "Wilfred"` |
| Drag `Character` into list at design time | Pick a `RoleSelectorSO` asset that resolves the Character at runtime |
| `DialogueLine._characterIndex = 1` | `SpeakStep._speakerRoleId = "Hero"` |
| `[index1].getName` placeholder | `[role:Hero].getName` placeholder |

Why named roles over indices:
- A scene authored once works for any pair of (player, NPC) without editing the asset per-instance.
- Self-documenting: `_speakerRoleId = "Wilfred"` reads better than `_characterIndex = 2`.
- Runtime binding lets the same scene fire from multiple trigger contexts (any player, any matching NPC).

Phase 1 ships three selectors:

| Selector | Resolves to | Authoring inputs | Use case |
|----------|-------------|------------------|----------|
| `Selector_TriggeringPlayer` | `ctx.TriggeringPlayer` (the player who fired the cinematic) | none | The "Hero" / player avatar role. |
| `Selector_OtherParticipant` | `ctx.OtherParticipant` (passed as 2nd arg to `TryPlay`) | none | The NPC the player is interacting with — typically the Talk target. Caller supplies via `Cinematics.TryPlay(scene, player, npc)`. |
| `Selector_CharacterByName` | First `Character` in the scene whose `CharacterName == _characterName` | `_characterName : string` | Named NPC ("Wilfred", "Tavern Keeper") — the closest analogue to dragging a specific character into `_testParticipants`. |

Phase 2 adds archetype-based selectors (`Selector_NearestArchetype`, `Selector_RandomInRadius`, `Selector_SpecificCharacter` with full archetype reference) for procedural / generic-NPC scenes.

## How to author a scene asset (Phase 1, no full editor window yet)

1. Project window → right-click in `Assets/Resources/Data/Cinematics/` → `Create → MWI → Cinematics → Scene`.
2. Inspector:
   - **Identity**: leave `_sceneId` GUID untouched, set `_displayName` for editor labelling.
   - **Triggering / Lifecycle headers** (Phase 2): leave defaults (`AnyPlayer` / `OncePerWorld` / `AllMustPress` / 5s grace / priority 50). The runtime ignores these in Phase 1.
   - **Cast**: click `+` on Roles. For each entry:
     - `Role Id` — short identifier referenced by steps (e.g. `Hero`, `Wilfred`, `Witness`).
     - `Display Name` — editor / debug label. **Falls back to `Role Id` if empty** (NOT to the character's name; the character is resolved at runtime).
     - `Selector` — drop a `RoleSelectorSO` asset (Phase 1 ships `Selector_TriggeringPlayer.asset` only; create one via `Create → MWI → Cinematics → Selectors → Triggering Player`).
     - `Is Optional` — required (default) hard-fails the cinematic if unbound; optional silently skips.
     - `Is Primary Actor` — Phase 2 `OncePerNpc` keying. Set to true on the role that "owns" the scene (typically the talked-to NPC).
   - **Timeline**: click `+` on Steps → use the **type-picker dropdown on the right side of each new element** to select Speak / Wait / Move / Trigger. The custom property drawer (`Assets/Scripts/Cinematics/Editor/CinematicStepDrawer.cs`) makes the picker visible inline so you don't need to right-click into Unity's hidden managed-reference menu.
     - `SpeakStep`: speakerRoleId, lineText (supports `[role:X].getName` placeholders), typingSpeedOverride (0 = default).
     - `WaitStep`: durationSec.
     - `MoveActorStep`: actorRoleId, targetMode (Role / WorldPos), target ref, stoppingDist (default 1.5 Unity units ≈ 0.23m), blocking, timeoutSec.
     - `TriggerStep`: effect (drag a `CinematicEffectSO` asset, e.g. `Effect_RaiseEvent`), eventHook (UnityEvent in the inspector).

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

- **`Time.time` / `Time.deltaTime` won't compile inside `namespace MWI.Cinematics`.** The C# resolver walks the enclosing namespace tree (`MWI.Cinematics → MWI → global`) before applying `using` directives, and the sibling `MWI.Time` namespace shadows `UnityEngine.Time`. Workaround: every cinematic file that touches Unity's clock has `using UTime = UnityEngine.Time;` at the top and uses `UTime.time` / `UTime.deltaTime`. Match this convention in any new file. (Or fully qualify `UnityEngine.Time.time`.)
- **Empty timeline elements when adding steps.** Unity's default `[SerializeReference]` UX hides the type-picker dropdown. The custom drawer at `Assets/Scripts/Cinematics/Editor/CinematicStepDrawer.cs` adds a visible dropdown next to each entry's foldout. If you see only "Element 0 (no step type set)" with nothing else, check that `CinematicStepDrawer.cs` exists and Unity has compiled the Editor folder.
- **Cinematic stalls forever.** `SpeakStep` has a length-aware safety timeout (`PHASE1_TYPING_TIMEOUT_BASE_SEC + char_count * PHASE1_TYPING_TIMEOUT_PER_CHAR`). If you see the timeout warning, the speaker's `CharacterSpeech._speechBubbleStack` is probably unwired on the prefab.
- **`ExecuteAction` returns false.** The actor is already running another action. `MoveActorStep` logs a warning and instant-completes (skips the move) rather than hanging the cinematic. Sequence steps so actors are free at the moment a `MoveActorStep` runs.
- **`IsCinematicActor` not visible to clients.** Phase 1 limitation — the flag is a server-side bool. Phase 2 promotes to `NetworkVariable<bool>`. Until then, MP scenes work because all combat/input authority is on the server, but client-side visuals (animator, UI) cannot react to the flag.
- **Required role unbindable → scene aborts.** Set `RoleSlot._isOptional = true` for "skip-if-missing" roles. The Phase 1 plan only ships `Selector_TriggeringPlayer`; multi-actor scenes need `Selector_OtherParticipant` (Phase 2).
- **`Display Name` vs character name.** `RoleSlot._displayName` is the editor / debug label for the *role*. If empty, it falls back to `Role Id` — NOT the resolved Character's name. The character's real name only appears in placeholder substitution (`[role:Hero].getName` → `Character.CharacterName`).
- **Rule #22 violation risk.** Never call `CharacterMovement.SetDestination` directly from a step. Always go through a `CharacterAction` subclass enqueued via `CharacterActions.ExecuteAction`.
- **Non-blocking `MoveActorStep` orphans the action on step exit.** Intentional (background-walk semantics). Phase 2 will register orphans on `CinematicContext` for scene-level abort. For Phase 1, accept that an aborted scene with a non-blocking move in flight will let the actor walk to arrival or `_timeoutSec` (default 30s).

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
