using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives the UI/HealthBar shader.
/// Subscribes to a <see cref="CharacterPrimaryStats"/> and pushes fill / ghost / flash
/// values to an instanced material each time the stat changes.
///
/// Ghost timing (delay, drain speed) is read from the material so every bar
/// sharing the same material uses the same settings.
/// </summary>
public class UI_HealthBar : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Image _barImage;
    [SerializeField] private TextMeshProUGUI _valueText;

    [Header("Heal Flash")]
    [SerializeField] private float _healFlashDuration = 0.5f;

    [Header("Animation")]
    [SerializeField] private float _fillAnimationDuration = 0.25f;

    [Header("Value Text")]
    [SerializeField] private Color _normalTextColor = Color.white;
    [SerializeField] private Color _lowTextColor = Color.red;
    [SerializeField] private float _lowTextThreshold = 0.2f;

    // ── Runtime state ────────────────────────────────────────────

    private CharacterPrimaryStats _targetStat;
    private Material              _instancedMaterial;

    private float     _ghostFill;
    private float     _currentFill;
    private Coroutine _ghostCoroutine;
    private Coroutine _healCoroutine;
    private Coroutine _fillCoroutine;

    // ── Cached shader property IDs ───────────────────────────────

    private static readonly int ID_FillAmount = Shader.PropertyToID("_FillAmount");
    private static readonly int ID_GhostFill  = Shader.PropertyToID("_GhostFill");
    private static readonly int ID_HealFlash  = Shader.PropertyToID("_HealFlash");
    private static readonly int ID_GhostDelay = Shader.PropertyToID("_GhostDelay");
    private static readonly int ID_GhostSpeed = Shader.PropertyToID("_GhostDrainSpeed");

    // ── Public API ───────────────────────────────────────────────

    public void Initialize(CharacterPrimaryStats stat)
    {
        CleanupEvents();
        _targetStat = stat;

        if (_targetStat == null) return;

        if (_barImage != null && _instancedMaterial == null)
        {
            _instancedMaterial = new Material(_barImage.material);
            _barImage.material = _instancedMaterial;
        }

        _targetStat.OnValueChanged  += HandleMaxValueChanged;
        _targetStat.OnAmountChanged += HandleAmountChanged;

        _currentFill = GetFillRatio();
        _ghostFill = _currentFill;
        SnapToShader();
        UpdateValueText();
    }

    // ── Unity lifecycle ──────────────────────────────────────────

    private void OnDestroy()
    {
        CleanupEvents();

        if (_instancedMaterial != null)
            Destroy(_instancedMaterial);
    }

    // ── Event handlers ───────────────────────────────────────────

    private void HandleMaxValueChanged(float oldVal, float newVal)
    {
        AnimateToShader();
        UpdateValueText();
    }

    private void HandleAmountChanged(float oldVal, float newVal)
    {
        if (newVal < oldVal) OnDamageTaken(oldVal);
        else if (newVal > oldVal) OnHealReceived();

        AnimateToShader();
        UpdateValueText();
    }

    // ── Damage / Heal effects ────────────────────────────────────

    private void OnDamageTaken(float oldAmount)
    {
        if (_instancedMaterial == null) return;

        // Snap ghost to pre-damage ratio so it stays frozen during the delay.
        float oldRatio = _targetStat.MaxValue > 0 ? oldAmount / _targetStat.MaxValue : 1f;
        _ghostFill = Mathf.Max(_ghostFill, oldRatio);

        if (_ghostCoroutine != null) StopCoroutine(_ghostCoroutine);
        _ghostCoroutine = StartCoroutine(GhostDrainRoutine());
    }

    private void OnHealReceived()
    {
        if (_instancedMaterial == null) return;

        // Do not snap ghost up instantly here. 
        // We will sync it with the primary fill animation so it doesn't pop ahead.

        if (_healCoroutine != null) StopCoroutine(_healCoroutine);
        _healCoroutine = StartCoroutine(HealFlashRoutine());
    }

    // ── Coroutines ───────────────────────────────────────────────

    private IEnumerator GhostDrainRoutine()
    {
        float delay = _instancedMaterial.GetFloat(ID_GhostDelay);
        yield return new WaitForSecondsRealtime(delay);

        while (_ghostFill > GetFillRatio() + 0.001f)
        {
            float speed = _instancedMaterial.GetFloat(ID_GhostSpeed);
            _ghostFill  = Mathf.MoveTowards(_ghostFill, GetFillRatio(), speed * Time.unscaledDeltaTime);
            _instancedMaterial.SetFloat(ID_GhostFill, _ghostFill);
            yield return null;
        }

        _ghostFill = GetFillRatio();
        _instancedMaterial.SetFloat(ID_GhostFill, _ghostFill);
        _ghostCoroutine = null;
    }

    private IEnumerator HealFlashRoutine()
    {
        float elapsed = 0f;

        while (elapsed < _healFlashDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float flash = Mathf.Sin((elapsed / _healFlashDuration) * Mathf.PI);
            _instancedMaterial.SetFloat(ID_HealFlash, flash);
            yield return null;
        }

        _instancedMaterial.SetFloat(ID_HealFlash, 0f);
        _healCoroutine = null;
    }

    private IEnumerator FillAnimationRoutine()
    {
        float startFill = _currentFill;
        float targetFill = GetFillRatio();
        float elapsed = 0f;

        while (elapsed < _fillAnimationDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / _fillAnimationDuration);
            float easedT = Mathf.SmoothStep(0f, 1f, t);
            
            _currentFill = Mathf.Lerp(startFill, targetFill, easedT);
            _instancedMaterial.SetFloat(ID_FillAmount, _currentFill);

            // If healing, sync ghost with current fill so yellow ghost doesn't pop ahead
            if (targetFill > startFill)
            {
                _ghostFill = _currentFill;
                _instancedMaterial.SetFloat(ID_GhostFill, _ghostFill);
            }

            yield return null;
        }

        _currentFill = targetFill;
        _instancedMaterial.SetFloat(ID_FillAmount, _currentFill);

        if (targetFill > startFill)
        {
            _ghostFill = _currentFill;
            _instancedMaterial.SetFloat(ID_GhostFill, _ghostFill);
        }

        _fillCoroutine = null;
    }

    // ── Helpers ───────────────────────────────────────────────────

    private float GetFillRatio()
    {
        return _targetStat.MaxValue > 0
            ? _targetStat.CurrentAmount / _targetStat.MaxValue
            : 0f;
    }

    private void SnapToShader()
    {
        if (_targetStat == null || _instancedMaterial == null) return;

        _instancedMaterial.SetFloat(ID_FillAmount, _currentFill);
        _instancedMaterial.SetFloat(ID_GhostFill,  _ghostFill);
    }

    private void AnimateToShader()
    {
        if (_targetStat == null || _instancedMaterial == null) return;

        if (_fillCoroutine != null) StopCoroutine(_fillCoroutine);
        _fillCoroutine = StartCoroutine(FillAnimationRoutine());
    }

    private void UpdateValueText()
    {
        if (_valueText == null || _targetStat == null) return;

        int current = Mathf.RoundToInt(_targetStat.CurrentAmount);
        int max = Mathf.RoundToInt(_targetStat.MaxValue);
        _valueText.text = $"{current} / {max}";
        _valueText.color = GetFillRatio() <= _lowTextThreshold ? _lowTextColor : _normalTextColor;
    }

    private void CleanupEvents()
    {
        if (_targetStat == null) return;
        _targetStat.OnValueChanged  -= HandleMaxValueChanged;
        _targetStat.OnAmountChanged -= HandleAmountChanged;
    }
}
