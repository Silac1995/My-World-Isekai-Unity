# Character Archetype System Design

**Date:** 2026-03-30
**Branch:** `feature/character-archetype-system`
**Status:** Approved Design

## Problem Statement

The game currently has a single monolithic `Character.cs` class with ~30 hardcoded `[SerializeField]` subsystem references. Every character — humanoid NPC, player, animal — must use the same class with the same subsystems. This makes it impossible to support radically different character types (deer, dragons, birds, flies) that have completely different capability sets, visuals, AI behaviors, and interaction options.

Additionally, the visual layer is tightly coupled to a humanoid sprite rig (CharacterBodyPartsController, HandsController, EyesController), blocking the planned migration to Spine 2D.

### Requirements

1. **Any character type** — humanoid, quadruped, flying, aquatic, insect — must be a first-class entity in the game.
2. **Players can BE any type** — a player can control a deer, a dragon, a human. PlayerController/NPCController switching works identically regardless of body type.
3. **Capabilities are composable** — a deer has Movement + AI but no Equipment or Dialogue. A sapient dragon has Movement + AI + Combat + Dialogue + Needs. Capabilities can be added/removed at runtime (e.g., learning a Flight skill).
4. **Visual layer is decoupled** — current sprite system and future Spine system both implement the same abstraction. No gameplay system knows about the rendering tech.
5. **AI is data-driven** — different character types have different behaviors through data (BT assets, Need definitions, blackboard parameters), not through subclassing GOAP or BT controllers.
6. **Interactions are capability-driven** — what you can do with a character depends on what capabilities it has, not hardcoded lists.
7. **Backward compatible** — existing code using `character.CharacterCombat` etc. keeps working during a phased migration.

---

## Architecture Overview

### Approach: Capability Registry (Composition over Inheritance)

`Character.cs` becomes a slim core. Subsystems register themselves into a runtime dictionary. Character "type" is defined by which subsystems are attached to the GameObject, not by class hierarchy. A `CharacterArchetype` ScriptableObject defines the default loadout for each type.

```
Character (slim core)
  + CharacterCapabilities (registry)
  + CharacterArchetype SO (blueprint)
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

---

## Section 1: Slim Core Character

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

Everything else is an optional capability discovered through the registry.

### Backward Compatibility

Existing properties remain but delegate to the registry:

```csharp
// Old code keeps working:
public CharacterCombat CharacterCombat => Get<CharacterCombat>();
public CharacterNeeds CharacterNeeds => Get<CharacterNeeds>();
// ... all ~30 existing properties, gradually deprecated
```

---

## Section 2: Capability Registry

The registry lives on `Character.cs` and is the single source of truth for what a character can do **right now**.

### API

```csharp
// Dual storage: dictionary for O(1) concrete type lookup, list for interface queries
private readonly Dictionary<Type, CharacterSystem> _capabilitiesByType = new();
private readonly List<CharacterSystem> _allCapabilities = new();

public void Register(CharacterSystem system);
public void Unregister(CharacterSystem system);

public T Get<T>() where T : CharacterSystem;              // exact type match, throws KeyNotFoundException if missing
public bool TryGet<T>(out T system) where T : CharacterSystem;  // exact type match, safe lookup
public bool Has<T>() where T : CharacterSystem;            // exact type match, existence check
public IEnumerable<T> GetAll<T>();                         // linear scan with `is T` check, for interface queries
```

**Lookup semantics:** `Get<T>()`, `TryGet<T>()`, and `Has<T>()` use **exact type match** via the dictionary. `GetAll<T>()` scans the list with `is T` assignability check — this is how multiple systems implementing `IInteractionProvider` are collected. The list is small (~10-30 entries), so linear scan is negligible.

### Registration

`CharacterSystem.Awake()` already resolves `_character` (this is unchanged). Registration moves to `OnEnable`/`OnDisable` to support the pre-place + enable/disable pattern for dynamic capabilities:

```csharp
// Awake() still resolves _character as it does today — unchanged
protected override void Awake()
{
    base.Awake();
    // _character is resolved here by existing CharacterSystem logic
}

