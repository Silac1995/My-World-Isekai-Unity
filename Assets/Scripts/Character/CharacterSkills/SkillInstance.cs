using System;
using UnityEngine;

[Serializable]
public class SkillInstance
{
    [SerializeField] private SkillSO _skillSO;
    [SerializeField] private int _currentLevel = 1;
    [SerializeField] private int _currentXP = 0;
    [SerializeField] private int _totalXP = 0;

    // Events pour notifier l'UI ou le manager
    public event Action<SkillInstance, int> OnLevelUp;
    public event Action<SkillInstance, int> OnXPGained;

    // TODO: A remplacer par une vraie courbe d'XP si besoin
    public int XPToNextLevel => _currentLevel * 100;

    public SkillSO Skill => _skillSO;
    public int Level => _currentLevel;
    public int XP => _currentXP;
    public int TotalXP => _totalXP;

    public SkillInstance(SkillSO skill, int initialLevel = 1)
    {
        _skillSO = skill;
        _currentLevel = initialLevel;
        _currentXP = 0;
        _totalXP = 0;
    }

    public void AddXP(int amount)
    {
        if (amount <= 0 || _skillSO == null) return;

        _currentXP += amount;
        _totalXP += amount;
        OnXPGained?.Invoke(this, amount);

        // Boucle au cas où l'XP fait monter plusieurs niveaux d'un coup
        bool leveledUp = false;
        while (_currentXP >= XPToNextLevel)
        {
            _currentXP -= XPToNextLevel;
            _currentLevel++;
            leveledUp = true;
        }

        if (leveledUp)
        {
            Debug.Log($"<color=green>[Skill]</color> Le niveau de '{_skillSO.SkillName}' est passé à {_currentLevel} !");
            OnLevelUp?.Invoke(this, _currentLevel);
        }
    }

    /// <summary>
    /// Calcule l'Efficacité (Proficiency) finale du personnage pour ce Skill.
    /// Basée sur son niveau métier + les bonus octroyés par ses statistiques.
    /// </summary>
    public float CalculateProficiency(CharacterStats stats)
    {
        if (_skillSO == null) return 0f;

        // 1. Base issue du métier lui-même
        float totalProficiency = _currentLevel * _skillSO.BaseProficiencyPerLevel;

        // 2. Bonus issus des statistiques du personnage
        if (stats != null && _skillSO.StatInfluences != null)
        {
            foreach (var influence in _skillSO.StatInfluences)
            {
                // Récupère la valeur réelle de la stat (ex: 15 Agilité)
                float statValue = stats.GetSecondaryStatValue(influence.StatType);
                
                // Ex: 15 * 0.5f = +7.5 d'efficacité bonus
                totalProficiency += (statValue * influence.ProficiencyPerPoint); 
            }
        }

        return totalProficiency;
    }
}
