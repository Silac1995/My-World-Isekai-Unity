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

    [Header("Animation")]
    [SerializeField] private float _fillAnimationDuration = 0.25f;

    [Header("Colors (Overrides Shader)")]
    [SerializeField] private Color _expColor   = new Color(1f, 0.84f, 0f, 1f); // Gold
    [SerializeField] private Color _ghostColor = new Color(1f, 0.95f, 0.6f, 1f); // Lighter gold
    [SerializeField] private Color _flashColor = new Color(1f, 1f, 1f, 1f);      // White flash

    // ── Runtime state ────────────────────────────────────────────

    private CharacterCombatLevel _targetExpSys;
    private Material             _instancedMaterial;

    private float     _ghostFill;
    private float     _currentFill;
    private Coroutine _ghostCoroutine;
    private Coroutine _levelUpCoroutine;
    private Coroutine _fillCoroutine;

    // ── Cached shader property IDs ───────────────────────────────

    private static readonly int ID_FillAmount      = Shader.PropertyToID("_FillAmount");
    private static readonly int ID_GhostFill       = Shader.PropertyToID("_GhostFill");
    private static readonly int ID_HealFlash       = Shader.PropertyToID("_HealFlash");
    private static readonly int ID_FlashWhole      = Shader.PropertyToID("_FlashWholeBarToggle");
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
            _instancedMaterial.SetFloat(ID_FlashWhole, 1f); // Enable full-bar flash for Level Ups
        }

        _targetExpSys.OnExperienceChanged += HandleExperienceChanged;
        _targetExpSys.OnLevelChanged      += HandleLevelChanged;

        _currentFill = GetFillRatio();
        _ghostFill = _currentFill;
        SnapToShader();
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

        // Add Experience behaves like a "heal" in visual terms (fills up)
        if (newFill > _currentFill)
        {
            OnExperienceGained(newFill >= 0.999f);
        }
        else if (newFill < _currentFill)
        {
            // Edge case: lost experience
            OnExperienceLost(_currentFill);
        }

        AnimateToShader();
    }

    private void HandleLevelChanged()
    {
        if (_instancedMaterial == null) return;

        _ghostFill = 0f; // Reset ghost
        AnimateToShader();

        if (_levelUpCoroutine != null) StopCoroutine(_levelUpCoroutine);
        _levelUpCoroutine = StartCoroutine(LevelUpFlashRoutine(true));
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

    private void OnExperienceGained(bool isMax)
    {
        if (_instancedMaterial == null) return;

        // Do not snap ghost up instantly here. 
        // We will sync it with the primary fill animation so it doesn't pop ahead.

        if (isMax)
        {
            if (_levelUpCoroutine != null) StopCoroutine(_levelUpCoroutine);
            _levelUpCoroutine = StartCoroutine(LevelUpFlashRoutine(true)); // Flash when reaching FULL exp
        }
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

    private IEnumerator LevelUpFlashRoutine(bool withSparkles)
    {
        if (withSparkles && _barImage != null)
        {
            // Spawns a procedural GPU Shader explosion over the bar.
            // Will only render locally on this canvas, avoiding multiplayer syncing overhead.
            UI_SparkleBurst.Spawn(_barImage.rectTransform, _expColor, 0.6f);
        }

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

            // If gaining exp, sync ghost with current fill so it doesn't pop ahead
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
        if (_targetExpSys == null) return 0f;
        
        float requiredExp = _targetExpSys.GetRequiredExpForNextLevel();
        return requiredExp > 0f ? (float)_targetExpSys.CurrentExperience / requiredExp : 0f;
    }

    private void SnapToShader()
    {
        if (_targetExpSys == null || _instancedMaterial == null) return;

        _instancedMaterial.SetFloat(ID_FillAmount, _currentFill);
        _instancedMaterial.SetFloat(ID_GhostFill,  _ghostFill);
    }

    private void AnimateToShader()
    {
        if (_targetExpSys == null || _instancedMaterial == null) return;

        if (_fillCoroutine != null) StopCoroutine(_fillCoroutine);
        _fillCoroutine = StartCoroutine(FillAnimationRoutine());
    }

    private void CleanupEvents()
    {
        if (_targetExpSys == null) return;
        _targetExpSys.OnExperienceChanged -= HandleExperienceChanged;
        _targetExpSys.OnLevelChanged      -= HandleLevelChanged;
    }
}
