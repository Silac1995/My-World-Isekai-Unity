---
name: character-archetype
description: Composable character types via a Capability Registry on Character.cs. Defines CharacterArchetype SO blueprints, visual abstraction interfaces, capability-driven interactions, and per-capability save/load contracts.
---

# Character Archetype System

## 0. Purpose

The Character Archetype System replaces the monolithic `Character.cs` (with ~30 hardcoded `[SerializeField]` subsystem references) with a **slim core + capability registry** pattern. Any living entity — humanoid, quadruped, flying, aquatic, insect — is a first-class character whose "type" is defined by which subsystems are attached and enabled, not by class hierarchy.

A `CharacterArchetype` ScriptableObject defines the default loadout (capabilities, visuals, locomotion, AI) for each type. The runtime **Capability Registry** on `Character.cs` is the single source of truth for what a character can do right now.

**Key principle:** Composition over Inheritance. A deer is a Character with Movement + AI + TameableAnimal. A sapient dragon is a Character with Movement + AI + Combat + Dialogue + Needs + Hoard. Capabilities are composable and can be enabled/disabled at runtime.

## 1. When to Use This Skill

Use this skill when:
- Modifying `Character.cs` or `CharacterSystem.cs`
- Adding a new archetype (new creature type)
- Adding a new capability (new `CharacterSystem` subclass)
- Working with visual abstractions (`ICharacterVisual` and related interfaces)
- Implementing interaction providers (`IInteractionProvider`)
- Implementing per-capability save/load (`ICharacterSaveData<T>`)
- Implementing offline catch-up for macro-simulation (`IOfflineCatchUp`)
- Debugging capability lookup issues (`Get<T>`, `TryGet<T>`, `Has<T>`, `GetAll<T>`)
- Spawning characters via `CharacterFactory`
- Handling player body-switching (human player controlling a deer/dragon)

## 2. Architecture Overview

```
Character.cs (slim core)
  + Capability Registry (Dictionary<Type, CharacterSystem> + List<CharacterSystem>)
  + CharacterArchetype SO reference (blueprint)
  |
  +-- [Child GO] CharacterMovement      (always present)
  +-- [Child GO] CharacterCombat         (optional - not on passive animals)
  +-- [Child GO] CharacterEquipment      (optional - not on animals)
  +-- [Child GO] CharacterDialogue       (optional - not on non-sapient)
  +-- [Child GO] CharacterNeeds          (optional - configurable per type)
  +-- [Child GO] ICharacterVisual impl   (always present, type varies)
  +-- [Child GO] TameableAnimal          (optional - only on tameable creatures)
  +-- [Child GO] MountableAnimal         (optional - only on mounts)
  +-- [Child GO] UniqueCharacterBehaviour (optional - named/story characters)
  +-- ... (any number of additional capabilities)
```

### Slim Core Character

`Character.cs` retains only what is **universally true for every living entity**:

| Responsibility | Details |
|---|---|
| **Identity** | Name, UUID, Race/Species |
| **Network** | NetworkCharacterId, NetworkRaceId, NetworkVisualSeed |
| **Lifecycle** | Alive / Unconscious / Dead states; `Die()`, `SetUnconscious()` |
| **Capability Registry** | `Get<T>()`, `TryGet<T>()`, `Has<T>()`, `GetAll<T>()` |
| **Controller Slot** | PlayerController / NPCController switching |
| **Physics Core** | Rigidbody, Collider reference |
| **Archetype Reference** | `CharacterArchetype` SO reference |
| **Events** | `OnDeath`, `OnIncapacitated`, `OnWakeUp`, `OnCombatStateChanged` |

### Backward Compatibility

Existing facade properties remain but delegate to the registry:

```csharp
// Old code keeps working:
public CharacterCombat CharacterCombat => Get<CharacterCombat>();
public CharacterNeeds CharacterNeeds => Get<CharacterNeeds>();
// ... all ~30 existing properties, gradually deprecated
```

## 3. Capability Registry API

The registry lives on `Character.cs`. Dual storage: dictionary for O(1) concrete type lookup, list for interface queries.

