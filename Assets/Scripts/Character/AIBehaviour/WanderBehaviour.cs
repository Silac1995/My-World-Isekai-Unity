using UnityEngine;
using UnityEngine.AI;
using System.Collections;

public class WanderBehaviour : IAIBehaviour
{
    private NPCController _npcController;
    private float _walkRadius;
    private float _minWait;
    private float _maxWait;
    private bool _isWaiting = false;
    private Coroutine _currentWaitCoroutine;
    private bool _isFinished = false;
    public bool IsFinished => _isFinished;

    public WanderBehaviour(NPCController npcController)
    {
        _npcController = npcController;
        _walkRadius = npcController.WalkRadius;
        _minWait = npcController.MinWaitTime;
        _maxWait = npcController.MaxWaitTime;

        // On ne lance pas PickNewDestination ici, on laisse Act s'en charger 
        // ou on lance l'attente initiale.
    }

    public void Act(Character selfCharacter)
    {
        if (_npcController == null || _npcController.Agent == null || _isWaiting)
            return;

        var agent = _npcController.Agent;

        // Si l'agent n'a pas de destination (au début ou après un ResetPath)
        // OU s'il est arrivé à destination
        if (!agent.pathPending && (!agent.hasPath || agent.remainingDistance <= agent.stoppingDistance))
        {
            _currentWaitCoroutine = _npcController.StartCoroutine(WaitAndPickNew());
        }
    }

    private IEnumerator WaitAndPickNew()
    {
        _isWaiting = true;

        float waitTime = Random.Range(_minWait, _maxWait);
        // Debug.Log($"[Wander] Pause de {waitTime}s...");
        yield return new WaitForSeconds(waitTime);

        PickNewDestination();
        _isWaiting = false;
        _currentWaitCoroutine = null;
    }

    private void PickNewDestination()
    {
        if (_npcController == null || _npcController.Agent == null) return;

        Vector3 randomDirection = Random.insideUnitSphere * _walkRadius + _npcController.transform.position;
        if (NavMesh.SamplePosition(randomDirection, out NavMeshHit hit, _walkRadius, NavMesh.AllAreas))
        {
            _npcController.Agent.SetDestination(hit.position);
        }
    }

    public void Exit(Character selfCharacter)
    {
        if (_npcController != null && _currentWaitCoroutine != null)
        {
            _npcController.StopCoroutine(_currentWaitCoroutine);
            _currentWaitCoroutine = null;
        }

        _isWaiting = false;

        if (_npcController?.Agent != null && _npcController.Agent.isOnNavMesh)
        {
            _npcController.Agent.ResetPath();
        }
    }

    public void Terminate() => _isFinished = true;
}