// Registration happens in OnEnable/OnDisable (runs after Awake on same object)
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

**Ordering guarantee:** Unity calls `Awake()` before `OnEnable()` on the same object, so `_character` is always resolved before registration. The existing event subscriptions in `CharacterSystem.OnEnable/OnDisable` are preserved via `base` calls.

Zero changes to existing subsystem logic. They just announce themselves. Disabled subsystems are not registered.

### Usage Patterns

| Scenario | Code |
|---|---|
| **Required** capability (e.g., BattleManager needs combat) | `character.Get<CharacterCombat>()` — throws if missing (correct: non-combat entity should never be in battle) |
| **Optional** capability (e.g., interaction checks for dialogue) | `character.TryGet<CharacterDialogue>(out var dialogue)` |
| **Existence** check (e.g., HUD deciding which panels to show) | `character.Has<CharacterEquipment>()` |
| **Interface query** (e.g., collecting interaction providers) | `character.GetAll<IInteractionProvider>()` |

### Dynamic Capabilities (Runtime Enable/Disable)

The archetype defines the **default loadout**. The registry is the **live truth**.

**NGO Constraint:** Netcode for GameObjects does NOT support adding or removing `NetworkBehaviour` components on a spawned `NetworkObject` at runtime. Since `CharacterSystem` extends `NetworkBehaviour`, we cannot use `AddComponent<T>()` / `Destroy()` for dynamic capabilities on networked characters.

**Solution: Pre-place + Enable/Disable pattern.** All *potential* capabilities are present on the prefab but start disabled. The registry only tracks **enabled** systems. Enabling/disabling a system registers/unregisters it from the registry:

```csharp
// CharacterSystem
protected override void OnEnable()
{
    base.OnEnable();
    _character?.Register(this);
}

protected override void OnDisable()
{
    _character?.Unregister(this);
    base.OnDisable();
}
```

**Runtime examples:**
- **Learning Flight skill** -> server enables `FlyingLocomotion` component (was pre-placed, disabled) -> `OnEnable` registers it -> `character.Has<FlyingLocomotion>()` returns `true` -> HUD shows fly toggle
- **Curse removes speech** -> server disables `CharacterDialogue` -> `OnDisable` unregisters it -> interaction options update automatically
- **Shapeshifter transforms** -> server disables humanoid subsystems, enables animal ones -> entire capability set swaps

**For truly dynamic capabilities that no prefab variant anticipated** (very rare edge cases), the solution is plain `MonoBehaviour` components that do NOT extend `CharacterSystem`/`NetworkBehaviour`. These local-only capabilities register in a separate lightweight list and cannot hold NetworkVariables.

The archetype says "humans don't fly." The registry says "this specific human can fly right now."

---

## Section 3: CharacterArchetype ScriptableObject

The blueprint that defines what a character type *is* — capabilities, visuals, locomotion, AI defaults. It is **data, not code**.

