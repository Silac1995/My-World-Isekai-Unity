using UnityEngine;

/// <summary>
/// Physical interaction to ask a boss for a job.
/// Inherits from InteractionInvitation to simulate the boss's deliberation delay.
/// </summary>
public class InteractionAskForJob : InteractionInvitation
{
    private CommercialBuilding _building;
    private Job _job;

    public InteractionAskForJob(CommercialBuilding building, Job job)
    {
        _building = building;
        _job = job;
    }

    public override bool CanExecute(Character source, Character target)
    {
        // Must not already have a job
        if (source.CharacterJob != null && source.CharacterJob.HasJob)
            return false;

        // The interaction can only run if the building has a boss and the job is still available.
        return _building != null && _job != null && _building.HasOwner && !_job.IsAssigned;
    }

    public override string GetInvitationMessage(Character source, Character target)
    {
        return $"Hello, do you still need a {_job.JobTitle}?";
    }

    public override bool? EvaluateCustomInvitation(Character source, Character target)
    {
        // Sociability does not matter here — only the professional evaluation (AskForJob) counts.
        // target (the boss) evaluates source (the applicant).
        if (_building.AskForJob(source, _job))
        {
            return true;
        }

        return false;
    }

    public override void OnAccepted(Character source, Character target)
    {
        // On success, run the official hiring step.
        if (source.CharacterJob != null)
        {
            if (source.CharacterJob.TakeJob(_job, _building))
            {
                Debug.Log($"<color=green>[InteractionAskForJob]</color> {source.CharacterName} was physically hired by {target.CharacterName}.");
            }
        }
    }

    public override string GetAcceptMessage()
    {
        return $"You're hired! Welcome to the team.";
    }

    public override string GetRefuseMessage()
    {
        return $"Sorry, you're not the profile we're looking for.";
    }

    public override void OnRefused(Character source, Character target)
    {
        Debug.Log($"<color=orange>[InteractionAskForJob]</color> {source.CharacterName} was rejected by {target.CharacterName}.");
        // We could add a morale debuff here later.
    }
}
