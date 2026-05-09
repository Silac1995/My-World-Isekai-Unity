using UnityEngine;

[System.Serializable]
public class CombatStyleExpertise
{
    [SerializeField] private CombatStyleSO _style;
    [SerializeField] private int _level = 1;
    [SerializeField] private float _experience = 0f;

    public const int MAX_LEVEL = 100;

    public CombatStyleSO Style => _style;
    public int Level => _level;
    public float Experience => _experience;
    public SkillTier CurrentTier => SkillTierExtensions.GetTierForLevel(_level);

    // Property for quick access (recommended)
    public WeaponType WeaponType => _style != null ? _style.WeaponType : WeaponType.None;

    public CombatStyleExpertise(CombatStyleSO style)
    {
        _style = style;
        _level = 1;
        _experience = 0f;
    }

    /// <summary>
    /// Restore constructor used by the save/load system to rebuild expertise state.
    /// </summary>
    public CombatStyleExpertise(CombatStyleSO style, int level, float experience)
    {
        _style = style;
        _level = Mathf.Clamp(level, 1, MAX_LEVEL);
        _experience = _level >= MAX_LEVEL ? 0f : Mathf.Max(0f, experience);
    }

    // The requested method
    public WeaponType GetWeaponType()
    {
        if (_style == null) return WeaponType.None; // Or a default value of your Enum
        return _style.WeaponType;
    }

    public RuntimeAnimatorController GetCurrentAnimator()
    {
        return _style != null ? _style.GetCombatController(_level) : null;
    }

    private float GetXPRequiredForLevel()
    {
        return _level * 100f;
    }

    public void AddExperience(float amount)
    {
        if (_level >= MAX_LEVEL) return;

        _experience += amount;

        bool leveledUp = false;
        while (_experience >= GetXPRequiredForLevel() && _level < MAX_LEVEL)
        {
            _experience -= GetXPRequiredForLevel();
            _level++;
            leveledUp = true;
        }

        if (_level >= MAX_LEVEL)
        {
            _experience = 0f;
        }

        if (leveledUp)
        {
            Debug.Log($"<color=yellow>[Combat]</color> Style {_style.StyleName} level UP! (Lv. {_level})");
        }
    }
}
