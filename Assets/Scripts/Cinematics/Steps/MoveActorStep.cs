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

        // Cached on OnEnter so OnExit can ClearCurrentAction without re-resolving roles
        // (the role binding may have shifted by abort time).
        private Character _actor;
        private CharacterAction_CinematicMoveTo _action;
        private bool _actionFinished;
        private bool _instantComplete;

        public override void OnEnter(CinematicContext ctx)
        {
            _actor = null;
            _action = null;
            _actionFinished = false;
            _instantComplete = false;

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

            _actor = actor;
            _action = new CharacterAction_CinematicMoveTo(
                actor, target, _stoppingDist, _timeoutSec);

            _action.OnActionFinished += OnActionFinishedHandler;

            // CharacterActions.ExecuteAction returns false if the actor is already running
            // another action. We treat that as instant-complete + warning so a busy actor
            // doesn't hang the cinematic; designers should sequence steps such that actors
            // are free at the moment a MoveActorStep runs for them.
            bool started = actor.CharacterActions.ExecuteAction(_action);
            if (!started)
            {
                Debug.LogWarning($"<color=yellow>[Cinematic]</color> MoveActorStep: ExecuteAction returned false for '{actor.CharacterName}' (busy or rejected). Skipping move.");
                _action.OnActionFinished -= OnActionFinishedHandler;
                _action = null;
                _actor = null;
                _instantComplete = true;
                return;
            }

            if (!_blocking) _instantComplete = true;
        }

        public override void OnExit(CinematicContext ctx)
        {
            // Non-blocking semantics: the whole point is to let the move run in the
            // background while later steps execute. Do NOT cancel on natural OnExit.
            // Phase 2: register orphaned actions on CinematicContext so scene-abort
            // can mass-cancel them. For Phase 1, we accept the orphan: the action
            // completes itself via WatchArrival arrival OR ActionTimerRoutine timeout.
            if (!_blocking)
            {
                if (_action != null) _action.OnActionFinished -= OnActionFinishedHandler;
                _action = null;
                _actor = null;
                return;
            }

            // Blocking case: if we're here with an action still running, the cinematic
            // is aborting (or the step somehow exited without _actionFinished firing).
            // Use CharacterActions.ClearCurrentAction() — the canonical full cleanup
            // path that stops ActionTimerRoutine, calls OnCancel, broadcasts the cancel
            // ClientRpc, resets animator triggers, and clears _currentAction. Calling
            // _action.OnCancel() directly would leave the actor stuck with CurrentAction
            // until ActionTimerRoutine eventually fired (up to _timeoutSec seconds).
            if (_action != null && !_actionFinished)
            {
                _action.OnActionFinished -= OnActionFinishedHandler;
                if (_actor != null && _actor.CharacterActions != null)
                {
                    _actor.CharacterActions.ClearCurrentAction();
                }
                else
                {
                    // Fallback: actor or CharacterActions destroyed — call OnCancel directly
                    // so at least our local cleanup runs. Stale CurrentAction is the
                    // CharacterActions instance's problem at that point (it's gone).
                    _action.OnCancel();
                }
            }

            _action = null;
            _actor = null;
        }

        public override bool IsComplete(CinematicContext ctx) =>
            _instantComplete || _actionFinished;

        private void OnActionFinishedHandler()
        {
            _actionFinished = true;
        }
    }
}
