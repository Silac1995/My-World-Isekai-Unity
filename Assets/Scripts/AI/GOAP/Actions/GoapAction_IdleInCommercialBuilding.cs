using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Action GOAP : Ordonne au worker de retourner à son bâtiment de travail et de flâner/attendre
/// (Version générique pour les CommercialBuilding comme la logistique)
/// </summary>
public class GoapAction_IdleInCommercialBuilding : GoapAction
{
    private Vector3 _wanderTarget;
    private bool _isComplete = false;
    private float _nextActionTime = 0f;
    private CommercialBuilding _building;
    private IdleState _currentState = IdleState.PickingDestination;
    private float _waitDuration = 0f;

    private enum IdleState
    {
        PickingDestination,
        Walking,
        Waiting
    }

    public GoapAction_IdleInCommercialBuilding(CommercialBuilding building)
    {
        _building = building;
    }

    public override string ActionName => "Idle In Building";

    public override Dictionary<string, bool> Preconditions => new Dictionary<string, bool>
    {
        // Aucune précondition: on peut toujours se mettre à flâner
    };

    public override Dictionary<string, bool> Effects => new Dictionary<string, bool>
    {
        { "isIdling", true } // Maintain the state
    };

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

        if (_currentState == IdleState.PickingDestination)
        {
            if (Time.time >= _nextActionTime)
            {
                if (_building.BuildingZone != null)
                {
                    Bounds bounds = _building.BuildingZone.bounds;
                    float randomX = Random.Range(bounds.min.x, bounds.max.x);
                    float randomZ = Random.Range(bounds.min.z, bounds.max.z);
                    
                    _wanderTarget = new Vector3(randomX, worker.transform.position.y, randomZ);

                    if (UnityEngine.AI.NavMesh.SamplePosition(_wanderTarget, out UnityEngine.AI.NavMeshHit hit, 10.0f, UnityEngine.AI.NavMesh.AllAreas))
                    {
                        _wanderTarget = hit.position;
                    }
                }
                else
                {
                    Vector2 randomCircle = Random.insideUnitCircle * 3f;
                    _wanderTarget = _building.transform.position + new Vector3(randomCircle.x, 0, randomCircle.y);
                }

                movement.Stop();
                movement.SetDestination(_wanderTarget);
                _currentState = IdleState.Walking;
            }
        }
        else if (_currentState == IdleState.Walking)
        {
            float distToTarget = Vector3.Distance(worker.transform.position, _wanderTarget);
            
            if (!movement.PathPending && (!movement.HasPath || (movement.RemainingDistance <= movement.StoppingDistance + 0.5f && distToTarget <= movement.StoppingDistance + 1f)))
            {
                _waitDuration = Time.time + Random.Range(2f, 4f);
                _currentState = IdleState.Waiting;
            }
        }
        else if (_currentState == IdleState.Waiting)
        {
            if (Time.time >= _waitDuration)
            {
                _currentState = IdleState.PickingDestination;
                _isComplete = true; // Permet au JobLogisticsManager.Execute de replanifier et traiter ses PendingOrders
            }
        }

        // On ne clear l'action courante que si c'est nécessaire pour ne pas spammer OnCancel
        if (worker.CharacterActions != null && worker.CharacterActions.CurrentAction != null)
        {
            worker.CharacterActions.ClearCurrentAction();
        }
    }

    public override void Exit(Character worker)
    {
        _currentState = IdleState.PickingDestination;
        _isComplete = false;
        _nextActionTime = 0f;
        _waitDuration = 0f;
    }
}
