using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Character Sheet window — header (name + level + XP + unspent points pill),
/// status effects icon row, vital bars, attribute grid w/ inline +button, three
/// grouped derived-stat blocks (Offense / Defense &amp; Mobility / Casting &amp;
/// Regen), and hover tooltips via the existing UI_TooltipManager singleton.
/// </summary>
/// <remarks>
/// Phase 0 recon confirmed paths:
///  - Tooltips use the existing string-based <c>UI_TooltipManager.Instance</c>
///    (no per-window tooltip prefab).
///  - Equipment stat contributions are NOT available (Path C) — tooltip shows
///    Base + Total only; bonus tint disabled.
///  - Tertiary formulas are runtime-parametric — multiplier / baseOffset /
///    linkedStat read live from <c>CharacterTertiaryStats</c> getters added in
///    Phase 1A.
/// </remarks>
public class UI_CharacterStats : UI_WindowBase
{
    [SerializeField] private Character _character;

    [Header("Header")]
    [SerializeField] private TextMeshProUGUI _nameText;
    [SerializeField] private TextMeshProUGUI _levelLineText;
    [SerializeField] private Image _xpFill;
    [SerializeField] private TextMeshProUGUI _xpText;
    [SerializeField] private GameObject _unspentPointsPill;
    [SerializeField] private TextMeshProUGUI _unspentPointsText;

    [Header("Status effects")]
    [SerializeField] private RectTransform _statusEffectsContainer;
    [SerializeField] private UI_StatusEffect _statusEffectPrefab;

    [Header("Section containers")]
    [SerializeField] private RectTransform _vitalsContainer;
    [SerializeField] private RectTransform _attributesContainer;
    [SerializeField] private RectTransform _offenseContainer;
    [SerializeField] private RectTransform _defenseContainer;
    [SerializeField] private RectTransform _castingContainer;

    [Header("Leaf prefabs")]
    [SerializeField] private UI_VitalBar _vitalBarPrefab;
    [SerializeField] private UI_AttributeCard _attributeCardPrefab;
    [SerializeField] private UI_DerivedStatRow _derivedRowPrefab;

    private static readonly StatType[] _vitals = {
        StatType.Health, StatType.Stamina, StatType.Mana, StatType.Initiative,
    };
    private static readonly StatType[] _attributes = {
        StatType.Strength, StatType.Agility, StatType.Dexterity,
        StatType.Intelligence, StatType.Endurance, StatType.Charisma,
    };
    private static readonly StatType[] _offense = {
        StatType.PhysicalPower, StatType.MagicalPower,
        StatType.Accuracy,      StatType.CriticalChance,
    };
    private static readonly StatType[] _defense = {
        StatType.Dodge, StatType.Speed,
    };
    private static readonly StatType[] _casting = {
        StatType.SpellCasting, StatType.CombatCasting,
        StatType.StaminaRegen, StatType.ManaRegen,
    };

    private readonly List<GameObject> _spawned = new List<GameObject>();
    private readonly List<UI_AttributeCard> _spawnedAttrCards = new List<UI_AttributeCard>();
    private readonly List<UI_StatusEffect> _spawnedStatusEffects = new List<UI_StatusEffect>();
    private readonly StringBuilder _tooltipSb = new StringBuilder(256);

    // Track the currently-hovered stat so we can refresh the tooltip on data change.
    private StatType? _hoveredStat;

    private void Awake()
    {
        // Defensive: ensure XP fill stays in Filled/Horizontal mode regardless
        // of any Inspector drift. Cheap one-time setup.
        if (_xpFill != null)
        {
            _xpFill.type = Image.Type.Filled;
            _xpFill.fillMethod = Image.FillMethod.Horizontal;
            _xpFill.fillOrigin = (int)Image.OriginHorizontal.Left;
        }
    }

    public void Initialize(Character character)
    {
        Unbind();
        _character = character;
        if (_character == null) { ClearRows(); ClearStatusEffects(); return; }

        BindCharacter();
        RebuildRows();
        RefreshHeader();
        RefreshStatusEffects();
    }

    private void OnEnable() { RefreshHeader(); RefreshStatusEffects(); }

