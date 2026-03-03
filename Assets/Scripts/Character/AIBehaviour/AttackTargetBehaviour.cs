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
        float attackRange = self.CharacterCombat?.CurrentCombatStyleExpertise?.Style?.MeleeRange ?? 3.5f;
        float zDist = Mathf.Abs(self.transform.position.z - _target.transform.position.z);

        if (dist <= attackRange && zDist <= 1.5f)
        {
            if (!_hasAttacked)
            {
                _hasAttacked = true;
                
                // On s'assure d'être face à la cible
                Vector3 dir = (_target.transform.position - self.transform.position).normalized;
                self.CharacterVisual?.UpdateFlip(dir);

                // --- ATTAQUE SURPRISE ---
                // On d?clenche l'attaque via ExecuteAction pour consommer l'initiative
                if (self.CharacterCombat.ExecuteAction(() => self.CharacterCombat.Attack()))
                {
                    Debug.Log($"<color=red>[AI]</color> {self.CharacterName} lance une attaque surprise sur {_target.CharacterName}!");
                    
                    // On ne termine ce comportement que si l'action a pu d?marrer
                    Terminate();
                }
                else
                {
                    // Si l'action a ?chou? (cooldown, etc.), on réessaye la frame d'après sans terminer
                    _hasAttacked = false; 
                }
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
