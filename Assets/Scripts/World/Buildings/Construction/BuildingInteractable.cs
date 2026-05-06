using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Player-facing interactable surface on a Building. Phase 1 exposes only "Finish
/// Construction" when the actor is the building's placer (PlacedByCharacterId match).
/// Stub seats: Abandon, Sell, OpenInterior — wired in later phases.
///
/// Players queue actions via PlayerController routing (Rule #33). NPCs (Phase 2)
/// reach the same actions through GOAP.
///
/// Authored 2026-05-06 — see docs/superpowers/specs/2026-05-06-building-construction-loop-design.md.
/// </summary>
[RequireComponent(typeof(Building))]
public class BuildingInteractable : MonoBehaviour
{
    public enum InteractionId
    {
        None = 0,
        FinishConstruction = 1,
        // Phase 2 stubs:
        // Abandon = 100,
        // Sell = 101,
        // OpenInterior = 102,
    }

    private Building _building;

    private void Awake() => _building = GetComponent<Building>();

    /// <summary>
    /// Returns the InteractionIds available to the requesting actor right now.
    /// Caller fills the result list (no per-call allocation; reuse a buffer).
    /// </summary>
    public void GetAvailableInteractions(Character actor, List<InteractionId> result)
    {
        if (result == null) return;
        result.Clear();
        if (_building == null || actor == null) return;

        if (_building.IsUnderConstruction && IsOwner(actor))
        {
            result.Add(InteractionId.FinishConstruction);
        }
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

    /// <summary>
    /// Player-input entry point. Looks up the action class for the InteractionId,
    /// instantiates it, and queues via the actor's CharacterActions.ExecuteAction.
    /// Server-RPC dispatch happens inside the action itself when needed.
    /// </summary>
    public bool TryQueueInteraction(InteractionId id, Character actor)
    {
        if (_building == null || actor == null) return false;

        switch (id)
        {
            case InteractionId.FinishConstruction:
                if (!_building.IsUnderConstruction) return false;
                if (!IsOwner(actor)) return false;
                var action = new CharacterAction_FinishConstruction(actor, _building);
                return actor.CharacterActions != null && actor.CharacterActions.ExecuteAction(action);

            default:
                return false;
        }
    }
}