```csharp
private readonly Dictionary<Type, CharacterSystem> _capabilitiesByType = new();
private readonly List<CharacterSystem> _allCapabilities = new();
```

### Methods

| Method | Behavior |
|---|---|
| `Register(CharacterSystem system)` | Adds system to both dictionary (keyed by `GetType()`) and list. Called automatically by `CharacterSystem.OnEnable()`. |
| `Unregister(CharacterSystem system)` | Removes from both. Called automatically by `CharacterSystem.OnDisable()`. |
| `T Get<T>() where T : CharacterSystem` | Exact type match via dictionary. **Throws `KeyNotFoundException`** if missing. Use when the capability is required (e.g., BattleManager needs combat). |
| `bool TryGet<T>(out T system) where T : CharacterSystem` | Exact type match via dictionary. Returns `false` if missing. Use for optional capabilities. |
| `bool Has<T>() where T : CharacterSystem` | Exact type match. Existence check only. Use for HUD/UI decisions. |
| `IEnumerable<T> GetAll<T>()` | Linear scan of list with `is T` assignability check. Use for **interface queries** (e.g., collecting all `IInteractionProvider` implementations). List is small (~10-30 entries), so linear scan is negligible. |

### Registration Lifecycle

Registration happens in `CharacterSystem.OnEnable`/`OnDisable`, NOT in `Awake`:

```csharp
// CharacterSystem base class
protected override void Awake()
{
    base.Awake();
    // _character is resolved here by existing logic — unchanged
}

protected override void OnEnable()
{
    base.OnEnable(); // existing event subscriptions (OnIncapacitated, OnDeath, etc.)
    _character?.Register(this);
}

protected override void OnDisable()
{
    _character?.Unregister(this);
    base.OnDisable(); // existing event unsubscriptions
}
```

**Ordering guarantee:** Unity calls `Awake()` before `OnEnable()` on the same object, so `_character` is always resolved before registration.

### Usage Patterns

| Scenario | Code |
|---|---|
| **Required** capability | `character.Get<CharacterCombat>()` — throws if missing (correct: non-combat entity should never be in battle) |
| **Optional** capability | `character.TryGet<CharacterDialogue>(out var dialogue)` |
| **Existence** check | `character.Has<CharacterEquipment>()` |
| **Interface query** | `character.GetAll<IInteractionProvider>()` |

### Dynamic Capabilities (Runtime Enable/Disable)

**NGO Constraint:** Netcode for GameObjects does NOT support adding/removing `NetworkBehaviour` components on a spawned `NetworkObject` at runtime. Since `CharacterSystem` extends `NetworkBehaviour`, we use the **pre-place + enable/disable pattern**:

- All *potential* capabilities are present on the prefab but start disabled.
- The registry only tracks **enabled** systems.
- Enabling/disabling a system registers/unregisters it automatically via `OnEnable`/`OnDisable`.

**Runtime examples:**
- **Learning Flight** -> server enables `FlyingLocomotion` -> `OnEnable` registers it -> `character.Has<FlyingLocomotion>()` returns `true`
- **Curse removes speech** -> server disables `CharacterDialogue` -> `OnDisable` unregisters it -> interactions update automatically
- **Shapeshifter transforms** -> server disables humanoid subsystems, enables animal ones -> entire capability set swaps

**Network sync:** Each toggleable `CharacterSystem` owns a `NetworkVariable<bool> _netEnabled` that drives its `enabled` state on clients via `OnValueChanged`.

**For truly dynamic capabilities** that no prefab variant anticipated (rare): use plain `MonoBehaviour` components that do NOT extend `CharacterSystem`/`NetworkBehaviour`. These local-only capabilities register in a separate lightweight list and cannot hold NetworkVariables.

> The archetype says "humans don't fly." The registry says "this specific human can fly right now."

## 4. CharacterArchetype ScriptableObject

The blueprint that defines what a character type *is*. It is **data, not code**.

