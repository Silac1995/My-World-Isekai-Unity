using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class UI_VitalBar : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private TextMeshProUGUI _labelText;
    [SerializeField] private TextMeshProUGUI _regenText;    // optional, may be null
    [SerializeField] private TextMeshProUGUI _valueText;
    [SerializeField] private Image _fillImage;

    private Character _character;
    private StatType _statType;
    private CharacterPrimaryStats _primary;
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

        if (StatDescriptionRegistry.TryGet(statType, out var d) && _labelText != null)
            _labelText.text = d.DisplayName;

        if (_character?.Stats == null) { Repaint(); return; }

        _primary = _character.Stats.GetBaseStat(statType) as CharacterPrimaryStats;
        _character.Stats.OnStatsUpdated += Repaint;
        if (_primary != null)
            _primary.OnAmountChanged += HandleAmountChanged;

        Repaint();
    }

    private void HandleAmountChanged(float oldVal, float newVal) => Repaint();

    private void Repaint()
    {
        if (_character?.Stats == null) return;
        var stat = _character.Stats.GetBaseStat(_statType);
        if (stat == null) return;

        float current, max;
        if (_primary != null)
        {
            current = _primary.CurrentAmount;
            max = _primary.MaxValue;
        }
        else
        {
            current = stat.CurrentValue;
            max = stat.CurrentValue;       // no max for non-primary vitals (Initiative falls here today)
        }

        float pct = max > 0f ? Mathf.Clamp01(current / max) : 0f;
        if (_fillImage != null) _fillImage.fillAmount = pct;

        if (_valueText != null)
            _valueText.text = (max > 0f && Mathf.Abs(max - current) > 0.5f)
                ? $"{current:F0} / {max:F0}"
                : current.ToString("F0");

        if (_regenText != null)
        {
            float regen = ResolveRegen();
            _regenText.gameObject.SetActive(regen > 0.01f);
            if (regen > 0.01f) _regenText.text = $"+{regen:F1}/s";
        }
    }

    private float ResolveRegen()
    {
        if (_character?.Stats == null) return 0f;
        return _statType switch
        {
            StatType.Stamina => _character.Stats.GetBaseStat(StatType.StaminaRegen)?.CurrentValue ?? 0f,
            StatType.Mana    => _character.Stats.GetBaseStat(StatType.ManaRegen)?.CurrentValue ?? 0f,
            _ => 0f,
        };
    }

    public void OnPointerEnter(PointerEventData _) => _onHover?.Invoke(_statType);
    public void OnPointerExit(PointerEventData _)  => _onHoverExit?.Invoke();

    private void Unbind()
    {
        if (_character?.Stats != null) _character.Stats.OnStatsUpdated -= Repaint;
        if (_primary != null) _primary.OnAmountChanged -= HandleAmountChanged;
        _primary = null;
    }

    private void OnDestroy() => Unbind();
}
