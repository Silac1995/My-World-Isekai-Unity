using System;
using System.Collections.Generic;
using UnityEngine;
using MWI.Quests;

namespace MWI.Ambition
{
    /// <summary>
    /// Runtime IQuest that bridges <see cref="IAmbitionStepQuest"/> (BT-tickable, ambition-driven)
    /// and <see cref="MWI.Quests.IQuest"/> (lives in CharacterQuestLog, save / sync / HUD).
    ///
    /// One AmbitionQuest is built from a <see cref="QuestSO"/> definition plus an
    /// <see cref="AmbitionContext"/>. It owns deep copies of the SO's tasks (via JSON round-trip)
    /// so two NPCs running the same quest don't share mid-pursuit state.
    ///
    /// Lifecycle states use the canonical <see cref="MWI.Quests.QuestState"/> values only —
    /// there is no Running / NotStarted / Cancelled. Initial state is <c>Open</c>; cancellation
    /// settles to <c>Abandoned</c>; success settles to <c>Completed</c>. Ambitions are patient
    /// (untimed): <see cref="IsExpired"/> is always false and <see cref="RemainingDays"/> returns -1.
    /// </summary>
    public class AmbitionQuest : IAmbitionStepQuest
    {
        // --- Authoring & runtime state ---
        private readonly QuestSO _so;
        private readonly Character _self; // self-issued: same character is issuer & sole contributor
        private readonly List<TaskBase> _tasks; // deep-copied from _so.Tasks so per-quest state is isolated
        private AmbitionContext _ctx;

        // --- Contributors / contribution buffers (allocated once; never re-allocated per CLAUDE.md rule #34) ---
        private readonly List<Character> _contributors = new List<Character>(1);
        private readonly Dictionary<string, int> _contribution = new Dictionary<string, int>(0);

        // --- Mutable lifecycle state ---
        private QuestState _state = QuestState.Open;
        private int _completedCount; // count of _tasks whose last reported status was Completed

        // === Construction ===

        public AmbitionQuest(QuestSO so, Character self, AmbitionContext ctx)
        {
            _so = so;
            _self = self;
            _ctx = ctx;
            _tasks = CloneTasksFromSO(so);

            QuestId = System.Guid.NewGuid().ToString("N");

            // Self-issued: the only contributor is the owning character.
            if (_self != null) _contributors.Add(_self);
        }

        // === IQuest — Identity & origin ===

        public string QuestId { get; private set; }
        public string OriginWorldId { get; set; } = string.Empty;
        public string OriginMapId { get; set; } = string.Empty;
        public Character Issuer => _self;
        public QuestType Type => QuestType.Custom;

        // === IQuest — Display data ===

        public string Title => _so != null ? _so.DisplayName : "(unknown)";
        // v1 falls back to Description; HUD layer may refine later (Phase 11 / Task 43).
        public string InstructionLine =>
            _so != null
                ? (!string.IsNullOrEmpty(_so.Description) ? _so.Description : _so.DisplayName)
                : string.Empty;
        public string Description => _so != null ? _so.Description : string.Empty;

        // === IQuest — Lifecycle ===

        public QuestState State => _state;
        public bool IsExpired => false;          // Ambitions are patient / untimed.
        public int RemainingDays => -1;          // Sentinel "no deadline"; do not return 0 (would imply expiry).

        // === IQuest — Progress ===

        public int TotalProgress => _completedCount;
        public int Required => _tasks != null ? _tasks.Count : 0;
        public int MaxContributors => 1;

        // === IQuest — Contributors ===

        public IReadOnlyList<Character> Contributors => _contributors;
        public IReadOnlyDictionary<string, int> Contribution => _contribution;

        public bool TryJoin(Character character)
        {
            // Single-character, self-issued — cannot be joined externally.
            return false;
        }

        public bool TryLeave(Character character)
        {
            // Mirror TryJoin: contributor list is fixed to _self for the quest's lifetime.
            return false;
        }