    private void BindCharacter()
    {
        if (_character == null) return;
        if (_character.Stats != null)
            _character.Stats.OnStatsUpdated += HandleStatsUpdated;
        if (_character.CharacterCombatLevel != null)
        {
            _character.CharacterCombatLevel.OnLevelChanged += RefreshHeader;
            _character.CharacterCombatLevel.OnExperienceChanged += RefreshHeader;
        }
        if (_character.StatusManager != null)
        {
            _character.StatusManager.OnStatusEffectAdded += HandleStatusEffectChanged;
            _character.StatusManager.OnStatusEffectRemoved += HandleStatusEffectChanged;
        }
    }

    private void Unbind()
    {
        if (_character == null) return;
        if (_character.Stats != null)
            _character.Stats.OnStatsUpdated -= HandleStatsUpdated;
        if (_character.CharacterCombatLevel != null)
        {
            _character.CharacterCombatLevel.OnLevelChanged -= RefreshHeader;
            _character.CharacterCombatLevel.OnExperienceChanged -= RefreshHeader;
        }
        if (_character.StatusManager != null)
        {
            _character.StatusManager.OnStatusEffectAdded -= HandleStatusEffectChanged;
            _character.StatusManager.OnStatusEffectRemoved -= HandleStatusEffectChanged;
        }
    }

    private void HandleStatsUpdated()
    {
        RefreshHeader();
        // If a tooltip is open for a stat that depends on this update, refresh it.
        if (_hoveredStat.HasValue)
            OnHover(_hoveredStat.Value);
    }

    private void HandleStatusEffectChanged(CharacterStatusEffectInstance _) => RefreshStatusEffects();

    private void RebuildRows()
    {
        ClearRows();

        foreach (var s in _vitals)
            SpawnVital(s);
        foreach (var s in _attributes)
            SpawnAttribute(s);
        foreach (var s in _offense)
            SpawnDerived(s, _offenseContainer);
        foreach (var s in _defense)
            SpawnDerived(s, _defenseContainer);
        foreach (var s in _casting)
            SpawnDerived(s, _castingContainer);
    }

    private void SpawnVital(StatType s)
    {
        if (_vitalBarPrefab == null || _vitalsContainer == null) return;
        var inst = Instantiate(_vitalBarPrefab, _vitalsContainer);
        inst.gameObject.SetActive(true);
        inst.Initialize(_character, s, OnHover, OnHoverExit);
        _spawned.Add(inst.gameObject);
    }

    private void SpawnAttribute(StatType s)
    {
        if (_attributeCardPrefab == null || _attributesContainer == null) return;
        var inst = Instantiate(_attributeCardPrefab, _attributesContainer);
        inst.gameObject.SetActive(true);
        inst.Initialize(_character, s, OnUpgrade, OnHover, OnHoverExit);
        _spawned.Add(inst.gameObject);
        _spawnedAttrCards.Add(inst);
    }

    private void SpawnDerived(StatType s, RectTransform parent)
    {
        if (_derivedRowPrefab == null || parent == null) return;
        var inst = Instantiate(_derivedRowPrefab, parent);
        inst.gameObject.SetActive(true);
        inst.Initialize(_character, s, OnHover, OnHoverExit);
        _spawned.Add(inst.gameObject);
    }

    private void ClearRows()
    {
        for (int i = 0; i < _spawned.Count; i++)
        {
            var go = _spawned[i];
            if (go != null) Destroy(go);
        }
        _spawned.Clear();
        _spawnedAttrCards.Clear();
    }

    private void ClearStatusEffects()
    {
        for (int i = 0; i < _spawnedStatusEffects.Count; i++)
        {
            var ui = _spawnedStatusEffects[i];
            if (ui != null) Destroy(ui.gameObject);
        }
        _spawnedStatusEffects.Clear();
    }

    private void RefreshStatusEffects()
    {
        ClearStatusEffects();
        if (_character == null || _character.StatusManager == null) return;
        if (_statusEffectPrefab == null || _statusEffectsContainer == null) return;

        var active = _character.StatusManager.ActiveEffects;
        for (int i = 0; i < active.Count; i++)
        {
            var inst = Instantiate(_statusEffectPrefab, _statusEffectsContainer);
            inst.gameObject.SetActive(true);
            inst.Setup(active[i]);
            _spawnedStatusEffects.Add(inst);
        }
    }

