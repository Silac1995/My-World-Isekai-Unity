using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Behaviour qui fait aller le NPC à son lieu de travail et exécute son job.
/// Se déplace vers le CommercialBuilding, puis appelle CharacterJob.Work() en boucle.
/// </summary>
public class WorkBehaviour : IAIBehaviour
{
    private NPCController _npcController;
    private bool _isFinished = false;
    private bool _hasArrived = false;
    private bool _isMoving = false;

    public bool IsFinished => _isFinished;

    public WorkBehaviour(NPCController npcController)
    {
        _npcController = npcController;
    }

    public void Act(Character selfCharacter)
    {
        if (_isFinished) return;

        var characterJob = selfCharacter.CharacterJob;
        if (characterJob == null || !characterJob.HasJob)
        {
            _isFinished = true;
            return;
        }

        var workplace = characterJob.Workplace;
        if (workplace == null)
        {
            _isFinished = true;
            return;
        }

        // Phase 1 : Se déplacer vers le lieu de travail
        if (!_hasArrived)
        {
            MoveToWorkplace(selfCharacter, workplace);
            return;
        }

        // Phase 2 : Travailler
        characterJob.Work();
    }

    private void MoveToWorkplace(Character self, CommercialBuilding workplace)
    {
        var movement = self.CharacterMovement;
        if (movement == null) return;

        // Si on n'a pas encore lancé le déplacement
        if (!_isMoving)
        {
            Vector3 destination = workplace.GetRandomPointInZone();
            movement.SetDestination(destination);
            _isMoving = true;
            return;
        }

        // Vérifier si on est arrivé
        if (!movement.PathPending && (!movement.HasPath || movement.RemainingDistance <= movement.StoppingDistance + 0.5f))
        {
            _hasArrived = true;
            _isMoving = false;
            Debug.Log($"<color=yellow>[Work]</color> {self.CharacterName} est arrivé à {workplace.BuildingName} et commence à travailler.");
        }
    }

    public void Exit(Character selfCharacter)
    {
        _hasArrived = false;
        _isMoving = false;
        selfCharacter.CharacterMovement?.ResetPath();
    }

    public void Terminate() => _isFinished = true;
}
