using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Action GOAP : Se rendre à la zone de récolte, récolter un GatherableObject,
/// puis ramasser le WorldItem spawné (dans le sac ou dans les mains).
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
    private bool _gatherFinished = false;
    private bool _pickupStarted = false;
    private GatherableObject _currentTarget = null;
    private CharacterGatherAction _gatherAction = null;

    public override bool IsComplete => _isComplete;

    public GoapAction_GatherResources(GatheringBuilding building)
    {
        _building = building;
    }

    public override bool IsValid(Character worker)
    {
        if (_building == null || !_building.HasGatherableZone) return false;

        // Si le worker ne peut plus rien porter (ni sac ni main), l'action est invalide
        var equipment = worker.CharacterEquipment;
        if (equipment != null)
        {
            var hands = worker.CharacterVisual?.BodyPartsController?.HandsController;
            bool handsFree = hands != null && hands.AreHandsFree();
            
            bool bagHasSpace = false;
            // Vérifier s'il y a de la place pour au moins UN des items voulus par le building
            var wantedItems = _building.GetWantedItems();
            if (wantedItems != null && wantedItems.Count > 0)
            {
                foreach (var wantedItem in wantedItems)
                {
                    if (equipment.HasFreeSpaceForItemSO(wantedItem))
                    {
                        bagHasSpace = true;
                        break;
                    }
                }
            }
            else
            {
                // Fallback générique si on ne sait pas quoi chercher
                bagHasSpace = equipment.HasFreeSpaceForMisc();
            }

            if (!handsFree && !bagHasSpace)
            {
                return false;
            }
        }

        return true;
    }

    public override void Execute(Character worker)
    {
        if (_isComplete) return;

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

        // Phase 2 : Trouver un GatherableObject
        if (_currentTarget == null)
        {
            _currentTarget = FindNearestGatherable(worker);
            if (_currentTarget == null)
            {
                Debug.Log($"<color=orange>[GOAP Gather]</color> {worker.CharacterName} : plus de ressources dans la zone.");
                _building.ClearGatherableZone();
                _isComplete = true;
                return;
            }

            Vector3 targetPos = _currentTarget.transform.position;
            movement.SetDestination(targetPos);
            return;
        }

        // Phase 3 : Lancer la CharacterGatherAction quand on est arrivé
        if (!_isGathering)
        {
            if (!movement.PathPending && (!movement.HasPath || movement.RemainingDistance <= movement.StoppingDistance + 1f))
            {
                _isGathering = true;
                _gatherAction = new CharacterGatherAction(worker, _currentTarget);

                _gatherAction.OnActionFinished += () =>
                {
                    _gatherFinished = true;
                    Debug.Log($"<color=cyan>[GOAP Gather]</color> {worker.CharacterName} a fini de récolter, recherche du WorldItem...");
                };

                if (!worker.CharacterActions.ExecuteAction(_gatherAction))
                {
                    Debug.Log($"<color=orange>[GOAP Gather]</color> {worker.CharacterName} ne peut pas lancer la récolte.");
                    _isComplete = true;
                }
            }
            return;
        }

        // Phase 4 : Après la récolte, ramasser le WorldItem spawné
        if (_gatherFinished && !_pickupStarted)
        {
            _pickupStarted = true;
            PickupNearbyWorldItem(worker);
            return;
        }

        // Phase 5 : Attendre que le pickup (CharacterPickUpItem) se termine
        // _isComplete sera mis à true par le callback
    }

    /// <summary>
    /// Cherche un WorldItem au sol proche du worker et le ramasse (sac ou mains).
    /// </summary>
    private void PickupNearbyWorldItem(Character worker)
    {
        WorldItem nearestWorldItem = FindNearestWantedWorldItem(worker);

        if (nearestWorldItem == null)
        {
            Debug.Log($"<color=orange>[GOAP Gather]</color> {worker.CharacterName} : aucun WorldItem trouvé à ramasser.");
            _isComplete = true;
            return;
        }

        ItemInstance itemInstance = nearestWorldItem.ItemInstance;
        if (itemInstance == null)
        {
            _isComplete = true;
            return;
        }

        // Option 1 : Le personnage a un sac avec de la place → CharacterPickUpItem
        var equipment = worker.CharacterEquipment;
        if (equipment != null && equipment.HaveInventory())
        {
            var inventory = equipment.GetInventory();
            if (inventory.HasFreeSpaceForItem(itemInstance))
            {
                var pickupAction = new CharacterPickUpItem(worker, itemInstance, nearestWorldItem.gameObject);
                pickupAction.OnActionFinished += () =>
                {
                    Debug.Log($"<color=green>[GOAP Gather]</color> {worker.CharacterName} a mis {itemInstance.ItemSO.ItemName} dans son sac.");
                    _isComplete = true;
                };

                if (!worker.CharacterActions.ExecuteAction(pickupAction))
                {
                    CarryItemFallback(worker, itemInstance, nearestWorldItem);
                }
                return;
            }
        }

        // Option 2 : Pas de sac ou plein → carry dans les mains
        CarryItemFallback(worker, itemInstance, nearestWorldItem);
    }

    /// <summary>
    /// Fallback : porte l'item dans les mains et détruit le WorldItem au sol.
    /// </summary>
    private void CarryItemFallback(Character worker, ItemInstance itemInstance, WorldItem worldItem)
    {
        var handsController = worker.CharacterVisual?.BodyPartsController?.HandsController;
        if (handsController != null && handsController.AreHandsFree())
        {
            handsController.CarryItem(itemInstance);
            Object.Destroy(worldItem.gameObject);
            Debug.Log($"<color=green>[GOAP Gather]</color> {worker.CharacterName} porte {itemInstance.ItemSO.ItemName} dans ses mains.");
        }
        else
        {
            Debug.Log($"<color=orange>[GOAP Gather]</color> {worker.CharacterName} ne peut ni stocker ni porter l'item.");
        }

        _isComplete = true;
    }

    /// <summary>
    /// Trouve le WorldItem le plus proche contenant un item voulu par le building.
    /// On cherche prioritairement autour de l'objet qu'on vient de récolter pour éviter de piocher dans la deposit zone.
    /// </summary>
    private WorldItem FindNearestWantedWorldItem(Character worker)
    {
        var wantedItems = _building.GetWantedItems();
        if (wantedItems.Count == 0) return null;

        WorldItem nearest = null;
        float nearestDist = float.MaxValue;

        // On cherche autour du joueur
        Vector3 searchCenter = worker.transform.position;
        float searchRadius = 5f; 

        Collider[] colliders = Physics.OverlapSphere(searchCenter, searchRadius);
        foreach (var col in colliders)
        {
            var worldItem = col.GetComponent<WorldItem>() ?? col.GetComponentInParent<WorldItem>();
            if (worldItem == null || worldItem.ItemInstance == null || worldItem.IsBeingCarried) continue;

            // Ignorer si l'item est dans la zone de Deposit
            if (Zone.IsPositionInZoneType(worldItem.transform.position, ZoneType.Deposit)) continue;

            if (wantedItems.Contains(worldItem.ItemInstance.ItemSO))
            {
                float dist = Vector3.Distance(worker.transform.position, worldItem.transform.position);
                if (dist < nearestDist)
                {
                    nearest = worldItem;
                    nearestDist = dist;
                }
            }
        }

        return nearest;
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

        if (!movement.PathPending && (!movement.HasPath || movement.RemainingDistance <= movement.StoppingDistance + 0.5f))
        {
            _arrivedAtZone = true;
            _isMovingToZone = false;
            Debug.Log($"<color=cyan>[GOAP Gather]</color> {worker.CharacterName} est arrivé à la zone de récolte.");
        }
    }

    private GatherableObject FindNearestGatherable(Character worker)
    {
        var wantedItems = _building.GetWantedItems();
        if (wantedItems.Count == 0) return null;

        GatherableObject nearest = null;
        float nearestDist = float.MaxValue;

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
        _gatherFinished = false;
        _pickupStarted = false;
        _currentTarget = null;
        worker.CharacterMovement?.ResetPath();
    }
}
