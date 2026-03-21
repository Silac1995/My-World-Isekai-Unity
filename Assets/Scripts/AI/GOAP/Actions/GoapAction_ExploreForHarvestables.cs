using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Action GOAP : Explorer pour trouver une zone contenant des Harvestable
/// qui produisent les items voulus par le HarvestingBuilding.
/// Le harvester se déplace aléatoirement, scanne les Harvestable proches,
/// et quand il en trouve un compatible, il met à jour la zone du building.
/// </summary>
public class GoapAction_ExploreForHarvestables : GoapAction
{
    public override string ActionName => "ExploreForHarvestables";

    public override Dictionary<string, bool> Preconditions => new Dictionary<string, bool>
    {
        { "hasHarvestZone", false },
        { "hasResources", false }
    };

    public override Dictionary<string, bool> Effects => new Dictionary<string, bool>
    {
        { "hasHarvestZone", true }
    };

    public override float Cost => 3f;

    private HarvestingBuilding _building;
    private bool _isComplete = false;
    private bool _isMoving = false;
    private float _searchRadius = 30f;
    private int _explorationAttempts = 0;
    private int _maxExplorationAttempts = 10;

    public override bool IsComplete => _isComplete;

    public GoapAction_ExploreForHarvestables(HarvestingBuilding building)
    {
        _building = building;
    }

    public override bool IsValid(Character worker)
    {
        return _building != null;
    }

    public override void Execute(Character worker)
    {
        if (_isComplete) return;

        // Terminer l'exploration immédiatement si des tâches sont disponibles
        if (_building.TaskManager != null && _building.TaskManager.HasAnyTaskOfType<HarvestResourceTask>())
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

        // Scanner les Harvestable via CharacterAwareness (chaque tick)
        if (ScanForHarvestables(worker))
        {
            _isComplete = true;
            return;
        }

        // Opportuniste : ramasser les WorldItem voulus vus au sol
        TryPickupNearbyWantedItems(worker);

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
    /// Scanne les Harvestable via CharacterAwareness.
    /// Cherche d'abord une Zone parente dans la hiérarchie,
    /// sinon cherche une Zone dont le collider contient le Harvestable.
    /// </summary>
    private bool ScanForHarvestables(Character worker)
    {
        var wantedItems = _building.GetWantedItems();
        if (wantedItems.Count == 0) return false;

        var awareness = worker.CharacterAwareness;
        if (awareness == null) return false;

        var visibleHarvestables = awareness.GetVisibleInteractables<Harvestable>();

        foreach (var harvestable in visibleHarvestables)
        {
            if (!harvestable.CanHarvest()) continue;

            if (harvestable.HasAnyOutput(wantedItems))
            {
                Debug.Log($"<color=green>[GOAP Explore]</color> {worker.CharacterName} a trouvé et ajouté un nouveau harvestable à la liste du bâtiment: {harvestable.gameObject.name} !");
                _building.AddToTrackedHarvestables(harvestable);

                // On essaie quand même d'ajouter toute la zone autour si elle existe
                Zone zone = harvestable.GetComponentInParent<Zone>();
                if (zone == null) zone = FindZoneContaining(harvestable.transform.position);
                if (zone == null) zone = FindNearestZone(harvestable.transform.position);

                if (zone != null && _building.HarvestableZone != zone)
                {
                    _building.ScanAndRegisterZone(zone);
                }

                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Cherche une Zone dont le collider (trigger) contient la position donnée.
    /// </summary>
    private Zone FindZoneContaining(Vector3 position)
    {
        Collider[] colliders = Physics.OverlapSphere(position, 1f, Physics.AllLayers, QueryTriggerInteraction.Collide);
        foreach (var col in colliders)
        {
            var zone = col.GetComponent<Zone>();
            if (zone != null) return zone;
        }
        return null;
    }

    /// <summary>
    /// Cherche la Zone la plus proche de la position donnée (fallback).
    /// </summary>
    private Zone FindNearestZone(Vector3 position)
    {
        Zone[] allZones = Object.FindObjectsByType<Zone>(FindObjectsSortMode.None);
        Zone nearest = null;
        float nearestDist = float.MaxValue;

        foreach (var zone in allZones)
        {
            float dist = Vector3.Distance(position, zone.transform.position);
            if (dist < nearestDist)
            {
                nearest = zone;
                nearestDist = dist;
            }
        }

        return nearest;
    }

    /// <summary>
    /// Pendant l'exploration, si le worker voit des WorldItem voulus par terre, il les ramasse.
    /// </summary>
    private void TryPickupNearbyWantedItems(Character worker)
    {
        var awareness = worker.CharacterAwareness;
        if (awareness == null) return;

        var wantedItems = _building.GetWantedItems();
        if (wantedItems.Count == 0) return;

        // Chercher les ItemInteractable visibles (WorldItem hérite d'InteractableObject via ItemInteractable)
        var visibleItems = awareness.GetVisibleInteractables<ItemInteractable>();

        foreach (var itemInteractable in visibleItems)
        {
            var worldItem = itemInteractable.GetComponent<WorldItem>();
            if (worldItem == null || worldItem.ItemInstance == null || worldItem.IsBeingCarried) continue;

            // Ignorer si l'item est dans la zone de Deposit
            if (Zone.IsPositionInZoneType(worldItem.transform.position, ZoneType.Deposit)) continue;

            // Vérifier si c'est un wanted item
            if (!wantedItems.Contains(worldItem.ItemInstance.ItemSO)) continue;

            // C'est un item valide, on a trouvé une zone potentielle !
            Debug.Log($"<color=cyan>[GOAP Explore]</color> {worker.CharacterName} a trouvé un {worldItem.ItemInstance.ItemSO.ItemName} par terre (Explore).");
            // Essayer de ramasser — vérifier si on peut porter
            var equipment = worker.CharacterEquipment;
            if (equipment != null && !equipment.CanCarryItemAnyMore(worldItem.ItemInstance))
            {
                // Le personnage est plein (sac plein + mains occupées), on skip
                continue;
            }

            // Exécuter l'action de pickup standard (gérera le sac ou les mains automatiquement)
            var pickupAction = new CharacterPickUpItem(worker, worldItem.ItemInstance, worldItem.gameObject);
            if (worker.CharacterActions.ExecuteAction(pickupAction))
            {
                Debug.Log($"<color=green>[GOAP Explore]</color> {worker.CharacterName} ramasse {worldItem.ItemInstance.ItemSO.ItemName} trouvé par terre !");
                return;
            }

            break; // Un seul pickup à la fois
        }
    }

    public override void Exit(Character worker)
    {
        _isComplete = false;
        _explorationAttempts = 0;
        _isMoving = false;
        worker.CharacterMovement?.ResetPath();
    }
}
