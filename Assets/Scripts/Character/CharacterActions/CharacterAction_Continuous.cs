using UnityEngine;

/// <summary>
/// Sibling of <see cref="CharacterAction"/> for actions that are condition-terminated
/// rather than timer-terminated.
///
/// The runner ticks <see cref="OnTick"/> at <see cref="TickIntervalSeconds"/>; when
/// OnTick returns true, the action finishes. <see cref="CharacterAction.OnApplyEffect"/>
/// is sealed to a no-op — continuous actions implement everything in <see cref="OnTick"/>.
///
/// Default <see cref="CharacterAction.AllowsMovementDuringAction"/> = false (inherited),
/// so any movement intent (player WASD, NPC re-route) cancels via the existing
/// <c>CharacterGameController</c> path.
///
/// Authored 2026-05-06 — see docs/superpowers/specs/2026-05-06-building-construction-loop-design.md.
/// </summary>
public abstract class CharacterAction_Continuous : CharacterAction
{
    /// <summary>Server tick cadence. Default 1 Hz; subclasses may override.</summary>
    public float TickIntervalSeconds { get; protected set; } = 1f;

    /// <summary>
    /// Server-ticked. Return true when the terminating condition has been met
    /// (the runner will then call <see cref="CharacterAction.Finish"/>).
    /// </summary>
    public abstract bool OnTick();

    protected CharacterAction_Continuous(Character character) : base(character, duration: 0f) { }

    /// <summary>
    /// Continuous actions never use a fixed duration; OnTick replaces OnApplyEffect.
    /// Sealed to prevent accidental subclass overrides re-introducing duration semantics.
    /// </summary>
    public sealed override void OnApplyEffect() { /* no-op */ }
}
