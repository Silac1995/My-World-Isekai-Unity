# GOAP Action Pattern
This example demonstrates how to create a proper `GoapAction` that integrates correctly with the `CharacterGoapController`, specifically handling the `IsInteractionProcessActive` flag when initiating interactions.

```csharp
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Example of a GoapAction where an NPC finds an item or interacts with someone.
/// </summary>
public class GoapAction_ExampleTask : GoapAction
{
    public override string ActionName => "ExampleTask";

    // What this action requires before it can run
    public override Dictionary<string, bool> Preconditions => new Dictionary<string, bool>
    {
        { "hasTarget", true }
    };

    // What this action achieves when completed
    public override Dictionary<string, bool> Effects => new Dictionary<string, bool>
    {
        { "isTaskComplete", true }
    };

    public override float Cost => 1f;

    // Use internal state flags to track progress across frames
    private bool _isComplete = false;
    private bool _hasStartedInteraction = false;
    private Character _targetCharacter;

    public override bool IsComplete => _isComplete;

    public GoapAction_ExampleTask(Character target)
    {
        _targetCharacter = target;
    }

    public override bool IsValid(Character worker)
    {
        // The action is invalid if it's already done or the target is missing/dead
        if (_isComplete) return false;
        if (_targetCharacter == null || !_targetCharacter.IsAlive()) return false;
        
        return true;
    }

    public override void Execute(Character worker)
    {
        if (_isComplete) return;

        // 1. Pre-Interaction: Waiting for the interaction to fully conclude
        // CRITICAL: We use IsInteractionProcessActive to ensure the GOAP action does NOT
        // complete while the character is still walking towards the target or talking.
        if (_hasStartedInteraction)
        {
            if (!worker.CharacterInteraction.IsInteractionProcessActive)
            {
                // The interaction is completely finished (they walked there, talked, and the bubble popped)
                _isComplete = true;
            }
            return; // Stay in the Execute loop until the interaction is over
        }

        // 2. Start the Interaction
        // If we haven't started yet, we initiate the interaction now.
        if (worker.CharacterInteraction.StartInteractionWith(_targetCharacter))
        {
            _hasStartedInteraction = true;
            Debug.Log($"<color=yellow>[GOAP Example]</color> {worker.CharacterName} started interacting with {_targetCharacter.CharacterName}.");
        }
        else
        {
            // Failed to start interaction (e.g. target is busy)
            _isComplete = true; // Complete immediately so the GOAP planner can Replan
        }
    }

    public override void Exit(Character worker)
    {
        // Cleanup state for the next time this action might be instantiated
        _isComplete = false;
        _hasStartedInteraction = false;
    }
}
```
