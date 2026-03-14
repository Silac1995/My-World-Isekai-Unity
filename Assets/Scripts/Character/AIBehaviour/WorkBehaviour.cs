using UnityEngine;
using UnityEngine.AI;

public enum WorkPhase
{
    MovingToWorkplace,
    Working,
    OnBreak
}

/// <summary>
/// Behaviour qui fait aller le NPC à son lieu de travail et exécute son job.
/// Pointe à l'arrivée, s'occupe de faire travailler ou flâner le personnage en pause, puis dépointe.
/// </summary>
public class WorkBehaviour : IAIBehaviour
{
    private NPCController _npcController;
    private bool _isFinished = false;
    private bool _isMoving = false;
    private bool _wasInInteraction = false;

    private WorkPhase _currentPhase = WorkPhase.MovingToWorkplace;
    private CommercialBuilding _workplace;

    public CommercialBuilding Workplace => _workplace;
    public bool IsFinished => _isFinished;
    public bool IsOnBreak => _currentPhase == WorkPhase.OnBreak;
    public WorkPhase CurrentPhase => _currentPhase;

    public WorkBehaviour(NPCController npcController)
    {
        _npcController = npcController;

        // S'abonner aux changements d'interaction pour savoir quand reprendre le poste
        var character = npcController.Character;
        if (character?.CharacterInteraction != null)
        {
            character.CharacterInteraction.OnInteractionStateChanged += OnInteractionChanged;
        }
    }

    private void OnInteractionChanged(Character target, bool started)
    {
        if (!started)
        {
            // L'interaction vient de se terminer — on devra reprendre le poste
            _wasInInteraction = true;
        }
    }

    public void Enter(Character selfCharacter) { }
    public void Act(Character selfCharacter)
    {
        if (_isFinished) return;

        var characterJob = selfCharacter.CharacterJob;
        if (characterJob == null || !characterJob.HasJob)
        {
            _isFinished = true;
            return;
        }

        _workplace = characterJob.Workplace;
        if (_workplace == null)
        {
            _isFinished = true;
            return;
        }

        // Phase 1 : Se déplacer vers le lieu de travail
        if (_currentPhase == WorkPhase.MovingToWorkplace)
        {
            MoveToWorkplace(selfCharacter, _workplace);
            return;
        }

        // Sécurité : si le personnage est en interaction, on ne fait rien
        // (il sera freeze/unfreeze par CharacterInteraction)
        if (selfCharacter.CharacterInteraction != null && selfCharacter.CharacterInteraction.IsInteracting)
        {
            return;
        }

        // Après une interaction, le personnage a pu être déplacé.
        // On vérifie s'il est encore dans la zone de travail et on le renvoie si nécessaire.
        if (_wasInInteraction)
        {
            _wasInInteraction = false;
            _currentPhase = WorkPhase.MovingToWorkplace;
            _isMoving = false;
            Debug.Log($"<color=cyan>[Work]</color> {selfCharacter.CharacterName} reprend son poste après une interaction.");
            return;
        }

        // Phase 2 : Vérifier l'état du travail en cours
        bool hasWork = characterJob.CurrentJob != null && characterJob.CurrentJob.HasWorkToDo();

        if (hasWork && _currentPhase == WorkPhase.OnBreak)
        {
            // Reprendre le travail après une pause
            Debug.Log($"<color=cyan>[Work]</color> {selfCharacter.CharacterName} reprend le travail (Il y a nouveau du travail à faire).");
            _currentPhase = WorkPhase.Working;
        }
        else if (!hasWork && _currentPhase == WorkPhase.Working)
        {
            // Mettre en pause car plus rien à faire
            Debug.Log($"<color=magenta>[Work Break]</color> {selfCharacter.CharacterName} n'a plus rien à faire et prend sa pause sur son lieu de travail.");
            _currentPhase = WorkPhase.OnBreak;
        }

        // Pendant la phase de travail OU de pause, le Job s'exécute pour générer du planner
        // (Ex: planifier l'IdleInBuilding pendant la pause)
        characterJob.Work();
    }

    private void MoveToWorkplace(Character self, CommercialBuilding workplace)
    {
        var movement = self.CharacterMovement;
        if (movement == null) return;

        // Si on n'a pas encore lancé le déplacement
        if (!_isMoving)
        {
            Vector3 destination = workplace.GetWorkPosition(self);
            movement.SetDestination(destination);
            _isMoving = true;
            return;
        }

        // Vérifier si on est arrivé
        if (!movement.PathPending && (!movement.HasPath || movement.RemainingDistance <= movement.StoppingDistance + 0.5f))
        {
            _currentPhase = WorkPhase.Working;
            _isMoving = false;

            // Arrivé -> Punch In
            workplace.WorkerStartingShift(self);
        }
    }

    public void Exit(Character selfCharacter)
    {
        _currentPhase = WorkPhase.MovingToWorkplace;
        _isMoving = false;
        _wasInInteraction = false;
        selfCharacter.CharacterMovement?.ResetPath();

        // Désabonner l'événement d'interaction
        if (selfCharacter.CharacterInteraction != null)
        {
            selfCharacter.CharacterInteraction.OnInteractionStateChanged -= OnInteractionChanged;
        }
        
        // On ne fait plus WorkerEndingShift ici car le PunchOut physique est géré par PunchOutBehaviour
    }

    public void Terminate()
    {
        _isFinished = true;
    }
}
