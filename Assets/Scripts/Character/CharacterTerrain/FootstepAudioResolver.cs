using UnityEngine;
using MWI.Terrain;

/// <summary>
/// Resolves footstep audio from terrain type x boot material.
/// Triggered by CharacterVisual.OnAnimationEvent("footstep").
/// </summary>
public class FootstepAudioResolver : MonoBehaviour
{
    [SerializeField] private AudioSource _footstepAudioSource;

    private CharacterTerrainEffects _terrainEffects;
    private Character _character;

    private void Awake()
    {
        _terrainEffects = GetComponent<CharacterTerrainEffects>();
        _character = GetComponentInParent<Character>();
    }

    private void OnEnable()
    {
        if (_character != null && _character.CharacterVisual != null)
            _character.CharacterVisual.OnAnimationEvent += HandleAnimationEvent;
    }

    private void OnDisable()
    {
        if (_character != null && _character.CharacterVisual != null)
            _character.CharacterVisual.OnAnimationEvent -= HandleAnimationEvent;
    }

    private void HandleAnimationEvent(string eventName)
    {
        if (eventName != "footstep") return;
        PlayFootstep();
    }

    public void PlayFootstep()
    {
        if (_terrainEffects == null || _terrainEffects.CurrentTerrainType == null) return;
        if (_footstepAudioSource == null) return;

        var terrainType = _terrainEffects.CurrentTerrainType;
        if (terrainType.FootstepProfile == null) return;

        // Resolve foot material via Character facade
        ItemMaterial bootMaterial = ItemMaterial.None;
        FootSurfaceType footSurface = FootSurfaceType.BareSkin;

        if (_character != null)
        {
            var equipment = _character.CharacterEquipment;
            if (equipment != null)
                bootMaterial = equipment.GetFootMaterial();

            var archetype = _character.Archetype;
            if (archetype != null)
                footSurface = archetype.DefaultFootSurface;
        }

        var (clip, volume, pitchVar) = terrainType.FootstepProfile.GetClip(bootMaterial, footSurface);
        if (clip == null) return;

        _footstepAudioSource.pitch = 1f + Random.Range(-pitchVar, pitchVar);
        _footstepAudioSource.PlayOneShot(clip, volume);
    }
}
