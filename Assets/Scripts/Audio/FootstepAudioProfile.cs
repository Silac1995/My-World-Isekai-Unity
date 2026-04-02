using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "MWI/Audio/Footstep Audio Profile")]
public class FootstepAudioProfile : ScriptableObject
{
    [Serializable]
    public class MaterialClipSet
    {
        public ItemMaterial BootMaterial;
        public FootSurfaceType FootSurface;
        public AudioClip[] Clips;
        public float VolumeMultiplier = 1f;
        [Range(0f, 0.3f)] public float PitchVariation = 0.1f;
    }

    [SerializeField] private List<MaterialClipSet> _materialClips = new();
    [SerializeField] private AudioClip[] _fallbackClips;

    public (AudioClip clip, float volume, float pitchVariation) GetClip(
        ItemMaterial bootMaterial, FootSurfaceType footSurface)
    {
        // Try exact boot material match first
        if (bootMaterial != ItemMaterial.None)
        {
            foreach (var set in _materialClips)
            {
                if (set.BootMaterial == bootMaterial && set.Clips != null && set.Clips.Length > 0)
                {
                    var clip = set.Clips[UnityEngine.Random.Range(0, set.Clips.Length)];
                    return (clip, set.VolumeMultiplier, set.PitchVariation);
                }
            }
        }

        // Try foot surface match
        foreach (var set in _materialClips)
        {
            if (set.BootMaterial == ItemMaterial.None && set.FootSurface == footSurface
                && set.Clips != null && set.Clips.Length > 0)
            {
                var clip = set.Clips[UnityEngine.Random.Range(0, set.Clips.Length)];
                return (clip, set.VolumeMultiplier, set.PitchVariation);
            }
        }

        // Fallback
        if (_fallbackClips != null && _fallbackClips.Length > 0)
        {
            var clip = _fallbackClips[UnityEngine.Random.Range(0, _fallbackClips.Length)];
            return (clip, 1f, 0.1f);
        }

        return (null, 0f, 0f);
    }
}
