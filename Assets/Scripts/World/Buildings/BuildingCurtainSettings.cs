using UnityEngine;

/// <summary>
/// Shared tuning data for the construction "curtain" particle effect emitted by Buildings
/// while UnderConstruction (see <see cref="Building.EnsureConstructionCurtainParticles"/>).
///
/// Authored as a single asset (any path under Assets/ is fine — no Resources folder required)
/// and referenced by <see cref="BuildingManager"/> via its Inspector field. Every Building
/// reads from <c>BuildingManager.Instance.CurtainSettings</c> at curtain-spawn time, so a
/// single edit to the asset propagates to every building.
///
/// Per-building overrides remain possible by assigning a different settings asset to the
/// Building's own <c>_curtainSettings</c> field — that takes precedence over the manager's.
/// </summary>
[CreateAssetMenu(
    fileName = "BuildingCurtainSettings",
    menuName = "MWI/Building/Curtain Settings",
    order = 0)]
public class BuildingCurtainSettings : ScriptableObject
{
    [Header("Emission")]
    [Tooltip("Particles emitted per second by the curtain ParticleSystem. Lower = fewer particles.")]
    [Min(0f)] public float EmissionRate = 6f;

    [Tooltip("Hard cap on simultaneously alive curtain particles. Should be ≥ EmissionRate × StartLifetime.")]
    [Min(0)] public int MaxParticles = 40;

    [Header("Particle Shape & Motion")]
    [Tooltip("How long each curtain particle lives, in seconds. Combined with rise speed determines column height (height ≈ speed × lifetime).")]
    [Min(0.01f)] public float StartLifetime = 5f;

    [Tooltip("World-space start size of each curtain particle quad. Larger = thicker 'wall'.")]
    [Min(0f)] public float StartSize = 0.1f;

    [Tooltip("Min upward rise speed (Unity units / second). Project scale: 11 u = 1.67 m.")]
    [Min(0f)] public float RiseSpeedMin = 1.5f;

    [Tooltip("Max upward rise speed. Each particle picks a value in [Min, Max] at spawn.")]
    [Min(0f)] public float RiseSpeedMax = 2.5f;

    [Header("Color & Fade")]
    [Tooltip("RGB tint applied to the curtain particles. Multiplied with the material color. Alpha is ignored — fade is driven by the AlphaMax / FadeInEnd / FadeOutStart curve.")]
    public Color Tint = new Color(0.5f, 0.9f, 1f, 1f);

    [Tooltip("Peak alpha during the plateau phase of the alpha gradient. Lower = more translucent particles.")]
    [Range(0f, 1f)] public float AlphaMax = 0.2f;

    [Tooltip("Lifetime fraction at which fade-IN completes (alpha reaches AlphaMax). 0..1.")]
    [Range(0.001f, 0.999f)] public float FadeInEnd = 0.1f;

    [Tooltip("Lifetime fraction at which fade-OUT begins (alpha leaves AlphaMax → 0). Must be > FadeInEnd. Lower = particles begin fading earlier.")]
    [Range(0.001f, 0.999f)] public float FadeOutStart = 0.35f;

    [Header("Perimeter Wall")]
    [Tooltip("Translucent material applied to the procedurally-built perimeter wall mesh. " +
             "Recommended: URP/Particles/Unlit (or any shader respecting vertex color), " +
             "Surface = Transparent, Blend = Alpha, NO Base Map texture (vertex color alone drives the gradient). " +
             "Leave NULL to skip the perimeter wall entirely.")]
    public Material WallMaterial;

    [Tooltip("Height of the perimeter wall in Unity units. Project scale: 11 u = 1.67 m. Set to 0 to skip the wall.")]
    [Min(0f)] public float WallHeight = 5f;

    [Tooltip("Alpha at the GROUND (bottom edge of the wall). 1 = fully opaque, 0 = invisible.")]
    [Range(0f, 1f)] public float WallAlphaBottom = 0.5f;

    [Tooltip("Alpha at the TOP edge of the wall. 0 = fully transparent (clean fade-out).")]
    [Range(0f, 1f)] public float WallAlphaTop = 0f;

    [Tooltip("RGB color of the perimeter wall. Multiplied with the WallMaterial's color. Alpha component is ignored — the alpha gradient is driven by WallAlphaBottom/Top.")]
    public Color WallColor = new Color(0.5f, 0.9f, 1f, 1f);
}
