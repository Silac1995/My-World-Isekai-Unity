/// <summary>
/// Contract for any furniture (or future non-furniture surface like a mount or vehicle)
/// that a <see cref="Character"/> can reserve and physically occupy.
///
/// Split out from <see cref="Furniture"/> on 2026-05-08 per ISP (rule #12) — the legacy
/// design baked occupancy state into every furniture instance, so decorative or pure-display
/// pieces (<c>StorageFurniture</c>, <c>TimeClockFurniture</c>, <c>ManagementFurniture</c>,
/// <c>DisplayTextFurniture</c>) inherited <c>_occupant</c>/<c>_reservedBy</c> machinery they
/// never used.
///
/// Reservation is **advisory** — see <see cref="Reserve"/>: <see cref="Use"/> only checks
/// <see cref="IsOccupied"/>, so whoever calls <see cref="Use"/> first wins. This is the
/// race-friendly pattern documented in the JobVendor pool model (<c>shop-system</c> SKILL).
///
/// Concrete shared implementation lives in <see cref="OccupiableFurniture"/>; subclasses
/// (<c>BedFurniture</c>, <c>ChairFurniture</c>, <c>Cashier</c>, <c>CraftingStation</c>)
/// extend that abstract base. Future non-Furniture occupiables (mounts, vehicles) can
/// implement the interface directly without inheriting <see cref="Furniture"/>.
/// </summary>
public interface IOccupiable
{
    /// <summary>The character currently driving / using this surface, or null.</summary>
    Character Occupant { get; }

    /// <summary>The character who reserved this surface but has not yet arrived, or null.</summary>
    Character ReservedBy { get; }

    /// <summary>True when an <see cref="Occupant"/> is bound.</summary>
    bool IsOccupied { get; }

    /// <summary>True when no occupant AND no reservation is held.</summary>
    bool IsFree();

    /// <summary>
    /// Attempt to reserve the surface for an approaching character. Advisory only —
    /// <see cref="Use"/> does not enforce reservation, only the <see cref="IsOccupied"/>
    /// check. Returns false if already occupied or reserved.
    /// </summary>
    bool Reserve(Character character);

    /// <summary>
    /// Bind the character as the active occupant. Returns false if already occupied.
    /// On success, the implementation is responsible for clearing any prior reservation
    /// AND back-linking the character via <see cref="Character.SetOccupyingFurniture"/>.
    /// </summary>
    bool Use(Character character);

    /// <summary>
    /// Release any active occupancy + any pending reservation. Implementations must
    /// also clear the back-link via <see cref="Character.SetOccupyingFurniture(null)"/>.
    /// </summary>
    void Release();
}
