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
            worker.CharacterMovement?.ResetPath();
            return;
        }

        Vector3 targetPos = _boss.transform.position;
        var movement = worker.CharacterMovement;

        // Mise à jour dynamique de la destination si la cible bouge
        if (!_isMoving || Vector3.Distance(movement.Destination, targetPos) > 0.1f)
        {
            movement?.SetDestination(targetPos);
            _isMoving = true;
        }
    }

    public override void Exit(Character worker)
    {
        _isMoving = false;
        worker.CharacterMovement?.ResetPath(); // Arrêt forcé si l'action est annulée par CharacterGoapController
    }
}