```
CharacterArchetype (ScriptableObject)
|
+-- Identity
|   +-- ArchetypeName: string ("Humanoid", "Deer", "Dragon")
|   +-- BodyType: enum (Bipedal, Quadruped, Flying, Aquatic, Insect)
|   +-- DefaultNameGenerator: INameGenerator
|
+-- Capabilities (which subsystems this type supports)
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

1. **Prefab creation** — each archetype has a corresponding prefab. The prefab has `Character.cs` on root + all subsystem child GameObjects (enabled or disabled per archetype defaults).
2. **Runtime validation** — the archetype capability flags are for **editor tooling and prefab validation only**. At runtime, the capability registry (component presence + enabled state) is the single source of truth. If a prefab has `CharacterCombat` enabled but the archetype says `CanEnterCombat = false`, the component wins — the archetype flag is a warning, not an override.
3. **Spawning** — `CharacterFactory` reads the archetype to instantiate the right prefab with defaults.
4. **Player switching** — when a player takes control of a dragon, `PlayerController` reads the archetype to know valid inputs. No equip button if `CanEquipItems == false`. HUD adapts based on capabilities.
5. **Individual overrides** — a specific named NPC can override any archetype default (custom BT, custom needs, custom interactions) without needing a new archetype. The archetype is the template; the instance is the final word.

---

## Section 4: AI Customization Strategy

The GOAP and Behaviour Tree controllers are **generic engines**. Customization happens at the data/configuration layer, never through subclassing.

### GOAP — Need-Driven Composition

Different character types have different Needs. Each Need provides its own goals and actions:

| Humanoid Needs | Deer Needs | Dragon Needs |
|---|---|---|
| NeedSocial | NeedFood | NeedFood |
| NeedJob | NeedSafety | NeedTerritory |
| NeedToWearClothing | NeedRest | NeedHoard |
| NeedFood | | NeedDominance |

The `CharacterGoapController` doesn't change — it asks "what are my needs?" and each Need generates goals + available actions. A deer's `NeedSafety` generates a `FleeFromThreat` goal with a `RunAwayAction`. The planner handles the rest.

**New character type = new Need components + new CharacterActions. Zero changes to the GOAP engine.**

### Behaviour Trees — Data-Driven

`NPCBehaviourTree` runs whichever BT asset is assigned:

- Humanoid NPC -> `HumanoidBT.asset` (wander, socialize, work, fight)
- Deer -> `DeerBT.asset` (graze, flee, herd)
- Dragon -> `DragonBT.asset` (patrol territory, hoard, hunt, parley)
- Named character -> `Aldric_TheDragonKing_BT.asset` (unique story beats)

### Behavioral Variation Within a Type

Two humans can behave differently without different BT assets. The BT reads blackboard parameters:

```
WanderStyle.Straight   -> pick point, move directly
WanderStyle.ZigZag     -> pick point, generate waypoints
WanderStyle.Patrol     -> follow predefined route
WanderStyle.Nervous    -> short bursts, frequent direction changes
```

**Option A (different BT assets)** for fundamentally different behavior loops (deer vs human).
**Option B (parameterized blackboard)** for variations within a type (two humans that wander differently).
Both work simultaneously.

### Unique Characters

A main character like "Aldric the Dragon King" gets:

1. A unique BT asset with story-specific nodes
2. A `UniqueCharacterBehaviour` CharacterSystem component for scripted sequences — cutscene triggers, unique dialogue conditions, phase transitions (e.g., "below 50% HP, switch to enraged BT")
3. Unique CharacterActions — e.g., `DragonBreathAction`, `ShapeShiftAction`

Note: Unique CharacterActions (e.g., `DragonBreathAction`, `ShapeShiftAction`) are plain C# classes, not CharacterSystem components — they cannot register in the capability registry. Instead, the `UniqueCharacterBehaviour` CharacterSystem component is what registers in the registry, and it *holds* the unique actions and exposes them to the action system.

These are capabilities registered in the same registry. `character.TryGet<UniqueCharacterBehaviour>()` lets any system check for unique behavior without coupling.

---

## Section 5: ICharacterVisual — Visual Abstraction

Decouples every system from how a character looks on screen. Current sprite rig, future Spine, any rendering tech — all implement the same contracts.

### Design Principles

- **White-base coloring**: All character sprites (except some main characters) are white-colored and tinted via shaders. Implementations must use Material Property Blocks (MPB) for coloring to preserve batching.
- **Equipment layers**: Three visual layers in strict draw order:
  - Layer 0 (bottom): **Underwear** — always present
  - Layer 1 (middle): **Clothing** — tunic, pants, cloak
  - Layer 2 (top): **Armor** — chestplate, helm, gauntlets
- **Spine-ready**: The interface is designed to map cleanly to Spine's track system, skin combining, slot/attachment API, and bone followers.

### Core Interface — Every Visual Implements This

```csharp
public interface ICharacterVisual
{
    void Initialize(Character character, CharacterArchetype archetype);
    void Cleanup();

