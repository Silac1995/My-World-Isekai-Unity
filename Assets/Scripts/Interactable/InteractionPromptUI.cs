using UnityEngine;
using TMPro;

public class InteractionPromptUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI promptText;
    private Transform target;
    private Collider targetCollider;

    public void SetTarget(Transform followTarget)
    {
        target = followTarget;
        // On récupère le collider principal (celui du corps)
        targetCollider = followTarget.GetComponentInChildren<Collider>();

        if (promptText != null)
            promptText.text = "Press [E]";
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