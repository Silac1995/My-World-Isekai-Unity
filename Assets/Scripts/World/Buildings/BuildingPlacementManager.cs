using UnityEngine;
using Unity.Netcode;
using MWI.WorldSystem;

namespace MWI.WorldSystem
{
    /// <summary>
    /// Handles the building placement lifecycle for any Character (Player or NPC).
    /// For Player: drives the ghost visual, mouse-based positioning, and click-to-place.
    /// Validation (range + obstacle) is shared so NPCs can reuse the same rules.
    /// Server-authoritative: the actual building is spawned via ServerRpc.
    /// </summary>
    public class BuildingPlacementManager : CharacterSystem
    {
        [Header("Settings")]
        [SerializeField] private LayerMask _groundLayer;
        [SerializeField] private LayerMask _obstacleLayer;
        [SerializeField] private Material _ghostMaterialValid;
        [SerializeField] private Material _ghostMaterialInvalid;

        [SerializeField] private WorldSettingsData _settings;
        private GameObject _ghostInstance;
        private string _activePrefabId;
        private Building _ghostBuildingComponent;
        private bool _isPlacementActive;
        private bool _isInstantMode;
        private Character _character;

        public bool IsPlacementActive => _isPlacementActive;

        // ────────────────────── Initialization ──────────────────────

        public void Initialize(Character character)
        {
            _character = character;

            EnsureSettings();
        }

        public void SetSettings(WorldSettingsData settings)
        {
            _settings = settings;
        }

        /// <summary>
        /// Toggles instant build mode. When enabled, placed buildings skip construction requirements.
        /// </summary>
        public void SetInstantMode(bool instant)
        {
            _isInstantMode = instant;
        }

        // ────────────────────── Placement Lifecycle ──────────────────────

        public void StartPlacement(string prefabId)
        {
            // We only clear the ghost/selection to stay in building mode (keep UI open)
            ClearGhost();
            EnsureSettings();

            if (_settings == null)
            {
                Debug.LogError("[BuildingPlacementManager] WorldSettingsData could not be loaded.");
                return;
            }

            var entry = _settings.BuildingRegistry.Find(e => e.PrefabId == prefabId);
            if (entry.BuildingPrefab == null)
            {
                Debug.LogWarning($"[BuildingPlacementManager] No prefab found for PrefabId '{prefabId}'.");
                return;
            }

            _activePrefabId = prefabId;
            _ghostInstance = Instantiate(entry.BuildingPrefab);
            _ghostBuildingComponent = _ghostInstance.GetComponent<Building>();

            // Disable physics/logic on ghost — it's purely visual
            if (_ghostInstance.TryGetComponent(out Rigidbody rb)) rb.isKinematic = true;
            foreach (var col in _ghostInstance.GetComponentsInChildren<Collider>()) col.isTrigger = true;

            // Disable any NetworkObject on the ghost to prevent network errors
            if (_ghostInstance.TryGetComponent(out NetworkObject netObj)) netObj.enabled = false;

            _ghostInstance.name = "PlacementGhost_" + prefabId;
            _isPlacementActive = true;
        
            // Ensure character state is set (in case it was started without the UI, though unlikely now)
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
            _activePrefabId = string.Empty;
            _ghostBuildingComponent = null;
        }

        protected override void HandleIncapacitated(Character character)
        {
            base.HandleIncapacitated(character);
            CancelPlacement();
        }

        protected override void HandleCombatStateChanged(bool inCombat)
        {
            base.HandleCombatStateChanged(inCombat);
            if (inCombat)
            {
                CancelPlacement();
            }
        }

        // ────────────────────── Frame Update (Player only) ──────────────────────

        private void Update()
        {
            if (!_isPlacementActive || !IsOwner) return;

            UpdateGhostPosition();
            HandleInput();
        }

