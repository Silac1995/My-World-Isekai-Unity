using System;
using System.Collections.Generic;
using MWI.Quests;

namespace MWI.Orders
{
    /// <summary>
    /// Order with a trackable objective. Implements IQuest so it appears in the
    /// receiver's CharacterQuestLog and reuses the existing snapshot sync, HUD
    /// markers, and quest infrastructure.
    ///
    /// Single-contributor (the receiver only). State derived from Order.State.
    /// QuestId stamped automatically from OrderId on acceptance.
    /// </summary>
    public abstract class OrderQuest : Order, IQuest
    {
        // ── IQuest identity ────────────────────────────────────────────────
        public string QuestId { get; private set; } = string.Empty;
        public string OriginWorldId { get; protected set; } = string.Empty;
        public string OriginMapId { get; protected set; } = string.Empty;
        public virtual MWI.Quests.QuestType Type => MWI.Quests.QuestType.Custom;

        Character IQuest.Issuer => Issuer?.AsCharacter;

        // ── IQuest display ─────────────────────────────────────────────────
        // Subclass supplies DisplayTitle / Description. IQuest.Title wraps DisplayTitle
        // with a [P:NN] priority decoration so high-priority orders stand out in the
        // quest log without needing a custom UI_QuestLogEntry prefab.
        public abstract string DisplayTitle { get; }
        public string Title => $"[P:{Priority}] {DisplayTitle}";
        public virtual string InstructionLine => Title;
        public abstract string Description { get; }

        // ── IQuest lifecycle (explicit impl to avoid clashing with Order.State : OrderState) ──
        MWI.Quests.QuestState IQuest.State
        {
            get
            {
                switch (State)
                {
                    case OrderState.Pending:   return MWI.Quests.QuestState.Open;
                    case OrderState.Accepted:  return MWI.Quests.QuestState.Open;
                    case OrderState.Active:    return MWI.Quests.QuestState.Open;
                    case OrderState.Complied:  return MWI.Quests.QuestState.Completed;
                    case OrderState.Disobeyed: return MWI.Quests.QuestState.Abandoned;
                    case OrderState.Cancelled: return MWI.Quests.QuestState.Abandoned;
                    default:                   return MWI.Quests.QuestState.Open;
                }
            }
        }

        public bool IsExpired => State == OrderState.Disobeyed && ElapsedSeconds >= TimeoutSeconds;

        // RemainingDays: convert remaining seconds to days. v1 orders are short — return a large number when timeout is short.
        public int RemainingDays
        {
            get
            {
                float remainingSec = UnityEngine.Mathf.Max(0f, TimeoutSeconds - ElapsedSeconds);
                if (remainingSec >= 86400f) return UnityEngine.Mathf.FloorToInt(remainingSec / 86400f);
                return remainingSec > 0f ? 1 : 0;
            }
        }

        // ── IQuest progress (single binary objective by default) ───────────
        public virtual int TotalProgress => IsComplied() ? 1 : 0;
        public virtual int Required => 1;
        public int MaxContributors => 1;

        public IReadOnlyList<Character> Contributors
        {
            get
            {
                if (Receiver == null) return System.Array.Empty<Character>();
                _contributorsList[0] = Receiver;
                return _contributorsList;
            }
        }
        private readonly Character[] _contributorsList = new Character[1];

        public IReadOnlyDictionary<string, int> Contribution
        {
            get
            {
                _contributionDict.Clear();
                if (Receiver != null) _contributionDict[Receiver.CharacterId] = TotalProgress;
                return _contributionDict;
            }
        }
        private readonly Dictionary<string, int> _contributionDict = new Dictionary<string, int>();

        // ── IQuest mutations (single-receiver model) ───────────────────────
        public bool TryJoin(Character character)
        {
            // Only the designated receiver can "join" — the order is not multi-claimable.
            if (character == null) return false;
            if (character != Receiver) return false;
            return true;
        }

        public bool TryLeave(Character character)
        {
            // Player abandoning the quest = player refusing the order. Cancel via the issuer side.
            if (character == null || character != Receiver) return false;
            // The actual cancellation must go through CharacterOrders.CancelIssuedOrder so the
            // state machine fires the right consequences. For v1, just mark as Cancelled.
            State = OrderState.Cancelled;
            OnStateChanged?.Invoke(this);
            return true;
        }

        public void RecordProgress(Character character, int amount)
        {
            // No-op for binary orders. Subclasses with stackable progress can override.
            OnProgressRecorded?.Invoke(this, character, amount);
        }

        // ── IQuest target (subclass supplies the wrapper) ──────────────────
        public abstract IQuestTarget Target { get; }

        // ── IQuest events ──────────────────────────────────────────────────
        public event Action<IQuest> OnStateChanged;
        public event Action<IQuest, Character, int> OnProgressRecorded;

        // ── Order ↔ CharacterQuestLog bridge ────────────────────────────────
        public override void OnAccepted()
        {
            if (Receiver == null) return;

            // Stamp identity now that OrderId is assigned. QuestId must be unique within the receiver's quest log.
            QuestId = $"Order_{OrderId}";

            // Stamp origin from the receiver's current map (best-effort).
            if (Receiver.TryGetComponent(out CharacterMapTracker mapTracker))
            {
                OriginMapId = mapTracker.CurrentMapID.Value.ToString();
            }

            // Register with the receiver's quest log. Server-side path — ServerTryClaim handles snapshot push.
            if (Receiver.CharacterQuestLog != null)
            {
                Receiver.CharacterQuestLog.TryClaim(this);
            }
        }

        public override void OnResolved(OrderState finalState)
        {
            // Fire the IQuest state-changed event so the quest log subscribes/unsubscribes correctly.
            // CharacterQuestLog.HandleQuestStateChanged calls ServerTryAbandon when state == Completed/Expired.
            OnStateChanged?.Invoke(this);
        }

        /// <summary>
        /// Subclasses must implement IsCompleted (the IQuest semantic). IsComplied (Order semantic) defers to it.
        /// </summary>
        public abstract bool IsCompleted();

        public override bool IsComplied() => IsCompleted();
    }
}

