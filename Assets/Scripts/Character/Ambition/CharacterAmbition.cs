using System;
using System.Collections.Generic;
using UnityEngine;

namespace MWI.Ambition
{
    /// <summary>
    /// Per-Character life-goal subsystem. Identical for player and NPC; consumer
    /// flips with controller switch (Character.SwitchToPlayer/NPC).
    /// Server-authoritative — Set/Clear mutations gated by IsServer; replication
    /// via NetworkVariable + ClientRpc fan-out (lands in Phase 7).
    /// </summary>
    public class CharacterAmbition : CharacterSystem
    {
        // Active state
        private AmbitionInstance _current;
        public AmbitionInstance Current => _current;
        public bool HasActive => _current != null;
        public float CurrentProgress01 => _current != null ? _current.Progress01 : 0f;

        // History
        private readonly List<CompletedAmbition> _history = new();
        public IReadOnlyList<CompletedAmbition> History => _history;

        // Events
        public event Action<AmbitionInstance> OnAmbitionSet;
        public event Action<AmbitionInstance, int> OnStepAdvanced; // (instance, newStepIndex)
        public event Action<CompletedAmbition> OnAmbitionCompleted;
        public event Action<CompletedAmbition> OnAmbitionCleared;

        // Public API
        public virtual void SetAmbition(AmbitionSO so, IReadOnlyDictionary<string, object> parameters = null)
        {
            // Server gate lands in Phase 7. For now allow direct call so EditMode tests work.
            DoSetAmbition(so, parameters);
        }

        public virtual void ClearAmbition()
        {
            DoClearAmbition(CompletionReason.ClearedByScript);
        }

        // Internal
        protected void DoSetAmbition(AmbitionSO so, IReadOnlyDictionary<string, object> parameters)
        {
            if (so == null)
            {
                Debug.LogError($"[CharacterAmbition] SetAmbition called with null SO on {name}.");
                return;
            }
            if (!so.ValidateParameters(parameters ?? EmptyDict))
            {
                Debug.LogError($"[CharacterAmbition] SetAmbition aborted: parameter validation failed for {so.name}.");
                return;
            }

            // Replacement: clear current first so it lands in history with ClearedByScript.
            if (_current != null) DoClearAmbition(CompletionReason.ClearedByScript);

            _current = new AmbitionInstance
            {
                SO = so,
                CurrentStepIndex = 0,
                Context = BuildContextFromParameters(so, parameters),
                AssignedDay = ResolveCurrentDay()
            };
            IssueStepQuest(0);
            OnAmbitionSet?.Invoke(_current);
        }

        protected void DoClearAmbition(CompletionReason reason)
        {
            if (_current == null) return;
            var snap = new CompletedAmbition(_current.SO, _current.Context, ResolveCurrentDay(), reason);
            CancelStepQuest();
            _current = null;
            _history.Add(snap);
            if (reason == CompletionReason.Completed) OnAmbitionCompleted?.Invoke(snap);
            else OnAmbitionCleared?.Invoke(snap);
        }

        protected void IssueStepQuest(int stepIndex)
        {
            if (_current == null || _current.SO == null) return;
            var so = _current.SO;
            if (stepIndex < 0 || stepIndex >= so.Quests.Count) return;
            var qso = so.Quests[stepIndex];
            if (qso == null) return;

            var aq = new AmbitionQuest(qso, _character, _current.Context);
            _current.CurrentStepQuest = aq;
            _current.CurrentStepIndex = stepIndex;

            aq.OnStateChanged += HandleStepStateChanged;
            aq.BindContext(_current.Context);

            // Add to CharacterQuestLog so player UI / save / sync pick it up.
            _character?.CharacterQuestLog?.TryClaim(aq);
        }

        protected void CancelStepQuest()
        {
            if (_current?.CurrentStepQuest == null) return;
            var aq = _current.CurrentStepQuest;
            aq.OnStateChanged -= HandleStepStateChanged;
            aq.Cancel();
            _character?.CharacterQuestLog?.TryAbandon(aq);
            _current.CurrentStepQuest = null;
        }

        private void HandleStepStateChanged(MWI.Quests.IQuest q)
        {
            if (_current == null || q != _current.CurrentStepQuest) return;
            if (q.State != MWI.Quests.QuestState.Completed) return;

            // Detach the old listener and remove from quest log.
            var aq = _current.CurrentStepQuest;
            aq.OnStateChanged -= HandleStepStateChanged;
            _character?.CharacterQuestLog?.TryAbandon(aq);
            _current.CurrentStepQuest = null;

            // Last step? -> Completed transition. Otherwise advance.
            if (_current.CurrentStepIndex >= _current.SO.Quests.Count - 1)
            {
                DoClearAmbition(CompletionReason.Completed);
            }
            else
            {
                int next = _current.CurrentStepIndex + 1;
                IssueStepQuest(next);
                OnStepAdvanced?.Invoke(_current, next);
            }
        }

        // Helpers
        private static readonly Dictionary<string, object> EmptyDict = new();

        protected virtual int ResolveCurrentDay()
        {
            return _character?.TimeManager?.CurrentDay ?? 0;
        }

        private static AmbitionContext BuildContextFromParameters(
            AmbitionSO so, IReadOnlyDictionary<string, object> parameters)
        {
            var ctx = new AmbitionContext();
            if (parameters == null) return ctx;
            foreach (var kvp in parameters)
            {
                try { ctx.Set<object>(kvp.Key, kvp.Value); }
                catch (Exception e) { Debug.LogException(e); }
            }
            return ctx;
        }

        // Test seams
        // Allows EditMode tests to pump state without instantiating a real Character.
        internal void TEST_ForceState(AmbitionInstance instance) { _current = instance; }
        internal void TEST_ForceHistory(IEnumerable<CompletedAmbition> list)
        { _history.Clear(); _history.AddRange(list); }
    }
}
