using System;
using System.Collections.Generic;
using UnityEngine;

namespace MWI.Quests
{
    public enum QuestType
    {
        Job = 0,
        Main = 1,
        Bounty = 2,
        Event = 3,
        Custom = 99
    }

    public enum QuestState
    {
        Open = 0,
        Full = 1,
        Completed = 2,
        Abandoned = 3,
        Expired = 4
    }

    /// <summary>
    /// Unified work-instruction primitive consumed by both players and NPCs.
    /// Existing types (BuildingTask, BuyOrder, TransportOrder, CraftingOrder)
    /// implement this directly. NPC GOAP keeps its existing typed APIs unchanged.
    /// </summary>
    public interface IQuest
    {
        // Identity & origin
        string QuestId { get; }
        string OriginWorldId { get; }
        string OriginMapId { get; }
        Character Issuer { get; }
        QuestType Type { get; }

        // Display data
        string Title { get; }
        string InstructionLine { get; }
        string Description { get; }

        // Lifecycle
        QuestState State { get; }
        bool IsExpired { get; }
        int RemainingDays { get; }

        // Progress
        int TotalProgress { get; }
        int Required { get; }
        int MaxContributors { get; }

        // Contributors
        IReadOnlyList<Character> Contributors { get; }
        IReadOnlyDictionary<string, int> Contribution { get; }

        // Mutations (server-authoritative — caller must be on server)
        bool TryJoin(Character character);
        bool TryLeave(Character character);
        void RecordProgress(Character character, int amount);

        // Targeting
        IQuestTarget Target { get; }

        // Events
        event Action<IQuest> OnStateChanged;
        event Action<IQuest, Character, int> OnProgressRecorded;
    }
}