```
CharacterArchetype (ScriptableObject)
|
+-- Identity
|   +-- ArchetypeName: string ("Humanoid", "Deer", "Dragon")
|   +-- BodyType: enum (Bipedal, Quadruped, Flying, Aquatic, Insect)
|   +-- DefaultNameGenerator: INameGenerator
|
+-- Capabilities (validation flags ONLY — not runtime truth)
|   +-- CanEnterCombat: bool
|   +-- CanEquipItems: bool
|   +-- CanDialogue: bool
|   +-- CanCraft: bool
|   +-- HasInventory: bool
|   +-- HasNeeds: bool
|   +-- IsTameable: bool
|   +-- IsMountable: bool
|
+-- Locomotion
|   +-- MovementModes: List<MovementMode> (Walk, Run, Fly, Swim, Burrow)
|   +-- DefaultSpeed, RunSpeed: float
|   +-- NavMeshAgentSettings: NavMeshAgentConfig
|
+-- AI Defaults
|   +-- DefaultBehaviourTree: BehaviourTreeAsset
|   +-- DefaultNeeds: List<NeedDefinition>
|   +-- DefaultWanderStyle: WanderStyle enum
|
+-- Visual
|   +-- VisualPrefab: GameObject (child GO with ICharacterVisual)
|   +-- AnimationProfile: AnimationProfile SO
|
+-- Interaction
    +-- DefaultInteractionRange: float
    +-- IsTargetable: bool
```

### How It Is Used

1. **Prefab creation** — each archetype has a corresponding prefab with `Character.cs` on root + all subsystem child GameObjects (enabled or disabled per archetype defaults).
2. **Runtime validation** — archetype capability flags are for **editor tooling and prefab validation only**. The registry (component presence + enabled state) is the single source of truth at runtime. If a prefab has `CharacterCombat` enabled but the archetype says `CanEnterCombat = false`, the component wins — the flag is a warning, not an override.
3. **Spawning** — `CharacterFactory` reads the archetype to instantiate the right prefab with defaults.
4. **Player switching** — `PlayerController` reads the archetype to know valid inputs. No equip button if `CanEquipItems == false`. HUD adapts based on capabilities.
5. **Individual overrides** — a named NPC can override any archetype default without needing a new archetype. The archetype is the template; the instance is the final word.

## 5. Visual Interfaces

Decouples every system from how a character looks on screen. Current sprite rig, future Spine, any rendering tech — all implement the same contracts.

### Design Principles

- **White-base coloring**: All sprites are white-colored and tinted via shaders. Implementations must use Material Property Blocks (MPB) for coloring to preserve batching.
- **Equipment layers**: Three visual layers in strict draw order: Layer 0 (Underwear, always present), Layer 1 (Clothing), Layer 2 (Armor).
- **Spine-ready**: The interface maps cleanly to Spine's track system, skin combining, slot/attachment API, and bone followers.

### ICharacterVisual — Core (every visual implements this)

```csharp
public interface ICharacterVisual
{
    void Initialize(Character character, CharacterArchetype archetype);
    void Cleanup();

    void SetFacingDirection(float direction);

    void PlayAnimation(AnimationKey key, bool loop = true);       // universal keys
    void PlayAnimation(string customKey, bool loop = true);       // archetype-specific keys
    bool IsAnimationPlaying(AnimationKey key);

    void ConfigureCollider(Collider collider);

    void SetHighlight(bool active);
    void SetTint(Color color);
    void SetVisible(bool visible);

    event Action<string> OnAnimationEvent;
}
```

### IAnimationLayering — Overlay Animations

```csharp
public interface IAnimationLayering
{
    void PlayOverlayAnimation(AnimationKey key, int layer, bool loop = false);
    void ClearOverlayAnimation(int layer);
}
```

Maps to Spine's multi-track system. Track 0 = base movement, Track 1+ = overlays (attack swing over walk, emotes, hand poses).

### ICharacterPartCustomization — Skins, Colors, Dismemberment

```csharp
public interface ICharacterPartCustomization
{
    void SetPart(string slotName, string attachmentName);
    void RemovePart(string slotName);
    void SetPartColor(string slotName, Color color);
    void SetPartPalette(string slotName, PaletteData palette);

    void ApplySkinSet(string skinName);
    void CombineSkins(params string[] skinNames);
}
```

