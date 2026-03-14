# Design Doc: GOAP-based Needs & Lifecycle

Currently, the `CharacterNeeds` system is reactive and hardcoded. Introducing GOAP allows NPCs to plan multi-step sequences to satisfy their drives and life goals.

## Proposed Architecture

### 1. `CharacterGoapController` (New Component)
A central hub on each NPC that manages:
- **World State**: Aggregates data from `CharacterNeeds`, `CharacterStats`, and `CharacterAwareness`.
- **Goal Selection**: Picks the most urgent `GoapGoal` (Needs or Life Goals).
- **Planner Integration**: Ticks the `GoapPlanner` and executes the resulting `GoapAction` queue.

### 2. "Life" Actions (New Category)
Actions that are always available (not tied to a specific Job instance):
- `GoapAction_AskForJob`: Handles the hiring interaction.
- `GoapAction_GoToLocation`: Generic navigation to satisfy preconditions.
- `GoapAction_Socialize`: satisfy social need by finding a partner.

### 3. Example: The Job Search Chain

**World State:**
- `hasJob`: `false`
- `knowsVacantJob`: `true` (updated by `NeedJob` sensor)
- `atBossLocation`: `false`

**Goal: `{"hasJob": true}`**

**Planner Result:**
1. `Action_GoToBoss` (Pre: `knowsVacantJob`, Effect: `atBossLocation`)
2. `Action_AskForJob` (Pre: `atBossLocation`, Effect: `hasJob`)

## Benefits
- **Emergent Behavior**: If the Boss is busy, the `Action_AskForJob` fails, and the planner might decide to "Wait" or "Socialize" while waiting, rather than just standing still.
- **Trait Integration**: "Ambitious" NPCs could have a higher priority for the `hasJob` goal.
- **Decoupling**: `NeedJob.cs` becomes a pure "Sensor" that updates the World State, instead of handling navigation and interaction itself.

## Draft: `GoapAction_AskForJob.cs`

```csharp
public class GoapAction_AskForJob : GoapAction
{
    public override string ActionName => "Ask For Job";
    
    public override Dictionary<string, bool> Preconditions => new Dictionary<string, bool> {
        { "atBossLocation", true },
        { "hasJob", false }
    };

    public override Dictionary<string, bool> Effects => new Dictionary<string, bool> {
        { "hasJob", true }
    };

    private bool _isComplete = false;
    public override bool IsComplete => _isComplete;

    public override bool IsValid(Character worker) {
        // Check if boss is still there and job is still vacant
        return true; 
    }

    public override void Execute(Character worker) {
        // Trigger the InteractionAskForJob
        _isComplete = true; // or wait for interaction result
    }
}
```
