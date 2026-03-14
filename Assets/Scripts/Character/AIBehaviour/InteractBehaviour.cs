using UnityEngine;

public class InteractBehaviour : IAIBehaviour
{
    private bool _isFinished = false;
    private bool _hasStopped = false;
    public bool IsFinished => _isFinished;

    public void Terminate() => _isFinished = true;

    public void Enter(Character selfCharacter) { }
    public void Act(Character selfCharacter)
    {
        Character target = selfCharacter.CharacterInteraction.CurrentTarget;

        if (target == null)
        {
            _isFinished = true;
            return;
        }

        // Premier tick : on coupe net tout mouvement résiduel
        if (!_hasStopped)
        {
            selfCharacter.CharacterMovement?.ResetPath();
            selfCharacter.CharacterMovement?.Stop();
            _hasStopped = true;
        }

        selfCharacter.CharacterMovement?.Stop();
    }

    public void Exit(Character selfCharacter)
    {
        Debug.Log($"{selfCharacter.CharacterName} sort de l'état d'interaction.");
    }
}
