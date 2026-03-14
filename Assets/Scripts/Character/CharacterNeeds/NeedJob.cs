using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class NeedJob : CharacterNeed
{
    // L'urgence peut varier en fonction de la condition du PNJ (Richesse, Faim, etc.) 
    // ou être fixe à 60 (Moyennement urgent, moins que la survie, plus que le blabla).
    private const float BASE_URGENCY = 60f;

    public NeedJob(Character character) : base(character)
    {
    }

    public override bool IsActive()
    {
        // Actif si le personnage est un PNJ ET qu'il n'a pas de job.
        // Optionnel : On peut exclure certaines classes (Enfants, Nobles, etc.)
        if (_character.Controller is PlayerController) return false;
        
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
            // Note: In a future iteration we could abstract this out into "BecomeOwnerAction" etc.
            var (building, job) = BuildingManager.Instance.FindAvailableJob<Job>();
            if (building != null && building.HasOwner && job != null)
            {
                actions.Add(new GoapAction_GoToBoss(building.Owner));
                actions.Add(new GoapAction_AskForJob(building, job));
            }
        }

        return actions;
    }
}
