---
name: spine-unity
description: Technical standards and implementation patterns for Spine-Unity integration. Covers skin composition, physics constraints, wound overlays, dismemberment, and cross-archetype socket attachment. All patterns preserve the logical APIs defined in ICharacterVisual / ICharacterPartCustomization / IBoneAttachment.
---

# Spine-Unity Implementation

This skill defines how to integrate and use **Spine-Unity** within the project. It emphasizes the project's strategy of wrapping Spine's technical implementation behind stable logical APIs.

## When to use this skill
- When migrating a component from `SpriteResolver` to `Spine-Unity`.
- When creating new character animations or visual effects using Spine skeletons.
- When implementing bone-specific logic (aiming, hand-holding, foot placement).
- When composing clothing layers (underwear / clothing / armor / accessories / prosthetics).
- When wiring physics-enabled garments (skirts, capes, long hair).
- When implementing wound overlays or dismemberment visuals.
- When handling Spine-specific events (footsteps, vfx triggers) in C#.

For the architectural overview (the "why"), see the [[visuals]] wiki page. This skill is the procedural "how".

## Architecture & Golden Rules

### 1. The Wrapper Pattern (Preserving Logical APIs)
The project is in a migration phase. High-level systems (GOAP, BT, AI) must remain agnostic of the underlying rendering tech.
- **GOLDEN RULE**: Never call `SkeletonAnimation` methods directly from AI or Gameplay logic.
- Instead, update the existing logical controllers (e.g., `HandsController`, `EyesController`) to call Spine methods internally.
- Example: `CharacterHand.SetPose("fist")` should be updated to switch a Spine skin or attachment, rather than the gameplay code being changed to call `skeletonAnimation.skeleton.SetAttachment(...)`.

### 2. Main Components & Lifecycle
- **SkeletonAnimation**: Core component for world entities.
    - **Lifecycle Callback**: If performing procedural bone overrides (aiming, looking), use `skeletonAnimation.UpdateComplete` or `UpdateLocal` delegates. This ensures modifications happen *after* animation is applied but *before* the mesh is rendered.
- **SkeletonMecanim**: Use only if Unity's Animator state machine is required.
    - **GOLDEN RULE**: Always key every animated property at **frame 0** in every animation to prevent "snapping" to setup pose when mixing.
- **SkeletonGraphic**: Use for UI elements. Requires `Spine/SkeletonGraphic` shaders.

### 3. Utility Components
- **Followers (Isolated GameObjects)**:
    - **BoneFollower**: Simplest way to attach an object (particle system, weapon) to a bone.
    - **PointFollower**: Use to track a **PointAttachment** for precise placement that can be animated independently of bone centers.
    - **BoneFollowerGraphic**: UI-specific version for `SkeletonGraphic`.
- **SkeletonRenderSeparator**: Use to split a skeleton's mesh into multiple sub-renderers for complex depth layering (e.g., character "sandwiching" an object).
    - Use `SkeletonRenderSeparator.AddToSkeletonRenderer(skeletonAnimation)` to set up at runtime.
- **SkeletonRagdoll/2D**: For death sim or physical interactions.

### 4. Rendering & Advanced Parameters
- **Material Overrides**: Never modify `MeshRenderer.materials` directly.
    - Use **`SkeletonRendererCustomMaterials`** (3D) or **`SkeletonGraphicCustomMaterials`** (UI) for stable overrides.
    - Use `SkeletonRenderer.CustomSlotMaterials` to replace materials for specific slots programmatically.
- **Z-Spacing**: Set a small positive value (e.g., 0.001) in the inspector to prevent **z-fighting**.
- **Sorting Groups**: **MANDATORY** for characters. Always add a `Sorting Group` component to the root to ensure the entire skeleton (potentially multi-material) sorts as one entity.
- **URP Support**: Use the `spine-unity-urp-shaders` package. Set `Cull Off` in custom shaders.
- **Root Motion**: Requires selecting a **Root Motion Bone** in the inspector.

## Technical Implementation

