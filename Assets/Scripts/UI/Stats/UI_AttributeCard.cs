using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class UI_AttributeCard : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private TextMeshProUGUI _nameText;
    [SerializeField] private TextMeshProUGUI _metaText;
    [SerializeField] private TextMeshProUGUI _valueText;
    [SerializeField] private Button _upgradeButton;
    [SerializeField] private Image _bonusTint;

    private Character _character;
    private StatType _statType;
    private Action<StatType> _onUpgrade;
    private Action<StatType> _onHover;
    private Action _onHoverExit;

    public void Initialize(Character character, StatType statType,
        Action<StatType> onUpgrade, Action<StatType> onHover, Action onHoverExit)
    {
        Unbind();
        _character = character;
        _statType = statType;
        _onUpgrade = onUpgrade;
        _onHover = onHover;
        _onHoverExit = onHoverExit;

        if (_nameText != null) _nameText.text = ShortName(statType);

        if (_upgradeButton != null)
        {
            _upgradeButton.onClick.RemoveAllListeners();
            _upgradeButton.onClick.AddListener(() => _onUpgrade?.Invoke(_statType));
        }

        if (_character?.Stats != null)
            _character.Stats.OnStatsUpdated += Repaint;
        if (_character?.CharacterCombatLevel != null)
            _character.CharacterCombatLevel.OnLevelChanged += Repaint;

        Repaint();
    }

    /// <summary>
    /// Repaint the card from live character state.
    /// Public so the orchestrator can force-refresh after operations that
    /// don't fire OnStatsUpdated / OnLevelChanged — notably SpendStatPoint,
    /// which only mutates the secondary's BaseValue + UnassignedStatPoints
    /// without raising any global event.
    /// </summary>
    public void Refresh() => Repaint();

    private void Repaint()
    {
        if (_character?.Stats == null) return;
        var stat = _character.Stats.GetBaseStat(_statType);
        if (stat == null) return;

        float baseV = stat.BaseValue;
        float total = stat.CurrentValue;
        float bonus = total - baseV;
        bool hasBonus = Mathf.Abs(bonus) > 0.01f;

        if (_valueText != null) _valueText.text = total.ToString("F0");
        if (_metaText != null)
        {
            _metaText.text = hasBonus
                ? $"{baseV:F0} base  <color=#6dc26d>{(bonus >= 0 ? "+" : "")}{bonus:F0}</color>"
                : $"{baseV:F0} base";
        }
        if (_bonusTint != null) _bonusTint.enabled = hasBonus;

        bool canUpgrade = _character.CharacterCombatLevel != null
                          && _character.CharacterCombatLevel.UnassignedStatPoints > 0;
        if (_upgradeButton != null) _upgradeButton.gameObject.SetActive(canUpgrade);
    }

    public void OnPointerEnter(PointerEventData _) => _onHover?.Invoke(_statType);
    public void OnPointerExit(PointerEventData _)  => _onHoverExit?.Invoke();

    private static string ShortName(StatType t) => t switch
    {
        StatType.Strength     => "STR",
        StatType.Agility      => "AGI",
        StatType.Dexterity    => "DEX",
        StatType.Intelligence => "INT",
        StatType.Endurance    => "END",
        StatType.Charisma     => "CHA",
        _ => t.ToString(),
    };

    private void Unbind()
    {
        if (_character?.Stats != null) _character.Stats.OnStatsUpdated -= Repaint;
        if (_character?.CharacterCombatLevel != null) _character.CharacterCombatLevel.OnLevelChanged -= Repaint;
        if (_upgradeButton != null) _upgradeButton.onClick.RemoveAllListeners();
    }

    private void OnDestroy() => Unbind();
}
