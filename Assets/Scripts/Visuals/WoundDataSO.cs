using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "WoundData", menuName = "ScriptableObjects/Visuals/WoundData")]
public class WoundDataSO : ScriptableObject
{
    [Serializable]
    public struct WoundSpriteEntry
    {
        public DamageType DamageType;
        public List<Sprite> Sprites;
    }

    public List<WoundSpriteEntry> WoundSprites;

    public Sprite GetRandomSprite(DamageType type)
    {
        var entry = WoundSprites.Find(x => x.DamageType == type);
        if (entry.Sprites != null && entry.Sprites.Count > 0)
        {
            return entry.Sprites[UnityEngine.Random.Range(0, entry.Sprites.Count)];
        }
        return null;
    }
}