Equipment layer refresh uses `CombineSkins(["underwear_default", clothing.SkinId, armor.SkinId])`. Partial equipment works correctly: no armor + no clothing = just underwear visible.

### IBoneAttachment — External Objects on Skeleton

```csharp
public interface IBoneAttachment
{
    Transform GetBoneTransform(string boneName);
    void AttachToBone(string boneName, GameObject obj);
    void DetachFromBone(string boneName, GameObject obj);
}
```

Maps to Spine's `BoneFollower` / `PointFollower`. Weapons follow hand bones, crowns follow head bones, particle effects follow any bone.

### AnimationKey Enum + String Overload

`AnimationKey` enum contains universal keys shared across all archetypes: `Idle`, `Walk`, `Run`, `Die`, `Attack`, `GetHit`. For archetype-specific animations (`Graze`, `Fly`, `BreathFire`), use the string-based overload: `PlayAnimation(string customKey)`.

### AnimationProfile SO

Each archetype has an `AnimationProfile` ScriptableObject that maps both enum keys and string keys to actual clip/Spine animation names:

```
AnimationKey.Walk -> "Deer_Walk_01"       (for deer archetype)
AnimationKey.Walk -> "Humanoid_Walk_01"   (for humanoid archetype)
AnimationKey.Attack -> "Dragon_Bite_01"   (for dragon archetype)
```

CharacterActions never reference clip names directly. `CharacterMeleeAttackAction` calls `visual.PlayAnimation(AnimationKey.Attack)` and gets the right animation regardless of body type.

### Spine Migration Path

1. Create `SpineCharacterVisual : MonoBehaviour, ICharacterVisual, IAnimationLayering, ICharacterPartCustomization, IBoneAttachment`
2. Internally wrap `SkeletonAnimation`, Spine skin API, `BoneFollower`
3. `AnimationProfile` maps keys to Spine animation names
4. Swap the visual prefab on the archetype SO
5. **No other system in the project changes**

Simple animals (deer) might only implement `ICharacterVisual` + `IAnimationLayering`. No part customization needed.

## 6. Interaction System

Interactions are **capability-driven** instead of hardcoded per character type.

### IInteractionProvider Interface

Any `CharacterSystem` can advertise interaction options:

```csharp
public interface IInteractionProvider
{
    List<InteractionOption> GetInteractionOptions(Character interactor);
}
```

### CharacterInteractable — The Collector

`CharacterInteractable` remains an `InteractableObject` (MonoBehaviour). It does NOT implement `IInteractionProvider`. It collects from all capability providers:

```csharp
public List<InteractionOption> GetAllOptions(Character interactor)
{
    var options = new List<InteractionOption>();
    foreach (var provider in _character.GetAll<IInteractionProvider>())
        options.AddRange(provider.GetInteractionOptions(interactor));
    return options;
}
```

### What Each Capability Provides

| Capability (implements IInteractionProvider) | Options | Conditions |
|---|---|---|
| `CharacterDialogue` | Talk, Greet, Insult, Goodbye | Character has dialogue capability |
| `CharacterParty` | Invite to Party | Both characters are party-capable |
| `CharacterTrading` | Trade | Character has inventory + trading |
| `TameableAnimal` | Pet, Tame, Feed | Tame only if interactor has taming skill |
| `MountableAnimal` | Mount, Dismount | Character is mountable |
| `UniqueCharacterBehaviour` | Story-specific options | Based on story flags |

### Context-Sensitive Options

`GetInteractionOptions` receives the interactor, enabling conditional logic (e.g., `TameableAnimal` only shows "Tame" if the interactor has `CharacterTamingSkill`).

### HUD Integration

Player right-clicks a character -> HUD collects all `InteractionOption` objects -> renders as menu. The HUD does not know character types exist. New types with new interactions appear automatically.

## 7. Save/Load

Per CLAUDE.md rule 20, characters are serializable as independent local files.

### ICharacterSaveData<T> — Per-Capability Serialization

Each `CharacterSystem` that has persistent state implements:

```csharp
public interface ICharacterSaveData<T>
{
    T Serialize();
    void Deserialize(T data);
}
```

