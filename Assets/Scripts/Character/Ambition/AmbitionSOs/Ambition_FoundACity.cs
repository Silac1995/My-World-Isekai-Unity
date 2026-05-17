using UnityEngine;

namespace MWI.Ambition
{
    /// <summary>
    /// Authored Ambition: "Found a City". Drives the full founding flow:
    /// CreateCommunity → BuildCapital (Plan 4) → PromoteCamp → PromoteVillage →
    /// PromoteTown → PromoteCity → PromoteKingdom → PromoteEmpire.
    /// <para>
    /// No parameter slots — the ambition operates on the actor's own community.
    /// Default <see cref="AmbitionSO.ValidateParameters"/> is inherited unchanged.
    /// </para>
    /// <para>
    /// Plan 3 ships only this typed shell. Plan 4 creates the <c>Ambition_FoundACity.asset</c>
    /// instance with the full quest chain wired up (adds <c>Quest_BuildCapital</c> +
    /// the Promote-tier quest assets).
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "MWI/Ambition/Ambition_FoundACity", fileName = "Ambition_FoundACity")]
    public class Ambition_FoundACity : AmbitionSO
    {
        // Empty body: relies entirely on AmbitionSO's base authoring + ValidateParameters.
        // Plan 4 will populate the Quests list on the .asset instance, not in code.
    }
}
