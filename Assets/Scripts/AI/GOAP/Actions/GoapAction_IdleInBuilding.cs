using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Action GOAP : Ordonne au worker de retourner à son bâtiment de travail et de flâner/attendre
/// car toutes les ressources demandées par le bâtiment ont été récoltées.
/// </summary>
public class GoapAction_IdleInBuilding : GoapAction
{
    private Vector3 _wanderTarget;
    private bool _isComplete = false;
    private bool _isWalking = false;
    private float _waitTime = 0f;
    private float _maxWaitTime = 5f;
    private GatheringBuilding _building;

    public GoapAction_IdleInBuilding(GatheringBuilding building)
    {
        _building = building;
    }

    public override string ActionName => "IdleInBuilding";

    // Précondition : le worker N'A PAS besoin de travailler (le bâtiment est "plein")
    public override Dictionary<string, bool> Preconditions => new Dictionary<string, bool>
    {
        { "needsToWork", false }
    };

    // Effet : le worker est en train de se reposer/flâner
    public override Dictionary<string, bool> Effects => new Dictionary<string, bool>
    {
        { "isIdling", true }
    };

    // Coût très faible pour que le planner le choisisse facilement quand `needsToWork` est false
    public override float Cost => 0.5f;

    public override bool IsComplete => _isComplete;

    public override bool IsValid(Character worker)
    {
        if (_building == null) return false;
        
        // Valide tant que le bâtiment n'a toujours pas besoin de ressources
        return _building.AreAllRequestedResourcesGathered();
    }

    public override void Execute(Character worker)
    {
        if (_isComplete) return;

        var movement = worker.CharacterMovement;
        if (movement == null) return;

        // Si on n'est pas en train de marcher, on attend un peu puis on choisit une nouvelle destination
        if (!_isWalking)
        {
            _waitTime -= Time.deltaTime;
            
            if (_waitTime <= 0f)
            {
                // Trouver une destination aléatoire DANS la zone du bâtiment (ou autour du centre)
                if (_building.DepositZone != null)
                {
                    _wanderTarget = _building.DepositZone.GetRandomPointInZone();
                }
                else
                {
                    // Fallback autour du centre du bâtiment
                    Vector2 randomCircle = Random.insideUnitCircle * 3f;
                    _wanderTarget = _building.transform.position + new Vector3(randomCircle.x, 0, randomCircle.y);
                }

                movement.Stop();
                movement.SetDestination(_wanderTarget);
                _isWalking = true;
                
                // Set the next wait time randomly between 2 and 6 seconds
                _waitTime = Random.Range(2f, 6f);
            }
        }
        else
        {
            // Vérifier si on est arrivé à la destination de flânerie
            if (!movement.PathPending && (!movement.HasPath || movement.RemainingDistance <= movement.StoppingDistance + 0.5f))
            {
                _isWalking = false;
            }
        }

        // Pendant qu'il flâne, on s'assure qu'il n'est pas en train d'exécuter une autre action (récolte, etc)
        if (worker.CharacterActions != null && worker.CharacterActions.CurrentAction != null)
        {
            worker.CharacterActions.ClearCurrentAction();
        }

        // On ne met jamais IsComplete = true ici. 
        // C'est IsValid() qui bloquera l'action quand le bâtiment aura à nouveau besoin de ressources.
        // Ou le planner qui changera de plan.
    }

    public override void Exit(Character worker)
    {
        _isWalking = false;
        _isComplete = false;
        _waitTime = 0f;
    }
}
