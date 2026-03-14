# Character Needs Patterns

## Sequential Resolution Logic (Reference)

```csharp
// Current standard for EvaluateNeeds/BTCond_HasUrgentNeed
var activeNeeds = allNeeds
    .Where(n => n.IsActive())
    .OrderByDescending(n => n.GetUrgency())
    .ToList();

foreach (var need in activeNeeds)
{
    if (need.Resolve(npc))
    {
        // One resolution started, stop here to avoid behavior stack explosion
        return true; 
    }
}
```

## Creating a Resolvable Need

A need is only as good as its `Resolve` logic. Always use `PushBehaviour` and provide a callback for when the character arrives at its destination.

```csharp
public override bool Resolve(NPCController npc)
{
    // 1. Guard against re-resolving if already moving
    if (npc.HasBehaviour<MoveToTargetBehaviour>()) return false;

    // 2. Find a target
    var target = FindTarget();
    if (target == null) return false;

    // 3. Initiate resolution behavior
    npc.PushBehaviour(new MoveToTargetBehaviour(npc, target, arrivalDistance, () => {
        // 4. Start actual interaction/action upon arrival
        StartResolutionInteraction(npc, target);
    }));

    return true;
}
```

## Urgency Scaling
Usually, urgency is `100 - _currentValue`, but it can be weighted by personality traits to make some NPCs focus more on certain needs than others.
