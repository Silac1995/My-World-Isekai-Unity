using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class InteractionPromptUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI promptText;
    [SerializeField] private Image fillBar;
    private Transform target;
    private Collider targetCollider;

    public void SetTarget(Transform followTarget, string customPromptText = "E")
    {
        target = followTarget;
        // On récupère le collider principal (celui du corps)
        targetCollider = followTarget.GetComponentInChildren<Collider>();

        if (promptText != null)
            promptText.text = customPromptText;
            
        SetFillAmount(0f);
    }

    public void SetFillAmount(float fillAmount)
    {
        if (fillBar != null)
        {
            fillBar.fillAmount = Mathf.Clamp01(fillAmount);
        }
    }

    private void LateUpdate()
    {
        if (target == null)
        {
            Destroy(gameObject);
            return;
        }

        Vector3 targetPos;

        if (targetCollider != null)
        {
            // Utilise le centre géométrique exact du collider (X, Y, Z)
            targetPos = targetCollider.bounds.center;
        }
        else
        {
            // Fallback : position du pivot si pas de collider
            targetPos = target.position;
        }

        transform.position = targetPos;

        // Toujours face à la caméra
        if (Camera.main != null)
        {
            transform.rotation = Quaternion.LookRotation(transform.position - Camera.main.transform.position);
        }
    }
}