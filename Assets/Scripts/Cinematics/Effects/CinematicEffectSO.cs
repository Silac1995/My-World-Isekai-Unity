using UnityEngine;

namespace MWI.Cinematics
{
    /// <summary>
    /// Polymorphic SO that fires an in-timeline effect (VFX, SFX, give-quest, etc.).
    /// Distinct from <see cref="CinematicTriggerSurfaceSO"/> (Phase 2): surfaces *start* a
    /// cinematic; effects run *inside* one. The payload of <see cref="TriggerStep"/>.
    /// </summary>
    public abstract class CinematicEffectSO : ScriptableObject
    {
        /// <summary>
        /// Apply the effect server-side. Implementations may broadcast ClientRpcs
        /// to participating clients via the existing systems they delegate to
        /// (CharacterQuestLog, CharacterSpeech, etc.).
        /// </summary>
        public abstract void Apply(CinematicContext ctx);
    }
}
