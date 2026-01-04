using UnityEngine;
using UnityEngine.AI;

public class FollowTargetBehaviour : IAIBehaviour
{
    private Character targetCharacter;
    private NavMeshAgent agent;
    private float followDistance;

    public FollowTargetBehaviour(Character target, NavMeshAgent agent, float followDistance = 30f)
    {
        this.targetCharacter = target;
        this.agent = agent;
        this.followDistance = followDistance;
    }

    public void Act(Character self)
    {
        if (targetCharacter == null || agent == null)
            return;

        float distance = Vector3.Distance(agent.transform.position, targetCharacter.transform.position);

        if (distance > followDistance)
        {
            agent.SetDestination(targetCharacter.transform.position);
        }
        else
        {
            agent.ResetPath(); // stoppe le NPC quand il est proche
        }
    }
    public void Exit(Character self)
    {
        // On arrête proprement l'agent pour qu'il ne continue pas vers la cible
        if (agent != null && agent.isOnNavMesh)
        {
            agent.ResetPath();
        }

        Debug.Log($"{self.CharacterName} arrête de suivre sa cible.");
    }
}