    private void RefreshHeader()
    {
        if (_character == null) return;

        if (_nameText != null)
            _nameText.text = string.IsNullOrEmpty(_character.CharacterName) ? "—" : _character.CharacterName;

        int level = _character.CharacterCombatLevel != null ? _character.CharacterCombatLevel.CurrentLevel : 1;
        string archetype = (_character.Archetype != null && !string.IsNullOrEmpty(_character.Archetype.ArchetypeName))
            ? _character.Archetype.ArchetypeName : "—";
        if (_levelLineText != null)
            _levelLineText.text = $"Combat Lv. {level} · {archetype}";

        int curXp = _character.CharacterCombatLevel != null ? _character.CharacterCombatLevel.CurrentExperience : 0;
        int reqXp = _character.CharacterCombatLevel != null ? _character.CharacterCombatLevel.GetRequiredExpForNextLevel() : 1;

        // CharacterCombatLevel subtracts the threshold from _currentExperience on
        // level-up (CharacterCombatLevel.cs:134), so it is already per-level —
        // no extra arithmetic needed here.
        float pct = reqXp > 0 ? Mathf.Clamp01((float)curXp / reqXp) : 0f;
        if (_xpFill != null) _xpFill.fillAmount = pct;
        if (_xpText != null) _xpText.text = $"{curXp:N0} / {reqXp:N0} XP";

        int pts = _character.CharacterCombatLevel != null ? _character.CharacterCombatLevel.UnassignedStatPoints : 0;
        if (_unspentPointsPill != null) _unspentPointsPill.SetActive(pts > 0);
        if (_unspentPointsText != null) _unspentPointsText.text = $"{pts} pts";
    }

    // ---------- Leaf callbacks ----------

    private void OnUpgrade(StatType stat)
    {
        if (_character == null || _character.CharacterCombatLevel == null) return;
        _character.CharacterCombatLevel.SpendStatPoint(stat);
        RefreshHeader();

        // SpendStatPoint mutates the secondary's BaseValue + UnassignedStatPoints
        // but does NOT fire CharacterStats.OnStatsUpdated or
        // CharacterCombatLevel.OnLevelChanged, so the cards' subscribed events
        // don't trigger. Force-refresh every attribute card so the `+` button
        // hides when the pool empties and the meta line reflects the new base
        // value.
        for (int i = 0; i < _spawnedAttrCards.Count; i++)
        {
            if (_spawnedAttrCards[i] != null) _spawnedAttrCards[i].Refresh();
        }

        // If the tooltip was open for this stat, recompute it so the preview reflects the new state.
        if (_hoveredStat.HasValue) OnHover(_hoveredStat.Value);
    }

    private void OnHover(StatType stat)
    {
        _hoveredStat = stat;
        if (UI_TooltipManager.Instance == null || _character == null || _character.Stats == null) return;
        var payload = BuildPayload(stat);
        var text = FormatTooltip(payload);
        UI_TooltipManager.Instance.ShowTooltip(text);
    }

    private void OnHoverExit()
    {
        _hoveredStat = null;
        if (UI_TooltipManager.Instance != null)
            UI_TooltipManager.Instance.HideTooltip();
    }

    // ---------- Tooltip payload + text formatting ----------

    private StatTooltipPayload BuildPayload(StatType stat)
    {
        var baseStat = _character.Stats.GetBaseStat(stat);
        float current = baseStat != null ? baseStat.CurrentValue : 0f;

        bool isAttribute = stat == StatType.Strength || stat == StatType.Agility
                        || stat == StatType.Dexterity || stat == StatType.Intelligence
                        || stat == StatType.Endurance || stat == StatType.Charisma;
        bool isVital = stat == StatType.Health || stat == StatType.Stamina
                    || stat == StatType.Mana || stat == StatType.Initiative;

        if (isAttribute)
        {
            var lines = BuildAttributeBreakdown(stat, baseStat);
            string preview = BuildAttributePreview(stat);
            return StatTooltipPayload.ForAttribute(stat, current, lines, preview);
        }
        if (isVital)
        {
            return StatTooltipPayload.ForVital(stat, current, breakdownLines: null);
        }
        // derived
        var dLines = BuildDerivedBreakdown(stat, baseStat);
        string formula = BuildDerivedFormula(stat);
        return StatTooltipPayload.ForDerived(stat, current, dLines, formula);
    }

    private IReadOnlyList<StatTooltipPayload.BreakdownLine> BuildAttributeBreakdown(StatType stat, CharacterBaseStats baseStat)
    {
        if (baseStat == null) return null;
        var list = new List<StatTooltipPayload.BreakdownLine>(2);
        list.Add(new StatTooltipPayload.BreakdownLine("Base value", baseStat.BaseValue));
        float delta = baseStat.CurrentValue - baseStat.BaseValue;
        if (Mathf.Abs(delta) > 0.01f)
            list.Add(new StatTooltipPayload.BreakdownLine("Modifiers", delta));
        return list;
    }

