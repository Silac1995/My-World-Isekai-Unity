using System.Collections;
using UnityEngine;

// We're inside namespace MWI.Cinematics, so `Time` resolves to the sibling MWI.Time
// namespace before reaching UnityEngine.Time. Aliasing avoids fully-qualifying every call.
using UTime = UnityEngine.Time;

namespace MWI.Cinematics
{
    /// <summary>
    /// Walk an actor to a world position. Used by MoveActorStep.
    /// Routes through the standard CharacterAction lane so player and NPC parity (rule #22)
    /// is preserved — combat actions, animation hooks, OnCancel cleanup all work normally.
    /// </summary>
    public class CharacterAction_CinematicMoveTo : CharacterAction
    {
        private readonly Vector3 _target;
        private readonly float   _stoppingDist;
        private readonly float   _timeoutSec;

        private Coroutine _watchCoroutine;
        private bool _finished;

        public override bool AllowsMovementDuringAction => true;
        public override bool ShouldPlayGenericActionAnimation => false;
        public override string ActionName => "Cinematic Move";

        public CharacterAction_CinematicMoveTo(
            Character actor,
            Vector3 target,
            float stoppingDist = 1.5f,
            float timeoutSec   = 30f)
            : base(actor, duration: timeoutSec)
        {
            _target       = target;
            _stoppingDist = Mathf.Max(0.1f, stoppingDist);
            _timeoutSec   = Mathf.Max(1f, timeoutSec);
        }

        public override void OnStart()
        {
            if (character == null || character.CharacterMovement == null)
            {
                Debug.LogError($"<color=red>[Cinematic]</color> CharacterAction_CinematicMoveTo: character or CharacterMovement is null. Finishing immediately.");
                FinishOnce();
                return;
            }

            Debug.Log($"<color=cyan>[Cinematic]</color> CharacterAction_CinematicMoveTo: '{character.CharacterName}' moving to {_target} (stoppingDist={_stoppingDist}, timeout={_timeoutSec}s).");

            character.CharacterMovement.SetDestination(_target);
            _watchCoroutine = character.StartCoroutine(WatchArrival());
        }

        public override void OnApplyEffect()
        {
            // No discrete effect — movement IS the action. ActionTimerRoutine may call this
            // if the action runs the full Duration (timeout case) without finishing early;
            // empty body is correct. Arrival fires Finish() via WatchArrival.
        }

        public override void OnCancel()
        {
            if (_watchCoroutine != null && character != null)
            {
                character.StopCoroutine(_watchCoroutine);
                _watchCoroutine = null;
            }
            // Use Unity's overloaded == for fake-null safety (character may be destroyed mid-cancel).
            if (character != null && character.CharacterMovement != null)
                character.CharacterMovement.Stop();
        }

        private void FinishOnce()
        {
            if (_finished) return;
            _finished = true;
            Finish();
        }

        private IEnumerator WatchArrival()
        {
            float elapsed = 0f;
            while (elapsed < _timeoutSec)
            {
                if (character == null) yield break;

                float dist = Vector3.Distance(character.transform.position, _target);
                if (dist <= _stoppingDist)
                {
                    Debug.Log($"<color=cyan>[Cinematic]</color> CharacterAction_CinematicMoveTo: '{character.CharacterName}' arrived (dist {dist:F2} <= {_stoppingDist:F2}).");
                    FinishOnce();
                    yield break;
                }

                elapsed += UTime.deltaTime;
                yield return null;
            }

            Debug.LogWarning($"<color=yellow>[Cinematic]</color> CharacterAction_CinematicMoveTo: '{character?.CharacterName}' timed out after {_timeoutSec}s. Finishing anyway.");
            FinishOnce();
        }
    }
}
