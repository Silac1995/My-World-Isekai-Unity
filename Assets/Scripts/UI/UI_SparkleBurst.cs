using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A highly optimized, purely GPU-driven UI explosion effect.
/// Spawns a single UI Image overlay and animates a procedural GPU shader.
/// Keeps the multiplayer architecture safe because it runs entirely locally on the Canvas HUD.
/// </summary>
public class UI_SparkleBurst : MonoBehaviour
{
    private static readonly int ID_Progress = Shader.PropertyToID("_Progress");
    private static readonly int ID_Color = Shader.PropertyToID("_Color");

    /// <summary>
    /// Spawns a procedural GPU burst radiating from the origin.
    /// </summary>
    public static void Spawn(RectTransform origin, Color color, float duration = 0.6f)
    {
        Shader shader = Shader.Find("UI/SparkleExplosion");
        if (shader == null)
        {
            Debug.LogWarning("[UI_SparkleBurst] Cannot find 'UI/SparkleExplosion' shader! Have you included it?");
            return;
        }

        // Create root container
        GameObject root = new GameObject("UI_ShaderExplosion", typeof(RectTransform), typeof(UI_SparkleBurst));
        RectTransform rt = root.GetComponent<RectTransform>();
        rt.SetParent(origin, false);
        rt.localPosition = Vector3.zero;
        
        // Ensure it doesn't block raycasts
        root.AddComponent<CanvasGroup>().blocksRaycasts = false;

        // Make the explosion container safely larger than the parent bar so particles can fly OUTSIDE the bar
        rt.anchorMin = new Vector2(0, 0);
        rt.anchorMax = new Vector2(1, 1);
        rt.offsetMin = new Vector2(-150f, -150f);
        rt.offsetMax = new Vector2(150f, 150f);
        
        Image img = root.AddComponent<Image>();
        img.raycastTarget = false; // Optimize UI
        
        Material instancedMat = new Material(shader);
        instancedMat.SetColor(ID_Color, color);
        instancedMat.SetFloat(ID_Progress, 0f);
        img.material = instancedMat;

        root.GetComponent<UI_SparkleBurst>().StartBurst(instancedMat, duration);
    }

    private void StartBurst(Material mat, float duration)
    {
        StartCoroutine(BurstRoutine(mat, duration));
    }

    private IEnumerator BurstRoutine(Material mat, float duration)
    {
        float elapsed = 0f;
        
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            
            // Push linear progress. The shader calculates the easing and positions.
            mat.SetFloat(ID_Progress, t);

            yield return null;
        }

        Destroy(mat);
        Destroy(gameObject);
    }
}
