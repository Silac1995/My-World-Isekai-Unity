using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System.Text;

public class UI_StatusEffect : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private Image _iconImage;
    [SerializeField] private TextMeshProUGUI _durationText;
    [SerializeField] private TextMeshProUGUI _nameText;
    
    private CharacterStatusEffectInstance _currentEffect;
    
    public void Setup(CharacterStatusEffectInstance effect)
    {
        _currentEffect = effect;

        if (_nameText != null)
        {
            _nameText.text = effect.StatusEffectName;
        }

        if (_iconImage != null)
        {
            if (effect.Icon != null)
            {
                _iconImage.sprite = effect.Icon;
                _iconImage.enabled = true;
            }
            else
            {
                _iconImage.enabled = false;
            }
        }
    }
    
    private void Update()
    {
        if (_currentEffect != null)
        {
            if (_currentEffect.IsPermanent)
            {
                 _durationText.text = "";
            }
            else
            {
                 int remaining = Mathf.CeilToInt(_currentEffect.RemainingDuration);
                 _durationText.text = remaining > 0 ? remaining.ToString() : "";
            }
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_currentEffect != null && UI_TooltipManager.Instance != null)
        {
            string tooltipText = BuildTooltipText();
            if (!string.IsNullOrEmpty(tooltipText))
            {
                UI_TooltipManager.Instance.ShowTooltip(tooltipText);
            }
        }
    }

    private string BuildTooltipText()
    {
        if (_currentEffect == null) return "";
        
        StringBuilder sb = new StringBuilder();

        // Base description
        if (!string.IsNullOrEmpty(_currentEffect.Description))
        {
            sb.AppendLine(_currentEffect.Description);
        }

        // Add mechanical breakdown
        if (_currentEffect.SourceAsset != null && _currentEffect.SourceAsset.StatusEffects != null)
        {
            bool hasMechanicals = false;

            foreach (var genericEffect in _currentEffect.SourceAsset.StatusEffects)
            {
                if (genericEffect is StatModifierEffect statMod)
                {
                    if (statMod.Modifiers != null && statMod.Modifiers.Count > 0 && !hasMechanicals)
                    {
                        if (sb.Length > 0) sb.AppendLine();
                        hasMechanicals = true;
                    }

                    foreach (var mod in statMod.Modifiers)
                    {
                        string sign = mod.Value >= 0 ? "+" : "";
                        string color = mod.Value >= 0 ? "#4CAF50" : "#F44336"; // Material green/red
                        sb.AppendLine($"<color={color}>{sign}{mod.Value} {mod.StatType}</color>");
                    }
                }
                else if (genericEffect is PeriodicStatEffect periodic)
                {
                    if (!hasMechanicals)
                    {
                        if (sb.Length > 0) sb.AppendLine();
                        hasMechanicals = true;
                    }

                    string sign = periodic.ValuePerSecond >= 0 ? "+" : "";
                    string color = periodic.ValuePerSecond >= 0 ? "#4CAF50" : "#F44336";
                    string percent = periodic.IsPercentage ? "%" : "";
                    sb.AppendLine($"<color={color}>{sign}{periodic.ValuePerSecond}{percent} {periodic.TargetStat} / s</color>");
                }
            }
        }

        return sb.ToString().TrimEnd();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (UI_TooltipManager.Instance != null)
        {
            UI_TooltipManager.Instance.HideTooltip();
        }
    }
}
