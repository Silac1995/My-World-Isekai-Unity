using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "VoiceSO", menuName = "Scriptable Objects/VoiceSO")]
public class VoiceSO : ScriptableObject
{
    [Header("Audio Samples")]
    [SerializeField] private List<AudioClip> _blipSounds = new List<AudioClip>();

    public AudioClip GetRandomClip()
    {
        if (_blipSounds.Count == 0) return null;
        return _blipSounds[Random.Range(0, _blipSounds.Count)];
    }
}