using UnityEngine;

/// <summary>
/// Métier de base pour tous les artisans manufacturiers (Forgerons, Tisserands...).
/// Lie le PNJ à une exigence de compétence (SkillSO) et un niveau (SkillTier).
/// S'assure que le bâtiment est un CraftingBuilding pour lancer la production via une CraftingOrder.
/// </summary>
public abstract class JobCrafter : Job
{
    [Header("Skill Requirements")]
    [SerializeField] protected SkillSO _requiredSkill;
    [SerializeField] protected SkillTier _requiredTier;

    public SkillSO RequiredSkill => _requiredSkill;
    public SkillTier RequiredTier => _requiredTier;

    public override JobCategory Category => JobCategory.Artisan;

    public JobCrafter(SkillSO requiredSkill, SkillTier requiredTier)
    {
        _requiredSkill = requiredSkill;
        _requiredTier = requiredTier;
    }

    /// <summary>
    /// Utilisé lors de l'embauche pour vérifier si le PNJ possède la compétence et le niveau requis.
    /// </summary>
    public virtual bool CheckRequirements(Character applicant)
    {
        if (applicant == null || applicant.CharacterSkills == null) return false;
        
        if (_requiredSkill == null) return true; // Pas de prérequis configuré

        if (!applicant.CharacterSkills.HasSkill(_requiredSkill))
        {
            Debug.Log($"<color=orange>[JobCrafter]</color> {applicant.CharacterName} n'a pas la compétence {_requiredSkill.SkillName} requise pour {JobTitle}.");
            return false;
        }

        int currentLevel = applicant.CharacterSkills.GetSkillLevel(_requiredSkill);
        SkillTier currentTier = SkillTierExtensions.GetTierForLevel(currentLevel);

        if (currentTier < _requiredTier)
        {
            Debug.Log($"<color=orange>[JobCrafter]</color> {applicant.CharacterName} a le tier {currentTier} en {_requiredSkill.SkillName}, mais {JobTitle} requiert {_requiredTier}.");
            return false;
        }

        return true;
    }
}
