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

        SyncSkillsFromInspector();
    }

    /// <summary>
    /// Permet de supporter l'ajout manuel de skills hors-code depuis l'inspecteur d'Unity (même en Play Mode).
    /// </summary>
    private void OnValidate()
    {
        if (Application.isPlaying)
        {
            SyncSkillsFromInspector();
        }
    }

    private Dictionary<CharacterBaseStats, float> _appliedSkillBonuses = new Dictionary<CharacterBaseStats, float>();

    private void SyncSkillsFromInspector()
    {
        if (_skillMap == null) _skillMap = new Dictionary<SkillSO, SkillInstance>();

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

        if (Application.isPlaying && _character != null && _character.Stats != null)
        {
            RecalculateAllSkillBonuses();
        }
    }

    private void Start()
    {
        // On s'assure que les bonus sont calculés au démarrage
        RecalculateAllSkillBonuses();
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
        
        RecalculateAllSkillBonuses();
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
        RecalculateAllSkillBonuses();
    }

    /// <summary>
    /// Calcule de zéro tous les bonus conférés par les Skills actuels et leurs niveaux.
    /// Utilise ApplyModifier/RemoveModifier pour ne pas écraser les stats de base liées à la race ou aux niveaux.
    /// </summary>
    private void RecalculateAllSkillBonuses()
    {
        if (_character == null || _character.Stats == null) return;

        // 1. Retirer tous les anciens modificateurs appliqués par les skills
        foreach (var stat in _appliedSkillBonuses.Keys)
        {
            stat.RemoveAllModifiersFromSource(this);
        }
        _appliedSkillBonuses.Clear();

        // 2. Calculer la somme des nouveaux bonus à appliquer
        Dictionary<CharacterBaseStats, float> newBonuses = new Dictionary<CharacterBaseStats, float>();

        foreach (var skillInstance in _skills)
        {
            if (skillInstance == null || skillInstance.Skill == null || skillInstance.Skill.LevelBonuses == null) continue;

            foreach (var bonus in skillInstance.Skill.LevelBonuses)
            {
                if (skillInstance.Level >= bonus.RequiredLevel) // On applique tous les bonus débloqués
                {
                    var statToBoost = _character.Stats.GetBaseStat(bonus.StatToBoost);
                    if (statToBoost != null)
                    {
                        if (!newBonuses.ContainsKey(statToBoost)) newBonuses[statToBoost] = 0f;
                        newBonuses[statToBoost] += bonus.BonusValue;
                    }
                }
            }
        }

        // 3. Appliquer les nouveaux modificateurs et les enregistrer
        foreach (var kvp in newBonuses)
        {
            kvp.Key.ApplyModifier(new StatModifier(kvp.Value, this));
            _appliedSkillBonuses[kvp.Key] = kvp.Value;
            Debug.Log($"<color=green>[CharacterSkills]</color> Modificateur total appliqué : +{kvp.Value} sur {kvp.Key.StatName}.");
        }

        // 4. Demander un recalcul global des stats tertiaires/dynamiques (HP Max, etc.)
        _character.Stats.RecalculateTertiaryStats(); 
    }
}
