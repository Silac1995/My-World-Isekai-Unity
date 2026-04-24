using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Centralized manager for handling BuildingTasks (Harvesting, Pickup, etc.) within a CommercialBuilding.
/// </summary>
public class BuildingTaskManager : MonoBehaviour
{
    private List<BuildingTask> _availableTasks = new List<BuildingTask>();
    private List<BuildingTask> _inProgressTasks = new List<BuildingTask>();

    public IReadOnlyList<BuildingTask> AvailableTasks => _availableTasks;
    public IReadOnlyList<BuildingTask> InProgressTasks => _inProgressTasks;

    // Quest-system publish events. Subscribed by CommercialBuilding (Task 16) to surface
    // BuildingTasks as IQuests to the player's CharacterQuestLog + NPC auto-claim path.
    public event System.Action<BuildingTask> OnTaskRegistered;
    public event System.Action<BuildingTask, Character> OnTaskClaimed;
    public event System.Action<BuildingTask, Character> OnTaskUnclaimed;
    public event System.Action<BuildingTask> OnTaskCompleted;

    /// <summary>
    /// Registers a new task if a task for the same target doesn't already exist.
    /// </summary>
    public void RegisterTask(BuildingTask newTask)
    {
        if (newTask == null || newTask.Target == null) return;

        // Ensure we don't register duplicate tasks for the same target
        bool taskExists = _availableTasks.Any(t => t.Target == newTask.Target) || 
                          _inProgressTasks.Any(t => t.Target == newTask.Target);

        if (!taskExists)
        {
            // Back-reference so BuildingTask.TryJoin/TryLeave (the IQuest path) can
            // notify us when a player claims/leaves outside the ClaimBestTask flow.
            newTask.Manager = this;
            _availableTasks.Add(newTask);
            Debug.Log($"<color=cyan>[TaskManager]</color> Task registered: {newTask.GetType().Name} for {newTask.Target.name}.");
            OnTaskRegistered?.Invoke(newTask);
        }
    }

    /// <summary>
    /// Called by <see cref="BuildingTask.TryJoin"/> after a player claims a task
    /// via the IQuest API (which bypasses <see cref="ClaimBestTask{T}"/>).
    /// Moves the task into the InProgress bucket so the debug UI and worker
    /// queries see it consistently with the NPC claim path.
    /// </summary>
    public void NotifyTaskExternallyClaimed(BuildingTask task, Character worker)
    {
        if (task == null) return;

        if (!task.CanBeClaimed())
        {
            _availableTasks.Remove(task);
        }

        if (!_inProgressTasks.Contains(task))
        {
            _inProgressTasks.Add(task);
        }

        OnTaskClaimed?.Invoke(task, worker);
    }

    /// <summary>
    /// Called by <see cref="BuildingTask.TryLeave"/> after a player abandons a
    /// task via the IQuest API. Re-evaluates the bucket so we don't leave an
    /// orphaned entry with empty ClaimedByWorkers in InProgressTasks (which the
    /// debug UI would render as "Unknown Worker") and so a fresh claimer can
    /// pick the task back up from AvailableTasks.
    /// </summary>
    public void NotifyTaskExternallyUnclaimed(BuildingTask task)
    {
        if (task == null) return;

        if (!task.IsClaimed)
        {
            _inProgressTasks.Remove(task);
        }

        if (task.IsValid() && task.CanBeClaimed() && !_availableTasks.Contains(task))
        {
            _availableTasks.Add(task);
        }
        else if (!task.IsValid())
        {
            _availableTasks.Remove(task);
        }
    }

    /// <summary>
    /// Finds the closest valid task of type T, claims it for the worker, and moves it to in-progress.
    /// </summary>
    public T ClaimBestTask<T>(Character worker, System.Predicate<T> predicate = null) where T : BuildingTask
    {
        // Clean up invalid tasks first (e.g. items destroyed outside of tasks)
        _availableTasks.RemoveAll(t => !t.IsValid());

        T bestTask = null;
        float bestDist = float.MaxValue;

        foreach (var task in _availableTasks.OfType<T>())
        {
            if (task.IsValid() && task.CanBeClaimed() && (predicate == null || predicate(task)))
            {
                float dist = Vector3.Distance(worker.transform.position, task.Target.transform.position);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestTask = task;
                }
            }
        }

