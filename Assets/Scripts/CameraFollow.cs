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

    [Header("Occlusion")]
    [Tooltip("Layers qui bloquent la vue (mettre Environment)")]
    [SerializeField] private LayerMask _occlusionLayer;
    [SerializeField] private float _occlusionRadius = 0.3f;
    [SerializeField] private float _minCameraDistance = 2f;
    [SerializeField] private float _occlusionSmoothing = 10f;

    [SerializeField] private Transform target;
    [SerializeField] private Character character;
    [SerializeField] private PlayerUI playerUI;

    private GameObject targetGameObject;
    private Camera _camera;
    private float _targetZoom = 0.5f;
    private float _currentZoom = 0.5f;
    private Vector3 _smoothVelocity;
    private float _currentOcclusionT = 1f; // 1 = position normale, 0 = au plus pres du perso

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

        // Zoom via scroll
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

        // Position cible ideale (sans occlusion)
        Vector3 idealPos = new Vector3(
            target.position.x,
            target.position.y + offsetY,
            clampedZ
        );

        // --- OCCLUSION AVOIDANCE ---
        // On utilise le centre visuel du personnage pour l''occlusion (pas les pieds)
        Vector3 occlusionOrigin = target.position;
        if (character != null && character.CharacterVisual != null)
        {
            occlusionOrigin = character.CharacterVisual.GetVisualCenter();
        }
        Vector3 finalPos = HandleOcclusion(occlusionOrigin, idealPos);

        transform.position = Vector3.SmoothDamp(transform.position, finalPos, ref _smoothVelocity, followSmoothing);
        transform.rotation = Quaternion.Euler(rotationX, 0f, 0f);
    }

    /// <summary>
    /// SphereCast du personnage vers la position ideale de la camera.
    /// Si un obstacle est detecte, on rapproche la camera.
    /// </summary>
    private Vector3 HandleOcclusion(Vector3 targetPos, Vector3 idealCamPos)
    {
        Vector3 direction = idealCamPos - targetPos;
        float maxDistance = direction.magnitude;

        if (maxDistance < 0.1f) return idealCamPos;

        float targetT = 1f; // Par defaut, position ideale

        if (Physics.SphereCast(targetPos, _occlusionRadius, direction.normalized, out RaycastHit hit, maxDistance, _occlusionLayer))
        {
            // Un obstacle bloque la vue : on place la camera juste devant
            float safeDistance = Mathf.Max(hit.distance - 0.2f, _minCameraDistance);
            targetT = safeDistance / maxDistance;
            targetT = Mathf.Clamp01(targetT);
        }

        // Smooth transition : rapide pour rentrer (eviter le clip), plus lent pour sortir
        float smoothSpeed = (targetT < _currentOcclusionT) ? _occlusionSmoothing * 2f : _occlusionSmoothing;
        _currentOcclusionT = Mathf.Lerp(_currentOcclusionT, targetT, Time.deltaTime * smoothSpeed);

        return Vector3.Lerp(targetPos, idealCamPos, _currentOcclusionT);
    }

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