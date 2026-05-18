using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using MWI.WorldSystem;

namespace MWI.UI.CityManagement
{
    /// <summary>
    /// Row leaf inside <see cref="UI_PlaceBuildingTab"/>'s scroll list. Displays a Civic
    /// blueprint name + footprint + Place button. Click fires the row's
    /// <see cref="Action"/> callback so the parent tab can route to the AB's placement
    /// RPC. Initialize-callback decoupling per rule #39 row-prefab pattern.
    ///
    /// Plan 4c Task 7.
    /// </summary>
    public class UI_CivicBlueprintRow : MonoBehaviour
    {
        [SerializeField] private TMP_Text _nameLabel;
        [SerializeField] private TMP_Text _footprintLabel;
        [SerializeField] private Button _placeButton;

        private BuildingSO _blueprint;
        private Action<BuildingSO> _onPlace;

        public void Initialize(BuildingSO blueprint, Action<BuildingSO> onPlace)
        {
            _blueprint = blueprint;
            _onPlace = onPlace;

            if (_nameLabel != null)
                _nameLabel.text = blueprint != null
                    ? (string.IsNullOrEmpty(blueprint.BuildingName) ? blueprint.name : blueprint.BuildingName)
                    : "<null>";

            if (_footprintLabel != null && blueprint != null)
                _footprintLabel.text = $"{blueprint.GridFootprintCells.x}×{blueprint.GridFootprintCells.y}";

            if (_placeButton != null)
            {
                _placeButton.onClick.RemoveAllListeners();
                _placeButton.onClick.AddListener(OnPlaceClicked);
            }
        }

        private void OnPlaceClicked()
        {
            if (_blueprint != null) _onPlace?.Invoke(_blueprint);
        }

        private void OnDestroy()
        {
            // Re-init safety per rule #16 + rule #39 row-prefab pattern — explicit
            // listener cleanup so re-binding doesn't double-fire.
            if (_placeButton != null) _placeButton.onClick.RemoveAllListeners();
        }
    }
}
