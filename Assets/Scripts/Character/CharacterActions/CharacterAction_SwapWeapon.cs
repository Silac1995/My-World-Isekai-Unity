using UnityEngine;

/// <summary>
/// Continuous server-side action that swaps the character to the next carried weapon.
/// Hardcoded 0.5s duration represents the stow-and-draw window (anti-spam + visual cue).
///
/// Lifecycle:
///   OnStart    — validates the character has >= 2 carried weapons (silently aborts if not).
///   OnTick     — accumulates elapsed time; on completion calls SwapToNextWeapon().
///   OnCancel   — no-op (no partial swap state to clean up; player retains original weapon).
///
/// Network (rule #18/#19b):
///   Server-only. CharacterEquipment.SwapToNextWeapon writes NetworkVariables so
///   all clients see the updated weapon state (_activeWeaponIndexNet, RecomputeActiveWeaponSentinel).
///
/// NPC parity (rule #22):
///   No IsOwner gate — NPC AI queues CharacterAction_SwapWeapon(npc) identically.
/// </summary>
public sealed class CharacterAction_SwapWeapon : CharacterAction_Continuous
{
    private const float SWAP_DURATION = 0.5f;

    private float _elapsed;
    private bool _validAtStart;

    public CharacterAction_SwapWeapon(Character character)
        : base(character)
    {
        TickIntervalSeconds = 0.1f; // 10 Hz — smooth Progress reporting + sub-0.1s completion accuracy
    }

    /// <summary>0..1 progress for HUD swap bars.</summary>
    public override float Progress => Mathf.Clamp01(_elapsed / SWAP_DURATION);

    /// <summary>Let future weapon-specific animation play instead of the generic action trigger.</summary>
    public override bool ShouldPlayGenericActionAnimation => false;

    public override void OnStart()
    {
        var equipment = character?.CharacterEquipment;
        var weapons = equipment?.GetInventory()?.GetWeaponInstances();
        _validAtStart = weapons != null && weapons.Count >= 2;

        if (!_validAtStart)
        {
            // Guard: no inventory (bag-less character) or < 2 weapons — abort immediately.
            Finish();
            return;
        }

        _elapsed = 0f;
        // Visual hook placeholder — future polish: trigger a "stow" animation here.
    }

    /// <summary>
    /// Accumulates time. Returns true when the swap duration has elapsed,
    /// triggering the runner to call Finish() which fires OnActionFinished.
    /// </summary>
    public override bool OnTick()
    {
        if (!_validAtStart) return true;

        _elapsed += TickIntervalSeconds;
        if (_elapsed < SWAP_DURATION) return false;

        // Duration elapsed — execute the swap. SwapToNextWeapon is server-only and
        // writes _activeWeaponIndexNet + calls RecomputeActiveWeaponSentinel to
        // replicate the new weapon state to all clients (rule #19b).
        character?.CharacterEquipment?.SwapToNextWeapon();
        return true; // signal runner to call Finish()
    }

    /// <summary>
    /// Called by CharacterActions when the action is interrupted externally
    /// (knockback, death, another queued action). No partial swap state exists —
    /// the character still wields the original weapon and can retry.
    /// </summary>
    public override void OnCancel()
    {
        // No-op — original weapon state is unchanged; no replication needed.
    }
}