    private string BuildAttributePreview(StatType bumped)
    {
        if (_character.CharacterCombatLevel == null || _character.CharacterCombatLevel.UnassignedStatPoints <= 0)
            return null;
        var s = _character.Stats;
        if (s == null) return null;

        var snap = new StatPreviewCalculator.Snapshot(
            ValueOrZero(s, StatType.Strength),
            ValueOrZero(s, StatType.Agility),
            ValueOrZero(s, StatType.Dexterity),
            ValueOrZero(s, StatType.Intelligence),
            ValueOrZero(s, StatType.Endurance),
            ValueOrZero(s, StatType.Charisma));

        var scaling = BuildScalingTable();
        var sb = new StringBuilder(128);
        sb.Append("+1 ").Append(ShortName(bumped));
        bool anyChange = false;
        foreach (var line in StatPreviewCalculator.PreviewPlusOne(snap, bumped, scaling))
        {
            if (Mathf.Abs(line.After - line.Before) <= 0.01f) continue;
            StatDescriptionRegistry.TryGet(line.DerivedStat, out var d);
            sb.Append('\n').Append("  ").Append(d.DisplayName)
              .Append(' ').Append(line.Before.ToString("F1"))
              .Append(" → ").Append(line.After.ToString("F1"));
            anyChange = true;
        }
        return anyChange ? sb.ToString() : null;
    }

    private IReadOnlyList<StatTooltipPayload.BreakdownLine> BuildDerivedBreakdown(StatType stat, CharacterBaseStats baseStat)
    {
        // Path C (per recon §6) — no equipment contribution surface. Show Base + Total
        // only when they differ; otherwise return null and the tooltip elides the section.
        if (baseStat == null) return null;
        float baseV = baseStat.BaseValue;
        float total = baseStat.CurrentValue;
        if (Mathf.Abs(total - baseV) < 0.01f) return null;
        var list = new List<StatTooltipPayload.BreakdownLine>(2);
        list.Add(new StatTooltipPayload.BreakdownLine("Base value", baseV));
        list.Add(new StatTooltipPayload.BreakdownLine("Modifiers", total - baseV));
        return list;
    }

    private string BuildDerivedFormula(StatType derived)
    {
        if (_character.Stats == null) return null;
        var tertiary = _character.Stats.GetBaseStat(derived) as CharacterTertiaryStats;
        if (tertiary == null || tertiary.LinkedStat == null) return null;
        string linked = LinkedShortName(tertiary.LinkedStat);
        float mult = tertiary.Multiplier;
        float baseOff = tertiary.BaseOffset;
        if (Mathf.Abs(baseOff) < 0.001f)
            return $"{linked} × {mult:F1}";
        return $"{linked} × {mult:F1} + {baseOff:F1}";
    }

    private StatPreviewCalculator.ScalingTable BuildScalingTable()
    {
        var table = new StatPreviewCalculator.ScalingTable();
        if (_character.Stats == null) return table;

        // Tertiary stats — formula `max(min, baseOffset + linked × mult)` lives
        // on CharacterTertiaryStats. Read live scaling so race overrides land
        // in the preview.
        var allDerived = new[] {
            StatType.PhysicalPower, StatType.MagicalPower, StatType.Speed,
            StatType.Accuracy, StatType.Dodge, StatType.CriticalChance,
            StatType.SpellCasting, StatType.CombatCasting,
            StatType.StaminaRegen, StatType.ManaRegen,
        };
        foreach (var d in allDerived)
        {
            var t = _character.Stats.GetBaseStat(d) as CharacterTertiaryStats;
            if (t == null) continue;
            StatType linkedType = ResolveLinkedStatType(t.LinkedStat);
            table.Set(d, linkedType, t.Multiplier, t.BaseOffset, t.MinValue);
        }

        // Primary stats whose MAX is also derived from a secondary
        // (CharacterStats.cs:134-137):
        //   Health   ← Endurance     × Mult + Offset
        //   Stamina  ← Endurance     × Mult + Offset
        //   Mana     ← Intelligence  × Mult + Offset
        //   Initiative has no linked stat — exclude.
        var allPrimaries = new[] { StatType.Health, StatType.Stamina, StatType.Mana };
        foreach (var p in allPrimaries)
        {
            var prim = _character.Stats.GetBaseStat(p) as CharacterPrimaryStats;
            if (prim == null || prim.LinkedStat == null) continue;
            StatType linkedType = ResolveLinkedStatType(prim.LinkedStat);
            table.Set(p, linkedType, prim.Multiplier, prim.BaseOffset, 0f);
        }

        return table;
    }

