using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [Header("Camera Position")]
    [SerializeField] private float offsetY = 10.5F;
    [SerializeField] private float minZPosition = 60f;
    [SerializeField] private float offsetZ = -17f;

    [Header("Camera Rotation")]
    // Le Range crée un curseur entre 0 et 90 degrés dans l'inspecteur
    [Range(0f, 90f)]
    [SerializeField] private float rotationX = 6f;

    [SerializeField] private Transform target;
    [SerializeField] private Character character;
    [SerializeField] private PlayerUI playerUI;

    private GameObject targetGameObject;
    private Camera _camera;

    private void Start()
    {
        _camera = GetComponent<Camera>();
        if (_camera != null)
        {
            // Tri des sprites basé uniquement sur Z (profondeur dans le monde)
            // Corrige le sorting incorrect quand les personnages sont à des hauteurs Y différentes
            _camera.transparencySortMode = TransparencySortMode.CustomAxis;
            _camera.transparencySortAxis = new Vector3(0, 0, 1);
        }
    }

    private void LateUpdate()
    {
        if (target == null) return;

        // Calcul de la position Z avec offset et limite minimum
        float desiredZ = target.position.z + offsetZ;
        float clampedZ = Mathf.Max(desiredZ, minZPosition);

        // Application de la position (suit le personnage sur tous les axes)
        transform.position = new Vector3(
            target.position.x,
            target.position.y + offsetY,
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