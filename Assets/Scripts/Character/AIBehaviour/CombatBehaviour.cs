using UnityEngine;
using UnityEngine.AI;

public class CombatBehaviour : IAIBehaviour
{
    private BattleManager _battleManager;
    private Character _currentTarget;
    private bool _isFinished = false;

    // On récupère la zone de combat (le BoxCollider créé par le BattleManager)
    private Collider _battleZone;

    public bool IsFinished => _isFinished;

    public CombatBehaviour(BattleManager battleManager, Character target)
    {
        _battleManager = battleManager;
        _currentTarget = target;

        // On récupère la zone via le manager
        // Note: Dans ton BattleManager, _battleZone est privé, assure-toi d'ajouter un getter si besoin
        // ou d'utiliser BattleManager.GetComponent<BoxCollider>()
    }

    public void Terminate() => _isFinished = true;

    public void Act(Character self)
    {
        if (_battleManager == null || _isFinished) return;


        // 1. Vérification de la cible
        if (_currentTarget == null || !_currentTarget.IsAlive())
        {
            // Si la cible meurt, le combat n'est pas forcément fini (il reste peut-être d'autres ennemis)
            // Mais pour ce comportement spécifique, on s'arrête si on n'a plus rien à taper
            return;
        }

        NavMeshAgent agent = self.Controller.Agent;
        if (agent == null || !agent.isOnNavMesh) return;
        if (agent.isStopped)
            agent.isStopped = false;

        // 2. Logique de mouvement vers la cible
        // On définit la destination vers l'ennemi
        agent.SetDestination(_currentTarget.transform.position);

        // 3. CONTRAINTE DE ZONE (La "BattleZone")
        // On récupère le collider du manager pour vérifier si on est dedans
        if (_battleZone == null) _battleZone = _battleManager.GetComponent<BoxCollider>();

        if (_battleZone != null)
        {
            // Si la position actuelle du personnage est HORS de la zone
            if (!_battleZone.bounds.Contains(self.transform.position))
            {
                // On trouve le point le plus proche à l'intérieur des limites de la zone
                Vector3 clampedPosition = _battleZone.ClosestPoint(self.transform.position);

                // On force l'agent à rester sur ce point
                agent.SetDestination(clampedPosition);

                Debug.LogWarning($"<color=red>[Combat]</color> {self.CharacterName} tente de fuir la zone !");
            }
        }

        // 4. Logique d'attaque (Visuel/Distance)
        float dist = Vector3.Distance(self.transform.position, _currentTarget.transform.position);
        if (dist < 2f) // Distance de corps à corps
        {
            // Ici tu pourrais déclencher une attaque auto ou un timer d'attaque
            // self.CharacterCombat.TryAttack(_currentTarget);

            // On regarde la cible
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