using UnityEngine;
using UnityEngine.AI;
using System.Collections;

public class WanderBehaviour : IAIBehaviour
{
    private NPCController npcController;
    private float walkRadius;
    private float minWait;
    private float maxWait;
    private Vector3 currentDestination;
    private bool waiting = false;
    private Coroutine currentWaitCoroutine;

    public WanderBehaviour(NPCController npcController)
    {
        this.npcController = npcController;
        this.walkRadius = npcController.WalkRadius;
        this.minWait = npcController.MinWaitTime;
        this.maxWait = npcController.MaxWaitTime;
        PickNewDestination();
    }

    public void Act(Character selfCharacter)
    {
        if (npcController == null || npcController.Agent == null)
            return;

        var agent = npcController.Agent;

        if (waiting)
            return;

        // Si on a atteint la destination, attendre un peu puis choisir une nouvelle
        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
        {
            if (!waiting) // On vérifie pour ne pas lancer 50 coroutines
                currentWaitCoroutine = npcController.StartCoroutine(WaitAndPickNew());
        }
    }
    public void Exit(Character selfCharacter)
    {
        // 1. On stoppe la coroutine de wait immédiatement
        if (npcController != null && currentWaitCoroutine != null)
        {
            npcController.StopCoroutine(currentWaitCoroutine);
            currentWaitCoroutine = null;
        }

        waiting = false;

        // 2. On vide le chemin de l'agent pour éviter le "sursaut" de mouvement
        if (npcController.Agent != null && npcController.Agent.isOnNavMesh)
        {
            npcController.Agent.ResetPath();
            npcController.Agent.velocity = Vector3.zero;
        }
    }

    private void PickNewDestination()
    {
        Vector3 randomDirection = Random.insideUnitSphere * walkRadius + npcController.transform.position;
        if (NavMesh.SamplePosition(randomDirection, out NavMeshHit hit, walkRadius, NavMesh.AllAreas))
        {
            currentDestination = hit.position;
            npcController.Agent.SetDestination(currentDestination);
        }
    }

    private IEnumerator WaitAndPickNew()
    {
        waiting = true;
        float waitTime = Random.Range(minWait, maxWait);
        yield return new WaitForSeconds(waitTime);
        PickNewDestination();
        waiting = false;
    }
}
