using UnityEngine;
using UnityEngine.AI;

public class CombatBehaviour : IAIBehaviour
{
    private BattleManager _battleManager;
    private Character _currentTarget;
    private bool _isFinished = false;
    private Collider _battleZone;

    public Character Target => _currentTarget;
    public bool IsFinished => _isFinished;
    public bool HasTarget => _currentTarget != null && _currentTarget.IsAlive();

    public CombatBehaviour(BattleManager battleManager, Character target)
    {
        _battleManager = battleManager;
        _currentTarget = target;
    }

    public void SetCurrentTarget(Character target)
    {
        _currentTarget = target;
    }

    public void Terminate() => _isFinished = true;

    public void Act(Character self)
    {
        if (_battleManager == null || _isFinished) return;

        var movement = self.CharacterMovement;
        if (movement == null) return;

        if (!HasTarget)
        {
            movement.Stop();
            return;
        }

        // On s'assure que le mouvement est actif
        movement.Resume();

        Vector3 destination = _currentTarget.transform.position;

        // CONTRAINTE DE ZONE
        if (_battleZone == null) _battleZone = _battleManager.GetComponent<BoxCollider>();

        if (_battleZone != null)
        {
            if (!_battleZone.bounds.Contains(self.transform.position))
            {
                destination = _battleZone.ClosestPoint(self.transform.position);
            }
        }
        
        movement.SetDestination(destination);

        // Flip
        float dist = Vector3.Distance(self.transform.position, _currentTarget.transform.position);
        if (dist < 2f)
        {
            Vector3 dir = _currentTarget.transform.position - self.transform.position;
            self.CharacterVisual?.UpdateFlip(dir);
        }
    }

    public void Exit(Character self)
    {
        self.CharacterMovement?.ResetPath();
        Debug.Log($"<color=orange>[AI]</color> {self.CharacterName} sort du mode Combat.");
    }
}
