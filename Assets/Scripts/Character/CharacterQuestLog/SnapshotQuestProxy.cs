using System;
using System.Collections.Generic;
using UnityEngine;
using MWI.Quests;

/// <summary>
/// Read-only IQuest implementation backed by a <see cref="QuestSnapshotEntry"/>.
/// Used on clients to expose quest data through the same IQuest API the HUD reads on the server.
///
/// Why this exists: the server holds live IQuest references in
/// <see cref="CharacterQuestLog._liveQuests"/>. Those references are NOT replicated across
/// the network — only the denormalized <see cref="QuestSnapshotEntry"/> ships via
/// targeted ClientRpc. Without this proxy, <c>CharacterQuestLog.FocusedQuest</c> /
/// <c>ActiveQuests</c> would return null/empty on every non-server peer, leaving the
/// HUD blank for client players even though they correctly received the snapshot.
///
/// Mutation methods (TryJoin/TryLeave/RecordProgress) are no-ops here — the actual
/// mutation must be routed through <see cref="CharacterQuestLog.TryClaim"/> /
/// <see cref="CharacterQuestLog.TryAbandon"/> which do the ServerRpc round-trip.
/// </summary>
internal class SnapshotQuestProxy : IQuest
{
    private readonly QuestSnapshotEntry _snap;
    private readonly SnapshotQuestTarget _target;
    private static readonly IReadOnlyDictionary<string, int> _emptyContribution = new Dictionary<string, int>();
    private static readonly IReadOnlyList<Character> _emptyContributors = Array.Empty<Character>();

    public SnapshotQuestProxy(QuestSnapshotEntry snap)
    {
        _snap = snap;
        _target = new SnapshotQuestTarget(snap);
    }

    public string QuestId => _snap.questId ?? string.Empty;
    public string OriginWorldId => _snap.originWorldId ?? string.Empty;
    public string OriginMapId => _snap.originMapId ?? string.Empty;
    public Character Issuer => null;  // snapshot only carries issuerCharacterId; HUD doesn't need the live ref
    public QuestType Type => (QuestType)_snap.questType;

    public string Title => _snap.title ?? string.Empty;
    public string InstructionLine => _snap.instructionLine ?? string.Empty;
    public string Description => _snap.description ?? string.Empty;

    public QuestState State => (QuestState)_snap.state;
    public bool IsExpired => State == QuestState.Expired;
    public int RemainingDays => int.MaxValue;  // not snapshot'd; HUD doesn't read this

    public int TotalProgress => _snap.totalProgress;
    public int Required => _snap.required;
    public int MaxContributors => _snap.maxContributors;

    public IReadOnlyList<Character> Contributors => _emptyContributors;
    public IReadOnlyDictionary<string, int> Contribution => _emptyContribution;

    public IQuestTarget Target => _target;

    public bool TryJoin(Character character) => false;
    public bool TryLeave(Character character) => false;
    public void RecordProgress(Character character, int amount) { }

    // Events never fire on a proxy — events are a server-side concern. add/remove are
    // empty so subscribers don't error or leak.
    public event Action<IQuest> OnStateChanged
    {
        add { }
        remove { }
    }
    public event Action<IQuest, Character, int> OnProgressRecorded
    {
        add { }
        remove { }
    }
}

/// <summary>
/// IQuestTarget implementation that returns position + zone bounds straight from a snapshot.
/// </summary>
internal class SnapshotQuestTarget : IQuestTarget
{
    private readonly QuestSnapshotEntry _snap;

    public SnapshotQuestTarget(QuestSnapshotEntry snap) { _snap = snap; }

    public Vector3 GetWorldPosition() => _snap.targetPosition;
    public Vector3? GetMovementTarget() => null;
    public Bounds? GetZoneBounds() => _snap.hasZoneBounds ? new Bounds(_snap.zoneCenter, _snap.zoneSize) : (Bounds?)null;
    public string GetDisplayName() => _snap.targetDisplayName ?? string.Empty;
    public bool IsVisibleToPlayer(Character viewer) => true;
}
