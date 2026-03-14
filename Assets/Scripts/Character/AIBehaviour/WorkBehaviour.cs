using UnityEngine;
using UnityEngine.AI;
using System.Linq;
/// <summary>
/// Behaviour qui s'assure d'abord que le personnage pointe (via PunchInBehaviour),
/// puis exécute le Job (qui gère ses propres phases via GOAP).
/// </summary>
public class WorkBehaviour : IAIBehaviour
{
    private NPCController _npcController;
    private bool _isFinished = false;
    private bool _wasInInteraction = false;
    private bool _hasPunchedIn = false;

    private CommercialBuilding _workplace;
    private PunchInBehaviour _punchInBehaviour;

    public CommercialBuilding Workplace => _workplace;
    public bool IsFinished => _isFinished;

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
            // L'interaction vient de se terminer — on devra s'assurer qu'on n'est pas bloqué
            _wasInInteraction = true;
        }
    }

    public void Enter(Character selfCharacter)
    {
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

        // Si le personnage n'est pas déjà enregistré comme travaillant, on lance le PunchIn
        if (!_workplace.ActiveWorkersOnShift.Contains(selfCharacter))
        {
            _punchInBehaviour = new PunchInBehaviour(_workplace);
            _npcController.PushBehaviour(_punchInBehaviour);
        }
        else
        {
            _hasPunchedIn = true; // Il a déjà pointé
        }
    }

    public void Act(Character selfCharacter)
    {
        if (_isFinished) return;

        var characterJob = selfCharacter.CharacterJob;
        if (characterJob == null || !characterJob.HasJob || _workplace == null)
        {
            _isFinished = true;
            return;
        }

        // Phase 1 : Attendre que le PunchInBehaviour ait terminé son travail (Déplacement + CharacterAction)
        if (!_hasPunchedIn)
        {
            // Dès que le PunchInBehaviour est sorti du summum de la stack
            if (_punchInBehaviour != null && _punchInBehaviour.IsFinished)
            {
                _hasPunchedIn = true;
            }
            else
            {
                return; // On attend encore d'être arrivé physiquement et d'avoir validé Action_PunchIn
            }
        }

        // Sécurité interaction
        if (selfCharacter.CharacterInteraction != null && selfCharacter.CharacterInteraction.IsInteracting)
        {
            return;
        }

        // Après une interaction, on s'assure juste que le pathfinding (qui est relancé par les Action internes au Job) peut reprendre
        if (_wasInInteraction)
        {
            _wasInInteraction = false;
            Debug.Log($"<color=cyan>[Work]</color> {selfCharacter.CharacterName} reprend l'exécution du travail après une interaction.");
            // CharacterGameController a déjà fait Resume() sur le mouvement global
        }

        // Phase 2 : Exécution pure déléguée au Job (qui va faire tourner son Planner)
        // C'est lui qui gérera GoapAction_IdleInCommercialBuilding s'il n'y a rien à faire.
        characterJob.Work();
    }

    public void Exit(Character selfCharacter)
    {
        _wasInInteraction = false;
        _hasPunchedIn = false;
        selfCharacter.CharacterMovement?.ResetPath();

        // Désabonner l'événement d'interaction
        if (selfCharacter.CharacterInteraction != null)
        {
            selfCharacter.CharacterInteraction.OnInteractionStateChanged -= OnInteractionChanged;
        }
        
        // Le PunchOut physique est géré par PunchOutBehaviour, poussé séparément à la fin du schedule
    }

    public void Terminate()
    {
        _isFinished = true;
    }
}
