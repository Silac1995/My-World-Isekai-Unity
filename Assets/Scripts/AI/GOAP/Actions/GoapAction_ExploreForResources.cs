using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Action GOAP : Explorer pour trouver une zone contenant des GatherableObject
/// qui produisent les items voulus par le GatheringBuilding.
/// Le gatherer se déplace aléatoirement, scanne les GatherableObject proches,
/// et quand il en trouve un compatible, il met à jour la zone du building.
/// </summary>
public class GoapAction_ExploreForResources : GoapAction
{
    public override string ActionName => "ExploreForResources";

    public override Dictionary<string, bool> Preconditions => new Dictionary<string, bool>
    {
        { "hasGatherZone", false }
    };

    public override Dictionary<string, bool> Effects => new Dictionary<string, bool>
    {
        { "hasGatherZone", true }
    };

    public override float Cost => 3f; // Coûteux car c'est de l'exploration

    private GatheringBuilding _building;
    private bool _isComplete = false;
    private bool _isMoving = false;
    private float _searchRadius = 30f;
    private float _scanTimer = 0f;
    private float _scanInterval = 2f; // Scanner toutes les 2 secondes
    private int _explorationAttempts = 0;
    private int _maxExplorationAttempts = 5;

    public override bool IsComplete => _isComplete;

    public GoapAction_ExploreForResources(GatheringBuilding building)
    {
        _building = building;
    }

    public override bool IsValid(Character worker)
    {
        return _building != null && !_building.HasGatherableZone;
    }

    public override void Execute(Character worker)
    {
        if (_isComplete) return;

        // Si le building a trouvé une zone pendant qu'on cherchait (un autre employé l'a trouvée)
        if (_building.HasGatherableZone)
        {
            _isComplete = true;
            return;
        }

        var movement = worker.CharacterMovement;
        if (movement == null)
        {
            _isComplete = true;
            return;
        }

        // Scanner les GatherableObject autour du worker
        _scanTimer += UnityEngine.Time.deltaTime;
        if (_scanTimer >= _scanInterval)
        {
            _scanTimer = 0f;
            if (ScanForGatherableObjects(worker))
            {
                _isComplete = true;
                return;
            }
        }

        // Si on n'est pas en mouvement, se déplacer vers un point aléatoire pour explorer
        if (!_isMoving || (!movement.PathPending && (!movement.HasPath || movement.RemainingDistance <= movement.StoppingDistance + 0.5f)))
        {
            _explorationAttempts++;

            if (_explorationAttempts > _maxExplorationAttempts)
            {
                Debug.Log($"<color=orange>[GOAP Explore]</color> {worker.CharacterName} n'a rien trouvé après {_maxExplorationAttempts} tentatives.");
                _isComplete = true;
                return;
            }

            // Se déplacer vers un point aléatoire dans un rayon
            Vector3 randomDirection = Random.insideUnitSphere * _searchRadius;
            randomDirection.y = 0;
            Vector3 targetPoint = worker.transform.position + randomDirection;

            if (UnityEngine.AI.NavMesh.SamplePosition(targetPoint, out UnityEngine.AI.NavMeshHit hit, _searchRadius, UnityEngine.AI.NavMesh.AllAreas))
            {
                movement.SetDestination(hit.position);
                _isMoving = true;
            }
        }
    }

    /// <summary>
    /// Scanne les GatherableObject autour du worker.
    /// Si un objet compatible est trouvé, cherche la Zone parente et met à jour le building.
    /// </summary>
    private bool ScanForGatherableObjects(Character worker)
    {
        var wantedItems = _building.GetWantedItems();
        if (wantedItems.Count == 0) return false;

        // Chercher les GatherableObject dans un rayon
        Collider[] colliders = Physics.OverlapSphere(worker.transform.position, _searchRadius);
        foreach (var col in colliders)
        {
            var gatherable = col.GetComponentInParent<GatherableObject>();
            if (gatherable == null || !gatherable.CanGather()) continue;

            // Vérifie si le gatherable produit un item voulu
            if (gatherable.HasAnyOutput(wantedItems))
            {
                // Chercher une Zone parente (GatheringArea)
                var zone = col.GetComponentInParent<Zone>();
                if (zone != null)
                {
                    Debug.Log($"<color=green>[GOAP Explore]</color> {worker.CharacterName} a trouvé une zone de récolte : {zone.zoneName} !");
                    _building.SetGatherableZone(zone);
                    return true;
                }
                else
                {
                    Debug.Log($"<color=orange>[GOAP Explore]</color> {worker.CharacterName} a trouvé un {gatherable.gameObject.name} mais sans Zone parente.");
                }
            }
        }

        return false;
    }

    public override void Exit(Character worker)
    {
        _isMoving = false;
        worker.CharacterMovement?.ResetPath();
    }
}
