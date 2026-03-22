using UnityEngine;
using UnityEngine.UI;
using MWI;

[RequireComponent(typeof(Image))]
public class UI_TargetIndicator : MonoBehaviour
{
    private Image _image;
    private Material _instancedMaterial;
    private Character _currentTarget;

    [Header("Shader Animation Settings")]
    [Tooltip("How fast the indicator floats up and down.")]
    [SerializeField] private float _bobSpeed = 3.0f;
    [Tooltip("How far the indicator floats up and down (in Canvas pixels).")]
    [SerializeField] private float _bobAmplitude = 10.0f;

    private void Awake()
    {
        _image = GetComponent<Image>();
        _instancedMaterial = new Material(_image.material);
        _image.material = _instancedMaterial;

        // Apply initial animation settings from Inspector
        ApplyAnimationSettings();
    }

    private void OnValidate()
    {
        // Allow live tweaking in Editor playmode if properties change
        ApplyAnimationSettings();
    }

    private void ApplyAnimationSettings()
    {
        if (_instancedMaterial != null)
        {
            _instancedMaterial.SetFloat("_BobSpeed", _bobSpeed);
            _instancedMaterial.SetFloat("_BobAmplitude", _bobAmplitude);
        }
    }

    public void SetTarget(Transform targetTransform)
    {
        // Unsubscribe from old target
        if (_currentTarget != null && _currentTarget.Stats?.Health != null)
        {
            _currentTarget.Stats.Health.OnAmountChanged -= HandleHealthChanged;
        }

        // Evaluate new target
        _currentTarget = targetTransform != null ? targetTransform.GetComponent<Character>() : null;

        if (_currentTarget != null && _currentTarget.Stats?.Health != null)
        {
            _currentTarget.Stats.Health.OnAmountChanged += HandleHealthChanged;
            UpdateHealthVisualUI(_currentTarget.Stats.Health.CurrentValue, _currentTarget.Stats.Health.MaxValue);
        }
        else
        {
            // Reset to full green if it's an object without health (InteractableObject)
            UpdateHealthVisualUI(1, 1);
        }
    }

    private void HandleHealthChanged(float oldAmount, float newAmount)
    {
        if (_currentTarget != null && _currentTarget.Stats?.Health != null)
        {
            UpdateHealthVisualUI(newAmount, _currentTarget.Stats.Health.MaxValue);
        }
    }

    private void UpdateHealthVisualUI(float current, float max)
    {
        if (max <= 0) return;
        float percent = current / max;
        
        if (_instancedMaterial != null)
        {
            _instancedMaterial.SetFloat("_HealthPercent", percent);
        }
    }

    private void OnDestroy()
    {
        if (_currentTarget != null && _currentTarget.Stats?.Health != null)
        {
            _currentTarget.Stats.Health.OnAmountChanged -= HandleHealthChanged;
        }

        if (_instancedMaterial != null)
        {
            Destroy(_instancedMaterial);
        }
    }
}
