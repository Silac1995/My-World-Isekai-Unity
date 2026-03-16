using System.Collections.Generic;
using UnityEngine;

namespace MWI.AI
{
    public class GoapAction_MoveToItem : GoapAction_MoveToTarget
    {
        private JobTransporter _job;

        public override string ActionName => "Move To Item";

        public override Dictionary<string, bool> Preconditions => new Dictionary<string, bool>
        {
            { "itemLocated", true },
            { "atItem", false }
        };

        public override Dictionary<string, bool> Effects => new Dictionary<string, bool>
        {
            { "atItem", true }
        };

        public GoapAction_MoveToItem(JobTransporter job)
        {
            _job = job;
        }

        public override bool IsValid(Character worker)
        {
            return _job != null && _job.CurrentOrder != null && _job.TargetWorldItem != null;
        }

        protected override Collider GetTargetCollider(Character worker)
        {
            if (_job == null || _job.TargetWorldItem == null) return null;
            
            var interactable = _job.TargetWorldItem.ItemInteractable;
            return interactable != null ? interactable.InteractionZone : _job.TargetWorldItem.GetComponentInChildren<Collider>();
        }

        protected override Vector3 GetDestinationPoint(Character worker)
        {
            if (_job == null || _job.TargetWorldItem == null) return worker.transform.position;
            return _job.TargetWorldItem.transform.position;
        }
    }
}
