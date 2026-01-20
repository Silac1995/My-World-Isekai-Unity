using UnityEngine;
using UnityEngine.AI;

public class FollowTargetBehaviour : IAIBehaviour
{
    private Character _targetCharacter;
    private NavMeshAgent _agent;
    private float _followDistance;
    private bool _isFinished = false;

    public bool IsFinished => _isFinished;

    public FollowTargetBehaviour(Character target, NavMeshAgent agent, float followDistance = 50f)
    {
        _targetCharacter = target;
        _agent = agent;
        _followDistance = followDistance;

        // On s'assure que le stoppingDistance de l'agent est petit 
        // pour que ce soit NOTRE code qui gère l'arrêt
        if (_agent != null) _agent.stoppingDistance = 0.5f;
    }

    public void Terminate() => _isFinished = true;

    public void Act(Character self)
    {
        if (_targetCharacter == null || _agent == null || !_agent.isOnNavMesh || _isFinished)
            return;

        float distance = Vector3.Distance(self.transform.position, _targetCharacter.transform.position);

        // Si on est plus loin que la distance voulue
        if (distance > _followDistance)
        {
            // On ne met à jour la destination que si la cible a bougé (optimisation)
            if (Vector3.Distance(_agent.destination, _targetCharacter.transform.position) > 0.5f)
            {
                _agent.isStopped = false;
                _agent.SetDestination(_targetCharacter.transform.position);
            }
        }
        else
        {
            // --- ARRÊT MANUEL ---
            // Si on est dans la zone de confort, on vide le chemin.
            // Ton Controller verra que l'agent n'a plus de chemin et le stoppera proprement.
            if (_agent.hasPath)
            {
                _agent.ResetPath();
                _agent.velocity = Vector3.zero;
            }

            // Visuel : Toujours regarder vers la cible même à l'arrêt
            Vector3 direction = _targetCharacter.transform.position - self.transform.position;
            self.CharacterVisual?.UpdateFlip(direction);
        }
    }

    public void Exit(Character self)
    {
        if (_agent != null && _agent.isOnNavMesh)
        {
            _agent.ResetPath();
        }
        Debug.Log($"{self.CharacterName} arrête de suivre sa cible.");
    }
}