using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Represents a building-local crafting commission.
/// Used by JobLogisticsManager to forward production needs to artisans (JobCrafter).
/// CustomerBuilding is the final destination (e.g. the ShopBuilding that placed the order).
/// Workshop is the building where the craft happens (the artisan's workplace) — used as
/// the IQuest target so contributors know where to physically perform the work.
/// </summary>
[System.Serializable]
public class CraftingOrder : MWI.Quests.IQuest
{
    public ItemSO ItemToCraft { get; private set; }
    public int Quantity { get; private set; }
    public int RemainingDays { get; private set; }
    public Character ClientBoss { get; private set; }
    public CommercialBuilding CustomerBuilding { get; private set; }
    public CommercialBuilding Workshop { get; private set; }

    // Quantity already crafted
    public int CraftedQuantity { get; private set; }

    public bool IsCompleted => CraftedQuantity >= Quantity;

    // Whether the order has been officially accepted by the artisan via interaction
    public bool IsPlaced { get; set; } = false;

    public CraftingOrder(ItemSO item, int quantity, int remainingDays, Character clientBoss = null, CommercialBuilding customerBuilding = null, CommercialBuilding workshop = null)
    {
        ItemToCraft = item;
        Quantity = quantity;
        RemainingDays = remainingDays;
        ClientBoss = clientBoss;
        CustomerBuilding = customerBuilding;
        Workshop = workshop;
        CraftedQuantity = 0;
    }

    public void DecreaseRemainingDays()
    {
        RemainingDays--;
    }

    /// <summary>
    /// Records a partial or total craft completion.
    /// Returns true if the overall order is now complete.
    /// </summary>
    public bool RecordCraft(int amount)
    {
        CraftedQuantity += amount;
        return IsCompleted;
    }

    // === IQuest implementation ===

    public string QuestId { get; private set; } = System.Guid.NewGuid().ToString("N");
    public string OriginWorldId { get; set; } = string.Empty;
    public string OriginMapId { get; set; } = string.Empty;
    public Character Issuer { get; set; }
    public MWI.Quests.QuestType Type => MWI.Quests.QuestType.Job;

    public string Title => "Craft Items";
    public string InstructionLine
    {
        get
        {
            string item = ItemToCraft != null ? ItemToCraft.ItemName : "<unknown>";
            string ws = Workshop != null ? Workshop.BuildingDisplayName : "<unknown>";
            return $"Craft {Quantity} {item} at {ws}.";
        }
    }
    public string Description =>
        $"Complete a crafting commission for {Quantity} {(ItemToCraft != null ? ItemToCraft.ItemName : "<unknown>")} at {(Workshop != null ? Workshop.BuildingDisplayName : "<unknown>")}.";

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

    public int TotalProgress => CraftedQuantity;
    public int Required => Quantity;
    public int MaxContributors => int.MaxValue;  // unlimited — multiple crafters can chip on one order

    private readonly List<Character> _contributors = new List<Character>();
    private readonly Dictionary<string, int> _contribution = new Dictionary<string, int>();
    public IReadOnlyList<Character> Contributors => _contributors;
    public IReadOnlyDictionary<string, int> Contribution => _contribution;

    private MWI.Quests.IQuestTarget _target;
    public MWI.Quests.IQuestTarget Target
    {
        get
        {
            if (_target == null && Workshop != null) _target = new MWI.Quests.BuildingTarget(Workshop);
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
