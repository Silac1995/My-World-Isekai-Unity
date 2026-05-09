using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Centralized manager for handling BuildingTasks (Harvesting, Pickup, etc.) within a CommercialBuilding.
///
/// HOT PATH: <see cref="ClaimBestTask{T}"/> is called by every active worker on every GOAP plan cycle.
/// With N workers and frequent task churn, GC pressure here compounds fast — so this file avoids all
/// LINQ (`.OfType`/`.Any`/`.RemoveAll`-with-predicate) which allocates closures/enumerators per call.
/// All per-event `Debug.Log` calls are gated behind <see cref="NPCDebug.VerboseJobs"/> because the
/// Unity Editor console fills up progressively under worker activity and progressively stalls the host.
/// </summary>
public class BuildingTaskManager : MonoBehaviour
{
    private readonly List<BuildingTask> _availableTasks = new List<BuildingTask>();
    private readonly List<BuildingTask> _inProgressTasks = new List<BuildingTask>();

    public IReadOnlyList<BuildingTask> AvailableTasks => _availableTasks;
    public IReadOnlyList<BuildingTask> InProgressTasks => _inProgressTasks;

    // Quest-system publish events. Subscribed by CommercialBuilding (Task 16) to surface
    // BuildingTasks as IQuests to the player's CharacterQuestLog + NPC auto-claim path.
    public event System.Action<BuildingTask> OnTaskRegistered;
    public event System.Action<BuildingTask, Character> OnTaskClaimed;
    public event System.Action<BuildingTask, Character> OnTaskUnclaimed;
    public event System.Action<BuildingTask> OnTaskCompleted;

    /// <summary>
    /// Registers a new task. For tasks with a non-null <see cref="BuildingTask.Target"/>
    /// (legacy MonoBehaviour-targeted tasks like HarvestResourceTask, PickupLooseItemTask),
    /// duplicate registration is dedup'd by reference equality on Target. Cell-targeted
    /// tasks (PlantCropTask, WaterCropTask) pass null Target — the building's own
    /// HasExistingPlantTaskForCell / HasExistingWaterTaskForCell checks handle dedup
    /// at the call site, and subclass IsValid() handles validity.
    /// </summary>
    public void RegisterTask(BuildingTask newTask)
    {
        if (newTask == null) return;

        // Manual duplicate-target check — avoids two LINQ `.Any(...)` closure allocations per call.
        // For null-target (cell-targeted) tasks, ContainsTargetInList returns false so registration
        // proceeds — call sites must dedup themselves before calling RegisterTask.
        if (ContainsTargetInList(_availableTasks, newTask)) return;
        if (ContainsTargetInList(_inProgressTasks, newTask)) return;

        // Back-reference so BuildingTask.TryJoin/TryLeave (the IQuest path) can
        // notify us when a player claims/leaves outside the ClaimBestTask flow.
        newTask.Manager = this;
        _availableTasks.Add(newTask);
        if (NPCDebug.VerboseJobs)
        {
            string targetName = newTask.Target != null ? newTask.Target.name : newTask.GetType().Name;
            Debug.Log($"<color=cyan>[TaskManager]</color> Task registered: {newTask.GetType().Name} for {targetName}.");
        }
        OnTaskRegistered?.Invoke(newTask);
    }