    private StatType ResolveLinkedStatType(CharacterBaseStats linked)
    {
        if (linked == null || _character.Stats == null) return StatType.Strength;
        if (linked == _character.Stats.Strength)     return StatType.Strength;
        if (linked == _character.Stats.Agility)      return StatType.Agility;
        if (linked == _character.Stats.Dexterity)    return StatType.Dexterity;
        if (linked == _character.Stats.Intelligence) return StatType.Intelligence;
        if (linked == _character.Stats.Endurance)    return StatType.Endurance;
        if (linked == _character.Stats.Charisma)     return StatType.Charisma;
        return StatType.Strength;
    }

    private static float ValueOrZero(CharacterStats s, StatType t)
    {
        var bs = s.GetBaseStat(t);
        return bs != null ? bs.CurrentValue : 0f;
    }

    // ---------- Tooltip text formatting (TMP rich text) ----------

    private string FormatTooltip(StatTooltipPayload p)
    {
        _tooltipSb.Clear();
        // Title row: name (bold) + current value.
        _tooltipSb.Append("<b>").Append(p.DisplayName).Append("</b>  ");
        _tooltipSb.Append("<color=#dddddd>").Append(p.CurrentValue.ToString("F1")).Append("</color>");
        _tooltipSb.Append('\n');

        if (!string.IsNullOrEmpty(p.Description))
        {
            _tooltipSb.Append("<size=85%><color=#aaaaaa>").Append(p.Description).Append("</color></size>\n");
        }

        if (!string.IsNullOrEmpty(p.FormulaString))
        {
            _tooltipSb.Append('\n');
            _tooltipSb.Append("<size=85%><color=#ffcc66>").Append(p.FormulaString).Append("</color></size>\n");
        }

        if (p.BreakdownLines != null && p.BreakdownLines.Count > 0)
        {
            _tooltipSb.Append('\n');
            _tooltipSb.Append("<size=85%>");
            for (int i = 0; i < p.BreakdownLines.Count; i++)
            {
                var line = p.BreakdownLines[i];
                string sign = line.Delta >= 0f ? "+" : "";
                string color = (i == 0) ? "#aaaaaa" : (line.Delta >= 0f ? "#6dc26d" : "#f06868");
                _tooltipSb.Append("<color=").Append(color).Append(">")
                          .Append(line.Label).Append("  ")
                          .Append(sign).Append(line.Delta.ToString("F1"))
                          .Append("</color>\n");
            }
            _tooltipSb.Append($"<color=#ffcc66>Total  {p.CurrentValue:F1}</color>");
            _tooltipSb.Append("</size>\n");
        }

        if (!string.IsNullOrEmpty(p.PreviewLine))
        {
            _tooltipSb.Append('\n');
            _tooltipSb.Append("<color=#6dc26d>").Append(p.PreviewLine).Append("</color>");
        }

        return _tooltipSb.ToString();
    }

    private static string ShortName(StatType t)
    {
        switch (t)
        {
            case StatType.Strength:     return "STR";
            case StatType.Agility:      return "AGI";
            case StatType.Dexterity:    return "DEX";
            case StatType.Intelligence: return "INT";
            case StatType.Endurance:    return "END";
            case StatType.Charisma:     return "CHA";
            default: return t.ToString();
        }
    }

    private string LinkedShortName(CharacterBaseStats linked)
    {
        if (linked == null || _character.Stats == null) return "?";
        if (linked == _character.Stats.Strength)     return "STR";
        if (linked == _character.Stats.Agility)      return "AGI";
        if (linked == _character.Stats.Dexterity)    return "DEX";
        if (linked == _character.Stats.Intelligence) return "INT";
        if (linked == _character.Stats.Endurance)    return "END";
        if (linked == _character.Stats.Charisma)     return "CHA";
        return "?";
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        Unbind();
        ClearRows();
        ClearStatusEffects();
    }
}
