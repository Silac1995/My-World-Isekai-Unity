using UnityEngine;
using UnityEngine.AI;
using System.Linq;

/// <summary>
/// Behaviour injecté juste avant la fin d'un WorkBehaviour afin que 
/// le personnage se déplace physiquement dans les limites de la BuildingZone
/// de son bâtiment pour y valider son départ (Punch Out).
/// </summary>
public class PunchOutBehaviour : IAIBehaviour
{
    private CommercialBuilding _workplace;
    private bool _isFinished = false;
    private bool _isMoving = false;

    public bool IsFinished => _isFinished;

    public PunchOutBehaviour(CommercialBuilding workplace)
    {
        _workplace = workplace;
    }

    public void Enter(Character selfCharacter) { }
    public void Act(Character selfCharacter)
    {
        if (_isFinished) return;

        if (_workplace == null)
        {
            _isFinished = true;
            return;
        }

        var movement = selfCharacter.CharacterMovement;
        if (movement == null)
        {
            _isFinished = true;
            return;
        }

        // Phase de mouvement
        if (!_isMoving)
        {
            Vector3 destination = _workplace.GetRandomPointInBuildingZone(selfCharacter.transform.position.y);
            movement.SetDestination(destination);
            _isMoving = true;
            return;
        }

        // Vérification de l'arrivée
        if (!movement.PathPending && (!movement.HasPath || movement.RemainingDistance <= movement.StoppingDistance + 0.5f))
        {
            // Arrivé -> Punch Out physique
            _workplace.WorkerEndingShift(selfCharacter);
            _isFinished = true; // Permet de passer à la suite de la stack (ex: WanderBehaviour retour maison)
        }
    }

    public void Exit(Character selfCharacter)
    {
        _isMoving = false;
        selfCharacter.CharacterMovement?.ResetPath();
        
        // Sécurité : si le behaviour est coupé de force, on dépointe quand même
        if (!_isFinished && _workplace != null && _workplace.ActiveWorkersOnShift.Contains(selfCharacter))
        {
            _workplace.WorkerEndingShift(selfCharacter);
        }
    }

    public void Terminate()
    {
        _isFinished = true;
    }
}
