using UnityEngine;

/// <summary>
/// MonoBehaviour holder for the project-wide <see cref="BuildingCurtainSettings"/> asset.
/// Add this component once to <c>Assets/Prefabs/Building/Building_prefab.prefab</c> and
/// drag the settings asset onto its <see cref="_settings"/> slot. Every building prefab
/// variant of Building_prefab inherits the assignment via Unity's prefab inheritance —
/// no per-variant authoring needed.
///
/// Per-variant overrides remain possible: open the variant prefab and override the
/// <see cref="_settings"/> field with a different settings asset. That follows the
/// standard Unity prefab override workflow rather than introducing a separate override
/// mechanism on Building.cs.
///
/// Read at runtime by <see cref="Building.EnsureConstructionCurtainParticles"/> via
/// <c>GetComponent&lt;BuildingCurtainSettingsHolder&gt;()</c> on the Building's root.
/// </summary>
[DisallowMultipleComponent]
public class BuildingCurtainSettingsHolder : MonoBehaviour
{
    [Tooltip("Tuning asset for the construction 'curtain' particle effect emitted while this " +
             "building is UnderConstruction. Assigned once on Building_prefab.prefab; every " +
             "building variant inherits it. Override on a specific variant prefab if needed.")]
    [SerializeField] private BuildingCurtainSettings _settings;

    /// <summary>
    /// The settings asset for this building's construction curtain. May be null if not
    /// assigned in the Inspector — Building will then skip the curtain effect with a warning.
    /// </summary>
    public BuildingCurtainSettings Settings => _settings;
}
