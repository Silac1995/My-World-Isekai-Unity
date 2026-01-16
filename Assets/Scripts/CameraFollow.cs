using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [Header("Camera Position")]
    [SerializeField] private float fixedYPosition = 20F;
    [SerializeField] private float minZPosition = 70f;
    [SerializeField] private float offsetZ = -14f;

    [Header("Camera Rotation")]
    // Le Range crée un curseur entre 0 et 90 degrés dans l'inspecteur
    [Range(0f, 90f)]
    [SerializeField] private float rotationX = 18f;

    [SerializeField] private Transform target;
    [SerializeField] private Character character;
    [SerializeField] private PlayerUI playerUI;

    private GameObject targetGameObject;

    private void Start()
    {
        
    }

    private void LateUpdate()
    {
        if (target == null) return;

        // Calcul de la position Z avec offset et limite minimum
        float desiredZ = target.position.z + offsetZ;
        float clampedZ = Mathf.Max(desiredZ, minZPosition);

        // Application de la position
        transform.position = new Vector3(
            target.position.x,
            fixedYPosition,
            clampedZ
        );

        // Utilisation de la variable rotationX pour permettre le changement via l'inspecteur
        transform.rotation = Quaternion.Euler(rotationX, 0f, 0f);
    }

    // ... Reste de tes méthodes SetGameObject et SetTarget identiques ...
    public void SetGameObject(GameObject newGameObject)
    {
        this.targetGameObject = newGameObject;
        if (newGameObject != null)
            SetTarget(newGameObject.transform, newGameObject);
        else
            SetTarget(null, null);

        if (playerUI != null)
            playerUI.Initialize(newGameObject);
    }

    public void SetTarget(Transform newTarget, GameObject go)
    {
        target = newTarget;
        if (go != null)
        {
            character = go.GetComponent<Character>();
            CharacterEquipmentUI uiEquip = Object.FindFirstObjectByType<CharacterEquipmentUI>();
            if (uiEquip != null) uiEquip.SetupUI(character);
        }
    }
}