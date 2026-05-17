using UnityEngine;
using UnityEngine.UI;

namespace MWI.UI.Combat
{
    /// <summary>
    /// Player-only initiative bar (Option A chrome from spec §7). Subscribes to
    /// CharacterCombat.OnInitiativeChanged (Task 2) for percent updates.
    /// Lives as a child of UI_CombatActionMenu._menuContainer — shows/hides with
    /// the action bar's parent IsInBattle gate.
    ///
    /// Not a UI_WindowBase variant — leaf prefab per CLAUDE.md rule #39.
    /// </summary>
    public class UI_CombatInitiativeBar : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("Image with Type=Filled / FillMethod=Horizontal. fillAmount drives the bar from 0 (empty) to 1 (ready to act).")]
        [SerializeField] private Image _fill;

        private Character _character;

        public void Initialize(Character character)
        {
            Unsubscribe();
            _character = character;
            if (_character?.CharacterCombat != null)
            {
                _character.CharacterCombat.OnInitiativeChanged += HandleInitiative;
            }
            // Paint initial state so a fresh subscriber doesn't show a stale 0 fill.
            HandleInitiative(0f);
        }

        private void HandleInitiative(float pct01)
        {
            if (_fill != null) _fill.fillAmount = Mathf.Clamp01(pct01);
        }

        private void Unsubscribe()
        {
            if (_character?.CharacterCombat != null)
            {
                _character.CharacterCombat.OnInitiativeChanged -= HandleInitiative;
            }
        }

        private void OnDestroy() { Unsubscribe(); }
    }
}
