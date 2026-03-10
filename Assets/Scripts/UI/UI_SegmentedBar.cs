using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class UI_SegmentedBar : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Image _barImage;

    [Header("Damage Ghost")]
    [Tooltip("Seconds to wait after taking damage before the ghost bar starts draining")]
    [SerializeField] private float _ghostDelay      = 0.6f;
    [Tooltip("How fast the ghost bar drains (fill units per second, 0-1 scale)")]
    [SerializeField] private float _ghostDrainSpeed = 0.4f;

    [Header("Heal Flash")]
    [Tooltip("Duration of the heal flash in seconds")]
    [SerializeField] private float _healFlashDuration = 0.5f;

    private CharacterPrimaryStats _targetStat;
    private Material               _instancedMaterial;

    private float     _ghostFill;
    private Coroutine _ghostCoroutine;
    private Coroutine _healCoroutine;

    private static readonly int ID_FillAmount = Shader.PropertyToID("_FillAmount");
    private static readonly int ID_GhostFill  = Shader.PropertyToID("_GhostFill");
    private static readonly int ID_HealFlash  = Shader.PropertyToID("_HealFlash");

    // -------------------------------------------------------------------------

    public void Initialize(CharacterPrimaryStats stat)
    {
        if (_targetStat != null)
        {
            _targetStat.OnValueChanged  -= HandleMaxValueChanged;
            _targetStat.OnAmountChanged -= HandleAmountChanged;
        }

        _targetStat = stat;

        if (_targetStat != null)
        {
            if (_barImage != null && _instancedMaterial == null)
            {
                _instancedMaterial = new Material(_barImage.material);
                _barImage.material = _instancedMaterial;
            }

            _targetStat.OnValueChanged  += HandleMaxValueChanged;
            _targetStat.OnAmountChanged += HandleAmountChanged;

            _ghostFill = _targetStat.MaxValue > 0
                         ? _targetStat.CurrentAmount / _targetStat.MaxValue
                         : 1f;

            UpdateShaderProperties();
        }
    }

    private void OnDestroy()
    {
        if (_targetStat != null)
        {
            _targetStat.OnValueChanged  -= HandleMaxValueChanged;
            _targetStat.OnAmountChanged -= HandleAmountChanged;
        }

        if (_instancedMaterial != null)
            Destroy(_instancedMaterial);
    }

    // -------------------------------------------------------------------------

    private void HandleMaxValueChanged(float oldVal, float newVal) => UpdateShaderProperties();

    private void HandleAmountChanged(float oldVal, float newVal)
    {
        if (newVal < oldVal) OnDamageTaken();
        else if (newVal > oldVal) OnHealReceived();

        UpdateShaderProperties();
    }

    // -------------------------------------------------------------------------

    private void OnDamageTaken()
    {
        if (_ghostCoroutine != null) StopCoroutine(_ghostCoroutine);
        _ghostCoroutine = StartCoroutine(GhostDrainRoutine());
    }

    private void OnHealReceived()
    {
        // Snap ghost up immediately so it never shows below the new fill
        _ghostFill = _targetStat.CurrentAmount / _targetStat.MaxValue;

        if (_healCoroutine != null) StopCoroutine(_healCoroutine);
        _healCoroutine = StartCoroutine(HealFlashRoutine());
    }

    private IEnumerator GhostDrainRoutine()
    {
        yield return new WaitForSeconds(_ghostDelay);

        float target = _targetStat.CurrentAmount / _targetStat.MaxValue;

        while (_ghostFill > target + 0.001f)
        {
            // Re-read target every frame in case HP changed again during drain
            target     = _targetStat.CurrentAmount / _targetStat.MaxValue;
            _ghostFill = Mathf.MoveTowards(_ghostFill, target, _ghostDrainSpeed * Time.deltaTime);
            _instancedMaterial.SetFloat(ID_GhostFill, _ghostFill);
            yield return null;
        }

        _ghostFill = target;
        _instancedMaterial.SetFloat(ID_GhostFill, _ghostFill);
        _ghostCoroutine = null;
    }

    private IEnumerator HealFlashRoutine()
    {
        float elapsed = 0f;

        while (elapsed < _healFlashDuration)
        {
            elapsed += Time.deltaTime;
            float flash = Mathf.Sin((elapsed / _healFlashDuration) * Mathf.PI);
            _instancedMaterial.SetFloat(ID_HealFlash, flash);
            yield return null;
        }

        _instancedMaterial.SetFloat(ID_HealFlash, 0f);
        _healCoroutine = null;
    }

    // -------------------------------------------------------------------------

    private void UpdateShaderProperties()
    {
        if (_targetStat == null || _instancedMaterial == null) return;

        float fill = _targetStat.MaxValue > 0
                     ? _targetStat.CurrentAmount / _targetStat.MaxValue
                     : 0f;

        _instancedMaterial.SetFloat(ID_FillAmount, fill);
        _instancedMaterial.SetFloat(ID_GhostFill,  _ghostFill);
    }
}