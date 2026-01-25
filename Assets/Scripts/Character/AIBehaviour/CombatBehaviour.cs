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

    // --- NOUVEAU : Setter de cible ---
    public void SetCurrentTarget(Character target)
    {
        _currentTarget = target;

        if (target == null)
        {
            Debug.Log("<color=gray>[Combat]</color> Cible réinitialisée (null).");
        }
    }

    public void Terminate() => _isFinished = true;

    public void Act(Character self)
    {
        if (_battleManager == null || _isFinished) return;

        // 1. Utilisation de la nouvelle vérification
        if (!HasTarget)
        {
            // On stoppe l'agent s'il n'y a plus de cible pour éviter qu'il continue 
            // de courir vers la dernière position connue
            if (self.Controller.Agent != null && self.Controller.Agent.isOnNavMesh)
            {
                self.Controller.Agent.isStopped = true;
            }
            return;
        }

        NavMeshAgent agent = self.Controller.Agent;
        if (agent == null || !agent.isOnNavMesh) return;

        // On s'assure que l'agent est actif (notre sécurité du Push)
        if (agent.isStopped)
            agent.isStopped = false;

        // 2. Logique de mouvement vers la cible
        agent.SetDestination(_currentTarget.transform.position);

        // 3. CONTRAINTE DE ZONE
        if (_battleZone == null) _battleZone = _battleManager.GetComponent<BoxCollider>();

        if (_battleZone != null)
        {
            if (!_battleZone.bounds.Contains(self.transform.position))
            {
                Vector3 clampedPosition = _battleZone.ClosestPoint(self.transform.position);
                agent.SetDestination(clampedPosition);
            }
        }

        // 4. Logique d'attaque / Flip
        float dist = Vector3.Distance(self.transform.position, _currentTarget.transform.position);
        if (dist < 2f)
        {
            Vector3 dir = _currentTarget.transform.position - self.transform.position;
            self.CharacterVisual?.UpdateFlip(dir);
        }
    }

    public void Exit(Character self)
    {
        if (self.Controller.Agent != null && self.Controller.Agent.isOnNavMesh)
        {
            self.Controller.Agent.ResetPath();
        }
        Debug.Log($"<color=orange>[AI]</color> {self.CharacterName} sort du mode Combat.");
    }
}