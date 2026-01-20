using UnityEngine;
using UnityEngine.AI;

public class InteractBehaviour : IAIBehaviour
{
    private bool _hasStoppedAgent = false;
    private bool _isFinished = false;
    public bool IsFinished => _isFinished;

    public void Terminate() => _isFinished = true;

    public void Act(Character self)
    {
        Character target = self.CharacterInteraction.CurrentTarget;

        // Si la cible disparaît, on se termine
        if (target == null)
        {
            _isFinished = true;
            return;
        }

        if (self.Controller.Agent != null && self.Controller.Agent.isOnNavMesh)
        {
            self.Controller.Agent.isStopped = true;
        }

        Vector3 direction = target.transform.position - self.transform.position;
        self.CharacterVisual?.UpdateFlip(direction);
    }

    public void Exit(Character self)
    {
        // On s'assure que les flags sont réinitialisés
        _hasStoppedAgent = false;

        Debug.Log($"{self.CharacterName} sort de l'état d'interaction.");
    }
}