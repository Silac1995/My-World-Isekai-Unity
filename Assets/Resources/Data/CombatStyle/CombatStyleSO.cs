using System.Collections.Generic;
using UnityEngine;

public enum SecondaryStatType { Strength, Agility, Dexterity, Intelligence, Endurance }

[System.Serializable]
public struct StyleLevelData
{
    public int MinLevel;
    // On utilise RuntimeAnimatorController pour accepter les Overrides OU les Controllers classiques
    public RuntimeAnimatorController CombatController;
}

public abstract class CombatStyleSO : ScriptableObject
{
    [SerializeField] private string _styleName;
    [SerializeField] private GameObject _prefab;

    [Header("Damage Settings")]
    [SerializeField] private SecondaryStatType _scalingStat = SecondaryStatType.Strength;
    [SerializeField] private float _statMultiplier = 1.0f;

    [SerializeField] private List<StyleLevelData> _levels = new List<StyleLevelData>();

    public abstract WeaponType WeaponType { get; }
    public string StyleName => _styleName;
    public GameObject Prefab => _prefab;

    public SecondaryStatType ScalingStat => _scalingStat;
    public float StatMultiplier => _statMultiplier;


    public RuntimeAnimatorController GetCombatController(int level)
    {
        if (_levels.Count == 0) return null;

        StyleLevelData bestMatch = _levels[0];
        foreach (var data in _levels)
        {
            if (level >= data.MinLevel) bestMatch = data;
            else break;
        }
        return bestMatch.CombatController;
    }
}