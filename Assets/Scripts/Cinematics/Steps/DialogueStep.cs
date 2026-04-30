using System.Collections.Generic;
using UnityEngine;

// Inside namespace MWI.Cinematics, the bare `Time` symbol resolves to the sibling
// MWI.Time namespace before reaching UnityEngine.Time. Aliasing avoids fully-qualifying.
using UTime = UnityEngine.Time;

namespace MWI.Cinematics
{
    /// <summary>
    /// One inline dialogue line authored inside a <see cref="DialogueStep"/>. Same
    /// authoring shape as the legacy <c>DialogueLine</c> on <c>DialogueSO</c>, but
    /// references the speaker by <see cref="ActorRoleId"/> (string) instead of a
    /// 1-based participant index — so the cinematic system's named-role binding
    /// flows through naturally.
    /// </summary>
    [System.Serializable]
    public class CinematicDialogueLine
    {
        [Tooltip("The Role Id of the speaker for this line. Must match a Role Id declared in the scene's Cast.")]
        [SerializeField] private string _speakerRoleId;

        [TextArea(2, 8)]
        [Tooltip("The line text. Supports [role:X].getName placeholders.")]
        [SerializeField] private string _lineText;

        [Tooltip("Per-line typing speed override. 0 = use the default speed configured on the speech bubble.")]
        [SerializeField] private float _typingSpeedOverride = 0f;

        public string SpeakerRoleId      => _speakerRoleId;
        public string LineText           => _lineText;
        public float  TypingSpeedOverride => _typingSpeedOverride;
    }

    /// <summary>
    /// Multi-line dialogue authored INLINE in the cinematic timeline. The canonical
    /// way to express a back-and-forth conversation as a single step.
    ///
    /// <para>
    /// Inspired by the legacy <see cref="MWI.Dialogue.DialogueSO"/> authoring shape
    /// (a flat list of lines, each with a speaker + text + typing-speed override) but
    /// adapted for the cinematic system: speakers are referenced by Role Id (string,
    /// matching a Cast entry) instead of by 1-based participant index. No external
    /// DialogueSO asset, no role/index mapping list — the lines belong to the step
    /// and are authored where they're played.
    /// </para>
    ///
    /// <para>
    /// Use <see cref="SpeakStep"/> for a single one-off line; use this step for
    /// conversational chunks. Both share the same Phase 1 auto-advance cadence
    /// (1.5s post-typing dwell) and the same length-aware safety timeout against
    /// dropped <c>onTypingFinished</c> callbacks.
    /// </para>
    ///
    /// <para>
    /// Placeholder syntax: <c>[role:Hero].getName</c> resolves to the Character
    /// bound to role <c>Hero</c>'s <c>CharacterName</c>. Same syntax as
    /// <see cref="SpeakStep"/>.
    /// </para>
    /// </summary>
    [System.Serializable]
    public class DialogueStep : CinematicStep
    {
        // Phase 1 safety net: same constants as SpeakStep — Phase 2's AllMustPress
        // protocol replaces both auto-advance paths.
        private const float TYPING_TIMEOUT_PER_CHAR = 0.15f;
        private const float TYPING_TIMEOUT_BASE_SEC = 5f;
        private const float POST_TYPING_DWELL_SEC   = 1.5f;

        [Tooltip("Ordered list of dialogue lines. Each line names its speaker by Role Id.")]
        [SerializeField] private List<CinematicDialogueLine> _lines = new();

        public IReadOnlyList<CinematicDialogueLine> Lines => _lines;

        // PHASE-1-ONLY: per-line state. Phase 2 rewrites around the AllMustPress press
        // tally; these fields go away.
        private int _currentLineIndex;
        private bool _typingDone;
        private bool _advanceRequested;
        private float _advanceTimerEnd;
        private float _typingTimeoutAt;
        private float _typingTimeoutSpan;
        private bool _initialised;

        public override void OnEnter(CinematicContext ctx)
        {
            _initialised = true;
            _currentLineIndex = 0;

            if (_lines == null || _lines.Count == 0)
            {
                Debug.LogWarning($"<color=yellow>[Cinematic]</color> DialogueStep: no lines authored on scene '{ctx?.Scene?.SceneId}'. Step will instant-complete.");
                return;
            }

            ShowCurrentLine(ctx);
        }

