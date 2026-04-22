using System.Collections.Generic;
using UnityEngine;
using MWI.AI;

/// <summary>
/// Action GOAP : Déposer les ressources récoltées à la zone de dépôt du building.
/// Le harvester se déplace vers la depositZone et y dépose les items
/// depuis son sac (inventaire) ou depuis ses mains (carry).
/// </summary>
public class GoapAction_DepositResources : GoapAction
{
    public override string ActionName => "DepositResources";
    public override float Cost => 1f;

    public override Dictionary<string, bool> Preconditions => new Dictionary<string, bool>
    {
        { "hasResources", true }
    };

    public override Dictionary<string, bool> Effects => new Dictionary<string, bool>
    {
        { "hasDepositedResources", true }
    };

    private HarvestingBuilding _building;
    private bool _isComplete = false;
    private bool _isMoving = false;
    private bool _isDepositing = false;
    private float _lastRouteRequestTime;

    public override bool IsComplete => _isComplete;

    public GoapAction_DepositResources(HarvestingBuilding building)
    {
        _building = building;
    }

    public override bool IsValid(Character worker)
    {
        return _building != null && _building.DepositZone != null;
    }

    public override void Execute(Character worker)
    {
        if (_isComplete) return;

        var movement = worker.CharacterMovement;
        if (movement == null)
        {
            _isComplete = true;
            return;
        }

        Zone depositZone = _building.DepositZone;
        if (depositZone == null)
        {
            Debug.LogWarning($"<color=red>[GOAP Deposit]</color> {worker.CharacterName} : pas de zone de dépôt configurée !");
            _isComplete = true;
            return;
        }

        Vector3 targetCenter = depositZone.GetComponent<Collider>().bounds.center;
        
        bool isCloseEnough = false;
        Vector3 workerPosFlat = new Vector3(worker.transform.position.x, 0, worker.transform.position.z);
        Vector3 targetPosFlat = new Vector3(targetCenter.x, 0, targetCenter.z);
        
        if (Vector3.Distance(workerPosFlat, targetPosFlat) <= 2.5f) 
        {
            isCloseEnough = true;
        }
        else if (depositZone.GetComponent<Collider>().bounds.Contains(worker.transform.position) && Vector3.Distance(workerPosFlat, targetPosFlat) <= 4.0f)
        {
            isCloseEnough = true;
        }
        
        // Try precise NavMesh state if bounds check failed
        if (!isCloseEnough) isCloseEnough = _isMoving && NavMeshUtility.HasAgentReachedDestination(movement, 0.5f);

        if (!isCloseEnough)
        {
            if (_isMoving)
            {
                bool hasPathFailed = NavMeshUtility.HasPathFailed(movement, _lastRouteRequestTime, 0.2f);
                if (hasPathFailed)
                {
                    bool blacklisted = worker.PathingMemory.RecordFailure(depositZone.gameObject.GetInstanceID());
                    if (blacklisted)
                    {
                        movement.Stop();
                        movement.ResetPath();
                        _isMoving = false;
                        _isComplete = true; // Complete prematurely to abort
                        return;
                    }
                    _isMoving = false; // Force recalculation
                }
            }

            if (!_isMoving)
            {
                // Navigate to the center of the zone rather than the edge to ensure the item stays in
                movement.SetDestination(targetCenter);
                
                _lastRouteRequestTime = UnityEngine.Time.unscaledTime;
                _isMoving = true;
            }
        }
        else
        {
            if (_isMoving)
            {
                movement.Stop();
                movement.ResetPath();
                _isMoving = false;
            }

            // Flip unconditionally when in range — previously gated on _isMoving,
            // which stranded harvesters that started the action already inside the
            // deposit zone (replan, short-distance plan, spawn adjacent, etc.).
            _isDepositing = true;
            ProcessSequentialDeposit(worker);
        }
    }

    /// <summary>
    /// Dépose séquentiellement les items depuis les mains puis le sac.
    /// Évalué à chaque frame : attend que l'action CharacterDropItem en cours se termine
    /// avant de déclencher la suivante, évitant le rejet par CharacterActions.
    /// </summary>
    private void ProcessSequentialDeposit(Character worker)
    {
        // 1. Si une action de drop est DÉJÀ en cours, on attend qu'elle finisse.
        if (worker.CharacterActions.CurrentAction is CharacterDropItem)
        {
            return;
        }

        var acceptedItems = _building.GetAcceptedItems();

        // 2. Vérifier les mains en priorité
        var handsController = worker.CharacterVisual?.BodyPartsController?.HandsController;
        if (handsController != null && handsController.IsCarrying)
        {
            if (handsController.CarriedItem != null && acceptedItems.Contains(handsController.CarriedItem.ItemSO))
            {
                ItemInstance carriedItem = handsController.CarriedItem;
                
                var dropAction = new CharacterDropItem(worker, carriedItem);
                dropAction.OnActionFinished += () => 
                {
                    _building.RegisterHarvestedItem(carriedItem.ItemSO);
                    Debug.Log($"<color=green>[GOAP Deposit]</color> {worker.CharacterName} a physiquement lâché {carriedItem.ItemSO.ItemName} (mains).");
                };

                worker.CharacterActions.ExecuteAction(dropAction);
                return; // On attend la frame suivante pour revérifier l'état
            }
        }

        // 3. Vérifier le sac
        var equipment = worker.CharacterEquipment;
        if (equipment != null && equipment.HaveInventory())
        {
            var inventory = equipment.GetInventory();

            // Trouver LE PROCHAIN item valide à déposer
            for (int i = inventory.ItemSlots.Count - 1; i >= 0; i--)
            {
                var slot = inventory.ItemSlots[i];
                if (slot.IsEmpty()) continue;

                ItemInstance item = slot.ItemInstance;
                if (item == null) continue;

                if (acceptedItems.Contains(item.ItemSO))
                {
                    var dropAction = new CharacterDropItem(worker, item);
                    dropAction.OnActionFinished += () => 
                    {
                        _building.RegisterHarvestedItem(item.ItemSO);
                        Debug.Log($"<color=green>[GOAP Deposit]</color> {worker.CharacterName} a physiquement lâché {item.ItemSO.ItemName} (sac).");
                    };

                    worker.CharacterActions.ExecuteAction(dropAction);
                    return; // On lance 1 seul item, on sort et on attend la frame suivante.
                }
            }
        }

        // 4. Si le code arrive ici, c'est que `CurrentAction` n'est pas un Drop ET qu'aucun item valide n'a été trouvé.
        // Cela signifie que nous avons fini de tout vider.
        Debug.Log($"<color=cyan>[GOAP Deposit]</color> {worker.CharacterName} a terminé de déposer toutes ses ressources valides.");
        _isComplete = true;
    }

    public override void Exit(Character worker)
    {
        _isComplete = false;
        _isMoving = false;
        _isDepositing = false;
        worker.CharacterMovement?.Stop();
        worker.CharacterMovement?.ResetPath();
    }
}
