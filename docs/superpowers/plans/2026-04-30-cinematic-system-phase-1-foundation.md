# Cinematic System — Phase 1: Runtime Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the server-side runtime foundation of the Cinematic / Scripted Scene system: a polymorphic step model, a single-player director that iterates steps end-to-end, four step types (Speak / Move / Wait / Trigger), the per-character `CharacterCinematicState` subsystem with a local `IsCinematicActor` flag, and a public `Cinematics.TryPlay()` facade. By the end of this phase, a designer can hand-author a `CinematicSceneSO`, trigger it from a debug button, and watch a multi-actor scripted scene play out in single-player.

**Architecture:** `CinematicSceneSO` (ScriptableObject) holds a `[SerializeReference] List<CinematicStep>` plus role definitions. `CinematicDirector` (plain `MonoBehaviour` for Phase 1; promoted to `NetworkBehaviour` in Phase 2) is spawned on demand, resolves roles, and iterates steps via `ICinematicStep.OnEnter / OnTick / IsComplete / OnExit`. `MoveActorStep` enqueues a new `CharacterAction_CinematicMoveTo` on the actor's `Character.CharacterActions` lane (rule #22 — gameplay through actions). `CharacterCinematicState` is a `CharacterSystem` subsystem on a child GameObject under each Character (rule #9 hierarchy); its `IsCinematicActor` field gates `CharacterCombat` damage, BT execution, and `PlayerController` input. Phase 1 is **server-only / single-player** — Phase 2 layers on `NetworkVariable`/RPCs.

**Tech Stack:** Unity 2022+, C#, Newtonsoft.Json (existing), `[SerializeReference]` for polymorphic step lists, ScriptableObjects for designer-authored scenes/roles/effects, existing `CharacterSpeech.SayScripted` + `CharacterMovement.SetDestination` for visual primitives.

**Spec:** [docs/superpowers/specs/2026-04-30-cinematic-system-design.md](../specs/2026-04-30-cinematic-system-design.md)

