using UnityEngine;

public class AttackTargetBehaviour : IAIBehaviour
{
    private Character _target;
    private bool _isFinished = false;
    private bool _hasAttacked = false;

    public bool IsFinished => _isFinished;

    public AttackTargetBehaviour(Character target)
    {
        _target = target;
    }

    public void Act(Character self)
    {
        if (_isFinished || _target == null || !_target.IsAlive())
        {
            Terminate();
            return;
        }

        var movement = self.CharacterMovement;
        if (movement == null) return;

        float dist = Vector3.Distance(self.transform.position, _target.transform.position);
        float attackRange = self.CharacterCombat?.CurrentCombatStyleExpertise?.Style?.AttackRange ?? 3.5f;

        if (dist <= attackRange)
        {
            if (!_hasAttacked)
            {
                _hasAttacked = true;
                
                // On s'assure d'être face à la cible
                Vector3 dir = (_target.transform.position - self.transform.position).normalized;
                self.CharacterVisual?.UpdateFlip(dir);

                // --- ATTAQUE SURPRISE ---
                // On déclenche l'attaque immédiatement (l'action gérera le Stop() du mouvement)
                // Le StartFight sera déclenché "naturellement" par CombatStyleAttack.cs lors de l'impact
                self.CharacterCombat.Attack();
                
                Debug.Log($"<color=red>[AI]</color> {self.CharacterName} lance une attaque surprise sur {_target.CharacterName}!");
                
                // On termine ce comportement, le CombatBehaviour prendra le relais via le BattleManager
                Terminate();
            }
        }
        else
        {
            movement.Resume();
            movement.SetDestination(_target.transform.position);
        }
    }

    public void Exit(Character self)
    {
        self.CharacterMovement?.Stop();
    }

    public void Terminate()
    {
        _isFinished = true;
    }
}
