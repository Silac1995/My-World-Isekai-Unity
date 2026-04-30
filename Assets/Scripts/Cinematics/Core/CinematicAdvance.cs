using UnityEngine;

// Inside namespace MWI.Cinematics, the bare `Time` symbol resolves to the sibling
// MWI.Time namespace before reaching UnityEngine.Time. Aliasing avoids fully-qualifying.
using UTime = UnityEngine.Time;

namespace MWI.Cinematics
{
    /// <summary>
    /// Phase 1 advance-press bridge. <see cref="PlayerController"/> calls
    /// <see cref="NotifyAdvanceRequested"/> when a bound-as-actor player presses
    /// Space / Left-Click. Steps poll <see cref="WasAdvanceRequestedThisFrame"/> to
    /// decide whether to advance early.
    ///
    /// <para>Mirrors <see cref="DialogueManager"/>'s advance semantics:</para>
    /// <list type="bullet">
    ///   <item>If at least one player is bound as a cinematic actor → step waits for
    ///     a press to advance.</item>
    ///   <item>If only NPCs are bound → step auto-advances 1.5s after typing finishes
    ///     (the existing Phase 1 dwell).</item>
    /// </list>
    ///
    /// <para>
    /// Phase 2 replaces this with the full <c>AllMustPress</c> protocol: ServerRpc
    /// from each participating client, server-side per-line tally with grace timer,
    /// disconnect auto-yield. The single-frame static-flag approach here is enough
    /// for Phase 1's server-only / single-player demo.
    /// </para>
    /// </summary>
    public static class CinematicAdvance
    {
        // Frame number when the most recent advance-press happened. Steps poll
        // WasAdvanceRequestedThisFrame() during their tick — guarantees the press
        // is consumed at most once per frame regardless of how many steps tick.
        private static int s_lastAdvanceFrame = -1;

        /// <summary>
        /// Called by <see cref="PlayerController"/> when a bound-as-actor player
        /// presses the advance input (Space or Left-Click). Stamps the current
        /// frame so the next step poll picks it up.
        /// </summary>
        public static void NotifyAdvanceRequested()
        {
            s_lastAdvanceFrame = UTime.frameCount;
        }

        /// <summary>
        /// Returns true if an advance-press was registered earlier in the same frame.
        /// Idempotent within the frame — safe to call multiple times. Resets
        /// implicitly when the frame counter advances.
        /// </summary>
        public static bool WasAdvanceRequestedThisFrame()
        {
            return s_lastAdvanceFrame == UTime.frameCount;
        }
    }
}
