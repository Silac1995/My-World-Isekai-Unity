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
    /// Role-based authorization for taking this seat. Default = true (anyone can sit on a
    /// chair). Subclasses with a job-specific gate override — e.g. <see cref="Cashier"/>
    /// requires the character to be the assigned <see cref="JobVendor"/> when the cashier
    /// has <c>RequiresVendor = true</c>.
    ///
    /// Called server-side from <see cref="CharacterAction_OccupyFurniture.CanExecute"/> and
    /// from <see cref="CharacterActions.RequestOccupyFurnitureServerRpc"/>. Server-only
    /// authoritative — client-side reads of CharacterJob state are not guaranteed to be
    /// populated for remote-client player owners (the system is not currently NetVar-replicated),
    /// so this gate intentionally lives on the server.
    /// </summary>
    public virtual bool IsCharacterAllowedToOccupy(Character character) => character != null;

    /// <summary>
    /// Universal interactable dispatch — tap-E binds the interactor as the occupant by
    /// queueing <see cref="CharacterAction_OccupyFurniture"/>. NPCs / host paths run
    /// server-side and queue directly; player-owner client paths relay through the
    /// canonical <see cref="CharacterActions.RequestOccupyFurnitureServerRpc"/> which
    /// re-validates proximity and authoritatively queues the action.
    ///
    /// Furniture with a bespoke E-press handler (e.g. <see cref="Cashier"/> via
    /// <c>CashierInteractable</c>, <see cref="BedFurniture"/> via the sleep flow,
    /// <c>CraftingStation</c> via the craft flow) override this entirely and route
    /// through their own action — Use is called inside those actions instead of here.
    ///
    /// Pre-2026-05-14 this called <see cref="Use"/> directly, bypassing the action system
    /// and breaking the uniform "every gameplay effect goes through CharacterAction" rule
    /// (rule #22). See docs/superpowers/specs/2026-05-14-furniture-occupancy-via-characteraction-design.md.
    /// </summary>
    public override bool OnInteract(Character interactor)
    {
        if (interactor == null) return false;
        if (IsOccupied && Occupant != interactor) return false;
        if (interactor.CharacterActions == null) return false;

        // Use the interactor's NetworkObject to decide local server vs client (every Character
        // has one; not every Furniture does — Bed/Chair are baked into a building prefab and
        // have no per-furniture NetworkObject per the no-nested-NO rule).
        var charNetObj = interactor.GetComponent<Unity.Netcode.NetworkObject>();
        bool isServer = charNetObj != null && charNetObj.NetworkManager != null && charNetObj.NetworkManager.IsServer;

        if (isServer)
        {
            // NPC / host path — queue the action directly on the server-authoritative actor.
            // Continuous actions are server-only; ExecuteAction returns true on success.
            return interactor.CharacterActions.ExecuteAction(new CharacterAction_OccupyFurniture(interactor, this));
        }

        // Client owner path — relay through the canonical ServerRpc which re-validates
        // proximity and authoritatively queues the action. The reference targets ANY
        // NetworkBehaviour reachable from this furniture: Cashier carries its own NB, but
        // Bed/Chair live as plain children under a building's NB. The position passed
        // alongside lets the server disambiguate multi-furniture buildings (same pattern
        // as RequestSleepOnFurnitureServerRpc with FindClosestBedUnder).
        var nb = GetComponent<Unity.Netcode.NetworkBehaviour>();
        if (nb == null) nb = GetComponentInParent<Unity.Netcode.NetworkBehaviour>();
        if (nb == null)
        {
            Debug.LogWarning($"[OccupiableFurniture] OnInteract: no NetworkBehaviour reachable from {FurnitureName} — cannot send ServerRpc. Tap-E ignored on client.");
            return false;
        }
        interactor.CharacterActions.RequestOccupyFurnitureServerRpc(
            new Unity.Netcode.NetworkBehaviourReference(nb),
            transform.position);
        return true;
    }
}
