# Character Archetype System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Transform the monolithic Character class into a composable capability registry so the game can support radically different character types (humanoids, animals, dragons, etc.) that players and NPCs can both inhabit.

**Architecture:** Capability Registry pattern. Character.cs becomes a slim core with identity, network, lifecycle, and a runtime dictionary of CharacterSystem capabilities. CharacterArchetype ScriptableObjects define default capability loadouts per character type. Visual layer abstracted behind ICharacterVisual interface. Interactions driven by IInteractionProvider capabilities.

**Tech Stack:** Unity 6, Netcode for GameObjects (NGO) 2.10, C#

**Spec:** `docs/superpowers/specs/2026-03-30-character-archetype-system-design.md`

---

## File Map

### New Files

| File | Location | Responsibility |
|------|----------|----------------|
| `BodyType.cs` | `Assets/Scripts/Character/Archetype/` | Enum: Bipedal, Quadruped, Flying, Aquatic, Insect |
| `MovementMode.cs` | `Assets/Scripts/Character/Archetype/` | Enum: Walk, Run, Fly, Swim, Burrow |
| `WanderStyle.cs` | `Assets/Scripts/Character/Archetype/` | Enum: Straight, ZigZag, Patrol, Nervous |
| `CharacterArchetype.cs` | `Assets/Scripts/Character/Archetype/` | ScriptableObject — blueprint for character types |
| `AnimationKey.cs` | `Assets/Scripts/Character/Visual/` | Enum: Idle, Walk, Run, Attack, GetHit, Die + universal keys |
| `AnimationProfile.cs` | `Assets/Scripts/Character/Visual/` | ScriptableObject — maps AnimationKey + strings to clip names |
| `ICharacterVisual.cs` | `Assets/Scripts/Character/Visual/` | Core visual interface — orientation, animation, feedback |
| `IAnimationLayering.cs` | `Assets/Scripts/Character/Visual/` | Optional interface — overlay animations on tracks |
| `ICharacterPartCustomization.cs` | `Assets/Scripts/Character/Visual/` | Optional interface — skins, colors, dismemberment |
| `IBoneAttachment.cs` | `Assets/Scripts/Character/Visual/` | Optional interface — attach GameObjects to bones |
| `IInteractionProvider.cs` | `Assets/Scripts/Interactable/` | Interface for capabilities to advertise interaction options |
| `InteractionOption.cs` | `Assets/Scripts/Interactable/` | Standalone class replacing nested struct in InteractableObject |
| `IOfflineCatchUp.cs` | `Assets/Scripts/Character/SaveLoad/` | Interface for macro-simulation offline need decay |
| `ICharacterSaveData.cs` | `Assets/Scripts/Character/SaveLoad/` | Interface for per-capability serialization |

### Modified Files

| File | Change |
|------|--------|
| `Assets/Scripts/Character/Character.cs` | Add capability registry (`_capabilitiesByType` dict + `_allCapabilities` list), archetype reference, `Register`/`Unregister`/`Get<T>`/`TryGet<T>`/`Has<T>`/`GetAll<T>` methods. Backward-compat properties delegate to registry. |
| `Assets/Scripts/Character/CharacterSystem.cs` | Add `_character?.Register(this)` in `OnEnable()`, `_character?.Unregister(this)` in `OnDisable()`. Translate French comments to English. |
| `Assets/Scripts/Character/CharacterVisual.cs` | Implement `ICharacterVisual`, `IAnimationLayering`, `ICharacterPartCustomization`. Wrap existing sprite logic behind interface methods. |
| `Assets/Scripts/Character/CharacterAnimator.cs` | Add `AnimationProfile`-aware methods alongside existing humanoid methods. No removal of existing code. |
| `Assets/Scripts/Interactable/InteractableObject.cs` | Extract `InteractionOption` struct to standalone class. Keep backward compat via using alias or adapter. |
| `Assets/Scripts/Interactable/CharacterInteractable.cs` | Replace hardcoded `GetHoldInteractionOptions`/`GetDialogueInteractionOptions` with `GetAll<IInteractionProvider>()` collection. |
| `Assets/Scripts/Character/CharacterControllers/CharacterGameController.cs` | Replace direct `GetComponent<NPCBehaviourTree>()` with registry lookup via `_character.TryGet<>()`. |

---

## Phase 1: Foundation (Non-Breaking)

### Task 1: Capability Registry on Character.cs

**Files:**
- Modify: `Assets/Scripts/Character/Character.cs`

- [ ] **Step 1: Read current Character.cs to understand the exact field layout**

Read `Assets/Scripts/Character/Character.cs` fully. Note every `[SerializeField]` subsystem reference and every public property that returns a subsystem.

- [ ] **Step 2: Add the registry data structures and methods to Character.cs**

Add these members to `Character.cs`, before the existing subsystem fields:

```csharp
// ── Capability Registry ──────────────────────────────────────────
private readonly Dictionary<System.Type, CharacterSystem> _capabilitiesByType = new();
private readonly List<CharacterSystem> _allCapabilities = new();

/// <summary>Register a subsystem in the capability registry. Called by CharacterSystem.OnEnable.</summary>
public void Register(CharacterSystem system)
{
    if (system == null) return;
    var type = system.GetType();
    _capabilitiesByType[type] = system;
    if (!_allCapabilities.Contains(system))
        _allCapabilities.Add(system);
}

/// <summary>Unregister a subsystem from the capability registry. Called by CharacterSystem.OnDisable.</summary>
public void Unregister(CharacterSystem system)
{
    if (system == null) return;
    _capabilitiesByType.Remove(system.GetType());
    _allCapabilities.Remove(system);
}

/// <summary>Get a capability by exact type. Throws KeyNotFoundException if missing.</summary>
public T Get<T>() where T : CharacterSystem
{
    if (_capabilitiesByType.TryGetValue(typeof(T), out var system))
        return (T)system;
    throw new System.Collections.Generic.KeyNotFoundException(
        $"Capability {typeof(T).Name} not found on character '{CharacterName}'.");
}

/// <summary>Try to get a capability by exact type. Returns false if missing.</summary>
public bool TryGet<T>(out T system) where T : CharacterSystem
{
    if (_capabilitiesByType.TryGetValue(typeof(T), out var s))
    {
        system = (T)s;
        return true;
    }
    system = null;
    return false;
}

/// <summary>Check if a capability exists by exact type.</summary>
public bool Has<T>() where T : CharacterSystem
{
    return _capabilitiesByType.ContainsKey(typeof(T));
}

/// <summary>Get all capabilities implementing a given interface or base type. Linear scan.</summary>
public System.Collections.Generic.IEnumerable<T> GetAll<T>()
{
    for (int i = 0; i < _allCapabilities.Count; i++)
    {
        if (_allCapabilities[i] is T match)
            yield return match;
    }
}
```

- [ ] **Step 3: Verify compilation**

Run: Use MCP `console-get-logs` or `assets-refresh` to trigger recompilation and verify zero errors.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Character/Character.cs
git commit -m "feat(archetype): add capability registry to Character.cs

Adds Register/Unregister/Get<T>/TryGet<T>/Has<T>/GetAll<T> methods
with dual storage (dictionary + list) for O(1) type lookup and
interface queries. No breaking changes — existing code untouched."
```

---

### Task 2: CharacterSystem Registration in OnEnable/OnDisable

**Files:**
- Modify: `Assets/Scripts/Character/CharacterSystem.cs`

- [ ] **Step 1: Read current CharacterSystem.cs**

Read `Assets/Scripts/Character/CharacterSystem.cs` fully. Note the existing `OnEnable`/`OnDisable` event subscription code.

- [ ] **Step 2: Add Register/Unregister calls**

Add `_character?.Register(this)` as the **last line** of the existing `OnEnable()`, and `_character?.Unregister(this)` as the **first line** of the existing `OnDisable()`. This ensures event subscriptions happen before registration, and unregistration happens before event unsubscriptions.

In `OnEnable()`, after the existing event subscriptions, add:
```csharp
_character?.Register(this);
```

In `OnDisable()`, before the existing event unsubscriptions, add:
```csharp
_character?.Unregister(this);
```

- [ ] **Step 3: Translate French comments to English**

Replace all French XML doc comments and inline comments with English equivalents:
- `Appelé quand le personnage tombe inconscient OU meurt.` → `Called when the character falls unconscious OR dies.`
- `Appelé quand le personnage se réveille d'une perte de connaissance.` → `Called when the character wakes up from unconsciousness.`
- `Appelé uniquement quand le personnage meurt définitivement.` → `Called only when the character permanently dies.`
- `Appelé quand le personnage entre ou sort de la posture de combat.` → `Called when the character enters or exits combat stance.`
- `Idéal pour stopper une ligne de déplacement ou annuler une interaction.` → `Ideal for stopping movement or cancelling an interaction.`

- [ ] **Step 4: Verify compilation**

Run: Use MCP `assets-refresh` to trigger recompilation. Verify zero errors.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Character/CharacterSystem.cs
git commit -m "feat(archetype): register CharacterSystem in capability registry on enable

CharacterSystem.OnEnable now calls _character.Register(this) after
event subscriptions. OnDisable calls Unregister before unsubscribing.
Also translated French comments to English (CLAUDE.md rule 23)."
```

---

### Task 3: Backward-Compatible Properties on Character.cs

**Files:**
- Modify: `Assets/Scripts/Character/Character.cs`

- [ ] **Step 1: Convert subsystem properties to registry delegation**

For each existing subsystem property on `Character.cs`, change the getter to delegate to the registry while keeping the `[SerializeField]` backing field for Inspector assignment. The backing field now serves as a fallback (for cases where `OnEnable` hasn't fired yet during initialization).

For every property that returns a `CharacterSystem` subclass (approximately 30 properties), change the pattern from:

```csharp
// Before:
public CharacterCombat CharacterCombat => _characterCombat;
```

To:

```csharp
// After:
public CharacterCombat CharacterCombat => TryGet<CharacterCombat>(out var s) ? s : _characterCombat;
```

This means: prefer the registry (live truth), fall back to the serialized field (for Awake-time access before OnEnable).

Apply this to all ~30 subsystem properties. The full list from the codebase:
- `Controller` (CharacterGameController)
- `CharacterMovement`
- `CharacterVisual`
- `CharacterActions`
- `CharacterInteraction`
- `CharacterEquipment` (CharacterEquipment)
- `CharacterRelation`
- `CharacterParty`
- `CharacterCommunity`
- `CharacterInteractable` (note: this is NOT a CharacterSystem — skip it)
- `CharacterCombat`
- `CharacterNeeds`
- `CharacterAwareness`
- `CharacterSpeech`
- `StatusManager`
- `CharacterProfile`
- `CharacterTraits`
- `CharacterInvitation`
- `CharacterJob`
- `CharacterSchedule`
- `CharacterSkills`
- `CharacterMentorship`
- `CharacterLocations`
- `CharacterGoap`
- `CharacterCombatLevel`
- `CharacterBlueprints`
- `CharacterAbilities`
- `CharacterBookKnowledge`
- `BattleCircleManager`
- `FloatingTextSpawner`
- `FurniturePlacementManager`

**Important:** Only convert properties whose type extends `CharacterSystem`. Skip `CharacterInteractable` (it extends `InteractableObject`, not `CharacterSystem`), `CharacterBio`, `CharacterStats`, and any non-CharacterSystem types. Check each type before converting.

- [ ] **Step 2: Verify compilation**

Run: Use MCP `assets-refresh`. Zero errors expected — all call sites still use the same property names.

- [ ] **Step 3: Quick smoke test in Unity**

Enter Play Mode briefly. Verify a humanoid character spawns and behaves normally (walks, interacts). The registry should now be populated with all subsystems — add a temporary `Debug.Log($"[Capability Registry] {CharacterName} registered {_allCapabilities.Count} capabilities")` in `OnNetworkSpawn()` or at the end of `InitializeAll()` to verify. Remove after confirming.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Character/Character.cs
git commit -m "feat(archetype): backward-compat properties delegate to capability registry

All ~30 subsystem properties now prefer registry lookup with fallback
to serialized field. Zero breaking changes — every call site works
unchanged. This enables future TryGet<T> migration."
```

