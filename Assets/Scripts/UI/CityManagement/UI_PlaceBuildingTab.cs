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
            // Follow-up: hand off to PlayerController.BeginCityPlacementMode(blueprint).
            // For v1 we log the intent — designers/Kevin can drive placement directly via
            // AdministrativeBuilding.PlaceCityBlueprintServerRpc(blueprint.PrefabId, cell, ...)
            // through the debug console.
            Debug.Log($"<color=cyan>[UI_PlaceBuildingTab]</color> Place clicked for '{blueprint.BuildingName}'. Cursor-mode hand-off TODO (Plan 4c follow-up).");
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
