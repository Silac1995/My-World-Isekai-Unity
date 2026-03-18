using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Abstract base class for any task assigned by a CommercialBuilding (or BuildingTaskManager).
/// </summary>
public abstract class BuildingTask
{
    /// <summary>
    /// The physical target of this task in the world.
    /// </summary>
    public MonoBehaviour Target { get; protected set; }

    /// <summary>
    /// The workers who have currently claimed this task.
    /// </summary>
    public List<Character> ClaimedByWorkers { get; private set; } = new List<Character>();

    public bool IsClaimed => ClaimedByWorkers.Count > 0;
    
    /// <summary>
    /// The maximum number of workers that can claim this task simultaneously.
    /// </summary>
    public virtual int MaxWorkers => 1;

    protected BuildingTask(MonoBehaviour target)
    {
        Target = target;
    }

    /// <summary>
    /// Checks if the task is still valid and can be executed.
    /// </summary>
    public abstract bool IsValid();

    public virtual bool CanBeClaimed()
    {
        return ClaimedByWorkers.Count < MaxWorkers;
    }

    public void Claim(Character worker)
    {
        if (!ClaimedByWorkers.Contains(worker))
        {
            ClaimedByWorkers.Add(worker);
        }
    }

    public void Unclaim(Character worker)
    {
        ClaimedByWorkers.Remove(worker);
    }
}
