using System.Collections;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Manages a single ground circle indicator beneath a character during battle.
/// Instantiated as a prefab, parented to the character's root transform.
/// Lifecycle managed exclusively by BattleCircleManager.
/// </summary>
public class BattleGroundCircle : MonoBehaviour
{
    [SerializeField] private DecalProjector _decalProjector;

    private Coroutine _fadeCoroutine;
    private bool _isCleaningUp;

    private const float FADE_DURATION = 0.3f;
    private const float DIM_FADE_FACTOR = 0.25f;

    /// <summary>
    /// Assigns the shared material and starts fade-in.
    /// Called once by BattleCircleManager after instantiation.
    /// </summary>
    public void Initialize(Material material)
    {
        if (_decalProjector == null)
            _decalProjector = GetComponent<DecalProjector>();

        _decalProjector.material = material;
        _decalProjector.fadeFactor = 0f;

        _fadeCoroutine = StartCoroutine(FadeTo(1f));
    }

    /// <summary>
    /// Reduces opacity for incapacitated characters. Circle stays visible but faded.
    /// </summary>
    public void Dim()
    {
        if (_isCleaningUp) return;
        StopActiveFade();
        _fadeCoroutine = StartCoroutine(FadeTo(DIM_FADE_FACTOR));
    }

    /// <summary>
    /// Restores full opacity for revived characters.
    /// </summary>
    public void Restore()
    {
        if (_isCleaningUp) return;
        StopActiveFade();
        _fadeCoroutine = StartCoroutine(FadeTo(1f));
    }

    /// <summary>
    /// Fade-out then self-destruct. Guarded against double-calls.
    /// </summary>
    public void Cleanup()
    {
        if (_isCleaningUp) return;
        _isCleaningUp = true;
        StopActiveFade();
        _fadeCoroutine = StartCoroutine(FadeOutAndDestroy());
    }

    private IEnumerator FadeTo(float targetFade)
    {
        if (_decalProjector == null) yield break;

        float startFade = _decalProjector.fadeFactor;
        float elapsed = 0f;

        while (elapsed < FADE_DURATION)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / FADE_DURATION);

            if (_decalProjector == null) yield break;
            _decalProjector.fadeFactor = Mathf.Lerp(startFade, targetFade, t);

            yield return null;
        }

        if (_decalProjector != null)
            _decalProjector.fadeFactor = targetFade;
    }

    private IEnumerator FadeOutAndDestroy()
    {
        if (_decalProjector != null)
        {
            float startFade = _decalProjector.fadeFactor;
            float elapsed = 0f;

            while (elapsed < FADE_DURATION)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / FADE_DURATION);

                if (_decalProjector == null) break;
                _decalProjector.fadeFactor = Mathf.Lerp(startFade, 0f, t);

                yield return null;
            }
        }

        Destroy(gameObject);
    }

    private void StopActiveFade()
    {
        if (_fadeCoroutine != null)
        {
            StopCoroutine(_fadeCoroutine);
            _fadeCoroutine = null;
        }
    }

    private void OnDestroy()
    {
        StopActiveFade();
    }
}
