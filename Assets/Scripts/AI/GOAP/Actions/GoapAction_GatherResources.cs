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
    private bool _arrivedAtSpawnedItem = false;
    private bool _pickupStarted = false;
    private GatherableObject _currentTarget = null;
    private WorldItem _targetWorldItem = null;
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

        // Phase 2 : Trouver une cible (Item au sol en priorité, ou Arbre/Ressource)
        if (_currentTarget == null && _targetWorldItem == null)
        {
            // 2a. Chercher d'abord un item par terre dans la zone de récolte
            _targetWorldItem = FindLooseWantedWorldItemInZone(worker);

            if (_targetWorldItem != null)
            {
                Debug.Log($"<color=cyan>[GOAP Gather]</color> {worker.CharacterName} a vu {_targetWorldItem.ItemInstance.ItemSO.ItemName} par terre, il va le ramasser.");

                Vector3 targetPos = _targetWorldItem.transform.position;
                var itemInteractable = _targetWorldItem.ItemInteractable;
                if (itemInteractable != null && itemInteractable.InteractionZone != null)
                {
                    targetPos = itemInteractable.InteractionZone.bounds.ClosestPoint(worker.transform.position);
                }
                else
                {
                    Collider col = _targetWorldItem.GetComponentInChildren<Collider>();
                    if (col != null && !col.isTrigger)
                    {
                        targetPos = col.bounds.ClosestPoint(worker.transform.position);
                    }
                }
                
                movement.SetDestination(targetPos);
                return;
            }

            // 2b. Si rien par terre, chercher un objet à récolter (Arbre, etc.)
            _currentTarget = FindNearestGatherable(worker);
            if (_currentTarget == null)
            {
                Debug.Log($"<color=orange>[GOAP Gather]</color> {worker.CharacterName} : plus de ressources dans la zone.");
                _building.ClearGatherableZone();
                _isComplete = true;
                return;
            }

            Vector3 gatherPos = _currentTarget.transform.position;
            if (_currentTarget.InteractionZone != null)
            {
                gatherPos = _currentTarget.InteractionZone.bounds.ClosestPoint(worker.transform.position);
            }
            movement.SetDestination(gatherPos);
            return;
        }

        // Phase 2.5 : L'employé va ramasser l'objet libre qu'il a ciblé
        if (_targetWorldItem != null)
        {
            if (!movement.PathPending)
            {
                if (!movement.HasPath) 
                {
                    // Chemin effacé : on annule la cible et on recommence la recherche
                    _targetWorldItem = null;
                    return;
                }
                else
                {
                    bool isAtWorldItem = false;
                    var workerCol = worker.GetComponent<Collider>();
                    var itemInteractable = _targetWorldItem.ItemInteractable;

                    if (itemInteractable != null && itemInteractable.InteractionZone != null && workerCol != null)
                    {
                        isAtWorldItem = itemInteractable.InteractionZone.bounds.Intersects(workerCol.bounds);
                    }
                    else
                    {
                        isAtWorldItem = movement.RemainingDistance <= movement.StoppingDistance + 0.5f;
                    }

                    // Fallback NavMesh si l'intersection physique est bloquée
                    if (!isAtWorldItem && movement.RemainingDistance <= movement.StoppingDistance + 0.5f)
                    {
                        isAtWorldItem = true;
                    }

                    if (isAtWorldItem && !_pickupStarted)
                    {
                        movement.Stop();
                        _pickupStarted = true;
                        PickupSpecificWorldItem(worker, _targetWorldItem);
                    }
                }
            }
            return; // On attend que le pickup finisse (mettra IsComplete à true)
        }

        // Phase 3 : Lancer la CharacterGatherAction quand on est arrivé à l'objet à récolter
        if (!_isGathering && _currentTarget != null)
        {
            if (!movement.PathPending)
            {
                if (!movement.HasPath)
                {
                    // Chemin effacé : on annule la cible et on recommence
                    _currentTarget = null;
                    return;
                }
                else
                {
                    // Vérification robuste : on attend d'être physiquement dans l'InteractionZone
                    bool isAtTarget = false;
                    var workerCol = worker.GetComponent<Collider>();

                    if (_currentTarget.InteractionZone != null && workerCol != null)
                    {
                        isAtTarget = _currentTarget.InteractionZone.bounds.Intersects(workerCol.bounds);
                    }
                    else
                    {
                        isAtTarget = movement.RemainingDistance <= movement.StoppingDistance + 1f;
                    }

                    // Fallback NavMesh si l'intersection physique est bloquée
                    if (!isAtTarget && movement.RemainingDistance <= movement.StoppingDistance + 0.5f)
                    {
                        isAtTarget = true;
                    }

                    if (isAtTarget)
                    {
                        // S'assurer de faire face à la cible
                        worker.transform.LookAt(new Vector3(_currentTarget.transform.position.x, worker.transform.position.y, _currentTarget.transform.position.z));
                        
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
                            _isComplete = true; // This ends the action!
                        }
                    }
                }
            }
            return;
        }

        // Reprise sur interruption : L'action a été annulée (ex: par le combat)
        if (_isGathering && !_gatherFinished)
        {
            if (worker.CharacterActions.CurrentAction != _gatherAction)
            {
                Debug.Log($"<color=red>[GOAP Gather]</color> {worker.CharacterName} : La récolte a été interrompue. Action annulée et on réinitialise l'objectif.");
                _isComplete = true; 
            }
            return;
        }

        // Phase 4 : Après la récolte, chercher le WorldItem spawné et s'y diriger
        if (_gatherFinished && !_arrivedAtSpawnedItem)
        {
            if (_targetWorldItem == null)
            {
                _targetWorldItem = FindNearestWantedWorldItem(worker);
                if (_targetWorldItem == null)
                {
                    Debug.Log($"<color=orange>[GOAP Gather]</color> {worker.CharacterName} : aucun WorldItem trouvé à ramasser après la récolte.");
                    _isComplete = true;
                    return;
                }
                
                Vector3 targetPos = _targetWorldItem.transform.position;
                var itemInteractable = _targetWorldItem.ItemInteractable;
                if (itemInteractable != null && itemInteractable.InteractionZone != null)
                {
                    targetPos = itemInteractable.InteractionZone.bounds.ClosestPoint(worker.transform.position);
                }
                else
                {
                    Collider col = _targetWorldItem.GetComponentInChildren<Collider>();
                    if (col != null && !col.isTrigger)
                    {
                        targetPos = col.bounds.ClosestPoint(worker.transform.position);
                    }
                }
                movement.SetDestination(targetPos);
            }

            // Phase 4.5 : Attendre d'arriver au WorldItem fraîchement spawné
            if (!movement.PathPending)
            {
                bool isAtWorldItem = false;
                var workerCol = worker.GetComponent<Collider>();

                var itemInteractable = _targetWorldItem.ItemInteractable;
                if (itemInteractable != null && itemInteractable.InteractionZone != null && workerCol != null)
                {
                    isAtWorldItem = itemInteractable.InteractionZone.bounds.Intersects(workerCol.bounds);
                }
                else
                {
                    isAtWorldItem = movement.RemainingDistance <= movement.StoppingDistance + 0.5f;
                }

                if (isAtWorldItem)
                {
                    movement.Stop();
                    _arrivedAtSpawnedItem = true;
                }
            }
            return;
        }

        // Phase 5 : On est devant le WorldItem spawné, on déclenche le pickup
        if (_arrivedAtSpawnedItem && !_pickupStarted)
        {
            _pickupStarted = true;
            PickupSpecificWorldItem(worker, _targetWorldItem);
            return;
        }

        // Phase 6 : Attendre que le pickup (CharacterPickUpItem) se termine
        // _isComplete sera mis à true par le callback
        if (_pickupStarted)
        {
            if (worker.CharacterActions.CurrentAction == null || !(worker.CharacterActions.CurrentAction is CharacterPickUpItem))
            {
                // Si l'action a été effacée sans le callback (interrompue par dégâts), terminer dans le doute
                if (!_isComplete) 
                {
                    Debug.Log($"<color=red>[GOAP Gather]</color> {worker.CharacterName} : Le ramassage a été interrompu ! Fausse complétion.");
                    _isComplete = true;
                }
            }
        }
    }

    /// <summary>
    /// Cherche un WorldItem au sol proche du worker après avoir récolté un objet.
    /// (Méthode conservée pour la compatibilité interne)
    /// </summary>
    private void PickupNearbyWorldItem(Character worker)
    {
        WorldItem nearestWorldItem = FindNearestWantedWorldItem(worker);

        if (nearestWorldItem == null)
        {
            Debug.Log($"<color=orange>[GOAP Gather]</color> {worker.CharacterName} : aucun WorldItem trouvé à ramasser après la récolte.");
            _isComplete = true;
            return;
        }

        PickupSpecificWorldItem(worker, nearestWorldItem);
    }

    /// <summary>
    /// Ramasse un WorldItem bien précis (dans le sac ou les mains).
    /// Met _isComplete à true à la fin.
    /// </summary>
    private void PickupSpecificWorldItem(Character worker, WorldItem worldItem)
    {
        ItemInstance itemInstance = worldItem.ItemInstance;
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
                var pickupAction = new CharacterPickUpItem(worker, itemInstance, worldItem.gameObject);
                pickupAction.OnActionFinished += () =>
                {
                    Debug.Log($"<color=green>[GOAP Gather]</color> {worker.CharacterName} a mis {itemInstance.ItemSO.ItemName} dans son sac.");
                    _isComplete = true;
                };

                if (!worker.CharacterActions.ExecuteAction(pickupAction))
                {
                    CarryItemFallback(worker, itemInstance, worldItem);
                }
                return;
            }
        }

        // Option 2 : Pas de sac ou plein → carry dans les mains
        CarryItemFallback(worker, itemInstance, worldItem);
    }

    /// <summary>
    /// Fallback : Tente de ramasser l'item via l'action standard (qui gèrera les mains si le sac est plein).
    /// </summary>
    private void CarryItemFallback(Character worker, ItemInstance itemInstance, WorldItem worldItem)
    {
        var pickupAction = new CharacterPickUpItem(worker, itemInstance, worldItem.gameObject);
        if (worker.CharacterActions.ExecuteAction(pickupAction))
        {
            Debug.Log($"<color=green>[GOAP Gather]</color> {worker.CharacterName} ramasse {itemInstance.ItemSO.ItemName}.");
            pickupAction.OnActionFinished += () =>
            {
                _isComplete = true;
            };
        }
        else
        {
            Debug.Log($"<color=orange>[GOAP Gather]</color> {worker.CharacterName} ne peut ni stocker ni porter l'item.");
            _isComplete = true;
        }
    }

    /// <summary>
    /// Cherche s'il existe des WorldItems demandés qui traînent au sol DANS la GatheringZone.
    /// Utilise le même OverlapBox que le bâtiment lui-même pour garantir la détection,
    /// ou un grand rayon autour du worker en cas d'absence de zone.
    /// </summary>
    private WorldItem FindLooseWantedWorldItemInZone(Character worker)
    {
        var wantedItems = _building.GetWantedItems();
        if (wantedItems.Count == 0) return null;

        Collider[] colliders = new Collider[0];
        BoxCollider boxCol = _building.GatherableZone?.GetComponent<BoxCollider>();

        if (boxCol != null)
        {
            Vector3 center = boxCol.transform.TransformPoint(boxCol.center);
            Vector3 halfExtents = Vector3.Scale(boxCol.size, boxCol.transform.lossyScale) * 0.5f;
            colliders = Physics.OverlapBox(center, halfExtents, boxCol.transform.rotation, Physics.AllLayers, QueryTriggerInteraction.Collide);
        }
        else
        {
            colliders = Physics.OverlapSphere(worker.transform.position, 30f);
        }

        return GetNearestValidWorldItem(worker, colliders, wantedItems);
    }

    /// <summary>
    /// Trouve le WorldItem le plus proche contenant un item voulu par le building.
    /// Utilisé UNIQUEMENT pour ramasser la ressource qui Vient de spawner après avoir cassé un arbre/rocher.
    /// Donc le rayon est petit.
    /// </summary>
    private WorldItem FindNearestWantedWorldItem(Character worker)
    {
        var wantedItems = _building.GetWantedItems();
        if (wantedItems.Count == 0) return null;

        float searchRadius = 5f; 
        Collider[] colliders = Physics.OverlapSphere(worker.transform.position, searchRadius);

        return GetNearestValidWorldItem(worker, colliders, wantedItems);
    }

    /// <summary>
    /// Méthode utilitaire appelée par les autres fonctions de recherche,
    /// itère sur une liste de colliders et filtre les WorldItems valides et ramassables,
    /// puis retourne le plus proche.
    /// </summary>
    private WorldItem GetNearestValidWorldItem(Character worker, Collider[] colliders, List<ItemSO> wantedItems)
    {
        WorldItem nearest = null;
        float nearestDist = float.MaxValue;

        foreach (var col in colliders)
        {
            var worldItem = col.GetComponent<WorldItem>() ?? col.GetComponentInParent<WorldItem>();
            if (worldItem == null || worldItem.ItemInstance == null || worldItem.IsBeingCarried) continue;

            // Ignorer si l'item se trouve dans une Deposit Zone locale
            if (Zone.IsPositionInZoneType(worldItem.transform.position, ZoneType.Deposit)) continue;

            // Ignorer l'objet au sol si on n'a pas la place précise de le stocker
            if (worker.CharacterEquipment != null && !worker.CharacterEquipment.CanCarryItemAnyMore(worldItem.ItemInstance)) continue;

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
            // NEW CHECK : Si on est DÉJÀ dans la zone, on skip la marche d'entrée
            BoxCollider box = _building.GatherableZone.GetComponent<BoxCollider>();
            if (box != null && box.bounds.Contains(worker.transform.position))
            {
                _arrivedAtZone = true;
                return;
            }

            Vector3 destination = _building.GatherableZone.GetRandomPointInZone();
            movement.SetDestination(destination);
            _isMovingToZone = true;
            return;
        }

        if (!movement.PathPending)
        {
            if (!movement.HasPath)
            {
                _isMovingToZone = false; // Path was cleared, restart journey
            }
            else if (movement.RemainingDistance <= movement.StoppingDistance + 0.5f)
            {
                _arrivedAtZone = true;
                _isMovingToZone = false;
                Debug.Log($"<color=cyan>[GOAP Gather]</color> {worker.CharacterName} est arrivé à la zone de récolte.");
            }
        }
    }

    private GatherableObject FindNearestGatherable(Character worker)
    {
        var wantedItems = _building.GetWantedItems();
        if (wantedItems.Count == 0) return null;

        GatherableObject nearest = null;
        float nearestDist = float.MaxValue;

        Collider[] colliders = new Collider[0];

        if (_building.GatherableZone != null)
        {
            BoxCollider boxCol = _building.GatherableZone.GetComponent<BoxCollider>();
            if (boxCol != null)
            {
                Vector3 center = boxCol.transform.TransformPoint(boxCol.center);
                Vector3 halfExtents = Vector3.Scale(boxCol.size, boxCol.transform.lossyScale) * 0.5f;
                colliders = Physics.OverlapBox(center, halfExtents, boxCol.transform.rotation, Physics.AllLayers, QueryTriggerInteraction.Collide);
            }
        }
        else
        {
            // Fallback (ne devrait normalement pas arriver si HasGatherableZone est respecté)
            colliders = Physics.OverlapSphere(worker.transform.position, 30f);
        }

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
        _targetWorldItem = null;
        worker.CharacterMovement?.ResetPath();
    }
}
