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

    // IDs des propriétés du shader (plus performant que d'utiliser des strings à chaque fois)
    private static readonly int BlinkFactorID = Shader.PropertyToID("_BlinkFactor");
    private static readonly int BlinkColorID = Shader.PropertyToID("_BlinkColor");

    private void Awake()
    {
        if (characterVisual == null)
            characterVisual = GetComponent<CharacterVisual>();
    }

    private void OnDestroy()
    {
        // TRÈS IMPORTANT : Détruire les instances de matériaux créées pour éviter les fuites mémoire
        foreach (var mat in materialCache.Values)
        {
            if (mat != null) Destroy(mat);
        }
        materialCache.Clear();
    }

    /// <summary>
    /// Lance un flash sur le personnage en utilisant la couleur définie dans le shader.
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
            
            // On descend graduellement de maxBlinkFactor à 0
            float currentFactor = Mathf.Lerp(maxBlinkFactor, 0f, lerpVal);
            
            SetBlinkProperties(currentFactor, color);
            
            yield return null;
        }

        // Sécurité finale : on s'assure d'être à 0
        SetBlinkProperties(0f, color);

        blinkCoroutine = null;
    }

    private void SetBlinkProperties(float factor, Color? color = null)
    {
        if (characterVisual == null || characterVisual.AllRenderers == null) return;

        foreach (var sr in characterVisual.AllRenderers)
        {
            if (sr == null) continue;

            // On récupère ou crée l'instance du matériau
            if (!materialCache.TryGetValue(sr, out Material mat))
            {
                // Accéder à sr.material crée une instance unique (clone)
                mat = sr.material;
                materialCache[sr] = mat;
            }

            if (color.HasValue)
                mat.SetColor(BlinkColorID, color.Value);
            
            mat.SetFloat(BlinkFactorID, factor);
        }
    }
}
