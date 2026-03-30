using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Maps semantic AnimationKey enums and custom string keys to actual clip/animation names.
/// Each CharacterArchetype references one of these to define its animation set.
/// </summary>
[CreateAssetMenu(fileName = "New Animation Profile", menuName = "MWI/Character/Animation Profile")]
public class AnimationProfile : ScriptableObject
{
    [System.Serializable]
    public struct AnimationEntry
    {
        public AnimationKey Key;
        public string ClipName;
    }

    [System.Serializable]
    public struct CustomAnimationEntry
    {
        public string CustomKey;
        public string ClipName;
    }

    [SerializeField] private List<AnimationEntry> _keyMappings = new();
    [SerializeField] private List<CustomAnimationEntry> _customMappings = new();

    private Dictionary<AnimationKey, string> _keyLookup;
    private Dictionary<string, string> _customLookup;

    private void BuildLookups()
    {
        if (_keyLookup != null) return;

        _keyLookup = new Dictionary<AnimationKey, string>();
        foreach (var entry in _keyMappings)
            _keyLookup[entry.Key] = entry.ClipName;

        _customLookup = new Dictionary<string, string>();
        foreach (var entry in _customMappings)
            _customLookup[entry.CustomKey] = entry.ClipName;
    }

    /// <summary>Resolve a universal AnimationKey to a clip name. Returns null if unmapped.</summary>
    public string GetClipName(AnimationKey key)
    {
        BuildLookups();
        return _keyLookup.TryGetValue(key, out var clip) ? clip : null;
    }

    /// <summary>Resolve a custom string key to a clip name. Returns null if unmapped.</summary>
    public string GetClipName(string customKey)
    {
        BuildLookups();
        return _customLookup.TryGetValue(customKey, out var clip) ? clip : null;
    }

    /// <summary>Force rebuild of lookups (call after modifying entries at runtime).</summary>
    public void InvalidateCache()
    {
        _keyLookup = null;
        _customLookup = null;
    }
}
