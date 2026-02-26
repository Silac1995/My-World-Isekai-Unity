using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace MWI.UI.Crafting
{
    /// <summary>
    /// Script attaché au prefab UI d'un item craftable (une ligne/un bouton dans la fenêtre).
    /// </summary>
    public class CraftingItemElement : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private Image _itemIcon;
        [SerializeField] private TextMeshProUGUI _itemNameText;
        [SerializeField] private Button _craftButton;

        private ItemSO _itemSO;
        private CraftingStation _station;
        private Character _user;

        private void Awake()
        {
            if (_craftButton != null)
            {
                _craftButton.onClick.AddListener(OnCraftButtonClicked);
            }
        }

        private void OnDestroy()
        {
            if (_craftButton != null)
            {
                _craftButton.onClick.RemoveListener(OnCraftButtonClicked);
            }
        }

        /// <summary>
        /// Initialise l'élément UI avec les données de l'item à crafter.
        /// </summary>
        public void Initialize(ItemSO item, CraftingStation station, Character user)
        {
            _itemSO = item;
            _station = station;
            _user = user;

            // Mise à jour visuelle
            if (_itemIcon != null && item.Icon != null)
            {
                _itemIcon.sprite = item.Icon;
                _itemIcon.enabled = true;
            }

            if (_itemNameText != null)
            {
                _itemNameText.text = item.ItemName;
            }
        }

        /// <summary>
        /// Appelé quand le joueur clique sur le bouton "Craft".
        /// </summary>
        private void OnCraftButtonClicked()
        {
            if (_station == null || _itemSO == null) return;

            // Déclenche le craft sur la station
            _station.Craft(_itemSO);

            // TODO : Ajouter un son, un flash visuel, ou désactiver le bouton le temps de l'animation
        }
    }
}
