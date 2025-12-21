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
            npcController.StartCoroutine(WaitAndPickNew());
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
