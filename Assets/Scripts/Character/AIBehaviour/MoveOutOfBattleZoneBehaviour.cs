using UnityEngine;
using UnityEngine.AI;

public class MoveOutOfBattleZoneBehaviour : IAIBehaviour
{
    private BattleManager _battleManager;
    private bool _isFinished = false;
    private Vector3 _targetExitPos;
    private bool _hasTarget = false;
    private const float MARGIN = 5f;

    public bool IsFinished => _isFinished;

    public MoveOutOfBattleZoneBehaviour(BattleManager battleManager)
    {
        _battleManager = battleManager;
    }

    public void Enter(Character selfCharacter) { }
    public void Act(Character self)
    {
        if (_battleManager == null || _isFinished)
        {
            _isFinished = true;
            return;
        }

        if (!_hasTarget)
        {
            CalculateExitPosition(self);
            if (_hasTarget)
            {
                self.CharacterMovement?.SetDestination(_targetExitPos);
                Debug.Log($"<color=orange>[AI]</color> {self.CharacterName} sort de la zone de combat.");
            }
            else
            {
                // Fallback si on ne peut pas calculer (on s'arrête)
                _isFinished = true;
                return;
            }
        }

        // Vérification d'arrivée ou si on est déjà sorti
        if (self.CharacterMovement != null)
        {
            float distToExit = Vector3.Distance(self.transform.position, _targetExitPos);
            
            // Si on est arrivés à la destination de sortie
            if (!self.CharacterMovement.PathPending && self.CharacterMovement.RemainingDistance <= self.CharacterMovement.StoppingDistance + 0.5f)
            {
                _isFinished = true;
                return;
            }

            // Vérification de sécurité : sommes-nous déjà hors de la zone ?
            if (!IsInsideZone(self.transform.position))
            {
                _isFinished = true;
                return;
            }
        }
    }

    private void CalculateExitPosition(Character self)
    {
        Collider zoneCollider = _battleManager.GetComponent<Collider>();
        if (zoneCollider == null) return;

        Bounds bounds = zoneCollider.bounds;
        Vector3 center = bounds.center;
        Vector3 playerPos = self.transform.position;

        // Direction du centre vers le joueur
        Vector3 dir = (playerPos - center).normalized;
        if (dir == Vector3.zero) dir = Vector3.right; // Fallback

        // Rayon approximatif (la moitié de la plus grande dimension)
        float extent = Mathf.Max(bounds.extents.x, bounds.extents.z);
        
        // On vise un point à (rayon + MARGIN) du centre
        Vector3 candidatePos = center + dir * (extent + MARGIN);

        if (NavMesh.SamplePosition(candidatePos, out NavMeshHit hit, MARGIN * 2f, NavMesh.AllAreas))
        {
            _targetExitPos = hit.position;
            _hasTarget = true;
        }
        else
        {
            // Deuxième essai : direction opposée au centre
            _targetExitPos = playerPos + (playerPos - center).normalized * MARGIN;
            _hasTarget = true;
        }
    }

    private bool IsInsideZone(Vector3 pos)
    {
        if (_battleManager == null) return false;
        Collider zoneCollider = _battleManager.GetComponent<Collider>();
        if (zoneCollider == null) return false;

        return zoneCollider.bounds.Contains(pos);
    }

    public void Exit(Character self)
    {
        self.CharacterMovement?.ResetPath();
    }

    public void Terminate() => _isFinished = true;
}
