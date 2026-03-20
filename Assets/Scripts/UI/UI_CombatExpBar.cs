using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives the UI/HealthBar shader for Experience Points.
/// Subscribes to a <see cref="CharacterCombatLevel"/> and pushes fill / ghost / flash
/// values to an instanced material each time the exp changes.
/// </summary>
public class UI_CombatExpBar : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Image _barImage;

    [Header("Level Up Flash")]
    [SerializeField] private float _levelUpFlashDuration = 0.5f;

    [Header("Colors (Overrides Shader)")]
    [SerializeField] private Color _expColor   = new Color(1f, 0.84f, 0f, 1f); // Gold
    [SerializeField] private Color _ghostColor = new Color(1f, 0.95f, 0.6f, 1f); // Lighter gold
    [SerializeField] private Color _flashColor = new Color(1f, 1f, 1f, 1f);      // White flash

    // ── Runtime state ────────────────────────────────────────────

    private CharacterCombatLevel _targetExpSys;
    private Material             _instancedMaterial;

    private float     _ghostFill;
    private Coroutine _ghostCoroutine;
    private Coroutine _levelUpCoroutine;

    // ── Cached shader property IDs ───────────────────────────────

    private static readonly int ID_FillAmount      = Shader.PropertyToID("_FillAmount");
    private static readonly int ID_GhostFill       = Shader.PropertyToID("_GhostFill");
    private static readonly int ID_HealFlash       = Shader.PropertyToID("_HealFlash");
    private static readonly int ID_GhostDelay      = Shader.PropertyToID("_GhostDelay");
    private static readonly int ID_GhostSpeed      = Shader.PropertyToID("_GhostDrainSpeed");
    private static readonly int ID_HealthColor     = Shader.PropertyToID("_HealthColor");
    private static readonly int ID_LowHealthColor  = Shader.PropertyToID("_LowHealthColor");
    private static readonly int ID_GhostColor      = Shader.PropertyToID("_GhostColor");
    private static readonly int ID_HealColor       = Shader.PropertyToID("_HealColor");

    // ── Public API ───────────────────────────────────────────────

    public void Initialize(CharacterCombatLevel expSys)
    {
        CleanupEvents();
        _targetExpSys = expSys;

        if (_targetExpSys == null) return;

        if (_barImage != null && _instancedMaterial == null)
        {
            _instancedMaterial = new Material(_barImage.material);
            _barImage.material = _instancedMaterial;

            // Override original shader colors to EXP colors
            _instancedMaterial.SetColor(ID_HealthColor, _expColor);
            _instancedMaterial.SetColor(ID_LowHealthColor, _expColor);
            _instancedMaterial.SetColor(ID_GhostColor, _ghostColor);
            _instancedMaterial.SetColor(ID_HealColor, _flashColor);
        }

        _targetExpSys.OnExperienceChanged += HandleExperienceChanged;
        _targetExpSys.OnLevelChanged      += HandleLevelChanged;

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

    private void HandleExperienceChanged()
    {
        float newFill = GetFillRatio();
        float currentFill = _instancedMaterial != null ? _instancedMaterial.GetFloat(ID_FillAmount) : 0f;

        // Add Experience behaves like a "heal" in visual terms (fills up)
        if (newFill > currentFill)
        {
            OnExperienceGained();
        }
        else if (newFill < currentFill)
        {
            // Edge case: lost experience
            OnExperienceLost(currentFill);
        }

        PushToShader();
    }

    private void HandleLevelChanged()
    {
        if (_instancedMaterial == null) return;

        _ghostFill = 0f; // Reset ghost
        PushToShader();

        if (_levelUpCoroutine != null) StopCoroutine(_levelUpCoroutine);
        _levelUpCoroutine = StartCoroutine(LevelUpFlashRoutine());
    }

    // ── Visual effects ────────────────────────────────────

    private void OnExperienceLost(float oldAmountRatio)
    {
        if (_instancedMaterial == null) return;

        // Snap ghost to pre-loss ratio so it stays frozen during the delay.
        _ghostFill = Mathf.Max(_ghostFill, oldAmountRatio);

        if (_ghostCoroutine != null) StopCoroutine(_ghostCoroutine);
        _ghostCoroutine = StartCoroutine(GhostDrainRoutine());
    }

    private void OnExperienceGained()
    {
        if (_instancedMaterial == null) return;

        // Snap ghost up so it never shows below the new fill.
        _ghostFill = GetFillRatio();

        if (_levelUpCoroutine != null) StopCoroutine(_levelUpCoroutine);
        _levelUpCoroutine = StartCoroutine(LevelUpFlashRoutine()); // Flash briefly when gaining exp
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

    private IEnumerator LevelUpFlashRoutine()
    {
        float elapsed = 0f;
        float duration = _levelUpFlashDuration;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float flash = Mathf.Sin((elapsed / duration) * Mathf.PI);
            _instancedMaterial.SetFloat(ID_HealFlash, flash);
            yield return null;
        }

        _instancedMaterial.SetFloat(ID_HealFlash, 0f);
        _levelUpCoroutine = null;
    }

    // ── Helpers ───────────────────────────────────────────────────

    private float GetFillRatio()
    {
        if (_targetExpSys == null) return 0f;
        
        float requiredExp = _targetExpSys.GetRequiredExpForNextLevel();
        return requiredExp > 0f ? (float)_targetExpSys.CurrentExperience / requiredExp : 0f;
    }

    private void PushToShader()
    {
        if (_targetExpSys == null || _instancedMaterial == null) return;

        _instancedMaterial.SetFloat(ID_FillAmount, GetFillRatio());
        _instancedMaterial.SetFloat(ID_GhostFill,  _ghostFill);
    }

    private void CleanupEvents()
    {
        if (_targetExpSys == null) return;
        _targetExpSys.OnExperienceChanged -= HandleExperienceChanged;
        _targetExpSys.OnLevelChanged      -= HandleLevelChanged;
    }
}
