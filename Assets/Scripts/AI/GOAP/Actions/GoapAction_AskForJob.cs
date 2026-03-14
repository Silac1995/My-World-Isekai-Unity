using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Action GOAP : Demander un job à un patron.
/// </summary>
public class GoapAction_AskForJob : GoapAction
{
    public override string ActionName => "AskForJob";

    public override Dictionary<string, bool> Preconditions => new Dictionary<string, bool>
    {
        { "atBossLocation", true },
        { "hasJob", false }
    };

    public override Dictionary<string, bool> Effects => new Dictionary<string, bool>
    {
        { "hasJob", true }
    };

    public override float Cost => 5f;

    private CommercialBuilding _building;
    private Job _job;
    private bool _isComplete = false;

    public override bool IsComplete => _isComplete;

    public GoapAction_AskForJob(CommercialBuilding building, Job job)
    {
        _building = building;
        _job = job;
    }

    public override bool IsValid(Character worker)
    {
        return _building != null && _job != null && !_job.IsAssigned && _building.HasOwner;
    }

    public override void Execute(Character worker)
    {
        if (_isComplete) return;

        // If we haven't started yet, trigger the interaction
        if (!worker.CharacterInteraction.IsInteracting)
        {
            Character boss = _building.Owner;
            if (boss == null)
            {
                _isComplete = true; // Fail gracefully
                return;
            }

            var interaction = new InteractionAskForJob(_building, _job);
            worker.CharacterInteraction.StartInteractionWith(boss, interaction);
        }
        else
        {
            // The action is only complete once the interaction itself finishes.
            // This prevents the GOAP block from terminating early and the BT
            // falling back to 'Wander' while we are talking.
            _isComplete = !worker.CharacterInteraction.IsInteracting;
        }
    }
}
