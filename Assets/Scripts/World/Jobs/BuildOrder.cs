using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Server-only construction order placed on a CommercialBuilding's BuildingLogisticsManager.
/// Read by JobBuilder employees to know what structure to deliver materials for next.
///
/// Non-expiring (HasExpiration = false, RemainingDays = -1).
/// IsCompleted reads off TargetBuilding.IsUnderConstruction.
/// Implements IQuest so it can appear in the Quest Log and be consumed by the ambition system.
/// </summary>
[System.Serializable]
public class BuildOrder : MWI.Quests.IQuest
{
    // ─────────────────────────────────────────────────────────
    // Domain data
    // ─────────────────────────────────────────────────────────

    /// <summary>The Building that needs to be constructed.</summary>
    public Building TargetBuilding { get; private set; }

    /// <summary>The Administrative Building whose employees will fulfil this order.</summary>
    public CommercialBuilding HostBuilding { get; private set; }

    /// <summary>The NPC (city boss / mayor) who placed this order.</summary>
    public Character ClientBoss { get; private set; }

    /// <summary>In-game day on which the order was placed (for diagnostics / future expiry).</summary>
    public int PlacedOnDay { get; private set; }

    // ─────────────────────────────────────────────────────────
    // Static empty collections — never allocate per-instance
    // ─────────────────────────────────────────────────────────
    private static readonly List<Character> s_emptyContributors = new List<Character>();
    private static readonly Dictionary<string, int> s_emptyContribution = new Dictionary<string, int>();

    // ─────────────────────────────────────────────────────────
    // IQuest state
    // ─────────────────────────────────────────────────────────
    private MWI.Quests.QuestState _state;

    // ─────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────

    /// <param name="target">The Building under construction. Null → order is immediately Completed.</param>
    /// <param name="host">The Administrative Building whose roster will supply builders. May be null.</param>
    /// <param name="clientBoss">The NPC who placed this order. May be null.</param>
    /// <param name="placedOnDay">Current simulation day at placement time.</param>
    public BuildOrder(Building target, CommercialBuilding host, Character clientBoss, int placedOnDay)
    {
        TargetBuilding = target;
        HostBuilding   = host;
        ClientBoss     = clientBoss;
        PlacedOnDay    = placedOnDay;

        // QuestId: stable string keyed to the building's persistent ID, or a GUID shorthand when
        // target is null (degenerate order — will settle to Completed immediately).
        QuestId = target != null
            ? "BuildOrder_" + target.BuildingId
            : "BuildOrder_" + System.Guid.NewGuid().ToString("N").Substring(0, 8);

        _state = IsCompleted ? MWI.Quests.QuestState.Completed : MWI.Quests.QuestState.Open;
    }

    // ─────────────────────────────────────────────────────────
    // Computed properties
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// True when there is nothing left to build — either the target is gone or fully constructed.
    /// </summary>
    public bool IsCompleted => TargetBuilding == null || !TargetBuilding.IsUnderConstruction;

    /// <summary>
    /// Iterates ConstructionRequirements and yields (item, missingCount) pairs.
    /// missingCount = required - already contributed (clamped to 0 below).
    /// </summary>
    public IEnumerable<(ItemSO item, int missing)> GetMissingMaterials()
    {
        if (TargetBuilding == null) yield break;

        var reqs = TargetBuilding.ConstructionRequirements;
        if (reqs == null) yield break;

        foreach (var req in reqs)
        {
            // CraftingIngredient is a struct; only the Item field can be null.
            if (req.Item == null) continue;

            int contributed = TargetBuilding.ContributedMaterials.TryGetValue(req.Item, out int v) ? v : 0;
            int missing = req.Amount - contributed;
            if (missing > 0)
                yield return (req.Item, missing);
        }
    }

    /// <summary>
    /// Re-evaluates internal state and fires OnStateChanged if the state has changed.
    /// Must be called server-side whenever TargetBuilding.IsUnderConstruction may have changed.
    /// </summary>
    public void RefreshState()
    {
        var newState = IsCompleted ? MWI.Quests.QuestState.Completed : MWI.Quests.QuestState.Open;
        if (newState != _state)
        {
            _state = newState;
            OnStateChanged?.Invoke(this);
        }
    }

