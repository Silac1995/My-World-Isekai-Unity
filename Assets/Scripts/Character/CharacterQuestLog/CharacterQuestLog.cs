using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using MWI.Quests;

/// <summary>
/// Per-character quest log. Holds claimed quest references + denormalized snapshots
/// so the HUD can render even when the source is unloaded. Server-authoritative;
/// clients route mutations via ServerRpc; snapshots push via targeted ClientRpc.
/// </summary>
public class CharacterQuestLog : CharacterSystem, ICharacterSaveData<QuestLogSaveData>
{
    // Server-authoritative claimed-id list. Clients sync via NetworkList.OnListChanged.
    private readonly NetworkList<FixedString64Bytes> _claimedQuestIds = new NetworkList<FixedString64Bytes>();

    // Server-authoritative focused-quest preference. Persists across disconnect/reconnect.
    private readonly NetworkVariable<FixedString64Bytes> _focusedQuestId = new NetworkVariable<FixedString64Bytes>(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // Server-side: live IQuest references keyed by QuestId.
    private readonly Dictionary<string, IQuest> _liveQuests = new Dictionary<string, IQuest>();

    // Server + client: denormalized snapshots (server builds on claim, pushed to owning client).
    private readonly Dictionary<string, QuestSnapshotEntry> _snapshots = new Dictionary<string, QuestSnapshotEntry>();

    // Dormant snapshots — loaded from save but not on the current map. Wake on CharacterMapTracker change.
    private readonly Dictionary<string, QuestSnapshotEntry> _dormantSnapshots = new Dictionary<string, QuestSnapshotEntry>();

    public IReadOnlyList<IQuest> ActiveQuests
    {
        get
        {
            // Server: live IQuest references in _liveQuests.
            // Client: snapshot proxies built from _snapshots (live refs aren't replicated
            // across the network — the ClientRpc only ships the denormalized snapshot).
            if (_liveQuests.Count > 0)
            {
                var list = new List<IQuest>(_liveQuests.Count);
                foreach (var q in _liveQuests.Values) list.Add(q);
                return list;
            }
            if (_snapshots.Count > 0)
            {
                var list = new List<IQuest>(_snapshots.Count);
                foreach (var snap in _snapshots.Values) list.Add(new SnapshotQuestProxy(snap));
                return list;
            }
            return Array.Empty<IQuest>();
        }
    }

    public IQuest FocusedQuest
    {
        get
        {
            var id = _focusedQuestId.Value.ToString();
            if (string.IsNullOrEmpty(id)) return null;
            // Prefer the live ref (server). On client the live dict is empty — fall back to snapshot.
            if (_liveQuests.TryGetValue(id, out var live)) return live;
            if (_snapshots.TryGetValue(id, out var snap)) return new SnapshotQuestProxy(snap);
            return null;
        }
    }

    public IReadOnlyDictionary<string, QuestSnapshotEntry> Snapshots => _snapshots;
    public IReadOnlyDictionary<string, QuestSnapshotEntry> DormantSnapshots => _dormantSnapshots;

    public event Action<IQuest> OnQuestAdded;
    public event Action<IQuest> OnQuestRemoved;
    public event Action<IQuest> OnQuestProgressChanged;
    public event Action<IQuest> OnFocusedChanged;

    private CharacterMapTracker _mapTrackerCache;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _claimedQuestIds.OnListChanged += HandleClaimedListChanged;
        _focusedQuestId.OnValueChanged += HandleFocusedChanged;

        if (_character != null && _character.TryGetComponent(out CharacterMapTracker tracker))
        {
            _mapTrackerCache = tracker;
            _mapTrackerCache.CurrentMapID.OnValueChanged += HandleMapChanged;
        }
    }