---

### Task 4: Enums and Base Types

**Files:**
- Create: `Assets/Scripts/Character/Archetype/BodyType.cs`
- Create: `Assets/Scripts/Character/Archetype/MovementMode.cs`
- Create: `Assets/Scripts/Character/Archetype/WanderStyle.cs`
- Create: `Assets/Scripts/Character/Visual/AnimationKey.cs`

- [ ] **Step 1: Create Archetype directory**

```bash
mkdir -p "Assets/Scripts/Character/Archetype"
mkdir -p "Assets/Scripts/Character/Visual"
```

- [ ] **Step 2: Create BodyType.cs**

```csharp
// No namespace — matches existing Character codebase convention
public enum BodyType : byte
{
    Bipedal,
    Quadruped,
    Flying,
    Aquatic,
    Insect
}
```

- [ ] **Step 3: Create MovementMode.cs**

```csharp
// No namespace — matches existing Character codebase convention
[System.Flags]
public enum MovementMode : byte
{
    Walk    = 1 << 0,
    Run     = 1 << 1,
    Fly     = 1 << 2,
    Swim    = 1 << 3,
    Burrow  = 1 << 4
}
```

- [ ] **Step 4: Create WanderStyle.cs**

```csharp
// No namespace — matches existing Character codebase convention
public enum WanderStyle : byte
{
    Straight,
    ZigZag,
    Patrol,
    Nervous
}
```

- [ ] **Step 5: Create AnimationKey.cs**

```csharp
// No namespace — matches existing Character codebase convention
/// <summary>
/// Universal animation keys shared across all archetypes.
/// For archetype-specific animations, use the string-based PlayAnimation overload.
/// </summary>
public enum AnimationKey : byte
{
    Idle,
    Walk,
    Run,
    Attack,
    GetHit,
    Die,
    PickUp,
    Action
}
```

- [ ] **Step 6: Verify compilation**

Run: Use MCP `assets-refresh`. Zero errors.

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/Character/Archetype/ Assets/Scripts/Character/Visual/AnimationKey.cs
git commit -m "feat(archetype): add BodyType, MovementMode, WanderStyle, AnimationKey enums

Foundation enums for the archetype system. BodyType defines character
body shapes, MovementMode is a flags enum for locomotion capabilities,
WanderStyle parameterizes AI wandering, AnimationKey provides universal
animation keys with string fallback for archetype-specific animations."
```

---

### Task 5: AnimationProfile ScriptableObject

**Files:**
- Create: `Assets/Scripts/Character/Visual/AnimationProfile.cs`

- [ ] **Step 1: Create AnimationProfile.cs**

```csharp
using System.Collections.Generic;
using UnityEngine;

// No namespace — matches existing Character codebase convention

/// <summary>
/// Maps semantic AnimationKey enums and custom string keys to actual clip/animation names.
/// Each CharacterArchetype references one of these to define its animation set.
/// </summary>
[CreateAssetMenu(fileName = "New Animation Profile", menuName = "MWI/Character/Animation Profile")]
public class AnimationProfile : ScriptableObject
{
        [System.Serializable]
        public struct AnimationEntry
        {
            public AnimationKey Key;
            public string ClipName;
        }

        [System.Serializable]
        public struct CustomAnimationEntry
        {
            public string CustomKey;
            public string ClipName;
        }

        [SerializeField] private List<AnimationEntry> _keyMappings = new();
        [SerializeField] private List<CustomAnimationEntry> _customMappings = new();

        private Dictionary<AnimationKey, string> _keyLookup;
        private Dictionary<string, string> _customLookup;

        private void BuildLookups()
        {
            if (_keyLookup != null) return;

            _keyLookup = new Dictionary<AnimationKey, string>();
            foreach (var entry in _keyMappings)
                _keyLookup[entry.Key] = entry.ClipName;

            _customLookup = new Dictionary<string, string>();
            foreach (var entry in _customMappings)
                _customLookup[entry.CustomKey] = entry.ClipName;
        }

        /// <summary>Resolve a universal AnimationKey to a clip name. Returns null if unmapped.</summary>
        public string GetClipName(AnimationKey key)
        {
            BuildLookups();
            return _keyLookup.TryGetValue(key, out var clip) ? clip : null;
        }

        /// <summary>Resolve a custom string key to a clip name. Returns null if unmapped.</summary>
        public string GetClipName(string customKey)
        {
            BuildLookups();
            return _customLookup.TryGetValue(customKey, out var clip) ? clip : null;
        }

