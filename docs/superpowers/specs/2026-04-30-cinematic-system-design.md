# Cinematic / Scripted Scene System — Design

**Status:** Design accepted, awaiting implementation plan
**Date:** 2026-04-30
**Owner:** Kevin (Silac)

---

## 1. Purpose

Introduce a **cinematic primitive** that elevates the existing scripted-dialogue system (one-shot `DialogueSO` + `DialogueManager`) into full Fire Emblem / Persona / Vandal Hearts style scripted scenes. A cinematic is an **ordered, branching, parallel-capable timeline** of typed steps (speak / move / wait / camera focus / trigger / choice / parallel block / character action) running on a server-authoritative director, with composable role binding, eligibility rules, multiple trigger surfaces, MP-coordinated advance, and per-character history tracking.

The design favours **long-term modularity, server authority, and persistence-first-class**:

- Every extension point is a polymorphic `ScriptableObject` (steps, role selectors, action recipes, in-scene effects, eligibility rules, trigger surfaces). New behaviour is a new SO subclass — never a switch statement somewhere central.
- Server-authoritative for full multiplayer correctness across Host↔Client, Client↔Client, Host/Client↔NPC.
- Existing `Character.CharacterAction` lane is the only path to gameplay effects (rule #22 — player ↔ NPC parity).
- Existing `CharacterId` (string GUID, persists across reconnects) is the identity primitive — no custom UUID type.
- Persistence buckets respect the project's **per-world vs per-character** model: world-scoped state in `Worlds/{worldGuid}.json`, character-scoped in `Profiles/{characterGuid}.json`. New world = new game = fresh `OncePerWorld` history automatically.
- World keeps running during a scene (no global pause). Bound actors are flagged `IsCinematicActor` (NetworkVariable) → combat skips damage, AI yields, player input is locked.

## 2. Scope

### In scope (v1)
- `CinematicSceneSO` ScriptableObject as the top-level authored asset.
- `ICinematicStep` polymorphic step model with **8 v1 step types**: `SpeakStep`, `MoveActorStep`, `WaitStep`, `CameraFocusStep`, `TriggerStep`, `ChoiceStep`, `ParallelStep`, `ExecuteActionStep`.
- `CinematicDirector` `NetworkBehaviour` — server-authoritative timeline runner; per-active-scene `NetworkObject`.
- `CinematicRegistry` server-side service (lazy-init static) — eligibility queries, per-character runtime assignment.
- `CharacterCinematicState` `CharacterSystem` subsystem on every `Character` (player and NPC) — networked actor flag, per-character played + pending scene history, `ICharacterSaveData<CinematicHistorySaveData>`.
- `CinematicWorldState` `ISaveable` — world-scoped played state (`OncePerWorld` bucket).
- 5 v1 trigger surfaces (`OnInteractionAction` [Talk only], `OnSpatialZone`, `OnSceneStart`, `Scripted`, `Debug`).
- Polymorphic catalogues for: role selectors (5 v1 types), action recipes (5 v1 types), in-scene effects (7 v1 types incl. `Effect_GiveQuest` / `Effect_RemoveQuest`), eligibility rules (6 v1 types).
- Bidirectional `OnInteractionAction` matching (A→B and B→A both fire if the participant set matches).
- Three participant patterns (`Anyone` / `RequireAtLeastOne` / `RestrictedToSet`) + `_initiatorFilter` (`AnyCharacter` | `PlayerOnly`).
- Four PlayModes (`OncePerWorld` / `OncePerPlayer` / `OncePerNpc` / `Repeatable`).
- Three advance modes (`AllMustPress` default + grace timer / `AnyAdvances` / `TriggerOnly`), per-scene override.
- `CharacterAction_CinematicMoveTo` (new `CharacterAction` subclass).
- `ICinematicCameraController` interface on the camera rig + v1 `CinematicCameraController` implementation handling `CameraFocusStep` only.
- Custom Unity Editor window — **Cinematic Scene Editor** (drag-reorder steps, color-coded, real-time validation).
- Cinematic Browser editor window (list, filter, create, jump-to-asset).
- `/cinematic` chat command + `DevCinematicModule` panel tab + `CharacterInspectorView` "Cinematic" sub-tab.
- Migration: `DialogueManager.StartDialogue(SO, participants)` API preserved as a thin wrapper on the new system; `Trigger Serialized Dialogue` context menu still works.
- Multiplayer correctness across Host↔Client, Client↔Client, Host/Client↔NPC with full advance-press protocol + late-joiner story.
- Documentation: new `cinematic-system` SKILL.md, new `wiki/systems/cinematic.md`, updates to dialogue / dialogue-manager / scripted-speech wiki + dialogue-system SKILL, new `cinematic-specialist` agent (`model: opus`).

### Out of scope (deferred to v2+)
- Full cinematic camera system (letterbox, virtual cams, shake, scrubbing). v1 ships only `CameraFocusStep` behind the `ICinematicCameraController` interface.
- `ConditionalStep` (state-based branching without UI) — v1 expresses via `ChoiceStep` + designer-side scene splitting.
- `LoopStep` / `RepeatStep`.
- Other `CharacterInteractionAction` triggers (Insult, Compliment, …) — scaffolding supports them via `Surface_OnInteractionAction._actionType`, but v1 validates `Talk` only.
- Reconnect-mid-scene rebinding — disconnected actors auto-yield; rejoiners are non-participants in v1.
- Save mid-cinematic (active scenes auto-abort on save/load).
- Automated MP test harness (manual smoke checklist for v1).
- Mid-scene role rebinding hooks.
- HUD "this NPC has a scripted scene available" indicator (registry caching is built; rendering layer deferred).
- `Effect_CompleteQuest` / `Effect_FailQuest` / `Effect_UpdateQuestProgress` (drop in as new SOs later — `Effect_GiveQuest` and `Effect_RemoveQuest` ship in v1).

## 3. Foundational Design Decisions

| # | Decision | Choice |
|---|---|---|
| 1 | Top-level data shape — extend `DialogueSO` or new SO? | **New `CinematicSceneSO`**. `DialogueSO` becomes inline content of one `SpeakStep`. Existing dialogues keep working. |
| 2 | Step model — flat fields vs polymorphic SOs? | **Polymorphic `ICinematicStep`**. Director never branches on step type. New step types = new class, no central change. |
| 3 | Authority model — local per-player vs server-authoritative? | **Server-authoritative `CinematicDirector` `NetworkBehaviour`**. Resolves the existing dialogue race condition flagged in `wiki/systems/dialogue.md`. |
| 4 | Trigger surfaces — single type or polymorphic? | **5 v1 `ICinematicTriggerSurface` types**, each a `ScriptableObject`. Future surfaces drop in. |
| 5 | Talk integration — generic Talk only or all `ICharacterInteractionAction`? | **`Surface_OnInteractionAction` parameterized by action type, v1 validates `Talk` only**. Future-proof for Insult/Compliment/etc. |
| 6 | Bidirectional Talk matching — A→B only, or both directions? | **Bidirectional**. Set semantics on `_participantIds` / `_participantArchetypes`; A→B and B→A both fire if the participant set matches. |
| 7 | Role binding — direct character refs or polymorphic selectors? | **`RoleSelectorSO` polymorphic SO**. Designers compose "TriggeringPlayer / TalkTarget / NearestArchetype / SpecificCharacter / RandomInRadius". |
| 8 | Role-binding fallback — hard-fail or best-effort? | **Hard-fail with `_isOptional` per role**. Required missing → scene doesn't trigger; optional missing → steps silently skip. |
| 9 | PlayMode default | **`OncePerWorld`**. `OncePerPlayer` / `OncePerNpc` / `Repeatable` available per-scene. |
| 10 | Trigger authority for main scenes | **`_triggerAuthority` field**: `AnyPlayer` (default) / `HostOnly`. Designers flip per main-story scene. |
| 11 | Advance input in MP | **`AllMustPress` + grace timer (5s default)**. Disconnect auto-yields. Per-scene override. |
| 12 | Camera scope in v1 | **Defer full camera system**; ship only `CameraFocusStep` behind `ICinematicCameraController` interface. v2 implements full cam. |
| 13 | Movement during scenes | **Routes through `CharacterAction_CinematicMoveTo`** (rule #22). No bypass of the action lane. |
| 14 | Invincibility during scenes | **`IsCinematicActor : NetworkVariable<bool>`** on `CharacterCinematicState`. Combat / AI / input gate on this flag. |
| 15 | Cinematic clock | **Simulation time** (`Time.deltaTime`, `GameSpeedController`-scaled). World keeps running per requirement. |
| 16 | Identity primitive | **Existing `Character.CharacterId` (string)** — globally unique, persists across reconnects. No custom UUID type. |
| 17 | Per-world vs per-character history | **World-scoped state in `CinematicWorldState : ISaveable`** (world save). **Per-character state in `CharacterCinematicState : ICharacterSaveData<>`** (character profile / `HibernatedNPCData`). New world = new game. |
| 18 | Designer assignment workflow | **Two paths**: (a) edit-time archetype matching via `_participantArchetypes`, (b) runtime imperative assignment via `Cinematics.AssignSceneToCharacter(scene, npc)` storing in `CharacterCinematicState._pendingSceneIds`. |
| 19 | Authoring UX | **Custom Unity Editor window** — drag-reorder, color-coded, real-time validation, inline editing. |
| 20 | Effect dispatch — separate concerns from triggers | **`CinematicEffectSO`** for in-timeline effects (the payload of `TriggerStep`); **`ICinematicTriggerSurface`** for what *starts* a cinematic. Distinct types — no naming overload. |

## 4. Architecture

Six cooperating units, each with a single responsibility and typed interface boundaries.

### Unit 1 — `CinematicDirector` (per-scene runtime)
- `NetworkBehaviour` on a server-spawned `NetworkObject`.
- Observer set = participating players' clients only (non-participants in the same map don't get director RPC traffic).
- Holds the active `CinematicContext` and step iteration state.
- Iterates `_steps` via `ICinematicStep.OnEnter / OnTick / IsComplete / OnExit` — never branches on concrete step type.
- Owns advance-press tally, abort heartbeat, scene-end bookkeeping.

### Unit 2 — `CinematicRegistry` (server-side service)
- Lazy-init static service (per `feedback_lazy_static_registry_pattern`).
- Indexes all `CinematicSceneSO` assets in `Assets/Resources/Data/Cinematics/` by trigger surface type and by `_sceneId`.
- Public eligibility queries: `TryGetBestEligibleOnInteractionAction`, `GetEligibleOnSpatialZone`, etc.
- Public imperative API: `AssignSceneToCharacter`, `RevokeSceneFromCharacter`, `GetPendingScenes`, `GetPlayedScenes`.
- Per-NPC eligibility cache (5s TTL + event-driven invalidation per rule #34).
- Deterministic random seed for `Selector_RandomInRadius` keyed `(sceneId, sessionId, roleId)`.

### Unit 3 — `CharacterCinematicState` (per-character subsystem)
- `CharacterSystem` subsystem on its own child GameObject under each `Character` root (per rule #9 / Character hierarchy).
- Implements `ICharacterSaveData<CinematicHistorySaveData>`.
- Three networked fields: `_isCinematicActor : NetworkVariable<bool>`, `_activeRoleId : NetworkVariable<FixedString64>`, `_activeSceneId : NetworkVariable<FixedString64>`.
- Two persisted collections: `_playedSceneIds : HashSet<string>`, `_pendingSceneIds : HashSet<string>`.
- Read by `CharacterCombat` (skip damage), `CharacterAI` BT (yield), `CharacterInteraction` (block external Talk/Insult), `PlayerController` (block movement/combat input).
- `LoadPriority = 75` (after `CharacterMapTracker` / `CharacterCombat` at 70).

### Unit 4 — `CinematicWorldState` (`ISaveable`)
- Server-only data + thin service wrapper.
- One field: `_playedOncePerWorld : HashSet<string>`.
- Keyed in the world save (`Worlds/{worldGuid}.json`) — fresh per world.
- `OncePerNpc` and `OncePerPlayer` are derived from per-character `_playedSceneIds` — not duplicated here.

### Unit 5 — Polymorphic SO catalogues
- `RoleSelectorSO` (5 v1 implementations).
- `CharacterActionRecipeSO` (5 v1 implementations).
- `CinematicEffectSO` (7 v1 implementations including `Effect_GiveQuest` / `Effect_RemoveQuest`).
- `CinematicEligibilityRuleSO` (6 v1 implementations).
- `CinematicTriggerSurfaceSO` (5 v1 implementations).
- All `[SerializeReference]` lists in scene SOs use polymorphic dropdowns in the editor.

### Unit 6 — Authoring & developer tools
- `CinematicSceneEditor` (Unity editor window, primary authoring surface).
- `CinematicBrowserWindow` (project-wide list/filter/create).
- `DevCinematicModule` (in-game dev panel tab).
- `/cinematic` chat command (`play <id>`, `abort`, `list`).
- `CharacterInspectorView` "Cinematic" sub-tab (read-only + dev-mode action buttons).

### Cross-cutting boundaries (rule #9)
- `CinematicDirector` reaches `Character` only via the facade — never caches or calls subsystems directly.
- `CinematicRegistry` is server-only and stateless except for caches + per-NPC overrides.
- Live `CinematicSceneSO` instances are read-only at runtime — director writes to a separate `CinematicContext`.
- The GOAP / BT layer reads `IsCinematicActor` only — never imports director or scene types.
- `ICinematicCameraController` is the only seam between director and the camera rig — v1 implementation is one class; v2 swaps the impl without scene-data migration.

## 5. Data Model

### 5.1 `CinematicSceneSO` (top-level asset)

```csharp
[CreateAssetMenu(fileName = "NewCinematicScene", menuName = "MWI/Cinematics/Scene")]
public class CinematicSceneSO : ScriptableObject
{
    [Header("Identity")]
    [SerializeField] private string _sceneId;          // stable GUID, generated on creation
    [SerializeField] private string _displayName;      // editor-only label

    [Header("Triggering")]
    [SerializeField] private TriggerAuthority _triggerAuthority = TriggerAuthority.AnyPlayer;
    [SerializeReference] private List<CinematicTriggerSurfaceSO> _triggerSurfaces;
    [SerializeReference] private List<CinematicEligibilityRuleSO> _eligibilityRules;
    [SerializeField] private int _priority = 50;

    [Header("Lifecycle")]
    [SerializeField] private PlayMode _playMode = PlayMode.OncePerWorld;
    [SerializeField] private AdvanceMode _advanceMode = AdvanceMode.AllMustPress;
    [SerializeField] private float _advanceGraceSec = 5f;

    [Header("Cast")]
    [SerializeField] private List<RoleSlot> _roles;

    [Header("Timeline")]
    [SerializeReference] private List<CinematicStep> _steps;

    public string SceneId => _sceneId;
    public string DisplayName => _displayName;
    public TriggerAuthority TriggerAuthority => _triggerAuthority;
    public IReadOnlyList<CinematicTriggerSurfaceSO> TriggerSurfaces => _triggerSurfaces;
    public IReadOnlyList<CinematicEligibilityRuleSO> EligibilityRules => _eligibilityRules;
    public int Priority => _priority;
    public PlayMode PlayMode => _playMode;
    public AdvanceMode AdvanceMode => _advanceMode;
    public float AdvanceGraceSec => _advanceGraceSec;
    public IReadOnlyList<RoleSlot> Roles => _roles;
    public IReadOnlyList<CinematicStep> Steps => _steps;
}
```

### 5.2 Enums + value types

```csharp
public enum TriggerAuthority { AnyPlayer, HostOnly }
public enum PlayMode         { OncePerWorld, OncePerPlayer, OncePerNpc, Repeatable }
public enum AdvanceMode      { AllMustPress, AnyAdvances, TriggerOnly }
public enum InitiatorFilter  { AnyCharacter, PlayerOnly }
public enum ParticipantsMode { Anyone, RequireAtLeastOne, RestrictedToSet }
public enum CompletionMode   { AllComplete, AnyComplete, FirstComplete }
public enum CinematicEndReason { Completed, Aborted, ActorLost, AllPlayersDisconnected }

[Serializable]
public readonly struct ActorRoleId : IEquatable<ActorRoleId>
{
    public readonly string Value;
    public ActorRoleId(string value) { Value = value; }
    // IEquatable + ToString omitted for brevity
}
```

### 5.3 `RoleSlot` + `IRoleSelector`

```csharp
[Serializable]
public struct RoleSlot
{
    public ActorRoleId RoleId;
    public string DisplayName;
    public RoleSelectorSO Selector;
    public bool IsOptional;
    public bool IsPrimaryActor;   // for OncePerNpc keying
}

public abstract class RoleSelectorSO : ScriptableObject
{
    public abstract Character Resolve(CinematicContext ctx);   // null = could not bind
}
```

v1 starter selectors:

| Selector | Behavior |
|----------|----------|
| `Selector_TriggeringPlayer` | Returns `ctx.TriggeringPlayer`. |
| `Selector_OtherParticipant` | Returns the *other* participant (not `ctx.TriggeringPlayer`). For bidirectional Talk scenes — resolves correctly regardless of who initiated. |
| `Selector_SpecificCharacter` | Designer assigns a specific `Character` ref via the project's character-asset resolution layer (typically a `CharacterArchetypeSO` for unique NPCs). |
| `Selector_NearestArchetype` | `_archetype : CharacterArchetypeSO`, `_radius : float`. Closest match around `ctx.TriggerOrigin`. |
| `Selector_RandomInRadius` | `_radius : float`, optional filter. Server-deterministic via seed `(sceneId, sessionId, roleId)`. |

### 5.4 `CinematicContext`

```csharp
public class CinematicContext
{
    public CinematicSceneSO Scene { get; }
    public CinematicDirector Director { get; }
    public Character TriggeringPlayer { get; }            // who fired the trigger
    public Character OtherParticipant { get; }            // talk target / scene partner; null for surfaces with no second party
    public Vector3 TriggerOrigin { get; }
    public IReadOnlyDictionary<ActorRoleId, Character> BoundRoles { get; }
    public IReadOnlyDictionary<string, GameObject> BoundObjects { get; }
    public IReadOnlyList<Character> ParticipatingPlayers { get; }
    public float StartTimeSim { get; }

    public Character GetActor(ActorRoleId id);   // throws if missing required, returns null if optional
    public GameObject GetObject(string key);
}
```

Steps read from context; they don't mutate it post-`OnEnter`.

### 5.5 `ICinematicStep` + `CinematicStep` base

```csharp
public interface ICinematicStep
{
    void OnEnter(CinematicContext ctx);
    void OnTick(CinematicContext ctx, float dt);
    void OnExit(CinematicContext ctx);    // also called on abort
    bool IsComplete(CinematicContext ctx);
}

[Serializable]
public abstract class CinematicStep : ICinematicStep
{
    [SerializeField] protected string _label;   // editor display label

    public virtual void OnEnter(CinematicContext ctx) { }
    public virtual void OnTick(CinematicContext ctx, float dt) { }
    public virtual void OnExit(CinematicContext ctx) { }
    public virtual bool IsComplete(CinematicContext ctx) => true;
}
```

### 5.6 `CharacterActionRecipeSO`

```csharp
public abstract class CharacterActionRecipeSO : ScriptableObject
{
    public abstract CharacterAction Build(Character actor, CinematicContext ctx);
}
```

v1 starter recipes mirror existing `Assets/Scripts/Character/CharacterActions/`:

| Recipe SO | Builds | Inspector fields |
|-----------|--------|------------------|
| `Recipe_Sleep` | `CharacterAction_Sleep` | `_durationSec` |
| `Recipe_SleepOnFurniture` | `CharacterAction_SleepOnFurniture` | actor + furniture role/anchor |
| `Recipe_DestroyHarvestable` | `CharacterAction_DestroyHarvestable` | `_targetSelector : IObjectSelector` |
| `Recipe_IssueOrder` | `CharacterAction_IssueOrder` | `_orderTemplate : OrderSO` |
| `Recipe_Custom` | from `UnityEvent<Character, CinematicContext>` callback | escape hatch |

### 5.7 `CinematicEffectSO`

```csharp
public abstract class CinematicEffectSO : ScriptableObject
{
    public abstract void Apply(CinematicContext ctx);   // server-side; broadcasts ClientRpc as needed
}
```

v1 starter effects:

| Effect SO | Behavior | Targets |
|-----------|----------|---------|
| `Effect_GiveQuest` | Adds quest to target's `CharacterQuestLog` via existing public API. | role + `QuestSO` |
| `Effect_RemoveQuest` | Removes matching quest from target's `CharacterQuestLog`. No-op + warning log if not present. | role + `QuestSO` |
| `Effect_PlayVFX` | Spawns VFX prefab at role/anchor/position. ClientRpc to participating clients. | role/anchor/pos |
| `Effect_PlaySFX` | Plays clip 2D or 3D. | role/pos + `AudioClip` |
| `Effect_PlayAnimation` | Triggers animator parameter on actor. ClientRpc broadcast. | role + parameter name/value |
| `Effect_GiveItem` | Adds `ItemSO` to role's inventory. | role + `ItemSO` + count |
| `Effect_RaiseEvent` | Fires `UnityEvent` (escape hatch). | n/a |

### 5.8 `CinematicEligibilityRuleSO`

```csharp
public abstract class CinematicEligibilityRuleSO : ScriptableObject
{
    public abstract bool Check(CinematicEligibilityContext ctx);
}

public class CinematicEligibilityContext
{
    public CinematicSceneSO Scene;
    public Character TriggeringPlayer;
    public Character OtherParticipant;
    public Vector3 TriggerOrigin;
}
```

v1 starter rules:

| Rule SO | Behavior |
|---------|----------|
| `Rule_RelationTierAtLeast` | Triggering player ↔ other participant relation tier ≥ X |
| `Rule_QuestCompleted` | Specific quest in player's completed list |
| `Rule_ItemHeld` | Triggering player has item X in inventory |
| `Rule_PlayedScenes` | Required prerequisite scenes already played |
| `Rule_TimeOfDayWindow` | Active inside a sim-time window |
| `Rule_Custom` | UnityEvent escape hatch |

### 5.9 `CinematicTriggerSurfaceSO`

```csharp
public abstract class CinematicTriggerSurfaceSO : ScriptableObject
{
    public abstract string SurfaceKey { get; }
}
```

v1 starter surfaces:

| Surface SO | Triggers when |
|------------|---------------|
| `Surface_OnInteractionAction` | Player invokes a `CharacterInteractionAction` on a target. v1 validates action type = `Talk`. |
| `Surface_OnSpatialZone` | A `CinematicTriggerCollider` MonoBehaviour with matching `_zoneId` fires `OnTriggerEnter`. |
| `Surface_OnSceneStart` | Map first becomes active for any player. |
| `Surface_Scripted` | Code-driven via `Cinematics.TryPlay(...)`. Quest hooks live here. |
| `Surface_Debug` | Editor-only; bypasses authority + PlayMode (does NOT mark played). |

`Surface_OnInteractionAction` shape (edit-time data only — runtime per-character assignments live on `CharacterCinematicState._pendingSceneIds`, not on the surface):

```csharp
public class Surface_OnInteractionAction : CinematicTriggerSurfaceSO
{
    [SerializeField] private SerializableType<ICharacterInteractionAction> _actionType;   // v1: Talk only
    [SerializeField] private InitiatorFilter _initiatorFilter = InitiatorFilter.PlayerOnly;
    [SerializeField] private ParticipantsMode _participantsMode = ParticipantsMode.Anyone;
    [SerializeField] private List<CharacterArchetypeSO> _participantArchetypes;

    public bool MatchesEditTime(Character initiator, Character target)
    {
        if (_initiatorFilter == InitiatorFilter.PlayerOnly && !initiator.IsPlayer) return false;

        switch (_participantsMode)
        {
            case ParticipantsMode.Anyone: return true;
            case ParticipantsMode.RequireAtLeastOne:
                return MatchesArchetype(initiator) || MatchesArchetype(target);
            case ParticipantsMode.RestrictedToSet:
                return MatchesArchetype(initiator) && MatchesArchetype(target);
        }
        return false;
    }

    private bool MatchesArchetype(Character c) =>
        _participantArchetypes != null && _participantArchetypes.Contains(c.Archetype);
}
```

Bidirectional matching emerges from set semantics on the archetype list. Runtime per-character assignments are evaluated in parallel by the registry — see §8.4.

## 6. Step Catalog (v1)

All extend `CinematicStep`. The director iterates and calls `OnEnter` → `OnTick`* → checks `IsComplete` → `OnExit`.

### 6.1 `SpeakStep`

```csharp
public class SpeakStep : CinematicStep
{
    [SerializeField] private ActorRoleId _speaker;
    [TextArea(2, 8)]
    [SerializeField] private string _lineText;          // supports [roleX].getName placeholders
    [SerializeField] private float _typingSpeed = 0f;   // 0 = default
    [SerializeField] private AudioClip _voiceClip;
    [SerializeField] private DialogueSO _legacyDialogue;   // alternative — wraps multi-line legacy SO
}
```

- **OnEnter**: resolves placeholders against `ctx.BoundRoles`; calls `actor.CharacterSpeech.SayScripted(text, speed, onTypingDone)` (existing replication); plays voice clip; clears director's per-line `_pressed` tally.
- **IsComplete**: typing finished AND advance-mode condition met (per `_advanceMode`).
- **OnExit**: `actor.CharacterSpeech.CloseSpeech()`.
- **Legacy bridge**: if `_legacyDialogue` set, iterates its `DialogueLine`s as sub-lines (single advance press per line). Migration path for §11.

### 6.2 `MoveActorStep`

```csharp
public class MoveActorStep : CinematicStep
{
    public enum TargetMode { Role, Anchor, WorldPos }

    [SerializeField] private ActorRoleId _actor;
    [SerializeField] private TargetMode _targetMode;
    [SerializeField] private ActorRoleId _targetRole;
    [SerializeField] private string _targetAnchor;
    [SerializeField] private Vector3 _targetPos;
    [SerializeField] private float _stoppingDist = 1.5f;
    [SerializeField] private bool _blocking = true;
    [SerializeField] private float _timeoutSec = 30f;
    [SerializeField] private bool _faceTarget = true;
}
```

- **OnEnter**: resolves target → `Vector3`; enqueues `CharacterAction_CinematicMoveTo` on `actor.CharacterActions`. Action subscribes director's `OnActionFinished`.
- **IsComplete**: `_blocking == false` → instant. `true` → on action finished OR timeout.
- **OnExit**: if `_faceTarget`, server snaps facing (replicated via existing facing sync).
- **Replication**: free — `CharacterMovement` is already net-synced.

### 6.3 `WaitStep`

```csharp
public class WaitStep : CinematicStep
{
    [SerializeField] private float _durationSec;
}
```

- **OnEnter**: stores `_endTime = simNow + _durationSec`.
- **IsComplete**: `simNow >= _endTime`.
- Sim time, not real time (rule #26).

### 6.4 `CameraFocusStep`

```csharp
public class CameraFocusStep : CinematicStep
{
    public enum TargetMode { Role, Object, WorldPos, RestoreDefault }

    [SerializeField] private TargetMode _targetMode;
    [SerializeField] private ActorRoleId _targetRole;
    [SerializeField] private string _objectKey;
    [SerializeField] private Vector3 _targetPos;
    [SerializeField] private float _zoomOverride;
    [SerializeField] private bool _useZoomOverride;
    [SerializeField] private float _smoothLerpSec = 0.5f;
    [SerializeField] private float _holdDurationSec;   // 0 = hold until next focus or scene end
}
```

- **OnEnter**: server fires `FocusCameraClientRpc(targetWorldPos, zoom, lerpSec)` to participating clients only.
- **IsComplete**: `_holdDurationSec == 0` → instant. `> 0` → after duration.
- **OnExit**: if `_holdDurationSec > 0`, fires paired `RestoreCameraClientRpc`.
- **Scene-end cleanup**: director always fires `RestoreCameraClientRpc` on scene end / abort.

### 6.5 `TriggerStep`

```csharp
public class TriggerStep : CinematicStep
{
    [SerializeField] private CinematicEffectSO _effect;
    [SerializeField] private UnityEvent _eventHook;
}
```

- **OnEnter**: `_effect?.Apply(ctx)`; `_eventHook?.Invoke()`.
- **IsComplete**: true on next tick (fire-and-forget).
- **Use case**: "trigger before/after a dialog line" = place `TriggerStep` before/after the `SpeakStep` in `_steps`.

### 6.6 `ChoiceStep`

```csharp
public class ChoiceStep : CinematicStep
{
    [SerializeField] private string _prompt;
    [SerializeField] private List<ChoiceOption> _options;
    [SerializeField] private ActorRoleId _chooserRole;
    [SerializeField] private float _timeoutSec;
    [SerializeField] private int _timeoutOption;
}

[Serializable]
public class ChoiceOption
{
    public string Label;
    public CinematicSceneSO BranchScene;       // jump to a different scene's _steps
    [SerializeReference] public List<CinematicStep> BranchInline;   // OR inline sub-steps
}
```

- **OnEnter**: server resolves `_chooserRole`. Player chooser → `ShowChoicesClientRpc(targetClient, ...)`. NPC chooser in v1 picks option 0.
- **IsComplete**: chooser submitted choice OR timeout elapsed.
- **OnExit**: pushes the chosen branch's steps onto the director's iteration queue. Eligibility / PlayMode of the parent scene continues to apply — the branch is *content*, not a separate scene playthrough.
- **Role binding for `BranchScene`**: the branch's roles are **not** re-resolved. `ActorRoleId` references inside the branch's steps are resolved against the **parent's `BoundRoles`** dictionary. Roles declared on `BranchScene._roles` that are not present in the parent are bound on demand using the branch's selectors. Role IDs that collide between parent and branch use the parent's binding (shadow rule). This keeps `Hero` consistent across the whole playthrough.

### 6.7 `ParallelStep`

```csharp
public class ParallelStep : CinematicStep
{
    [SerializeReference] private List<CinematicStep> _children;
    [SerializeField] private CompletionMode _completionMode = CompletionMode.AllComplete;
}
```

- **OnEnter**: calls `OnEnter` on every child.
- **OnTick**: ticks every child whose `IsComplete` is false.
- **IsComplete**: per `_completionMode`.
- **OnExit**: calls `OnExit` on all children that haven't already exited.
- **Nesting**: unbounded — director iterates uniformly via interface.

### 6.8 `ExecuteActionStep`

```csharp
public class ExecuteActionStep : CinematicStep
{
    [SerializeField] private ActorRoleId _actor;
    [SerializeField] private CharacterActionRecipeSO _recipe;
    [SerializeField] private bool _blocking = true;
}
```

- **OnEnter**: `recipe.Build(actor, ctx)` → `CharacterAction`. Enqueue on `actor.CharacterActions`. Subscribe `OnActionFinished`.
- **IsComplete**: `_blocking == false` → instant. `true` → on action finished OR cancellation.
- **OnExit**: if aborted mid-action, calls `action.OnCancel()`.
- **Combat parity**: future `Recipe_AttackTarget` against an `IsCinematicActor` target produces full animation, zero damage (combat checks the target's flag).

### 6.9 Step completion summary

| Step | Completes when |
|------|----------------|
| `SpeakStep` | Typing done + advance-press contract met |
| `MoveActorStep` | Action finished OR timeout (or instant if non-blocking) |
| `WaitStep` | Sim-time elapsed |
| `CameraFocusStep` | Instant (or after `_holdDurationSec`) |
| `TriggerStep` | Next tick (fire-and-forget) |
| `ChoiceStep` | Submitted OR timeout |
| `ParallelStep` | Per `_completionMode` |
| `ExecuteActionStep` | Action finished (or instant if non-blocking) |

## 7. Network Protocol

### 7.1 NetworkVariables

On `CharacterCinematicState` (per character):

| Field | Type | Authority |
|-------|------|-----------|
| `_isCinematicActor` | `NetworkVariable<bool>` | server-write, all-clients-read |
| `_activeRoleId` | `NetworkVariable<FixedString64>` | server-write, all-clients-read |
| `_activeSceneId` | `NetworkVariable<FixedString64>` | server-write, all-clients-read |

On `CinematicDirector` (per active scene):

| Field | Type | Notes |
|-------|------|-------|
| `_currentStepIndex` | `NetworkVariable<int>` | for late-joiner snapshot |
| `_isAborted` | `NetworkVariable<bool>` | sticky during the last frame before despawn |
| `_advanceMode` | `NetworkVariable<AdvanceMode>` | baked from SO at spawn |

The director does **not** replicate the full step list. Per-step `ClientRpc`s drive client-visible state.

### 7.2 ServerRpc surface (client → server)

| RPC | Caller | Purpose |
|-----|--------|---------|
| `CinematicEntryNetSync.TryStartOnInteractionActionServerRpc(targetNetId, actionTypeKey)` | Talking client | Eligibility check + scene start. |
| `CinematicEntryNetSync.TryStartFromColliderServerRpc(zoneId)` | Client entered collider | Same gate. |
| `CinematicDirector.AdvanceServerRpc()` | Participating client | Records advance press; idempotent per (line, clientId). |
| `CinematicDirector.SubmitChoiceServerRpc(int optionIndex)` | Chooser client | For `ChoiceStep`. Server validates sender against `_chooserRole`. |
| `CinematicDirector.RequestAbortServerRpc()` | Client | Honored only if `DevMode` enabled OR sender is host. |

`AdvanceServerRpc` server-side de-dupes per (currentLineIndex, clientId).

### 7.3 ClientRpc surface (server → participating clients)

| RPC | Purpose |
|-----|---------|
| `OnSceneStartedClientRpc(sceneId, roleAssignments[])` | Each client maps `roleId → Character.NetworkObjectId`. |
| `OnLineShownClientRpc(stepIndex, speakerNetId, text, typingSpeed)` | Triggers `CharacterSpeech.SayScripted` on speaker. |
| `OnLineAdvanceUiClientRpc(stepIndex, pressedClientIds[], graceRemaining)` | HUD: "2/3 ready, 4.2s". Sent on each press + once/sec during grace. |
| `OnLineAdvancedClientRpc(nextStepIndex)` | Closes current bubble, advances local step pointer for HUD. |
| `OnFocusCameraClientRpc(targetWorldPos, zoom?, lerpSec)` | `CinematicCameraController` lerps. |
| `OnRestoreCameraClientRpc(lerpSec)` | Camera returns to default follow. |
| `OnShowChoicesClientRpc(targetClient, prompt, options[], timeoutSec)` | Targeted RPC — only chooser receives. Renders `UI_DialogueChoicesWindow`. |
| `OnSceneEndedClientRpc(reason)` | Reason ∈ `CinematicEndReason`. Triggers cleanup. |

### 7.4 Advance-press protocol (canonical `AllMustPress` mode)

```
SERVER                                             PARTICIPATING CLIENTS
─────                                              ─────────────────────
StepEnter (SpeakStep)
  speakerCharacterSpeech.SayScripted(text)         CharacterSpeech bubble appears (existing replication)
  _pressed.Clear(); graceTimerActive = false

  ◄── (player A presses Space) ─── AdvanceServerRpc
  _pressed.Add(A.ClientId)
  graceTimerActive = true; deadline = simNow + advanceGraceSec
  OnLineAdvanceUiClientRpc(idx, [A], grace=5.0) ──►  HUD: "1/3 ready, 5.0s"

  ◄── (player B presses Space) ─── AdvanceServerRpc
  _pressed.Add(B.ClientId)
  OnLineAdvanceUiClientRpc(idx, [A,B], grace=3.4)──►  HUD: "2/3 ready, 3.4s"

  [tick: simNow ≥ deadline OR _pressed == participatingPlayers]
  OnLineAdvancedClientRpc(nextIdx) ──────────────►  bubble closes, advance HUD
StepExit; load next step
```

`AnyAdvances` mode: first press advances. `TriggerOnly` mode: only `ctx.TriggeringPlayer.ClientId`'s press counts. Disconnected players auto-yield (added to `_pressed` server-side). NPC-only scenes (zero participating players) advance on grace timeout.

### 7.5 Director `NetworkObject` lifecycle

```
[trigger fires, eligibility passes]
SERVER:
  1. director = NetworkObjectPool.Spawn(_directorPrefab)
  2. director.NetworkObject.SpawnWithObservers(participatingClientIds)
       └── observers = participating players' clients only
  3. director.Initialize(scene, ctx)
  4. foreach actor in BoundRoles:
       actor.CharacterCinematicState._isCinematicActor.Value = true
       actor.CharacterCinematicState._activeRoleId.Value     = roleId
       actor.CharacterCinematicState._activeSceneId.Value    = sceneId
  5. OnSceneStartedClientRpc to participating clients
  6. director.RunStepLoop()    // coroutine

[scene ends or aborts]
SERVER:
  7. foreach actor: clear all three NetworkVariables
  8. OnSceneEndedClientRpc(reason)
  9. director.NetworkObject.Despawn(destroy=true)
 10. if reason == Completed:
        worldState.MarkPlayed(scene, ctx)                       // OncePerWorld
        foreach actor in BoundRoles:
            actor.CharacterCinematicState.MarkSceneCompleted(sceneId)
            actor.CharacterCinematicState.RemovePendingScene(sceneId)
```

### 7.6 Late-joiner story

| Concern | v1 behavior |
|---------|-------------|
| Joiner not bound to any role | Treated as non-participant. Sees actors animate via existing replication. No director RPCs, no camera focus. Input unrestricted. |
| Joiner WAS bound but disconnected | Treated as non-participant on rejoin. Scene continues with disconnected player auto-yielding presses. |
| Joiner combat targets a participating actor | Damage skipped via `IsCinematicActor` (synced regardless of director observership). |
| Joiner sees `IsCinematicActor` NPC | NPC's BT yields normally. |

### 7.7 Failure modes

| Scenario | Server response |
|----------|-----------------|
| Step throws in `OnEnter`/`OnTick` | Caught, logged with full context (rule #31). Step skipped → next. 3 consecutive failures → abort. |
| Bound actor `Character` destroyed mid-scene | Director heartbeat (1 Hz) checks `BoundRoles`. Required missing → abort. Optional missing → continue, target steps skip. |
| Map containing actors hibernates | `MapController` hibernation hook checks for active cinematics. Defers hibernation OR aborts the scene if hibernation forced. |
| Action in `ExecuteActionStep` cancelled externally | Treated as completion (not abort). Director moves to next step. |
| `_currentStepIndex` exceeds `_steps.Count` | Normal end; `OnSceneEndedClientRpc(Completed)`. |

### 7.8 Authority validation (rule #19)

| Scenario | Validated path |
|----------|----------------|
| Host triggers + Host-only actor | Server runs director, host receives RPCs as participating. ✓ |
| Host triggers + remote client actor | Director observers include both. ✓ |
| Client triggers (`AnyPlayer`) + multiple actors | Server validates `_triggerAuthority`, spawns director with all participating clients as observers. ✓ |
| Client tries `HostOnly` scene | Eligibility returns 0 → fallback to generic Talk. No director spawned. ✓ |
| NPC-only scene (no players) | Director spawns with empty observer list; runs purely server-side; advances on grace timeout. ✓ |

## 8. Eligibility & PlayMode Bookkeeping

### 8.1 `CinematicRegistry` public API

```csharp
public static class Cinematics    // facade
{
    public static bool TryPlay(string sceneId, Character triggeringPlayer, Character otherParticipant = null);
    public static void AssignSceneToCharacter(CinematicSceneSO scene, Character npc);
    public static void RevokeSceneFromCharacter(CinematicSceneSO scene, Character npc);
    public static IReadOnlyCollection<string> GetPendingScenes(Character character);
    public static IReadOnlyCollection<string> GetPlayedScenes(Character character);
    public static IEnumerable<CinematicSceneSO> GetEligibleScenesForCharacter(
        Character character, bool perspectiveAsInitiator = true);
}
```

Internal registry methods (server-only):

```csharp
internal static class CinematicRegistry
{
    public static CinematicSceneSO TryGetBestEligibleOnInteractionAction(
        Character initiator, Character target, Type actionType);
    public static IEnumerable<CinematicSceneSO> GetEligibleOnSpatialZone(Character player, string zoneId);
    public static IEnumerable<CinematicSceneSO> GetEligibleOnSceneStart(string mapId);
    public static CinematicSceneSO GetById(string sceneId);
    public static bool TryStart(CinematicSceneSO scene, CinematicContext ctx);   // spawns director
}
```

### 8.2 Eligibility evaluation

```
fn IsEligible(scene, ctx):
    if scene._triggerAuthority == HostOnly && !ctx.TriggeringPlayer.IsHost: return false
    if !PlayModePermits(scene, ctx): return false
    foreach rule in scene._eligibilityRules:
        if !rule.Check(eligibilityCtx): return false
    if !RolesAreBindable(scene, ctx): return false      // dry-run; required roles only
    return true
```

`RolesAreBindable` runs `IRoleSelector.Resolve` for every required role. The deterministic random seed `(sceneId, sessionId, roleId)` ensures the actual play resolves to the same characters.

### 8.3 PlayMode permission check

```
fn PlayModePermits(scene, ctx):
    switch scene._playMode:
      OncePerWorld:
        return !worldState.HasPlayedInWorld(scene._sceneId)
      OncePerPlayer:
        return !ctx.TriggeringPlayer.CinematicState.HasPlayedScene(scene._sceneId)
      OncePerNpc:
        var primaryNpc = ResolvePrimaryActor(scene, ctx)
        return primaryNpc != null && !primaryNpc.CinematicState.HasPlayedScene(scene._sceneId)
      Repeatable:
        return true
```

`ResolvePrimaryActor` returns the role marked `_isPrimaryActor` (default: the role with `Selector_OtherParticipant` if any, else first non-player role). Editor validation flags `OncePerNpc` scenes without a primary actor.

### 8.4 Bidirectional `OnInteractionAction` matching

A scene is a candidate via either:
- **Edit-time path**: `Surface_OnInteractionAction.MatchesEditTime(initiator, target)` returns true (archetype-based, set-semantic, bidirectional automatically).
- **Runtime-assigned path**: the scene is in `initiator.CharacterCinematicState.GetPendingScenes()` OR `target.CharacterCinematicState.GetPendingScenes()`.

```
fn TryGetBestEligibleOnInteractionAction(initiator, target, actionType):
    candidates = registry.ScenesIndexedByTriggerType[typeof(Surface_OnInteractionAction)]
    eligible = []
    ctx = BuildContext(initiator, target)

    foreach scene in candidates:
        foreach surface in scene._triggerSurfaces.OfType<Surface_OnInteractionAction>():
            if surface._actionType != actionType: continue

            // Edit-time match (archetype set semantics — bidirectional)
            bool editTimeMatch = surface.MatchesEditTime(initiator, target);

            // Runtime-assigned match (scene is pending on either character)
            bool runtimeMatch = false;
            if surface._initiatorFilter == AnyCharacter
               || (surface._initiatorFilter == PlayerOnly && initiator.IsPlayer):
                runtimeMatch =
                    initiator.CharacterCinematicState.GetPendingScenes().Contains(scene.SceneId)
                 || target.CharacterCinematicState.GetPendingScenes().Contains(scene.SceneId);

            if (editTimeMatch || runtimeMatch) && IsEligible(scene, ctx):
                eligible.add(scene); break

    return eligible.OrderByDescending(s => s.Priority).FirstOrDefault()
```

Runtime-assigned scenes still respect the `_initiatorFilter` on the surface — assigning a `PlayerOnly` scene to an NPC and having that NPC initiate doesn't fire it.

### 8.5 Per-NPC eligibility cache (perf, rule #34)

```csharp
private Dictionary<string /*characterId*/, CachedEligibility> _eligibilityCache;

private struct CachedEligibility
{
    public string PlayerKey;
    public List<CinematicSceneSO> Scenes;
    public float Timestamp;
}
```

Invalidated on: scene played, quest event, relation tier crossed, override mutation, world state load. Hard TTL 5 sim-seconds. Server-only, never replicated.

## 9. Trigger Surfaces — Wiring

### 9.1 Talk integration

`CharacterInteraction.OnTalk(player, npc)` server-side modification:

```csharp
[ServerSide]
public void OnTalk(Character player, Character npc)
{
    var scene = CinematicRegistry.TryGetBestEligibleOnInteractionAction(
        player, npc, typeof(CharacterInteractionAction_Talk));
    if (scene != null)
    {
        var ctx = BuildContext(player, npc);
        if (CinematicRegistry.TryStart(scene, ctx)) return;   // cinematic owns the interaction
    }
    StartGenericTalkInteraction(player, npc);   // existing fallback
}
```

Bidirectional matching is built into `Surface_OnInteractionAction.Matches` — the registry calls it with (initiator, target) and the surface checks both directions automatically.

### 9.2 `CinematicTriggerCollider` MonoBehaviour

```csharp
public class CinematicTriggerCollider : MonoBehaviour
{
    [SerializeField] private string _zoneId;
    [SerializeField] private bool _onceOnly;

    private void OnTriggerEnter(Collider other)
    {
        if (!NetworkManager.Singleton.IsServer) return;
        var character = other.GetComponentInParent<Character>();
        if (character == null || !character.IsPlayer) return;
        var scenes = CinematicRegistry.GetEligibleOnSpatialZone(character, _zoneId);
        var scene = scenes.OrderByDescending(s => s.Priority).FirstOrDefault();
        if (scene != null)
        {
            CinematicRegistry.TryStart(scene, BuildContext(character, transform.position));
            if (_onceOnly) GetComponent<Collider>().enabled = false;
        }
    }
}
```

### 9.3 Scripted (code-driven)

```csharp
public static class Cinematics
{
    public static bool TryPlay(string sceneId, Character triggeringPlayer, Character otherParticipant = null)
    {
        if (!NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning("[Cinematics] TryPlay called from client; server-only.");
            return false;
        }
        var scene = CinematicRegistry.GetById(sceneId);
        if (scene == null) return false;
        var ctx = new CinematicContext { ... };
        return CinematicRegistry.TryStart(scene, ctx);
    }
}
```

Used by quest scripts, BT actions, narrative timers.

### 9.4 Debug surface

`/cinematic` chat command (registered via `DevModeManager`):

| Subcommand | Behavior |
|------------|----------|
| `/cinematic play <sceneId>` | Bypass `_triggerAuthority`, eligibility, PlayMode. Does NOT mark played. Triggering player = command sender. |
| `/cinematic abort` | Force-end active scene targeted at sender. |
| `/cinematic list` | Dump registry contents to console. |
| `/cinematic assign <sceneId> <characterId>` | Calls `Cinematics.AssignSceneToCharacter`. |
| `/cinematic revoke <sceneId> <characterId>` | Calls `Cinematics.RevokeSceneFromCharacter`. |

## 10. Persistence

### 10.1 Storage matrix

| Data | Lives where | Persistence mechanism |
|------|-------------|----------------------|
| `_playedOncePerWorld : HashSet<string>` | `CinematicWorldState` | `ISaveable` → `Worlds/{worldGuid}.json` |
| Per-character `_playedSceneIds` | `CharacterCinematicState` | `ICharacterSaveData<CinematicHistorySaveData>` → character profile (player) / `HibernatedNPCData.ProfileData` (NPC) |
| Per-character `_pendingSceneIds` | `CharacterCinematicState` | Same as above |
| Active director state | not persisted | Auto-aborts on save/load |

### 10.2 `CinematicHistorySaveData`

```csharp
[Serializable]
public class CinematicHistorySaveData
{
    public List<string> PlayedSceneIds = new();
    public List<string> PendingSceneIds = new();
}
```

`CharacterCinematicState : ICharacterSaveData<CinematicHistorySaveData>`:
- `SaveKey = "CharacterCinematicState"`
- `LoadPriority = 75`
- Auto-discovered by `CharacterDataCoordinator.GetComponentsInChildren<ICharacterSaveData>`.
- `Serialize()` → `new CinematicHistorySaveData { PlayedSceneIds = _playedSceneIds.ToList(), PendingSceneIds = _pendingSceneIds.ToList() }`.
- `Deserialize(data)` → repopulates the two HashSets.

### 10.3 `CinematicWorldState` (ISaveable)

```csharp
public class CinematicWorldState : ISaveable
{
    private HashSet<string> _playedOncePerWorld = new();

    public bool HasPlayedInWorld(string sceneId) => _playedOncePerWorld.Contains(sceneId);
    public void MarkPlayedInWorld(string sceneId) => _playedOncePerWorld.Add(sceneId);

    public string SaveKey => "CinematicWorldState";
    public string Serialize() => /* Newtonsoft */;
    public void Deserialize(string json) => /* Newtonsoft */;
}
```

### 10.4 New world = new game

Path automatically validated: when `WorldSelectMenu` creates World B and calls `GameLauncher.Launch()`:
1. `Worlds/{worldB-guid}.json` does not yet exist.
2. `SaveManager` loads → `CinematicWorldState` defaults to empty.
3. `_playedOncePerWorld = {}` → every `OncePerWorld` scene is fresh.
4. The character profile (with its `_playedSceneIds` for player) is untouched, but those entries only matter for `OncePerPlayer` mode (which is intentionally cross-world).

`OncePerWorld` scenes re-trigger across new worlds. ✓ matches the user's "world is like a new game" requirement.

## 11. Migration Plan

### 11.1 Existing `DialogueSO` assets

No data migration. Existing assets continue to work via auto-wrapping when they're triggered through the legacy path:

- `DialogueManager.StartDialogue(dialogueSO, participants)` internally creates a one-step `CinematicSceneSO` with a single `SpeakStep._legacyDialogue = dialogueSO` and delegates to `CinematicDirector`.
- All callers (existing context menu, future code paths) work unchanged.
- Designers explicitly "promote" a legacy `DialogueSO` into a real cinematic by hand-authoring a `CinematicSceneSO` that wraps it in a richer step list.

### 11.2 `DialogueManager` — phased deprecation

**Milestone M1 — coexistence (ships with cinematic system):**
- `DialogueManager.StartDialogue` API preserved; internals delegate to `CinematicDirector`.
- `Trigger Serialized Dialogue` context menu routes through cinematic system.
- `IsInDialogue` reads `_owner.CharacterCinematicState.IsCinematicActor`.
- `Update()` `Input.GetKeyDown(Space)` poll **moved to `PlayerController`** (resolves rule #33 violation).
- Caller-code changes: zero.

**Milestone M2 — cleanup (one release cycle later):**
- Mark `DialogueManager` `[Obsolete]`.
- Audit/replace remaining direct callers of `StartDialogue` and `IsInDialogue`.
- Delete after grace period.

### 11.3 Save migration

- `CinematicWorldState` defaults empty for existing world saves (graceful `ISaveable` default).
- `CharacterCinematicState.History` defaults empty for existing character profiles.
- Existing saves load without changes; played-cinematic state starts fresh post-update.

## 12. Authoring & Tools

### 12.1 Editor windows

| Window | Purpose | Menu path |
|--------|---------|-----------|
| **Cinematic Scene Editor** | Primary authoring window for one `CinematicSceneSO`. Drag-reorder, color-coded steps, real-time validation, inline editing. | `Window → MWI → Cinematic Scene Editor` (or double-click any `CinematicSceneSO` asset) |
| **Cinematic Browser** | Project-wide list of all `CinematicSceneSO` assets. Filter by trigger type, PlayMode, status, tags. "New Scene" button. | `Window → MWI → Cinematic Browser` |
| **Validation Panel** | Live issue list embedded in Scene Editor + standalone `Tools → MWI → Validate All Cinematics`. | Embedded |

Scene Editor layout (vertical sections):

1. **Identity** — sceneId (read-only GUID), displayName, triggerAuthority, playMode, advanceMode, advanceGraceSec, priority.
2. **Trigger Surfaces** — list of `CinematicTriggerSurfaceSO` refs, +/- buttons, polymorphic dropdown.
3. **Eligibility** — list of `CinematicEligibilityRuleSO` refs.
4. **Cast** — list of `RoleSlot`s with inline edit (roleId, displayName, selector, isOptional, isPrimaryActor).
5. **Timeline** — vertical step list, color per step type, drag-reorder, indented children for ParallelStep / ChoiceStep, inline expansion of step fields.
6. **Footer** — `[+ Add Step ▾]` palette button, `[▶ Test]` (enters playmode + invokes scene), `[Validate]` (re-runs OnValidate).

Color scheme: blue=Speak, green=Move, gray=Wait, purple=Camera, orange=Trigger, pink=Choice, yellow=Parallel, red=ExecuteAction.

### 12.2 Asset creation

`Right-click in Project window → Create → MWI → Cinematics → Cinematic Scene` creates an asset in the current folder (target: `Assets/Resources/Data/Cinematics/`). Auto-fills `_sceneId` with a fresh GUID at creation.

### 12.3 Editor-time validation (`OnValidate` + custom panel)

- Every `[SerializeReference]` step entry non-null (no "Missing reference").
- Every `ActorRoleId` in any step references a `RoleSlot._roleId` that exists.
- `OncePerNpc` scenes have at least one role flagged `_isPrimaryActor`.
- `ChoiceStep._chooserRole` references an existing role.
- `ChoiceStep._branchScene` chains can't infinite-recurse (depth-limited at 8).
- `Surface_OnInteractionAction._actionType` is `Talk` (v1 limitation).
- All step `_actor` / `_speaker` references resolve to declared roles.

### 12.4 Anchor gizmos (Scene view)

When a `CinematicSceneSO` is selected in the Scene Editor, the Scene view draws:
- Colored arrows + labels for every `MoveActorStep` target (Vector3 / named anchor) and `CameraFocusStep` target.
- Uses `OnDrawGizmos` style + `Handles.Label` for the role names.

### 12.5 Multiplayer test launcher (optional v1)

If the project ships a multi-process test pipeline, integrate. Otherwise, ship a manual MP smoke checklist covering:

- All 5 trigger surfaces × 4 player relationships (Host↔Client, Client↔Client, Host↔NPC, Client↔NPC).
- All 3 advance modes × 2/3/4 participating players.
- Disconnect mid-scene scenarios (single client, host, all-but-one).
- Host-only trigger filter rejection on client side.

### 12.6 Dev tools (extends `DevModeManager`)

- New `DevCinematicModule` tab — list of known scenes (filtered by current map's biome + NPC composition), "Play here" button (current player as triggering), live director state for active scenes (step index, bound roles, advance-press tally, time elapsed).
- `CharacterInspectorView` "Cinematic" sub-tab — `IsCinematicActor`, `_activeRoleId`, `_activeSceneId`, history of played scenes (with display names + completion timestamps), pending list. Dev-mode-only buttons: "Force-add to pending", "Force-remove from pending", "Force-mark as played".
- `/cinematic` chat command per §9.4.

## 13. File / Component Manifest

### New runtime files (server-side)

```
Assets/Scripts/Cinematics/
├── Core/
│   ├── CinematicSceneSO.cs
│   ├── CinematicStep.cs                   // ICinematicStep + abstract base
│   ├── CinematicContext.cs
│   ├── CinematicDirector.cs               // NetworkBehaviour
│   ├── CinematicEntryNetSync.cs           // ServerRpc trigger entry points
│   ├── CinematicRegistry.cs               // server-only static
│   ├── CinematicWorldState.cs             // ISaveable
│   ├── Cinematics.cs                      // public facade
│   └── Enums.cs                           // TriggerAuthority, PlayMode, etc.
├── Steps/
│   ├── SpeakStep.cs
│   ├── MoveActorStep.cs
│   ├── WaitStep.cs
│   ├── CameraFocusStep.cs
│   ├── TriggerStep.cs
│   ├── ChoiceStep.cs
│   ├── ParallelStep.cs
│   └── ExecuteActionStep.cs
├── Roles/
│   ├── RoleSlot.cs
│   ├── ActorRoleId.cs
│   ├── RoleSelectorSO.cs                  // abstract
│   ├── Selector_TriggeringPlayer.cs
│   ├── Selector_OtherParticipant.cs
│   ├── Selector_SpecificCharacter.cs
│   ├── Selector_NearestArchetype.cs
│   └── Selector_RandomInRadius.cs
├── Recipes/
│   ├── CharacterActionRecipeSO.cs         // abstract
│   ├── Recipe_Sleep.cs
│   ├── Recipe_SleepOnFurniture.cs
│   ├── Recipe_DestroyHarvestable.cs
│   ├── Recipe_IssueOrder.cs
│   └── Recipe_Custom.cs
├── Effects/
│   ├── CinematicEffectSO.cs               // abstract
│   ├── Effect_GiveQuest.cs
│   ├── Effect_RemoveQuest.cs
│   ├── Effect_PlayVFX.cs
│   ├── Effect_PlaySFX.cs
│   ├── Effect_PlayAnimation.cs
│   ├── Effect_GiveItem.cs
│   └── Effect_RaiseEvent.cs
├── Rules/
│   ├── CinematicEligibilityRuleSO.cs      // abstract
│   ├── Rule_RelationTierAtLeast.cs
│   ├── Rule_QuestCompleted.cs
│   ├── Rule_ItemHeld.cs
│   ├── Rule_PlayedScenes.cs
│   ├── Rule_TimeOfDayWindow.cs
│   └── Rule_Custom.cs
├── Surfaces/
│   ├── CinematicTriggerSurfaceSO.cs       // abstract
│   ├── Surface_OnInteractionAction.cs
│   ├── Surface_OnSpatialZone.cs
│   ├── Surface_OnSceneStart.cs
│   ├── Surface_Scripted.cs
│   ├── Surface_Debug.cs
│   └── CinematicTriggerCollider.cs        // MonoBehaviour
├── Camera/
│   ├── ICinematicCameraController.cs
│   └── CinematicCameraController.cs
└── Actions/
    └── CharacterAction_CinematicMoveTo.cs
```

### New character subsystem

```
Assets/Scripts/Character/CharacterCinematicState/
├── CharacterCinematicState.cs             // CharacterSystem subsystem
├── CinematicHistorySaveData.cs            // DTO
└── CharacterCinematicStateSaveAdapter.cs  // ICharacterSaveData<T> bridge
```

### Editor-only files

```
Assets/Scripts/Editor/Cinematics/
├── CinematicSceneEditor.cs                // primary authoring window
├── CinematicBrowserWindow.cs
├── CinematicSceneSOInspector.cs           // CustomEditor for SO
├── CinematicStepDrawer.cs                 // PropertyDrawer for [SerializeReference]
├── CinematicGizmoDrawer.cs                // Scene view anchor gizmos
├── CinematicValidator.cs                  // shared validation logic
└── DevCinematicModule.cs                  // dev panel tab
```

### Modified existing files

| File | Change |
|------|--------|
| `Assets/Scripts/Character/Character.cs` | Add `[SerializeField] _cinematicState : CharacterCinematicState` + `Awake()` `GetComponentInChildren<>()` fallback. |
| `Assets/Scripts/Character/CharacterCombat/CharacterCombat.cs` | Damage application checks `target.CharacterCinematicState.IsCinematicActor`; skips damage if true. |
| `Assets/Scripts/Character/CharacterAI/*` (BT root) | Yield gate at root: if `IsCinematicActor`, return Running without ticking children. |
| `Assets/Scripts/Character/CharacterInteraction/CharacterInteraction.cs` | Block `StartInteractionWith(...)` if either party is `IsCinematicActor`. Modify `OnTalk` per §9.1. |
| `Assets/Scripts/Character/CharacterControllers/PlayerController.cs` | Owns `AdvanceCinematic` input (Space). Forwards to `CinematicDirector.AdvanceServerRpc`. Blocks movement/combat input when self is `IsCinematicActor`. |
| `Assets/Scripts/Dialogue/DialogueManager.cs` | Becomes thin wrapper delegating to `CinematicDirector`. Remove `Update()` Input poll. |
| `Assets/Scripts/Core/SaveLoad/SaveManager.cs` | Register `CinematicWorldState` ISaveable. |
| `Assets/Scripts/World/MapController.cs` | Hibernation hook checks for active cinematics with actors on the map → defer or abort. |
| `Assets/Scripts/Debug/DevModeManager.cs` (or equivalent) | Register `/cinematic` chat command + `DevCinematicModule` tab. |

## 14. Documentation Deliverables (rules #28, #29, #29b)

| File | Status | Action |
|------|--------|--------|
| `.agent/skills/cinematic-system/SKILL.md` | new | Procedure source of truth — authoring workflow, common patterns, troubleshooting. |
| `.agent/skills/dialogue-system/SKILL.md` | update | Mark legacy primitives, link to cinematic-system as canonical scripted-scene path. |
| `wiki/systems/cinematic.md` | new | Architecture page (links to SKILL in Sources). |
| `wiki/systems/dialogue.md` | update | Cross-link cinematic, mark legacy. |
| `wiki/systems/dialogue-manager.md` | update | Note thin-wrapper status post-M1. |
| `wiki/systems/scripted-speech.md` | update | Cross-link cinematic. |
| `.claude/agents/cinematic-specialist.md` | new | New agent, `model: opus` per `feedback_always_opus`. Domain: cinematic system, role binding, step authoring, network protocol, persistence. |

## 15. v1 Concrete Deliverables Checklist

- [ ] 8 step types implemented + tested in EditMode unit tests.
- [ ] `CinematicDirector` runs scenes server-side with full step lifecycle + abort handling.
- [ ] `CinematicRegistry` lazy-init, eligibility queries, per-NPC override storage.
- [ ] `CharacterCinematicState` subsystem on `Character` prefab + `Profiles/{characterGuid}.json` round-trip.
- [ ] `CinematicWorldState` `ISaveable` round-trip.
- [ ] 5 role selectors + 5 action recipes + 7 effects + 6 eligibility rules + 5 trigger surfaces.
- [ ] All ServerRpcs + ClientRpcs implemented; advance-press protocol works for `AllMustPress` / `AnyAdvances` / `TriggerOnly`.
- [ ] `CharacterAction_CinematicMoveTo` works in MP (movement replication validated).
- [ ] `IsCinematicActor` flag gates combat damage, BT execution, player input.
- [ ] `CinematicCameraController` handles `CameraFocusStep` (target → lerp → restore).
- [ ] `DialogueManager.StartDialogue` API preserved + delegates to cinematic; `Trigger Serialized Dialogue` context menu works.
- [ ] `Input.GetKeyDown(Space)` for advance is in `PlayerController`, not `DialogueManager`.
- [ ] Cinematic Scene Editor window functional (drag-reorder, color-coded, validation, Test button).
- [ ] Cinematic Browser editor window functional.
- [ ] `/cinematic` chat command registered.
- [ ] `DevCinematicModule` panel tab + `CharacterInspectorView` Cinematic sub-tab.
- [ ] MP smoke checklist passed: 5 surfaces × 4 player relationships × 3 advance modes (sample matrix).
- [ ] `cinematic-system/SKILL.md` written.
- [ ] `wiki/systems/cinematic.md` written; existing dialogue wiki pages updated.
- [ ] `cinematic-specialist.md` agent created (`model: opus`).

## 16. Open Questions / Future Work

- **NPC chooser logic for `ChoiceStep`** — v1 picks option 0; v2 may use BT/personality-driven utility.
- **Animation override during `MoveActorStep`** — v1 uses default walk. May add `_animationOverride` for "march", "stagger", etc.
- **Letterbox bars for cinematic mode** — defer with the rest of the camera system.
- **Reconnect-mid-scene rebinding** — v1 disconnects auto-yield; rejoiners are non-participants. v2 may add a "rejoin window" if it matters in practice.
- **HUD "scene available" indicator on NPCs** — registry caching is built; rendering layer deferred.
- **Save-during-cinematic** — v1 auto-aborts. v2 may persist active director state if needed.
- **`Effect_CompleteQuest` / `Effect_FailQuest`** — drop in as new SOs when quest pacing requires.
- **In-editor scene-flow visualization** for `ChoiceStep` branching — v1 inline, v2 may add a graph view.

---

## Appendix A — Identity & Save Path Validation

- `Character.CharacterId` (string GUID) is generated on first spawn (existing field on `Character.cs:319`).
- `Character.FindByUUID(string)` exists for resolution (existing API).
- `Worlds/{worldGuid}.json` and `Profiles/{characterGuid}.json` already partition state per the save-load skill.
- `CharacterDataCoordinator` auto-discovers `ICharacterSaveData` on root + children — no explicit registration.
- `HibernatedNPCData.ProfileData` carries the full `CharacterProfileSaveData` for NPCs (per save-load skill 2026-04-23 update) — `CinematicHistorySaveData` rides along automatically.

## Appendix B — Worked Authoring Example

`Cinematic_FirstMeeting`:
1. Create asset in `Assets/Resources/Data/Cinematics/Cinematic_FirstMeeting.asset`.
2. Identity: displayName "First Meeting"; defaults (`AnyPlayer`, `OncePerWorld`, `AllMustPress`, grace 5s, priority 50).
3. Trigger: `Surface_OnInteractionAction` — `_actionType = Talk`, `_initiatorFilter = PlayerOnly`, `_participantsMode = RequireAtLeastOne`, `_participantArchetypes = [WilfredArchetype]`.
4. Eligibility: `Rule_RelationTierAtLeast(2)`.
5. Cast:
   - `Hero` → `Selector_TriggeringPlayer`. Required.
   - `Wilfred` → `Selector_OtherParticipant`. Required. Primary actor.
6. Timeline:
   - Step 0 — `SpeakStep`: speaker `Wilfred`, text `"Ah, traveler from afar..."`.
   - Step 1 — `ParallelStep`:
     - `MoveActorStep`: actor `Hero`, target `Wilfred`, blocking, stoppingDist 1.5.
     - `SpeakStep`: speaker `Wilfred`, text `"Come closer, I have something for you."`.
   - Step 2 — `TriggerStep`: `Effect_GiveQuest(targetRole=Hero, _questDefinition=Quest_FindTheElder)`.
   - Step 3 — `SpeakStep`: speaker `Wilfred`, text `"Find the elder of [index1].getName."` *(placeholder resolved at runtime to "Hero")*.
7. Validate → all green.
8. ▶ Test → enters playmode and triggers with current scene's first player as Hero.

## Appendix C — Decisions Log

| Date | Decision | Rationale |
|------|----------|-----------|
| 2026-04-30 | Trigger surface set v1 = 5 (drop `Surface_OnQuestEvent`, fold into `Scripted`) | Quest scripts already use code hooks; parallel routing logic in registry is unnecessary. |
| 2026-04-30 | `CharacterUUID` struct dropped; use existing `Character.CharacterId` | Already globally unique (`NetworkCharacterId.Value.ToString()`); world scoping is at save-file level (`Worlds/{worldGuid}.json`), not at character ID level. |
| 2026-04-30 | `_npcOverrides` registry dictionary dropped; consolidated on per-character `_pendingSceneIds` | Single source of truth for "this character has this scene"; per-character API matches user's original imperative request. |
| 2026-04-30 | `_playedOncePerNpc` and `_playedOncePerPlayer` dropped from world state; derived from per-character `_playedSceneIds` | Single source of truth; no duplicate state to keep in sync. |
| 2026-04-30 | Bidirectional matching for `OnInteractionAction` is set-semantics on participant lists | Order-free → A→B and B→A both fire automatically when participant set matches. |
| 2026-04-30 | `Selector_OtherParticipant` (not `Selector_TalkTarget`) | Resolves correctly regardless of who initiated in bidirectional matching. |
| 2026-04-30 | `_advanceMode` default = `AllMustPress` + 5s grace timer | Self-paced reading for all participants; AFK partner doesn't block scene; matches user's explicit pick. |
| 2026-04-30 | `_playMode` default = `OncePerWorld` | Matches user's explicit pick. New world = new game = scene re-triggerable. |
| 2026-04-30 | Camera scope v1 = `CameraFocusStep` only | "Long-term modular" — `ICinematicCameraController` interface defined now, full system slots in v2 without scene-data migration. |
| 2026-04-30 | Authoring UX = custom Editor window (not plain Inspector) | "Long-term modular" — flat Inspector arrays don't scale to 30-step hierarchical scenes. |
