using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Action GOAP : Déposer les ressources récoltées à la zone de dépôt du building.
/// Le gatherer se déplace vers la depositZone et y dépose les items
/// depuis son sac (inventaire) ou depuis ses mains (carry).
/// </summary>
public class GoapAction_DepositResources : GoapAction
{
    public override string ActionName => "DepositResources";

    public override Dictionary<string, bool> Preconditions => new Dictionary<string, bool>
    {
        { "hasResources", true }
    };

    public override Dictionary<string, bool> Effects => new Dictionary<string, bool>
    {
        { "hasDepositedResources", true }
    };

    public override float Cost => 1f;

    private GatheringBuilding _building;
    private bool _isComplete = false;
    private bool _isMoving = false;

    public override bool IsComplete => _isComplete;

    public GoapAction_DepositResources(GatheringBuilding building)
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

        // Phase 1 : Se déplacer vers la zone de dépôt
        if (!_isMoving)
        {
            Vector3 destination = depositZone.GetRandomPointInZone();
            movement.SetDestination(destination);
            _isMoving = true;
            return;
        }

        // Phase 2 : Vérifier si on est arrivé
        if (!movement.PathPending)
        {
            if (!movement.HasPath)
            {
                // Le chemin a été effacé (ex: par le combat), on doit relancer le déplacement
                _isMoving = false;
            }
            else if (movement.RemainingDistance <= movement.StoppingDistance + 0.5f)
            {
                DepositItems(worker);
                _isComplete = true;
            }
        }
    }

    /// <summary>
    /// Dépose les items depuis le sac (wanted items) et/ou depuis les mains.
    /// Lance l'action CharacterDropItem pour qu'ils soient spawnés physiquement
    /// par le système d'animation et de CharacterAction centralisé.
    /// </summary>
    private void DepositItems(Character worker)
    {
        bool deposited = false;
        var acceptedItems = _building.GetAcceptedItems();

        // 1. Déposer l'item porté dans les mains EN PREMIER (pour libérer l'animation)
        var handsController = worker.CharacterVisual?.BodyPartsController?.HandsController;
        if (handsController != null && handsController.IsCarrying)
        {
            if (handsController.CarriedItem != null && acceptedItems.Contains(handsController.CarriedItem.ItemSO))
            {
                ItemInstance carriedItem = handsController.CarriedItem;
                
                // Exécuter l'action physique de drop (! La première action réussit, les suivantes pourraient échouer si exécutées à la même frame)
                if (worker.CharacterActions.ExecuteAction(new CharacterDropItem(worker, carriedItem)))
                {
                    _building.RegisterGatheredItem(carriedItem.ItemSO);
                    Debug.Log($"<color=green>[GOAP Deposit]</color> {worker.CharacterName} a physiquement lâché {carriedItem.ItemSO.ItemName} (mains) à la zone de dépôt.");
                    deposited = true;
                }
            }
        }

        // 2. Déposer les items acceptés depuis le sac (inventaire)
        var equipment = worker.CharacterEquipment;
        if (equipment != null && equipment.HaveInventory())
        {
            var inventory = equipment.GetInventory();

            // Parcourir les slots et déposer les items acceptés
            for (int i = inventory.ItemSlots.Count - 1; i >= 0; i--)
            {
                var slot = inventory.ItemSlots[i];
                if (slot.IsEmpty()) continue;

                ItemInstance item = slot.ItemInstance;
                if (item == null) continue;

                // Vérifier si c'est un item accepté
                if (acceptedItems.Contains(item.ItemSO))
                {
                    // Lancer l'action de drop.
                    // IMPORTANT : CharacterActions.ExecuteAction ne peut pas stacker 10 actions à la frame 1 si CanExecute() = false pdt une autre action.
                    // Cependant, pour la version actuelle, on tente de drop ce qui est possible, GOAP replanifiera la suite au tick suivant !
                    if (worker.CharacterActions.ExecuteAction(new CharacterDropItem(worker, item)))
                    {
                        _building.RegisterGatheredItem(item.ItemSO);
                        Debug.Log($"<color=green>[GOAP Deposit]</color> {worker.CharacterName} a libéré {item.ItemSO.ItemName} (sac) pour la zone de dépôt.");
                        deposited = true;
                    }
                }
            }
        }

        if (!deposited)
        {
            Debug.LogWarning($"<color=orange>[GOAP Deposit]</color> {worker.CharacterName} n'avait rien à déposer (ou a échoué l'action de drop) !");
        }
    }

    public override void Exit(Character worker)
    {
        _isMoving = false;
        worker.CharacterMovement?.ResetPath();
    }
}
