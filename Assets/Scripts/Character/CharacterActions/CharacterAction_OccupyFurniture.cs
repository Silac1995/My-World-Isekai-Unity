using UnityEngine;

/// <summary>
/// Continuous action that seats a character on an <see cref="OccupiableFurniture"/> and
/// holds the seat until cancelled. Single source of truth for occupy/leave across both
/// player (E-press → ServerRpc) and NPC (server-side <see cref="JobVendor"/>.Execute) paths
/// — controller swaps become no-ops for seating state (rule #22 player↔NPC parity).
///
/// Server-only: continuous actions are rejected on clients at
/// <c>CharacterActions.ExecuteAction</c>'s continuous branch (the routine that ticks OnTick
/// is gated on IsServer). OnStart mutates server-authoritative state (Use/Leave on the
/// target Furniture) and therefore must not run on a client peer.
///
/// Lifecycle:
/// <list type="bullet">
///   <item><b>OnStart</b> — calls <see cref="OccupiableFurniture.Use"/>. If Use returns
///   false (lost the race), <see cref="_seatingFailed"/> is set and OnTick ends the action
///   on its first tick.</item>
///   <item><b>OnTick</b> — validates that the target still exists and that
///   <c>target.Occupant == character</c>. On invalidation, calls Leave defensively and
///   returns true so <see cref="CharacterAction.Finish"/> runs.</item>
///   <item><b>OnCancel</b> — calls <see cref="OccupiableFurniture.Leave"/> on the target.
///   Idempotent — Leave returns false if not the current occupant (already evicted by
///   <see cref="Character.AutoLeaveOccupiedFurniture"/> on combat / unconscious / death).</item>
/// </list>
///
/// Replication: <see cref="CharacterAction.IsReplicatedInternally"/> remains false → the
/// standard 600s-sentinel visual-proxy pipeline (CharacterActions.ExecuteAction line ~50)
/// sets <c>_currentAction</c> on every peer. <see cref="ShouldPlayGenericActionAnimation"/>
/// is overridden to false because the character should look like they are idling at the
/// StandingPoint, not "performing an action".
///
/// Authored 2026-05-14 — replaces <c>Cashier.ServerTickAutoOccupy</c>. See
/// docs/superpowers/specs/2026-05-14-furniture-occupancy-via-characteraction-design.md.
/// </summary>
public sealed class CharacterAction_OccupyFurniture : CharacterAction_Continuous
{
    private readonly OccupiableFurniture _target;
    private bool _seatingFailed;

    public OccupiableFurniture Target => _target;

    public override string ActionName => "OccupyFurniture";
    public override bool ShouldPlayGenericActionAnimation => false;

    public CharacterAction_OccupyFurniture(Character character, OccupiableFurniture target)
        : base(character)
    {
        _target = target;
        TickIntervalSeconds = 1f;
    }

    public override bool CanExecute()
    {
        if (character == null || _target == null) return false;
        var interactable = _target.GetComponent<InteractableObject>();
        if (interactable != null && !interactable.IsCharacterInInteractionZone(character)) return false;
        // Allow re-entry by the same character (defensive — should already be rejected upstream),
        // but reject if someone else is in the seat.
        if (_target.IsOccupied && _target.Occupant != character) return false;
        return true;
    }

    public override void OnStart()
    {
        // Belt-and-suspenders: CharacterActions short-circuits the continuous tick routine on
        // non-server peers (CharacterActions.cs:596), so OnStart is effectively server-side.
        // If we somehow ran on a client, mark seating-failed so OnTick ends the action without
        // touching server-authoritative state.
        var co = character != null ? character.GetComponent<Unity.Netcode.NetworkObject>() : null;
        bool isServer = co != null && co.NetworkManager != null && co.NetworkManager.IsServer;
        if (!isServer)
        {
            _seatingFailed = true;
            return;
        }

        if (!_target.Use(character))
        {
            _seatingFailed = true;
            Debug.LogWarning($"<color=orange>[OccupyFurniture]</color> {character?.CharacterName} failed to seat on {_target?.FurnitureName} (lost the race or already occupied).");
        }
    }

    public override bool OnTick()
    {
        if (_seatingFailed) return true;
        if (_target == null) return true;

        // Lost the seat (forced eviction by AutoLeaveOccupiedFurniture, furniture removed,
        // race with another OccupyFurniture action). Leave defensively — Leave returns false
        // if we aren't the occupant, so this is a no-op in the already-evicted case.
        if (_target.Occupant != character)
        {
            _target.Leave(character);
            return true;
        }
        return false;
    }

    public override void OnCancel()
    {
        if (_seatingFailed) return;
        if (_target == null) return;
        // Server-only — continuous actions only run on the server, and OnCancel is invoked
        // from CharacterActions.ClearCurrentActionLocally which is also server-side for
        // continuous actions. Idempotent: Leave(this) returns false if we aren't the current
        // occupant (already cleared by AutoLeaveOccupiedFurniture on combat/unconscious/death,
        // which runs BEFORE OnCombatStateChanged fires the ClearCurrentActionLocally that
        // brought us here).
        _target.Leave(character);
    }
}
