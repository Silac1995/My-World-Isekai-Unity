using UnityEngine;

namespace MWI.Cinematics
{
    /// <summary>
    /// Resolves to the first <see cref="Character"/> in the active scene whose
    /// <see cref="Character.CharacterName"/> matches <c>_characterName</c>.
    ///
    /// Phase 1 quick-author selector — analogous to dragging a Character into a list slot
    /// in the legacy <c>DialogueManager._testParticipants</c> array, but resilient to
    /// scene/prefab GUID drift because we look up by display name. Designer types
    /// "Wilfred" into the SO and authoring works without a runtime instance to drag.
    ///
    /// Trade-off: ambiguous if multiple characters share a name (returns the first found).
    /// For named NPCs in your authored scenes this is fine; for generic NPCs prefer
    /// <see cref="Selector_OtherParticipant"/> (Phase 1) or
    /// <see cref="Selector_NearestArchetype"/> / <see cref="Selector_RandomInRadius"/>
    /// (Phase 2 — archetype-based selectors).
    ///
    /// Performance: <see cref="Object.FindObjectsByType"/> is O(n) over the scene roots.
    /// Acceptable for once-per-scene-start role binding; do not call from a hot path.
    /// </summary>
    [CreateAssetMenu(
        fileName = "Selector_CharacterByName",
        menuName = "MWI/Cinematics/Selectors/Character By Name")]
    public class Selector_CharacterByName : RoleSelectorSO
    {
        [Tooltip("The Character.CharacterName to match. Case-sensitive.")]
        [SerializeField] private string _characterName;

        public string CharacterName => _characterName;

        public override Character Resolve(CinematicContext ctx)
        {
            if (string.IsNullOrEmpty(_characterName)) return null;

            // FindObjectsByType returns only enabled root objects by default, which is what
            // we want — disabled / hibernating characters can't participate in a cinematic.
            var all = Object.FindObjectsByType<Character>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].CharacterName == _characterName)
                    return all[i];
            }

            Debug.LogWarning($"<color=yellow>[Cinematic]</color> Selector_CharacterByName: no Character with CharacterName='{_characterName}' found in active scene.");
            return null;
        }
    }
}