    /// <summary>Force rebuild of lookups (call after modifying entries at runtime).</summary>
    public void InvalidateCache()
    {
        _keyLookup = null;
        _customLookup = null;
    }
}
```

- [ ] **Step 2: Verify compilation**

Run: Use MCP `assets-refresh`. Zero errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Character/Visual/AnimationProfile.cs
git commit -m "feat(archetype): add AnimationProfile ScriptableObject

Maps AnimationKey enums and custom string keys to clip names.
Each archetype references one profile to define its animation set.
Lazy-built dictionary caches for O(1) lookup."
```

---

### Task 6: CharacterArchetype ScriptableObject

**Files:**
- Create: `Assets/Scripts/Character/Archetype/CharacterArchetype.cs`

- [ ] **Step 1: Create CharacterArchetype.cs**

**Important:** The existing codebase does NOT use namespaces for Character classes. All new files must be in the global namespace to match.

```csharp
using System.Collections.Generic;
using UnityEngine;

// No namespace — matches existing Character codebase convention

/// <summary>
/// Blueprint defining what a character type is: capabilities, visuals, locomotion, AI defaults.
/// The archetype is data, not code. Runtime behavior is determined by the capability registry.
/// Archetype flags are for editor tooling and prefab validation only.
/// </summary>
[CreateAssetMenu(fileName = "New Character Archetype", menuName = "MWI/Character/Character Archetype")]
public class CharacterArchetype : ScriptableObject
{
    // ── Identity ──────────────────────────────────────────────────
    [Header("Identity")]
    [SerializeField] private string _archetypeName;
    [SerializeField] private BodyType _bodyType;

    public string ArchetypeName => _archetypeName;
    public BodyType BodyType => _bodyType;

    // ── Capability Flags (editor validation only) ─────────────────
    [Header("Capabilities (Validation Only — Registry is Runtime Truth)")]
    [SerializeField] private bool _canEnterCombat = true;
    [SerializeField] private bool _canEquipItems = true;
    [SerializeField] private bool _canDialogue = true;
    [SerializeField] private bool _canCraft = true;
    [SerializeField] private bool _hasInventory = true;
    [SerializeField] private bool _hasNeeds = true;
    [SerializeField] private bool _isTameable;
    [SerializeField] private bool _isMountable;

    public bool CanEnterCombat => _canEnterCombat;
    public bool CanEquipItems => _canEquipItems;
    public bool CanDialogue => _canDialogue;
    public bool CanCraft => _canCraft;
    public bool HasInventory => _hasInventory;
    public bool HasNeeds => _hasNeeds;
    public bool IsTameable => _isTameable;
    public bool IsMountable => _isMountable;

    // ── Locomotion ────────────────────────────────────────────────
    [Header("Locomotion")]
    [SerializeField] private MovementMode _movementModes = MovementMode.Walk | MovementMode.Run;
    [SerializeField] private float _defaultSpeed = 3.5f;
    [SerializeField] private float _runSpeed = 6f;

    public MovementMode MovementModes => _movementModes;
    public float DefaultSpeed => _defaultSpeed;
    public float RunSpeed => _runSpeed;

    // ── AI Defaults ───────────────────────────────────────────────
    [Header("AI Defaults")]
    [SerializeField] private WanderStyle _defaultWanderStyle = WanderStyle.Straight;
    [Tooltip("BT asset assigned to NPCBehaviourTree for this archetype's default behavior")]
    [SerializeField] private ScriptableObject _defaultBehaviourTree;

    public WanderStyle DefaultWanderStyle => _defaultWanderStyle;
    public ScriptableObject DefaultBehaviourTree => _defaultBehaviourTree;

    // ── Visual ────────────────────────────────────────────────────
    [Header("Visual")]
    [SerializeField] private AnimationProfile _animationProfile;
    [Tooltip("Prefab containing the visual child GO with ICharacterVisual implementation")]
    [SerializeField] private GameObject _visualPrefab;

    public AnimationProfile AnimationProfile => _animationProfile;
    public GameObject VisualPrefab => _visualPrefab;

    // ── Interaction ───────────────────────────────────────────────
    [Header("Interaction")]
    [SerializeField] private float _defaultInteractionRange = 3.5f;
    [SerializeField] private bool _isTargetable = true;

    public float DefaultInteractionRange => _defaultInteractionRange;
    public bool IsTargetable => _isTargetable;
}
```

- [ ] **Step 2: Add archetype reference to Character.cs**

In `Character.cs`, add a serialized field in the basic info section:

```csharp
[Header("Archetype")]
[SerializeField] private CharacterArchetype _archetype;
public CharacterArchetype Archetype => _archetype;
```

- [ ] **Step 3: Verify compilation**

Run: Use MCP `assets-refresh`. Zero errors.

- [ ] **Step 4: Create a default Humanoid archetype asset for testing**

Use MCP `assets-create-folder` to create `Assets/Data/Archetypes/` if it doesn't exist. Then right-click in Unity or use MCP to create a `Humanoid.asset` CharacterArchetype with default humanoid values (all capabilities true, Bipedal body type, Walk+Run movement).

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Character/Archetype/CharacterArchetype.cs Assets/Scripts/Character/Character.cs
git commit -m "feat(archetype): add CharacterArchetype ScriptableObject

Blueprint SO defining character type identity, capability flags,
locomotion, AI defaults, visual profile, and interaction settings.
Capability flags are for editor validation only — the registry is
runtime truth. Added _archetype field to Character.cs."
```

---

## Phase 2: Visual Abstraction

### Task 7: ICharacterVisual and Related Interfaces

**Files:**
- Create: `Assets/Scripts/Character/Visual/ICharacterVisual.cs`
- Create: `Assets/Scripts/Character/Visual/IAnimationLayering.cs`
- Create: `Assets/Scripts/Character/Visual/ICharacterPartCustomization.cs`
- Create: `Assets/Scripts/Character/Visual/IBoneAttachment.cs`

- [ ] **Step 1: Create ICharacterVisual.cs**

```csharp
using System;
using UnityEngine;