### CharacterSaveData Structure

```
CharacterSaveData
{
    CharacterId: GUID
    ArchetypeId: string              // links to CharacterArchetype SO
    Name: string
    IsAlive: bool
    IsUnconscious: bool

    // Capability states (nullable/optional per capability)
    CombatData?: CharacterCombatSaveData
    NeedsData?: CharacterNeedsSaveData
    EquipmentData?: CharacterEquipmentSaveData
    ...

    // Dynamic capability overrides (relative to archetype defaults)
    EnabledCapabilities: List<string>
    DisabledCapabilities: List<string>
}
```

**Load sequence:** spawn archetype prefab -> apply enable/disable overrides -> deserialize each subsystem's state.

### IOfflineCatchUp — Macro-Simulation Catch-Up

Per CLAUDE.md rule 29, any NPC stat/need/behavior that changes over time must have a corresponding offline catch-up formula:

```csharp
public interface IOfflineCatchUp
{
    void CalculateOfflineDelta(float elapsedDays);
}
```

`HibernatedNPCData` stores the archetype ID so `MacroSimulator` knows which formulas to run.

**Rules:**
- Every new Need type must implement `IOfflineCatchUp`
- Every new archetype that can hold a job must register `JobYieldRecipe` entries in `JobYieldRegistry`
- Animal archetypes without jobs skip the Inventory Yields step entirely

## 8. How to Add a New Archetype (Step-by-Step)

1. **Create the CharacterArchetype SO** — duplicate an existing one, set identity (name, BodyType), capability flags, locomotion, AI defaults, visual prefab, animation profile.
2. **Create the prefab** — root GO with `Character.cs` + all subsystem child GOs. Enable only the capabilities this archetype supports. Pre-place (disabled) any capabilities that could be dynamically enabled later.
3. **Create the AnimationProfile SO** — map `AnimationKey` entries and any custom string keys to actual clip/Spine animation names.
4. **Create the ICharacterVisual implementation** (if needed) — or reuse an existing one (e.g., `SpriteCharacterVisual`). Assign as the visual prefab on the archetype SO.
5. **Create BT asset** — define the behaviour tree for this archetype's AI. Assign on the archetype SO.
6. **Define Need components** (if applicable) — create new `CharacterSystem` subclasses for unique needs (e.g., `NeedTerritory` for dragons). Each need implements `IOfflineCatchUp`.
7. **Register in NetworkPrefabs** — add the prefab to NGO's `NetworkPrefabs` list at build time.
8. **Register in CharacterFactory** — ensure the factory can spawn this archetype by ID.
9. **Add JobYieldRecipe entries** (if applicable) — register in `JobYieldRegistry` for macro-simulation.
10. **Test all scenarios** — Host spawns, Client sees correct visuals, NPC AI runs, interaction options appear correctly, save/load round-trips.

## 9. How to Add a New Capability (Step-by-Step)

1. **Create the CharacterSystem subclass** — one script on its own child GameObject under the Character hierarchy. Keep a reference to `_character` (resolved in `Awake()`).
2. **Registration is automatic** — `OnEnable` calls `_character.Register(this)`, `OnDisable` calls `_character.Unregister(this)` (inherited from `CharacterSystem` base class).
3. **Implement IInteractionProvider** (if applicable) — if this capability provides interaction options, implement the interface and return options from `GetInteractionOptions`.
4. **Implement ICharacterSaveData<T>** (if has persistent state) — define a serializable data struct and implement `Serialize()`/`Deserialize()`.
5. **Implement IOfflineCatchUp** (if time-dependent) — provide a `CalculateOfflineDelta(float elapsedDays)` method.
6. **Add NetworkVariable<bool> _netEnabled** (if toggleable at runtime) — server sets it, clients react via `OnValueChanged` to enable/disable the component.
7. **Pre-place on relevant prefabs** — add the component (disabled if optional) to all archetype prefabs that might use it.
8. **Update CharacterArchetype SO** (if adding a new capability flag) — add a validation bool.
9. **Write/update SKILL.md** — document the new capability in `.agent/skills/`.
10. **Test** — verify Host<->Client, Client<->Client, Host/Client<->NPC scenarios.

