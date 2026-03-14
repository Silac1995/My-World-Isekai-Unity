---
name: spine-unity
description: Technical standards and implementation patterns for Spine-Unity integration, focusing on preserving logical APIs during migration.
---

# Spine-Unity Implementation

This skill defines how to integrate and use **Spine-Unity** within the project. It emphasizes the project's strategy of wrapping Spine's technical implementation behind stable logical APIs.

## When to use this skill
- When migrating a component from `SpriteResolver` to `Spine-Unity`.
- When creating new character animations or visual effects using Spine skeletons.
- When implementing bone-specific logic (e.g., aiming, hand-holding, foot placement).
- To handle Spine-specific events (footsteps, vfx triggers) in C#.

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

### 2. Animation Management
- **Track 0**: Reserved for base movement (Idle, Walk, Run).
- **Track 1+**: Use for overlays (Attacking, Emotes, Hand Poses).
- **Mixing**: Set mix times in the `SkeletonDataAsset` for smooth transitions.

### Handling Spine Events
Listen to `AnimationState.Event` to trigger gameplay effects (sounds, sparks) synchronized with animation frames.
```csharp
void Start() {
    skeletonAnimation.AnimationState.Event += OnSpineEvent;
}

void OnSpineEvent(TrackEntry trackEntry, Spine.Event e) {
    if (e.Data.Name == "footstep") {
        PlayFootstepSound();
    }
}
```

### Bone Manipulation
- **BoneFollower**: Use if a GameObject (like a particle system) just needs to follow a bone.
- **SkeletonUtilityBone**: Use when you need to **override** a bone's position (e.g., procedural looking at a target).
    - Requires a `SkeletonUtility` component on the root.
    - Set mode to `Override` to take control from the animation.

## Tips for 2.5D Environment
- **Sorting Groups**: Always add a `Sorting Group` component to characters with multi-atlas Spine skeletons to prevent depth-sorting artifacts.
- **Billboarding**: Ensure the Spine GameObject's parent handles billboarding logic if it's a 2D entity in a 3D world.