/// <summary>
/// Core visual contract that every character visual implementation must satisfy.
/// Decouples all gameplay systems from the rendering technology (sprites, Spine, 3D models).
/// </summary>
public interface ICharacterVisual
{
    void Initialize(Character character, CharacterArchetype archetype);
    void Cleanup();

    // Orientation
    void SetFacingDirection(float direction);

    // Base animation (Track 0 in Spine terms)
    void PlayAnimation(AnimationKey key, bool loop = true);
    void PlayAnimation(string customKey, bool loop = true);
    bool IsAnimationPlaying(AnimationKey key);

    // Physics shape
    void ConfigureCollider(Collider collider);

    // Visual feedback (via MPB, not direct material modification)
    void SetHighlight(bool active);
    void SetTint(Color color);
    void SetVisible(bool visible);

    // Animation events -> gameplay (footsteps, VFX triggers)
    event Action<string> OnAnimationEvent;
}
```

**Note:** Check if the project uses namespaces for Character classes. If not, keep interfaces in global namespace. If yes, match the convention.

- [ ] **Step 2: Create IAnimationLayering.cs**

```csharp
/// <summary>
/// Optional interface for visuals that support overlay animations on multiple tracks.
/// Maps to Spine's multi-track system (Track 0 = base, Track 1+ = overlays).
/// </summary>
public interface IAnimationLayering
{
    void PlayOverlayAnimation(AnimationKey key, int layer, bool loop = false);
    void PlayOverlayAnimation(string customKey, int layer, bool loop = false);
    void ClearOverlayAnimation(int layer);
}
```

- [ ] **Step 3: Create ICharacterPartCustomization.cs**

```csharp
using UnityEngine;

/// <summary>
/// Optional interface for visuals that support part/skin customization.
/// Covers equipment layers, dismemberment, per-part coloring, and skin combining.
/// All color changes must use Material Property Blocks to preserve batching.
/// </summary>
public interface ICharacterPartCustomization
{
    void SetPart(string slotName, string attachmentName);
    void RemovePart(string slotName);
    void SetPartColor(string slotName, Color color);
    void SetPartPalette(string slotName, Texture2D paletteLUT);

    void ApplySkinSet(string skinName);
    void CombineSkins(params string[] skinNames);
}
```

- [ ] **Step 4: Create IBoneAttachment.cs**

```csharp
using UnityEngine;

/// <summary>
/// Optional interface for visuals that support attaching GameObjects to skeleton bones.
/// Maps to Spine's BoneFollower / PointFollower system.
/// </summary>
public interface IBoneAttachment
{
    Transform GetBoneTransform(string boneName);
    void AttachToBone(string boneName, GameObject obj);
    void DetachFromBone(string boneName, GameObject obj);
}
```

- [ ] **Step 5: Verify compilation**

Run: Use MCP `assets-refresh`. Zero errors. These are just interfaces with no dependencies.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Character/Visual/
git commit -m "feat(archetype): add ICharacterVisual and related visual interfaces

ICharacterVisual (core), IAnimationLayering (overlay tracks),
ICharacterPartCustomization (skins, colors, dismemberment),
IBoneAttachment (attach objects to bones). All rendering-agnostic.
Designed for current sprites and future Spine migration."
```

---

### Task 8: Implement ICharacterVisual on Existing CharacterVisual

**Files:**
- Modify: `Assets/Scripts/Character/CharacterVisual.cs`

This is the most complex task. We wrap the existing sprite system behind the interface without changing behavior.

- [ ] **Step 1: Read CharacterVisual.cs fully**

Read `Assets/Scripts/Character/CharacterVisual.cs` to understand every method that needs to map to the interface.

- [ ] **Step 2: Add interface implementation to CharacterVisual class declaration**

Change:
```csharp
public class CharacterVisual : CharacterSystem
```
To:
```csharp
public class CharacterVisual : CharacterSystem, ICharacterVisual, IAnimationLayering
```

Note: We implement `ICharacterVisual` and `IAnimationLayering`. We do NOT implement `ICharacterPartCustomization` or `IBoneAttachment` on the current sprite visual — those are for the Spine implementation later.

- [ ] **Step 3: Implement ICharacterVisual methods**

Add these methods to `CharacterVisual.cs`. Each one delegates to existing functionality:

