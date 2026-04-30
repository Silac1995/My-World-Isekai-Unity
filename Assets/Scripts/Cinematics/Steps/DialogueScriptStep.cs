using System.Collections.Generic;
using UnityEngine;
using MWI.Dialogue;

// Inside namespace MWI.Cinematics, the bare `Time` symbol resolves to the sibling
// MWI.Time namespace before reaching UnityEngine.Time. Aliasing avoids fully-qualifying.
using UTime = UnityEngine.Time;

namespace MWI.Cinematics
{
    /// <summary>
    /// Plays an existing legacy <see cref="DialogueSO"/> as a single step in the cinematic
    /// timeline. Designed as a reuse helper: legacy dialogues authored via the
    /// <see cref="DialogueManager"/> workflow can be embedded in a richer cinematic
    /// without re-authoring each line as a separate <see cref="SpeakStep"/>.
    ///
    /// <para>Iterates <see cref="DialogueSO.Lines"/> in order. For each line:</para>
    /// <list type="bullet">
    /// <item>Maps <c>DialogueLine.CharacterIndex</c> (1-based) to a <see cref="ActorRoleId"/>
    ///   via <see cref="_roleIdByIndex"/> (designer-authored on this step).</item>
    /// <item>Resolves the speaker via <see cref="CinematicContext.GetActor"/>.</item>
    /// <item>Calls <c>speaker.CharacterSpeech.SayScripted</c> with the line text and a
    ///   length-aware safety timeout.</item>
    /// <item>After the typing-finished callback fires, dwells <see cref="POST_TYPING_DWELL_SEC"/>
    ///   then advances to the next line.</item>
    /// </list>
    ///
    /// <para>Placeholder support: both legacy <c>[indexN].getName</c> and the new cinematic
    /// <c>[role:X].getName</c> syntax are resolved. Use whichever matches your authored content.</para>
    ///
    /// <para><b>Choices</b> on the wrapped <see cref="DialogueSO"/> (if present) are ignored
    /// in Phase 1 — log a warning. Phase 3's <c>ChoiceStep</c> will offer proper integration;
    /// for now, branching dialogues should be split into separate cinematics or use
    /// <see cref="SpeakStep"/> + future <c>ChoiceStep</c> directly.</para>
    /// </summary>
    [System.Serializable]
    public class DialogueScriptStep : CinematicStep
    {
        // Phase 1 safety net: if the typing-finished callback never fires (e.g. malformed
        // prefab missing _speechBubbleStack), un-stick the cinematic after a length-aware
        // timeout. Same constants as SpeakStep — Phase 2's AllMustPress protocol replaces both.
        private const float TYPING_TIMEOUT_PER_CHAR = 0.15f;
        private const float TYPING_TIMEOUT_BASE_SEC = 5f;
        private const float POST_TYPING_DWELL_SEC   = 1.5f;

        [Tooltip("The legacy DialogueSO whose lines this step plays in order.")]
        [SerializeField] private DialogueSO _dialogue;

        [Tooltip("Role IDs ordered by DialogueLine.CharacterIndex (1-based). Index 0 here = CharacterIndex 1, index 1 = CharacterIndex 2, etc. Must contain enough entries to cover the highest CharacterIndex used in the dialogue.")]
        [SerializeField] private List<string> _roleIdByIndex = new();

        // PHASE-1-ONLY: per-line state. Phase 2 rewrites around the AllMustPress press tally;
        // these fields go away.
        private int _currentLineIndex;
        private bool _typingDone;
        private bool _advanceRequested;
        private float _advanceTimerEnd;
        private float _typingTimeoutAt;
        private float _typingTimeoutSpan;
        private bool _initialised;

        public DialogueSO Dialogue => _dialogue;
        public IReadOnlyList<string> RoleIdByIndex => _roleIdByIndex;

        public override void OnEnter(CinematicContext ctx)
        {
            _initialised = true;
            _currentLineIndex = 0;

            if (_dialogue == null)
            {
                Debug.LogError($"<color=red>[Cinematic]</color> DialogueScriptStep: _dialogue is null on scene '{ctx?.Scene?.SceneId}'. Step will instant-complete.");
                return;
            }

            if (_dialogue.HasChoices)
            {
                Debug.LogWarning($"<color=yellow>[Cinematic]</color> DialogueScriptStep: DialogueSO '{_dialogue.name}' has choices — ignored in Phase 1. Phase 3's ChoiceStep will integrate with branching.");
            }

            if (_dialogue.Lines == null || _dialogue.Lines.Count == 0)
            {
                Debug.LogWarning($"<color=yellow>[Cinematic]</color> DialogueScriptStep: DialogueSO '{_dialogue.name}' has no lines. Step will instant-complete.");
                return;
            }

            ShowCurrentLine(ctx);
        }

        public override void OnTick(CinematicContext ctx, float dt)
        {
            if (!_initialised) return;
            if (_dialogue == null || _dialogue.Lines == null) return;
            if (_currentLineIndex >= _dialogue.Lines.Count) return;

            // Safety-net: if typing callback never fires, force the line to complete.
            if (!_typingDone && UTime.time >= _typingTimeoutAt)
            {
                Debug.LogWarning($"<color=yellow>[Cinematic]</color> DialogueScriptStep: line {_currentLineIndex} typing-finished callback timed out after {_typingTimeoutSpan:F1}s. Force-advancing.");
                _typingDone = true;
                _advanceRequested = true;
                _advanceTimerEnd = UTime.time;
            }

            // Idempotent post-typing arm: first tick after typing-done schedules the dwell.
            if (_typingDone && !_advanceRequested)
            {
                _advanceRequested = true;
                _advanceTimerEnd = UTime.time + POST_TYPING_DWELL_SEC;
            }

            // Advance to next line when the dwell is done.
            if (_advanceRequested && UTime.time >= _advanceTimerEnd)
            {
                _currentLineIndex++;
                if (_currentLineIndex < _dialogue.Lines.Count)
                {
                    ShowCurrentLine(ctx);
                }
                // else: IsComplete will return true on the next poll.
            }
        }

