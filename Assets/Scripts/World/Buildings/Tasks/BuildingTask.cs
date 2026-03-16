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
    /// The worker who has currently claimed this task.
    /// </summary>
    public Character ClaimedBy { get; private set; }

    public bool IsClaimed => ClaimedBy != null;

    protected BuildingTask(MonoBehaviour target)
    {
        Target = target;
    }

    /// <summary>
    /// Checks if the task is still valid and can be executed.
    /// </summary>
    public abstract bool IsValid();

    public void Claim(Character worker)
    {
        ClaimedBy = worker;
    }

    public void Unclaim()
    {
        ClaimedBy = null;
    }
}
