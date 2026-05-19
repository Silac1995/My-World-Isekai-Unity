using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public class UI_DerivedStatRow : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private TextMeshProUGUI _nameText;
    [SerializeField] private TextMeshProUGUI _valueText;

    private Character _character;
    private StatType _statType;
    private Action<StatType> _onHover;
    private Action _onHoverExit;

    public void Initialize(Character character, StatType statType,
        Action<StatType> onHover, Action onHoverExit)
    {
        Unbind();
        _character = character;
        _statType = statType;
        _onHover = onHover;
        _onHoverExit = onHoverExit;

        if (StatDescriptionRegistry.TryGet(statType, out var d) && _nameText != null)
            _nameText.text = d.DisplayName;

        if (_character?.Stats != null)
            _character.Stats.OnStatsUpdated += Repaint;

        Repaint();
    }

    private void Repaint()
    {
        if (_character?.Stats == null) return;
        var stat = _character.Stats.GetBaseStat(_statType);
        if (stat == null) return;

        string fmt = _statType switch
        {
            StatType.Dodge or StatType.CriticalChance => $"{stat.CurrentValue:F1}%",
            StatType.StaminaRegen or StatType.ManaRegen => $"{stat.CurrentValue:F1}/s",
            _ => stat.CurrentValue.ToString("F1"),
        };
        if (_valueText != null) _valueText.text = fmt;
    }

    public void OnPointerEnter(PointerEventData _) => _onHover?.Invoke(_statType);
    public void OnPointerExit(PointerEventData _)  => _onHoverExit?.Invoke();

    private void Unbind()
    {
        if (_character?.Stats != null) _character.Stats.OnStatsUpdated -= Repaint;
    }

    private void OnDestroy() => Unbind();
}
