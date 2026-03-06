using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Action GOAP : Déposer les ressources récoltées à la zone de dépôt du building.
/// Le gatherer se déplace vers la depositZone et y dépose les items.
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
        if (!movement.PathPending && (!movement.HasPath || movement.RemainingDistance <= movement.StoppingDistance + 0.5f))
        {
            // Déposer les ressources
            DepositItems(worker);
            _isComplete = true;
        }
    }

    /// <summary>
    /// Dépose les items dans la zone de dépôt.
    /// Pour l'instant, on enregistre juste les items au building.
    /// TODO: Quand l'inventaire worker est implémenté, transférer les items.
    /// </summary>
    private void DepositItems(Character worker)
    {
        // TODO: Parcourir l'inventaire du worker et déposer les items voulus
        // Pour l'instant, on simule le dépôt
        var wantedItems = _building.GetWantedItems();
        if (wantedItems.Count > 0)
        {
            // Simuler le dépôt du premier item voulu
            ItemSO depositedItem = wantedItems[0];
            _building.RegisterGatheredItem(depositedItem);
            Debug.Log($"<color=green>[GOAP Deposit]</color> {worker.CharacterName} a déposé {depositedItem.ItemName} à la zone de dépôt.");
        }
    }

    public override void Exit(Character worker)
    {
        _isMoving = false;
        worker.CharacterMovement?.ResetPath();
    }
}
