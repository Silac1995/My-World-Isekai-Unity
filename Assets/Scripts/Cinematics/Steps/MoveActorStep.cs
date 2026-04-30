using UnityEngine;

namespace MWI.Cinematics
{
    [System.Serializable]
    public class MoveActorStep : CinematicStep
    {
        public enum TargetMode { Role, WorldPos }

        [SerializeField] private string _actorRoleId;
        [SerializeField] private TargetMode _targetMode = TargetMode.Role;
        [SerializeField] private string _targetRoleId;
        [SerializeField] private Vector3 _targetPos;
        [SerializeField] private float _stoppingDist = 1.5f;
        [SerializeField] private bool  _blocking = true;
        [SerializeField] private float _timeoutSec = 30f;

        private CharacterAction_CinematicMoveTo _action;
        private bool _actionFinished;
        private bool _instantComplete;

        public override void OnEnter(CinematicContext ctx)
        {
            _actionFinished = false;
            _instantComplete = false;
            _action = null;

            var actor = ctx.GetActor(new ActorRoleId(_actorRoleId));
            if (actor == null)
            {
                Debug.LogError($"<color=red>[Cinematic]</color> MoveActorStep: actor role '{_actorRoleId}' could not be resolved.");
                _instantComplete = true;
                return;
            }

            Vector3 target;
            switch (_targetMode)
            {
                case TargetMode.Role:
                {
                    var targetActor = ctx.GetActor(new ActorRoleId(_targetRoleId));
                    if (targetActor == null)
                    {
                        Debug.LogError($"<color=red>[Cinematic]</color> MoveActorStep: target role '{_targetRoleId}' could not be resolved.");
                        _instantComplete = true;
                        return;
                    }
                    target = targetActor.transform.position;
                    break;
                }
                case TargetMode.WorldPos:
                    target = _targetPos;
                    break;
                default:
                    Debug.LogError($"<color=red>[Cinematic]</color> MoveActorStep: unknown target mode {_targetMode}.");
                    _instantComplete = true;
                    return;
            }

            _action = new CharacterAction_CinematicMoveTo(
                actor, target, _stoppingDist, _timeoutSec);

            _action.OnActionFinished += OnActionFinishedHandler;

            // ExecuteAction (NOT Enqueue — that was a plan inaccuracy). Returns false if
            // actor is already in another action. We treat that as instant-complete + warning
            // so a busy actor doesn't hang the cinematic; designers should sequence steps
            // such that actors are free at the moment a MoveActorStep runs for them.
            bool started = actor.CharacterActions != null
                        && actor.CharacterActions.ExecuteAction(_action);
            if (!started)
            {
                Debug.LogWarning($"<color=yellow>[Cinematic]</color> MoveActorStep: ExecuteAction returned false for '{actor.CharacterName}' (busy or rejected). Skipping move.");
                _action.OnActionFinished -= OnActionFinishedHandler;
                _action = null;
                _instantComplete = true;
                return;
            }

            if (!_blocking) _instantComplete = true;
        }

        public override void OnExit(CinematicContext ctx)
        {
            // If the step is aborted (or non-blocking + step torn down later), cancel the action so
            // the actor stops walking. Unsubscribe before calling OnCancel to prevent double-fire.
            if (_action != null && !_actionFinished)
            {
                _action.OnActionFinished -= OnActionFinishedHandler;
                _action.OnCancel();
            }
            _action = null;
        }

        public override bool IsComplete(CinematicContext ctx) =>
            _instantComplete || _actionFinished;

        private void OnActionFinishedHandler()
        {
            _actionFinished = true;
        }
    }
}
