using System;
using System.Collections.Generic;
using UnityEngine;
using MWI.Quests;

/// <summary>
/// Abstract base class for any task assigned by a CommercialBuilding (or BuildingTaskManager).
/// Implements <see cref="IQuest"/> so the same primitive can be consumed by both
/// player HUDs and NPC GOAP through a unified API (Hybrid C unification).
/// </summary>
public abstract class BuildingTask : IQuest
{
    /// <summary>
    /// The physical target of this task in the world (legacy MonoBehaviour reference).
    /// IQuest's strongly-typed target is exposed via <see cref="QuestTarget"/>.
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

    /// <summary>
    /// Back-reference to the BuildingTaskManager this task is registered with.
    /// Set by <see cref="BuildingTaskManager.RegisterTask"/>; let the IQuest
    /// path (TryLeave) bookkeep the task list correctly when the player
    /// abandons via the IQuest API instead of TaskManager.UnclaimTask.
    /// </summary>
    public BuildingTaskManager Manager { get; internal set; }

    protected BuildingTask(MonoBehaviour target)
    {
        Target = target;
    }

    // === IQuest implementation ===

    public string QuestId { get; private set; } = System.Guid.NewGuid().ToString("N");
    public string OriginWorldId { get; protected set; } = string.Empty;
    public string OriginMapId { get; protected set; } = string.Empty;
    public Character Issuer { get; set; }
    public virtual MWI.Quests.QuestType Type => MWI.Quests.QuestType.Job;

    public virtual string Title => GetType().Name.Replace("Task", "");
    public virtual string InstructionLine => Title;
    public virtual string Description => Title;

    public MWI.Quests.QuestState State
    {
        get
        {
            if (!IsValid()) return MWI.Quests.QuestState.Expired;
            if (IsCompletedInternal()) return MWI.Quests.QuestState.Completed;
            if (ClaimedByWorkers.Count >= MaxWorkers) return MWI.Quests.QuestState.Full;
            return MWI.Quests.QuestState.Open;
        }
    }
    public bool IsExpired => !IsValid();
    public virtual int RemainingDays => int.MaxValue;  // BuildingTasks don't expire by days

    public virtual int TotalProgress
    {
        get { int sum = 0; foreach (var v in _contribution.Values) sum += v; return sum; }
    }
    public virtual int Required => 1;
    public int MaxContributors => MaxWorkers;

    public IReadOnlyList<Character> Contributors => ClaimedByWorkers;
    public IReadOnlyDictionary<string, int> Contribution => _contribution;
    private readonly Dictionary<string, int> _contribution = new Dictionary<string, int>();

    public IQuestTarget QuestTarget { get; protected set; }
    IQuestTarget IQuest.Target => QuestTarget;

    public event Action<IQuest> OnStateChanged;
    public event Action<IQuest, Character, int> OnProgressRecorded;

    /// <summary>Internal helper for the publisher (CommercialBuilding) to stamp world/map ids at publish time.</summary>
    public void StampOrigin(string worldId, string mapId)
    {
        OriginWorldId = worldId;
        OriginMapId = mapId;
    }

    public bool TryJoin(Character character)
    {
        if (character == null || !CanBeClaimed() || ClaimedByWorkers.Contains(character)) return false;
        Claim(character);
        OnStateChanged?.Invoke(this);
        // Mirror TryLeave: tell the manager so the InProgress / Available buckets
        // reflect the IQuest claim. Without this the player's claim only lives in
        // ClaimedByWorkers while the task stays in _availableTasks, and the debug
        // UI / worker queries don't see the player as actively working it.
        Manager?.NotifyTaskExternallyClaimed(this, character);
        return true;
    }

    public bool TryLeave(Character character)
    {
        if (character == null || !ClaimedByWorkers.Contains(character)) return false;
        Unclaim(character);
        OnStateChanged?.Invoke(this);
        // The IQuest path bypasses BuildingTaskManager.UnclaimTask; tell the manager
        // to re-evaluate this task's bucket so the InProgress list doesn't keep an
        // orphaned entry with empty ClaimedByWorkers (causes "Unknown Worker" in
        // the debug UI and prevents a fresh claimer from picking the task back up).
        Manager?.NotifyTaskExternallyUnclaimed(this);
        return true;
    }

    public void RecordProgress(Character character, int amount)
    {
        if (character == null || amount <= 0) return;
        var id = ResolveCharacterId(character);
        if (string.IsNullOrEmpty(id)) return;
        _contribution.TryGetValue(id, out int prev);
        _contribution[id] = prev + amount;
        OnProgressRecorded?.Invoke(this, character, amount);
        if (IsCompletedInternal())
        {
            OnStateChanged?.Invoke(this);
        }
    }

    /// <summary>Subclasses override to define completion (default: TotalProgress >= Required).</summary>
    protected virtual bool IsCompletedInternal() => TotalProgress >= Required;

    /// <summary>Resolve a stable id for the character. Uses Character.CharacterId (Network-synced GUID).</summary>
    private static string ResolveCharacterId(Character c)
    {
        if (c == null) return string.Empty;
        var id = c.CharacterId;
        return string.IsNullOrEmpty(id) ? c.name : id;
    }

    // === Existing methods (preserved unchanged) ===

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
