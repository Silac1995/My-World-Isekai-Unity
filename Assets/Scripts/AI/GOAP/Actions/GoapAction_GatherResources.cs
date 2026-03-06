using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Action GOAP : Se rendre à la zone de récolte et récolter les GatherableObject.
/// Le gatherer se déplace vers la gatherableZone du building, trouve un GatherableObject,
/// et le récolte. L'item récolté est ajouté à l'inventaire du worker.
/// </summary>
public class GoapAction_GatherResources : GoapAction
{
    public override string ActionName => "GatherResources";

    public override Dictionary<string, bool> Preconditions => new Dictionary<string, bool>
    {
        { "hasGatherZone", true },
        { "hasResources", false }
    };

    public override Dictionary<string, bool> Effects => new Dictionary<string, bool>
    {
        { "hasResources", true }
    };

    public override float Cost => 1f;

    private GatheringBuilding _building;
    private bool _isComplete = false;
    private bool _isMovingToZone = false;
    private bool _arrivedAtZone = false;
    private bool _isGathering = false;
    private GatherableObject _currentTarget = null;
    private CharacterGatherAction _gatherAction = null;

    public override bool IsComplete => _isComplete;

    public GoapAction_GatherResources(GatheringBuilding building)
    {
        _building = building;
    }

    public override bool IsValid(Character worker)
    {
        return _building != null && _building.HasGatherableZone;
    }

    public override void Execute(Character worker)
    {
        if (_isComplete) return;

        // Vérifier que la zone est encore valide
        if (!_building.HasGatherableZone)
        {
            Debug.Log($"<color=orange>[GOAP Gather]</color> {worker.CharacterName} : la zone de récolte a disparu !");
            _isComplete = true;
            return;
        }

        var movement = worker.CharacterMovement;
        if (movement == null)
        {
            _isComplete = true;
            return;
        }

        // Phase 1 : Se déplacer vers la zone de récolte
        if (!_arrivedAtZone)
        {
            MoveToGatherZone(worker, movement);
            return;
        }

        // Phase 2 : Trouver un GatherableObject et récolter
        if (_currentTarget == null)
        {
            _currentTarget = FindNearestGatherable(worker);
            if (_currentTarget == null)
            {
                // Plus rien à récolter dans cette zone → la vider
                Debug.Log($"<color=orange>[GOAP Gather]</color> {worker.CharacterName} : plus de ressources dans la zone.");
                _building.ClearGatherableZone();
                _isComplete = true;
                return;
            }

            // Se déplacer vers le GatherableObject
            Vector3 targetPos = _currentTarget.transform.position;
            movement.SetDestination(targetPos);
            return;
        }

        // Phase 3 : Lancer la CharacterGatherAction quand on est arrivé
        if (!_isGathering)
        {
            // Vérifier si on est arrivé au GatherableObject
            if (!movement.PathPending && (!movement.HasPath || movement.RemainingDistance <= movement.StoppingDistance + 1f))
            {
                _isGathering = true;
                _gatherAction = new CharacterGatherAction(worker, _currentTarget);

                // S'abonner à la fin de l'action
                _gatherAction.OnActionFinished += () =>
                {
                    if (_gatherAction.HarvestedItem != null)
                    {
                        Debug.Log($"<color=green>[GOAP Gather]</color> {worker.CharacterName} a récolté {_gatherAction.HarvestedItem.ItemName} !");
                    }
                    _isComplete = true;
                };

                // Exécuter via le système de CharacterActions
                if (!worker.CharacterActions.ExecuteAction(_gatherAction))
                {
                    Debug.Log($"<color=orange>[GOAP Gather]</color> {worker.CharacterName} ne peut pas lancer la récolte.");
                    _isComplete = true;
                }
            }
            return;
        }

        // Phase 4 : Attendre que la CharacterGatherAction se termine
        // (Le système CharacterActions gère le timer et appelle OnApplyEffect automatiquement)
    }

    private void MoveToGatherZone(Character worker, CharacterMovement movement)
    {
        if (!_isMovingToZone)
        {
            Vector3 destination = _building.GatherableZone.GetRandomPointInZone();
            movement.SetDestination(destination);
            _isMovingToZone = true;
            return;
        }

        // Vérifier si on est arrivé
        if (!movement.PathPending && (!movement.HasPath || movement.RemainingDistance <= movement.StoppingDistance + 0.5f))
        {
            _arrivedAtZone = true;
            _isMovingToZone = false;
            Debug.Log($"<color=cyan>[GOAP Gather]</color> {worker.CharacterName} est arrivé à la zone de récolte.");
        }
    }

    /// <summary>
    /// Trouve le GatherableObject le plus proche qui produit un item voulu et qui n'est pas épuisé.
    /// </summary>
    private GatherableObject FindNearestGatherable(Character worker)
    {
        var wantedItems = _building.GetWantedItems();
        if (wantedItems.Count == 0) return null;

        GatherableObject nearest = null;
        float nearestDist = float.MaxValue;

        // Chercher dans un rayon autour du worker
        Collider[] colliders = Physics.OverlapSphere(worker.transform.position, 20f);
        foreach (var col in colliders)
        {
            var gatherable = col.GetComponentInParent<GatherableObject>();
            if (gatherable == null || !gatherable.CanGather()) continue;

            if (gatherable.HasAnyOutput(wantedItems))
            {
                float dist = Vector3.Distance(worker.transform.position, gatherable.transform.position);
                if (dist < nearestDist)
                {
                    nearest = gatherable;
                    nearestDist = dist;
                }
            }
        }

        return nearest;
    }

    public override void Exit(Character worker)
    {
        _isMovingToZone = false;
        _arrivedAtZone = false;
        _isGathering = false;
        _currentTarget = null;
        worker.CharacterMovement?.ResetPath();
    }
}