```csharp
// ── ICharacterVisual Implementation ──────────────────────────────

public void Initialize(Character character, CharacterArchetype archetype)
{
    // Current initialization happens in Awake + ApplyPresetFromRace.
    // This method exists for future visual implementations (Spine).
    // For the sprite visual, initialization is already handled by the existing lifecycle.
}

public void Cleanup()
{
    // Cleanup is handled by existing OnDestroy. Exists for interface compliance.
}

public void SetFacingDirection(float direction)
{
    IsFacingRight = direction >= 0f;
}

void ICharacterVisual.PlayAnimation(AnimationKey key, bool loop)
{
    if (_characterAnimator == null || _characterAnimator.Animator == null) return;
    // Map AnimationKey to existing animator behavior
    switch (key)
    {
        case AnimationKey.Idle:
            _characterAnimator.StopLocomotion();
            break;
        case AnimationKey.Attack:
            _characterAnimator.PlayMeleeAttack();
            break;
        case AnimationKey.PickUp:
            _characterAnimator.PlayPickUpItem();
            break;
        case AnimationKey.Die:
            _characterAnimator.SetDead(true);
            break;
        default:
            // Walk, Run, GetHit, Action — handled via animator parameters, not direct play
            break;
    }
}

void ICharacterVisual.PlayAnimation(string customKey, bool loop)
{
    // String-based animation not supported by sprite visual.
    // Will be implemented by SpineCharacterVisual.
    Debug.LogWarning($"[CharacterVisual] String-based animation '{customKey}' not supported by sprite visual.");
}

public bool IsAnimationPlaying(AnimationKey key)
{
    if (_characterAnimator == null || _characterAnimator.Animator == null) return false;
    var stateInfo = _characterAnimator.Animator.GetCurrentAnimatorStateInfo(0);
    return key switch
    {
        AnimationKey.Die => stateInfo.IsName("Dead"),
        _ => false // Sprite visual doesn't have per-key state tracking
    };
}

public void ConfigureCollider(Collider collider)
{
    ResizeColliderToSprite();
}

public void SetHighlight(bool active)
{
    // TODO: Implement via MPB in Spine migration. Current sprite visual has no highlight system.
}

public void SetTint(Color color)
{
    // Use existing ApplyColor for now.
    // Note: Current impl uses sr.color (tech debt - should use MPB).
    foreach (var sr in allRenderers)
    {
        if (sr != null) sr.color = color;
    }
}

public void SetVisible(bool visible)
{
    foreach (var sr in allRenderers)
    {
        if (sr != null) sr.enabled = visible;
    }
}

public event Action<string> OnAnimationEvent;

/// <summary>Called by animation events to forward to ICharacterVisual consumers.</summary>
public void RaiseAnimationEvent(string eventName)
{
    OnAnimationEvent?.Invoke(eventName);
}
```

- [ ] **Step 4: Implement IAnimationLayering methods**

```csharp
// ── IAnimationLayering Implementation ────────────────────────────

public void PlayOverlayAnimation(AnimationKey key, int layer, bool loop)
{
    // Sprite visual uses Unity Animator layers, but current setup is single-layer.
    // Overlay animations will be fully supported in Spine. For now, delegate to base play.
    ((ICharacterVisual)this).PlayAnimation(key, loop);
}

public void PlayOverlayAnimation(string customKey, int layer, bool loop)
{
    Debug.LogWarning($"[CharacterVisual] Overlay animation '{customKey}' not supported by sprite visual.");
}

public void ClearOverlayAnimation(int layer)
{
    // No-op for sprite visual. Spine will clear specific tracks.
}
```

- [ ] **Step 5: Translate French comments to English**

Replace all French comments in CharacterVisual.cs with English equivalents. Key ones:
- `// --- Look Target : cible persistante pour orienter le regard ---` → `// --- Look Target: persistent target for orientation ---`
- `// --- Anti-flicker : cooldown entre les flips ---` → `// --- Anti-flicker: cooldown between flips ---`
- `// --- Dictionnaires ---` → `// --- Dictionaries ---`
- `// On applique le scale sur le visualRoot pour éviter les conflits avec le NetworkTransform !` → `// Apply scale on visualRoot to avoid conflicts with NetworkTransform`
- `// On filtre pour exclure les ombres...` → `// Filter to exclude shadows and utility elements...`

- [ ] **Step 6: Verify compilation**

Run: Use MCP `assets-refresh`. Zero errors.

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/Character/CharacterVisual.cs
git commit -m "feat(archetype): implement ICharacterVisual on existing CharacterVisual

Wraps existing sprite visual system behind ICharacterVisual and
IAnimationLayering interfaces. All existing behavior preserved.
Sprite-specific methods remain for backward compat. Translated
French comments to English. Prepares for Spine migration."
```

---

## Phase 3: Interaction Refactor

### Task 9: Standalone InteractionOption and IInteractionProvider

**Files:**
- Create: `Assets/Scripts/Interactable/IInteractionProvider.cs`
- Modify: `Assets/Scripts/Interactable/InteractableObject.cs`
- Create: `Assets/Scripts/Interactable/InteractionOption.cs` (standalone)

- [ ] **Step 1: Read InteractableObject.cs to understand the existing InteractionOption struct**

Read `Assets/Scripts/Interactable/InteractableObject.cs`.

- [ ] **Step 2: Create standalone InteractionOption.cs**

Create a standalone class that matches the existing struct's fields:

```csharp
/// <summary>
/// Represents a single interaction option that can be presented to the player.
/// Standalone class replacing the nested struct in InteractableObject.
/// </summary>
[System.Serializable]
public class InteractionOption
{
    public string Name;
    public System.Action Action;
    public bool IsDisabled;
    public string ToggleName;

    public InteractionOption() { }

    public InteractionOption(string name, System.Action action)
    {
        Name = name;
        Action = action;
    }
}
```

- [ ] **Step 3: Migrate InteractableObject to use the standalone class**

In `InteractableObject.cs`, remove the nested `InteractionOption` struct. The return types of `GetHoldInteractionOptions` and `GetDialogueInteractionOptions` already return `List<InteractionOption>`, so they will now resolve to the standalone class.

**Note:** This changes `InteractionOption` from a struct to a class (reference semantics instead of value). All existing code uses object initializer syntax which works identically. No equality comparisons exist in the codebase.

The following files reference `InteractableObject.InteractionOption` explicitly and must be updated to use the standalone `InteractionOption`:
- `Assets/Scripts/Character/PlayerInteractionDetector.cs`
- `Assets/Scripts/UI/WorldUI/UI_InteractionMenu.cs`
- `Assets/Scripts/UI/PlayerUI.cs`

Search for `InteractableObject.InteractionOption` across the entire codebase and replace with `InteractionOption`.

- [ ] **Step 4: Create IInteractionProvider.cs**

```csharp
using System.Collections.Generic;