        public void RecordProgress(Character character, int amount)
        {
            // Ambition progress is task-driven, not contribution-driven.
        }

        // === IQuest — Targeting ===

        // v1 returns null — current-task target wiring is a Phase 11 concern (Task 43).
        public IQuestTarget Target => null;

        // === IQuest — Events ===

        public event Action<IQuest> OnStateChanged;
        public event Action<IQuest, Character, int> OnProgressRecorded; // declared to satisfy interface; not fired in v1.

        // === IAmbitionStepQuest extension ===

        public void BindContext(AmbitionContext ctx)
        {
            _ctx = ctx;
            if (_tasks == null) return;
            for (int i = 0; i < _tasks.Count; i++)
            {
                var t = _tasks[i];
                if (t == null) continue;
                t.Bind(_ctx);
                t.RegisterCompletionListeners(_self, _ctx);
            }
            // Note: no QuestState.Running exists — quest stays Open while ticking.
        }

        public TaskStatus TickActiveTasks(Character npc)
        {
            // If the quest has already settled, nothing more to tick.
            if (_state == QuestState.Completed || _state == QuestState.Abandoned)
                return _state == QuestState.Completed ? TaskStatus.Completed : TaskStatus.Failed;

            if (_tasks == null || _tasks.Count == 0)
            {
                CompleteAndNotify();
                return TaskStatus.Completed;
            }

            TaskStatus result;
            switch (_so != null ? _so.Ordering : TaskOrderingMode.Sequential)
            {
                case TaskOrderingMode.Parallel:
                    result = TickParallel(npc);
                    break;
                case TaskOrderingMode.AnyOf:
                    result = TickAnyOf(npc);
                    break;
                case TaskOrderingMode.Sequential:
                default:
                    result = TickSequential(npc);
                    break;
            }

            if (result == TaskStatus.Completed) CompleteAndNotify();
            return result;
        }

        public void Cancel()
        {
            if (_tasks != null)
            {
                for (int i = 0; i < _tasks.Count; i++)
                {
                    var t = _tasks[i];
                    if (t == null) continue;
                    t.UnregisterCompletionListeners(_self);
                    t.Cancel();
                }
            }
            SetState(QuestState.Abandoned);
        }

        public void OnControllerSwitching(Character npc, ControllerKind goingTo)
        {
            if (_tasks == null) return;
            for (int i = 0; i < _tasks.Count; i++)
            {
                var t = _tasks[i];
                if (t == null) continue;
                t.OnControllerSwitching(npc, goingTo);
            }
        }

        // === Read-only access for save / debug / inspector ===

        public IReadOnlyList<TaskBase> Tasks => _tasks;
        public QuestSO SourceSO => _so;
        public bool IsAmbitionStep => true; // marker — IQuest itself has no such flag

        // === Internal helpers ===

        /// <summary>
        /// Sequential: tick the first not-yet-completed task. The whole quest only
        /// completes when the last task in list-order returns Completed.
        /// </summary>
        private TaskStatus TickSequential(Character npc)
        {
            // Find the first task that is still Running. _completedCount is monotonic,
            // so it indexes the next active task directly.
            int idx = _completedCount;
            if (idx >= _tasks.Count) return TaskStatus.Completed;

            var t = _tasks[idx];
            if (t == null)
            {
                // Treat null entries as already-done so Sequential keeps progressing.
                _completedCount = idx + 1;
                return _completedCount >= _tasks.Count ? TaskStatus.Completed : TaskStatus.Running;
            }

            var s = t.Tick(npc, _ctx);
            if (s == TaskStatus.Completed)
            {
                t.UnregisterCompletionListeners(_self);
                _completedCount = idx + 1;
                return _completedCount >= _tasks.Count ? TaskStatus.Completed : TaskStatus.Running;
            }
            if (s == TaskStatus.Failed) return TaskStatus.Failed;
            return TaskStatus.Running;
        }