    // Orientation
    void SetFacingDirection(float direction);

    // Base animation (Track 0 in Spine terms)
    void PlayAnimation(AnimationKey key, bool loop = true);       // universal keys (Idle, Walk, Attack, Die)
    void PlayAnimation(string customKey, bool loop = true);       // archetype-specific keys ("Graze", "Fly", "BreathFire")
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

### Animation Layering — Overlay Animations

```csharp
public interface IAnimationLayering
{
    void PlayOverlayAnimation(AnimationKey key, int layer, bool loop = false);
    void ClearOverlayAnimation(int layer);
}
```

Maps to Spine's multi-track system. Track 0 = base movement, Track 1+ = overlays (attack swing over walk, emotes, hand poses).

### Part Customization — Skins, Colors, Dismemberment

```csharp
public interface ICharacterPartCustomization
{
    // Individual part management
    void SetPart(string slotName, string attachmentName);
    void RemovePart(string slotName);                       // unequip or dismember
    void SetPartColor(string slotName, Color color);        // per-part tinting via MPB
    void SetPartPalette(string slotName, PaletteData palette); // full palette swap (LUT)

    // Full skin management
    void ApplySkinSet(string skinName);
    void CombineSkins(params string[] skinNames);           // Spine skin combining
}
```

**Equipment layer refresh** using skin combining:

```csharp
void RefreshEquipmentVisual()
{
    var skins = new List<string>();
    skins.Add("underwear_default");                      // Layer 0 -- always present

    if (clothing != null) skins.Add(clothing.SkinId);    // Layer 1
    if (armor != null) skins.Add(armor.SkinId);          // Layer 2

    // Order determines draw priority: later skins overlay earlier ones
    if (visual is ICharacterPartCustomization parts)
        parts.CombineSkins(skins.ToArray());
}
```

Partial equipment works correctly: no armor + no clothing = just underwear visible.

### Bone Attachment — External Objects on Skeleton

```csharp
public interface IBoneAttachment
{
    Transform GetBoneTransform(string boneName);
    void AttachToBone(string boneName, GameObject obj);
    void DetachFromBone(string boneName, GameObject obj);
}
```

Maps to Spine's `BoneFollower` / `PointFollower`. Weapons follow hand bones, crowns follow head bones, particle effects follow any bone.

### AnimationProfile SO — Semantic Key-to-Clip Mapping

Each archetype has an `AnimationProfile` ScriptableObject:

```
AnimationKey.Walk    -> "Deer_Walk_01"       (for deer)
AnimationKey.Walk    -> "Humanoid_Walk_01"   (for humanoid)
AnimationKey.Attack  -> "Dragon_Bite_01"     (for dragon)
AnimationKey.Idle    -> "Bird_Perch_01"      (for bird)
```

CharacterActions never reference clip names directly. `CharacterMeleeAttackAction` calls `visual.PlayAnimation(AnimationKey.Attack)` and gets the right animation regardless of body type.

**AnimationKey extensibility:** The `AnimationKey` enum contains universal keys shared across all archetypes (Idle, Walk, Run, Die, Attack, GetHit). For archetype-specific animations (Graze, Fly, BreathFire), use a string-based overload: `PlayAnimation(string customKey)`. The `AnimationProfile` SO maps both enum keys and string keys to actual clip/Spine animation names. This keeps common animations type-safe while allowing open-ended extension.

### Spine Migration Path

When implementing Spine:

1. Create `SpineCharacterVisual : MonoBehaviour, ICharacterVisual, IAnimationLayering, ICharacterPartCustomization, IBoneAttachment`
2. Internally wraps `SkeletonAnimation`, Spine skin API, `BoneFollower`
3. `AnimationProfile` maps keys to Spine animation names
4. Swap the visual prefab on the archetype SO
5. **No other system in the project changes**

Simple animals (deer) might only implement `ICharacterVisual` + `IAnimationLayering`. No part customization needed.

---

## Section 6: Interaction Abstraction

Interactions become **capability-driven** instead of hardcoded per character type.

### IInteractionProvider Interface

Any CharacterSystem can advertise interaction options:

```csharp
public interface IInteractionProvider
{
    List<InteractionOption> GetInteractionOptions(Character interactor);
}
```

### Collection by CharacterInteractable

`CharacterInteractable` remains an `InteractableObject` (MonoBehaviour) — it is the **collector**, not a provider. It does NOT implement `IInteractionProvider`. Its current hardcoded methods (`GetHoldInteractionOptions`, `GetDialogueInteractionOptions`) are replaced by delegation to capability providers. The existing `InteractableObject.InteractionOption` nested struct is migrated to a standalone `InteractionOption` class.

```csharp
// CharacterInteractable -- no longer hardcoded
public List<InteractionOption> GetAllOptions(Character interactor)
{
    var options = new List<InteractionOption>();

    foreach (var provider in _character.GetAll<IInteractionProvider>())
        options.AddRange(provider.GetInteractionOptions(interactor));

    return options;
}
```

### What Each System Provides

| Capability implementing IInteractionProvider | Options provided | Conditions |
|---|---|---|
| `CharacterDialogue` | Talk, Greet, Insult, Goodbye | Character has dialogue capability |
| `CharacterParty` | Invite to Party | Both characters are party-capable |
| `CharacterTrading` | Trade | Character has inventory + trading |
| `TameableAnimal` | Pet, Tame, Feed | Tame only if interactor has taming skill |
| `MountableAnimal` | Mount, Dismount | Character is mountable |
| `UniqueCharacterBehaviour` | Story-specific options | Based on story flags |

### Context-Sensitive Options

`GetInteractionOptions` receives the interactor, enabling conditional logic:

```csharp
// TameableAnimal.cs implements IInteractionProvider
public List<InteractionOption> GetInteractionOptions(Character interactor)
{
    var options = new List<InteractionOption>();

    if (_isTamed && _owner == interactor)
        options.Add(new InteractionOption("Command", CommandAction));
    else if (!_isTamed && interactor.Has<CharacterTamingSkill>())
        options.Add(new InteractionOption("Tame", TameAction));

    options.Add(new InteractionOption("Pet", PetAction));
    return options;
}
```

### Per Character Type Result

- **Humanoid NPC**: Talk, Greet, Trade, Party Invite (from Dialogue + Party + Trading)
- **Deer**: Pet, Tame (from TameableAnimal, no Dialogue/Trading subsystems exist)
- **Tamed Wolf**: Pet, Feed, Command (from TameableAnimal + CompanionAI)
- **Sapient Dragon**: Parley, Trade, Challenge (from Dialogue + Trading + UniqueBehaviour)
- **Feral Dragon**: nothing or "Observe" (minimal IInteractionProvider)

### HUD Integration

Player right-clicks a character -> HUD collects all `InteractionOption` objects -> renders as menu. The HUD doesn't know character types exist. New types with new interactions appear automatically.

---

## Section 7: Macro-Simulation & Hibernation Integration

Per CLAUDE.md rule 29, any NPC stat/need/behavior that changes over time must have a corresponding offline catch-up formula in `MacroSimulator`.

### Archetype-Aware Catch-Up

When a map hibernates, all NPCs are serialized into `HibernatedNPCData`. On wake-up, `MacroSimulator` runs catch-up. The archetype determines **which catch-up formulas apply**:

| Catch-Up Step | Humanoid | Deer | Dragon |
|---|---|---|---|
| Resource Pool Regeneration | Yes | N/A | N/A |
| Inventory Yields (JobYieldRegistry) | Yes (per job) | N/A | Yes (hoard growth) |
| Needs Decay | NeedSocial, NeedJob, NeedFood | NeedFood, NeedSafety, NeedRest | NeedFood, NeedTerritory |
| Position Snap | Last known + wander delta | Herd center + wander delta | Lair position |

**Rules for new archetypes:**
- Every new Need type must implement `IOfflineCatchUp` with a `CalculateOfflineDelta(float elapsedDays)` method
- Every new archetype that can hold a job must register `JobYieldRecipe` entries in `JobYieldRegistry`
- Animal archetypes without jobs skip the Inventory Yields step entirely
- `HibernatedNPCData` stores the archetype ID so `MacroSimulator` knows which formulas to run

### New Resource/Harvestable Types

Animal archetypes may interact with resources differently (deer graze, dragons hoard). Any new resource type must be registered in `BiomeDefinition` with a `ResourcePoolEntry`.

---

## Section 8: Save/Load & ICharacterData Integration

Per CLAUDE.md rule 20, characters must be serializable as independent local files.

### Archetype Persistence

```
CharacterSaveData
{
    CharacterId: GUID
    ArchetypeId: string              // "Humanoid", "Deer", "Dragon" — links to CharacterArchetype SO
    Name: string

    // Core state
    IsAlive: bool
    IsUnconscious: bool

    // Capability states (only for capabilities that exist on this archetype)
    CombatData?: CharacterCombatSaveData
    NeedsData?: CharacterNeedsSaveData
    EquipmentData?: CharacterEquipmentSaveData
    InventoryData?: CharacterInventorySaveData
    ... (nullable/optional per capability)

    // Dynamic capability overrides
    EnabledCapabilities: List<string>    // capabilities enabled beyond archetype defaults
    DisabledCapabilities: List<string>   // capabilities disabled from archetype defaults
}
```

**Key rules:**
- `ArchetypeId` is always saved — it determines which prefab to spawn on load
- Each CharacterSystem that has persistent state implements `ICharacterSaveData<T>` with `Serialize()` / `Deserialize()` methods
- Dynamic capability changes (e.g., learned Flight) are persisted as enable/disable overrides relative to the archetype default
- On load: spawn archetype prefab -> apply enable/disable overrides -> deserialize each subsystem's state
- The save format is archetype-agnostic: a deer save file only contains the fields relevant to deer capabilities (no empty Equipment/Dialogue blocks)

### Player Body-Switching Persistence

When a player takes control of a different body type:
- The player's "soul" data (account ID, player preferences) is separate from character data
- The current character is saved to its own file
- The new character is loaded from its file
- Player ownership transfers to the new NetworkObject

---

## Network Considerations

All existing network rules from `NETWORK_ARCHITECTURE.md` apply:

1. **Capability registration is local** — the registry is built on each client from the spawned prefab's components. No need to sync the registry itself.
2. **CharacterArchetype determines the prefab** — server spawns the correct NetworkObject prefab, all clients instantiate the same components. Each archetype prefab must be registered in NGO's `NetworkPrefabs` list at build time.
3. **Dynamic capability enable/disable** is server-authoritative — each toggleable `CharacterSystem` owns a `NetworkVariable<bool> _netEnabled` that drives its `enabled` state on clients via `OnValueChanged`. This keeps sync co-located with the capability it controls and avoids a centralized bitmask that would couple Character to all possible capabilities.
4. **ICharacterVisual state** (skin, colors, equipment layers) synced via existing NetworkVariables on equipment/appearance systems. Visual implementations read synced state and apply locally. The `_netIsFacingRight` currently on `CharacterVisual` migrates to the `ICharacterVisual` implementation (it remains a `CharacterSystem` / `NetworkBehaviour`, it just also implements the interface).
5. **Player body-switching** (human takes control of a deer) requires server authority: (a) save current character state, (b) despawn old NetworkObject, (c) spawn new archetype prefab, (d) transfer ownership to player, (e) deserialize character state onto new object. All client references to the old Character become null — systems must handle this gracefully via `OnNetworkDespawn` cleanup.
6. All three scenarios validated: Host<->Client, Client<->Client, Host/Client<->NPC.

---

## Migration Strategy

### Phase 1: Foundation (Non-Breaking)
- Add capability registry to `Character.cs`
- Add `Register()`/`Unregister()` calls to `CharacterSystem`
- Add backward-compat properties delegating to registry
- Create `CharacterArchetype` SO structure
- **Zero breaking changes. All existing code works.**

### Phase 2: Visual Abstraction
- Define `ICharacterVisual`, `IAnimationLayering`, `ICharacterPartCustomization`, `IBoneAttachment`
- Wrap current sprite system in a `SpriteCharacterVisual` implementing these interfaces
- Decouple CharacterAnimator from hardcoded humanoid assumptions
- **Prepares for Spine migration without doing it yet.**

### Phase 3: Interaction Refactor
- Define `IInteractionProvider`
- Move hardcoded interaction options into respective subsystems
- Refactor `CharacterInteractable` to collect from providers
- **Existing interactions keep working, now extensible.**

### Phase 4: First Non-Humanoid Archetype
- Create a simple animal archetype (deer) as proof of concept
- Define archetype SO, prefab, BT asset, need definitions
- Validate the entire pipeline end-to-end

### Phase 5: Gradual Migration
- Migrate call sites from `character.CharacterCombat` to `character.TryGet<CharacterCombat>()`
- Remove deprecated properties as all call sites are updated
- Add new archetypes as needed

---

## Files Affected

### New Files
- `CharacterArchetype.cs` — ScriptableObject
- `ICharacterVisual.cs` — core visual interface
- `IAnimationLayering.cs` — overlay animation interface
- `ICharacterPartCustomization.cs` — part/skin/color interface
- `IBoneAttachment.cs` — bone follower interface
- `IInteractionProvider.cs` — interaction contribution interface
- `IOfflineCatchUp.cs` — macro-simulation catch-up interface for needs/capabilities
- `ICharacterSaveData.cs` — per-capability serialization interface
- `AnimationKey.cs` — semantic animation enum
- `AnimationProfile.cs` — key-to-clip mapping SO
- `InteractionOption.cs` — standalone interaction option class (replaces existing `InteractableObject.InteractionOption` nested struct)
- `BodyType.cs` — body type enum
- `MovementMode.cs` — movement mode enum
- `WanderStyle.cs` — wander style enum

### Modified Files
- `Character.cs` — add capability registry, archetype reference, slim down over time
- `CharacterSystem.cs` — move Register/Unregister to OnEnable/OnDisable
- `CharacterInteractable.cs` — refactor to use IInteractionProvider collection
- `CharacterAnimator.cs` — decouple from hardcoded humanoid assumptions
- `CharacterVisual.cs` — implement ICharacterVisual interface (keeps being a CharacterSystem/NetworkBehaviour for `_netIsFacingRight` sync)
- `CharacterGameController.cs` — fix facade violations (direct `GetComponent<NPCBehaviourTree>()` calls) to use registry
- `SpawnManager.cs` — integrate with CharacterFactory for archetype-based spawning
- `MacroSimulator.cs` — archetype-aware catch-up formulas

### Unchanged
- All existing CharacterAction subclasses (they're already abstract enough — they are plain C# classes, not CharacterSystems)
- CharacterGoapController (data-driven, no changes needed)
- NPCBehaviourTree (runs whatever BT asset is assigned)
- CharacterMovement (already handles NavMesh + Rigidbody generically)
- CharacterCombat, CharacterNeeds, CharacterStats (optional capabilities, just need Register calls)

### Tech Debt to Address During Migration
- French comments throughout Character-related files (CLAUDE.md rule 23 requires English)
- `CharacterVisual.ApplyColor()` uses `sr.color` directly instead of Material Property Blocks (CLAUDE.md rule 25) — fix when wrapping in ICharacterVisual
- Existing `InteractableObject.InteractionOption` nested struct needs migration to standalone `InteractionOption` class in Phase 3
