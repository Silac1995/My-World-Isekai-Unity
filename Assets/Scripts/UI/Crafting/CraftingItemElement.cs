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
        /// Initialise l'élément UI avec les données de l'item à crafter.
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

                // 1. Vérifie si la station de craft est compatible 
                bool canCraft = _station.CanCraft(_itemSO);

                // 2. Vérifie si le personnage possède les compétences nécessaires
                if (canCraft && _itemSO.RequiredCraftingSkill != null)
                {
                    if (_user.CharacterSkills == null || !_user.CharacterSkills.HasRequiredSkillLevel(_itemSO.RequiredCraftingSkill, _itemSO.RequiredCraftingLevel))
                    {
                        canCraft = false;
                        _itemNameText.text += $" <color=red>(Req: {_itemSO.RequiredCraftingSkill.SkillName} Lvl {_itemSO.RequiredCraftingLevel})</color>";
                    }
                }

                // 3. (Futur) Vérifie les ingrédients de la recette dans l'inventaire
                // if (canCraft)
                // {
                //     foreach (var ingredient in _itemSO.CraftingRecipe) ...
                // }

                // Active ou désactive le bouton en fonction des autorisations (Station, Stats, Ingrédients)
                if (_craftButton != null)
                {
                    _craftButton.interactable = canCraft;
                }
            }
        }

        /// <summary>
        /// Appelé quand le joueur clique sur le bouton "Craft".
        /// </summary>
        private void OnCraftButtonClicked()
        {
            if (_station == null || _itemSO == null) return;

            // Déclenche le craft via le système d'actions centralisé
            if (_user != null && _user.CharacterActions != null)
            {
                _user.CharacterActions.ExecuteAction(new CharacterCraftAction(_user, _station, _itemSO, _primaryColor, _secondaryColor));
            }

            // TODO : Ajouter un son, un flash visuel, ou désactiver le bouton le temps de l'animation
        }
    }
}
