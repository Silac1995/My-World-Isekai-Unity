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