    /// <summary>
    /// Walks the list looking for a task with the same legacy MonoBehaviour Target.
    /// Cell-targeted tasks (null Target) are NOT considered duplicates by target —
    /// their dedup is the caller's responsibility (see <see cref="RegisterTask"/> docs).
    /// </summary>
    private static bool ContainsTargetInList(List<BuildingTask> list, BuildingTask candidate)
    {
        if (list == null || candidate == null) return false;
        if (candidate.Target == null) return false;   // null-target tasks: no target-based dedup
        for (int i = 0; i < list.Count; i++)
        {
            var existing = list[i];
            if (existing != null && existing.Target == candidate.Target) return true;
        }
        return false;
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
    /// Called every GOAP plan cycle per worker — kept allocation-free on the hot path.
    /// </summary>
    public T ClaimBestTask<T>(Character worker, System.Predicate<T> predicate = null) where T : BuildingTask
    {
        // Drop invalid tasks in place — avoids the predicate+closure allocation of List.RemoveAll.
        for (int i = _availableTasks.Count - 1; i >= 0; i--)
        {
            if (!_availableTasks[i].IsValid())
                _availableTasks.RemoveAt(i);
        }

        T bestTask = null;
        float bestDist = float.MaxValue;
        Vector3 workerPos = worker.transform.position;

        // Manual loop + type-check replaces `.OfType<T>()` (which allocates an enumerator wrapper).
        for (int i = 0; i < _availableTasks.Count; i++)
        {
            if (!(_availableTasks[i] is T task)) continue;
            if (!task.IsValid() || !task.CanBeClaimed()) continue;
            if (predicate != null && !predicate(task)) continue;

            // Cell-targeted tasks (null Target) resolve their world position via the
            // virtual GetTaskWorldPosition() override — falls back to Target.transform.position
            // for legacy MonoBehaviour-targeted tasks.
            float dist = Vector3.Distance(workerPos, task.GetTaskWorldPosition());
            if (dist < bestDist)
            {
                bestDist = dist;
                bestTask = task;
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
            if (NPCDebug.VerboseJobs)
            {
                string targetName = bestTask.Target != null ? bestTask.Target.name : bestTask.GetType().Name;
                Debug.Log($"<color=green>[TaskManager]</color> {worker.CharacterName} claimed task {typeof(T).Name} for {targetName}.");
            }
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
                if (NPCDebug.VerboseJobs)
                {
                    string targetName = task.Target != null ? task.Target.name : task.GetType().Name;
                    Debug.Log($"<color=orange>[TaskManager]</color> Task unclaimed and returned to pool: {task.GetType().Name} for {targetName}.");
                }
            }
            else if (!task.IsValid())
            {
                if (NPCDebug.VerboseJobs)
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
            if (NPCDebug.VerboseJobs)
                Debug.Log($"<color=green>[TaskManager]</color> Task completed: {task.GetType().Name}.");
            OnTaskCompleted?.Invoke(task);
        }
    }

    /// <summary>
    /// Removes any task associated with a specific target. Manual loops avoid the
    /// predicate+closure allocation of List.RemoveAll.
    /// </summary>
    public void UnregisterTaskByTarget(MonoBehaviour target)
    {
        if (target == null) return;

        for (int i = _availableTasks.Count - 1; i >= 0; i--)
        {
            if (_availableTasks[i].Target == target) _availableTasks.RemoveAt(i);
        }
        for (int i = _inProgressTasks.Count - 1; i >= 0; i--)
        {
            if (_inProgressTasks[i].Target == target) _inProgressTasks.RemoveAt(i);
        }
    }

    /// <summary>
    /// Returns the in-progress task of type T already claimed by <paramref name="worker"/>, if any.
    /// Used by GOAP actions whose worker had a task pre-claimed by the quest-system auto-claim path
    /// (<see cref="CommercialBuilding.TryAutoClaimExistingQuests"/> on <c>WorkerStartingShift</c>) —
    /// without this lookup, the GoapAction's <c>ClaimBestTask</c> only walks <c>_availableTasks</c>
    /// and returns null even though the worker DOES have a valid claim sitting in
    /// <c>_inProgressTasks</c>, causing an Idle/Action ping-pong loop. Mirrors the InProgress branch
    /// of <see cref="HasAvailableOrClaimedTask{T}"/> so the two stay in sync semantically.
    /// </summary>
    public T FindClaimedTaskByWorker<T>(Character worker, System.Predicate<T> predicate = null) where T : BuildingTask
    {
        if (worker == null) return null;
        for (int i = 0; i < _inProgressTasks.Count; i++)
        {
            if (!(_inProgressTasks[i] is T task)) continue;
            if (!task.ClaimedByWorkers.Contains(worker)) continue;
            if (!task.IsValid()) continue;
            if (predicate != null && !predicate(task)) continue;
            return task;
        }
        return null;
    }

    /// <summary>
    /// Checks if there is any available or currently claimed (by this worker) task of the specified type.
    /// Manual loops replace `.OfType<T>().Any(...)` which allocates an enumerator + predicate closure per call.
    /// </summary>
    public bool HasAvailableOrClaimedTask<T>(Character worker = null, System.Predicate<T> predicate = null) where T : BuildingTask
    {
        for (int i = 0; i < _availableTasks.Count; i++)
        {
            if (!(_availableTasks[i] is T task)) continue;
            if (!task.IsValid() || !task.CanBeClaimed()) continue;
            if (predicate != null && !predicate(task)) continue;
            return true;
        }

        if (worker != null)
        {
            for (int i = 0; i < _inProgressTasks.Count; i++)
            {
                if (!(_inProgressTasks[i] is T task)) continue;
                if (!task.ClaimedByWorkers.Contains(worker)) continue;
                if (!task.IsValid()) continue;
                if (predicate != null && !predicate(task)) continue;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if there is ANY registered task of the specified type, whether available or currently claimed.
    /// Useful to distinguish between "no available tasks right now" and "no tasks exist at all".
    /// </summary>
    public bool HasAnyTaskOfType<T>(System.Predicate<T> predicate = null) where T : BuildingTask
    {
        for (int i = 0; i < _availableTasks.Count; i++)
        {
            if (!(_availableTasks[i] is T task)) continue;
            if (!task.IsValid()) continue;
            if (predicate != null && !predicate(task)) continue;
            return true;
        }
        for (int i = 0; i < _inProgressTasks.Count; i++)
        {
            if (!(_inProgressTasks[i] is T task)) continue;
            if (!task.IsValid()) continue;
            if (predicate != null && !predicate(task)) continue;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Clears all tasks of a specific type from the available queue.
    /// </summary>
    public void ClearAvailableTasksOfType<T>() where T : BuildingTask
    {
        for (int i = _availableTasks.Count - 1; i >= 0; i--)
        {
            if (_availableTasks[i] is T) _availableTasks.RemoveAt(i);
        }
    }
}