        public override bool IsComplete(CinematicContext ctx)
        {
            if (!_initialised) return false;
            if (_dialogue == null || _dialogue.Lines == null || _dialogue.Lines.Count == 0) return true;
            return _currentLineIndex >= _dialogue.Lines.Count;
        }

        public override void OnExit(CinematicContext ctx)
        {
            // Close the last open bubble so the next step starts clean.
            if (_dialogue != null && _currentLineIndex < _dialogue.Lines.Count)
            {
                var line = _dialogue.Lines[_currentLineIndex];
                Character speaker = ResolveSpeakerForLine(line, ctx);
                if (speaker != null && speaker.CharacterSpeech != null)
                    speaker.CharacterSpeech.CloseSpeech();
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private void ShowCurrentLine(CinematicContext ctx)
        {
            _typingDone = false;
            _advanceRequested = false;

            var line = _dialogue.Lines[_currentLineIndex];
            Character speaker = ResolveSpeakerForLine(line, ctx);

            // If speaker can't be resolved, skip this line: arm the timers so the next tick
            // advances. We don't hard-fail the whole step — the surrounding scene may still
            // make sense without one missing line.
            if (speaker == null || speaker.CharacterSpeech == null)
            {
                Debug.LogError($"<color=red>[Cinematic]</color> DialogueScriptStep: line {_currentLineIndex} (CharacterIndex={line.CharacterIndex}) — speaker unresolvable. Skipping.");
                _typingDone = true;
                _advanceRequested = true;
                _advanceTimerEnd = UTime.time;
                return;
            }

            // Close any prior bubble on this speaker before opening a new one.
            speaker.CharacterSpeech.CloseSpeech();

            string processedText = ResolvePlaceholders(line.LineText, ctx);

            int charCount = string.IsNullOrEmpty(processedText) ? 0 : processedText.Length;
            _typingTimeoutSpan = TYPING_TIMEOUT_BASE_SEC + (charCount * TYPING_TIMEOUT_PER_CHAR);
            _typingTimeoutAt = UTime.time + _typingTimeoutSpan;

            Debug.Log($"<color=cyan>[Cinematic]</color> DialogueScriptStep: line {_currentLineIndex}/{_dialogue.Lines.Count - 1} — '{speaker.CharacterName}' says \"{processedText}\".");

            // Capture for null-safety in the closure.
            var speakerForClosure = speaker;
            speaker.CharacterSpeech.SayScripted(
                processedText,
                line.TypingSpeedOverride,
                onTypingFinished: () =>
                {
                    _typingDone = true;
                    if (speakerForClosure != null)
                        Debug.Log($"<color=cyan>[Cinematic]</color> DialogueScriptStep: typing finished for '{speakerForClosure.CharacterName}' (line {_currentLineIndex}).");
                });
        }

        private Character ResolveSpeakerForLine(DialogueLine line, CinematicContext ctx)
        {
            // CharacterIndex is 1-based; map to 0-based index into _roleIdByIndex.
            int mappingIndex = line.CharacterIndex - 1;
            if (mappingIndex < 0 || _roleIdByIndex == null || mappingIndex >= _roleIdByIndex.Count)
            {
                Debug.LogError($"<color=red>[Cinematic]</color> DialogueScriptStep: CharacterIndex={line.CharacterIndex} is out of mapping range (have {_roleIdByIndex?.Count ?? 0} role IDs in _roleIdByIndex).");
                return null;
            }

            string roleIdString = _roleIdByIndex[mappingIndex];
            if (string.IsNullOrEmpty(roleIdString))
            {
                Debug.LogError($"<color=red>[Cinematic]</color> DialogueScriptStep: _roleIdByIndex[{mappingIndex}] is empty (CharacterIndex {line.CharacterIndex} has no role assignment).");
                return null;
            }

            return ctx?.GetActor(new ActorRoleId(roleIdString));
        }

        private string ResolvePlaceholders(string text, CinematicContext ctx)
        {
            if (string.IsNullOrEmpty(text)) return text;

            // Cheap early-out — if no placeholder syntax is present, skip both passes.
            bool hasIndexToken = text.IndexOf("[index", System.StringComparison.Ordinal) >= 0;
            bool hasRoleToken  = text.IndexOf("[role:", System.StringComparison.Ordinal) >= 0;
            if (!hasIndexToken && !hasRoleToken) return text;

            string result = text;

            // Legacy syntax: [indexN].getName → bound character at _roleIdByIndex[N-1]'s name.
            if (hasIndexToken)
            {
                for (int i = 0; i < _roleIdByIndex.Count; i++)
                {
                    int displayIndex = i + 1;
                    string token = $"[index{displayIndex}].getName";
                    if (result.Contains(token))
                    {
                        var c = ctx?.GetActor(new ActorRoleId(_roleIdByIndex[i]));
                        string nameToInsert = (c != null) ? c.CharacterName : _roleIdByIndex[i];
                        result = result.Replace(token, nameToInsert);
                    }
                }
            }

            // New syntax: [role:X].getName → bound character at role X's name. Walks scene.Roles
            // (same fallback logic as SpeakStep.ResolvePlaceholders).
            if (hasRoleToken && ctx?.Scene != null)
            {
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
            }

            return result;
        }
    }
}