        /// <summary>
        /// Parallel: tick every task each pass. Quest completes when all tasks are Completed.
        /// </summary>
        private TaskStatus TickParallel(Character npc)
        {
            int doneThisPass = 0;
            for (int i = 0; i < _tasks.Count; i++)
            {
                var t = _tasks[i];
                if (t == null) { doneThisPass++; continue; }

                // Already-completed tasks are skipped on subsequent ticks. We mark them done
                // by unregistering their listeners on first completion; the simplest robust
                // signal is to count via _completedCount, but Parallel needs per-slot tracking.
                // Strategy: re-tick all; tasks that have already returned Completed once are
                // expected to remain Completed (idempotent re-tick), so we just re-aggregate.
                var s = t.Tick(npc, _ctx);
                if (s == TaskStatus.Failed) return TaskStatus.Failed;
                if (s == TaskStatus.Completed) doneThisPass++;
            }

            // Refresh _completedCount only when it increases (don't decrement on flaky re-evaluation).
            if (doneThisPass > _completedCount) _completedCount = doneThisPass;

            return _completedCount >= _tasks.Count ? TaskStatus.Completed : TaskStatus.Running;
        }

        /// <summary>
        /// AnyOf: tick every task each pass. Quest completes the first time any task returns
        /// Completed; remaining tasks are Cancel-led so they release their reservations.
        /// </summary>
        private TaskStatus TickAnyOf(Character npc)
        {
            int winner = -1;
            for (int i = 0; i < _tasks.Count; i++)
            {
                var t = _tasks[i];
                if (t == null) continue;
                var s = t.Tick(npc, _ctx);
                if (s == TaskStatus.Completed) { winner = i; break; }
                // Note: a single Failed sibling does not fail the whole AnyOf — others may still win.
            }

            if (winner < 0) return TaskStatus.Running;

            // Cancel the losers.
            for (int i = 0; i < _tasks.Count; i++)
            {
                if (i == winner) continue;
                var t = _tasks[i];
                if (t == null) continue;
                t.UnregisterCompletionListeners(_self);
                t.Cancel();
            }
            // The winning task's listeners are dropped by CompleteAndNotify().
            _completedCount = _tasks.Count; // AnyOf reports full progress on win for HUD parity.
            return TaskStatus.Completed;
        }

        private void CompleteAndNotify()
        {
            if (_tasks != null)
            {
                for (int i = 0; i < _tasks.Count; i++)
                {
                    var t = _tasks[i];
                    if (t == null) continue;
                    t.UnregisterCompletionListeners(_self);
                }
            }
            SetState(QuestState.Completed);
        }

        private void SetState(QuestState newState)
        {
            if (_state == newState) return;
            _state = newState;
            OnStateChanged?.Invoke(this);
        }

        // === Task cloning ===

        /// <summary>
        /// Deep-copies the SO's task list so each AmbitionQuest instance owns mutable, isolated
        /// task state. Uses a JSON round-trip via a [SerializeReference]-decorated wrapper to
        /// avoid per-subclass clone overrides while preserving the polymorphic shape.
        /// </summary>
        private static List<TaskBase> CloneTasksFromSO(QuestSO so)
        {
            var dst = new List<TaskBase>();
            if (so == null || so.Tasks == null) return dst;

            // Source -> wrapper -> JSON -> wrapper(copy). JsonUtility honours [SerializeReference].
            var srcWrapper = new TaskListWrapper();
            srcWrapper.Tasks = new List<TaskBase>(so.Tasks.Count);
            for (int i = 0; i < so.Tasks.Count; i++) srcWrapper.Tasks.Add(so.Tasks[i]);

            string json = JsonUtility.ToJson(srcWrapper);
            var dstWrapper = JsonUtility.FromJson<TaskListWrapper>(json);
            if (dstWrapper != null && dstWrapper.Tasks != null)
            {
                for (int i = 0; i < dstWrapper.Tasks.Count; i++) dst.Add(dstWrapper.Tasks[i]);
            }
            return dst;
        }

        [Serializable]
        private class TaskListWrapper
        {
            [SerializeReference] public List<TaskBase> Tasks;
        }
    }
}
