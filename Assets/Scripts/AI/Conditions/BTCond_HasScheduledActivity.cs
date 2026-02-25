namespace MWI.AI
{
    /// <summary>
    /// Condition : le schedule indique une activité spécifique (Work, Sleep, etc.).
    /// Exécute le BTAction correspondant à l'activité.
    /// </summary>
    public class BTCond_HasScheduledActivity : BTNode
    {
        private BTAction_Work _workAction = new BTAction_Work();
        private BTAction_Idle _sleepAction = new BTAction_Idle(); // TODO: remplacer par SleepBehaviour
        private BTNode _currentAction = null;

        protected override BTNodeStatus OnExecute(Blackboard bb)
        {
            Character self = bb.Self;
            if (self == null) return BTNodeStatus.Failure;

            CharacterSchedule schedule = self.CharacterSchedule;
            if (schedule == null) return BTNodeStatus.Failure;

            ScheduleActivity activity = schedule.CurrentActivity;
            bb.Set(Blackboard.KEY_SCHEDULE_ACTIVITY, activity);

            // Wander est le fallback, pas une activité schedulée 
            if (activity == ScheduleActivity.Wander) return BTNodeStatus.Failure;

            // Choisir le bon action node
            BTNode targetAction = activity switch
            {
                ScheduleActivity.Work => _workAction,
                ScheduleActivity.Sleep => _sleepAction,
                ScheduleActivity.GoHome => _sleepAction, // TODO: GoHomeBehaviour
                ScheduleActivity.Leisure => null, // TODO: LeisureBehaviour
                _ => null
            };

            if (targetAction == null) return BTNodeStatus.Failure;

            // Si on change d'action, abort l'ancienne
            if (_currentAction != null && _currentAction != targetAction)
            {
                _currentAction.Abort(bb);
            }
            _currentAction = targetAction;

            return _currentAction.Execute(bb);
        }

        protected override void OnExit(Blackboard bb)
        {
            _currentAction?.Abort(bb);
            _currentAction = null;
        }
    }
}
