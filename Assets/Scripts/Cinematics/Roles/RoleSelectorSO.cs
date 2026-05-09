using UnityEngine;

namespace MWI.Cinematics
{
    /// <summary>
    /// Polymorphic SO that resolves an abstract role to a live Character at scene start.
    /// New selection rules drop in as new subclasses — no central switch.
    /// </summary>
    public abstract class RoleSelectorSO : ScriptableObject
    {
        /// <summary>
        /// Returns the Character that fills this role for the given context.
        /// Returns null if no character could be bound (caller decides hard-fail vs skip).
        /// </summary>
        public abstract Character Resolve(CinematicContext ctx);
    }
}
