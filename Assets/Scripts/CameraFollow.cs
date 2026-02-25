using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [Header("Camera Position")]
    [SerializeField] private float minZPosition = 60f;

    [Header("Zoom Settings")]
    [SerializeField] private float minOffsetY = 13f;
    [SerializeField] private float maxOffsetY = 18f;
    [SerializeField] private float minOffsetZ = -12.5f;
    [SerializeField] private float maxOffsetZ = -23.5f;
    [SerializeField] private float zoomSpeed = 2f;
    [SerializeField] private float zoomSmoothing = 8f;
    [SerializeField] private float followSmoothing = 0.15f;

    [Header("Camera Rotation")]
    [Range(0f, 90f)]
    [SerializeField] private float rotationX = 13f;

    [SerializeField] private Transform target;
    [SerializeField] private Character character;
    [SerializeField] private PlayerUI playerUI;

    private GameObject targetGameObject;
    private Camera _camera;
    private float _targetZoom = 0.5f;
    private float _currentZoom = 0.5f;
    private Vector3 _smoothVelocity;

    private void Start()
    {
        _camera = GetComponent<Camera>();
        if (_camera != null)
        {
            _camera.transparencySortMode = TransparencySortMode.CustomAxis;
            _camera.transparencySortAxis = new Vector3(0, 0, 1);
        }
    }

    private void LateUpdate()
    {
        if (target == null) return;

        // Zoom via scroll (scroll up = zoom in = 0, scroll down = zoom out = 1)
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.01f)
        {
            _targetZoom = Mathf.Clamp01(_targetZoom - scroll * zoomSpeed);
        }

        _currentZoom = Mathf.Lerp(_currentZoom, _targetZoom, Time.deltaTime * zoomSmoothing);

        // Interpolation des offsets selon le zoom
        float offsetY = Mathf.Lerp(minOffsetY, maxOffsetY, _currentZoom);
        float offsetZ = Mathf.Lerp(minOffsetZ, maxOffsetZ, _currentZoom);

        // Calcul de la position Z avec offset et limite minimum
        float desiredZ = target.position.z + offsetZ;
        float clampedZ = Mathf.Max(desiredZ, minZPosition);

        // Position cible avec smoothing
        Vector3 targetPos = new Vector3(
            target.position.x,
            target.position.y + offsetY,
            clampedZ
        );

        transform.position = Vector3.SmoothDamp(transform.position, targetPos, ref _smoothVelocity, followSmoothing);

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