### 1. Simple Skin Switch
```csharp
public void SetSpineSkin(string skinName) {
    var skeleton = skeletonAnimation.Skeleton;
    skeleton.SetSkin(skinName);
    skeleton.SetSlotsToSetupPose(); // CRITICAL: Prevents attachment leaking from previous skins
    skeletonAnimation.AnimationState.Apply(skeleton);
}
```

### 2. Mix-and-Match Skin Composition (clothing layers)

Equipment items are Spine skins organized hierarchically (see [[visuals]] §Skin organization). Compose the current outfit by adding every active skin into a new combined skin, then applying it once.

```csharp
public class SpineCharacterVisual : MonoBehaviour,
    ICharacterVisual, ICharacterPartCustomization, IBoneAttachment, IAnimationLayering
{
    [SerializeField] private SkeletonAnimation _skeleton;
    private Skin _combinedSkin;
    private readonly HashSet<string> _activeSkinNames = new();

    public void SetPart(string slotName, string attachmentName)
    {
        // Replace any existing entry for this logical slot prefix
        _activeSkinNames.RemoveWhere(s => s.StartsWith(slotName + "/"));
        if (!string.IsNullOrEmpty(attachmentName))
            _activeSkinNames.Add(attachmentName);
        RebuildSkin();
    }

    public void RemovePart(string slotName)
    {
        _activeSkinNames.RemoveWhere(s => s.StartsWith(slotName + "/"));
        RebuildSkin();
    }

    public void CombineSkins(params string[] skinNames)
    {
        _activeSkinNames.Clear();
        foreach (var n in skinNames) _activeSkinNames.Add(n);
        RebuildSkin();
    }

    private void RebuildSkin()
    {
        _combinedSkin = new Skin("combined");
        foreach (var name in _activeSkinNames)
        {
            var skin = _skeleton.Skeleton.Data.FindSkin(name);
            if (skin != null) _combinedSkin.AddSkin(skin);
            else Debug.LogWarning($"[SpineVisual] Skin '{name}' not found in skeleton.");
        }
        _skeleton.Skeleton.SetSkin(_combinedSkin);
        _skeleton.Skeleton.SetSlotsToSetupPose();   // CRITICAL — clears leaked attachments
        UpdatePhysicsState();                       // see §3
    }
}
```

**Typical equip flow** — `CharacterEquipment.Equip` calls:
```csharp
visual.CombineSkins(
    "body/humanoid_male",
    "underwear/basic",
    "clothing/tshirt",
    "clothing/jeans",
    "armor/leather_chest",
    "accessories/glasses"
);
```

### 3. Physics Constraint Control (skirts, capes, long hair)

Spine 4.2+ physics run automatically once configured in the Spine Editor. The runtime only needs to enable/disable them based on which garment is equipped, and reset accumulated velocity when toggling back on.

```csharp
// Declarative mapping: logical physics group → bone-name prefix.
private static readonly Dictionary<string, string[]> PhysicsGroups = new()
{
    ["skirt"]     = new[] { "skirt_front_", "skirt_left_", "skirt_right_", "skirt_back_" },
    ["cape"]      = new[] { "cape_" },
    ["hair_long"] = new[] { "hair_long_" },
    ["tail"]      = new[] { "tail_" },
};

private void UpdatePhysicsState()
{
    // Collect active groups from equipped items
    var activeGroups = CollectActiveGroupsFromEquippedItems();

    foreach (var constraint in _skeleton.Skeleton.PhysicsConstraints)
    {
        var boneName = constraint.Bone.Data.Name;
        bool shouldBeActive = PhysicsGroups
            .Any(g => activeGroups.Contains(g.Key) &&
                      g.Value.Any(prefix => boneName.StartsWith(prefix)));

        bool wasInactive = constraint.Mix == 0f;
        constraint.Mix = shouldBeActive ? 1f : 0f;

        // Reset on re-activation to clear accumulated velocity
        if (shouldBeActive && wasInactive) constraint.Reset();
    }
}

public void ResetAllPhysics()    // call after teleport / respawn
{
    foreach (var constraint in _skeleton.Skeleton.PhysicsConstraints)
        constraint.Reset();
}

public void SetWind(Vector2 wind)
{
    foreach (var constraint in _skeleton.Skeleton.PhysicsConstraints)
        constraint.Wind = wind.x;
}
```

