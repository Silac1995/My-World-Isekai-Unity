using UnityEngine;

public abstract class CombatStyleSO : ScriptableObject
{
    [Header("Identity")]
    [SerializeField] private string _styleName;

    [Header("Visuals")]
    [SerializeField] private AnimatorOverrideController _styleOverride;

    // Propriété abstraite : chaque enfant DOIT dire quel type d'arme il gère
    public abstract WeaponType WeaponType { get; }

    public string StyleName => _styleName;
    public AnimatorOverrideController StyleOverride => _styleOverride;
}