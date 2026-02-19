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

        Vector3 direction = target.transform.position - self.transform.position;
        self.CharacterVisual?.UpdateFlip(direction);
    }

    public void Exit(Character self)
    {
        Debug.Log($"{self.CharacterName} sort de l'état d'interaction.");
    }
}