    public override void OnNetworkDespawn()
    {
        _claimedQuestIds.OnListChanged -= HandleClaimedListChanged;
        _focusedQuestId.OnValueChanged -= HandleFocusedChanged;
        if (_mapTrackerCache != null)
        {
            _mapTrackerCache.CurrentMapID.OnValueChanged -= HandleMapChanged;
            _mapTrackerCache = null;
        }
        base.OnNetworkDespawn();
    }

    // =========================================================================
    // Mutations (server-authoritative)
    // =========================================================================

    public bool TryClaim(IQuest quest)
    {
        if (quest == null) return false;
        if (!IsServer) { TryClaimServerRpc(quest.QuestId); return false; }
        return ServerTryClaim(quest);
    }

    private bool ServerTryClaim(IQuest quest)
    {
        if (quest == null || _liveQuests.ContainsKey(quest.QuestId)) return false;
        if (!quest.TryJoin(_character)) return false;

        _liveQuests[quest.QuestId] = quest;
        _claimedQuestIds.Add(new FixedString64Bytes(quest.QuestId));

        var snap = BuildSnapshot(quest);
        _snapshots[quest.QuestId] = snap;
        PushQuestSnapshotClientRpc(snap, RpcTargetForOwner());

        quest.OnProgressRecorded += HandleQuestProgress;
        quest.OnStateChanged += HandleQuestStateChanged;

        OnQuestAdded?.Invoke(quest);

        // Auto-focus most recent if nothing focused.
        if (string.IsNullOrEmpty(_focusedQuestId.Value.ToString()))
            _focusedQuestId.Value = new FixedString64Bytes(quest.QuestId);

        return true;
    }

    public bool TryAbandon(IQuest quest)
    {
        if (quest == null) return false;
        if (!IsServer) { TryAbandonServerRpc(quest.QuestId); return false; }
        return ServerTryAbandon(quest.QuestId);
    }

    private bool ServerTryAbandon(string questId)
    {
        if (!_liveQuests.TryGetValue(questId, out var quest)) return false;

        quest.TryLeave(_character);
        quest.OnProgressRecorded -= HandleQuestProgress;
        quest.OnStateChanged -= HandleQuestStateChanged;

        _liveQuests.Remove(questId);
        _snapshots.Remove(questId);
        for (int i = 0; i < _claimedQuestIds.Count; i++)
        {
            if (_claimedQuestIds[i].ToString() == questId) { _claimedQuestIds.RemoveAt(i); break; }
        }

        OnQuestRemoved?.Invoke(quest);

        // Re-focus to another active quest if abandoned was focused.
        if (_focusedQuestId.Value.ToString() == questId)
        {
            _focusedQuestId.Value = _liveQuests.Count > 0
                ? new FixedString64Bytes(GetFirstQuestId())
                : default;
        }
        return true;
    }

    private string GetFirstQuestId()
    {
        foreach (var kv in _liveQuests) return kv.Key;
        return string.Empty;
    }

    public void SetFocused(IQuest quest)
    {
        if (!IsServer) { SetFocusedServerRpc(quest != null ? quest.QuestId : string.Empty); return; }
        _focusedQuestId.Value = new FixedString64Bytes(quest != null ? quest.QuestId : string.Empty);
    }

    [ServerRpc(RequireOwnership = false)]
    private void TryClaimServerRpc(string questId)
    {
        var quest = ResolveQuest(questId);
        if (quest != null) ServerTryClaim(quest);
    }

    [ServerRpc(RequireOwnership = false)]
    private void TryAbandonServerRpc(string questId) => ServerTryAbandon(questId);

    [ServerRpc(RequireOwnership = false)]
    private void SetFocusedServerRpc(string questId) => _focusedQuestId.Value = new FixedString64Bytes(questId);

    // =========================================================================
    // Server-side IQuest resolver (v1: linear scan; future QuestRegistry for O(1))
    // =========================================================================

