using System.Collections.Generic;
using UnityEngine;
using TMPro;
using MWI.UI.Core;

namespace MWI.UI.Crafting
{
    /// <summary>
    /// Crafting window that displays the list of items craftable by a CraftingStation.
    /// </summary>
    public class CraftingWindow : ClosableWindow
    {
        [Header("Crafting Window UI")]
        [SerializeField] private TextMeshProUGUI _stationNameText;
        [SerializeField] private Transform _itemsContainer;
        [SerializeField] private GameObject _itemElementPrefab;

        private CraftingFurnitureInteractable _currentInteractable;
        private CraftingStation _currentStation;
        private Character _currentUser;
        private List<GameObject> _instantiatedItems = new List<GameObject>();

        protected override void Awake()
        {
            base.Awake(); // Ensures _closeButton gets bound
        }

        /// <summary>
        /// Opens the window and loads the station's craftable items.
        /// </summary>
        public void OpenForStation(CraftingFurnitureInteractable interactable, Character user)
        {
            if (interactable == null || interactable.CraftingStation == null) return;

            _currentInteractable = interactable;
            CraftingStation station = interactable.CraftingStation;

            _currentStation = station;
            _currentUser = user;

            // 1. Open the window
            Open();

            // 2. Update the title
            if (_stationNameText != null)
            {
                _stationNameText.text = station.FurnitureName;
            }

            // 3. Clear the previous items
            ClearItems();

            // 4. Populate the list with the station's craftable items
            if (_itemElementPrefab == null || _itemsContainer == null)
            {
                Debug.LogError("<color=red>[Crafting UI]</color> Missing prefab or container in CraftingWindow!");
                return;
            }

            foreach (ItemSO itemSO in station.CraftableItems)
            {
                if (itemSO == null) continue;

                // Instantiate the prefab as a child of the container,
                // with worldPositionStays=false to preserve the prefab's local transforms and avoid LayoutGroup bugs
                GameObject newElementGo = Instantiate(_itemElementPrefab, _itemsContainer, false);

                // --- FIX: Reset the RectTransform for LayoutGroups ---
                RectTransform rt = newElementGo.GetComponent<RectTransform>();
                if (rt != null)
                {
                    rt.localScale = Vector3.one;
                    rt.localPosition = new Vector3(rt.localPosition.x, rt.localPosition.y, 0f); // Reset Z
                    rt.localRotation = Quaternion.identity;
                }
                // -----------------------------------------------------

                _instantiatedItems.Add(newElementGo);

                // Initialize the component with the data
                if (newElementGo.TryGetComponent(out CraftingItemElement itemElement))
                {
                    itemElement.Initialize(itemSO, station, user);
                }
            }
        }

        public override void Close()
        {
            base.Close();
            ClearItems();
            _currentStation = null;
            _currentUser = null;

            if (_currentInteractable != null)
            {
                _currentInteractable.Release();
                _currentInteractable = null;
            }
        }

        private void ClearItems()
        {
            foreach (GameObject go in _instantiatedItems)
            {
                if (go != null)
                {
                    Destroy(go);
                }
            }
            _instantiatedItems.Clear();
        }
    }
}
