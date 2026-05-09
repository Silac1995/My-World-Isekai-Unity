using System;
using UnityEngine;

/// One row in UI_InteractionMenu. Returned by Harvestable.GetInteractionOptions(Character).
public readonly struct HarvestInteractionOption
{
    public readonly string Label;
    public readonly Sprite Icon;
    public readonly string OutputPreview;
    public readonly bool IsAvailable;
    public readonly string UnavailableReason;
    public readonly Func<Character, CharacterAction> ActionFactory;

    public HarvestInteractionOption(
        string label,
        Sprite icon,
        string outputPreview,
        bool isAvailable,
        string unavailableReason,
        Func<Character, CharacterAction> actionFactory)
    {
        Label = label;
        Icon = icon;
        OutputPreview = outputPreview;
        IsAvailable = isAvailable;
        UnavailableReason = unavailableReason;
        ActionFactory = actionFactory;
    }
}
