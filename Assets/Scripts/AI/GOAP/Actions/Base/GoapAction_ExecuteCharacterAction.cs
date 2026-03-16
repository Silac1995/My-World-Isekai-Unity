using System;
using UnityEngine;
using MWI.AI;

/// <summary>
/// Centralizes the tracking boilerplate of invoking CharacterAction scripts and gracefully monitoring their completion in GOAP.
/// This prevents duplicating the _isComplete and null-checking for CharacterPicksUpItem/CharacterDropItem.
/// </summary>
public abstract class GoapAction_ExecuteCharacterAction : GoapAction
{
    protected bool _isComplete = false;
    protected bool _isActionStarted = false;

    public override float Cost => 1f;
    public override bool IsComplete => _isComplete;

    /// <summary>
    /// Executes the logical setup necessary to fetch what the character should do.
    /// Returns a new instance of CharacterAction to inject into the Character's State Machine.
    /// If null is returned, the GOAP action immediately completes.
    /// </summary>
    protected abstract CharacterAction PrepareAction(Character worker);

    /// <summary>
    /// Event triggered when the CharacterAction has officially finished (success or fail).
    /// Used to execute final callback logic (like updating a Job order or inventory).
    /// </summary>
    protected virtual void OnActionFinished() { }

    /// <summary>
    /// Event triggered if PrepareAction fails or if ExecuteAction rejects the CharacterAction (e.g., out of range).
    /// </summary>
    protected virtual void OnActionFailed(Character worker) { }

    public override void Execute(Character worker)
    {
        if (_isComplete) return;

        if (!_isActionStarted)
        {
            CharacterAction actionToRun = PrepareAction(worker);

            if (actionToRun == null)
            {
                OnActionFailed(worker);
                _isComplete = true; // Lost track or action nullified physically
                return;
            }

            // Hook the callback to cleanly advance GOAP when the animation physically ends
            actionToRun.OnActionFinished += () =>
            {
                OnActionFinished();
                _isComplete = true;
            };

            if (worker.CharacterActions.ExecuteAction(actionToRun))
            {
                _isActionStarted = true;
            }
            else
            {
                Debug.LogWarning($"<color=orange>[{ActionName}]</color> {worker.CharacterName} could not execute action. Target might be out of range.");
                OnActionFailed(worker);
                _isComplete = true; 
            }
        }
    }

    public override void Exit(Character worker)
    {
        _isComplete = false;
        _isActionStarted = false;
    }
}
