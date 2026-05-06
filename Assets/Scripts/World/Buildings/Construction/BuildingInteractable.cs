using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Player-facing interactable surface on a Building. Inherits InteractableObject so the
/// existing E-key flow (PlayerInteractionDetector → Interact / hold-menu) drives it
/// uniformly with all other interactables — no custom click-handling.
///
/// Phase 1 exposes only "Finish Construction" when the actor is the building's placer
/// (PlacedByCharacterId match). Stub seats: Abandon, Sell, OpenInterior — wired in
/// later phases.
///
/// The InteractionZone defaults to <c>Building.BuildingZone</c> when not authored —
/// the player walks into the building footprint and presses E. Authoring an explicit
/// trigger collider on the prefab overrides that fallback.
///
/// Updated 2026-05-06 — refactored from plain MonoBehaviour to InteractableObject.
/// See docs/superpowers/specs/2026-05-06-building-construction-loop-design.md.
/// </summary>
[RequireComponent(typeof(Building))]
public class BuildingInteractable : InteractableObject
{
    private Building _building;

    protected void Awake()
    {
        _building = GetComponent<Building>();

        // Default the InteractableObject's _interactionZone to Building.BuildingZone if
        // the prefab didn't author a separate trigger. Use reflection because the base
        // field is `private` — keeping the upstream API unchanged.
        var fInteractionZone = typeof(InteractableObject).GetField("_interactionZone",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (fInteractionZone != null)
        {
            var current = fInteractionZone.GetValue(this) as Collider;
            if (current == null && _building != null && _building.BuildingZone != null)
            {
                fInteractionZone.SetValue(this, _building.BuildingZone);
            }
        }

        // Default prompt — designer can override on the prefab.
        if (string.IsNullOrEmpty(interactionPrompt) || interactionPrompt == "Press E to interact")
        {
            interactionPrompt = "Press E to build";
        }
    }

    /// <summary>
    /// Tap-E entry point. Phase 1: only the placer can finalize a building under construction.
    /// Server-relay via Building.RequestStartFinishConstructionRpc — required for
    /// CharacterAction_Continuous which OnTick's server-authoritatively.
    /// </summary>
    public override void Interact(Character interactor)
    {
        if (interactor == null || _building == null)
        {
            Debug.Log($"<color=magenta>[BuildingInteractable.Interact]</color> aborted — interactor or building null");
            return;
        }

        bool inZone = IsCharacterInInteractionZone(interactor);
        bool isUC = _building.IsUnderConstruction;
        bool localIsServer = _building.IsServer;
        var charPos = interactor.transform.position;
        var zoneBounds = InteractionZone != null ? InteractionZone.bounds.ToString() : "NULL";

        Debug.Log($"<color=magenta>[BuildingInteractable.Interact]</color> {_building.BuildingName} actor={interactor.CharacterId} actorPos={charPos} zoneBounds={zoneBounds} inZone={inZone} isUnderConstruction={isUC} | localIsServer={localIsServer}");

        if (!inZone) return;
        if (!isUC) return;

        // Cooperative model: anyone in the zone can finalize. Server-relay via RPC.
        Debug.Log($"<color=magenta>[BuildingInteractable.Interact]</color> dispatching RequestStartFinishConstructionRpc");
        _building.RequestStartFinishConstructionRpc(new Unity.Netcode.NetworkBehaviourReference(interactor));
    }

    /// <summary>
    /// Hold-E menu options. Phase 1: returns "Finish Construction" while UnderConstruction
    /// (same as tap-E target — provides discoverability). Phase 2 stubs (Abandon, Sell,
    /// OpenInterior) plug in here.
    /// </summary>
    public override List<InteractionOption> GetHoldInteractionOptions(Character interactor)
    {
        if (interactor == null || _building == null) return null;

        var opts = new List<InteractionOption>();
        if (_building.IsUnderConstruction)
        {
            opts.Add(new InteractionOption("Finish Construction", () => Interact(interactor)));
        }
        // Phase 2:
        // opts.Add(new InteractionOption("Abandon", () => ...));
        // opts.Add(new InteractionOption("Sell",    () => ...));

        return opts.Count > 0 ? opts : null;
    }

    /// <summary>
    /// True iff the actor's CharacterId matches Building.PlacedByCharacterId.
    /// Phase 2 will broaden to include co-owners / community manager authority.
    /// </summary>
    public bool IsOwner(Character actor)
    {
        if (_building == null || actor == null) return false;
        var placedBy = _building.PlacedByCharacterId.Value.ToString();
        if (string.IsNullOrEmpty(placedBy)) return false;
        return placedBy == actor.CharacterId;
    }
}
