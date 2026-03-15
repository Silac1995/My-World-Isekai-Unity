using System.Collections.Generic;
using UnityEngine;

namespace MWI.AI
{
    public class GoapAction_MoveToItem : GoapAction
    {
        private JobTransporter _job;
        private bool _isMoving = false;
        private float _lastRouteRequestTime;
        protected bool _isComplete = false;

        public override string ActionName => "Move To Item";
        public override float Cost => 1f;

        public override Dictionary<string, bool> Preconditions => new Dictionary<string, bool>
        {
            { "itemLocated", true },
            { "atItem", false }
        };

        public override Dictionary<string, bool> Effects => new Dictionary<string, bool>
        {
            { "atItem", true }
        };

        public override bool IsComplete => _isComplete;

        public GoapAction_MoveToItem(JobTransporter job)
        {
            _job = job;
        }

        public override bool IsValid(Character worker)
        {
            return _job != null && _job.CurrentOrder != null && _job.TargetWorldItem != null;
        }

        public override void Execute(Character worker)
        {
            if (_job.CurrentOrder == null || _job.TargetWorldItem == null)
            {
                _isComplete = true;
                return;
            }

            var movement = worker.CharacterMovement;
            if (movement == null) return;

            bool isCloseEnough = false;
            var workerCol = worker.GetComponent<Collider>();
            
            if (_job.TargetWorldItem.ItemInteractable != null && _job.TargetWorldItem.ItemInteractable.InteractionZone != null && workerCol != null)
            {
                isCloseEnough = _job.TargetWorldItem.ItemInteractable.InteractionZone.bounds.Intersects(workerCol.bounds);
            }
            else
            {
                isCloseEnough = movement.RemainingDistance <= movement.StoppingDistance + 0.5f;
            }

            if (!isCloseEnough)
            {
                bool hasPathFailed = (UnityEngine.Time.time - _lastRouteRequestTime > 0.2f) && (movement.PathStatus == UnityEngine.AI.NavMeshPathStatus.PathInvalid || (!movement.HasPath && !movement.PathPending));

                if (!_isMoving || hasPathFailed)
                {
                    Vector3 dest = _job.TargetWorldItem.transform.position;
                    if (_job.TargetWorldItem.ItemInteractable != null && _job.TargetWorldItem.ItemInteractable.InteractionZone != null)
                    {
                        dest = _job.TargetWorldItem.ItemInteractable.InteractionZone.bounds.ClosestPoint(worker.transform.position);
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
