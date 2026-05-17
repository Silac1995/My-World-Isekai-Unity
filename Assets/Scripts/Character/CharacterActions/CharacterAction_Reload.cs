using UnityEngine;

/// <summary>
/// Continuous server-side action that reloads a magazine weapon over time.
/// Duration = MagazineRangedCombatStyleSO.ReloadTime (2s default).
///
/// Lifecycle:
///   OnStart    — validates state, sets _isReloading flag on the weapon instance.
///   OnTick     — accumulates elapsed time; returns true when elapsed >= duration.
///   OnCancel   — called by CharacterActions on interrupt (knockback, death, weapon swap);
///               resets IsReloading without changing ammo via CancelReload().
///
/// Network (rule #18/#19b):
///   Runs server-side only (CharacterActions.ExecuteAction is server-authoritative).
///   CharacterEquipment.RecomputeActiveWeaponSentinel() writes NetworkVariables so
///   all clients (including late-joiners) see the updated weapon state.
///
/// NPC parity (rule #22):
///   No IsOwner gate — NPC AI queues CharacterAction_Reload(npc, mag) identically.
/// </summary>
public sealed class CharacterAction_Reload : CharacterAction_Continuous
{
    private readonly MagazineWeaponInstance _mag;
    private readonly float _duration;
    private float _elapsed;

    public CharacterAction_Reload(Character character, MagazineWeaponInstance mag)
        : base(character)
    {
        _mag = mag;
        _duration = ResolveDuration(character, mag);
        // Tick at 10 Hz for smooth Progress reporting and sub-second completion accuracy.
        TickIntervalSeconds = 0.1f;
    }

    private static float ResolveDuration(Character character, MagazineWeaponInstance mag)
    {
        var style = character?.CharacterCombat?.CurrentCombatStyleExpertise?.Style;
        if (style is MagazineRangedCombatStyleSO magStyle)
            return magStyle.ReloadTime;
        // Defensive fallback — should not occur for a magazine weapon in normal play.
        return 2f;
    }

    /// <summary>0..1 progress for HUD reload bars.</summary>
    public override float Progress => _duration > 0f ? Mathf.Clamp01(_elapsed / _duration) : 1f;

    /// <summary>Let the weapon-specific animation play instead of the generic action trigger.</summary>
    public override bool ShouldPlayGenericActionAnimation => false;

    public override void OnStart()
    {
        if (_mag == null) { Finish(); return; }
        if (_mag.IsReloading) { Finish(); return; }
        if (_mag.CurrentAmmo >= _mag.MagazineSize) { Finish(); return; }

        _elapsed = 0f;
        _mag.StartReload();
        // Replicate IsReloading=true to all clients via NetworkVariables (rule #19b).
        character?.CharacterEquipment?.RecomputeActiveWeaponSentinel();
    }

    /// <summary>
    /// Accumulates time. Returns true when the reload duration has elapsed,
    /// triggering the runner to call Finish() which fires OnActionFinished.
    /// </summary>
    public override bool OnTick()
    {
        _elapsed += TickIntervalSeconds;
        if (_elapsed < _duration) return false;

        if (_mag != null)
        {
            _mag.FinishReload();
            // Replicate full ammo + IsReloading=false to all clients.
            character?.CharacterEquipment?.RecomputeActiveWeaponSentinel();
        }

        return true; // signal runner to call Finish()
    }

    /// <summary>
    /// Called by CharacterActions when the action is interrupted externally
    /// (knockback, death, weapon swap, manual cancel). Resets IsReloading without
    /// filling the magazine — ammo stays at its pre-reload value.
    /// </summary>
    public override void OnCancel()
    {
        if (_mag == null) return;
        _mag.CancelReload();
        // Replicate IsReloading=false to clients without ammo change.
        character?.CharacterEquipment?.RecomputeActiveWeaponSentinel();
    }
}
