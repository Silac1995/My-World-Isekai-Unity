using UnityEngine;
using UnityEngine.AI;

namespace MWI.AI
{
    /// <summary>
    /// Implémentation native de l'exploration (Wander) pour le Behaviour Tree.
    /// Ne dépend plus de l'ancien IAIBehaviour (WanderBehaviour) pour éviter 
    /// les conflits de Coroutines sous-jacentes.
    /// </summary>
    public class BTAction_Wander : BTNode
    {
        private float _waitEndTime = 0f;
        private bool _isWaiting = false;

        private float _edgePressureEndTime = 0f;
        private const float MAX_EDGE_PRESSURE_TIME = 3f;
        private const float EDGE_DETECTION_DIST = 1.5f;
        private const float FORCE_NEW_DEST_DIST = 0.5f;

        private int _framesSincePathRequest = 0;

        protected override void OnEnter(Blackboard bb)
        {
            _isWaiting = false;
            _edgePressureEndTime = 0f;
            PickNewDestination(bb);
        }

        protected override BTNodeStatus OnExecute(Blackboard bb)
        {
            Character self = bb.Self;
            if (self == null) return BTNodeStatus.Failure;

            var movement = self.CharacterMovement;
            if (movement == null) return BTNodeStatus.Failure;

            if (_isWaiting)
            {
                if (UnityEngine.Time.time >= _waitEndTime)
                {
                    _isWaiting = false;
                    PickNewDestination(bb);
                }
                return BTNodeStatus.Running;
            }

            _framesSincePathRequest++;

            // Wait until NavMesh thread answers
            if (_framesSincePathRequest > 5)
            {
                // Vérifier si on est arrivé à destination
                if (!movement.PathPending && (!movement.HasPath || movement.RemainingDistance <= movement.StoppingDistance + 0.5f))
                {
                    StartWaiting(bb.Self);
                    return BTNodeStatus.Running;
                }
            }

            // Détection anti-glissement sur les bords
            if (NavMesh.FindClosestEdge(self.transform.position, out NavMeshHit hit, NavMesh.AllAreas))
            {
                if (hit.distance < FORCE_NEW_DEST_DIST)
                {
                    if (_edgePressureEndTime <= 0) _edgePressureEndTime = UnityEngine.Time.time + MAX_EDGE_PRESSURE_TIME;
                    
                    if (UnityEngine.Time.time > _edgePressureEndTime)
                    {
                        StartWaiting(bb.Self);
                    }
                }
                else
                {
                    _edgePressureEndTime = 0f;
                }
            }

            return BTNodeStatus.Running;
        }

        private void StartWaiting(Character self)
        {
            NPCController npc = self.Controller as NPCController;
            float minWait = npc != null ? npc.MinWaitTime : 2f;
            float maxWait = npc != null ? npc.MaxWaitTime : 7f;

            _waitEndTime = UnityEngine.Time.time + Random.Range(minWait, maxWait);
            _isWaiting = true;
            _edgePressureEndTime = 0f;
            _framesSincePathRequest = 0;
        }

        private void PickNewDestination(Blackboard bb)
        {
            _framesSincePathRequest = 0;
            Character self = bb.Self;
            NPCController npc = self.Controller as NPCController;
            float walkRadius = npc != null ? npc.WalkRadius : 50f;

            var movement = self.CharacterMovement;
            if (movement == null) return;

            Vector3 finalDirectionBias = Vector3.zero;

            if (NavMesh.FindClosestEdge(self.transform.position, out NavMeshHit edgeHit, NavMesh.AllAreas))
            {
                if (edgeHit.distance < EDGE_DETECTION_DIST)
                {
                    finalDirectionBias = edgeHit.normal * 3f; 
                }
            }

            // NEW LOGIC FOR MAPCONTROLLER BOUNDS
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

            bool pathFound = false;

            // Essayer jusqu'à 5 fois de trouver un point atteignable (PathComplete)
            for (int i = 0; i < 5; i++)
            {
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
                    Vector2 randomCircle = Random.insideUnitCircle * walkRadius;
                    randomPos = new Vector3(randomCircle.x, 0, randomCircle.y) + self.transform.position;
                }

                Vector3 biasedPos = randomPos + finalDirectionBias;

                if (NavMesh.SamplePosition(biasedPos, out NavMeshHit hit, walkRadius, NavMesh.AllAreas))
                {
                    NavMeshPath path = new NavMeshPath();
                    if (NavMesh.CalculatePath(self.transform.position, hit.position, NavMesh.AllAreas, path))
                    {
                        if (path.status == NavMeshPathStatus.PathComplete)
                        {
                            movement.SetDestination(hit.position);
                            pathFound = true;
                            break;
                        }
                    }
                }
            }

            if (!pathFound)
            {
                movement.SetDestination(self.transform.position);
            }
        }

        protected override void OnExit(Blackboard bb)
        {
            _isWaiting = false;
            _edgePressureEndTime = 0f;
            bb.Self?.CharacterMovement?.ResetPath();
        }
    }
}
