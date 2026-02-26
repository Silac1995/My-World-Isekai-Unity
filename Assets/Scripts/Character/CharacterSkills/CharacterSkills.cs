using System.Collections.Generic;
using UnityEngine;

public class CharacterSkills : MonoBehaviour
{
    [SerializeField]
    private Character _character;
    
    [SerializeField]
    private List<SkillInstance> _skills = new List<SkillInstance>();

    /// <summary>
    /// Pour recherche rapide des skills par leur SO.
    /// </summary>
    private Dictionary<SkillSO, SkillInstance> _skillMap = new Dictionary<SkillSO, SkillInstance>();

    private void Awake()
    {
        if (_character == null)
        {
            _character = GetComponent<Character>();
        }

        // Initialisation de la map
        foreach (var skill in _skills)
        {
            if (skill != null && skill.Skill != null)
            {
                if (!_skillMap.ContainsKey(skill.Skill))
                {
                    _skillMap.Add(skill.Skill, skill);
                    skill.OnLevelUp += HandleSkillLevelUp; // S'abonner aux level ups
                }
            }
        }
    }

    private void Start()
    {
        // Appliquer tous les bonus passifs initiaux (si le personnage spawn avec des niveaux de métier)
        foreach (var skill in _skills)
        {
            ApplyPassiveBonuses(skill);
        }
    }

    private void OnDestroy()
    {
        foreach (var skill in _skills)
        {
            if (skill != null)
            {
                skill.OnLevelUp -= HandleSkillLevelUp;
            }
        }
    }

    public void AddSkill(SkillSO newSkill, int startingLevel = 1)
    {
        if (newSkill == null || _skillMap.ContainsKey(newSkill)) return;

        SkillInstance instance = new SkillInstance(newSkill, startingLevel);
        _skills.Add(instance);
        _skillMap.Add(newSkill, instance);
        
        instance.OnLevelUp += HandleSkillLevelUp;
        
        ApplyPassiveBonuses(instance);
    }

    public bool HasSkill(SkillSO skill)
    {
        if (skill == null) return false;
        return _skillMap.ContainsKey(skill);
    }

    public int GetSkillLevel(SkillSO skill)
    {
        if (skill == null || !_skillMap.TryGetValue(skill, out var instance)) return 0;
        return instance.Level;
    }

    /// <summary>
    /// Retourne l'Efficacité finale (Niveau du métier + Bonus des Statistiques (Agi, Str, etc.)).
    /// </summary>
    public float GetSkillProficiency(SkillSO skill)
    {
        if (skill == null || !_skillMap.TryGetValue(skill, out var instance)) return 0f;
        return instance.CalculateProficiency(_character.Stats);
    }

    public bool HasRequiredSkillLevel(SkillSO skill, int requiredLevel)
    {
        if (skill == null) return false;
        if (!_skillMap.TryGetValue(skill, out var instance)) return false;

        return instance.Level >= requiredLevel;
    }

    public void GainXP(SkillSO skill, int amount)
    {
        if (skill == null) return;

        if (_skillMap.TryGetValue(skill, out var instance))
        {
            instance.AddXP(amount);
        }
        else
        {
            // Le personnage apprend la compétence lors du premier gain d'XP
            AddSkill(skill, 1);
            _skillMap[skill].AddXP(amount);
        }
    }

    /// <summary>
    /// Appelé par l'event OnLevelUp d'un SkillInstance
    /// </summary>
    private void HandleSkillLevelUp(SkillInstance skillInstance, int newLevel)
    {
        Debug.Log($"<color=cyan>[CharacterSkills]</color> {_character.CharacterName} est passé niveau {newLevel} en {skillInstance.Skill.SkillName}.");
        ApplyPassiveBonuses(skillInstance);
        
        // Comme on a ajouté/modifié des statistiques de base, on demande un recalcul des stats tertiaires/dynamiques
        _character.Stats.RecalculateTertiaryStats(); 
    }

    /// <summary>
    /// Parcourt la liste des Bonus définis dans le SkillSO et les applique aux BaseStats du personnage 
    /// s'il a atteint le niveau requis.
    /// (Note : Pour éviter le double-stack à chaque appel, un système plus complexe d'Identifiants de modificateurs 
    /// serait mieux, mais pour l'instant on ajoute directement à la BaseValue de la stat).
    /// </summary>
    private void ApplyPassiveBonuses(SkillInstance instance)
    {
        if (instance == null || instance.Skill == null || instance.Skill.LevelBonuses == null) return;
        if (_character.Stats == null) return;

        foreach (var bonus in instance.Skill.LevelBonuses)
        {
            // On vérifie de façon simpliste : si c'est exactement le niveau, on donne le bonus une fois
            // (Si on appelait ça dans le Start() avec un level 10 d'un coup, on parcourrait jusqu'au niveau 10).
            // Pour être parfaitement juste même lors de chargement de sauvegarde, il faudrait tracker quels bonus ont déjà été appliqués.
            // Par souci de simplicité pour le moment (LevelUp en temps réel), on applique juste quand le niveau est atteint.
            
            if (bonus.RequiredLevel == instance.Level)
            {
                var statToBoost = _character.Stats.GetBaseStat(bonus.StatToBoost);
                if (statToBoost != null)
                {
                    // Ajout permanent du bonus à la base stat
                    statToBoost.SetBaseValue(statToBoost.BaseValue + bonus.BonusValue);
                    Debug.Log($"<color=green>[CharacterSkills]</color> Bonus appliqué : +{bonus.BonusValue} en {bonus.StatToBoost}.");
                }
            }
        }
    }
}