/// <summary>
/// Interface for character capabilities that provide interaction options.
/// Any CharacterSystem can implement this to advertise what interactions it offers.
/// CharacterInteractable collects all providers via Character.GetAll&lt;IInteractionProvider&gt;().
/// </summary>
public interface IInteractionProvider
{
    List<InteractionOption> GetInteractionOptions(Character interactor);
}
```

- [ ] **Step 5: Verify compilation**

Run: Use MCP `assets-refresh`. Check for any compilation errors from the InteractionOption migration. Fix any files that referenced the nested struct explicitly.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Interactable/
git commit -m "feat(archetype): add IInteractionProvider and standalone InteractionOption

Extracted InteractionOption from InteractableObject nested struct to
standalone class. Added IInteractionProvider interface for capabilities
to advertise interaction options. CharacterInteractable will collect
from all providers in next task."
```

---

### Task 10: Refactor CharacterInteractable to Use IInteractionProvider

**Files:**
- Modify: `Assets/Scripts/Interactable/CharacterInteractable.cs`

- [ ] **Step 1: Read CharacterInteractable.cs fully**

Read `Assets/Scripts/Interactable/CharacterInteractable.cs`. Note the hardcoded options in `GetHoldInteractionOptions` and `GetDialogueInteractionOptions`.

- [ ] **Step 2: Add provider-based collection method**

Add a new method that collects from all `IInteractionProvider` capabilities:

```csharp
/// <summary>
/// Collects interaction options from all capability providers on this character.
/// This is the new extensible approach — capabilities advertise their own options.
/// </summary>
public List<InteractionOption> GetCapabilityInteractionOptions(Character interactor)
{
    var options = new List<InteractionOption>();
    foreach (var provider in _character.GetAll<IInteractionProvider>())
    {
        var providerOptions = provider.GetInteractionOptions(interactor);
        if (providerOptions != null)
            options.AddRange(providerOptions);
    }
    return options;
}
```

- [ ] **Step 3: Integrate into existing GetHoldInteractionOptions**

Modify `GetHoldInteractionOptions` to append provider options after the existing hardcoded ones. This is the phased approach — hardcoded options stay for now, but new systems use the provider pattern:

```csharp
public override List<InteractionOption> GetHoldInteractionOptions(Character interactor)
{
    var options = new List<InteractionOption>();

    // Legacy hardcoded options (will be migrated to IInteractionProvider on each subsystem later)
    options.Add(new InteractionOption { Name = "Follow Me", Action = () => { /* existing follow logic */ } });
    options.Add(new InteractionOption { Name = "Greet", Action = () => { /* existing greet logic */ } });
    // ... keep existing party invite logic ...

    // New: collect from all capability providers
    options.AddRange(GetCapabilityInteractionOptions(interactor));

    return options;
}
```

**Important:** Keep the existing logic exactly as-is for Follow, Greet, and Party Invite. Just append the provider options at the end. The gradual migration of existing options to providers happens in Phase 5.

- [ ] **Step 4: Verify compilation**

Run: Use MCP `assets-refresh`. Zero errors.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Interactable/CharacterInteractable.cs
git commit -m "feat(archetype): CharacterInteractable collects from IInteractionProvider

Added GetCapabilityInteractionOptions() that queries all capabilities
implementing IInteractionProvider. Integrated into GetHoldInteractionOptions
alongside existing hardcoded options for phased migration."
```

### Task 10.5: Fix CharacterGameController Facade Violation

**Files:**
- Modify: `Assets/Scripts/Character/CharacterControllers/CharacterGameController.cs`

- [ ] **Step 1: Read CharacterGameController.cs**

Read `Assets/Scripts/Character/CharacterControllers/CharacterGameController.cs`. Find all direct `GetComponent<>()` calls that bypass the Character facade.

- [ ] **Step 2: Replace GetComponent calls with registry lookups**

Replace any `_character.GetComponent<NPCBehaviourTree>()` or similar facade-bypassing calls with `_character.TryGet<NPCBehaviourTree>(out var bt)`. Example:

```csharp
// Before:
var bt = _character.GetComponent<NPCBehaviourTree>();

// After:
_character.TryGet<NPCBehaviourTree>(out var bt);
```

- [ ] **Step 3: Translate any French comments encountered**

- [ ] **Step 4: Verify compilation and commit**

```bash
git add Assets/Scripts/Character/CharacterControllers/CharacterGameController.cs
git commit -m "fix(archetype): replace GetComponent facade violations with registry lookup

CharacterGameController now uses _character.TryGet<T>() instead of
direct GetComponent<T>() calls, respecting the facade pattern."
```

---

## Phase 4: Save/Load and Macro-Simulation Interfaces

### Task 11: IOfflineCatchUp and ICharacterSaveData Interfaces

**Files:**
- Create: `Assets/Scripts/Character/SaveLoad/IOfflineCatchUp.cs`
- Create: `Assets/Scripts/Character/SaveLoad/ICharacterSaveData.cs`

- [ ] **Step 1: Create SaveLoad directory if needed**

```bash
ls "Assets/Scripts/Character/SaveLoad/" || mkdir -p "Assets/Scripts/Character/SaveLoad/"
```

- [ ] **Step 2: Create IOfflineCatchUp.cs**

```csharp
/// <summary>
/// Interface for character capabilities that need offline catch-up during macro-simulation.
/// Any CharacterSystem implementing this will have its offline delta calculated by MacroSimulator
/// when a map wakes up from hibernation.
/// </summary>
public interface IOfflineCatchUp
{
    /// <summary>
    /// Calculate and apply offline state changes for the given elapsed time.
    /// Called by MacroSimulator during map wake-up catch-up.
    /// </summary>
    /// <param name="elapsedDays">Number of days elapsed since last simulation.</param>
    void CalculateOfflineDelta(float elapsedDays);
}
```

- [ ] **Step 3: Create ICharacterSaveData.cs**

```csharp
/// <summary>
/// Interface for character capabilities that have persistent state to save/load.
/// Each CharacterSystem with state implements this with its own save data type.
/// </summary>
/// <typeparam name="T">The save data struct/class for this capability.</typeparam>
public interface ICharacterSaveData<T>
{
    /// <summary>Serialize this capability's state into a save data object.</summary>
    T Serialize();

