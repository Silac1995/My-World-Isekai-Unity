using UnityEngine;

namespace MWI.Cinematics
{
    [CreateAssetMenu(
        fileName = "Selector_TriggeringPlayer",
        menuName = "MWI/Cinematics/Selectors/Triggering Player")]
    public class Selector_TriggeringPlayer : RoleSelectorSO
    {
        public override Character Resolve(CinematicContext ctx) => ctx.TriggeringPlayer;
    }
}
