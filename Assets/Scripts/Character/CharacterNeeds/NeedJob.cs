using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class NeedJob : CharacterNeed
{
    // L'urgence peut varier en fonction de la condition du PNJ (Richesse, Faim, etc.) 
    // ou être fixe à 60 (Moyennement urgent, moins que la survie, plus que le blabla).
    private const float BASE_URGENCY = 60f;
    private float _lastJobSearchTime = -999f;
    private const float _searchCooldown = 15f; // Attendre 15 secondes avant de regénérer l'objectif si on l'annule ou le termine

    public NeedJob(Character character) : base(character)
    {
    }

    public override bool IsActive()
    {
        // Actif si le personnage est un PNJ ET qu'il n'a pas de job.
        // Optionnel : On peut exclure certaines classes (Enfants, Nobles, etc.)
        if (_character.Controller is PlayerController) return false;
        
        // Cooldown local pour ne pas harceler le boss en boucle si l'action échoue (ex: boss toujours occupé)
        if (UnityEngine.Time.time - _lastJobSearchTime < _searchCooldown) return false;
        
        return _character.CharacterJob != null && !_character.CharacterJob.HasJob;
    }

    public override float GetUrgency()
    {
        return BASE_URGENCY;
    }

    public override GoapGoal GetGoapGoal()
    {
        return new GoapGoal("FindJob", new Dictionary<string, bool> { { "hasJob", true } }, (int)GetUrgency());
    }

    public override List<GoapAction> GetGoapActions()
    {
        List<GoapAction> actions = new List<GoapAction>();

        if (BuildingManager.Instance != null)
        {
            var (building, job) = BuildingManager.Instance.FindAvailableJob<Job>(true);
            if (building != null && job != null)
            {
                Debug.Log($"<color=yellow>[NeedJob]</color> Found job '{job.JobTitle}' at '{building.BuildingName}' with boss '{building.Owner?.CharacterName}'. Generating Actions.");
                actions.Add(new GoapAction_GoToBoss(building.Owner));
                actions.Add(new GoapAction_AskForJob(building, job));
            }
            else
            {
                Debug.LogWarning($"<color=orange>[NeedJob]</color> FindAvailableJob(true) returned null for both building and job! No boss-owned vacant jobs found in BuildingManager.");
            }
        }

        // On a généré des actions (ou on a cherché en vain), on déclenche le cooldown pour laisser respirer le GOAP
        _lastJobSearchTime = UnityEngine.Time.time;

        return actions;
    }
}
