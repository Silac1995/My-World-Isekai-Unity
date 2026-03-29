using UnityEngine;
using Unity.Netcode;
using MWI.UI.Notifications;

public class FurniturePlacementManager : CharacterSystem
{
    [Header("Settings")]
    [SerializeField] private LayerMask _groundLayer;
    [SerializeField] private LayerMask _obstacleLayer;
    [SerializeField] private KeyCode _placementKey = KeyCode.F;
    [SerializeField] private float _maxPlacementRange = 10f;
    [SerializeField] private Material _ghostMaterialValid;
    [SerializeField] private Material _ghostMaterialInvalid;

    [Header("Notifications")]
    [SerializeField] private ToastNotificationChannel _toastChannel;

    private GameObject _ghostInstance;
    private FurnitureItemSO _activeFurnitureItemSO;
    private Furniture _ghostFurnitureComponent;
    private bool _isPlacementActive;
    private bool _isDebugMode;
    private Quaternion _ghostRotation = Quaternion.identity;

    public bool IsPlacementActive => _isPlacementActive;

    // ────────────────────── Entry Points ──────────────────────

    /// <summary>
    /// Debug mode: enter placement without carrying the item. Called by DebugScript.
    /// </summary>
    public void StartPlacementDebug(FurnitureItemSO furnitureItemSO)
    {
        if (furnitureItemSO == null || furnitureItemSO.InstalledFurniturePrefab == null)
        {
            Debug.LogError("[FurniturePlacementManager] Invalid FurnitureItemSO or missing installed prefab.");
            return;
        }

        _isDebugMode = true;
        StartPlacement(furnitureItemSO);
    }

    private void StartPlacement(FurnitureItemSO furnitureItemSO)
    {
        ClearGhost();

        _activeFurnitureItemSO = furnitureItemSO;
        _ghostInstance = Instantiate(furnitureItemSO.InstalledFurniturePrefab.gameObject);
        _ghostFurnitureComponent = _ghostInstance.GetComponent<Furniture>();
        _ghostRotation = Quaternion.identity;

        // Disable physics/logic on ghost
        if (_ghostInstance.TryGetComponent(out Rigidbody rb)) rb.isKinematic = true;
        foreach (var col in _ghostInstance.GetComponentsInChildren<Collider>()) col.enabled = false;
        if (_ghostInstance.TryGetComponent(out NetworkObject netObj)) netObj.enabled = false;

        // Set layer to Ignore Raycast so it doesn't block ground raycast or push characters
        SetLayerRecursive(_ghostInstance, LayerMask.NameToLayer("Ignore Raycast"));

        _ghostInstance.name = "FurnitureGhost_" + furnitureItemSO.name;
        _isPlacementActive = true;

        if (_character != null && !_character.IsBuilding)
            _character.SetBuildingState(true);

        ApplyGhostMaterials(_ghostMaterialValid);
    }

    public void CancelPlacement()
    {
        ClearGhost();
        if (_character != null)
            _character.SetBuildingState(false);
    }

    private void ClearGhost()
    {
        if (_ghostInstance != null)
        {
            Destroy(_ghostInstance);
            _ghostInstance = null;
        }
        _isPlacementActive = false;
        _activeFurnitureItemSO = null;
        _ghostFurnitureComponent = null;
        _isDebugMode = false;
    }

    // ────────────────────── CharacterSystem Overrides ──────────────────────

    protected override void HandleIncapacitated(Character character)
    {
        base.HandleIncapacitated(character);
        CancelPlacement();
    }

    protected override void HandleCombatStateChanged(bool inCombat)
    {
        base.HandleCombatStateChanged(inCombat);
        if (inCombat) CancelPlacement();
    }

    private void OnDestroy()
    {
        CancelPlacement();
    }

    // ────────────────────── Frame Update (Player only) ──────────────────────

    private void Update()
    {
        if (!IsOwner) return;

        // Check for placement key press while carrying furniture
        if (!_isPlacementActive && Input.GetKeyDown(_placementKey))
        {
            var hands = _character?.CharacterVisual?.BodyPartsController?.HandsController;
            if (hands != null && hands.CarriedItem is FurnitureItemInstance furnitureItem)
            {
                var furnitureItemSO = furnitureItem.ItemSO as FurnitureItemSO;
                if (furnitureItemSO != null)
                {
                    StartPlacement(furnitureItemSO);
                }
            }
        }

        if (!_isPlacementActive) return;

        UpdateGhostPosition();
        HandleRotationInput();
        HandlePlacementInput();
    }

