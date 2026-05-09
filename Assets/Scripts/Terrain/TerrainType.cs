using UnityEngine;

namespace MWI.Terrain
{
    [CreateAssetMenu(menuName = "MWI/Terrain/Terrain Type")]
    public class TerrainType : ScriptableObject
    {
        [Header("Identity")]
        public string TypeId;
        public string DisplayName;
        public Color DebugColor = Color.white;

        [Header("Character Effects")]
        public float SpeedMultiplier = 1f;
        public float DamagePerSecond = 0f;
        public float SlipFactor = 0f;
        public DamageType DamageType;
        public bool HasDamage => DamagePerSecond > 0f;

        [Header("Growth")]
        public bool CanGrowVegetation;

        [Header("Audio")]
        public FootstepAudioProfile FootstepProfile;

        [Header("Visuals")]
        public Material GroundOverlayMaterial;
        [Range(0f, 1f)] public float OverlayOpacityAtFullSaturation = 0.8f;
    }
}
