using UnityEngine;
using TMPro;

public class InteractionPromptUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI promptText;
    [SerializeField] private Vector3 offset = new Vector3(0, 2f, 0); // Augmente la hauteur

    private Transform target;

    public void SetTarget(Transform followTarget)
    {
        target = followTarget;
        if (promptText != null)
            promptText.text = "Press E";
    }

    private void LateUpdate()
    {
        if (target == null)
        {
            Destroy(gameObject);
            return;
        }

        // Positionner le prompt avec l'offset dans l'espace monde
        transform.position = target.position + offset;

        // Faire face à la caméra, en ignorant la rotation Y
        Vector3 directionToCamera = Camera.main.transform.position - transform.position;
        directionToCamera.y = 0;
        if (directionToCamera != Vector3.zero)
        {
            transform.rotation = Quaternion.LookRotation(-directionToCamera, Vector3.up);
        }
    }
}