    // ─────────────────────────────────────────────────────────
    // IQuest — Identity & Origin
    // ─────────────────────────────────────────────────────────

    public string QuestId        { get; private set; }
    public string OriginWorldId  { get; set; } = string.Empty;
    public string OriginMapId    { get; set; } = string.Empty;
    public Character Issuer      => ClientBoss;
    public MWI.Quests.QuestType Type => MWI.Quests.QuestType.Custom;

    // ─────────────────────────────────────────────────────────
    // IQuest — Display data
    // ─────────────────────────────────────────────────────────

    public string Title
    {
        get
        {
            string name = TargetBuilding != null ? TargetBuilding.BuildingName : "<unknown building>";
            return $"Construct {name}";
        }
    }

    public string InstructionLine
    {
        get
        {
            string target = TargetBuilding != null ? TargetBuilding.BuildingName : "<unknown>";
            string host   = HostBuilding   != null ? HostBuilding.BuildingDisplayName : "<no workplace>";
            return $"Deliver materials to build {target} (via {host}).";
        }
    }

    public string Description
    {
        get
        {
            string boss   = ClientBoss     != null ? ClientBoss.CharacterName     : "<unknown boss>";
            string target = TargetBuilding != null ? TargetBuilding.BuildingName  : "<unknown>";
            string host   = HostBuilding   != null ? HostBuilding.BuildingDisplayName : "<no workplace>";
            return $"Order placed by {boss} on day {PlacedOnDay}. Builders from {host} are tasked with constructing {target}.";
        }
    }

    // ─────────────────────────────────────────────────────────
    // IQuest — Lifecycle
    // ─────────────────────────────────────────────────────────

    public MWI.Quests.QuestState State => _state;

    /// <summary>BuildOrder never expires on its own — returns false always.</summary>
    public bool IsExpired     => false;

    /// <summary>-1 signals "non-expiring" to consumers such as QuestSnapshotEntry.</summary>
    public int  RemainingDays => -1;

    // ─────────────────────────────────────────────────────────
    // IQuest — Progress
    // ─────────────────────────────────────────────────────────

    /// <summary>Sum of all contributed material counts so far.</summary>
    public int TotalProgress
    {
        get
        {
            if (TargetBuilding == null) return 0;
            int total = 0;
            foreach (var kvp in TargetBuilding.ContributedMaterials)
                total += kvp.Value;
            return total;
        }
    }

    /// <summary>Sum of all required material amounts from ConstructionRequirements.</summary>
    public int Required
    {
        get
        {
            if (TargetBuilding == null) return 0;
            var reqs = TargetBuilding.ConstructionRequirements;
            if (reqs == null) return 0;
            int sum = 0;
            foreach (var req in reqs)
                sum += req.Amount;
            return sum;
        }
    }

    /// <summary>
    /// Maximum number of JobBuilder employees that can work this order simultaneously.
    /// 0 in v1 when no host building is wired up.
    /// </summary>
    public int MaxContributors => HostBuilding != null ? 2 : 0;

    // ─────────────────────────────────────────────────────────
    // IQuest — Contributors (v1: implicit — not tracked here)
    // ─────────────────────────────────────────────────────────

    public IReadOnlyList<Character> Contributors                 => s_emptyContributors;
    public IReadOnlyDictionary<string, int> Contribution        => s_emptyContribution;

    // ─────────────────────────────────────────────────────────
    // IQuest — Mutations (no-ops in v1)
    // ─────────────────────────────────────────────────────────

    public bool TryJoin(Character character)                     => false;
    public bool TryLeave(Character character)                    => false;
    public void RecordProgress(Character character, int amount)  { /* v1: no-op */ }

    // ─────────────────────────────────────────────────────────
    // IQuest — Targeting
    // ─────────────────────────────────────────────────────────

    public MWI.Quests.IQuestTarget Target =>
        TargetBuilding != null ? new MWI.Quests.BuildingTarget(TargetBuilding) : null;

    // ─────────────────────────────────────────────────────────
    // IQuest — Events
    // ─────────────────────────────────────────────────────────

    public event Action<MWI.Quests.IQuest> OnStateChanged;
    public event Action<MWI.Quests.IQuest, Character, int> OnProgressRecorded;
}
