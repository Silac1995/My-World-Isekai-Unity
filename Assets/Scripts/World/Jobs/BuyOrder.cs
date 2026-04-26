using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Represents a logistics order (purchase/transport) between two buildings.
/// Used by JobLogisticsManager and JobTransporter to manage the economy.
/// </summary>
[System.Serializable]
public class BuyOrder : MWI.Quests.IQuest
{
    public ItemSO ItemToTransport { get; private set; }
    public int Quantity { get; private set; }
    public CommercialBuilding Source { get; private set; }
    public CommercialBuilding Destination { get; private set; }
    public int RemainingDays { get; private set; }
    public Character ClientBoss { get; private set; }
    public Character IntermediaryBoss { get; private set; }

    // Quantity already delivered by the transporters
    public int DeliveredQuantity { get; private set; }

    // Quantity for which a TransportOrder has already been generated
    public int DispatchedQuantity { get; private set; }

    public bool IsCompleted => DeliveredQuantity >= Quantity;

    // Indicates whether the order has been officially accepted by the supplier through interaction
    public bool IsPlaced { get; set; } = false;

    // The physical ItemInstances explicitly reserved for this order from the source building's inventory
    public System.Collections.Generic.List<ItemInstance> ReservedItems { get; private set; } = new System.Collections.Generic.List<ItemInstance>();

    /// <summary>
    /// Phase-B reachability-failure counter. Incremented each time a transporter
    /// bound to this BuyOrder aborts movement because <c>NavMesh.CalculatePath</c>
    /// returned <c>PathInvalid</c> or <c>PathPartial</c>. After <see cref="MaxPathUnreachableAttempts"/>
    /// the dispatcher flags this order as reachability-stalled so the logistics tick
    /// stops re-dispatching until it expires via <c>DecreaseRemainingDays</c>.
    /// </summary>
    public int PathUnreachableCount { get; private set; } = 0;
    public const int MaxPathUnreachableAttempts = 3;
    public bool IsReachabilityStalled => PathUnreachableCount >= MaxPathUnreachableAttempts;

    /// <summary>
    /// Bump the reachability-failure counter. Returns <c>true</c> if this increment
    /// pushed the order past <see cref="MaxPathUnreachableAttempts"/> and it should
    /// now be treated as stalled (logged + left to expire naturally).
    /// </summary>
    public bool RecordPathUnreachable()
    {
        PathUnreachableCount++;
        return IsReachabilityStalled;
    }

    public BuyOrder(ItemSO item, int quantity, CommercialBuilding source, CommercialBuilding dest, int remainingDays, Character clientBoss, Character intermediaryBoss = null)
    {
        ItemToTransport = item;
        Quantity = quantity;
        Source = source;
        Destination = dest;
        RemainingDays = remainingDays;
        ClientBoss = clientBoss;
        IntermediaryBoss = intermediaryBoss;
        DeliveredQuantity = 0;
        DispatchedQuantity = 0;
    }

    public void DecreaseRemainingDays()
    {
        RemainingDays--;
    }

    /// <summary>
    /// Records a partial or full delivery.
    /// Returns true if the overall order is now complete.
    /// </summary>
    public bool RecordDelivery(int amount)
    {
        DeliveredQuantity += amount;
        return IsCompleted;
    }

    public void RecordDispatch(int amount)
    {
        DispatchedQuantity += amount;
    }

    public void AddQuantity(int amount)
    {
        if (amount > 0 && !IsPlaced)
        {
            Quantity += amount;
            Debug.Log($"<color=green>[BuyOrder]</color> Quantity increased by {amount}. New quantity: {Quantity} for {ItemToTransport.ItemName}");
        }
    }

    public void CancelDispatch(int amount)
    {
        DispatchedQuantity = Mathf.Max(0, DispatchedQuantity - amount);
    }

    public void ReserveItem(ItemInstance item)
    {
        if (item != null && !ReservedItems.Contains(item))
        {
            ReservedItems.Add(item);
        }
    }

    public void UnreserveItem(ItemInstance item)
    {
        if (item != null && ReservedItems.Contains(item))
        {
            ReservedItems.Remove(item);
        }
    }

    // === IQuest implementation ===

    public string QuestId { get; private set; } = System.Guid.NewGuid().ToString("N");
    public string OriginWorldId { get; set; } = string.Empty;
    public string OriginMapId { get; set; } = string.Empty;
    public Character Issuer { get; set; }
    public MWI.Quests.QuestType Type => MWI.Quests.QuestType.Job;

    public string Title => "Place Buy Order";
    public string InstructionLine
    {
        get
        {
            string item = ItemToTransport != null ? ItemToTransport.ItemName : "<unknown>";
            string source = Source != null ? Source.BuildingDisplayName : "<unknown>";
            return $"Procure {Quantity} {item} from {source}.";
        }
    }
    public string Description =>
        $"Place a buy order at {(Source != null ? Source.BuildingDisplayName : "<unknown>")} for {Quantity} {(ItemToTransport != null ? ItemToTransport.ItemName : "<unknown>")}.";

    public MWI.Quests.QuestState State
    {
        get
        {
            if (RemainingDays <= 0) return MWI.Quests.QuestState.Expired;
            if (IsCompleted) return MWI.Quests.QuestState.Completed;
            if (_contributors.Count >= MaxContributors) return MWI.Quests.QuestState.Full;
            return MWI.Quests.QuestState.Open;
        }
    }
    public bool IsExpired => RemainingDays <= 0;

    public int TotalProgress => DeliveredQuantity;
    public int Required => Quantity;
    public int MaxContributors => 1;  // one transporter per buy order

    private readonly List<Character> _contributors = new List<Character>();
    private readonly Dictionary<string, int> _contribution = new Dictionary<string, int>();
    public IReadOnlyList<Character> Contributors => _contributors;
    public IReadOnlyDictionary<string, int> Contribution => _contribution;

    private MWI.Quests.IQuestTarget _target;
    public MWI.Quests.IQuestTarget Target
    {
        get
        {
            if (_target == null && Source != null) _target = new MWI.Quests.BuildingTarget(Source);
            return _target;
        }
    }

    public event Action<MWI.Quests.IQuest> OnStateChanged;
    public event Action<MWI.Quests.IQuest, Character, int> OnProgressRecorded;

    public bool TryJoin(Character character)
    {
        if (character == null || _contributors.Count >= MaxContributors || _contributors.Contains(character)) return false;
        _contributors.Add(character);
        OnStateChanged?.Invoke(this);
        return true;
    }
    public bool TryLeave(Character character)
    {
        if (character == null) return false;
        bool removed = _contributors.Remove(character);
        if (removed) OnStateChanged?.Invoke(this);
        return removed;
    }
    public void RecordProgress(Character character, int amount)
    {
        if (character == null || amount <= 0) return;
        var id = character.CharacterId;
        if (string.IsNullOrEmpty(id)) return;
        _contribution.TryGetValue(id, out int prev);
        _contribution[id] = prev + amount;
        OnProgressRecorded?.Invoke(this, character, amount);
        if (IsCompleted) OnStateChanged?.Invoke(this);
    }
}
