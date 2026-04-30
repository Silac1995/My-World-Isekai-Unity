using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MWI.Cinematics
{
    /// <summary>
    /// Server-side runtime that iterates a CinematicSceneSO's steps end-to-end.
    ///
    /// Phase 1: plain MonoBehaviour, single-player / server-only. Spawned as a child of
    /// a host-only "CinematicDirectors" container GameObject for the duration of the scene.
    ///
    /// Phase 2 will promote to NetworkBehaviour with NetworkObject spawn-with-observers,
    /// ServerRpc/ClientRpc, and the AllMustPress advance-press protocol.
    /// </summary>
    public class CinematicDirector : MonoBehaviour
    {
        private CinematicSceneSO _scene;
        private CinematicContext _ctx;
        private bool _running;
        private bool _aborted;

        public CinematicSceneSO Scene => _scene;
        public bool IsRunning => _running;

        // Step queue allows future ChoiceStep (Phase 3) to push branch steps onto the front
        // without rebuilding. Phase 1 just iterates the scene's steps once.
        private readonly LinkedList<ICinematicStep> _stepQueue = new();

        public void Initialize(CinematicSceneSO scene, CinematicContext ctx)
        {
            _scene = scene;
            _ctx   = ctx;
            _ctx.Scene    = scene;
            _ctx.Director = this;
            _ctx.StartTimeSim = Time.time;

            foreach (var step in scene.Steps)
            {
                if (step != null) _stepQueue.AddLast(step);
            }

            Debug.Log($"<color=cyan>[Cinematic]</color> Director initialized for scene '{scene.SceneId}' with {_stepQueue.Count} steps.");
        }

        public void RunScene()
        {
            if (_running) return;
            _running = true;
            StartCoroutine(StepLoop());
        }

        public void Abort(CinematicEndReason reason = CinematicEndReason.Aborted)
        {
            if (!_running || _aborted) return;
            _aborted = true;
            Debug.LogWarning($"<color=yellow>[Cinematic]</color> Director aborting scene '{_scene?.SceneId}' (reason={reason}).");
        }

        private IEnumerator StepLoop()
        {
            int stepNumber = 0;
            ICinematicStep currentStep = null;

            while (_stepQueue.Count > 0 && !_aborted)
            {
                currentStep = _stepQueue.First.Value;
                _stepQueue.RemoveFirst();

                Debug.Log($"<color=cyan>[Cinematic]</color> Director: entering step {stepNumber} ({currentStep.GetType().Name}).");

                bool entered = false;
                try
                {
                    currentStep.OnEnter(_ctx);
                    entered = true;
                }
                catch (System.Exception e)
                {
                    Debug.LogException(e);
                    Debug.LogError($"<color=red>[Cinematic]</color> Step {stepNumber} OnEnter threw — skipping step.");
                }

                if (entered)
                {
                    // Tick + IsComplete loop. `yield return null` MUST live OUTSIDE try/catch
                    // (C# disallows yield inside catch). The try/catch wraps only the step's
                    // OnTick + IsComplete calls.
                    while (!_aborted)
                    {
                        bool isComplete = false;
                        bool tickThrew = false;
                        try
                        {
                            currentStep.OnTick(_ctx, Time.deltaTime);
                            isComplete = currentStep.IsComplete(_ctx);
                        }
                        catch (System.Exception e)
                        {
                            Debug.LogException(e);
                            Debug.LogError($"<color=red>[Cinematic]</color> Step {stepNumber} OnTick/IsComplete threw — treating as complete.");
                            tickThrew = true;
                        }

                        if (isComplete || tickThrew) break;
                        yield return null;
                    }

                    try
                    {
                        currentStep.OnExit(_ctx);
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogException(e);
                        Debug.LogError($"<color=red>[Cinematic]</color> Step {stepNumber} OnExit threw — continuing.");
                    }
                }

                currentStep = null;
                stepNumber++;
            }

            EndScene(_aborted ? CinematicEndReason.Aborted : CinematicEndReason.Completed);
        }

        private void EndScene(CinematicEndReason reason)
        {
            // Clear actor flags + record completion in per-character history.
            if (_ctx != null)
            {
                foreach (var kvp in _ctx.BoundRoles)
                {
                    var actor = kvp.Value;
                    // Unity-style != null catches fake-null (destroyed) Characters.
                    if (actor == null) continue;

                    if (actor.CharacterCinematicState != null)
                    {
                        actor.CharacterCinematicState.ClearActiveActor();
                        if (reason == CinematicEndReason.Completed && _scene != null)
                        {
                            actor.CharacterCinematicState.MarkSceneCompleted(_scene.SceneId);
                            actor.CharacterCinematicState.RemovePendingScene(_scene.SceneId);
                        }
                    }
                }
            }

            Debug.Log($"<color=cyan>[Cinematic]</color> Director: scene '{_scene?.SceneId}' ended (reason={reason}).");

            _running = false;
            // Phase 1: destroy the director GameObject. Phase 2 will use NetworkObject.Despawn.
            Destroy(gameObject);
        }
    }
}