## 10. Key File Locations

| File | Purpose |
|---|---|
| `Assets/Scripts/Character/Character.cs` | Slim core + capability registry |
| `Assets/Scripts/Character/CharacterSystem.cs` | Base class with auto-registration |
| `Assets/Scripts/Character/CharacterArchetype.cs` | ScriptableObject blueprint |
| `Assets/Scripts/Character/Visual/ICharacterVisual.cs` | Core visual interface |
| `Assets/Scripts/Character/Visual/IAnimationLayering.cs` | Overlay animation interface |
| `Assets/Scripts/Character/Visual/ICharacterPartCustomization.cs` | Part/skin/color interface |
| `Assets/Scripts/Character/Visual/IBoneAttachment.cs` | Bone follower interface |
| `Assets/Scripts/Character/Visual/AnimationKey.cs` | Semantic animation enum |
| `Assets/Scripts/Character/Visual/AnimationProfile.cs` | Key-to-clip mapping SO |
| `Assets/Scripts/Character/Interaction/IInteractionProvider.cs` | Interaction contribution interface |
| `Assets/Scripts/Character/Interaction/InteractionOption.cs` | Standalone interaction option class |
| `Assets/Scripts/Character/SaveLoad/ICharacterSaveData.cs` | Per-capability serialization interface |
| `Assets/Scripts/Character/SaveLoad/IOfflineCatchUp.cs` | Macro-simulation catch-up interface |
| `Assets/Scripts/Character/Enums/BodyType.cs` | Body type enum |
| `Assets/Scripts/Character/Enums/MovementMode.cs` | Movement mode enum |
| `Assets/Scripts/Character/Enums/WanderStyle.cs` | Wander style enum |

## 11. Dependencies

| Depends On | Why |
|---|---|
| `character_core` | Character.cs is the host of the registry; CharacterSystem is the base class |
| `multiplayer` / `network-troubleshooting` | NGO constraints (pre-place + enable/disable, NetworkVariable sync) |
| `world-system` | MacroSimulator integration, HibernatedNPCData, BiomeDefinition |
| `save-load-system` | ICharacterSaveData contract, character file serialization |
| `interactable-system` | CharacterInteractable collects from IInteractionProvider |
| `goap` / `behaviour_tree` | AI customization is data-driven through archetype SO |
| `character_needs` | Need definitions are per-archetype; each implements IOfflineCatchUp |
| `item_system` | Equipment visuals feed into ICharacterPartCustomization |

## 12. Golden Rules

1. **The registry is runtime truth.** Never check archetype capability flags to decide if a character can do something at runtime. Always use `Has<T>()`, `TryGet<T>()`, or `Get<T>()`.
2. **Archetype flags are validation only.** They exist for editor tooling, prefab validation, and HUD hints. They do NOT override the registry.
3. **No direct cross-system calls.** A CharacterSystem must never cache or call another CharacterSystem directly. All cross-system communication goes through `Character` (the facade) or `[SerializeField]` inspector links.
4. **Pre-place + Enable/Disable for NGO.** Never `AddComponent<T>()` or `Destroy()` a NetworkBehaviour on a spawned NetworkObject. All potential capabilities are pre-placed on the prefab and toggled via `enabled`.
5. **No namespaces.** This project does not use C# namespaces (consistent with existing codebase convention).
6. **Underscore prefix for privates.** All private fields use `_camelCase` (e.g., `_capabilitiesByType`).
7. **Events and coroutine cleanup in OnDestroy.** Always unsubscribe from events and stop coroutines (CLAUDE.md rule 16).
8. **Every gameplay effect through CharacterAction.** Player HUDs only queue actions; never implement gameplay logic directly in a player-only manager (CLAUDE.md rule 22).
9. **Visual implementations use MPB.** Never use `sr.color` or direct material modification. Always use Material Property Blocks to preserve batching (CLAUDE.md rule 25).
10. **Every new capability gets a SKILL.md.** No capability ships without its documentation being current (CLAUDE.md rule 21/28).
