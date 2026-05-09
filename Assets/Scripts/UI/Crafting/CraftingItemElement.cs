using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace MWI.UI.Crafting
{
    /// <summary>
    /// Script attached to the UI prefab of a craftable item (one row/button inside the window).
    /// </summary>
    public class CraftingItemElement : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private Image _itemIcon;
        [SerializeField] private TextMeshProUGUI _itemNameText;
        [SerializeField] private Button _craftButton;

        [Header("Crafting Settings (Temp)")]
        [SerializeField] private Color _primaryColor = new Color(0,0,0,0);
        [SerializeField] private Color _secondaryColor = new Color(0,0,0,0);

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
        /// Initializes the UI element with the data of the item to craft.
        /// </summary>
        public void Initialize(ItemSO item, CraftingStation station, Character user)
        {
            _itemSO = item;
            _station = station;
            _user = user;

            if (_itemSO != null)
            {
                if (_itemNameText != null) _itemNameText.text = _itemSO.ItemName;
                if (_itemIcon != null) _itemIcon.sprite = _itemSO.Icon;

                // 1. Check whether the crafting station is compatible
                bool canCraft = _station.CanCraft(_itemSO);

                // 2. Check whether the character has the required skills
                if (canCraft && _itemSO.RequiredCraftingSkill != null)
                {
                    if (_user.CharacterSkills == null || !_user.CharacterSkills.HasRequiredSkillLevel(_itemSO.RequiredCraftingSkill, _itemSO.RequiredCraftingLevel))
                    {
                        canCraft = false;
                        _itemNameText.text += $" <color=red>(Req: {_itemSO.RequiredCraftingSkill.SkillName} Lvl {_itemSO.RequiredCraftingLevel})</color>";
                    }
                }

                // 3. (Future) Check the recipe ingredients in the inventory
                // if (canCraft)
                // {
                //     foreach (var ingredient in _itemSO.CraftingRecipe) ...
                // }

                // Enable or disable the button based on the authorisations (Station, Stats, Ingredients)
                if (_craftButton != null)
                {
                    _craftButton.interactable = canCraft;
                }
            }
        }

        /// <summary>
        /// Called when the player clicks the "Craft" button.
        /// </summary>
        private void OnCraftButtonClicked()
        {
            if (_station == null || _itemSO == null) return;

            // Trigger the craft via the centralized action system
            if (_user != null && _user.CharacterActions != null)
            {
                _user.CharacterActions.ExecuteAction(new CharacterCraftAction(_user, _itemSO, _primaryColor, _secondaryColor));
            }

            // TODO: Add a sound, a visual flash, or disable the button for the duration of the animation
        }
    }
}
