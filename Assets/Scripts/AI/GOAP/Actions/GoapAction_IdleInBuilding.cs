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
    private bool _hasChosenDestination = false;
    private float _waitTime = 0f;
    private float _maxWaitTime = 5f;
    private HarvestingBuilding _building;

    public GoapAction_IdleInBuilding(HarvestingBuilding building)
    {
        _building = building;
    }

    public override string ActionName => "IdleInBuilding";

    public override Dictionary<string, bool> Preconditions => new Dictionary<string, bool>();

    // Effet : le worker est en train de se reposer/flâner
    public override Dictionary<string, bool> Effects => new Dictionary<string, bool>
    {
        { "isIdling", true }
    };

    // Coût très faible pour que le planner le choisisse facilement quand le but est d'idling
    public override float Cost => 0.5f;

    public override bool IsComplete => _isComplete;

    public override bool IsValid(Character worker)
    {
        return _building != null;
    }

    public override void Execute(Character worker)
    {
        if (_isComplete) return;

        var movement = worker.CharacterMovement;
        if (movement == null) return;

        if (!_hasChosenDestination)
        {
            // Trouver une destination aléatoire DANS la zone du bâtiment (ou DepositZone en fallback)
            if (_building.BuildingZone != null)
            {
                Bounds bounds = _building.BuildingZone.bounds;
                float randomX = Random.Range(bounds.min.x, bounds.max.x);
                float randomZ = Random.Range(bounds.min.z, bounds.max.z);
                
                // Utiliser le Y du worker pour éviter d'être bloqué dans le toit du bâtiment
                _wanderTarget = new Vector3(randomX, worker.transform.position.y, randomZ);

                // S'assurer qu'on est sur le NavMesh avec un grand rayon de recherche
                if (UnityEngine.AI.NavMesh.SamplePosition(_wanderTarget, out UnityEngine.AI.NavMeshHit hit, 10.0f, UnityEngine.AI.NavMesh.AllAreas))
                {
                    _wanderTarget = hit.position;
                }
            }
            else if (_building.DepositZone != null)
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
            _hasChosenDestination = true;
        }
        else if (_isWalking)
        {
            // Vérifier si on est arrivé à la destination de flânerie
            if (!movement.PathPending && (!movement.HasPath || movement.RemainingDistance <= movement.StoppingDistance + 0.5f))
            {
                _isWalking = false;
                _waitTime = Random.Range(2f, 6f);
            }
        }
        else
        {
            // Attente sur place
            _waitTime -= Time.deltaTime;
            if (_waitTime <= 0f)
            {
                _isComplete = true; // Termine l'action pour permettre au planner de réévaluer le quota/les arbres
            }
        }

        // Pendant qu'il flâne, on s'assure qu'il n'est pas en train d'exécuter une autre action (récolte, etc)
        if (worker.CharacterActions != null && worker.CharacterActions.CurrentAction != null)
        {
            worker.CharacterActions.ClearCurrentAction();
        }
    }

    public override void Exit(Character worker)
    {
        _isWalking = false;
        _isComplete = false;
        _hasChosenDestination = false;
        _waitTime = 0f;
        worker.CharacterMovement?.Stop();
        worker.CharacterMovement?.ResetPath();
    }
}
