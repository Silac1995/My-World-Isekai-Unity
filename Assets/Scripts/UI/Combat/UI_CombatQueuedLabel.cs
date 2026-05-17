using System;
using TMPro;
using UnityEngine;

namespace MWI.UI.Combat
{
    /// <summary>
    /// Floating pill: "▶ Queued: &lt;action&gt; → &lt;target name&gt;". Shown on
    /// OnActionIntentDecided; hidden on OnActionIntentCleared (both wired in Task 2).
    ///
    /// v1 label content is a generic "Action" placeholder because PlannedAction is a
    /// Func&lt;bool&gt; closure with no semantic identity. Future polish: enrich
    /// SetActionIntent with an ActionDescriptor (icon + name) parameter so the label
    /// can render the actual action.
    ///
    /// Not a UI_WindowBase variant — leaf prefab per CLAUDE.md rule #39.
    /// </summary>
    public class UI_CombatQueuedLabel : MonoBehaviour
    {
        [Header("Wiring")]
        [SerializeField] private TMP_Text _label;
        [Tooltip("Sub-object toggled on/off to show or hide the pill. Allows the leaf prefab to keep its parent layout slot while hiding the visual.")]
        [SerializeField] private GameObject _visualRoot;

        private Character _character;

        public void Initialize(Character character)
        {
            Unsubscribe();
            _character = character;
            if (_character?.CharacterCombat != null)
            {
                _character.CharacterCombat.OnActionIntentDecided += HandleIntentDecided;
                _character.CharacterCombat.OnActionIntentCleared += HandleIntentCleared;
            }
            Hide();
        }

        private void HandleIntentDecided(Character target, Func<bool> action)
        {
            if (_visualRoot != null) _visualRoot.SetActive(true);
            if (_label != null)
            {
                string actionName = ResolveActionName(action);
                string targetName = target != null ? target.CharacterName : "—";
                _label.text = $"▶ Queued: {actionName} → {targetName}";
            }
        }

        private void HandleIntentCleared() { Hide(); }

        private void Hide()
        {
            if (_visualRoot != null) _visualRoot.SetActive(false);
        }

        private string ResolveActionName(Func<bool> action)
        {
            // PlannedAction is a closure with no embedded semantic identity. Until
            // SetActionIntent gains an ActionDescriptor parameter (deferred polish),
            // we emit a generic label. The blue glow on the action bar button still
            // tells the player WHICH button they queued, so the missing per-action
            // text is acceptable for v1.
            return "Action";
        }

        private void Unsubscribe()
        {
            if (_character?.CharacterCombat != null)
            {
                _character.CharacterCombat.OnActionIntentDecided -= HandleIntentDecided;
                _character.CharacterCombat.OnActionIntentCleared -= HandleIntentCleared;
            }
        }

        private void OnDestroy() { Unsubscribe(); }
    }
}
