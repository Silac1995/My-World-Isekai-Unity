using UnityEngine;

/// <summary>
/// Shared tuning data for the construction-zone outline rendered by
/// <see cref="BuildingConstructionOutline"/> while a Building is UnderConstruction.
///
/// Authored as a single asset shared across every building so designers can re-tune
/// the look in one place. Per-building overrides remain possible by assigning a
/// different settings asset to a specific Building prefab variant.
///
/// <para>
/// Crash-safety: this SO holds <b>only primitive fields</b> (Color, float, bool, int).
/// It deliberately has zero <see cref="UnityEngine.Object"/> references — no Material,
/// no Texture, no Shader assignment — so the standalone Mono build's strict native loader
/// cannot crash on a dangling reference (see [[material-buildproperties-standalone-crash]]).
/// The outline's runtime Material is constructed in code via <c>Shader.Find</c> against a
/// built-in shader, never serialized to disk.
/// </para>
/// </summary>
[CreateAssetMenu(
    fileName = "BuildingConstructionOutlineSettings",
    menuName = "MWI/Building/Construction Outline Settings",
    order = 0)]
public class BuildingConstructionOutlineSettings : ScriptableObject
{
    [Header("Stroke")]
    [Tooltip("Line thickness in Unity units. Project scale: 11 u = 1.67 m, so the default 0.15 u ≈ 2.3 cm — readable from a normal isometric view, not heavy enough to obscure ground detail.")]
    [Min(0.001f)] public float Width = 0.15f;

    [Tooltip("Outline color (RGB + alpha). Default = construction-yellow.")]
    public Color Color = new Color(1f, 0.85f, 0f, 1f);

    [Header("Geometry")]
    [Tooltip("Vertical offset above the BuildingZone bottom face, in Unity units. Default 0.05 u (~7 mm) lifts the line off the ground to prevent Z-fighting with terrain meshes.")]
    [Min(0f)] public float HeightOffset = 0.05f;

    [Tooltip("Smoothing vertices inserted at each corner of the rectangle. 0 = sharp 90° corners (recommended for blueprint look). >0 = rounded corners (higher = smoother, more verts).")]
    [Range(0, 8)] public int CornerVertices = 0;

    [Header("Shader")]
    [Tooltip("Built-in shader name used to render the line. Default 'Sprites/Default' is bundled with every Unity install (built-in & URP) and supports tinted vertex color out of the box. KEEP THIS A BUILT-IN SHADER — custom shaders re-introduce the May 2026 Material::BuildProperties crash class.")]
    public string ShaderName = "Sprites/Default";

    [Tooltip("Fallback shader if the primary ShaderName fails to resolve. Both shaders ship with every Unity install — if neither resolves, the outline silently no-ops.")]
    public string FallbackShaderName = "Unlit/Color";
}
