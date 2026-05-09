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

    // --- EDGE DETECTION (EDGE AVOIDANCE) ---
    private float _edgePressureTimer = 0f;
    private const float MAX_EDGE_PRESSURE_TIME = 3f; // Max time hugging a wall
    private const float EDGE_DETECTION_DIST = 1.5f;  // Distance to detect an edge
    private const float FORCE_NEW_DEST_DIST = 0.5f;  // Distance to force a change when stuck

    // --- ACTIVITY RESUMPTION (Classes, Jobs, etc.) ---
    private float _periodicCheckTimer = 0f;
    private const float PERIODIC_CHECK_RATE = 10f;

    public bool IsFinished => _isFinished;

    private Zone _wanderZone;

    public WanderBehaviour(NPCController npcController, Zone wanderZone = null)
    {
        _npcController = npcController;
        _walkRadius = npcController.WalkRadius;
        _minWait = npcController.MinWaitTime;
        _maxWait = npcController.MaxWaitTime;
        _wanderZone = wanderZone;
    }

    public void Enter(Character selfCharacter)
    {
        // Initialization logic if needed (currently handled in constructor, but can be moved here)
    }

    public void Act(Character selfCharacter)
    {
        var movement = selfCharacter.CharacterMovement;
        if (movement == null || _isFinished) return;

        // 0. CHECK INTERRUPTED ACTIVITIES (resume if interrupted by combat/dialogue)
        _periodicCheckTimer += Time.deltaTime;
        if (_periodicCheckTimer >= PERIODIC_CHECK_RATE)
        {
            _periodicCheckTimer = 0f;
            if (TryResumeActivity(selfCharacter)) return;
        }

        if (_isWaiting) return;

        // 1. NORMAL ARRIVAL
        if (!movement.PathPending && (!movement.HasPath || movement.RemainingDistance <= movement.StoppingDistance + 0.5f))
        {
            StartWaitCoroutine(selfCharacter);
            return;
        }

        // 2. "EDGE PROXIMITY" DETECTION (anti-sliding)
        // If we're very close to an edge and barely moving (low speed or repeated collision)
        if (NavMesh.FindClosestEdge(selfCharacter.transform.position, out NavMeshHit hit, NavMesh.AllAreas))
        {
            if (hit.distance < FORCE_NEW_DEST_DIST)
            {
                _edgePressureTimer += Time.deltaTime;
                if (_edgePressureTimer > MAX_EDGE_PRESSURE_TIME)
                {
                    Debug.Log($"<color=orange>[Wander]</color> {selfCharacter.name} seems to be hugging an edge. Forced direction change.");
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

        Vector3 targetPos;

        if (_wanderZone != null)
        {
            targetPos = _wanderZone.GetRandomPointInZone();
        }
        else
        {
            // --- NATURAL "BOUNCE" LOGIC (existing logic) ---
            Vector3 finalDirectionBias = Vector3.zero;

            if (NavMesh.FindClosestEdge(self.transform.position, out NavMeshHit edgeHit, NavMesh.AllAreas))
            {
                if (edgeHit.distance < EDGE_DETECTION_DIST)
                {
                    finalDirectionBias = edgeHit.normal * 3f; 
                }
            }

            Bounds? mapBounds = null;
            if (self.TryGetComponent(out CharacterMapTracker tracker) && !string.IsNullOrEmpty(tracker.CurrentMapID.Value.ToString()))
            {
                string mapId = tracker.CurrentMapID.Value.ToString();
                var maps = UnityEngine.Object.FindObjectsByType<MWI.WorldSystem.MapController>(UnityEngine.FindObjectsSortMode.None);
                foreach (var m in maps)
                {
                    if (m.MapId == mapId)
                    {
                        if (m.TryGetComponent(out Collider mapCollider))
                        {
                            mapBounds = mapCollider.bounds;
                        }
                        break;
                    }
                }
            }

            Vector3 randomPos;
            if (mapBounds.HasValue)
            {
                randomPos = new Vector3(
                    Random.Range(mapBounds.Value.min.x, mapBounds.Value.max.x),
                    self.transform.position.y,
                    Random.Range(mapBounds.Value.min.z, mapBounds.Value.max.z)
                );
            }
            else
            {
                Vector2 randomCircle = Random.insideUnitCircle * _walkRadius;
                randomPos = new Vector3(randomCircle.x, 0, randomCircle.y) + self.transform.position;
            }

            Vector3 biasedPos = randomPos + finalDirectionBias;

            if (NavMesh.SamplePosition(biasedPos, out NavMeshHit hit, _walkRadius, NavMesh.AllAreas))
            {
                targetPos = hit.position;
            }
            else
            {
                targetPos = self.transform.position;
            }
        }

        movement.SetDestination(targetPos);
    }

    /// <summary>
    /// Checks whether the character should stop wandering to resume a higher-priority activity
    /// they may have forgotten following an interruption (combat, dialogue).
    /// Returns true if the Behaviour has been replaced.
    /// </summary>
    private bool TryResumeActivity(Character self)
    {
        // 1. Mentorship / Class in progress
        var mentorship = self.CharacterMentorship;
        if (mentorship != null && mentorship.CurrentMentor != null)
        {
            var mentorMentorship = mentorship.CurrentMentor.CharacterMentorship;
            if (mentorMentorship != null && mentorMentorship.IsCurrentlyTeaching && mentorMentorship.SpawnedClassZone != null)
            {
                Debug.Log($"<color=cyan>[Mentorship]</color> {self.CharacterName} se souvient qu'il a un cours en cours et y retourne !");
                _npcController.SetBehaviour(new AttendClassBehaviour(_npcController));
                return true;
            }
        }

        // --- D'autres vérifications futures (Jobs, Schedules oubliés) pourront être ajoutées ici ---

        return false;
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
