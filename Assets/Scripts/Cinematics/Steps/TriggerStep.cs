using UnityEngine;
using UnityEngine.Events;

namespace MWI.Cinematics
{
    /// <summary>
    /// Fire-and-forget step that fires a CinematicEffectSO and/or a UnityEvent.
    /// Completes on the next tick — pair with a following WaitStep if a designer
    /// wants the cinematic to dwell on the effect.
    ///
    /// "Trigger before/after a dialog line" pattern: place a TriggerStep
    /// immediately before/after the relevant SpeakStep in the timeline.
    /// </summary>
    [System.Serializable]
    public class TriggerStep : CinematicStep
    {
        [SerializeField] private CinematicEffectSO _effect;
        [SerializeField] private UnityEvent _eventHook;

        public override void OnEnter(CinematicContext ctx)
        {
            Debug.Log($"<color=cyan>[Cinematic]</color> TriggerStep entered (effect={(_effect != null ? _effect.name : "<none>")}).");

            // Defensive: a misconfigured effect should not crash the whole director (rule #31).
            // Try/catch is acceptable here because OnEnter fires once per step instance, not per frame.
            try { _effect?.Apply(ctx); }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                Debug.LogError($"<color=red>[Cinematic]</color> TriggerStep: effect '{(_effect != null ? _effect.name : "<none>")}' threw — continuing.");
            }

            try { _eventHook?.Invoke(); }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                Debug.LogError($"<color=red>[Cinematic]</color> TriggerStep: UnityEvent threw — continuing.");
            }
        }

        // IsComplete inherits from CinematicStep base → returns true → fire-and-forget.
    }
}