    private void UpdateGhostPosition()
    {
        if (_ghostInstance == null || Camera.main == null) return;

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, 100f, _groundLayer))
        {
            _ghostInstance.transform.position = hit.point;
            _ghostInstance.transform.rotation = _ghostRotation;

            bool isValid = ValidatePlacement(hit.point);
            ApplyGhostMaterials(isValid ? _ghostMaterialValid : _ghostMaterialInvalid);
        }
    }

    private void HandleRotationInput()
    {
        if (Input.GetKeyDown(KeyCode.Q))
            _ghostRotation *= Quaternion.Euler(0, -90f, 0);
        if (Input.GetKeyDown(KeyCode.E))
            _ghostRotation *= Quaternion.Euler(0, 90f, 0);
    }

    private void HandlePlacementInput()
    {
        // Left-click: confirm placement
        if (Input.GetMouseButtonDown(0))
        {
            if (_ghostInstance != null && ValidatePlacement(_ghostInstance.transform.position))
            {
                ConfirmPlacement(_ghostInstance.transform.position, _ghostRotation);
            }
        }

        // Right-click: cancel current ghost but keep building mode
        if (Input.GetMouseButtonDown(1))
        {
            ClearGhost();
        }

        // Escape: exit placement mode completely
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            CancelPlacement();
        }
    }

    private void ConfirmPlacement(Vector3 position, Quaternion rotation)
    {
        if (_activeFurnitureItemSO == null) return;

        // Queue the shared CharacterAction
        var action = new CharacterPlaceFurnitureAction(
            _character,
            _activeFurnitureItemSO,
            position,
            rotation
        );

        if (_character.CharacterActions.ExecuteAction(action))
        {
            ClearGhost();

            // In debug mode, re-enter placement immediately for rapid testing
            if (_isDebugMode)
            {
                // Don't clear building state — stay in placement mode
                return;
            }

            // Normal mode: exit building state
            if (_character != null)
                _character.SetBuildingState(false);
        }
    }

    // ────────────────────── Validation ──────────────────────

    public bool ValidatePlacement(Vector3 position)
    {
        if (_character == null || _ghostFurnitureComponent == null) return false;

        // Range check
        float dist = Vector3.Distance(_character.transform.position, position);
        if (dist > _maxPlacementRange)
        {
            Debug.Log($"<color=red>[FurniturePlacement]</color> FAILED: Range ({dist:F1}m > {_maxPlacementRange}m)");
            return false;
        }

        // Obstacle overlap (ghost colliders are disabled, so it won't detect itself)
        BoxCollider ghostBox = _ghostInstance.GetComponent<BoxCollider>();
        if (ghostBox != null)
        {
            Vector3 center = _ghostInstance.transform.TransformPoint(ghostBox.center);
            Vector3 halfExtents = Vector3.Scale(ghostBox.size, _ghostInstance.transform.lossyScale) * 0.45f;
            Collider[] overlaps = Physics.OverlapBox(center, halfExtents, _ghostInstance.transform.rotation, _obstacleLayer);
            if (overlaps.Length > 0)
            {
                string names = string.Join(", ", System.Array.ConvertAll(overlaps, c => $"{c.gameObject.name}(layer:{c.gameObject.layer})"));
                Debug.Log($"<color=red>[FurniturePlacement]</color> FAILED: Obstacle overlap with [{names}]");
                return false;
            }
        }

        // Grid check if inside a room
        Room room = FindRoomAtPosition(position);
        if (room != null && room.Grid != null)
        {
            if (!room.Grid.CanPlaceFurniture(position, _ghostFurnitureComponent.SizeInCells))
            {
                Debug.Log($"<color=red>[FurniturePlacement]</color> FAILED: Grid check in room {room.RoomName}");
                return false;
            }
        }

        return true;
    }

    // ────────────────────── Helpers ──────────────────────

    private Room FindRoomAtPosition(Vector3 position)
    {
        Room[] allRooms = FindObjectsByType<Room>(FindObjectsSortMode.None);
        foreach (var room in allRooms)
        {
            if (room.IsPointInsideRoom(position)) return room;
        }
        return null;
    }

    private void ApplyGhostMaterials(Material mat)
    {
        if (mat == null || _ghostInstance == null) return;
        foreach (var renderer in _ghostInstance.GetComponentsInChildren<Renderer>())
        {
            renderer.sharedMaterial = mat; // Use sharedMaterial to avoid material instance leaks
        }
    }

    private void SetLayerRecursive(GameObject obj, int layer)
    {
        obj.layer = layer;
        foreach (Transform child in obj.transform)
        {
            SetLayerRecursive(child.gameObject, layer);
        }
    }
}
