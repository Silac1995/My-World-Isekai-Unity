using UnityEngine;

namespace MWI.Cinematics
{
    [System.Serializable]
    public class SpeakStep : CinematicStep
    {
        // Phase 1 safety net: if the typing-finished callback never fires (e.g. the
        // speaker's _speechBubbleStack is unwired on a malformed prefab), fail-safe
        // out after this many seconds-per-character + a base buffer. Phase 2 replaces
        // the entire auto-advance path with the AllMustPress protocol.
        private const float PHASE1_TYPING_TIMEOUT_PER_CHAR = 0.15f;
        private const float PHASE1_TYPING_TIMEOUT_BASE_SEC = 5f;
        private const float PHASE1_AUTO_ADVANCE_DELAY_SEC  = 1.5f;

        [SerializeField] private string _speakerRoleId;
        [TextArea(2, 8)]
        [SerializeField] private string _lineText;
        [SerializeField] private float _typingSpeedOverride = 0f;     // 0 = use default

        // PHASE-1-ONLY: state for auto-advance. Phase 2 rewrites IsComplete around the
        // server-driven AllMustPress press tally; these fields go away.
        private bool _typingDone;
        private bool _advanceRequested;
        private float _advanceTimerEnd;
        private float _typingTimeoutAt;
        private float _typingTimeoutSpan;   // configured timeout (sec) — cached for accurate logging

        public ActorRoleId SpeakerRoleId => new ActorRoleId(_speakerRoleId);
        public string      LineText      => _lineText;

        public override void OnEnter(CinematicContext ctx)
        {
            _typingDone = false;
            _advanceRequested = false;

            var speaker = ctx.GetActor(SpeakerRoleId);
            // Use Unity's overloaded == to catch fake-null Characters (destroyed but
            // still in BoundRoles dict). C# `?.` doesn't trigger Unity's == overload.
            if (speaker == null)
            {
                Debug.LogError($"<color=red>[Cinematic]</color> SpeakStep: speaker role '{_speakerRoleId}' could not be resolved on scene '{ctx.Scene?.SceneId}'.");
                MarkSkippedAndAdvance();
                return;
            }

            if (speaker.CharacterSpeech == null)
            {
                Debug.LogError($"<color=red>[Cinematic]</color> SpeakStep: speaker '{speaker.CharacterName}' has no CharacterSpeech component.");
                MarkSkippedAndAdvance();
                return;
            }

            // Close any other open bubble on the speaker before opening a new one.
            // CharacterSpeech.CloseSpeech() dismisses the current scripted bubble;
            // ambient bubbles stack independently and are unaffected.
            speaker.CharacterSpeech.CloseSpeech();

            string processedText = ResolvePlaceholders(_lineText, ctx);

            // Compute the safety-net timeout: if the callback never fires we still
            // un-stick the cinematic. Length-aware so longer lines get more headroom.
            int charCount = string.IsNullOrEmpty(processedText) ? 0 : processedText.Length;
            _typingTimeoutSpan = PHASE1_TYPING_TIMEOUT_BASE_SEC + (charCount * PHASE1_TYPING_TIMEOUT_PER_CHAR);
            _typingTimeoutAt = Time.time + _typingTimeoutSpan;

            Debug.Log($"<color=cyan>[Cinematic]</color> SpeakStep: '{speaker.CharacterName}' says \"{processedText}\".");

            // Capture for closure null-safety check after destruction.
            var speakerForClosure = speaker;
            speaker.CharacterSpeech.SayScripted(
                processedText,
                _typingSpeedOverride,
                onTypingFinished: () =>
                {
                    _typingDone = true;
                    if (speakerForClosure != null)
                        Debug.Log($"<color=cyan>[Cinematic]</color> SpeakStep: typing finished for '{speakerForClosure.CharacterName}'.");
                });
        }

        public override void OnTick(CinematicContext ctx, float dt)
        {
            // Safety-net: if typing callback never fires, force-advance after timeout.
            if (!_typingDone && Time.time >= _typingTimeoutAt)
            {
                Debug.LogWarning($"<color=yellow>[Cinematic]</color> SpeakStep: typing-finished callback timed out after {_typingTimeoutSpan:F1}s. Force-advancing.");
                MarkSkippedAndAdvance();
                return;
            }

            // Idempotent state transition: first tick after typing-done arms the
            // 1.5s post-typing dwell. Subsequent ticks no-op until IsComplete fires.
            if (_typingDone && !_advanceRequested)
            {
                _advanceRequested = true;
                _advanceTimerEnd = Time.time + PHASE1_AUTO_ADVANCE_DELAY_SEC;
            }
        }

        public override void OnExit(CinematicContext ctx)
        {
            // Close bubble after the step ends so the next step's bubble opens clean.
            // Use Unity's overloaded == on Character to catch fake-null (destroyed
            // mid-step). C# `?.` short-circuits on C# null only and would NPE on the
            // CharacterSpeech access for a destroyed-but-not-null Unity object.
            var speaker = ctx.GetActor(SpeakerRoleId);
            if (speaker != null && speaker.CharacterSpeech != null)
                speaker.CharacterSpeech.CloseSpeech();
        }

        public override bool IsComplete(CinematicContext ctx) =>
            // PHASE-1-ONLY: auto-advance after typing + 1.5s dwell. Phase 2 replaces with press tally.
            _advanceRequested && Time.time >= _advanceTimerEnd;

        private void MarkSkippedAndAdvance()
        {
            _typingDone = true;
            _advanceRequested = true;
            _advanceTimerEnd = Time.time;     // IsComplete true on next read
        }

        private string ResolvePlaceholders(string text, CinematicContext ctx)
        {
            if (string.IsNullOrEmpty(text) || ctx?.Scene == null) return text;

            // Cheap early-out: if no [role:...] placeholders at all, skip the role scan.
            if (text.IndexOf("[role:", System.StringComparison.Ordinal) < 0) return text;

            // [role:Hero].getName  → replaces with the Hero role's display name
            // Simple replace loop — Phase 4 may upgrade to a regex-based formatter if more tags arrive.
            string result = text;
            foreach (var slot in ctx.Scene.Roles)
            {
                string token = $"[role:{slot.RoleId}].getName";
                if (result.Contains(token))
                {
                    var c = ctx.GetActor(slot.RoleId);
                    string nameToInsert = (c != null) ? c.CharacterName : slot.DisplayName;
                    result = result.Replace(token, nameToInsert);
                }
            }
            return result;
        }
    }
}
