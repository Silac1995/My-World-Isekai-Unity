using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [Header("Camera Position")]
    [SerializeField] private float fixedYPosition = 15f;
    [SerializeField] private float fixedZPosition = 60f;

    [Header("Camera Rotation")]
    [SerializeField] private float rotationX = 2f;

    [SerializeField] private Transform target;
    [SerializeField] private PlayerUI playerUI;
    [SerializeField] new GameObject gameObject; // Hides inherited Component.gameObject

    private void LateUpdate()
    {
        if (target == null) return;

        // Follow target on X, lock Y and Z
        Vector3 newPosition = new Vector3(
            target.position.x,
            fixedYPosition,   // fixed Y
            fixedZPosition    // fixed Z
        );

        transform.position = newPosition;

        // Fixed rotation X = 2°, no changes
        transform.rotation = Quaternion.Euler(2f, 0f, 0f);
    }


    public void SetGameObject(GameObject newGameObject)
    {
        this.gameObject = newGameObject;

        if (newGameObject != null)
        {
            SetTarget(newGameObject.transform);
        }
        else
        {
            SetTarget(null);
        }

        if (playerUI != null)
        {
            playerUI.Initialize(newGameObject);
        }
    }

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }
}