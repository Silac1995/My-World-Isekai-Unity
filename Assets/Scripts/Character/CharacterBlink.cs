using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CharacterBlink : MonoBehaviour
{
    [Header("Components")]
    [SerializeField] private CharacterVisual characterVisual;

    [Header("Settings")]
    [SerializeField] private float defaultBlinkDuration = 0.3f;
    [SerializeField] private float maxBlinkFactor = 0.5f;

    private Dictionary<SpriteRenderer, Material> materialCache = new Dictionary<SpriteRenderer, Material>();
    private Coroutine blinkCoroutine;

    // Shader property IDs (faster than using strings every time)
    private static readonly int BlinkFactorID = Shader.PropertyToID("_BlinkFactor");
    private static readonly int BlinkColorID = Shader.PropertyToID("_BlinkColor");

    private void Awake()
    {
        if (characterVisual == null)
            characterVisual = GetComponent<CharacterVisual>();
    }

    private void OnDestroy()
    {
        // VERY IMPORTANT: destroy the material instances we created to avoid memory leaks
        foreach (var mat in materialCache.Values)
        {
            if (mat != null) Destroy(mat);
        }
        materialCache.Clear();
    }

    /// <summary>
    /// Triggers a flash on the character using the colour defined in the shader.
    /// </summary>
    [ContextMenu("Test Blink")]
    public void Blink()
    {
        Blink(defaultBlinkDuration);
    }

    public void Blink(float duration)
    {
        if (blinkCoroutine != null)
            StopCoroutine(blinkCoroutine);
        
        blinkCoroutine = StartCoroutine(BlinkRoutine(duration));
    }

    public void Blink(Color color, float duration)
    {
        if (blinkCoroutine != null)
            StopCoroutine(blinkCoroutine);
        
        blinkCoroutine = StartCoroutine(BlinkRoutine(duration, color));
    }

    private IEnumerator BlinkRoutine(float duration, Color? color = null)
    {
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float lerpVal = elapsed / duration;
            
            // Gradually go down from maxBlinkFactor to 0
            float currentFactor = Mathf.Lerp(maxBlinkFactor, 0f, lerpVal);
            
            SetBlinkProperties(currentFactor, color);
            
            yield return null;
        }

        // Final safety: make sure we're at 0
        SetBlinkProperties(0f, color);

        blinkCoroutine = null;
    }

    private void SetBlinkProperties(float factor, Color? color = null)
    {
        if (characterVisual == null || characterVisual.AllRenderers == null) return;

        foreach (var sr in characterVisual.AllRenderers)
        {
            if (sr == null) continue;

            // Fetch or create the material instance
            if (!materialCache.TryGetValue(sr, out Material mat))
            {
                // Accessing sr.material creates a unique instance (clone)
                mat = sr.material;
                materialCache[sr] = mat;
            }

            if (color.HasValue)
                mat.SetColor(BlinkColorID, color.Value);
            
            mat.SetFloat(BlinkFactorID, factor);
        }
    }
}
