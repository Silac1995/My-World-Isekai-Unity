using UnityEngine;

/// <summary>
/// Vendor job — pool model. The shop has N vendor slots (one per cashier
/// with RequiresVendor=true). Each shift, every assigned worker
/// independently picks any free cashier in the pool, races to claim it
/// via Reserve+Use (loser falls through to the next free one), and idles
/// while occupying. Customer interactions are customer-initiated through
/// CashierInteractable / RequestStartBuyServerRpc — there is no "call next
/// customer" loop here.
///
/// Player-vendor parity (rule #22): players aren't driven by Execute —
/// they walk where they want, and Cashier.ServerTickAutoOccupy seats them
/// when they happen to stand on the InteractionPoint during their work
/// shift. Symmetrical race semantics apply.
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

        // 1) Already occupying — idle.
        if (_heldCashier != null && _heldCashier.Occupant == _worker)
        {
            _isMovingToCashier = false;
            return;
        }

        // 2) Lost the seat (race / shift change / cashier removed) — drop and re-pick.
        if (_heldCashier != null && _heldCashier.Occupant != _worker)
        {
            if (_heldCashier.ReservedBy == _worker) _heldCashier.Release();
            _heldCashier = null;
            _hasReserved = false;
            _isMovingToCashier = false;
        }

        // 3) Pick a free cashier from the shop and walk to it.
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
            if (_heldCashier.Occupant == _worker) _heldCashier.Release();
            else if (_heldCashier.ReservedBy == _worker) _heldCashier.Release();
        }
        _heldCashier = null;
        _hasReserved = false;
        _isMovingToCashier = false;
        base.Unassign();
    }
}