    /// <summary>Restore this capability's state from a save data object.</summary>
    void Deserialize(T data);
}
```

- [ ] **Step 4: Verify compilation**

Run: Use MCP `assets-refresh`. Zero errors.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Character/SaveLoad/
git commit -m "feat(archetype): add IOfflineCatchUp and ICharacterSaveData interfaces

IOfflineCatchUp: for capabilities needing offline macro-simulation
catch-up (needs decay, etc.). ICharacterSaveData<T>: for capabilities
with persistent state that needs save/load support."
```

---

## Phase 5: Skill Documentation

### Task 12: Create Archetype System SKILL.md

**Files:**
- Create: `.agent/skills/character-archetype/SKILL.md`

- [ ] **Step 1: Create the skill file**

```bash
mkdir -p ".agent/skills/character-archetype"
```

Write the SKILL.md documenting the entire archetype system: capability registry API, CharacterArchetype SO fields, ICharacterVisual interface, IInteractionProvider pattern, IOfflineCatchUp/ICharacterSaveData interfaces, how to create a new archetype, and how to add a new capability.

The skill file must cover:
- **Purpose:** What the archetype system does and why
- **Public API:** `Character.Register/Unregister/Get<T>/TryGet<T>/Has<T>/GetAll<T>`
- **CharacterArchetype SO:** All fields and their purposes
- **Visual interfaces:** ICharacterVisual, IAnimationLayering, ICharacterPartCustomization, IBoneAttachment
- **Interaction:** IInteractionProvider pattern
- **Save/Load:** ICharacterSaveData<T>, IOfflineCatchUp
- **How to add a new archetype:** Step-by-step guide
- **How to add a new capability:** Step-by-step guide
- **Dependencies:** What this system depends on
- **Events:** What events are fired

- [ ] **Step 2: Update character_core SKILL.md**

Read `.agent/skills/character_core/SKILL.md` and update it to reference the capability registry as the primary lookup mechanism. Note that direct `[SerializeField]` subsystem access is now a backward-compat pattern.

- [ ] **Step 3: Commit**

```bash
git add .agent/skills/character-archetype/
git commit -m "docs(archetype): add character-archetype SKILL.md

Documents capability registry API, CharacterArchetype SO, visual
interfaces, interaction provider pattern, save/load interfaces,
and step-by-step guides for adding new archetypes and capabilities."
```

---

### Task 13: Final Verification and Cleanup

- [ ] **Step 1: Full compilation check**

Use MCP `assets-refresh` and `console-get-logs` to verify zero compilation errors across the entire project.

- [ ] **Step 2: Enter Play Mode smoke test**

Enter Play Mode. Verify:
- Humanoid characters spawn and behave normally
- Characters walk, interact, enter combat
- No new errors in console
- Registry is populated (check via Debug.Log or Inspector)

- [ ] **Step 3: Clean up any temporary debug logs**

Remove any `Debug.Log` statements added during development for registry verification.

- [ ] **Step 4: Final commit**

```bash
git add -A
git commit -m "chore(archetype): final cleanup and verification

All Phase 1-4 tasks complete. Capability registry functional,
visual interfaces defined, interaction provider pattern in place,
save/load interfaces ready. Zero breaking changes to existing code."
```

---

## Summary

| Phase | Tasks | What It Achieves |
|-------|-------|-----------------|
| **Phase 1: Foundation** | Tasks 1-6 | Registry on Character, CharacterSystem auto-registers, backward-compat properties, enums, AnimationProfile SO, CharacterArchetype SO |
| **Phase 2: Visual** | Tasks 7-8 | ICharacterVisual + related interfaces defined, existing CharacterVisual wrapped |
| **Phase 3: Interaction** | Tasks 9-10.5 | IInteractionProvider, standalone InteractionOption, CharacterInteractable collects from providers, CharacterGameController facade fix |
| **Phase 4: Save/Load** | Task 11 | IOfflineCatchUp and ICharacterSaveData interfaces |
| **Phase 5: Docs** | Tasks 12-13 | SKILL.md, verification, cleanup |

**Total tasks:** 14
**Breaking changes:** Zero. All existing code continues to work unchanged.

**Intentionally deferred (not in scope for this plan):**
- **AI customization (spec Section 4):** GOAP and BT engines are already data-driven and need no code changes. New BT assets, Need definitions, and WanderStyle parameterization happen when creating specific archetypes (e.g., deer, dragon), not in this foundation plan.
- **CharacterAnimator modifications:** AnimationProfile integration on the Animator will be done during the Spine migration, not now. Current sprite visual delegates to CharacterAnimator directly.
- **French comments in Character.cs:** Will be translated when Character.cs is slimmed down in the gradual migration phase.

**Next steps after this plan:** Create first non-humanoid archetype (deer) as proof of concept, then Spine visual migration in a separate conversation.
