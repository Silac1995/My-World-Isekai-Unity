using UnityEngine;

[System.Serializable]
public class CombatStyleExpertise
{
    [SerializeField] private CombatStyleSO _style;
    [SerializeField] private int _level = 1;
    [SerializeField] private float _experience = 0f;

    public CombatStyleSO Style => _style;
    public int Level => _level;
    public float Experience => _experience;

    // Propriété pour un accès rapide (recommandé)
    public WeaponType WeaponType => _style != null ? _style.WeaponType : WeaponType.None;

    public CombatStyleExpertise(CombatStyleSO style)
    {
        _style = style;
        _level = 1;
        _experience = 0f;
    }

    // La méthode demandée
    public WeaponType GetWeaponType()
    {
        if (_style == null) return WeaponType.None; // Ou une valeur par défaut de ton Enum
        return _style.WeaponType;
    }

    public RuntimeAnimatorController GetCurrentAnimator()
    {
        return _style != null ? _style.GetCombatController(_level) : null;
    }

    public void AddExperience(float amount)
    {
        _experience += amount;

        // Logique de montée de niveau simple (ex: 100 XP par niveau)
        float xpRequired = _level * 100f;
        if (_experience >= xpRequired)
        {
            _experience -= xpRequired;
            _level++;
            Debug.Log($"<color=yellow>[Combat]</color> Style {_style.StyleName} level UP ! (Niv. {_level})");
        }
    }
}