        private void UpdateGhostPosition()
        {
            if (_ghostInstance == null || Camera.main == null) return;

            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 100f, _groundLayer))
            {
                _ghostInstance.transform.position = hit.point;

                bool isValid = ValidatePlacement(hit.point);
                ApplyGhostMaterials(isValid ? _ghostMaterialValid : _ghostMaterialInvalid);
            }
        }

        private void HandleInput()
        {
            // Left-Click: Confirm placement
            if (Input.GetMouseButtonDown(0))
            {
                if (_ghostInstance != null && ValidatePlacement(_ghostInstance.transform.position))
                {
                    RequestPlacementServerRpc(
                        _activePrefabId,
                        _ghostInstance.transform.position,
                        _ghostInstance.transform.rotation,
                        _isInstantMode
                    );
                    // We call ClearGhost instead of CancelPlacement to keep the UI open
                    ClearGhost();
                }
            }

            // Right-Click: Cancel current selection but keep building mode active
            if (Input.GetMouseButtonDown(1))
            {
                ClearGhost();
            }

            // Escape: Exit building mode completely
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                CancelPlacement();
            }
        }

        // ────────────────────── Validation (shared with NPC systems) ──────────────────────

        /// <summary>
        /// Validates whether a building can be placed at the given position.
        /// Checks range from the owning character and obstacle overlap.
        /// This is intentionally public so NPC AI can call it directly.
        /// </summary>
        public bool ValidatePlacement(Vector3 position)
        {
            if (_character == null || _character.CharacterBlueprints == null) return false;

            // 1. Range check — uses MaxPlacementRange from CharacterBlueprints
            float dist = Vector3.Distance(_character.transform.position, position);
            if (dist > _character.CharacterBlueprints.MaxPlacementRange) return false;

            // 2. Obstacle overlap check using the building's zone collider
            if (_ghostBuildingComponent != null && _ghostBuildingComponent.BuildingZone is BoxCollider box)
            {
                Vector3 center = _ghostInstance.transform.TransformPoint(box.center);
                // Slightly smaller than real size to avoid false positives from grazing edges
                Vector3 halfExtents = Vector3.Scale(box.size, _ghostInstance.transform.lossyScale) * 0.45f;

                Collider[] overlaps = Physics.OverlapBox(center, halfExtents, _ghostInstance.transform.rotation, _obstacleLayer);
                if (overlaps.Length > 0) return false;
            }

            return true;
        }

        // ────────────────────── Visual Helpers ──────────────────────

        private void ApplyGhostMaterials(Material mat)
        {
            if (mat == null || _ghostInstance == null) return;
            foreach (var renderer in _ghostInstance.GetComponentsInChildren<Renderer>())
            {
                renderer.material = mat;
            }
        }

        // ────────────────────── Server-Authoritative Spawn ──────────────────────

        [ServerRpc]
        private void RequestPlacementServerRpc(string prefabId, Vector3 position, Quaternion rotation, bool instant)
        {
            EnsureSettings();
            if (_settings == null) return;

            var entry = _settings.BuildingRegistry.Find(e => e.PrefabId == prefabId);
            if (entry.BuildingPrefab == null) return;

            GameObject buildingObj = Instantiate(entry.BuildingPrefab, position, rotation);

            // Spawn on the network
            var netObj = buildingObj.GetComponent<NetworkObject>();
            if (netObj != null)
            {
                netObj.Spawn();
            }
            else
            {
                Debug.LogError($"[BuildingPlacementManager] Prefab for '{prefabId}' is missing a NetworkObject component! It will only exist on the Server/Host.");
                // Destroy to prevent desync where host has a building that clients don't see
                Destroy(buildingObj);
                return;
            }

            // If instant mode, skip construction requirements
            if (instant)
            {
                var building = buildingObj.GetComponent<Building>();
                if (building != null)
                {
                    building.BuildInstantly();
                }
            }
        }

        // ────────────────────── Utilities ──────────────────────

        private void EnsureSettings()
        {
            if (_settings == null)
            {
                _settings = Resources.Load<WorldSettingsData>("Data/World/WorldSettingsData");
            }
        }

        private void OnDestroy()
        {
            CancelPlacement();
        }
    }
}
