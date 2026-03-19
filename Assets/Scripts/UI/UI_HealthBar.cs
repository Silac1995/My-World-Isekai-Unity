using System.Collections;
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

    [Header("Heal Flash")]
    [SerializeField] private float _healFlashDuration = 0.5f;

    // ── Runtime state ────────────────────────────────────────────

    private CharacterPrimaryStats _targetStat;
    private Material              _instancedMaterial;

    private float     _ghostFill;
    private Coroutine _ghostCoroutine;
    private Coroutine _healCoroutine;

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

        _ghostFill = GetFillRatio();
        PushToShader();
    }

    // ── Unity lifecycle ──────────────────────────────────────────

    private void OnDestroy()
    {
        CleanupEvents();

        if (_instancedMaterial != null)
            Destroy(_instancedMaterial);
    }

    // ── Event handlers ───────────────────────────────────────────

    private void HandleMaxValueChanged(float oldVal, float newVal) => PushToShader();

    private void HandleAmountChanged(float oldVal, float newVal)
    {
        if (newVal < oldVal) OnDamageTaken(oldVal);
        else if (newVal > oldVal) OnHealReceived();

        PushToShader();
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

        // Snap ghost up so it never shows below the new fill.
        _ghostFill = GetFillRatio();

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

    // ── Helpers ───────────────────────────────────────────────────

    private float GetFillRatio()
    {
        return _targetStat.MaxValue > 0
            ? _targetStat.CurrentAmount / _targetStat.MaxValue
            : 0f;
    }

    private void PushToShader()
    {
        if (_targetStat == null || _instancedMaterial == null) return;

        _instancedMaterial.SetFloat(ID_FillAmount, GetFillRatio());
        _instancedMaterial.SetFloat(ID_GhostFill,  _ghostFill);
    }

    private void CleanupEvents()
    {
        if (_targetStat == null) return;
        _targetStat.OnValueChanged  -= HandleMaxValueChanged;
        _targetStat.OnAmountChanged -= HandleAmountChanged;
    }
}
