using UnityEngine;
using UnityEngine.AI;

public class FollowTargetBehaviour : IAIBehaviour
{
    private Character _target;
    private NavMeshAgent _agent;
    private float _followDist;
    private bool _isFinished = false;

    public bool IsFinished => _isFinished;

    public FollowTargetBehaviour(Character target, NavMeshAgent agent, float dist = 3f)
    {
        _target = target;
        _agent = agent;
        _followDist = dist;
    }

    public void Terminate() => _isFinished = true;

    public void Act(Character self)
    {
        if (_target == null || _isFinished) return;

        float dist = Vector3.Distance(self.transform.position, _target.transform.position);

        if (dist > _followDist)
        {
            _agent.isStopped = false;
            _agent.SetDestination(_target.transform.position);
        }
        else
        {
            _agent.isStopped = true;
            _agent.velocity = Vector3.zero;
        }
    }

    public void Exit(Character self)
    {
        if (_agent != null && _agent.isOnNavMesh) _agent.ResetPath();
    }
}