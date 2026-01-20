using UnityEngine;

public class IdleBehaviour : IAIBehaviour
{
    private bool _isStopped = false;
    private bool _isFinished = false;
    public bool IsFinished => _isFinished;

    public void Terminate() => _isFinished = true;

    public void Act(Character self)
    {
        // 1. Immobilisation unique au démarrage du comportement
        if (!_isStopped)
        {
            var controller = self.GetComponent<CharacterGameController>();
            if (controller != null && controller.Agent != null && controller.Agent.isOnNavMesh)
            {
                controller.Agent.ResetPath();
                controller.Agent.velocity = Vector3.zero; // Stop immédiat pour l'Animator
            }
            _isStopped = true;
        }

        // 2. Emplacement pour les animations Idle aléatoires
        // C'est ici que tu pourras déclencher des petits mouvements 
        // d'oreilles ou de respiration via ton rig.
        // Ex: if (Random.value < 0.01f) self.Animator.SetTrigger("TwitchEar");
    }

    public void Exit(Character self)
    {
        // On réinitialise l'état pour la prochaine fois qu'il sera Idle
        _isStopped = false;
        Debug.Log($"{self.CharacterName} n'est plus en Idle.");
    }
}