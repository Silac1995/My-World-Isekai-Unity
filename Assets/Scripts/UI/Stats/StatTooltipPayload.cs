using System.Collections.Generic;

public readonly struct StatTooltipPayload
{
    public readonly struct BreakdownLine
    {
        public readonly string Label;
        public readonly float Delta;
        public BreakdownLine(string label, float delta) { Label = label; Delta = delta; }
    }

    public readonly StatType Type;
    public readonly string DisplayName;
    public readonly float CurrentValue;
    public readonly string Description;
    public readonly string FormulaString;
    public readonly IReadOnlyList<BreakdownLine> BreakdownLines;
    public readonly string PreviewLine;

    private StatTooltipPayload(StatType type, string displayName, float currentValue,
        string description, string formulaString,
        IReadOnlyList<BreakdownLine> breakdownLines, string previewLine)
    {
        Type = type;
        DisplayName = displayName;
        CurrentValue = currentValue;
        Description = description;
        FormulaString = formulaString;
        BreakdownLines = breakdownLines;
        PreviewLine = previewLine;
    }

    public static StatTooltipPayload ForAttribute(StatType type, float currentValue,
        IReadOnlyList<BreakdownLine> breakdownLines, string previewLine)
    {
        StatDescriptionRegistry.TryGet(type, out var d);
        return new StatTooltipPayload(type, d.DisplayName, currentValue, d.Description,
            formulaString: null, breakdownLines: breakdownLines, previewLine: previewLine);
    }

    // Phase 0 recon §5d: formula string is built at runtime by the orchestrator from
    // live tertiary scaling (CharacterTertiaryStats.Multiplier / BaseOffset / LinkedStat),
    // not pulled from the registry. Caller passes the rendered string in.
    public static StatTooltipPayload ForDerived(StatType type, float currentValue,
        IReadOnlyList<BreakdownLine> breakdownLines, string formulaString)
    {
        StatDescriptionRegistry.TryGet(type, out var d);
        return new StatTooltipPayload(type, d.DisplayName, currentValue, d.Description,
            formulaString: formulaString, breakdownLines: breakdownLines, previewLine: null);
    }

    public static StatTooltipPayload ForVital(StatType type, float currentValue,
        IReadOnlyList<BreakdownLine> breakdownLines)
    {
        StatDescriptionRegistry.TryGet(type, out var d);
        return new StatTooltipPayload(type, d.DisplayName, currentValue, d.Description,
            formulaString: null, breakdownLines: breakdownLines, previewLine: null);
    }
}