**Testing approach:** No automated test suite exists in this Unity project. Each task ends with manual verification in Play mode. `Debug.Log` statements at branching points are required (project rule #27). EditMode test stubs are added in Phase 2 once the surface stabilizes.

**World scale reminder (rule #32):** 11 Unity units = 1.67 m. `MoveActorStep._stoppingDist` defaults to 1.5 Unity units (~0.23 m) — close enough to feel "next to" the target without clipping.

**Phase 1 scope explicitly excludes:**

- `NetworkVariable` / `ServerRpc` / `ClientRpc` (deferred to Phase 2).
- `ChoiceStep`, `ParallelStep`, `CameraFocusStep`, `ExecuteActionStep` (deferred to Phase 3).
- All trigger surfaces except code-driven `Cinematics.TryPlay(...)` (deferred to Phase 2).
- All eligibility rules and PlayMode bookkeeping (deferred to Phase 2).
- All persistence (`ISaveable` / `ICharacterSaveData<T>`) (deferred to Phase 2).
- Editor windows / browser / validation panel (deferred to Phase 4).
- `DialogueManager` migration (deferred to Phase 4).
- All action recipes, effects beyond `Effect_RaiseEvent`, eligibility rules (deferred to Phase 3).
- Camera focus (`ICinematicCameraController`, `Effect_PlayVFX/SFX/Animation/GiveQuest/...`).
- **AI BT yield gate** on `IsCinematicActor` (deferred to Phase 2). Phase 1 only binds player avatars as actors via `Selector_TriggeringPlayer`. The player's BT is not active (PlayerController controls them, and PlayerController IS gated in Task 15). NPCs bound as actors would need a BT yield — that requires the project's BT framework integration which lands with the Talk surface in Phase 2 alongside `Selector_OtherParticipant`.
- **`Selector_OtherParticipant`** (deferred to Phase 2). Phase 1 ships only `Selector_TriggeringPlayer` because that's the only role-binding rule needed to validate the step iteration model.

These exclusions keep Phase 1 to a single-player, server-only foundation that proves the step iteration model works end-to-end before MP / persistence / editor tooling are layered on. **Phase 1 demoability**: a designer triggers a single-actor scene (Hero only) and watches it play out. Multi-actor scenes start working in Phase 2.

---

## File Structure

### Files created (Phase 1)

```
Assets/Scripts/Cinematics/
├── Cinematics.asmdef                            — assembly definition
├── Core/
│   ├── Enums.cs                                  — TriggerAuthority, PlayMode, AdvanceMode, InitiatorFilter, ParticipantsMode, CompletionMode, CinematicEndReason
│   ├── ActorRoleId.cs                            — readonly struct value type
│   ├── CinematicSceneSO.cs                       — top-level ScriptableObject
│   ├── CinematicContext.cs                       — runtime context threaded through steps
│   ├── ICinematicStep.cs                         — interface + CinematicStep abstract base
│   ├── CinematicDirector.cs                      — MonoBehaviour, runs the timeline (Phase 1 = local-only)
│   └── Cinematics.cs                             — public static facade (TryPlay)
├── Roles/
│   ├── RoleSlot.cs                               — Serializable struct
│   ├── RoleSelectorSO.cs                         — abstract ScriptableObject
│   └── Selector_TriggeringPlayer.cs              — concrete, returns ctx.TriggeringPlayer
├── Steps/
│   ├── SpeakStep.cs
│   ├── WaitStep.cs
│   ├── MoveActorStep.cs
│   └── TriggerStep.cs
├── Effects/
│   ├── CinematicEffectSO.cs                      — abstract ScriptableObject
│   └── Effect_RaiseEvent.cs                      — UnityEvent escape hatch (only effect needed for Phase 1)
└── Actions/
    └── CharacterAction_CinematicMoveTo.cs        — CharacterAction subclass

Assets/Scripts/Character/CharacterCinematicState/
└── CharacterCinematicState.cs                    — CharacterSystem subsystem (no NetworkVariable yet)
```

### Files modified (Phase 1)

| File | Change |
|------|--------|
| `Assets/Scripts/Character/Character.cs` | Add `[SerializeField] _cinematicState : CharacterCinematicState` field + property + `Awake()` `GetComponentInChildren<>()` fallback. |
| `Assets/Scripts/Character/CharacterCombat/CharacterCombat.cs` | Damage application checks `target.CharacterCinematicState.IsCinematicActor` → skip damage if true. |
| `Assets/Scripts/Character/CharacterControllers/PlayerController.cs` | Block movement command queueing + combat hotkeys when `IsCinematicActor` is true. |
| `Assets/Scripts/DebugScript.cs` | Add a "Trigger Test Cinematic" button that loads `Resources/Data/Cinematics/Test_FirstMeeting` and calls `Cinematics.TryPlay`. |

### Designer-authored test asset (Phase 1 verification)

```
Assets/Resources/Data/Cinematics/
└── Test_FirstMeeting.asset                        — hand-authored CinematicSceneSO for Phase 1 verification
```

---

## Phase 1A — Foundation Types

### Task 1: Create folder structure + assembly definition

**Files:**
- Create: `Assets/Scripts/Cinematics/Cinematics.asmdef`
- Create folders: `Assets/Scripts/Cinematics/Core/`, `Roles/`, `Steps/`, `Effects/`, `Actions/`
- Create folder: `Assets/Scripts/Character/CharacterCinematicState/`
- Create folder: `Assets/Resources/Data/Cinematics/`

- [ ] **Step 1.1: Create the folder hierarchy**

In Unity Editor, right-click the relevant parent folders and choose `Create → Folder` to create:
- `Assets/Scripts/Cinematics/`
- `Assets/Scripts/Cinematics/Core/`
- `Assets/Scripts/Cinematics/Roles/`
- `Assets/Scripts/Cinematics/Steps/`
- `Assets/Scripts/Cinematics/Effects/`
- `Assets/Scripts/Cinematics/Actions/`
- `Assets/Scripts/Character/CharacterCinematicState/`
- `Assets/Resources/Data/Cinematics/`

- [ ] **Step 1.2: Create `Cinematics.asmdef`**

In `Assets/Scripts/Cinematics/`, right-click → `Create → Assembly Definition`. Name it `Cinematics`.

Contents (set via Inspector or edit the `.asmdef` JSON directly):

```json
{
    "name": "Cinematics",
    "rootNamespace": "MWI.Cinematics",
    "references": [
        "GUID:<character-asmdef-guid>",
        "GUID:<unity-netcode-guid>",
        "GUID:<unity-events-guid>"
    ],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

If the project does not currently use `.asmdef` files (check by searching `Assets/Scripts/` for any `.asmdef` files via Grep), skip this step entirely and let the Cinematics scripts compile into the project's default assembly. The cinematic system has no need for assembly isolation in Phase 1.

- [ ] **Step 1.3: Verify Unity recompiles cleanly**

Force a recompile via `Assets → Refresh` (or Ctrl+R). Watch the Console — should be **zero new compile errors** (no scripts exist yet, so the only errors should be pre-existing ones unrelated to this work).

- [ ] **Step 1.4: Commit**

```bash
git add Assets/Scripts/Cinematics/.gitkeep \
        Assets/Scripts/Character/CharacterCinematicState/.gitkeep \
        Assets/Resources/Data/Cinematics/.gitkeep
git commit -m "chore(cinematics): scaffold folder structure for Phase 1"
```

(Unity creates `.meta` files for empty folders only when at least one file lives in them — placeholder `.gitkeep` files are not strictly required if your team prefers letting Unity track folders implicitly. Drop them if your convention is no-`.gitkeep`.)

---

### Task 2: Define enums + `ActorRoleId`

**Files:**
- Create: `Assets/Scripts/Cinematics/Core/Enums.cs`
- Create: `Assets/Scripts/Cinematics/Core/ActorRoleId.cs`

- [ ] **Step 2.1: Create `Enums.cs`**

```csharp
namespace MWI.Cinematics
{
    public enum TriggerAuthority { AnyPlayer, HostOnly }

    public enum PlayMode         { OncePerWorld, OncePerPlayer, OncePerNpc, Repeatable }

    public enum AdvanceMode      { AllMustPress, AnyAdvances, TriggerOnly }

    public enum InitiatorFilter  { AnyCharacter, PlayerOnly }

    public enum ParticipantsMode { Anyone, RequireAtLeastOne, RestrictedToSet }

    public enum CompletionMode   { AllComplete, AnyComplete, FirstComplete }

    public enum CinematicEndReason
    {
        Completed,
        Aborted,
        ActorLost,
        AllPlayersDisconnected
    }
}
```

These enums match the spec §5.2. Phase 1 only uses `TriggerAuthority`, `PlayMode`, `CinematicEndReason` — the rest are scaffolded so Phase 2/3 can drop in without touching Phase 1 code.

- [ ] **Step 2.2: Create `ActorRoleId.cs`**

```csharp
using System;

namespace MWI.Cinematics
{
    /// <summary>
    /// Typed wrapper around a string role identifier. Prevents stringly-typed bugs
    /// when wiring step.<paramref name="_actor"/> fields to RoleSlot.RoleId.
    /// </summary>
    [Serializable]
    public readonly struct ActorRoleId : IEquatable<ActorRoleId>
    {
        public readonly string Value;

        public ActorRoleId(string value) { Value = value; }

        public static readonly ActorRoleId Empty = new ActorRoleId(string.Empty);

        public bool IsEmpty => string.IsNullOrEmpty(Value);

        public bool Equals(ActorRoleId other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is ActorRoleId o && Equals(o);

        public override int GetHashCode() =>
            Value != null ? StringComparer.Ordinal.GetHashCode(Value) : 0;

        public override string ToString() => Value ?? string.Empty;

        public static bool operator ==(ActorRoleId a, ActorRoleId b) => a.Equals(b);
        public static bool operator !=(ActorRoleId a, ActorRoleId b) => !a.Equals(b);
    }
}
```

- [ ] **Step 2.3: Verify compile**

Force `Assets → Refresh`. Console should be clean.

- [ ] **Step 2.4: Commit**

```bash
git add Assets/Scripts/Cinematics/Core/Enums.cs \
        Assets/Scripts/Cinematics/Core/ActorRoleId.cs
git commit -m "feat(cinematics): add foundational enums + ActorRoleId value type"
```

---

### Task 3: Create `ICinematicStep` interface + `CinematicStep` abstract base

**Files:**
- Create: `Assets/Scripts/Cinematics/Core/ICinematicStep.cs`

- [ ] **Step 3.1: Create the file**

```csharp
using System;
using UnityEngine;

namespace MWI.Cinematics
{
    /// <summary>
    /// Polymorphic step contract iterated by CinematicDirector.
    /// The director never branches on concrete step type — extension is one new subclass.
    /// </summary>
    public interface ICinematicStep
    {
        /// <summary>Called once when the step becomes active.</summary>
        void OnEnter(CinematicContext ctx);

        /// <summary>Called every director tick while IsComplete returns false.</summary>
        void OnTick(CinematicContext ctx, float dt);

        /// <summary>Called once when the step completes or is aborted.</summary>
        void OnExit(CinematicContext ctx);

        /// <summary>Director polls this; advances when true.</summary>
        bool IsComplete(CinematicContext ctx);
    }

    /// <summary>
    /// Default abstract base. Subclasses override only what they need.
    /// Default IsComplete returns true (instant step) — override for stateful steps.
    /// </summary>
    [Serializable]
    public abstract class CinematicStep : ICinematicStep
    {
        [SerializeField] protected string _label;     // editor display label
        public string Label => string.IsNullOrEmpty(_label) ? GetType().Name : _label;

        public virtual void OnEnter(CinematicContext ctx) { }
        public virtual void OnTick (CinematicContext ctx, float dt) { }
        public virtual void OnExit (CinematicContext ctx) { }
        public virtual bool IsComplete(CinematicContext ctx) => true;
    }
}
```

The class is `[Serializable]` so `[SerializeReference]` on `CinematicSceneSO._steps` will surface concrete subclasses in the Inspector dropdown.

- [ ] **Step 3.2: Verify compile**

`Assets → Refresh`. The compile will fail with `CinematicContext not found` — that's expected. Move to Task 4 to resolve.

---

### Task 4: Create `CinematicContext`

**Files:**
- Create: `Assets/Scripts/Cinematics/Core/CinematicContext.cs`

- [ ] **Step 4.1: Create the file**

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace MWI.Cinematics
{
    /// <summary>
    /// Runtime context threaded through every step callback. Server-side only.
    /// Steps read from this; they should not mutate it post-OnEnter.
    /// </summary>
    public class CinematicContext
    {
        public CinematicSceneSO    Scene             { get; internal set; }
        public CinematicDirector   Director          { get; internal set; }
        public Character           TriggeringPlayer  { get; internal set; }
        public Character           OtherParticipant  { get; internal set; }   // null for surfaces with no second party
        public Vector3             TriggerOrigin     { get; internal set; }
        public float               StartTimeSim      { get; internal set; }

        // Mutable during scene start (role resolution); read-only after
        public Dictionary<ActorRoleId, Character> BoundRoles    { get; } = new();
        public Dictionary<string, GameObject>     BoundObjects  { get; } = new();
        public List<Character>                    ParticipatingPlayers { get; } = new();

        /// <summary>
        /// Resolve a role to its bound Character.
        /// Throws if the role is required and missing; returns null if optional and missing.
        /// </summary>
        public Character GetActor(ActorRoleId id)
        {
            if (BoundRoles.TryGetValue(id, out var c)) return c;

            // Look up role definition to know if optional
            if (Scene != null)
            {
                foreach (var slot in Scene.Roles)
                {
                    if (slot.RoleId == id)
                    {
                        if (slot.IsOptional) return null;
                        Debug.LogError($"<color=red>[Cinematic]</color> Required role '{id}' is unbound on scene '{Scene.SceneId}'.");
                        return null;
                    }
                }
            }

            Debug.LogWarning($"<color=yellow>[Cinematic]</color> Role '{id}' is not declared on scene '{Scene?.SceneId}'.");
            return null;
        }

        public GameObject GetObject(string key) =>
            BoundObjects.TryGetValue(key, out var go) ? go : null;
    }
}
```

`Scene` and `Director` use `internal set` so the director can populate them at scene start without exposing mutability publicly. (If your project's assembly layout doesn't make `internal` work between Cinematics + Director, drop the `internal` and add a public `Initialize(...)` method. With everything in the default assembly, `internal` works.)

- [ ] **Step 4.2: Verify compile**

Compile will still fail with `CinematicSceneSO not found` and `CinematicDirector not found`. That's expected — they arrive in Task 5 / 14.

---

### Task 5: Create `RoleSlot` + `RoleSelectorSO` + `Selector_TriggeringPlayer`

**Files:**
- Create: `Assets/Scripts/Cinematics/Roles/RoleSlot.cs`
- Create: `Assets/Scripts/Cinematics/Roles/RoleSelectorSO.cs`
- Create: `Assets/Scripts/Cinematics/Roles/Selector_TriggeringPlayer.cs`

- [ ] **Step 5.1: `RoleSlot.cs`**

```csharp
using System;
using UnityEngine;

namespace MWI.Cinematics
{
    [Serializable]
    public struct RoleSlot
    {
        [SerializeField] private string _roleId;
        [SerializeField] private string _displayName;
        [SerializeField] private RoleSelectorSO _selector;
        [SerializeField] private bool _isOptional;
        [SerializeField] private bool _isPrimaryActor;   // for OncePerNpc keying — Phase 2

        public ActorRoleId    RoleId         => new ActorRoleId(_roleId);
        public string         DisplayName    => string.IsNullOrEmpty(_displayName) ? _roleId : _displayName;
        public RoleSelectorSO Selector       => _selector;
        public bool           IsOptional     => _isOptional;
        public bool           IsPrimaryActor => _isPrimaryActor;
    }
}
```

- [ ] **Step 5.2: `RoleSelectorSO.cs`**

```csharp
using UnityEngine;

namespace MWI.Cinematics
{
    /// <summary>
    /// Polymorphic SO that resolves an abstract role to a live Character at scene start.
    /// New selection rules drop in as new subclasses — no central switch.
    /// </summary>
    public abstract class RoleSelectorSO : ScriptableObject
    {
        /// <summary>
        /// Returns the Character that fills this role for the given context.
        /// Returns null if no character could be bound (caller decides hard-fail vs skip).
        /// </summary>
        public abstract Character Resolve(CinematicContext ctx);
    }
}
```

- [ ] **Step 5.3: `Selector_TriggeringPlayer.cs`**

```csharp
using UnityEngine;

namespace MWI.Cinematics
{
    [CreateAssetMenu(
        fileName = "Selector_TriggeringPlayer",
        menuName = "MWI/Cinematics/Selectors/Triggering Player")]
    public class Selector_TriggeringPlayer : RoleSelectorSO
    {
        public override Character Resolve(CinematicContext ctx) => ctx.TriggeringPlayer;
    }
}
```

- [ ] **Step 5.4: Verify compile**

Compile will fail (still no `CinematicSceneSO`). Park.

- [ ] **Step 5.5: Create the asset**

In `Assets/Resources/Data/Cinematics/`, right-click → `Create → MWI → Cinematics → Selectors → Triggering Player`. Save as `Selector_TriggeringPlayer.asset`. (We'll reuse this asset across all test scenes.)

This step is deferred to Task 17 if Unity refuses the asset creation while compile errors exist. Note in your TODOs and continue.

---

### Task 6: Create `CinematicSceneSO`

**Files:**
- Create: `Assets/Scripts/Cinematics/Core/CinematicSceneSO.cs`

- [ ] **Step 6.1: Create the file**

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace MWI.Cinematics
{
    [CreateAssetMenu(
        fileName = "NewCinematicScene",
        menuName = "MWI/Cinematics/Scene")]
    public class CinematicSceneSO : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string _sceneId = System.Guid.NewGuid().ToString("N");
        [SerializeField] private string _displayName;

        [Header("Triggering (Phase 2 — wired in by registry; Phase 1 ignores)")]
        [SerializeField] private TriggerAuthority _triggerAuthority = TriggerAuthority.AnyPlayer;
        [SerializeField] private int _priority = 50;

        [Header("Lifecycle (Phase 2 — registry consults these)")]
        [SerializeField] private PlayMode _playMode = PlayMode.OncePerWorld;
        [SerializeField] private AdvanceMode _advanceMode = AdvanceMode.AllMustPress;
        [SerializeField] private float _advanceGraceSec = 5f;

        [Header("Cast")]
        [SerializeField] private List<RoleSlot> _roles = new();

        [Header("Timeline")]
        [SerializeReference] private List<CinematicStep> _steps = new();

        public string  SceneId          => _sceneId;
        public string  DisplayName      => string.IsNullOrEmpty(_displayName) ? name : _displayName;
        public TriggerAuthority TriggerAuthority => _triggerAuthority;
        public int     Priority         => _priority;
        public PlayMode  PlayMode       => _playMode;
        public AdvanceMode AdvanceMode  => _advanceMode;
        public float   AdvanceGraceSec  => _advanceGraceSec;
        public IReadOnlyList<RoleSlot>  Roles => _roles;
        public IReadOnlyList<CinematicStep> Steps => _steps;

        // Editor-only safety: re-seed sceneId if duplicated via Ctrl+D
        private void OnValidate()
        {
            if (string.IsNullOrEmpty(_sceneId))
                _sceneId = System.Guid.NewGuid().ToString("N");
        }
    }
}
```

The `_sceneId` defaults to a fresh GUID on creation. `OnValidate()` re-seeds it if it ends up empty (e.g., a designer accidentally cleared the field). Phase 2 will add a more sophisticated duplicate-detection check.

- [ ] **Step 6.2: Verify compile**

`Assets → Refresh`. **All previous compile errors should resolve.** If anything still fails, fix before proceeding — you can't author scene assets while the SO type itself doesn't compile.

- [ ] **Step 6.3: Commit Tasks 3–6 together**

```bash
git add Assets/Scripts/Cinematics/Core/ICinematicStep.cs \
        Assets/Scripts/Cinematics/Core/CinematicContext.cs \
        Assets/Scripts/Cinematics/Core/CinematicSceneSO.cs \
        Assets/Scripts/Cinematics/Roles/
git commit -m "feat(cinematics): scene SO + step contract + role binding scaffolding"
```

---

## Phase 1B — Step Implementations

### Task 7: `WaitStep`

The simplest step — pure timer. Write this first to validate the step lifecycle works end-to-end before tackling steps that touch external systems.

**Files:**
- Create: `Assets/Scripts/Cinematics/Steps/WaitStep.cs`

- [ ] **Step 7.1: Create the file**

```csharp
using UnityEngine;

namespace MWI.Cinematics
{
    [System.Serializable]
    public class WaitStep : CinematicStep
    {
        [SerializeField] private float _durationSec = 1f;

        private float _endTimeSim;

        public override void OnEnter(CinematicContext ctx)
        {
            _endTimeSim = Time.time + Mathf.Max(0f, _durationSec);
            Debug.Log($"<color=cyan>[Cinematic]</color> WaitStep entered — will complete at sim time {_endTimeSim:F2} (duration {_durationSec:F2}s).");
        }

        public override bool IsComplete(CinematicContext ctx) => Time.time >= _endTimeSim;
    }
}
```

`Time.time` is sim time when `Time.timeScale` is the only multiplier. The project's `GameSpeedController` typically modifies `Time.timeScale`, so `Time.time` automatically scales — matches rule #26's "simulation time" requirement for cinematics. (If the project uses a custom sim clock, swap to that; the spec §6.3 says "use simulation time" without specifying the API. Verify in the project before merging.)

- [ ] **Step 7.2: Verify compile**

`Assets → Refresh`. Console should be clean.

- [ ] **Step 7.3: Commit**

```bash
git add Assets/Scripts/Cinematics/Steps/WaitStep.cs
git commit -m "feat(cinematics): WaitStep — sim-time delay primitive"
```

---

### Task 8: `SpeakStep` (uses existing `CharacterSpeech.SayScripted`)

**Files:**
- Create: `Assets/Scripts/Cinematics/Steps/SpeakStep.cs`

- [ ] **Step 8.1: Pre-check — verify the legacy bridge target API**

Run Grep for `SayScripted` across `Assets/Scripts/Character/CharacterSpeech/`:

Expected hit: `CharacterSpeech.SayScripted(string text, float typingSpeedOverride, System.Action onTypingDone)` (signature confirmed during brainstorming via [DialogueManager.cs:142](Assets/Scripts/Dialogue/DialogueManager.cs#L142)).

If the signature differs, adjust the step's `OnEnter` accordingly. **STOP and update this task** if the signature is materially different.

- [ ] **Step 8.2: Create `SpeakStep.cs`**

```csharp
using UnityEngine;

namespace MWI.Cinematics
{
    [System.Serializable]
    public class SpeakStep : CinematicStep
    {
        [SerializeField] private string _speakerRoleId;
        [TextArea(2, 8)]
        [SerializeField] private string _lineText;
        [SerializeField] private float _typingSpeedOverride = 0f;     // 0 = use default

        private bool _typingDone;
        private bool _advanceRequested;

        public ActorRoleId SpeakerRoleId => new ActorRoleId(_speakerRoleId);
        public string      LineText      => _lineText;

        public override void OnEnter(CinematicContext ctx)
        {
            _typingDone = false;
            _advanceRequested = false;

            var speaker = ctx.GetActor(SpeakerRoleId);
            if (speaker == null)
            {
                Debug.LogError($"<color=red>[Cinematic]</color> SpeakStep: speaker role '{_speakerRoleId}' could not be resolved on scene '{ctx.Scene?.SceneId}'.");
                _typingDone = true;
                _advanceRequested = true;     // skip this step
                return;
            }

            if (speaker.CharacterSpeech == null)
            {
                Debug.LogError($"<color=red>[Cinematic]</color> SpeakStep: speaker '{speaker.CharacterName}' has no CharacterSpeech component.");
                _typingDone = true;
                _advanceRequested = true;
                return;
            }

            // Close any other open bubble on the speaker before opening a new one
            speaker.CharacterSpeech.CloseSpeech();

            string processedText = ResolvePlaceholders(_lineText, ctx);

            Debug.Log($"<color=cyan>[Cinematic]</color> SpeakStep: '{speaker.CharacterName}' says \"{processedText}\".");

            speaker.CharacterSpeech.SayScripted(
                processedText,
                _typingSpeedOverride,
                onTypingDone: () =>
                {
                    _typingDone = true;
                    Debug.Log($"<color=cyan>[Cinematic]</color> SpeakStep: typing finished for '{speaker.CharacterName}'.");
                });
        }

        public override void OnExit(CinematicContext ctx)
        {
            // Close bubble after the step ends so the next step's bubble opens clean
            var speaker = ctx.GetActor(SpeakerRoleId);
            speaker?.CharacterSpeech?.CloseSpeech();
        }

        public override bool IsComplete(CinematicContext ctx)
        {
            // Phase 1 — auto-advance 1.5s after typing finishes (placeholder until Phase 2 advance protocol).
            // We use the same 1.5s default that DialogueManager uses for NPC-only dialogues.
            if (!_typingDone) return false;
            if (!_advanceRequested)
            {
                _advanceRequested = true;
                _advanceTimerEnd = Time.time + 1.5f;
            }
            return Time.time >= _advanceTimerEnd;
        }

        private float _advanceTimerEnd;

        private string ResolvePlaceholders(string text, CinematicContext ctx)
        {
            if (string.IsNullOrEmpty(text)) return text;

            // [role:Hero].getName  → replaces with the Hero role's display name
            // Simple replace loop — Phase 4 may upgrade to a regex-based formatter if more tags arrive
            string result = text;
            foreach (var slot in ctx.Scene.Roles)
            {
                string token = $"[role:{slot.RoleId}].getName";
                if (result.Contains(token))
                {
                    var c = ctx.GetActor(slot.RoleId);
                    string nameToInsert = c != null ? c.CharacterName : slot.DisplayName;
                    result = result.Replace(token, nameToInsert);
                }
            }
            return result;
        }
    }
}
```

**Note on advance behaviour:** Phase 1 auto-advances 1.5s after typing finishes — it does NOT wait for player input. This matches the spec's §3 decision #11 only after Phase 2 layers in the `AllMustPress` advance-press protocol. For Phase 1 demo purposes, auto-advance is what we want (no MP networking yet).

- [ ] **Step 8.3: Verify compile**

`Assets → Refresh`. Console should be clean.

- [ ] **Step 8.4: Commit**

```bash
git add Assets/Scripts/Cinematics/Steps/SpeakStep.cs
git commit -m "feat(cinematics): SpeakStep — wraps existing CharacterSpeech.SayScripted with placeholder resolution"
```

---

### Task 9: `CharacterAction_CinematicMoveTo`

**Files:**
- Create: `Assets/Scripts/Cinematics/Actions/CharacterAction_CinematicMoveTo.cs`

- [ ] **Step 9.1: Pre-check — confirm `CharacterAction` constructor + `CharacterMovement` API**

Run Grep for `class CharacterAction` and `class CharacterMovement` across `Assets/Scripts/Character/`:

Expected hits (confirmed during brainstorming via [CharacterAction.cs](Assets/Scripts/Character/CharacterActions/CharacterAction.cs) and [CharacterMovement.cs:288-294](Assets/Scripts/Character/CharacterMovement/CharacterMovement.cs#L288)):
- `CharacterAction` abstract base with `protected CharacterAction(Character character, float duration = 0f)` constructor.
- `CharacterMovement.SetDestination(Vector3 target)` and `CharacterMovement.Stop()`.
- `Character.CharacterActions.Enqueue(...)` lane (verify exact method name on `CharacterActions`).

If `CharacterActions.Enqueue` is named differently (e.g. `Queue`, `Add`, `EnqueueAction`), update Task 12's `OnEnter` accordingly.

- [ ] **Step 9.2: Create the file**

```csharp
using System.Collections;
using UnityEngine;

namespace MWI.Cinematics
{
    /// <summary>
    /// Walk an actor to a world position. Used by MoveActorStep.
    /// Routes through the standard CharacterAction lane so player and NPC parity (rule #22)
    /// is preserved — combat actions, animation hooks, OnCancel cleanup all work normally.
    /// </summary>
    public class CharacterAction_CinematicMoveTo : CharacterAction
    {
        private readonly Vector3 _target;
        private readonly float   _stoppingDist;
        private readonly float   _timeoutSec;

        private Coroutine _watchCoroutine;

        public override bool AllowsMovementDuringAction => true;
        public override bool ShouldPlayGenericActionAnimation => false;
        public override string ActionName => "Cinematic Move";

        public CharacterAction_CinematicMoveTo(
            Character actor,
            Vector3 target,
            float stoppingDist = 1.5f,
            float timeoutSec   = 30f)
            : base(actor, duration: timeoutSec)
        {
            _target       = target;
            _stoppingDist = Mathf.Max(0.1f, stoppingDist);
            _timeoutSec   = Mathf.Max(1f, timeoutSec);
        }

        public override void OnStart()
        {
            if (character == null || character.CharacterMovement == null)
            {
                Debug.LogError($"<color=red>[Cinematic]</color> CharacterAction_CinematicMoveTo: character or CharacterMovement is null. Finishing immediately.");
                Finish();
                return;
            }

            Debug.Log($"<color=cyan>[Cinematic]</color> CharacterAction_CinematicMoveTo: '{character.CharacterName}' moving to {_target} (stoppingDist={_stoppingDist}, timeout={_timeoutSec}s).");

            character.CharacterMovement.SetDestination(_target);
            _watchCoroutine = character.StartCoroutine(WatchArrival());
        }

        public override void OnApplyEffect()
        {
            // No discrete effect — movement is the action; arrival fires Finish() via the watch coroutine.
        }

        public override void OnCancel()
        {
            if (_watchCoroutine != null && character != null)
            {
                character.StopCoroutine(_watchCoroutine);
                _watchCoroutine = null;
            }
            character?.CharacterMovement?.Stop();
        }

        private IEnumerator WatchArrival()
        {
            float elapsed = 0f;
            while (elapsed < _timeoutSec)
            {
                if (character == null) yield break;

                float dist = Vector3.Distance(character.transform.position, _target);
                if (dist <= _stoppingDist)
                {
                    Debug.Log($"<color=cyan>[Cinematic]</color> CharacterAction_CinematicMoveTo: '{character.CharacterName}' arrived (dist {dist:F2} ≤ {_stoppingDist:F2}).");
                    Finish();
                    yield break;
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

            Debug.LogWarning($"<color=yellow>[Cinematic]</color> CharacterAction_CinematicMoveTo: '{character?.CharacterName}' timed out after {_timeoutSec}s. Finishing anyway.");
            Finish();
        }
    }
}
```

The action holds a coroutine that polls distance to target. When inside `_stoppingDist`, it calls `Finish()` (which fires `OnActionFinished`). On timeout, it finishes anyway so the scene doesn't stall forever.

- [ ] **Step 9.3: Verify compile**

`Assets → Refresh`. Should compile cleanly. If `Character.StartCoroutine` isn't available because `Character` isn't a `MonoBehaviour` (verify), fall back to `CoroutineRunner.Instance.StartCoroutine(...)` or whatever singleton the project uses for coroutines outside MonoBehaviours. Check by Grep'ing `class Character` — `Character.cs` extends `NetworkBehaviour` (which extends `MonoBehaviour`), so `StartCoroutine` is available.

- [ ] **Step 9.4: Commit**

```bash
git add Assets/Scripts/Cinematics/Actions/CharacterAction_CinematicMoveTo.cs
git commit -m "feat(cinematics): CharacterAction_CinematicMoveTo — routes scene movement through CharacterAction lane (rule #22)"
```

---

### Task 10: `MoveActorStep`

**Files:**
- Create: `Assets/Scripts/Cinematics/Steps/MoveActorStep.cs`

- [ ] **Step 10.1: Pre-check — confirm `Character.CharacterActions.Enqueue` API**

Run Grep for `CharacterActions` and `Enqueue` together. Expected: `CharacterActions.cs` exposes a method (e.g. `EnqueueAction`, `Enqueue`, or `QueueAction`) that takes a `CharacterAction`. Note the exact name and signature; we'll use it below as `actor.CharacterActions.Enqueue(...)`. If the project uses a different name, **substitute it consistently in Tasks 10, 12, and any future ExecuteActionStep work**.

- [ ] **Step 10.2: Create the file**

```csharp
using UnityEngine;

namespace MWI.Cinematics
{
    [System.Serializable]
    public class MoveActorStep : CinematicStep
    {
        public enum TargetMode { Role, WorldPos }

        [SerializeField] private string _actorRoleId;
        [SerializeField] private TargetMode _targetMode = TargetMode.Role;
        [SerializeField] private string _targetRoleId;
        [SerializeField] private Vector3 _targetPos;
        [SerializeField] private float _stoppingDist = 1.5f;
        [SerializeField] private bool  _blocking = true;
        [SerializeField] private float _timeoutSec = 30f;

        private CharacterAction_CinematicMoveTo _action;
        private bool _actionFinished;
        private bool _instantComplete;

        public override void OnEnter(CinematicContext ctx)
        {
            _actionFinished = false;
            _instantComplete = false;

            var actor = ctx.GetActor(new ActorRoleId(_actorRoleId));
            if (actor == null)
            {
                Debug.LogError($"<color=red>[Cinematic]</color> MoveActorStep: actor role '{_actorRoleId}' could not be resolved.");
                _instantComplete = true;
                return;
            }

            Vector3 target;
            switch (_targetMode)
            {
                case TargetMode.Role:
                    var targetActor = ctx.GetActor(new ActorRoleId(_targetRoleId));
                    if (targetActor == null)
                    {
                        Debug.LogError($"<color=red>[Cinematic]</color> MoveActorStep: target role '{_targetRoleId}' could not be resolved.");
                        _instantComplete = true;
                        return;
                    }
                    target = targetActor.transform.position;
                    break;
                case TargetMode.WorldPos:
                    target = _targetPos;
                    break;
                default:
                    Debug.LogError($"<color=red>[Cinematic]</color> MoveActorStep: unknown target mode {_targetMode}.");
                    _instantComplete = true;
                    return;
            }

            _action = new CharacterAction_CinematicMoveTo(
                actor, target, _stoppingDist, _timeoutSec);

            _action.OnActionFinished = () =>
            {
                _actionFinished = true;
                Debug.Log($"<color=cyan>[Cinematic]</color> MoveActorStep: action OnActionFinished fired for '{actor.CharacterName}'.");
            };

            // Enqueue on the actor's CharacterActions lane.
            // VERIFY method name on CharacterActions during implementation — see Task 10.1 pre-check.
            actor.CharacterActions.Enqueue(_action);

            if (!_blocking) _instantComplete = true;
        }

        public override void OnExit(CinematicContext ctx)
        {
            // If we're aborting mid-walk, cancel the action so the actor stops
            if (_action != null && !_actionFinished)
            {
                _action.OnCancel();
            }
        }

        public override bool IsComplete(CinematicContext ctx) =>
            _instantComplete || _actionFinished;
    }
}
```

The non-blocking path completes immediately at `OnEnter` end, leaving the action running in the background. The blocking path waits on `_actionFinished` flipping true via the `OnActionFinished` event.

- [ ] **Step 10.3: Verify compile**

`Assets → Refresh`. The `actor.CharacterActions.Enqueue(_action)` call may fail to compile if `Enqueue` isn't the actual method name. Adjust per Task 10.1 pre-check.

- [ ] **Step 10.4: Commit**

```bash
git add Assets/Scripts/Cinematics/Steps/MoveActorStep.cs
git commit -m "feat(cinematics): MoveActorStep — actor walks to role/position via CharacterAction lane"
```

---

### Task 11: `CinematicEffectSO` + `Effect_RaiseEvent` + `TriggerStep`

**Files:**
- Create: `Assets/Scripts/Cinematics/Effects/CinematicEffectSO.cs`
- Create: `Assets/Scripts/Cinematics/Effects/Effect_RaiseEvent.cs`
- Create: `Assets/Scripts/Cinematics/Steps/TriggerStep.cs`

- [ ] **Step 11.1: `CinematicEffectSO.cs`**

```csharp
using UnityEngine;

namespace MWI.Cinematics
{
    /// <summary>
    /// Polymorphic SO that fires an in-timeline effect (VFX, SFX, give-quest, etc.).
    /// Distinct from ICinematicTriggerSurface — surfaces start a cinematic; effects run inside one.
    /// </summary>
    public abstract class CinematicEffectSO : ScriptableObject
    {
        public abstract void Apply(CinematicContext ctx);
    }
}
```

- [ ] **Step 11.2: `Effect_RaiseEvent.cs`**

```csharp
using UnityEngine;
using UnityEngine.Events;

namespace MWI.Cinematics
{
    [CreateAssetMenu(
        fileName = "Effect_RaiseEvent",
        menuName = "MWI/Cinematics/Effects/Raise Event")]
    public class Effect_RaiseEvent : CinematicEffectSO
    {
        [Tooltip("UnityEvent escape hatch — wire any callback you like.")]
        [SerializeField] private UnityEvent _onApply;

        public override void Apply(CinematicContext ctx)
        {
            Debug.Log($"<color=cyan>[Cinematic]</color> Effect_RaiseEvent: firing UnityEvent on scene '{ctx.Scene?.SceneId}'.");
            _onApply?.Invoke();
        }
    }
}
```

- [ ] **Step 11.3: `TriggerStep.cs`**

```csharp
using UnityEngine;
using UnityEngine.Events;

namespace MWI.Cinematics
{
    [System.Serializable]
    public class TriggerStep : CinematicStep
    {
        [SerializeField] private CinematicEffectSO _effect;
        [SerializeField] private UnityEvent _eventHook;

        public override void OnEnter(CinematicContext ctx)
        {
            Debug.Log($"<color=cyan>[Cinematic]</color> TriggerStep entered (effect={_effect?.name ?? "null"}).");

            try { _effect?.Apply(ctx); }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                Debug.LogError($"<color=red>[Cinematic]</color> TriggerStep: effect '{_effect?.name}' threw — continuing.");
            }

            try { _eventHook?.Invoke(); }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                Debug.LogError($"<color=red>[Cinematic]</color> TriggerStep: UnityEvent threw — continuing.");
            }
        }

        // IsComplete inherits from base → returns true → fire-and-forget
    }
}
```

The try/catch follows rule #31 (defensive coding) — a misconfigured effect should not crash the whole director.

- [ ] **Step 11.4: Verify compile**

`Assets → Refresh`. Console should be clean.

- [ ] **Step 11.5: Commit**

```bash
git add Assets/Scripts/Cinematics/Effects/ \
        Assets/Scripts/Cinematics/Steps/TriggerStep.cs
git commit -m "feat(cinematics): TriggerStep + CinematicEffectSO + Effect_RaiseEvent (escape hatch)"
```

---

## Phase 1C — `CharacterCinematicState` Subsystem + Integration

### Task 12: Create `CharacterCinematicState` (no NetworkVariable yet)

**Files:**
- Create: `Assets/Scripts/Character/CharacterCinematicState/CharacterCinematicState.cs`

- [ ] **Step 12.1: Pre-check — confirm `CharacterSystem` base class**

Run Grep for `class CharacterSystem`. Confirm: `CharacterSystem` is the abstract base for character subsystem components (e.g., `CharacterMovement : CharacterSystem` per [CharacterMovement.cs:6](Assets/Scripts/Character/CharacterMovement/CharacterMovement.cs#L6)). It typically holds a reference to the parent `Character` and is auto-assigned via `GetComponentInChildren`.

If the base class is not `CharacterSystem` but something else (e.g. `CharacterSubsystem` or just `MonoBehaviour`), use the same base every other subsystem in the project uses.

- [ ] **Step 12.2: Create the file**

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace MWI.Cinematics
{
    /// <summary>
    /// Per-character cinematic state. Holds the active-actor flag (Phase 1: local bool;
    /// Phase 2: NetworkVariable<bool>), the played + pending scene-ID lists, and the
    /// active scene/role identifiers.
    ///
    /// Read by CharacterCombat (skip damage), CharacterAI (yield BT), CharacterInteraction
    /// (block external Talk/Insult), PlayerController (block movement/combat input).
    /// </summary>
    public class CharacterCinematicState : CharacterSystem
    {
        // ── Active-actor flag (Phase 1 = local bool; Phase 2 promotes to NetworkVariable) ──
        private bool _isCinematicActor;
        private string _activeRoleId;
        private string _activeSceneId;

        public bool   IsCinematicActor => _isCinematicActor;
        public string ActiveRoleId     => _activeRoleId;
        public string ActiveSceneId    => _activeSceneId;

        // ── Per-character scene history (Phase 1 = in-memory only; Phase 2 adds ICharacterSaveData<T>) ──
        private readonly HashSet<string> _playedSceneIds  = new();
        private readonly HashSet<string> _pendingSceneIds = new();

        public IReadOnlyCollection<string> GetPlayedScenes()  => _playedSceneIds;
        public IReadOnlyCollection<string> GetPendingScenes() => _pendingSceneIds;
        public bool HasPlayedScene(string sceneId) => _playedSceneIds.Contains(sceneId);

        // ── Server-side mutators (called by director / registry) ──
        public void MarkActiveActor(string sceneId, string roleId)
        {
            _isCinematicActor = true;
            _activeSceneId    = sceneId;
            _activeRoleId     = roleId;
            Debug.Log($"<color=cyan>[Cinematic]</color> '{Character?.CharacterName}' is now cinematic actor (scene={sceneId}, role={roleId}).");
        }

        public void ClearActiveActor()
        {
            _isCinematicActor = false;
            _activeSceneId    = null;
            _activeRoleId     = null;
            Debug.Log($"<color=cyan>[Cinematic]</color> '{Character?.CharacterName}' cinematic actor cleared.");
        }

        public void MarkSceneCompleted(string sceneId)
        {
            if (string.IsNullOrEmpty(sceneId)) return;
            _playedSceneIds.Add(sceneId);
            _pendingSceneIds.Remove(sceneId);
        }

        public void AddPendingScene(string sceneId)
        {
            if (string.IsNullOrEmpty(sceneId)) return;
            _pendingSceneIds.Add(sceneId);
        }

        public void RemovePendingScene(string sceneId)
        {
            if (string.IsNullOrEmpty(sceneId)) return;
            _pendingSceneIds.Remove(sceneId);
        }
    }
}
```

`Character` (the parent reference) is inherited from `CharacterSystem`. If `CharacterSystem` doesn't expose it directly, replace with whatever the project uses (e.g., `_characterRoot`).

- [ ] **Step 12.3: Verify compile**

`Assets → Refresh`. Console should be clean.

- [ ] **Step 12.4: Commit**

```bash
git add Assets/Scripts/Character/CharacterCinematicState/CharacterCinematicState.cs
git commit -m "feat(cinematics): CharacterCinematicState subsystem — per-character history + actor flag"
```

---

### Task 13: Wire `CharacterCinematicState` into the `Character` facade

**Files:**
- Modify: `Assets/Scripts/Character/Character.cs`

- [ ] **Step 13.1: Add the SerializeField + property**

In `Character.cs`, locate the block of subsystem references (search for other `[SerializeField] private CharacterMovement _characterMovement` style fields — typically grouped near the top of the class). Add:

```csharp
[SerializeField] private CharacterCinematicState _cinematicState;
public CharacterCinematicState CharacterCinematicState => _cinematicState;
```

- [ ] **Step 13.2: Auto-assign in `Awake()`**

Locate `Character.Awake()` (or the method where other subsystems are auto-assigned via `GetComponentInChildren`). Add a fallback assignment:

```csharp
if (_cinematicState == null)
    _cinematicState = GetComponentInChildren<CharacterCinematicState>(includeInactive: true);
```

- [ ] **Step 13.3: Add `using MWI.Cinematics;` to the top of `Character.cs`**

If the cinematics assembly definition is configured in Task 1, you may need to add `Cinematics` to the project's primary assembly references too. If everything compiles into the default assembly (no `.asmdef`), no change needed.

- [ ] **Step 13.4: Add `CharacterCinematicState` to the Character prefab**

Open the Character prefab (or every Character prefab variant — players, NPCs, animals if applicable) in Prefab edit mode. Add a child GameObject named `CharacterCinematicState` under the prefab root. Add the `CharacterCinematicState` component to it. Drag the child into the `_cinematicState` SerializeField on the root `Character` script. Save the prefab.

If the project has multiple Character prefab variants (player vs NPC vs animal), repeat for each. The hierarchy convention from the project rules requires every Character to have this subsystem.

- [ ] **Step 13.5: Verify in Play mode**

Enter Play mode. Spawn a character. Open the Inspector on the root Character GameObject. Confirm `_cinematicState` is non-null and points to the child `CharacterCinematicState`. Stop Play mode.

- [ ] **Step 13.6: Commit**

```bash
git add Assets/Scripts/Character/Character.cs \
        Assets/Resources/<character-prefabs>
git commit -m "feat(cinematics): wire CharacterCinematicState into Character facade + prefab hierarchy"
```

(Adjust the prefab path glob to match your project's actual Character prefab locations — common spots: `Assets/Resources/Characters/`, `Assets/Prefabs/Characters/`.)

---

### Task 14: Damage gate in `CharacterCombat`

**Files:**
- Modify: `Assets/Scripts/Character/CharacterCombat/CharacterCombat.cs`

- [ ] **Step 14.1: Find the damage application method**

Run Grep for `void TakeDamage` or `ApplyDamage` inside `CharacterCombat.cs`. The exact name varies by project. Note the line number; the gate goes at the very top of the method body.

- [ ] **Step 14.2: Add the gate**

At the top of the damage method (before any HP mutation, status-effect application, or invocation of damage events), add:

```csharp
// Cinematic actor invincibility — bound actors take no damage.
// Phase 1: this is a local check (works on host); Phase 2 promotes IsCinematicActor to a NetworkVariable
// so all clients respect it.
if (Character?.CharacterCinematicState != null && Character.CharacterCinematicState.IsCinematicActor)
{
    Debug.Log($"<color=cyan>[Cinematic]</color> Damage skipped on '{Character.CharacterName}' — IsCinematicActor=true.");
    return;
}
```

The `Character?.CharacterCinematicState` chain is null-safe so existing characters without the new subsystem don't crash (defensive coding, rule #31).

- [ ] **Step 14.3: Verify compile**

`Assets → Refresh`. Should compile cleanly.

- [ ] **Step 14.4: Commit**

```bash
git add Assets/Scripts/Character/CharacterCombat/CharacterCombat.cs
git commit -m "feat(cinematics): CharacterCombat skips damage when target IsCinematicActor"
```

---

### Task 15: Player input gate in `PlayerController`

**Files:**
- Modify: `Assets/Scripts/Character/CharacterControllers/PlayerController.cs`

- [ ] **Step 15.1: Find the input handlers**

Run Grep inside `PlayerController.cs` for `Input.GetKey`, `Input.GetMouseButton`, and any methods that queue movement / combat actions. The gate goes at the top of `Update()` (or wherever movement/combat input is read), before any input is converted into an action queue mutation.

- [ ] **Step 15.2: Add the gate**

At the very top of `Update()` (after the `IsOwner` gate per rule #33), add:

```csharp
// Block movement / combat input while this player is bound as a cinematic actor.
// UI input (ESC menu, dialogue advance) is allowed and routed elsewhere.
if (_character?.CharacterCinematicState != null && _character.CharacterCinematicState.IsCinematicActor)
{
    return;     // Input.* checks below are skipped; menu input lives in other components
}
```

`_character` is the existing reference to the owned Character on `PlayerController` (verify the field name during implementation — common names: `_character`, `_ownedCharacter`, `Character`).

- [ ] **Step 15.3: Verify compile**

`Assets → Refresh`. Should compile cleanly.

- [ ] **Step 15.4: Commit**

```bash
git add Assets/Scripts/Character/CharacterControllers/PlayerController.cs
git commit -m "feat(cinematics): PlayerController blocks movement/combat input when IsCinematicActor"
```

---

## Phase 1D — Director + Public Facade

### Task 16: Create `CinematicDirector` (local-only Phase 1)

**Files:**
- Create: `Assets/Scripts/Cinematics/Core/CinematicDirector.cs`

- [ ] **Step 16.1: Create the file**

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MWI.Cinematics
{
    /// <summary>
    /// Server-side runtime that iterates a CinematicSceneSO's steps end-to-end.
    ///
    /// Phase 1: plain MonoBehaviour, single-player / server-only. Spawned as a child of
    /// a host-only "CinematicDirectors" container GameObject for the duration of the scene.
    ///
    /// Phase 2 will promote to NetworkBehaviour with NetworkObject spawn-with-observers,
    /// ServerRpc/ClientRpc, and the AllMustPress advance-press protocol.
    /// </summary>
    public class CinematicDirector : MonoBehaviour
    {
        private CinematicSceneSO _scene;
        private CinematicContext _ctx;
        private int _currentStepIndex = -1;
        private ICinematicStep _currentStep;
        private bool _running;
        private bool _aborted;

        public CinematicSceneSO Scene => _scene;
        public bool IsRunning => _running;

        // Step queue allows ChoiceStep (Phase 3) to push branch steps onto the front
        private readonly LinkedList<ICinematicStep> _stepQueue = new();

        public void Initialize(CinematicSceneSO scene, CinematicContext ctx)
        {
            _scene = scene;
            _ctx   = ctx;
            _ctx.Scene    = scene;
            _ctx.Director = this;
            _ctx.StartTimeSim = Time.time;

            foreach (var step in scene.Steps)
                _stepQueue.AddLast(step);

            Debug.Log($"<color=cyan>[Cinematic]</color> Director initialized for scene '{scene.SceneId}' with {_stepQueue.Count} steps.");
        }

        public void RunScene()
        {
            if (_running) return;
            _running = true;
            StartCoroutine(StepLoop());
        }

        public void Abort(CinematicEndReason reason = CinematicEndReason.Aborted)
        {
            if (!_running) return;
            _aborted = true;
            Debug.LogWarning($"<color=yellow>[Cinematic]</color> Director aborting scene '{_scene?.SceneId}' (reason={reason}).");
        }

        private IEnumerator StepLoop()
        {
            int stepNumber = 0;
            while (_stepQueue.Count > 0 && !_aborted)
            {
                _currentStep = _stepQueue.First.Value;
                _stepQueue.RemoveFirst();
                _currentStepIndex = stepNumber++;

                Debug.Log($"<color=cyan>[Cinematic]</color> Director: entering step {_currentStepIndex} ({_currentStep.GetType().Name}).");

                bool entered = false;
                try { _currentStep.OnEnter(_ctx); entered = true; }
                catch (System.Exception e)
                {
                    Debug.LogException(e);
                    Debug.LogError($"<color=red>[Cinematic]</color> Step {_currentStepIndex} OnEnter threw — skipping step.");
                }

                if (entered)
                {
                    while (!_aborted)
                    {
                        bool isComplete;
                        try
                        {
                            _currentStep.OnTick(_ctx, Time.deltaTime);
                            isComplete = _currentStep.IsComplete(_ctx);
                        }
                        catch (System.Exception e)
                        {
                            Debug.LogException(e);
                            Debug.LogError($"<color=red>[Cinematic]</color> Step {_currentStepIndex} OnTick/IsComplete threw — treating as complete.");
                            isComplete = true;
                        }

                        if (isComplete) break;
                        yield return null;
                    }

                    try { _currentStep.OnExit(_ctx); }
                    catch (System.Exception e)
                    {
                        Debug.LogException(e);
                        Debug.LogError($"<color=red>[Cinematic]</color> Step {_currentStepIndex} OnExit threw — continuing.");
                    }
                }

                _currentStep = null;
            }

            EndScene(_aborted ? CinematicEndReason.Aborted : CinematicEndReason.Completed);
        }

        private void EndScene(CinematicEndReason reason)
        {
            // Clear actor flags
            foreach (var kvp in _ctx.BoundRoles)
            {
                kvp.Value?.CharacterCinematicState?.ClearActiveActor();
                if (reason == CinematicEndReason.Completed)
                    kvp.Value?.CharacterCinematicState?.MarkSceneCompleted(_scene.SceneId);
            }

            Debug.Log($"<color=cyan>[Cinematic]</color> Director: scene '{_scene?.SceneId}' ended (reason={reason}).");

            _running = false;
            // Phase 1: destroy the director GameObject. Phase 2 will use NetworkObject.Despawn.
            Destroy(gameObject);
        }
    }
}
```

The director uses a `LinkedList<ICinematicStep>` so future Phase 3 `ChoiceStep` can `AddFirst()` branch steps without rebuilding the queue. Phase 1 just iterates the list once.

- [ ] **Step 16.2: Verify compile**

`Assets → Refresh`. Console should be clean.

- [ ] **Step 16.3: Commit**

```bash
git add Assets/Scripts/Cinematics/Core/CinematicDirector.cs
git commit -m "feat(cinematics): CinematicDirector Phase 1 — local step iteration with try/catch resilience"
```

---

### Task 17: Create `Cinematics` public facade (`TryPlay`)

**Files:**
- Create: `Assets/Scripts/Cinematics/Core/Cinematics.cs`

- [ ] **Step 17.1: Create the file**

```csharp
using UnityEngine;

namespace MWI.Cinematics
{
    /// <summary>
    /// Public entry point. Phase 1 — server-only, no eligibility checks (those land in Phase 2's
    /// CinematicRegistry). Phase 1's TryPlay binds roles, sets up the context, spawns a director,
    /// and runs the scene.
    /// </summary>
    public static class Cinematics
    {
        private const string DirectorContainerName = "CinematicDirectors";

        /// <summary>
        /// Trigger a cinematic scene. Phase 1: server-side only (or solo / host).
        /// Phase 2's TryPlay will route through CinematicRegistry for eligibility + PlayMode checks.
        /// </summary>
        public static bool TryPlay(
            CinematicSceneSO scene,
            Character triggeringPlayer,
            Character otherParticipant = null)
        {
            if (scene == null)
            {
                Debug.LogError("<color=red>[Cinematic]</color> Cinematics.TryPlay: scene is null.");
                return false;
            }
            if (triggeringPlayer == null)
            {
                Debug.LogError("<color=red>[Cinematic]</color> Cinematics.TryPlay: triggeringPlayer is null.");
                return false;
            }

            Debug.Log($"<color=cyan>[Cinematic]</color> Cinematics.TryPlay: starting scene '{scene.SceneId}' triggered by '{triggeringPlayer.CharacterName}'.");

            var ctx = new CinematicContext
            {
                TriggeringPlayer = triggeringPlayer,
                OtherParticipant = otherParticipant,
                TriggerOrigin    = triggeringPlayer.transform.position,
            };

            // Resolve roles
            foreach (var slot in scene.Roles)
            {
                if (slot.Selector == null)
                {
                    if (slot.IsOptional) continue;
                    Debug.LogError($"<color=red>[Cinematic]</color> Required role '{slot.RoleId}' has no selector assigned. Aborting.");
                    return false;
                }

                var bound = slot.Selector.Resolve(ctx);
                if (bound == null)
                {
                    if (slot.IsOptional)
                    {
                        Debug.LogWarning($"<color=yellow>[Cinematic]</color> Optional role '{slot.RoleId}' did not bind. Continuing.");
                        continue;
                    }
                    Debug.LogError($"<color=red>[Cinematic]</color> Required role '{slot.RoleId}' could not be bound. Aborting.");
                    return false;
                }

                ctx.BoundRoles[slot.RoleId] = bound;
                if (bound.IsPlayer()) ctx.ParticipatingPlayers.Add(bound);
            }

            // Mark all bound actors
            foreach (var kvp in ctx.BoundRoles)
            {
                kvp.Value.CharacterCinematicState?.MarkActiveActor(scene.SceneId, kvp.Key.Value);
            }

            // Spawn the director
            var container = GameObject.Find(DirectorContainerName);
            if (container == null) container = new GameObject(DirectorContainerName);

            var directorGo = new GameObject($"Director_{scene.SceneId}");
            directorGo.transform.SetParent(container.transform);
            var director = directorGo.AddComponent<CinematicDirector>();
            director.Initialize(scene, ctx);
            director.RunScene();

            return true;
        }
    }
}
```

`Character.IsPlayer()` was confirmed during brainstorming via [DialogueManager.cs:68](Assets/Scripts/Dialogue/DialogueManager.cs#L68). If the property is named differently (`IsPlayer` as a property vs. method), adjust accordingly.

- [ ] **Step 17.2: Verify compile**

`Assets → Refresh`. Should compile cleanly. Phase 1's `Cinematics.TryPlay` does NOT do eligibility / PlayMode / authority checks — those land in Phase 2's `CinematicRegistry`.

- [ ] **Step 17.3: Commit**

```bash
git add Assets/Scripts/Cinematics/Core/Cinematics.cs
git commit -m "feat(cinematics): Cinematics public facade with TryPlay (Phase 1 — no registry/eligibility yet)"
```

---

## Phase 1E — Demo Asset + Verification

### Task 18: Author the test scene asset

**Files:**
- Create: `Assets/Resources/Data/Cinematics/Test_FirstMeeting.asset`
- Create: `Assets/Resources/Data/Cinematics/Selector_TriggeringPlayer.asset` (if Task 5.5 was deferred)
- Create: `Assets/Resources/Data/Cinematics/Selector_OtherParticipant.asset` (skipped — not yet implemented; Phase 2)

For Phase 1 the test scene uses **two `Selector_TriggeringPlayer` instances** (or two roles both bound to the trigger context's `Hero`). This is enough to validate the step iteration model. A proper `Selector_OtherParticipant` lands in Phase 2 alongside the Talk surface integration.

- [ ] **Step 18.1: Create the selector asset (if deferred from Task 5.5)**

Project window → `Assets/Resources/Data/Cinematics/` → right-click → `Create → MWI → Cinematics → Selectors → Triggering Player`. Save as `Selector_TriggeringPlayer.asset`.

- [ ] **Step 18.2: Create the scene asset**

Project window → `Assets/Resources/Data/Cinematics/` → right-click → `Create → MWI → Cinematics → Scene`. Save as `Test_FirstMeeting.asset`.

- [ ] **Step 18.3: Configure the scene asset in the Inspector**

Click `Test_FirstMeeting.asset`. Configure:

**Identity:**
- `_displayName`: `"Test First Meeting (Phase 1)"`
- `_sceneId`: leave the auto-generated GUID

**Cast (Roles):**
- Add one entry:
  - `_roleId`: `Hero`
  - `_displayName`: `Hero`
  - `_selector`: drag `Selector_TriggeringPlayer.asset` here
  - `_isOptional`: false
  - `_isPrimaryActor`: true

**Timeline (Steps):**

Use the `[SerializeReference]` polymorphic dropdown to add four steps in order:

1. **WaitStep** — `_durationSec`: `0.5`
2. **SpeakStep** — `_speakerRoleId`: `Hero`, `_lineText`: `"...where am I?"`, `_typingSpeedOverride`: `0`
3. **WaitStep** — `_durationSec`: `0.5`
4. **TriggerStep** — `_effect`: leave null, `_eventHook`: leave empty (just verifies the step lifecycle runs)

Save the asset.

- [ ] **Step 18.4: Verify the asset loads at runtime**

In `Assets/Scripts/DebugScript.cs` (or any temporary harness MonoBehaviour with an inspector button), add a public method that calls:

```csharp
[ContextMenu("Test Phase 1 Cinematic")]
public void TriggerTestCinematic()
{
    var scene = Resources.Load<MWI.Cinematics.CinematicSceneSO>("Data/Cinematics/Test_FirstMeeting");
    if (scene == null)
    {
        Debug.LogError("Could not load Test_FirstMeeting from Resources/Data/Cinematics/.");
        return;
    }

    var player = FindObjectOfType<Character>();
    if (player == null)
    {
        Debug.LogError("No Character in scene to use as triggering player.");
        return;
    }

    MWI.Cinematics.Cinematics.TryPlay(scene, player);
}
```

`FindObjectOfType<Character>()` is a Phase 1-only convenience — it picks any character in the scene. In the test scene that's typically the player avatar; in MP it'd be the wrong call. Phase 2 wires the Talk surface and this debug hook is no longer needed for normal operation.

- [ ] **Step 18.5: Commit**

```bash
git add Assets/Resources/Data/Cinematics/Test_FirstMeeting.asset \
        Assets/Resources/Data/Cinematics/Selector_TriggeringPlayer.asset \
        Assets/Scripts/DebugScript.cs
git commit -m "test(cinematics): hand-authored Test_FirstMeeting scene + DebugScript context-menu trigger"
```

---

### Task 19: End-to-end manual verification

This is the Phase 1 ship gate. If all checks pass, Phase 1 is done.

- [ ] **Step 19.1: Enter Play mode**

Open the project's main test scene (typically a sandbox map with a player and at least one NPC). Enter Play mode.

- [ ] **Step 19.2: Confirm the player avatar has `CharacterCinematicState`**

Select the player Character GameObject in the Hierarchy. Confirm in the Inspector:
- `Character` component's `_cinematicState` field is non-null
- A child GameObject named `CharacterCinematicState` exists with the `CharacterCinematicState` component

If missing, return to Task 13.4 and re-add the subsystem to the prefab.

- [ ] **Step 19.3: Trigger the test cinematic**

In the Inspector on the GameObject that holds `DebugScript`, right-click the component header → `Test Phase 1 Cinematic`.

**Expected log output (in order):**

```
[Cinematic] Cinematics.TryPlay: starting scene 'XXX...' triggered by 'PlayerName'.
[Cinematic] 'PlayerName' is now cinematic actor (scene=..., role=Hero).
[Cinematic] Director initialized for scene 'XXX...' with 4 steps.
[Cinematic] Director: entering step 0 (WaitStep).
[Cinematic] WaitStep entered — will complete at sim time XXX (duration 0.50s).
[Cinematic] Director: entering step 1 (SpeakStep).
[Cinematic] SpeakStep: 'PlayerName' says "...where am I?".
[Cinematic] SpeakStep: typing finished for 'PlayerName'.
[Cinematic] Director: entering step 2 (WaitStep).
[Cinematic] WaitStep entered — will complete at sim time XXX (duration 0.50s).
[Cinematic] Director: entering step 3 (TriggerStep).
[Cinematic] TriggerStep entered (effect=null).
[Cinematic] Director: scene 'XXX...' ended (reason=Completed).
[Cinematic] 'PlayerName' cinematic actor cleared.
```

If a step transitions out of order, or the speech bubble doesn't appear, or the actor doesn't get the cinematic-actor flag, the issue is local — debug with the `Debug.Log` statements already in place.

- [ ] **Step 19.4: Verify visual: speech bubble appears + persists**

Watch the player's head in the Game view during step 1 (`SpeakStep`). The speech bubble should appear with the line text and stay visible for ~1.5s after typing finishes (the Phase 1 auto-advance delay), then close.

- [ ] **Step 19.5: Verify combat damage is blocked**

While the cinematic is running:
- If your test setup has a way to deal damage to the player (a hostile NPC, a damage tester button), trigger it.
- Expected: log line `[Cinematic] Damage skipped on 'PlayerName' — IsCinematicActor=true.` and zero HP loss.

If damage applies, return to Task 14 and verify the gate is at the top of the right method.

- [ ] **Step 19.6: Verify player input is blocked**

While the cinematic is running:
- Press WASD / left-click to move
- Expected: the player does NOT move; `PlayerController.Update()` early-returns due to the gate.

If input still applies, return to Task 15 and verify the gate is at the top of `Update()`.

- [ ] **Step 19.7: Verify cleanup after scene end**

After the scene ends:
- Combat damage applies normally again.
- Player input controls movement again.
- The `Director_*` GameObject is destroyed (check the Hierarchy under `CinematicDirectors` container).
- `CharacterCinematicState.IsCinematicActor` is `false` (Inspector check).

- [ ] **Step 19.8: Re-trigger the cinematic to verify idempotence**

Run `Test Phase 1 Cinematic` a second time. Expected: scene plays again identically. (Phase 1 has no PlayMode bookkeeping — `OncePerWorld` enforcement comes in Phase 2.)

- [ ] **Step 19.9: Commit any final fix-ups discovered during verification**

If verification surfaced bugs that required edits, commit those now:

```bash
git add <fix-files>
git commit -m "fix(cinematics): <specific fix discovered during Phase 1 verification>"
```

---

## Phase 1 Self-Review Checklist (post-implementation)

Before declaring Phase 1 done, run this checklist:

- [ ] All 19 tasks completed; commits exist for each major group.
- [ ] Console is clean during normal gameplay (no spam from cinematics code outside an active scene).
- [ ] `IsCinematicActor` correctly clears in all scene-end paths (completion, abort, exception during step).
- [ ] `Character.CharacterCinematicState` is non-null on every instantiated Character (player and NPC) in the test scene.
- [ ] `MoveActorStep` correctly enqueues a `CharacterAction_CinematicMoveTo` and waits on `OnActionFinished` (verified by adding a `Debug.Break()` in the action's `Finish()` callback if uncertain).
- [ ] No `// TODO` comments left in the code. Future-Phase deferrals are noted as `// Phase N: ...` with the specific phase that addresses them.
- [ ] Unit ≈ Verification step works on host. (Phase 2 handles "works on remote client".)

---

## Hand-off to Phase 2

Once Phase 1 verification is green and committed, the next plan handles:

- Promote `CinematicDirector` to `NetworkBehaviour` on a server-spawned `NetworkObject` with a participating-clients observer set.
- Promote `CharacterCinematicState._isCinematicActor` to `NetworkVariable<bool>` (+ `_activeRoleId`, `_activeSceneId`).
- Add `CharacterCinematicState : ICharacterSaveData<CinematicHistorySaveData>` (persistence).
- Add `CinematicWorldState : ISaveable` (world-scoped played-state).
- Add `CinematicRegistry` server-side service with eligibility queries + per-character runtime assignment API.
- Add `Surface_OnInteractionAction` (Talk-only validation) + 4 other surface SOs.
- Add 6 `CinematicEligibilityRuleSO` subclasses + 4 PlayModes.
- Wire `CharacterInteraction.OnTalk` to query the registry before falling back to generic Talk.
- Implement the `AllMustPress` advance-press protocol with grace timer.
- Replace Phase 1's `SpeakStep` 1.5s auto-advance with the real advance-press wait.
- Add disconnect / hibernation / actor-lost abort scenarios.

The Phase 2 plan will be written as a separate doc when Phase 1 ships and is validated in solo + host scenarios.