    private IQuest ResolveQuest(string questId)
    {
        if (string.IsNullOrEmpty(questId)) return null;
        if (BuildingManager.Instance == null) return null;
        foreach (var b in BuildingManager.Instance.allBuildings)
        {
            if (b is CommercialBuilding cb)
            {
                var q = cb.GetQuestById(questId);
                if (q != null) return q;
            }
        }
        return null;
    }

    // =========================================================================
    // Event handlers
    // =========================================================================

    private void HandleQuestProgress(IQuest quest, Character contributor, int amount)
    {
        if (!_snapshots.TryGetValue(quest.QuestId, out var snap)) return;
        snap.totalProgress = quest.TotalProgress;
        int my = quest.Contribution.TryGetValue(_character.CharacterId, out var c) ? c : 0;
        snap.myContribution = my;
        QuestProgressUpdatedClientRpc(quest.QuestId, snap.totalProgress, snap.myContribution, RpcTargetForOwner());
        OnQuestProgressChanged?.Invoke(quest);
    }

    private void HandleQuestStateChanged(IQuest quest)
    {
        if (quest.State == QuestState.Completed || quest.State == QuestState.Expired)
        {
            ServerTryAbandon(quest.QuestId);
        }
    }

    private void HandleClaimedListChanged(NetworkListEvent<FixedString64Bytes> evt)
    {
        // Server already updated local state in ServerTryClaim/Abandon. Clients:
        if (IsServer) return;
        // Both Remove (by-value) and RemoveAt (by-index) fire when the server abandons or
        // punches out — ServerTryAbandon uses RemoveAt. Without the second branch the
        // client snapshot stays forever (HUD tracker, log window, world marker all stuck).
        // See CharacterEquipment.HandleEquipmentSyncListChanged for the same pattern.
        if (evt.Type == NetworkListEvent<FixedString64Bytes>.EventType.Remove ||
            evt.Type == NetworkListEvent<FixedString64Bytes>.EventType.RemoveAt)
        {
            var id = evt.Value.ToString();
            _snapshots.Remove(id);
            OnQuestRemoved?.Invoke(null);  // HUD reads _snapshots directly to resolve
        }
        else if (evt.Type == NetworkListEvent<FixedString64Bytes>.EventType.Clear)
        {
            _snapshots.Clear();
            OnQuestRemoved?.Invoke(null);
        }
    }

    private void HandleFocusedChanged(FixedString64Bytes prev, FixedString64Bytes next)
    {
        OnFocusedChanged?.Invoke(FocusedQuest);
    }

    private void HandleMapChanged(FixedString128Bytes prev, FixedString128Bytes next)
    {
        string newMapId = next.ToString();
        // Promote dormant snapshots whose originMapId now matches.
        var promoted = new List<string>();
        foreach (var kv in _dormantSnapshots)
        {
            if (kv.Value.originMapId == newMapId)
            {
                var quest = ResolveQuest(kv.Key);
                if (quest != null && quest.State != QuestState.Completed && quest.State != QuestState.Expired)
                {
                    _liveQuests[kv.Key] = quest;
                    _snapshots[kv.Key] = kv.Value;
                    quest.TryJoin(_character);
                    quest.OnProgressRecorded += HandleQuestProgress;
                    quest.OnStateChanged += HandleQuestStateChanged;
                    OnQuestAdded?.Invoke(quest);
                    promoted.Add(kv.Key);
                }
            }
        }
        foreach (var id in promoted) _dormantSnapshots.Remove(id);
    }

    // =========================================================================
    // ClientRpc — per-quest snapshot push (targeted at owning client)
    // =========================================================================

    [ClientRpc]
    private void PushQuestSnapshotClientRpc(QuestSnapshotEntry snap, ClientRpcParams target = default)
    {
        if (snap == null) return;
        _snapshots[snap.questId] = snap;
        OnQuestAdded?.Invoke(null);
    }

