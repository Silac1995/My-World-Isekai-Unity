using System.Collections;
using UnityEngine;

/// <summary>
/// Manages a single ground circle indicator beneath a character during battle.
/// Uses a flat quad mesh with the Custom/BattleGroundCircle unlit transparent shader.
/// Fade is driven via MaterialPropertyBlock so shared materials are never modified.
/// Lifecycle managed exclusively by BattleCircleManager.
/// </summary>
public class BattleGroundCircle : MonoBehaviour
{
    [SerializeField] private MeshRenderer _meshRenderer;

    private MaterialPropertyBlock _mpb;
    private float _currentFade;
    private Coroutine _fadeCoroutine;
    private bool _isCleaningUp;

    private static readonly int _fadePropId = Shader.PropertyToID("_FadeFactor");
    private static readonly int _initProgressId = Shader.PropertyToID("_InitProgress");
    private static readonly int _initFlashId = Shader.PropertyToID("_InitFlash");

    private float _flashTimer;
    private bool _wasReady;

    private const float FADE_DURATION   = 0.3f;
    private const float DIM_FADE_FACTOR = 0.25f;
    private const float FLASH_DURATION  = 0.4f;

    /// <summary>
    /// Assigns the shared material and starts fade-in.
    /// Called once by BattleCircleManager after instantiation.
    /// </summary>
    public void Initialize(Material material)
    {
        if (_meshRenderer == null)
            _meshRenderer = GetComponent<MeshRenderer>();

        _mpb = new MaterialPropertyBlock();
        _meshRenderer.sharedMaterial = material;
        SetFade(0f);

        _fadeCoroutine = StartCoroutine(FadeTo(1f));
    }

    /// <summary>Reduces opacity for incapacitated characters.</summary>
    public void Dim()
    {
        if (_isCleaningUp) return;
        StopActiveFade();
        _fadeCoroutine = StartCoroutine(FadeTo(DIM_FADE_FACTOR));
    }

    /// <summary>Restores full opacity for revived characters.</summary>
    public void Restore()
    {
        if (_isCleaningUp) return;
        StopActiveFade();
        _fadeCoroutine = StartCoroutine(FadeTo(1f));
    }

    /// <summary>Fade-out then self-destruct. Guarded against double-calls.</summary>
    public void Cleanup()
    {
        if (_isCleaningUp) return;
        _isCleaningUp = true;
        StopActiveFade();
        _fadeCoroutine = StartCoroutine(FadeOutAndDestroy());
    }

    /// <summary>
    /// Sets the initiative fill (0 = empty, 1 = full). When it first reaches 1, triggers a short flash.
    /// </summary>
    public void SetInitiativeProgress(float progress01)
    {
        if (_mpb == null) return;

        bool isReady = progress01 >= 1f;
        if (isReady && !_wasReady)
            _flashTimer = FLASH_DURATION; // trigger flash on the frame it fills

        _wasReady = isReady;

        _mpb.SetFloat(_initProgressId, progress01);
        // Flash is updated in the same call to avoid extra SetPropertyBlock
        _mpb.SetFloat(_initFlashId, Mathf.Clamp01(_flashTimer / FLASH_DURATION));
        _meshRenderer.SetPropertyBlock(_mpb);
    }

    private void Update()
    {
        if (_flashTimer > 0f)
        {
            _flashTimer -= Time.deltaTime;
            if (_mpb != null && _meshRenderer != null)
            {
                _mpb.SetFloat(_initFlashId, Mathf.Clamp01(_flashTimer / FLASH_DURATION));
                _meshRenderer.SetPropertyBlock(_mpb);
            }
        }
    }

    private void SetFade(float value)
    {
        _currentFade = value;
        _mpb.SetFloat(_fadePropId, value);
        _meshRenderer.SetPropertyBlock(_mpb);
    }

    private IEnumerator FadeTo(float target)
    {
        float start   = _currentFade;
        float elapsed = 0f;
        while (elapsed < FADE_DURATION)
        {
            elapsed += Time.unscaledDeltaTime;
            SetFade(Mathf.Lerp(start, target, Mathf.Clamp01(elapsed / FADE_DURATION)));
            yield return null;
        }
        SetFade(target);
    }

    private IEnumerator FadeOutAndDestroy()
    {
        float start   = _currentFade;
        float elapsed = 0f;
        while (elapsed < FADE_DURATION)
        {
            elapsed += Time.unscaledDeltaTime;
            SetFade(Mathf.Lerp(start, 0f, Mathf.Clamp01(elapsed / FADE_DURATION)));
            yield return null;
        }
        Destroy(gameObject);
    }

    private void StopActiveFade()
    {
        if (_fadeCoroutine == null) return;
        StopCoroutine(_fadeCoroutine);
        _fadeCoroutine = null;
    }

    private void OnDestroy()
    {
        StopActiveFade();
    }
}
