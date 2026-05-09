using UnityEngine;

namespace MWI.Cinematics
{
    /// <summary>
    /// Resolves to <see cref="CinematicContext.OtherParticipant"/> — the second character
    /// passed into <see cref="Cinematics.TryPlay"/> (typically the NPC the player is
    /// talking to).
    ///
    /// Phase 1 use case: a 2-role scene (Hero + Wilfred) where Hero binds via
    /// <see cref="Selector_TriggeringPlayer"/> and Wilfred binds via this selector.
    /// Caller passes the NPC as the second argument: <c>Cinematics.TryPlay(scene, player, npc)</c>.
    ///
    /// Phase 2 will also wire this through the Talk surface: when a player Talks to an NPC,
    /// the registry calls <c>TryPlay(scene, player, npc)</c> automatically and this selector
    /// resolves correctly without any caller change.
    /// </summary>
    [CreateAssetMenu(
        fileName = "Selector_OtherParticipant",
        menuName = "MWI/Cinematics/Selectors/Other Participant")]
    public class Selector_OtherParticipant : RoleSelectorSO
    {
        public override Character Resolve(CinematicContext ctx) => ctx?.OtherParticipant;
    }
}
