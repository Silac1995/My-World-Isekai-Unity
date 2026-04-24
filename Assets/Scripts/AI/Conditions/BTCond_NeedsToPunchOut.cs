using UnityEngine;
using System.Linq;

namespace MWI.AI
{
    /// <summary>
    /// Condition : Le personnage vient de finir son shift (son ScheduleActivity n'est PLUS Work)
    /// mais il est TOUJOURS enregistré physiquement comme on-shift dans le bâtiment
    /// (IsWorkerOnShift retourne vrai). Il DOIT dépointer.
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

            // Si le schedule demande de travailler, alors on ne dépointe pas
            if (schedule.CurrentActivity == ScheduleActivity.Work)
                return BTNodeStatus.Failure;

            // Sinon, est-on encore enregistré au travail ?
            CommercialBuilding workplace = jobInfo.Workplace;
            if (workplace == null || !workplace.IsWorkerOnShift(self))
                return BTNodeStatus.Failure; // Déjà dépointé ou chômeur

            // On délègue à l'action native de BT pour dépointer
            return _actionPunchOut.Execute(bb);
        }

        protected override void OnExit(Blackboard bb)
        {
            _actionPunchOut.Abort(bb);
            base.OnExit(bb);
        }
    }
}
