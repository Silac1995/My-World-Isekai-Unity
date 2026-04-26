using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Hunger HUD bar. Subscribes to NeedHunger.OnValueChanged and pushes fill / starving-flash
/// values into an instanced material. Mirrors UI_HealthBar's shader-based pattern, but
/// targets a NeedHunger (POCO) instead of CharacterPrimaryStats. Uses unscaled time per rule #26.
/// </summary>
public class UI_HungerBar : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Image _barImage;
    [SerializeField] private TextMeshProUGUI _valueText;

    [Header("Animation")]
    [SerializeField] private float _fillAnimationDuration = 0.25f;

    [Header("Starving Flash")]
    [SerializeField] private float _starvingFlashPeriod = 0.5f;

    [Header("Value Text")]
    [SerializeField] private Color _normalTextColor = Color.white;
    [SerializeField] private Color _lowTextColor = Color.red;
    [SerializeField] private float _lowTextThreshold = 0.3f;

    private NeedHunger _target;
    private Material _instancedMaterial;
    private Coroutine _fillCoroutine;
    private Coroutine _starveCoroutine;
    private float _currentFill;

    private static readonly int ID_FillAmount = Shader.PropertyToID("_FillAmount");
    private static readonly int ID_HealFlash = Shader.PropertyToID("_HealFlash");

    public void Initialize(NeedHunger hunger)
    {
        Cleanup();
        _target = hunger;

        if (_target == null) return;

        if (_barImage != null && _instancedMaterial == null)
        {
            _instancedMaterial = new Material(_barImage.material);
            _barImage.material = _instancedMaterial;
        }

        _target.OnValueChanged += HandleValueChanged;
        _target.OnStarvingChanged += HandleStarvingChanged;

        _currentFill = GetFillRatio();
        SnapToShader();
        UpdateValueText();

        if (_target.IsStarving) StartStarveFlash();
    }

    private void OnDestroy()
    {
        Cleanup();
        if (_instancedMaterial != null) Destroy(_instancedMaterial);
    }

    private void Cleanup()
    {
        if (_target == null) return;
        _target.OnValueChanged -= HandleValueChanged;
        _target.OnStarvingChanged -= HandleStarvingChanged;
        _target = null;

        if (_starveCoroutine != null)
        {
            StopCoroutine(_starveCoroutine);
            _starveCoroutine = null;
        }
        if (_fillCoroutine != null)
        {
            StopCoroutine(_fillCoroutine);
            _fillCoroutine = null;
        }
    }

    private void HandleValueChanged(float _newValue)
    {
        if (_fillCoroutine != null) StopCoroutine(_fillCoroutine);
        _fillCoroutine = StartCoroutine(AnimateFill());
        UpdateValueText();
    }

    private void HandleStarvingChanged(bool isStarving)
    {
        if (isStarving) StartStarveFlash();
        else StopStarveFlash();
    }

    private void StartStarveFlash()
    {
        if (_starveCoroutine != null) return;
        _starveCoroutine = StartCoroutine(StarveFlashRoutine());
    }

    private void StopStarveFlash()
    {
        if (_starveCoroutine != null) StopCoroutine(_starveCoroutine);
        _starveCoroutine = null;
        if (_instancedMaterial != null) _instancedMaterial.SetFloat(ID_HealFlash, 0f);
    }

    private IEnumerator AnimateFill()
    {
        float startFill = _currentFill;
        float targetFill = GetFillRatio();
        float elapsed = 0f;
        while (elapsed < _fillAnimationDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / _fillAnimationDuration);
            _currentFill = Mathf.Lerp(startFill, targetFill, Mathf.SmoothStep(0f, 1f, t));
            if (_instancedMaterial != null) _instancedMaterial.SetFloat(ID_FillAmount, _currentFill);
            yield return null;
        }
        _currentFill = targetFill;
        if (_instancedMaterial != null) _instancedMaterial.SetFloat(ID_FillAmount, _currentFill);
        _fillCoroutine = null;
    }

    private IEnumerator StarveFlashRoutine()
    {
        while (true)
        {
            float elapsed = 0f;
            while (elapsed < _starvingFlashPeriod)
            {
                elapsed += Time.unscaledDeltaTime;
                float flash = Mathf.Sin((elapsed / _starvingFlashPeriod) * Mathf.PI);
                if (_instancedMaterial != null) _instancedMaterial.SetFloat(ID_HealFlash, flash);
                yield return null;
            }
        }
    }

    private float GetFillRatio()
    {
        if (_target == null || _target.MaxValue <= 0f) return 0f;
        return _target.CurrentValue / _target.MaxValue;
    }

    private void SnapToShader()
    {
        if (_instancedMaterial == null) return;
        _instancedMaterial.SetFloat(ID_FillAmount, _currentFill);
    }

    private void UpdateValueText()
    {
        if (_valueText == null || _target == null) return;
        int current = Mathf.RoundToInt(_target.CurrentValue);
        int max = Mathf.RoundToInt(_target.MaxValue);
        _valueText.text = $"{current} / {max}";
        _valueText.color = GetFillRatio() <= _lowTextThreshold ? _lowTextColor : _normalTextColor;
    }
}
