using System.Collections.Generic;
using UnityEngine;

namespace MWI.AI
{
    public class GoapAction_GoToSourceStorage : GoapAction_MoveToTarget
    {
        private JobTransporter _job;

        public override string ActionName => "Go To Source Storage";

        public override Dictionary<string, bool> Preconditions => new Dictionary<string, bool>
        {
            { "atSourceStorage", false },
            { "itemCarried", false }
        };

        public override Dictionary<string, bool> Effects => new Dictionary<string, bool>
        {
            { "atSourceStorage", true }
        };

        public GoapAction_GoToSourceStorage(JobTransporter job)
        {
            _job = job;
        }

        public override bool IsValid(Character worker)
        {
            return _job != null && _job.CurrentOrder != null && _job.CurrentOrder.Source != null;
        }
        
        protected override Collider GetTargetCollider(Character worker)
        {
            if (_job == null || _job.CurrentOrder == null || _job.CurrentOrder.Source == null) return null;
            
            CommercialBuilding source = _job.CurrentOrder.Source;
            Zone targetZone = source.StorageZone ?? source.MainRoom.GetComponent<Zone>();
            return targetZone != null ? targetZone.GetComponent<Collider>() : null;
        }

        protected override Vector3 GetDestinationPoint(Character worker)
        {
            if (_job == null || _job.CurrentOrder == null || _job.CurrentOrder.Source == null) return worker.transform.position;
            
            CommercialBuilding source = _job.CurrentOrder.Source;
            Zone targetZone = source.StorageZone ?? source.MainRoom.GetComponent<Zone>();
            return targetZone != null ? targetZone.transform.position : source.transform.position;
        }
    }
}
