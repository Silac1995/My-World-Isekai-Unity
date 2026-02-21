using UnityEngine;

public class InteractBehaviour : IAIBehaviour
{
    private bool _isFinished = false;
    public bool IsFinished => _isFinished;

    public void Terminate() => _isFinished = true;

    public void Act(Character self)
    {
        Character target = self.CharacterInteraction.CurrentTarget;

        if (target == null)
        {
            _isFinished = true;
            return;
        }

        self.CharacterMovement?.Stop();
        self.CharacterVisual?.FaceCharacter(target);
    }

    public void Exit(Character self)
    {
        Debug.Log($"{self.CharacterName} sort de l'état d'interaction.");
    }
}
