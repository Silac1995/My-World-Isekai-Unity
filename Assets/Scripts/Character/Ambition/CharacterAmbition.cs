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
    public class CharacterAmbition : CharacterSystem, ICharacterSaveData<AmbitionSaveData>
    {
        // Active state
        private AmbitionInstance _current;
        public AmbitionInstance Current => _current;
        public bool HasActive => _current != null;
        public float CurrentProgress01 => _current != null ? _current.Progress01 : 0f;

        // History
        private readonly List<CompletedAmbition> _history = new();
        public IReadOnlyList<CompletedAmbition> History => _history;

        // Queue of (key, kind, serializedValue, targetCtx) tuples whose Character refs couldn't resolve at load.
        // Retried whenever Character.OnCharacterSpawned fires for any character.
        private readonly List<(string key, ContextValueKind kind, string serializedValue, AmbitionContext targetCtx)> _deferredBindings = new();

        // Events
        public event Action<AmbitionInstance> OnAmbitionSet;
        public event Action<AmbitionInstance, int> OnStepAdvanced; // (instance, newStepIndex)
        public event Action<CompletedAmbition> OnAmbitionCompleted;
        public event Action<CompletedAmbition> OnAmbitionCleared;

        // Lifecycle
        protected override void OnEnable()
        {
            base.OnEnable();
            Character.OnCharacterSpawned += HandleCharacterSpawned;
        }

        protected override void OnDisable()
        {
            Character.OnCharacterSpawned -= HandleCharacterSpawned;
            CancelStepQuest();
            base.OnDisable();
        }

        private void HandleCharacterSpawned(Character c)
        {
            if (_deferredBindings.Count == 0 || c == null) return;
            for (int i = _deferredBindings.Count - 1; i >= 0; i--)
            {
                var d = _deferredBindings[i];
                if (d.kind != ContextValueKind.Character) continue;
                if (d.serializedValue != c.CharacterId) continue;
                d.targetCtx?.Set(d.key, c);
                _deferredBindings.RemoveAt(i);
            }
        }

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

        // ── ICharacterSaveData<AmbitionSaveData> ───────────────────────
        public string SaveKey => "CharacterAmbition";
        public int LoadPriority => 80;

        public AmbitionSaveData Serialize()
        {
            var dto = new AmbitionSaveData();
            foreach (var h in _history) dto.History.Add(SerializeCompleted(h));
            if (_current != null && _current.SO != null)
            {
                dto.ActiveAmbitionSOGuid = AmbitionRegistry.GetGuid(_current.SO);
                dto.Context = SerializeContext(_current.Context);
                dto.CurrentStepIndex = _current.CurrentStepIndex;
                dto.TaskStates = SerializeActiveTasks(_current.CurrentStepQuest);
                dto.AssignedDay = _current.AssignedDay;
            }
            return dto;
        }

        public void Deserialize(AmbitionSaveData data)
        {
            if (data == null) return;
            _history.Clear();
            foreach (var dtoH in data.History)
                _history.Add(DeserializeCompleted(dtoH));

            if (string.IsNullOrEmpty(data.ActiveAmbitionSOGuid))
            {
                _current = null;
                return;
            }

            var so = AmbitionRegistry.Get(data.ActiveAmbitionSOGuid);
            if (so == null)
            {
                Debug.LogError($"[CharacterAmbition] Saved AmbitionSO '{data.ActiveAmbitionSOGuid}' not found in registry. Clearing.");
                _current = null;
                return;
            }

            var ctx = DeserializeContext(data.Context, target: null);
            // Guard: empty Quests list would underflow Mathf.Clamp(x, 0, -1).
            int clampMax = Mathf.Max(0, so.Quests.Count - 1);
            _current = new AmbitionInstance
            {
                SO = so,
                CurrentStepIndex = Mathf.Clamp(data.CurrentStepIndex, 0, clampMax),
                Context = ctx,
                AssignedDay = data.AssignedDay
            };
            // Wire deferred-binding targets to the live context now that it exists.
            for (int i = 0; i < _deferredBindings.Count; i++)
            {
                var d = _deferredBindings[i];
                if (d.targetCtx == null) _deferredBindings[i] = (d.key, d.kind, d.serializedValue, ctx);
            }

            IssueStepQuest(_current.CurrentStepIndex);

            // Restore mid-task state on the freshly issued AmbitionQuest.
            if (_current.CurrentStepQuest is AmbitionQuest aq && data.TaskStates != null)
            {
                foreach (var ts in data.TaskStates)
                {
                    if (ts.TaskIndexInQuest < 0 || ts.TaskIndexInQuest >= aq.Tasks.Count) continue;
                    aq.Tasks[ts.TaskIndexInQuest]?.DeserializeState(ts.SerializedState);
                }
            }

            SafetyCheckOnLoaded();
        }

        private CompletedAmbition DeserializeCompleted(CompletedAmbitionDTO dto)
        {
            var so = AmbitionRegistry.Get(dto.AmbitionSOGuid);
            var ctx = DeserializeContext(dto.FinalContext, target: null);
            return new CompletedAmbition(so, ctx, dto.CompletedDay, dto.Reason);
        }

        private AmbitionContext DeserializeContext(List<ContextEntryDTO> entries, AmbitionContext target)
        {
            var ctx = target ?? new AmbitionContext();
            if (entries == null) return ctx;
            foreach (var e in entries)
            {
                switch (e.Kind)
                {
                    case ContextValueKind.Character:
                        var c = Character.FindByUUID(e.SerializedValue);
                        if (c != null) ctx.Set(e.Key, c);
                        else _deferredBindings.Add((e.Key, e.Kind, e.SerializedValue, ctx));
                        break;
                    case ContextValueKind.Zone:
                        // No unified zone-by-id registry in v1 — see Task 24 deviation. Wire when WorldZoneRegistry lands.
                        Debug.LogWarning($"[CharacterAmbition] Zone context resolution not yet wired for key '{e.Key}'.");
                        break;
                    case ContextValueKind.AmbitionSO:
                        var amb = AmbitionRegistry.Get(e.SerializedValue);
                        if (amb != null) ctx.Set(e.Key, amb);
                        break;
                    case ContextValueKind.QuestSO:
                        var qs = QuestRegistry.Get(e.SerializedValue);
                        if (qs != null) ctx.Set(e.Key, qs);
                        break;
                    case ContextValueKind.ItemSO:
                        Debug.LogWarning($"[CharacterAmbition] ItemSO context resolution not yet wired for key '{e.Key}'.");
                        break;
                    case ContextValueKind.NeedSO:
                        Debug.LogWarning($"[CharacterAmbition] NeedSO context resolution not yet wired for key '{e.Key}'.");
                        break;
                    case ContextValueKind.Enum:
                        if (!string.IsNullOrEmpty(e.SerializedValue))
                        {
                            var parts = e.SerializedValue.Split('|');
                            if (parts.Length == 2)
                            {
                                var t = System.Type.GetType(parts[0]);
                                if (t != null && System.Enum.TryParse(t, parts[1], out var ev))
                                    ctx.Set(e.Key, ev);
                            }
                        }
                        break;
                    case ContextValueKind.Primitive:
                        if (e.SerializedValue == null) break;
                        if (int.TryParse(e.SerializedValue, out var iVal)) ctx.Set(e.Key, iVal);
                        else if (float.TryParse(e.SerializedValue, out var fVal)) ctx.Set(e.Key, fVal);
                        else if (bool.TryParse(e.SerializedValue, out var bVal)) ctx.Set(e.Key, bVal);
                        else ctx.Set(e.Key, e.SerializedValue);
                        break;
                }
            }
            return ctx;
        }

        private void SafetyCheckOnLoaded()
        {
            if (_current == null) return;

            if (!_current.SO.ValidateParameters(_current.Context.AsReadOnly()))
            {
                Debug.LogError($"[CharacterAmbition] Saved ambition {_current.SO.name} failed parameter validation on load. Clearing.");
                DoClearAmbition(CompletionReason.ClearedByScript);
            }
        }

        string ICharacterSaveData.SerializeToJson() => CharacterSaveDataHelper.SerializeToJson(this);
        void ICharacterSaveData.DeserializeFromJson(string json) => CharacterSaveDataHelper.DeserializeFromJson(this, json);

        private static List<ContextEntryDTO> SerializeContext(AmbitionContext ctx)
        {
            var list = new List<ContextEntryDTO>();
            if (ctx == null) return list;
            foreach (var kvp in ctx.AsReadOnly())
            {
                var entry = new ContextEntryDTO { Key = kvp.Key };
                var v = kvp.Value;
                if (v == null) { entry.Kind = ContextValueKind.Primitive; entry.SerializedValue = null; }
                else if (v is Character c)
                {
                    entry.Kind = ContextValueKind.Character;
                    entry.SerializedValue = c.CharacterId;
                }
                else if (v is MWI.WorldSystem.IWorldZone z)
                {
                    entry.Kind = ContextValueKind.Zone;
                    entry.SerializedValue = z.ZoneId;
                }
                else if (v is AmbitionSO amb) { entry.Kind = ContextValueKind.AmbitionSO; entry.SerializedValue = AmbitionRegistry.GetGuid(amb); }
                else if (v is QuestSO qs)     { entry.Kind = ContextValueKind.QuestSO; entry.SerializedValue = QuestRegistry.GetGuid(qs); }
                else if (v is ItemSO it)      { entry.Kind = ContextValueKind.ItemSO; entry.SerializedValue = it.name; }
                // NeedSO not in codebase — Phase 3 deviation #3; needs are tracked by type-name. ContextValueKind.NeedSO is reserved for future use.
                else if (v.GetType().IsEnum)  { entry.Kind = ContextValueKind.Enum; entry.SerializedValue = $"{v.GetType().FullName}|{v}"; }
                else                          { entry.Kind = ContextValueKind.Primitive; entry.SerializedValue = v.ToString(); }
                list.Add(entry);
            }
            return list;
        }

        private CompletedAmbitionDTO SerializeCompleted(CompletedAmbition src)
        {
            return new CompletedAmbitionDTO
            {
                AmbitionSOGuid = AmbitionRegistry.GetGuid(src.SO),
                FinalContext = SerializeContext(src.FinalContext),
                CompletedDay = src.CompletedDay,
                Reason = src.Reason
            };
        }

        private static List<TaskStateDTO> SerializeActiveTasks(IAmbitionStepQuest stepQuest)
        {
            var list = new List<TaskStateDTO>();
            if (stepQuest is not AmbitionQuest aq) return list;
            for (int i = 0; i < aq.Tasks.Count; i++)
            {
                var t = aq.Tasks[i];
                if (t == null) continue;
                var s = t.SerializeState();
                if (string.IsNullOrEmpty(s)) continue;
                list.Add(new TaskStateDTO { TaskIndexInQuest = i, SerializedState = s });
            }
            return list;
        }

        // Test seams
        // Allows EditMode tests to pump state without instantiating a real Character.
        internal void TEST_ForceState(AmbitionInstance instance) { _current = instance; }
        internal void TEST_ForceHistory(IEnumerable<CompletedAmbition> list)
        { _history.Clear(); _history.AddRange(list); }
    }
}
