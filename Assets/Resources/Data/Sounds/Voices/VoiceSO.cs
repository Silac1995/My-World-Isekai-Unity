using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "VoiceSO", menuName = "Scriptable Objects/VoiceSO")]
public class VoiceSO : ScriptableObject
{
    [Header("Audio Samples")]
    [SerializeField] private List<AudioClip> _blipSounds = new List<AudioClip>();

    [Header("Pitch Settings")]
    [Range(0.5f, 2.0f)] public float MinPitch = 0.9f;
    [Range(0.5f, 2.0f)] public float MaxPitch = 1.1f;

    public AudioClip GetRandomClip()
    {
        if (_blipSounds.Count == 0) return null;
        return _blipSounds[Random.Range(0, _blipSounds.Count)];
    }
}