        public override void OnTick(CinematicContext ctx, float dt)
        {
            if (!_initialised) return;
            if (_lines == null) return;
            if (_currentLineIndex >= _lines.Count) return;

            // Safety-net: if typing callback never fires, force the line to complete.
            if (!_typingDone && UTime.time >= _typingTimeoutAt)
            {
                Debug.LogWarning($"<color=yellow>[Cinematic]</color> DialogueStep: line {_currentLineIndex} typing-finished callback timed out after {_typingTimeoutSpan:F1}s. Force-advancing.");
                _typingDone = true;
                _advanceRequested = true;
                _advanceTimerEnd = UTime.time;
            }

            if (_typingDone && !_advanceRequested)
            {
                bool hasParticipatingPlayers = ctx?.ParticipatingPlayers != null
                    && ctx.ParticipatingPlayers.Count > 0;

                if (hasParticipatingPlayers)
                {
                    // Player-driven mode: wait for an advance-press from any participating
                    // player. Mirrors DialogueManager's wait-for-input behaviour. The press
                    // is forwarded via PlayerController → CinematicAdvance.
                    if (CinematicAdvance.WasAdvanceRequestedThisFrame())
                    {
                        _advanceRequested = true;
                        _advanceTimerEnd = UTime.time;     // advance immediately on next check
                    }
                }
                else
                {
                    // NPC-only mode: auto-advance 1.5s after typing finishes.
                    _advanceRequested = true;
                    _advanceTimerEnd = UTime.time + POST_TYPING_DWELL_SEC;
                }
            }

            // Advance to next line when the dwell / press condition is satisfied.
            if (_advanceRequested && UTime.time >= _advanceTimerEnd)
            {
                _currentLineIndex++;
                if (_currentLineIndex < _lines.Count)
                {
                    ShowCurrentLine(ctx);
                }
                // else: IsComplete returns true on the next poll.
            }
        }

        public override bool IsComplete(CinematicContext ctx)
        {
            if (!_initialised) return false;
            if (_lines == null || _lines.Count == 0) return true;
            return _currentLineIndex >= _lines.Count;
        }

        public override void OnExit(CinematicContext ctx)
        {
            // Close the last open bubble so the next step starts clean.
            if (_lines != null && _currentLineIndex < _lines.Count)
            {
                var line = _lines[_currentLineIndex];
                if (line != null)
                {
                    var speaker = ctx?.GetActor(new ActorRoleId(line.SpeakerRoleId));
                    if (speaker != null && speaker.CharacterSpeech != null)
                        speaker.CharacterSpeech.CloseSpeech();
                }
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private void ShowCurrentLine(CinematicContext ctx)
        {
            _typingDone = false;
            _advanceRequested = false;

            var line = _lines[_currentLineIndex];
            if (line == null)
            {
                Debug.LogError($"<color=red>[Cinematic]</color> DialogueStep: line {_currentLineIndex} is null. Skipping.");
                MarkLineSkipped();
                return;
            }

            if (string.IsNullOrEmpty(line.SpeakerRoleId))
            {
                Debug.LogError($"<color=red>[Cinematic]</color> DialogueStep: line {_currentLineIndex} has empty SpeakerRoleId. Skipping.");
                MarkLineSkipped();
                return;
            }

            var speaker = ctx.GetActor(new ActorRoleId(line.SpeakerRoleId));
            if (speaker == null)
            {
                Debug.LogError($"<color=red>[Cinematic]</color> DialogueStep: line {_currentLineIndex} — speaker role '{line.SpeakerRoleId}' could not be resolved on scene '{ctx?.Scene?.SceneId}'.");
                MarkLineSkipped();
                return;
            }

            if (speaker.CharacterSpeech == null)
            {
                Debug.LogError($"<color=red>[Cinematic]</color> DialogueStep: line {_currentLineIndex} — speaker '{speaker.CharacterName}' has no CharacterSpeech component.");
                MarkLineSkipped();
                return;
            }

            // Close any prior bubble on the speaker before opening a new one.
            speaker.CharacterSpeech.CloseSpeech();

            string processedText = ResolvePlaceholders(line.LineText, ctx);

            int charCount = string.IsNullOrEmpty(processedText) ? 0 : processedText.Length;
            _typingTimeoutSpan = TYPING_TIMEOUT_BASE_SEC + (charCount * TYPING_TIMEOUT_PER_CHAR);
            _typingTimeoutAt = UTime.time + _typingTimeoutSpan;

            Debug.Log($"<color=cyan>[Cinematic]</color> DialogueStep: line {_currentLineIndex}/{_lines.Count - 1} — '{speaker.CharacterName}' says \"{processedText}\".");

            // Capture for null-safety in the closure.
            var speakerForClosure = speaker;
            speaker.CharacterSpeech.SayScripted(
                processedText,
                line.TypingSpeedOverride,
                onTypingFinished: () =>
                {
                    _typingDone = true;
                    if (speakerForClosure != null)
                        Debug.Log($"<color=cyan>[Cinematic]</color> DialogueStep: typing finished for '{speakerForClosure.CharacterName}' (line {_currentLineIndex}).");
                });
        }

        private void MarkLineSkipped()
        {
            // Skip this line: arm the timers so the next tick advances to the next line.
            _typingDone = true;
            _advanceRequested = true;
            _advanceTimerEnd = UTime.time;
        }

        private string ResolvePlaceholders(string text, CinematicContext ctx)
        {
            if (string.IsNullOrEmpty(text) || ctx?.Scene == null) return text;

            // Cheap early-out: if no [role:...] placeholders, skip the role scan.
            if (text.IndexOf("[role:", System.StringComparison.Ordinal) < 0) return text;

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
