using UnityEngine;

namespace MWI.Cinematics
{
    /// <summary>
    /// Resolves to a specific <see cref="Character"/> by its persistent
    /// <see cref="Character.CharacterId"/> (the UUID stored in the character's profile
    /// JSON at <c>Profiles/{characterGuid}.json</c>).
    ///
    /// Use this for **predefined / main characters** with stable UUIDs that you want to
    /// reference unambiguously in a cinematic script — e.g., a story-critical NPC like
    /// "the prophet of the eastern shrine" who must be a specific instance, not any
    /// random NPC of the same archetype or any character that happens to share a name.
    ///
    /// <para>
    /// Trade-off vs <see cref="Selector_CharacterByName"/>: UUIDs are stable across
    /// renames / localization / duplicate-name characters, but the authoring UX is
    /// worse (designer types a 32-char hex string). Phase 4's Cinematic Scene Editor
    /// will add a UUID picker that shows known main characters in a dropdown; until
    /// then, copy the UUID from the character's profile filename or the runtime
    /// inspector dump.
    /// </para>
    /// <para>
    /// Resolution uses <see cref="Character.FindByUUID(string)"/> — the project's
    /// canonical lookup. Returns null if the character isn't currently spawned in the
    /// active scene (despawned / hibernated / not yet loaded for this map). Mark the
    /// role <see cref="RoleSlot.IsOptional"/> if the cinematic should still play when
    /// this character is offscreen.
    /// </para>
    /// </summary>
    [CreateAssetMenu(
        fileName = "Selector_CharacterById",
        menuName = "MWI/Cinematics/Selectors/Character By Id (UUID)")]
    public class Selector_CharacterById : RoleSelectorSO
    {
        [Tooltip("The Character.CharacterId (UUID) to look up. Find it in the character's profile filename: Profiles/{this-uuid}.json. Or read it from the Character component at runtime.")]
        [SerializeField] private string _characterId;

        [Tooltip("Optional designer-facing label so this asset is greppable in the Project window. Has no runtime effect.")]
        [SerializeField] private string _displayHint;

        public string CharacterId => _characterId;
        public string DisplayHint => _displayHint;

        public override Character Resolve(CinematicContext ctx)
        {
            if (string.IsNullOrEmpty(_characterId))
            {
                Debug.LogWarning($"<color=yellow>[Cinematic]</color> Selector_CharacterById ('{name}'): _characterId is empty.");
                return null;
            }

            var c = Character.FindByUUID(_characterId);
            if (c == null)
            {
                Debug.LogWarning($"<color=yellow>[Cinematic]</color> Selector_CharacterById ('{name}'): no Character with CharacterId='{_characterId}' is currently spawned. (Character may be hibernated / on another map / not yet loaded.)");
            }
            return c;
        }
    }
}