        if (bestTask != null)
        {
            bestTask.Claim(worker);
            
            if (!bestTask.CanBeClaimed())
            {
                _availableTasks.Remove(bestTask);
            }
            
            if (!_inProgressTasks.Contains(bestTask))
            {
                _inProgressTasks.Add(bestTask);
            }
            Debug.Log($"<color=green>[TaskManager]</color> {worker.CharacterName} claimed task {typeof(T).Name} for {bestTask.Target.name}.");
            OnTaskClaimed?.Invoke(bestTask, worker);
        }

        return bestTask;
    }

    /// <summary>
    /// Removes the claim on a task and puts it back in the available pool.
    /// </summary>
    public void UnclaimTask(BuildingTask task, Character worker)
    {
        if (task != null && _inProgressTasks.Contains(task))
        {
            task.Unclaim(worker);

            if (!task.IsClaimed)
            {
                _inProgressTasks.Remove(task);
            }

            if (task.IsValid() && task.CanBeClaimed() && !_availableTasks.Contains(task))
            {
                _availableTasks.Add(task);
                Debug.Log($"<color=orange>[TaskManager]</color> Task unclaimed and returned to pool: {task.GetType().Name} for {task.Target.name}.");
            }
            else if (!task.IsValid())
            {
                Debug.Log($"<color=orange>[TaskManager]</color> Task unclaimed but target invalid, discarded: {task.GetType().Name}.");
            }

            OnTaskUnclaimed?.Invoke(task, worker);
        }
    }

    /// <summary>
    /// Marks a task as complete and removes it from the system entirely.
    /// </summary>
    public void CompleteTask(BuildingTask task)
    {
        if (task != null)
        {
            _inProgressTasks.Remove(task);
            _availableTasks.Remove(task);
            Debug.Log($"<color=green>[TaskManager]</color> Task completed: {task.GetType().Name}.");
            OnTaskCompleted?.Invoke(task);
        }
    }

    /// <summary>
    /// Removes any task associated with a specific target.
    /// </summary>
    public void UnregisterTaskByTarget(MonoBehaviour target)
    {
        if (target == null) return;

        _availableTasks.RemoveAll(t => t.Target == target);
        _inProgressTasks.RemoveAll(t => t.Target == target);
    }

    /// <summary>
    /// Checks if there is any available or currently claimed (by this worker) task of the specified type.
    /// </summary>
    public bool HasAvailableOrClaimedTask<T>(Character worker = null, System.Predicate<T> predicate = null) where T : BuildingTask
    {
        bool hasAvailable = _availableTasks.OfType<T>().Any(t => t.IsValid() && t.CanBeClaimed() && (predicate == null || predicate(t)));
        if (hasAvailable) return true;

        if (worker != null)
        {
            return _inProgressTasks.OfType<T>().Any(t => t.ClaimedByWorkers.Contains(worker) && t.IsValid() && (predicate == null || predicate(t)));
        }

        return false;
    }

    /// <summary>
    /// Checks if there is ANY registered task of the specified type, whether available or currently claimed by someone.
    /// This is useful to distinguish between "No available tasks right now" and "No tasks exist at all".
    /// </summary>
    public bool HasAnyTaskOfType<T>(System.Predicate<T> predicate = null) where T : BuildingTask
    {
        bool hasAvailable = _availableTasks.OfType<T>().Any(t => t.IsValid() && (predicate == null || predicate(t)));
        if (hasAvailable) return true;

        bool hasInProgress = _inProgressTasks.OfType<T>().Any(t => t.IsValid() && (predicate == null || predicate(t)));
        return hasInProgress;
    }

    /// <summary>
    /// Clears all tasks of a specific type from the available queue.
    /// </summary>
    public void ClearAvailableTasksOfType<T>() where T : BuildingTask
    {
        _availableTasks.RemoveAll(t => t is T);
    }
}
