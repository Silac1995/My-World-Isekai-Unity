using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using MWI.WorldSystem;
using System.Linq;

namespace MWI.UI.Building
{
    /// <summary>
    /// UI menu that displays all building blueprints the player's character has unlocked.
    /// Selecting an entry starts the placement flow on the BuildingPlacementManager.
    /// Includes an optional "Instant Build" toggle for debug/cheat purposes.
    /// </summary>
    public class UI_BuildingPlacementMenu : UI_WindowBase
    {
        [Header("References")]
        [SerializeField] private WorldSettingsData _worldSettings;
        [SerializeField] private Transform _entryContainer;
        [SerializeField] private UI_BuildingEntry _entryPrefab;

        [Header("Instant Mode (Cheat)")]
        [SerializeField] private Toggle _instantModeToggle;
        
        public WorldSettingsData WorldSettings => _worldSettings;

        private CharacterBlueprints _blueprints;
        private BuildingPlacementManager _placementManager;
        private Character _character;

        public void Initialize(Character character)
        {
            if (character == null) return;
            _character = character;
            _blueprints = character.CharacterBlueprints;
            _placementManager = _blueprints.PlacementManager;

            // Wire up instant mode toggle
            if (_instantModeToggle != null)
            {
                _instantModeToggle.onValueChanged.RemoveAllListeners();
                _instantModeToggle.onValueChanged.AddListener(OnInstantModeChanged);
                // Sync initial state
                if (_placementManager != null)
                    _placementManager.SetInstantMode(_instantModeToggle.isOn);
            }

            // Subscribe to state changes (for auto-close)
            _character.OnBuildingStateChanged -= HandleBuildingStateChanged;
            _character.OnBuildingStateChanged += HandleBuildingStateChanged;

            RefreshMenu();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (_character != null)
            {
                _character.OnBuildingStateChanged -= HandleBuildingStateChanged;
            }
        }

        public override void OpenWindow()
        {
            base.OpenWindow();
            if (_character != null) _character.SetBuildingState(true);
        }

        public override void CloseWindow()
        {
            if (_character != null) _character.SetBuildingState(false);
            base.CloseWindow();
        }

        private void HandleBuildingStateChanged(bool active)
        {
            // If building mode was cancelled externally (e.g. combat), close the UI
            if (!active && gameObject.activeSelf)
            {
                base.CloseWindow(); // Close without triggering SetBuildingState(false) again
            }
        }

        public void RefreshMenu()
        {
            if (_blueprints == null || _worldSettings == null || _entryContainer == null || _entryPrefab == null) return;

            // Clear existing entries
            foreach (Transform child in _entryContainer)
            {
                Destroy(child.gameObject);
            }

            // Populate from unlocked blueprints
            foreach (var blueprintId in _blueprints.UnlockedBuildingIds)
            {
                var registryEntry = _worldSettings.BuildingRegistry.FirstOrDefault(e => e.PrefabId == blueprintId);
                if (string.IsNullOrEmpty(registryEntry.PrefabId)) continue;

                UI_BuildingEntry entry = Instantiate(_entryPrefab, _entryContainer);
                entry.Setup(registryEntry, OnBuildingSelected);
            }
        }

        private void OnBuildingSelected(string prefabId)
        {
            if (_placementManager != null)
            {
                _placementManager.StartPlacement(prefabId);
            }
        }

        private void OnInstantModeChanged(bool isOn)
        {
            if (_placementManager != null)
            {
                _placementManager.SetInstantMode(isOn);
            }
        }
    }
}
