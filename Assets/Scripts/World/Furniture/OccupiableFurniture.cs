using UnityEngine;

/// <summary>
/// Abstract base for any <see cref="Furniture"/> that a <see cref="Character"/> can
/// reserve and physically occupy (Bed, Chair, Cashier, CraftingStation, …).
///
/// Holds the shared <c>_occupant</c> + <c>_reservedBy</c> state and the standard
/// <see cref="Reserve"/> / <see cref="Use"/> / <see cref="Release"/> bodies that used
/// to live on the base <see cref="Furniture"/> class. Phase 2b's design slip — every
/// <c>Furniture</c> subclass inheriting occupancy machinery whether it needed it or not —
/// is corrected here per ISP (rule #12).
///
/// Subclasses override <see cref="Use"/> / <see cref="Release"/> to add their own
/// side-effects (e.g. <c>Cashier</c> broadcasts <c>NotifyOccupiedClientRpc</c>;
/// <c>BedFurniture</c> picks a free slot and calls <c>EnterSleep</c>) and call
/// <c>base.Use(...)</c> / <c>base.Release()</c> to keep the lock state in sync.
///
/// Multi-slot furniture (<c>BedFurniture</c>) overrides the single-slot API to delegate
/// to its slot-aware methods — the base implementation is the legacy "first-come" path.
/// </summary>
public abstract class OccupiableFurniture : Furniture, IOccupiable
{
    private Character _occupant;
    private Character _reservedBy;

    /// <summary>
    /// The active occupant on this peer. Marked <c>virtual</c> so subclasses with their own
    /// replication channel can override the read path — e.g. <see cref="Cashier"/> resolves
    /// the occupant from a sibling <c>CashierNetSync</c> NetworkVariable on client peers,
    /// because the server-only <see cref="Use"/> assignment never reaches clients otherwise
    /// (rule #19). Base implementation returns the in-memory field, which is correct on the
    /// server and on every subclass that does not need cross-peer visibility.
    /// </summary>
    public virtual Character Occupant => _occupant;
    public Character ReservedBy => _reservedBy;
    public virtual bool IsOccupied => Occupant != null;

    /// <summary>
    /// Réserve le meuble pour un personnage en approche. Advisory only — see <see cref="IOccupiable"/>.
    /// </summary>
    public virtual bool Reserve(Character character)
    {
        if (character == null) return false;
        if (IsOccupied || _reservedBy != null) return false;

        _reservedBy = character;
        return true;
    }

    /// <summary>
    /// Un personnage utilise physiquement ce meuble. Override + call <c>base.Use</c> from
    /// subclasses that need to broadcast / animate the occupy event.
    /// </summary>
    public virtual bool Use(Character character)
    {
        if (character == null) return false;
        if (IsOccupied)
        {
            Debug.Log($"<color=orange>[Furniture]</color> {FurnitureName} est déjà utilisé par {_occupant.CharacterName}.");
            return false;
        }

        _occupant = character;
        _reservedBy = null;
        _occupant.SetOccupyingFurniture(this);
        Debug.Log($"<color=cyan>[Furniture]</color> {character.CharacterName} utilise {FurnitureName}.");
        return true;
    }

    /// <summary>
    /// Libère l'utilisation ou la réservation du meuble. Override + call <c>base.Release</c>
    /// from subclasses that need to broadcast / animate the release event.
    /// </summary>
    public virtual void Release()
    {
        if (_occupant != null)
        {
            Debug.Log($"<color=cyan>[Furniture]</color> {_occupant.CharacterName} quitte {FurnitureName}.");
            _occupant.SetOccupyingFurniture(null);
        }
        _occupant = null;
        _reservedBy = null;
    }

    /// <summary>
    /// Per-character inverse of <see cref="Use(Character)"/>. Releases <b>only</b> the
    /// given character's hold on this furniture (occupant or reservation), leaving any
    /// other occupants intact.
    ///
    /// Single-slot subclasses (Cashier, ChairFurniture, …) inherit the default
    /// implementation which delegates to <see cref="Release"/> when <paramref name="c"/>
    /// is the current occupant. Multi-slot subclasses (<see cref="BedFurniture"/>) MUST
    /// override to release only the caller's slot — otherwise calling
    /// <c>furniture.Release()</c> would evict every sleeping person from a shared bed
    /// whenever one of them entered combat.
    ///
    /// Returns <c>true</c> if a hold was released, <c>false</c> if <paramref name="c"/>
    /// was not occupying / reserving this furniture.
    /// </summary>
    public virtual bool Leave(Character c)
    {
        if (c == null) return false;
        if (_occupant == c)
        {
            Release();
            return true;
        }
        if (_reservedBy == c)
        {
            _reservedBy = null;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Vérifie si le meuble est totalement libre (ni occupé, ni réservé).
    /// </summary>
    public virtual bool IsFree()
    {
        return _occupant == null && _reservedBy == null;
    }

    /// <summary>
    /// Universal interactable dispatch — for occupiable furniture, E-press binds the
    /// interactor as the occupant via <see cref="Use"/>. Subclasses with bespoke
    /// interaction semantics (e.g. opening a UI without occupying) can override
    /// further; the base behavior here matches the legacy
    /// <c>FurnitureInteractable.Interact → _furniture.Use</c> path so chairs / beds /
    /// cashiers / crafting stations still seat the interactor on tap-E.
    /// </summary>
    public override bool OnInteract(Character interactor)
    {
        if (interactor == null) return false;
        if (IsOccupied)
        {
            // Already-occupied case is logged inside Use() too — no double log here.
            return false;
        }
        return Use(interactor);
    }
}
