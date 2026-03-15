using System.Collections.Generic;
using UnityEngine;

namespace MWI.AI
{
    public class GoapAction_GoToSourceStorage : GoapAction
    {
        private JobTransporter _job;
        private bool _isMoving = false;
        private float _lastRouteRequestTime;
        protected bool _isComplete = false;

        public override string ActionName => "Go To Source Storage";
        public override float Cost => 1f;

        public override Dictionary<string, bool> Preconditions => new Dictionary<string, bool>
        {
            { "atSourceStorage", false },
            { "itemCarried", false }
        };

        public override Dictionary<string, bool> Effects => new Dictionary<string, bool>
        {
            { "atSourceStorage", true }
        };

        public override bool IsComplete => _isComplete;

        public GoapAction_GoToSourceStorage(JobTransporter job)
        {
            _job = job;
        }

        public override bool IsValid(Character worker)
        {
            return _job != null && _job.CurrentOrder != null && _job.CurrentOrder.Source != null;
        }

        public override void Execute(Character worker)
        {
            if (_job.CurrentOrder == null || _job.CurrentOrder.Source == null)
            {
                _isComplete = true;
                return;
            }

            var movement = worker.CharacterMovement;
            if (movement == null) return;

            // Get StorageZone OR MainRoom if no StorageZone exists
            CommercialBuilding source = _job.CurrentOrder.Source;
            Zone targetZone = source.StorageZone ?? source.MainRoom.GetComponent<Zone>();
            
            if (targetZone == null)
            {
                _isComplete = true;
                return;
            }

            bool isCloseEnough = false;
            var workerCol = worker.Collider;
            var zoneCollider = targetZone.GetComponent<Collider>();

            if (zoneCollider != null && workerCol != null)
            {
                var zoneBounds = zoneCollider.bounds;
                isCloseEnough = zoneBounds.Intersects(workerCol.bounds);

                if (!isCloseEnough)
                {
                    // Check if center to center is close enough
                    Vector3 charPos = worker.transform.position;
                    charPos.y = 0;
                    Vector3 closestPoint = zoneBounds.ClosestPoint(worker.transform.position);
                    closestPoint.y = 0;

                    if (Vector3.Distance(charPos, closestPoint) <= 1f)
                    {
                        isCloseEnough = true;
                    }
                }
            }
            else
            {
                isCloseEnough = _isMoving && movement.RemainingDistance <= movement.StoppingDistance + 0.5f;
            }

            if (!isCloseEnough)
            {
                bool hasPathFailed = (UnityEngine.Time.time - _lastRouteRequestTime > 0.2f) && 
                                     (movement.PathStatus == UnityEngine.AI.NavMeshPathStatus.PathInvalid || 
                                     (!movement.HasPath && !movement.PathPending));

                if (!_isMoving || hasPathFailed)
                {
                    Vector3 dest = targetZone.transform.position;
                    if (zoneCollider != null)
                    {
                        dest = zoneCollider.bounds.center;
                    }

                    movement.SetDestination(dest);
                    _lastRouteRequestTime = UnityEngine.Time.time;
                    _isMoving = true;
                }
            }
            else
            {
                if (_isMoving)
                {
                    movement.Stop();
                    _isMoving = false;
                }
                _isComplete = true;
            }
        }

        public override void Exit(Character worker)
        {
            _isComplete = false;
            _isMoving = false;
            worker.CharacterMovement?.Stop();
        }
    }
}
