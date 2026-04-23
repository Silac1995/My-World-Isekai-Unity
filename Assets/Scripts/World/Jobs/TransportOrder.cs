using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Logistics order representing a pure physical transport of items between two locations.
/// Segregated from BuyOrder to keep commercial and physical transactions independent (SRP).
/// </summary>
[System.Serializable]
public class TransportOrder : MWI.Quests.IQuest
{
    public ItemSO ItemToTransport { get; private set; }
    public int Quantity { get; private set; }
    public CommercialBuilding Source { get; private set; }
    public CommercialBuilding Destination { get; private set; }

    // Quantité déjà livrée par les transporteurs
    public int DeliveredQuantity { get; private set; }

    // Quantité actuellement dans les sacs/mains des transporteurs
    public int InTransitQuantity { get; private set; }

    public bool IsCompleted => DeliveredQuantity >= Quantity;

    // Indique si la commande a été officiellement acceptée par le fournisseur via interaction
    public bool IsPlaced { get; set; } = false;

    // The physical ItemInstances explicitly reserved for this order from the source building's inventory
    public System.Collections.Generic.List<ItemInstance> ReservedItems { get; private set; } = new System.Collections.Generic.List<ItemInstance>();

    public BuyOrder AssociatedBuyOrder { get; private set; }

    public TransportOrder(ItemSO item, int quantity, CommercialBuilding source, CommercialBuilding dest, BuyOrder associatedBuyOrder = null)
    {
        ItemToTransport = item;
        Quantity = quantity;
        Source = source;
        Destination = dest;
        AssociatedBuyOrder = associatedBuyOrder;
        DeliveredQuantity = 0;
        InTransitQuantity = 0;
    }

    /// <summary>
    /// Enregistre une livraison partielle ou totale.
    /// Retourne vrai si le transport global est désormais complété.
    /// </summary>
    public bool RecordDelivery(int amount)
    {
        DeliveredQuantity += amount;
        return IsCompleted;
    }

    public void AddInTransit(int amount)
    {
        InTransitQuantity += amount;
    }

    public void RemoveInTransit(int amount)
    {
        InTransitQuantity = Mathf.Max(0, InTransitQuantity - amount);
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

    public string Title => "Transport Goods";
    public string InstructionLine
    {
        get
        {
            string item = ItemToTransport != null ? ItemToTransport.ItemName : "<unknown>";
            string dest = Destination != null ? Destination.BuildingDisplayName : "<unknown>";
            return $"Deliver {Quantity} {item} to {dest}.";
        }
    }
    public string Description =>
        $"Load {Quantity} {(ItemToTransport != null ? ItemToTransport.ItemName : "<unknown>")} from {(Source != null ? Source.BuildingDisplayName : "<unknown>")} and deliver to {(Destination != null ? Destination.BuildingDisplayName : "<unknown>")}.";

    public MWI.Quests.QuestState State
    {
        get
        {
            if (IsCompleted) return MWI.Quests.QuestState.Completed;
            if (_contributors.Count >= MaxContributors) return MWI.Quests.QuestState.Full;
            return MWI.Quests.QuestState.Open;
        }
    }
    public bool IsExpired => false;
    public int RemainingDays => int.MaxValue;  // TransportOrders don't expire by days

    public int TotalProgress => DeliveredQuantity;
    public int Required => Quantity;
    public int MaxContributors => 1;  // one transporter per trip

    private readonly List<Character> _contributors = new List<Character>();
    private readonly Dictionary<string, int> _contribution = new Dictionary<string, int>();
    public IReadOnlyList<Character> Contributors => _contributors;
    public IReadOnlyDictionary<string, int> Contribution => _contribution;

    private MWI.Quests.IQuestTarget _target;
    public MWI.Quests.IQuestTarget Target
    {
        get
        {
            if (_target == null && Destination != null) _target = new MWI.Quests.BuildingTarget(Destination);
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
