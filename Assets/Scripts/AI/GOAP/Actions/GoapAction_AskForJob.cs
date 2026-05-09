using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// GOAP action: ask a boss for a job.
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
    private bool _hasStartedInteraction = false;
    private float _waitStartTime = 0f;
    private bool _isWaitingForBoss = false;

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

        if (!worker.CharacterInteraction.IsInteractionProcessActive)
        {
            if (_hasStartedInteraction)
            {
                // The interaction finished (or timed out).
                _isComplete = true;
                return;
            }

            Character boss = _building.Owner;
            if (boss == null || _job.IsAssigned)
            {
                _isComplete = true; // Fail gracefully
                return;
            }

            // Wait patiently for the boss to become free instead of replanning infinitely.
            if (!boss.IsFree())
            {
                if (!_isWaitingForBoss)
                {
                    _isWaitingForBoss = true;
                    _waitStartTime = UnityEngine.Time.time;
                }
                else if (UnityEngine.Time.time - _waitStartTime > 10f)
                {
                    Debug.LogWarning($"<color=orange>[Interaction]</color> {worker.CharacterName} waited too long to speak with {boss.CharacterName}. Giving up on the job.");
                    _isComplete = true; // Give up
                }
                return;
            }

            _isWaitingForBoss = false;

            var interaction = new InteractionAskForJob(_building, _job);
            if (worker.CharacterInteraction.StartInteractionWith(boss, interaction))
            {
                _hasStartedInteraction = true;
            }
        }
    }

    public override void Exit(Character worker)
    {
        _isWaitingForBoss = false;
        _isComplete = false;
        _hasStartedInteraction = false;
    }
}
