using UnityEngine;

/// <summary>
/// Vendor job — pool model. The shop has N vendor slots (one per cashier
/// with RequiresVendor=true). Each shift, every assigned worker
/// independently picks any free cashier in the pool, races to claim it
/// via Reserve, walks to the InteractionPoint, then queues
/// <see cref="CharacterAction_OccupyFurniture"/> on arrival to take the seat.
/// Customer interactions are customer-initiated through
/// CashierInteractable / RequestStartBuyServerRpc — there is no "call next
/// customer" loop here.
///
/// Player-vendor parity (rule #22): players queue
/// <see cref="CharacterAction_OccupyFurniture"/> via
/// <see cref="CashierInteractable.Interact"/> (E-press, three-branch routing).
/// NPCs queue the same action here in step 3 of <see cref="Execute"/>. Switching
/// the controller between PlayerController and NPCController is a no-op for
/// seating state — the action runs on <see cref="CharacterActions"/>, which lives
/// on the Character regardless of who drives it. Pre-2026-05-14 the NPC path
/// relied on the deleted <c>Cashier.ServerTickAutoOccupy</c> proximity tick.
/// </summary>
public class JobVendor : Job
{
    public override string JobTitle => "Vendor";
    public override JobCategory Category => JobCategory.Service;

    private Cashier _heldCashier;
    private bool _hasReserved;
    private bool _isMovingToCashier;

    public Cashier HeldCashier => _heldCashier;

    public override string CurrentActionName
    {
        get
        {
            if (_heldCashier == null) return "Idle (no free cashier)";
            if (_heldCashier.Occupant == _worker) return $"Manning {_heldCashier.FurnitureName}";
            return $"Walking to {_heldCashier.FurnitureName}";
        }
    }

    public override bool CanExecute() =>
        base.CanExecute() && _workplace is ShopBuilding;

    public override void Execute()
    {
        if (_worker == null) return;

        // 1) Already manning the cashier — idle until customer arrives or shift ends.
        if (_heldCashier != null && _heldCashier.Occupant == _worker)
        {
            _isMovingToCashier = false;
            return;
        }

        // 2) Lost the seat to someone else (race / forced eviction / cashier removed) —
        //    drop our reservation and re-pick next tick.
        if (_heldCashier != null && _heldCashier.Occupant != null && _heldCashier.Occupant != _worker)
        {
            if (_heldCashier.ReservedBy == _worker) _heldCashier.Release();
            _heldCashier = null;
            _hasReserved = false;
            _isMovingToCashier = false;
            return;
        }

        // 3) Reservation held + walked to the InteractionPoint → queue the seat-take action.
        //    JobVendor runs server-side, so ExecuteAction is direct (no ServerRpc roundtrip).
        if (_heldCashier != null && _isMovingToCashier)
        {
            var interactable = _heldCashier.GetComponent<InteractableObject>();
            bool inZone = interactable != null && interactable.IsCharacterInInteractionZone(_worker);
            if (inZone
                && _worker.CharacterActions != null
                && _worker.CharacterActions.CurrentAction == null)
            {
                bool started = _worker.CharacterActions.ExecuteAction(
                    new CharacterAction_OccupyFurniture(_worker, _heldCashier));
                if (started)
                {
                    _isMovingToCashier = false;
                    // Next tick: step 1 sees Occupant == _worker and idles.
                }
            }
            return;
        }

        // 4) No reservation → pick a free cashier from the pool and walk to it.
        var shop = _workplace as ShopBuilding;
        if (shop == null) return;

        for (int i = 0; i < shop.Cashiers.Count; i++)
        {
            var c = shop.Cashiers[i];
            if (c == null) continue;
            if (!c.RequiresVendor) continue;
            if (c.Occupant != null) continue;
            if (c.ReservedBy != null && c.ReservedBy != _worker) continue;
            if (!c.Reserve(_worker)) continue;

            _heldCashier = c;
            _hasReserved = true;
            var movement = _worker.CharacterMovement;
            if (movement != null)
            {
                movement.SetDestination(c.GetInteractionPosition(_worker.transform.position));
                _isMovingToCashier = true;
            }
            return;
        }

        // No free cashier — vendor stays idle in the shop zone (existing fallback behavior).
    }

    public override void Unassign()
    {
        if (_heldCashier != null)
        {
            // Route through the action's OnCancel so future listeners (animations, audio,
            // schedule transitions) fire correctly. ClearCurrentAction is server-side here
            // since JobVendor only runs on the server.
            if (_heldCashier.Occupant == _worker)
            {
                if (_worker != null && _worker.CharacterActions != null)
                    _worker.CharacterActions.ClearCurrentAction();

                // Defensive belt-and-suspenders: if the current action wasn't the occupy
                // action (e.g. it had already finished but Unassign was deferred a tick),
                // release the seat directly so the cashier doesn't stay held.
                if (_heldCashier != null && _heldCashier.Occupant == _worker)
                    _heldCashier.Leave(_worker);
            }
            else if (_heldCashier.ReservedBy == _worker)
            {
                _heldCashier.Release();
            }
        }
        _heldCashier = null;
        _hasReserved = false;
        _isMovingToCashier = false;
        base.Unassign();
    }
}
