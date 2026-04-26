using UnityEngine;
using System.Linq;

namespace MWI.AI
{
    /// <summary>
    /// Condition: The character has just finished their shift (their ScheduleActivity is NO LONGER Work)
    /// but they are STILL physically registered as on-shift in the building
    /// (IsWorkerOnShift returns true). They MUST clock out.
    /// </summary>
    public class BTCond_NeedsToPunchOut : BTNode
    {
        private BTAction_PunchOut _actionPunchOut = new BTAction_PunchOut();

        protected override BTNodeStatus OnExecute(Blackboard bb)
        {
            Character self = bb.Self;
            if (self == null) return BTNodeStatus.Failure;

            var jobInfo = self.CharacterJob;
            var schedule = self.CharacterSchedule;
            if (jobInfo == null || schedule == null) return BTNodeStatus.Failure;

            // If the schedule requires work, do not clock out
            if (schedule.CurrentActivity == ScheduleActivity.Work)
                return BTNodeStatus.Failure;

            // Otherwise, are we still registered at work?
            CommercialBuilding workplace = jobInfo.Workplace;
            if (workplace == null || !workplace.IsWorkerOnShift(self))
                return BTNodeStatus.Failure; // Already clocked out or unemployed

            // Delegate to the native BT action to clock out
            return _actionPunchOut.Execute(bb);
        }

        protected override void OnExit(Blackboard bb)
        {
            _actionPunchOut.Abort(bb);
            base.OnExit(bb);
        }
    }
}
