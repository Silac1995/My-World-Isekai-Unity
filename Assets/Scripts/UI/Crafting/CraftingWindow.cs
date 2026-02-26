using System.Collections.Generic;
using UnityEngine;
using TMPro;
using MWI.UI.Core;

namespace MWI.UI.Crafting
{
    /// <summary>
    /// Fenêtre de craft qui affiche la liste des objets craftables d'une CraftingStation.
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
            base.Awake(); // S'assure que le _closeButton se bind
        }

        /// <summary>
        /// Ouvre la fenêtre et charge les objets craftables de la station.
        /// </summary>
        public void OpenForStation(CraftingFurnitureInteractable interactable, Character user)
        {
            if (interactable == null || interactable.CraftingStation == null) return;

            _currentInteractable = interactable;
            CraftingStation station = interactable.CraftingStation;

            _currentStation = station;
            _currentUser = user;

            // 1. Ouvrir la fenêtre
            Open();

            // 2. Mettre à jour le titre
            if (_stationNameText != null)
            {
                _stationNameText.text = station.FurnitureName;
            }

            // 3. Nettoyer les anciens items
            ClearItems();

            // 4. Peupler la liste avec les items craftables de la station
            if (_itemElementPrefab == null || _itemsContainer == null)
            {
                Debug.LogError("<color=red>[Crafting UI]</color> Il manque le prefab ou le conteneur dans CraftingWindow !");
                return;
            }

            foreach (ItemSO itemSO in station.CraftableItems)
            {
                if (itemSO == null) continue;

                // Instancie le prefab en tant qu'enfant du conteneur, 
                // mais avec false pour garder les propriétés locales du prefab et éviter les bugs de LayoutGroup
                GameObject newElementGo = Instantiate(_itemElementPrefab, _itemsContainer, false);
                
                // --- FIX : Réinitialiser le RectTransform pour les LayoutGroups ---
                RectTransform rt = newElementGo.GetComponent<RectTransform>();
                if (rt != null)
                {
                    rt.localScale = Vector3.one;
                    rt.localPosition = new Vector3(rt.localPosition.x, rt.localPosition.y, 0f); // Reset Z
                    rt.localRotation = Quaternion.identity;
                }
                // ------------------------------------------------------------------

                _instantiatedItems.Add(newElementGo);

                // Initialise le composant avec les données
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