    [ClientRpc]
    private void QuestProgressUpdatedClientRpc(string questId, int newTotal, int newMyContribution, ClientRpcParams target = default)
    {
        if (_snapshots.TryGetValue(questId, out var snap))
        {
            snap.totalProgress = newTotal;
            snap.myContribution = newMyContribution;
            OnQuestProgressChanged?.Invoke(null);
        }
    }

    private ClientRpcParams RpcTargetForOwner()
    {
        return new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } } };
    }

    // =========================================================================
    // Snapshot builder
    // =========================================================================

    private QuestSnapshotEntry BuildSnapshot(IQuest q)
    {
        var snap = new QuestSnapshotEntry
        {
            questId = q.QuestId,
            originWorldId = q.OriginWorldId,
            originMapId = q.OriginMapId,
            issuerCharacterId = q.Issuer != null ? q.Issuer.CharacterId : string.Empty,
            questType = (int)q.Type,
            title = q.Title,
            instructionLine = q.InstructionLine,
            description = q.Description,
            totalProgress = q.TotalProgress,
            required = q.Required,
            maxContributors = q.MaxContributors,
            myContribution = q.Contribution.TryGetValue(_character.CharacterId, out var c) ? c : 0,
            state = (int)q.State,
            targetDisplayName = q.Target != null ? q.Target.GetDisplayName() : string.Empty,
        };
        if (q.Target != null)
        {
            snap.targetPosition = q.Target.GetWorldPosition();
            var bounds = q.Target.GetZoneBounds();
            if (bounds.HasValue)
            {
                snap.hasZoneBounds = true;
                snap.zoneCenter = bounds.Value.center;
                snap.zoneSize = bounds.Value.size;
            }
        }
        return snap;
    }

    // =========================================================================
    // ICharacterSaveData
    // =========================================================================

    public string SaveKey => "CharacterQuestLog";
    public int LoadPriority => 70;

    public QuestLogSaveData Serialize()
    {
        var data = new QuestLogSaveData
        {
            focusedQuestId = _focusedQuestId.Value.ToString()
        };
        foreach (var snap in _snapshots.Values) data.activeQuests.Add(snap);
        foreach (var snap in _dormantSnapshots.Values) data.activeQuests.Add(snap);
        return data;
    }

    public void Deserialize(QuestLogSaveData data)
    {
        _liveQuests.Clear();
        _snapshots.Clear();
        _dormantSnapshots.Clear();
        if (data == null || data.activeQuests == null) return;

        string currentMapId = ResolveCurrentMapId();
        foreach (var entry in data.activeQuests)
        {
            if (!string.IsNullOrEmpty(currentMapId) && entry.originMapId == currentMapId)
            {
                var quest = ResolveQuest(entry.questId);
                if (quest != null && quest.State != QuestState.Completed && quest.State != QuestState.Expired)
                {
                    _liveQuests[entry.questId] = quest;
                    _snapshots[entry.questId] = entry;
                    quest.TryJoin(_character);
                    quest.OnProgressRecorded += HandleQuestProgress;
                    quest.OnStateChanged += HandleQuestStateChanged;
                }
                else
                {
                    Debug.LogWarning($"[CharacterQuestLog] Quest {entry.questId} no longer resolvable on current map; dropping.");
                }
            }
            else
            {
                _dormantSnapshots[entry.questId] = entry;
            }
        }

        if (!string.IsNullOrEmpty(data.focusedQuestId) && IsServer)
        {
            _focusedQuestId.Value = new FixedString64Bytes(data.focusedQuestId);
        }
    }

    private string ResolveCurrentMapId()
    {
        if (_character == null) return string.Empty;
        if (_character.TryGetComponent(out CharacterMapTracker tracker))
        {
            return tracker.CurrentMapID.Value.ToString();
        }
        return string.Empty;
    }

    string ICharacterSaveData.SerializeToJson() => CharacterSaveDataHelper.SerializeToJson(this);
    void ICharacterSaveData.DeserializeFromJson(string json) => CharacterSaveDataHelper.DeserializeFromJson(this, json);
}
