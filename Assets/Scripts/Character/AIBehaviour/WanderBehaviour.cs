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

    // --- DETECTION DE BORDURE (EDGE AVOIDANCE) ---
    private float _edgePressureTimer = 0f;
    private const float MAX_EDGE_PRESSURE_TIME = 3f; // Temps max à raser un mur
    private const float EDGE_DETECTION_DIST = 3.0f;  // Distance pour détecter un bord
    private const float FORCE_NEW_DEST_DIST = 1.2f;  // Distance pour forcer un changement si bloqué

    public bool IsFinished => _isFinished;

    public WanderBehaviour(NPCController npcController)
    {
        _npcController = npcController;
        _walkRadius = npcController.WalkRadius;
        _minWait = npcController.MinWaitTime;
        _maxWait = npcController.MaxWaitTime;
    }

    public void Act(Character selfCharacter)
    {
        var movement = selfCharacter.CharacterMovement;
        if (movement == null || _isFinished) return;

        if (_isWaiting) return;

        // 1. ARRIVÉE NORMALE
        if (!movement.PathPending && (!movement.HasPath || movement.RemainingDistance <= movement.StoppingDistance + 0.5f))
        {
            StartWaitCoroutine(selfCharacter);
            return;
        }

        // 2. DÉTECTION DE "PROXIMITÉ EDGE" (Anti-glissade)
        // Si on est très près d'un bord et qu'on bouge peu (vitesse faible ou collision répétée)
        if (NavMesh.FindClosestEdge(selfCharacter.transform.position, out NavMeshHit hit, NavMesh.AllAreas))
        {
            if (hit.distance < FORCE_NEW_DEST_DIST)
            {
                _edgePressureTimer += Time.deltaTime;
                if (_edgePressureTimer > MAX_EDGE_PRESSURE_TIME)
                {
                    Debug.Log($"<color=orange>[Wander]</color> {selfCharacter.name} semble longer un bord. Changement de direction forcé.");
                    StartWaitCoroutine(selfCharacter);
                }
            }
            else
            {
                _edgePressureTimer = 0f;
            }
        }
    }

    private void StartWaitCoroutine(Character self)
    {
        if (_isWaiting) return;
        _currentWaitCoroutine = _npcController.StartCoroutine(WaitAndPickNew(self));
    }

    private IEnumerator WaitAndPickNew(Character self)
    {
        _isWaiting = true;
        _edgePressureTimer = 0f;

        float waitTime = Random.Range(_minWait, _maxWait);
        yield return new WaitForSeconds(waitTime);

        PickNewDestination(self);
        _isWaiting = false;
        _currentWaitCoroutine = null;
    }

    private void PickNewDestination(Character self)
    {
        var movement = self.CharacterMovement;
        if (movement == null) return;

        // --- LOGIQUE DE "REBOND" NATUREL ---
        Vector3 finalDirectionBias = Vector3.zero;

        // On regarde si on est près d'un bord
        if (NavMesh.FindClosestEdge(self.transform.position, out NavMeshHit edgeHit, NavMesh.AllAreas))
        {
            if (edgeHit.distance < EDGE_DETECTION_DIST)
            {
                // On crée un vecteur qui part du mur vers l'intérieur (la normale)
                finalDirectionBias = edgeHit.normal * _walkRadius * 0.5f; 
            }
        }

        // JITTER + BIAIS
        Vector2 randomCircle = Random.insideUnitCircle * _walkRadius;
        Vector3 randomPos = new Vector3(randomCircle.x, 0, randomCircle.y) + self.transform.position;
        
        // On additionne le biais pour "pousser" la recherche vers l'espace libre
        Vector3 biasedPos = randomPos + finalDirectionBias;

        if (NavMesh.SamplePosition(biasedPos, out NavMeshHit hit, _walkRadius, NavMesh.AllAreas))
        {
            movement.SetDestination(hit.position);
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
        _edgePressureTimer = 0f;
        selfCharacter.CharacterMovement?.ResetPath();
    }

    public void Terminate() => _isFinished = true;
}
