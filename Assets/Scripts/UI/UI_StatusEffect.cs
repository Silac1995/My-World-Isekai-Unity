using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class UI_StatusEffect : MonoBehaviour
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
}