**Which items declare which groups** — store `string[] requiredPhysicsGroups` on the `ClothingItemSO`. A pair of jeans declares `[]`; a short skirt declares `["skirt"]`; a dress with a cape declares `["skirt", "cape"]`.

### 4. Wound Overlays

Two mechanisms coexist — use the appropriate one per wound type.

#### 4a. Narrative wounds via skin overlay

```csharp
public void AddWound(WoundType type, BodyRegion region)
{
    // Skin path convention: "wounds/{type}_{region}"
    var skinName = $"wounds/{type.ToString().ToLower()}_{region.ToString().ToLower()}";
    _activeSkinNames.Add(skinName);
    RebuildSkin();
}

public void ClearWounds()
{
    _activeSkinNames.RemoveWhere(s => s.StartsWith("wounds/"));
    RebuildSkin();
}
```

#### 4b. Dynamic bruises via shader MPB

Random placement of up to 4 bruises, driven entirely by shader (no batching break per rule #25).

```csharp
private static readonly int BruisePositionsID = Shader.PropertyToID("_BruisePositions");
private static readonly int BruiseCountID     = Shader.PropertyToID("_BruiseCount");
private static readonly MaterialPropertyBlock _mpb = new();

public void ApplyRandomBruises(int count)
{
    count = Mathf.Clamp(count, 0, 4);
    var positions = new Vector4[4];
    for (int i = 0; i < count; i++)
        positions[i] = new Vector4(
            Random.Range(0.1f, 0.9f),  // UV x
            Random.Range(0.1f, 0.9f),  // UV y
            Random.Range(0.05f, 0.15f),// radius
            Random.Range(0.5f, 1f));   // intensity

    var renderer = _skeleton.GetComponent<MeshRenderer>();
    renderer.GetPropertyBlock(_mpb);
    _mpb.SetVectorArray(BruisePositionsID, positions);
    _mpb.SetInt(BruiseCountID, count);
    renderer.SetPropertyBlock(_mpb);
}
```

Target shader: `MWI/Spine-Wounds-Lit` (to be authored — extends `Spine/Skeleton-Lit`).

### 5. Dismemberment visuals

When [[character-dismemberment]] fires `Dismember(ArmRight)`, the visual backend hides all base/clothing/armor slots belonging to that limb:

```csharp
private static readonly Dictionary<BodyPartId, string[]> LimbSegments = new()
{
    [BodyPartId.ArmRight]  = new[] { "upperarm_R", "forearm_R", "hand_R" },
    [BodyPartId.ArmLeft]   = new[] { "upperarm_L", "forearm_L", "hand_L" },
    [BodyPartId.LegRight]  = new[] { "thigh_R", "shin_R", "foot_R" },
    [BodyPartId.LegLeft]   = new[] { "thigh_L", "shin_L", "foot_L" },
    [BodyPartId.HandRight] = new[] { "hand_R" },
    // ...
};

public void HideLimb(BodyPartId part)
{
    if (!LimbSegments.TryGetValue(part, out var segments)) return;

    foreach (var seg in segments)
        foreach (var layer in new[] { "_base", "_clothing", "_armor" })
            RemovePart(seg + layer);

    // Optional: add stump attachment at joint
    var jointSlot = segments[0] + "_stump";   // e.g. "upperarm_R_stump"
    SetPart(jointSlot, $"stump/{segments[0]}_clean");
}

public void AttachProsthetic(BodyPartId part, string prostheticSkinName)
{
    // Prosthetic skin fills the same *_base slots the limb used to occupy.
    _activeSkinNames.Add(prostheticSkinName);   // e.g. "prosthetic/wooden_arm_R"
    RebuildSkin();
}
```

### 6. Cross-Archetype Socket Attachment

Equipment (caps, back-mounted items) attaches to logical **sockets**, not bone names. The archetype's `EquipmentSocketMap` SO resolves sockets → bones with per-archetype offsets.

```csharp
private EquipmentSocketMap _socketMap;

public void Initialize(Character character, CharacterArchetype archetype)
{
    _socketMap = archetype.SocketMap;
    // ...
}

public void AttachToSocket(string socketName, GameObject obj)
{
    if (!_socketMap.TryGetSocket(socketName, out var socket))
    {
        obj.SetActive(false);   // archetype doesn't support this socket
        return;
    }

    var follower = obj.AddComponent<BoneFollower>();
    follower.SkeletonRenderer = _skeleton;
    follower.SetBone(socket.boneName);

    obj.transform.localPosition   = socket.localOffset;
    obj.transform.localEulerAngles = socket.localRotation;
    obj.transform.localScale      = socket.localScale;
}
```

See [[visuals]] §Cross-Archetype Equipment Sockets for the SO schema.

### 6b. Skin Bones — wings and other optional body extensions

Wings, horns, extra arms, and other **rare/variant-heavy/dynamic** body extensions should not be baked into the master skeleton. Use Spine's Skin Bones feature: the skin declares bones + physics constraints + meshes, which are injected on apply and removed on unapply. See [[visuals]] §Skin Bones for the architectural decision rule.

No special runtime code is required — `CombineSkins(..., "wings/angel_feather")` injects the skin's bones into the skeleton automatically. Physics constraints declared inside the skin are picked up by the existing `UpdatePhysicsState()` loop (they appear in `_skeleton.Skeleton.PhysicsConstraints` only when their skin is active).

**Wings animation pattern** — dedicate Track 2 for wing overlays:
```csharp
private const int TRACK_MOVEMENT = 0;
private const int TRACK_COMBAT_OVERLAY = 1;
private const int TRACK_WINGS = 2;

public void SetWingState(WingState state)
{
    if (!HasWingsSkinActive()) return;   // character without wings — no-op

    switch (state)
    {
        case WingState.Folded:  _skeleton.AnimationState.SetAnimation(TRACK_WINGS, "wings_fold", true); break;
        case WingState.Idle:    _skeleton.AnimationState.SetAnimation(TRACK_WINGS, "fly_idle", true); break;
        case WingState.Flap:    _skeleton.AnimationState.SetAnimation(TRACK_WINGS, "fly_flap", true); break;
        case WingState.Glide:   _skeleton.AnimationState.SetAnimation(TRACK_WINGS, "fly_glide", true); break;
    }
}

private bool HasWingsSkinActive() => _activeSkinNames.Any(n => n.StartsWith("wings/"));
```

**Combat integration** — when entering combat, fold wings on Track 2 to prevent clipping with sword arcs:
```csharp
public void OnEnterCombat() => SetWingState(WingState.Folded);
public void OnExitCombat()  => SetWingState(WingState.Idle);
```

**Physics tuning for wings** — wings are lighter than skirts and respond to fast flapping; typical constraint values: Inertia 0.5, Strength 200, Damping 0.6, Mass 0.5, Gravity -3 (less than a skirt). Tune per wing variant (insect wings nearly weightless, dragon wings heavy).

**Gotcha** — Spine silently ignores animation keys referencing bones that don't currently exist. If you key a `fly_flap` animation against wing bones, playing it on a character without wings is a safe no-op. Never add defensive "if wings skin active" guards around `SetAnimation` calls on Track 2 — the silent ignore is by design.

### 7. Animation Management

- **Track 0**: Reserved for base movement (Idle, Walk, Run).
- **Track 1+**: Use for overlays (Attacking, Emotes, Hand Poses, Hit Reacts).
- **Mixing**: Set mix times in the `SkeletonDataAsset` for smooth transitions.

```csharp
public void PlayOverlayAnimation(AnimationKey key, int layer, bool loop = false)
{
    _skeleton.AnimationState.SetAnimation(layer, key.ToString(), loop);
}

public void ClearOverlayAnimation(int layer)
{
    _skeleton.AnimationState.ClearTrack(layer);
}
```

### 8. Handling Spine Events
Listen to `AnimationState.Event` to trigger gameplay effects (sounds, sparks) synchronized with animation frames.
```csharp
void Start()
{
    skeletonAnimation.AnimationState.Event += OnSpineEvent;
}

void OnSpineEvent(TrackEntry trackEntry, Spine.Event e)
{
    // Forward to ICharacterVisual.OnAnimationEvent so gameplay layers stay backend-agnostic
    _onAnimationEvent?.Invoke(e.Data.Name);
}
```

### 9. Bone Manipulation
- **BoneFollower**: Use if a GameObject (like a particle system) just needs to follow a bone.
- **SkeletonUtilityBone**: Use when you need to **override** a bone's position (e.g., procedural looking at a target).
    - Requires a `SkeletonUtility` component on the root.
    - Set mode to `Override` to take control from the animation.

## Multi-family architecture

MWI supports multiple creature archetypes (humanoid, quadruped, bird, insect, aquatic, unique monsters — see [[visuals]] §Archetype Families). The **C# implementation is shared**: one `SpineCharacterVisual` class reads everything from `CharacterArchetype`. Adding a new creature (a fox, a parrot, a slime variant) is data-only — author a Spine skeleton + a `CharacterArchetype` SO, no new scripts.

### Authoring a new non-humanoid creature

1. **Skeleton** — create in Spine Editor using the appropriate family's master skeleton (`QuadrupedSmall_Master`, `Bird_Master`, etc.) or a fully custom one for unique monsters. Follow the family's slot layout (see [[visuals]] §Family-specific slot layouts).
2. **AnimationSetSO** — declare the `AnimationKey → Spine animation name` mapping. Every key the gameplay layer needs (`Idle`, `Walk`, `Run`, `Attack`, `Hit`, `Death`) must resolve to a real Spine animation in the skeleton.
3. **EquipmentSocketMap** — list the sockets the creature exposes (dog: `neck_accessory`, `back_accessory`, `body_armor`; slime: none or a single `top_accessory`). Unknown sockets silently fail at equip time.
4. **CharacterArchetype SO** — wire it all:
   - `family` = one of the enum values.
   - `visualPrefab` = the prefab that carries this skeleton.
   - `socketMap`, `animationSet` = the assets from steps 2-3.
   - `bodyPartCapabilities` flags = which systems apply (dismemberment, clothing layers, wings, facial).
5. **Prefab** — `Character_<Name>.prefab` under `Assets/Prefabs/Character/`. Root has `Character.cs` facade, `SpineCharacterVisual` child with the `SkeletonAnimation`, `Sorting Group` component.
6. **Test compatibility flags** — verify that disabled features no-op cleanly (a slime getting hit should not trigger dismemberment VFX even if combat dispatches the call).

### Capability-gated code pattern

Feature systems must consult `archetype.bodyPartCapabilities` before running their logic. Example for dismemberment:

```csharp
public void Dismember(BodyPartId part)
{
    if (!_character.Archetype.bodyPartCapabilities.HasFlag(BodyPartCapabilities.SupportsDismemberment))
        return;   // silently no-op for slimes, etc.

    // ... normal dismemberment flow
}
```

Same pattern for `CanWearClothing`, `HasWings`, `SupportsFacialExpression`. Never hardcode family checks (`if (family == Humanoid)`) — always gate on capability flags so new families compose cleanly.

### Animation key parity

GOAP/BT actions emit `AnimationKey.Attack` regardless of whether the character is a knight or a wolf. The archetype's `AnimationSetSO` resolves the key to the right Spine animation. **Every family's `AnimationSetSO` must cover the full `AnimationKey` enum** — a missing entry falls back to `Idle` with a warning log. Flag missing entries during integration, never silently.

```csharp
public string Resolve(AnimationKey key)
{
    if (_mapping.TryGetValue(key, out var name)) return name;
    Debug.LogWarning($"[{name}] Missing animation mapping for {key} on archetype {_archetypeName}; falling back to Idle.");
    return _mapping[AnimationKey.Idle];
}
```

## Setup Checklist — New Humanoid Spine Character

1. **Skeleton** — import/create skeleton with all bone chains baked in (body, arms, legs, **skirt**, **cape**, **long hair**, **tail** if applicable). See [[visuals]] §Bone chains baked into the master skeleton.
2. **Slots** — one slot per body segment per layer (`torso_base`, `torso_underwear`, `torso_clothing`, `torso_armor`). Include stump slots (`upperarm_R_stump`, ...) and accessory slots (`head_accessory`, `neck_accessory`, `hair_accessory_N`).
3. **Slot draw order** — bottom→top: base → underwear → clothing → armor → accessories. Back limb variants below torso; front limb variants above.
4. **Skins** — organize hierarchically: `body/`, `underwear/`, `clothing/`, `skirt/`, `armor/`, `accessories/`, `wounds/`, `stump/`, `prosthetic/`.
5. **Mesh weights** — rigid armor/helmet/shoes = region attachments. Soft cloth/pants/sleeves = mesh weights for smooth joint deformation.
6. **Physics constraints** — add on every chain bone (skirt_xxx, cape_xx, hair_long_xx, tail_xx). Default Mix=1 in setup; runtime code will toggle.
7. **Animations** — key every animated property at frame 0. Track 0 = movement. Track 1+ = overlays.
8. **Sorting Group** on the Unity prefab root (mandatory for multi-atlas skeletons).
9. **Archetype SO** — create `EquipmentSocketMap` asset with sockets (`head`, `back`, `hand_R`, `hand_L`) and their bone mappings.
10. **`SpineCharacterVisual`** component on the prefab — wires `ICharacterVisual` + optional interfaces.

## Tips & Troubleshooting

- **A sprite does not appear / Visual bug**: Verify that the logic actually calls the basic API (`SetPose()`, `SetClosed()`). The fact that the underlying technology is temporary does not excuse bypassing the modular architecture.
- **Attachments leak after skin swap** → forgot `SetSlotsToSetupPose()`. Always call it after `SetSkin`.
- **Skirt oscillates violently on re-equip** → forgot `constraint.Reset()` when flipping Mix 0→1. Reset clears accumulated velocity.
- **Skirt oscillates after teleport** → call `ResetAllPhysics()` on position snaps.
- **Helmet floats above head on an animal** → the archetype's `EquipmentSocketMap` lacks or has wrong offsets for the `head` socket. Tune `localOffset`/`localScale` per archetype.
- **Glove leaks on dismembered hand** → the clothing-layer slot was not hidden. `HideLimb` must cover all three layers (`_base`, `_clothing`, `_armor`), not just base.
- **Prosthetic renders naked (no sleeve)** → the prosthetic skin should fill `*_base` only; the clothing skin supplies the sleeve on `*_clothing`. If you want a sleeved prosthetic by design, include a `_clothing` attachment inside the prosthetic skin.
- **Back arm renders in front of torso during an animation** → missing Draw Order keys in that animation. Key the slot order at frame 0 and any frame where the facing flips.

## 2.5D Environment Tips
- **Sorting Groups**: Always add a `Sorting Group` component to characters with multi-atlas Spine skeletons to prevent depth-sorting artifacts.
- **Billboarding**: Ensure the Spine GameObject's parent handles billboarding logic if it's a 2D entity in a 3D world.
- **Shadow casting**: After migration, the `SkeletonAnimation.MeshRenderer` uses `Spine-Skeleton-Lit-ZWrite` (already in the project). Shadow behaviour stays identical; `ICharacterVisual` is not touched. See `.agent/skills/rendering/shadows/SKILL.md`.

## References
- Architectural overview: [[visuals]] wiki page.
- Related procedural skill: [character_visuals/SKILL.md](../character_visuals/SKILL.md) — logical body-part API (eyes, hands) that survives the migration.
- Related wiki: [[character-equipment]], [[character-dismemberment]], [[character-archetype]].
- Existing example: `Assets/Spine Examples/Scripts/MixAndMatch.cs`.
- Reusable patterns: [examples/spine_patterns.md](examples/spine_patterns.md).
