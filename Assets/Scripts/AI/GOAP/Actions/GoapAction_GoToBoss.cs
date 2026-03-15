using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Action GOAP : Se rendre auprès du patron d'un bâtiment pour une interaction.
/// </summary>
public class GoapAction_GoToBoss : GoapAction
{
    public override string ActionName => "GoToBoss";

    public override Dictionary<string, bool> Preconditions => new Dictionary<string, bool>
    {
        { "knowsVacantJob", true },
        { "atBossLocation", false }
    };

    public override Dictionary<string, bool> Effects => new Dictionary<string, bool>
    {
        { "atBossLocation", true }
    };

    public override float Cost => 2f;

    private Character _boss;
    private bool _isComplete = false;
    private bool _isMoving = false;
    
    private Vector3 _lastTargetPos = Vector3.positiveInfinity;
    private float _lastRouteRequestTime = 0f;

    public override bool IsComplete => _isComplete;

    public GoapAction_GoToBoss(Character boss)
    {
        _boss = boss;
    }

    public override bool IsValid(Character worker)
    {
        return _boss != null && _boss.IsAlive();
    }

    public override void Execute(Character worker)
    {
        if (_isComplete) return;

        float dist = Vector3.Distance(worker.transform.position, _boss.transform.position);
        if (dist <= 2.5f)
        {
            _isComplete = true;
            worker.CharacterMovement?.Stop();
            return;
        }

        Vector3 targetPos = _boss.transform.position;
        var movement = worker.CharacterMovement;
        
        bool hasPathFailed = (UnityEngine.Time.time - _lastRouteRequestTime > 0.2f) && (movement.PathStatus == UnityEngine.AI.NavMeshPathStatus.PathInvalid || (!movement.HasPath && !movement.PathPending));

        // Mise à jour dynamique de la destination si la cible bouge
        if (!_isMoving || Vector3.Distance(_lastTargetPos, targetPos) > 1f || hasPathFailed)
        {
            movement?.SetDestination(targetPos);
            _lastTargetPos = targetPos;
            _lastRouteRequestTime = UnityEngine.Time.time;
            _isMoving = true;
        }
    }

    public override void Exit(Character worker)
    {
        _isComplete = false;
        _isMoving = false;
        worker.CharacterMovement?.Stop();
    }
}
