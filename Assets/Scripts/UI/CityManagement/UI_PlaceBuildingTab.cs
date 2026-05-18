using System.Collections.Generic;
using TMPro;
using UnityEngine;
using MWI.WorldSystem;

namespace MWI.UI.CityManagement
{
    /// <summary>
    /// PlaceBuildingTab — lists the current tier's UnlockedBlueprints
    /// (<see cref="MWI.WorldSystem.CommunityTierRequirementsSO.UnlockedBlueprints"/>).
    /// Each row's Place button kicks off the RTS-style placement cursor for the chosen
    /// blueprint.
    ///
    /// v1 row-click handler logs the intent — the cursor-mode hand-off into
    /// <see cref="PlayerController"/> is a separate follow-up (see Plan 4c plan doc
    /// Risk R6 + Task 7 step 5). Until that lands, designers can drive placement via
    /// the AdministrativeBuilding.PlaceCityBlueprintServerRpc directly (dev tool).
    ///
    /// Plan 4c Task 7.
    /// </summary>
    public class UI_PlaceBuildingTab : MonoBehaviour
    {
        [Header("Wiring")]
        [SerializeField] private RectTransform _rowContainer;
        [SerializeField] private UI_CivicBlueprintRow _rowPrefab;
        [SerializeField] private TMP_Text _emptyStateLabel;

        private AdministrativeBuilding _ab;
        private readonly List<UI_CivicBlueprintRow> _rows = new List<UI_CivicBlueprintRow>();

        public void Initialize(AdministrativeBuilding ab)
        {
            _ab = ab;
        }

        public void RefreshFromAB()
        {
            ClearRows();
            if (_ab == null || _ab.OwnerCommunity == null) return;

            // Prefer the SO-ref path (supports designer-authored off-enum tiers); fall
            // back to the legacy enum lookup so old saves without a tier id still work.
            var tierReq = _ab.OwnerCommunity.CurrentTier
                       ?? MWI.WorldSystem.CommunityTierRegistry.Get(_ab.OwnerCommunity.level);
            var unlocked = tierReq != null ? tierReq.UnlockedBlueprints : null;
            string tierLabel = tierReq != null ? tierReq.DisplayName : _ab.OwnerCommunity.level.ToString();

            if (unlocked == null || unlocked.Count == 0)
            {
                if (_emptyStateLabel != null)
                    _emptyStateLabel.text = $"No civic blueprints unlocked at {tierLabel}.";
                if (_emptyStateLabel != null) _emptyStateLabel.gameObject.SetActive(true);
                return;
            }

            if (_emptyStateLabel != null) _emptyStateLabel.gameObject.SetActive(false);

            if (_rowPrefab == null || _rowContainer == null) return;
            for (int i = 0; i < unlocked.Count; i++)
            {
                var bp = unlocked[i];
                if (bp == null) continue;
                var row = Instantiate(_rowPrefab, _rowContainer);
                row.Initialize(bp, OnPlaceClicked);
                _rows.Add(row);
            }
        }

        private void OnPlaceClicked(BuildingSO blueprint)
        {
            if (blueprint == null || _ab == null) return;

            // Find the local player's BuildingPlacementManager. Same lookup pattern as
            // UI_CharacterMapTrackerOverlay.FindLocalPlayerMapTracker.
            var localClient = Unity.Netcode.NetworkManager.Singleton != null
                ? Unity.Netcode.NetworkManager.Singleton.LocalClient
                : null;
            var playerObj = localClient != null ? localClient.PlayerObject : null;
            if (playerObj == null)
            {
                Debug.LogWarning("<color=orange>[UI_PlaceBuildingTab]</color> Cannot enter civic placement mode — no LocalClient.PlayerObject.");
                return;
            }

            var bpm = playerObj.GetComponentInChildren<BuildingPlacementManager>(includeInactive: false);
            if (bpm == null)
            {
                Debug.LogWarning("<color=orange>[UI_PlaceBuildingTab]</color> Cannot enter civic placement mode — local player has no BuildingPlacementManager subsystem.");
                return;
            }

            // Close the city management window so the player can see the map. The window
            // re-opens via tap-E on the CityManagementFurniture after placement completes
            // (or is cancelled).
            PlayerUI.Instance?.CloseCityManagementWindow();

            bpm.StartCivicPlacement(blueprint, _ab);

            if (NPCDebug.VerboseJobs)
                Debug.Log($"<color=cyan>[UI_PlaceBuildingTab]</color> Civic placement mode entered for '{blueprint.BuildingName}' (AB={_ab.BuildingName}).");
        }

        private void ClearRows()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i] != null) Destroy(_rows[i].gameObject);
            }
            _rows.Clear();
        }

        private void OnDisable()
        {
            ClearRows();
        }
    }
}
