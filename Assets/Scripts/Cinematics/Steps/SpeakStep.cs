using UnityEngine;

namespace MWI.Cinematics
{
    [System.Serializable]
    public class SpeakStep : CinematicStep
    {
        [SerializeField] private string _speakerRoleId;
        [TextArea(2, 8)]
        [SerializeField] private string _lineText;
        [SerializeField] private float _typingSpeedOverride = 0f;     // 0 = use default

        private bool _typingDone;
        private bool _advanceRequested;
        private float _advanceTimerEnd;

        public ActorRoleId SpeakerRoleId => new ActorRoleId(_speakerRoleId);
        public string      LineText      => _lineText;

        public override void OnEnter(CinematicContext ctx)
        {
            _typingDone = false;
            _advanceRequested = false;

            var speaker = ctx.GetActor(SpeakerRoleId);
            if (speaker == null)
            {
                Debug.LogError($"<color=red>[Cinematic]</color> SpeakStep: speaker role '{_speakerRoleId}' could not be resolved on scene '{ctx.Scene?.SceneId}'.");
                _typingDone = true;
                _advanceRequested = true;     // skip this step
                _advanceTimerEnd = Time.time;
                return;
            }

            if (speaker.CharacterSpeech == null)
            {
                Debug.LogError($"<color=red>[Cinematic]</color> SpeakStep: speaker '{speaker.CharacterName}' has no CharacterSpeech component.");
                _typingDone = true;
                _advanceRequested = true;
                _advanceTimerEnd = Time.time;
                return;
            }

            // Close any other open bubble on the speaker before opening a new one
            speaker.CharacterSpeech.CloseSpeech();

            string processedText = ResolvePlaceholders(_lineText, ctx);

            Debug.Log($"<color=cyan>[Cinematic]</color> SpeakStep: '{speaker.CharacterName}' says \"{processedText}\".");

            speaker.CharacterSpeech.SayScripted(
                processedText,
                _typingSpeedOverride,
                onTypingFinished: () =>
                {
                    _typingDone = true;
                    Debug.Log($"<color=cyan>[Cinematic]</color> SpeakStep: typing finished for '{speaker.CharacterName}'.");
                });
        }

        public override void OnExit(CinematicContext ctx)
        {
            // Close bubble after the step ends so the next step's bubble opens clean
            var speaker = ctx.GetActor(SpeakerRoleId);
            speaker?.CharacterSpeech?.CloseSpeech();
        }

        public override bool IsComplete(CinematicContext ctx)
        {
            // Phase 1 — auto-advance 1.5s after typing finishes (placeholder until Phase 2 advance protocol).
            // We use the same 1.5s default that DialogueManager uses for NPC-only dialogues.
            if (!_typingDone) return false;
            if (!_advanceRequested)
            {
                _advanceRequested = true;
                _advanceTimerEnd = Time.time + 1.5f;
            }
            return Time.time >= _advanceTimerEnd;
        }

        private string ResolvePlaceholders(string text, CinematicContext ctx)
        {
            if (string.IsNullOrEmpty(text) || ctx?.Scene == null) return text;

            // [role:Hero].getName  → replaces with the Hero role's display name
            // Simple replace loop — Phase 4 may upgrade to a regex-based formatter if more tags arrive.
            string result = text;
            foreach (var slot in ctx.Scene.Roles)
            {
                string token = $"[role:{slot.RoleId}].getName";
                if (result.Contains(token))
                {
                    var c = ctx.GetActor(slot.RoleId);
                    string nameToInsert = c != null ? c.CharacterName : slot.DisplayName;
                    result = result.Replace(token, nameToInsert);
                }
            }
            return result;
        }
    }
}
