# Character Party System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement a small-scale character party/group system with leader-based follow behavior, gathering-based map transitions, persistent membership, and full multiplayer synchronization.

**Architecture:** Hybrid Component + Registry approach. `PartyData` (plain C# data), `PartyRegistry` (static lookup), `CharacterParty` (CharacterSystem MonoBehaviour on every character). Follow logic driven via a new BT node; gathering logic via a BoxCollider on a child GameObject; invitations via existing `InteractionInvitation` pipeline.

**Tech Stack:** Unity 6, Netcode for GameObjects (NGO), C#, NavMesh, Behaviour Tree

**Spec:** `docs/superpowers/specs/2026-03-27-character-party-system-design.md`

---

## File Map

### New Files

| File | Location | Responsibility |
|------|----------|----------------|
| `PartyFollowMode.cs` | `Assets/Scripts/Character/CharacterParty/` | Enum: Strict, Loose |
| `PartyState.cs` | `Assets/Scripts/Character/CharacterParty/` | Enum: Active, LeaderlessHold, Gathering |
| `PartyData.cs` | `Assets/Scripts/Character/CharacterParty/` | Plain C# data class — party identity, members, state |
| `PartyRegistry.cs` | `Assets/Scripts/Character/CharacterParty/` | Static dictionary lookup: PartyId ↔ PartyData, CharacterId ↔ PartyId |
| `CharacterParty.cs` | `Assets/Scripts/Character/CharacterParty/` | CharacterSystem MonoBehaviour — lifecycle, follow logic, gathering, network sync |
| `PartyGatherZone.cs` | `Assets/Scripts/Character/CharacterParty/` | MonoBehaviour on child GO — forwards OnTriggerEnter/Exit to CharacterParty |
| `PartyInvitation.cs` | `Assets/Scripts/Character/CharacterParty/` | InteractionInvitation subclass — invitation CanExecute/OnAccepted |
| `MapType.cs` | `Assets/Scripts/World/MapSystem/` | Enum: Region, Interior, Dungeon, Arena |
| `MapTransitionZone.cs` | `Assets/Scripts/World/MapSystem/` | MonoBehaviour — doorless region border trigger |
| `BTCond_IsInPartyFollow.cs` | `Assets/Scripts/AI/Conditions/` | BT condition — checks blackboard flag, wraps follow action |
| `BTAction_FollowPartyLeader.cs` | `Assets/Scripts/AI/Actions/` | BT action — pathfinds NPC to party leader |
| `UI_PartyPanel.cs` | `Assets/Scripts/UI/` | HUD panel — create party, member list, leader controls |
| `Leadership.asset` | `Assets/Data/Skills/` | SkillSO ScriptableObject for Leadership skill |

### Modified Files

| File | Change |
|------|--------|
| `Assets/Scripts/Character/Character.cs` | Replace old `_currentParty`/party stubs with `CharacterParty` subsystem reference |
| `Assets/Scripts/World/MapSystem/MapController.cs` | Add `MapType _mapType` field, replace `IsInteriorOffset` bool |
| `Assets/Scripts/World/MapSystem/MapTransitionDoor.cs` | Intercept party leader in `Interact()` → delegate to gathering |
| `Assets/Scripts/AI/NPCBehaviourTree.cs` | Insert `BTCond_IsInPartyFollow` at index 5 in `BuildTree()` |
| `Assets/Scripts/AI/Core/Blackboard.cs` | Add `KEY_PARTY_FOLLOW` constant |
| `Assets/Scripts/World/MapSystem/MapSaveData.cs` | Add `PartyId` field to `HibernatedNPCData` |
| `Assets/Scripts/Character/SaveLoad/ICharacterData` | Add `PartyId` field for persistence |
| `Assets/Scripts/World/SaveLoad/SaveManager.cs` | Load/save `PartyData` entries into `PartyRegistry` on boot |
| `Assets/Scripts/World/Simulation/MacroSimulator.cs` | Party-aware position snap during hibernation catch-up |

---

## Task 1: Data Layer — Enums

**Files:**
- Create: `Assets/Scripts/Character/CharacterParty/PartyFollowMode.cs`
- Create: `Assets/Scripts/Character/CharacterParty/PartyState.cs`

- [ ] **Step 1: Create `PartyFollowMode.cs`**

```csharp
public enum PartyFollowMode : byte
{
    Strict = 0,
    Loose = 1
}
```

- [ ] **Step 2: Create `PartyState.cs`**

```csharp
public enum PartyState : byte
{
    Active = 0,
    LeaderlessHold = 1,
    Gathering = 2
}
```

- [ ] **Step 3: Trigger recompilation, verify no errors**

Run: Check Unity console via `console-get-logs` MCP tool.
Expected: No compilation errors.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Character/CharacterParty/PartyFollowMode.cs Assets/Scripts/Character/CharacterParty/PartyState.cs
git commit -m "feat(party): add PartyFollowMode and PartyState enums"
```

---

## Task 2: Data Layer — PartyData

**Files:**
- Create: `Assets/Scripts/Character/CharacterParty/PartyData.cs`

**Read first:** The old `Assets/Scripts/Character/CharacterParty/CharacterParty.cs` (lines 1-69) — this is the stub being replaced. Do NOT delete it yet (Task 6 replaces it).

- [ ] **Step 1: Create `PartyData.cs`**

Plain C# class, no MonoBehaviour. Uses string UUIDs, not direct Character references.

```csharp
using System;
using System.Collections.Generic;

[Serializable]
public class PartyData
{
    public string PartyId;
    public string PartyName;
    public string LeaderId;
    public List<string> MemberIds = new List<string>();
    public PartyFollowMode FollowMode = PartyFollowMode.Strict;

    // Transient — not persisted, resets to Active on load
    [NonSerialized] public PartyState State = PartyState.Active;

    public PartyData(string leaderId, string leaderName, string partyName = null)
    {
        PartyId = Guid.NewGuid().ToString();
        LeaderId = leaderId;
        PartyName = string.IsNullOrEmpty(partyName) ? $"{leaderName}'s Party" : partyName;
        MemberIds.Add(leaderId);
    }

    public bool IsLeader(string characterId) => LeaderId == characterId;
    public bool IsMember(string characterId) => MemberIds.Contains(characterId);
    public bool IsFull(int maxSize) => MemberIds.Count >= maxSize;

    public void AddMember(string characterId)
    {
        if (!MemberIds.Contains(characterId))
            MemberIds.Add(characterId);
    }

    public void RemoveMember(string characterId)
    {
        MemberIds.Remove(characterId);

        if (characterId == LeaderId && MemberIds.Count > 0)
            LeaderId = MemberIds[0];
    }

    public int MemberCount => MemberIds.Count;
}
```

- [ ] **Step 2: Verify compilation**

Run: `console-get-logs` — no errors expected.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Character/CharacterParty/PartyData.cs
git commit -m "feat(party): add PartyData plain C# data class"
```

---

## Task 3: Data Layer — PartyRegistry

**Files:**
- Create: `Assets/Scripts/Character/CharacterParty/PartyRegistry.cs`

- [ ] **Step 1: Create `PartyRegistry.cs`**

Static class with two dictionaries and O(1) lookups.

```csharp
using System.Collections.Generic;
using UnityEngine;

public static class PartyRegistry
{
    private static readonly Dictionary<string, PartyData> _parties = new();
    private static readonly Dictionary<string, string> _characterToParty = new();

    public static void Register(PartyData party)
    {
        if (party == null || string.IsNullOrEmpty(party.PartyId)) return;

        _parties[party.PartyId] = party;

        foreach (string memberId in party.MemberIds)
            _characterToParty[memberId] = party.PartyId;
    }

    public static void Unregister(string partyId)
    {
        if (!_parties.TryGetValue(partyId, out PartyData party)) return;

        foreach (string memberId in party.MemberIds)
            _characterToParty.Remove(memberId);

        _parties.Remove(partyId);
    }

    public static PartyData GetParty(string partyId)
    {
        if (string.IsNullOrEmpty(partyId)) return null;
        _parties.TryGetValue(partyId, out PartyData party);
        return party;
    }

    public static PartyData GetPartyForCharacter(string characterId)
    {
        if (string.IsNullOrEmpty(characterId)) return null;
        if (!_characterToParty.TryGetValue(characterId, out string partyId)) return null;
        return GetParty(partyId);
    }

    public static IEnumerable<PartyData> GetAllParties() => _parties.Values;

    /// <summary>
    /// Keeps reverse lookup in sync when a member joins/leaves.
    /// Call after modifying PartyData.MemberIds.
    /// </summary>
    public static void MapCharacterToParty(string characterId, string partyId)
    {
        _characterToParty[characterId] = partyId;
    }

    public static void UnmapCharacter(string characterId)
    {
        _characterToParty.Remove(characterId);
    }

    /// <summary>
    /// Called on server shutdown to reset state.
    /// </summary>
    public static void Clear()
    {
        _parties.Clear();
        _characterToParty.Clear();
    }
}
```

- [ ] **Step 2: Verify compilation**

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Character/CharacterParty/PartyRegistry.cs
git commit -m "feat(party): add PartyRegistry static lookup class"
```

---

## Task 4: MapType Enum + MapController Changes

**Files:**
- Create: `Assets/Scripts/World/MapSystem/MapType.cs`
- Modify: `Assets/Scripts/World/MapSystem/MapController.cs`

**Read first:** `MapController.cs` — find `IsInteriorOffset` (line 18), understand how it's used throughout the file.

- [ ] **Step 1: Create `MapType.cs`**

```csharp
public enum MapType : byte
{
    Region = 0,
    Interior = 1,
    Dungeon = 2,
    Arena = 3
}
```

- [ ] **Step 2: Add `_mapType` field to `MapController.cs`**

Read `MapController.cs` first. Then add the field near line 18 (next to `IsInteriorOffset`):

```csharp
[SerializeField] private MapType _mapType = MapType.Region;
public MapType Type => _mapType;
```

- [ ] **Step 3: Replace `IsInteriorOffset` usages**

Search the codebase for all `IsInteriorOffset` references. Replace each with `_mapType == MapType.Interior`. Keep `IsInteriorOffset` as a deprecated property that reads from `_mapType` for backward compatibility:

```csharp
[Obsolete("Use Type == MapType.Interior instead")]
public bool IsInteriorOffset => _mapType == MapType.Interior;
```

- [ ] **Step 4: Verify compilation**

Run: `console-get-logs` — no errors. Search for any remaining `IsInteriorOffset` references that might break.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/World/MapSystem/MapType.cs Assets/Scripts/World/MapSystem/MapController.cs
git commit -m "feat(party): add MapType enum, migrate MapController from IsInteriorOffset"
```

---

## Task 5: Blackboard Key + BT Nodes

**Files:**
- Modify: `Assets/Scripts/AI/Core/Blackboard.cs`
- Create: `Assets/Scripts/AI/Actions/BTAction_FollowPartyLeader.cs`
- Create: `Assets/Scripts/AI/Conditions/BTCond_IsInPartyFollow.cs`
- Modify: `Assets/Scripts/AI/NPCBehaviourTree.cs`

**Read first:**
- `Assets/Scripts/AI/Core/Blackboard.cs` (lines 13-22) — existing key constants
- `Assets/Scripts/AI/Conditions/BTCond_IsInCombat.cs` — template for condition node pattern
- `Assets/Scripts/AI/Actions/BTAction_Follow.cs` — template for follow action pattern
- `Assets/Scripts/AI/NPCBehaviourTree.cs` (lines 92-131) — `BuildTree()` for insertion point

- [ ] **Step 1: Add blackboard key**

In `Blackboard.cs`, add after line 22 (after `KEY_SOCIAL_TARGET`):

```csharp
public const string KEY_PARTY_FOLLOW = "PartyFollow";
```

- [ ] **Step 2: Create `BTAction_FollowPartyLeader.cs`**

**Important:** This uses `CharacterMovement.SetDestination()` directly (NOT a `CharacterAction`), same pattern as `CharacterInvitation.FollowTargetRoutine`. This ensures following does NOT affect `IsFree()`.

```csharp
using UnityEngine;

namespace MWI.AI
{
public class BTAction_FollowPartyLeader : BTNode
{
    private const float FOLLOW_DISTANCE = 3f;
    private const float RECHECK_DISTANCE = 5f;

    protected override BTNodeStatus OnExecute(Blackboard bb)
    {
        Character self = bb.Self;
        if (self == null) return BTNodeStatus.Failure;

        Character leader = bb.Get<Character>(Blackboard.KEY_PARTY_FOLLOW);
        if (leader == null || !leader.IsAlive()) return BTNodeStatus.Failure;

        float distance = Vector3.Distance(self.transform.position, leader.transform.position);

        if (distance <= FOLLOW_DISTANCE)
        {
            self.CharacterMovement.Stop();
            return BTNodeStatus.Running;
        }

        if (distance > FOLLOW_DISTANCE)
        {
            self.CharacterMovement.SetDestination(leader.transform.position);
        }

        return BTNodeStatus.Running;
    }

    protected override void OnExit(Blackboard bb)
    {
        Character self = bb.Self;
        if (self != null && self.CharacterMovement != null)
            self.CharacterMovement.Stop();

        bb.Remove(Blackboard.KEY_PARTY_FOLLOW);
    }
}
} // namespace MWI.AI
```

- [ ] **Step 3: Create `BTCond_IsInPartyFollow.cs`**

Follows the pattern of `BTCond_IsInCombat.cs` — check condition, delegate to action.

```csharp
namespace MWI.AI
{
public class BTCond_IsInPartyFollow : BTNode
{
    private BTAction_FollowPartyLeader _followAction = new BTAction_FollowPartyLeader();

    protected override BTNodeStatus OnExecute(Blackboard bb)
    {
        Character self = bb.Self;
        if (self == null) return BTNodeStatus.Failure;

        // Check if blackboard has a follow target set by CharacterParty
        Character leader = bb.Get<Character>(Blackboard.KEY_PARTY_FOLLOW);
        if (leader == null || !leader.IsAlive()) return BTNodeStatus.Failure;

        return _followAction.Execute(bb);
    }

    protected override void OnExit(Blackboard bb)
    {
        _followAction.Abort(bb);
    }
}
} // namespace MWI.AI
```

- [ ] **Step 4: Insert into `NPCBehaviourTree.BuildTree()`**

Read `NPCBehaviourTree.cs`. In the `BuildTree()` method (around line 92), add a field for the party follow node and insert it in the `BTSelector` after `_agressionSequence` and before `_punchOutNode`.

Add field:
```csharp
private BTCond_IsInPartyFollow _partyFollowNode;
```

In `BuildTree()`, after line ~107 (where nodes are instantiated):
```csharp
_partyFollowNode = new BTCond_IsInPartyFollow();
```

In the `BTSelector` constructor (lines 119-130), insert `_partyFollowNode` between `_agressionSequence` and `_punchOutNode`:

```csharp
return new BTSelector(
    _legacySequence,       // 0. Imperative
    _orderNode,            // 1. Orders
    _combatNode,           // 2. Combat
    _entraideSequence,     // 3. Entraide
    _agressionSequence,    // 4. Aggression
    _partyFollowNode,      // 5. Party Follow  ← NEW
    _punchOutNode,         // 6. PunchOut (was 5)
    _scheduleNode,         // 7. Schedule (was 6)
    _goapNode,             // 8. GOAP (was 7)
    _socialNode,           // 9. Social (was 8)
    _wanderNode            // 10. Wander (was 9)
);
```

- [ ] **Step 5: Update `UpdateDebugNodeName()` in `NPCBehaviourTree.cs`**

Find the `UpdateDebugNodeName()` method and add the party follow node to the debug tracking:

```csharp
else if (_partyFollowNode != null && _partyFollowNode.IsRunning) _currentNodeName = "PartyFollow";
```

Insert this after the aggression check and before the punchOut check, matching the priority order.

- [ ] **Step 6: Verify compilation**

Run: `console-get-logs` — no errors.

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/AI/Core/Blackboard.cs Assets/Scripts/AI/Actions/BTAction_FollowPartyLeader.cs Assets/Scripts/AI/Conditions/BTCond_IsInPartyFollow.cs Assets/Scripts/AI/NPCBehaviourTree.cs
git commit -m "feat(party): add BT party follow nodes and integrate into behaviour tree"
```

---

## Task 6: CharacterParty Component — Core Lifecycle

This is the largest task. It creates the main `CharacterParty : CharacterSystem` class, replacing the old stub.

**Files:**
- Delete: old `Assets/Scripts/Character/CharacterParty/CharacterParty.cs` (the plain C# stub)
- Create: `Assets/Scripts/Character/CharacterParty/CharacterParty.cs` (new CharacterSystem MonoBehaviour)
- Modify: `Assets/Scripts/Character/Character.cs`

**Read first:**
- Old `Assets/Scripts/Character/CharacterParty/CharacterParty.cs` (lines 1-69) — understand what's being replaced
- `Assets/Scripts/Character/Character.cs` — lines 109 (`_currentParty`), 146 (`CurrentParty`), 486-523 (party methods), 290-340 (subsystem init pattern)
- `Assets/Scripts/Character/CharacterSystem.cs` (full) — base class pattern
- `Assets/Scripts/Character/CharacterSkills/CharacterSkills.cs` — `HasSkill()`, `AddSkill()` method signatures

- [ ] **Step 1: Delete the old `CharacterParty.cs` stub**

Delete the file at `Assets/Scripts/Character/CharacterParty/CharacterParty.cs`. This is the old plain C# class with `_partyName`, `_leader`, `_members`.

- [ ] **Step 2: Create the new `CharacterParty.cs`**

Core lifecycle only — no follow/gathering logic yet (those come in Tasks 8 & 9).

```csharp
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public class CharacterParty : CharacterSystem
{
    // --- Serialized References ---
    [SerializeField] private SkillSO _leadershipSkill;
    [SerializeField] private ToastNotificationChannel _toastChannel;

    // --- Network Variables ---
    private NetworkVariable<FixedString64Bytes> _networkPartyId = new(default,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<byte> _networkPartyState = new(0,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<byte> _networkFollowMode = new(0,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // --- Runtime State ---
    private PartyData _partyData;

    // --- Public Accessors ---
    public PartyData PartyData => _partyData;
    public bool IsInParty => _partyData != null;
    public bool IsPartyLeader => _partyData != null && _partyData.IsLeader(_character.CharacterId);
    public string NetworkPartyId => _networkPartyId.Value.ToString();
    public PartyState CurrentState => (PartyState)_networkPartyState.Value;
    public PartyFollowMode CurrentFollowMode => (PartyFollowMode)_networkFollowMode.Value;

    // --- Events (fire on both server and client) ---
    public event Action<PartyData> OnJoinedParty;
    public event Action OnLeftParty;
    public event Action<PartyFollowMode> OnFollowModeChanged;
    public event Action<PartyState> OnPartyStateChanged;
    public event Action OnGatheringStarted;
    public event Action OnGatheringComplete;
    public event Action<string> OnMemberKicked;

    // --- Leader event subscriptions ---
    private Character _subscribedLeader;

    // =============================================
    //  NETWORK LIFECYCLE
    // =============================================

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer)
        {
            // Try to rejoin party from saved PartyId
            TryReconnectToParty();
        }

        // Client: listen for network variable changes
        _networkPartyId.OnValueChanged += OnNetworkPartyIdChanged;
        _networkPartyState.OnValueChanged += OnNetworkPartyStateChanged;
        _networkFollowMode.OnValueChanged += OnNetworkFollowModeChanged;
    }

    public override void OnNetworkDespawn()
    {
        _networkPartyId.OnValueChanged -= OnNetworkPartyIdChanged;
        _networkPartyState.OnValueChanged -= OnNetworkPartyStateChanged;
        _networkFollowMode.OnValueChanged -= OnNetworkFollowModeChanged;

        UnsubscribeFromLeader();
        base.OnNetworkDespawn();
    }

    // =============================================
    //  PARTY LIFECYCLE (Server-Only)
    // =============================================

    /// <summary>
    /// Create a new party. Requires the Leadership skill.
    /// </summary>
    public bool CreateParty(string partyName = null)
    {
        if (!IsServer) return false;
        if (IsInParty) return false;
        if (!_character.CharacterSkills.HasSkill(_leadershipSkill)) return false;

        _partyData = new PartyData(_character.CharacterId, _character.CharacterName, partyName);
        PartyRegistry.Register(_partyData);

        SyncNetworkVariables();
        SubscribeToLeader(Character.FindByUUID(_partyData.LeaderId));

        OnJoinedParty?.Invoke(_partyData);
        NotifyJoinedPartyClientRpc(_partyData.PartyId, _partyData.PartyName);

        Debug.Log($"<color=cyan>[CharacterParty]</color> {_character.CharacterName} created party '{_partyData.PartyName}'");
        return true;
    }

    /// <summary>
    /// Join an existing party by ID. Auto-leaves current party if in one.
    /// </summary>
    public bool JoinParty(string partyId)
    {
        if (!IsServer) return false;

        PartyData party = PartyRegistry.GetParty(partyId);
        if (party == null) return false;

        // Auto-leave current party
        if (IsInParty)
            LeaveParty();

        int maxSize = GetMaxPartySize(party.LeaderId);
        if (party.IsFull(maxSize)) return false;

        party.AddMember(_character.CharacterId);
        PartyRegistry.MapCharacterToParty(_character.CharacterId, partyId);
        _partyData = party;

        SyncNetworkVariables();
        SubscribeToLeader(Character.FindByUUID(_partyData.LeaderId));

        OnJoinedParty?.Invoke(_partyData);
        NotifyJoinedPartyClientRpc(_partyData.PartyId, _partyData.PartyName);
        NotifyPartyMemberJoinedClientRpc(_character.CharacterName);

        Debug.Log($"<color=cyan>[CharacterParty]</color> {_character.CharacterName} joined party '{_partyData.PartyName}'");
        return true;
    }

    /// <summary>
    /// Convenience: join the party of a specific character (if they have one).
    /// </summary>
    public bool JoinCharacterParty(Character leader)
    {
        if (leader == null) return false;
        CharacterParty leaderParty = leader.CharacterParty;
        if (leaderParty == null || !leaderParty.IsInParty) return false;
        return JoinParty(leaderParty.PartyData.PartyId);
    }

    /// <summary>
    /// Leave the current party.
    /// </summary>
    public void LeaveParty()
    {
        if (!IsServer || !IsInParty) return;

        string partyId = _partyData.PartyId;
        string charId = _character.CharacterId;
        bool wasLeader = _partyData.IsLeader(charId);

        _partyData.RemoveMember(charId);
        PartyRegistry.UnmapCharacter(charId);

        UnsubscribeFromLeader();

        // If leader left, the new leader is auto-assigned in RemoveMember
        if (wasLeader && _partyData.MemberCount > 0)
        {
            GrantLeadershipSkillIfNeeded(_partyData.LeaderId);
            NotifyLeaderChangedClientRpc(_partyData.LeaderId);
        }

        // Disband if empty
        if (_partyData.MemberCount == 0)
        {
            PartyRegistry.Unregister(partyId);
        }

        _partyData = null;
        SyncNetworkVariables();

        OnLeftParty?.Invoke();
        NotifyLeftPartyClientRpc();

        Debug.Log($"<color=cyan>[CharacterParty]</color> {_character.CharacterName} left party");
    }

    /// <summary>
    /// Kick a member. Leader-only. Works even if target is offline.
    /// </summary>
    public void KickMember(string characterId)
    {
        if (!IsServer || !IsInParty || !IsPartyLeader) return;
        if (characterId == _character.CharacterId) return; // Can't kick yourself

        _partyData.RemoveMember(characterId);
        PartyRegistry.UnmapCharacter(characterId);

        // Try to notify the kicked character if online
        Character kicked = Character.FindByUUID(characterId);
        if (kicked != null && kicked.CharacterParty != null)
        {
            kicked.CharacterParty.HandleKicked();
        }

        OnMemberKicked?.Invoke(characterId);
        NotifyMemberKickedClientRpc(characterId);

        if (_partyData.MemberCount == 0)
        {
            PartyRegistry.Unregister(_partyData.PartyId);
            _partyData = null;
            SyncNetworkVariables();
            OnLeftParty?.Invoke();
            NotifyLeftPartyClientRpc();
        }
    }

    /// <summary>
    /// Promote a member to leader. Leader-only.
    /// </summary>
    public void PromoteLeader(string characterId)
    {
        if (!IsServer || !IsInParty || !IsPartyLeader) return;
        if (!_partyData.IsMember(characterId)) return;

        _partyData.LeaderId = characterId;
        GrantLeadershipSkillIfNeeded(characterId);

        // Re-subscribe all members to new leader
        NotifyLeaderChangedClientRpc(characterId);

        // Update own subscription
        UnsubscribeFromLeader();
        SubscribeToLeader(Character.FindByUUID(characterId));

        Debug.Log($"<color=cyan>[CharacterParty]</color> {_character.CharacterName} promoted {characterId} to party leader");
    }

    /// <summary>
    /// Set follow mode. Leader-only.
    /// </summary>
    public void SetFollowMode(PartyFollowMode mode)
    {
        if (!IsServer || !IsInParty || !IsPartyLeader) return;
        _partyData.FollowMode = mode;
        _networkFollowMode.Value = (byte)mode;
        OnFollowModeChanged?.Invoke(mode);
    }

    /// <summary>
    /// Disband the entire party. Leader-only.
    /// </summary>
    public void DisbandParty()
    {
        if (!IsServer || !IsInParty || !IsPartyLeader) return;

        string partyId = _partyData.PartyId;
        List<string> memberIds = new List<string>(_partyData.MemberIds);

        foreach (string memberId in memberIds)
        {
            Character member = Character.FindByUUID(memberId);
            if (member != null && member.CharacterParty != null)
            {
                member.CharacterParty.HandleDisbanded();
            }
            PartyRegistry.UnmapCharacter(memberId);
        }

        PartyRegistry.Unregister(partyId);
    }

    // =============================================
    //  INTERNAL HANDLERS
    // =============================================

    private void HandleKicked()
    {
        if (!IsServer) return;
        UnsubscribeFromLeader();
        string partyName = _partyData?.PartyName ?? "the party";
        _partyData = null;
        SyncNetworkVariables();
        OnLeftParty?.Invoke();
        NotifyLeftPartyClientRpc();
        // Toast on client: "You were removed from [Party Name]"
        NotifyKickedToastClientRpc(partyName);
    }

    private void HandleDisbanded()
    {
        if (!IsServer) return;
        UnsubscribeFromLeader();
        _partyData = null;
        SyncNetworkVariables();
        OnLeftParty?.Invoke();
        NotifyLeftPartyClientRpc();
    }

    // =============================================
    //  LEADER EVENT SUBSCRIPTIONS
    // =============================================

    protected override void HandleDeath(Character character)
    {
        // Self died — handled by CharacterSystem base
    }

    protected override void HandleIncapacitated(Character character)
    {
        // Self incapacitated — handled by CharacterSystem base
    }

    protected override void HandleWakeUp(Character character)
    {
        // Self woke up — handled by CharacterSystem base
    }

    private void OnLeaderDied(Character leader)
    {
        if (!IsServer || !IsInParty) return;

        // IMPORTANT: This fires on EVERY member subscribed to the leader.
        // Only process once — the first member to handle it will modify PartyData.
        // Check if the leader is still in the member list (guards against duplicate processing).
        if (!_partyData.IsMember(leader.CharacterId)) return;

        UnsubscribeFromLeader();

        if (_partyData.MemberCount <= 1)
        {
            // Only leader left (and they died), disband
            HandleDisbanded();
            return;
        }

        _partyData.RemoveMember(leader.CharacterId);
        PartyRegistry.UnmapCharacter(leader.CharacterId);

        // Auto-promote (RemoveMember already set LeaderId to MemberIds[0])
        GrantLeadershipSkillIfNeeded(_partyData.LeaderId);
        NotifyLeaderChangedClientRpc(_partyData.LeaderId);

        // Re-subscribe to new leader (unless I became the leader)
        if (!_partyData.IsLeader(_character.CharacterId))
        {
            Character newLeader = Character.FindByUUID(_partyData.LeaderId);
            SubscribeToLeader(newLeader);
        }
        else
        {
            Debug.Log($"<color=cyan>[CharacterParty]</color> {_character.CharacterName} became party leader after leader death");
        }

        UpdateAllMembersFollowState();
    }

    private void OnLeaderIncapacitated(Character leader)
    {
        if (!IsServer || !IsInParty) return;
        SetPartyState(PartyState.LeaderlessHold);
    }

    private void OnLeaderWokeUp(Character leader)
    {
        if (!IsServer || !IsInParty) return;
        if (_partyData.State == PartyState.LeaderlessHold)
            SetPartyState(PartyState.Active);
    }

    private void SubscribeToLeader(Character leader)
    {
        if (leader == null || leader == _character) return;
        UnsubscribeFromLeader();
        _subscribedLeader = leader;
        _subscribedLeader.OnDeath += OnLeaderDied;
        _subscribedLeader.OnIncapacitated += OnLeaderIncapacitated;
        _subscribedLeader.OnWakeUp += OnLeaderWokeUp;
    }

    private void UnsubscribeFromLeader()
    {
        if (_subscribedLeader == null) return;
        _subscribedLeader.OnDeath -= OnLeaderDied;
        _subscribedLeader.OnIncapacitated -= OnLeaderIncapacitated;
        _subscribedLeader.OnWakeUp -= OnLeaderWokeUp;
        _subscribedLeader = null;
    }

    // =============================================
    //  RECONNECT
    // =============================================

    private void TryReconnectToParty()
    {
        // TODO: Read PartyId from ICharacterData save file
        // For now, check if PartyRegistry already has us
        PartyData existing = PartyRegistry.GetPartyForCharacter(_character.CharacterId);
        if (existing != null)
        {
            _partyData = existing;
            SyncNetworkVariables();
            SubscribeToLeader(Character.FindByUUID(_partyData.LeaderId));
            OnJoinedParty?.Invoke(_partyData);
            Debug.Log($"<color=cyan>[CharacterParty]</color> {_character.CharacterName} reconnected to party '{_partyData.PartyName}'");
        }
    }

    // =============================================
    //  HELPERS
    // =============================================

    private void SetPartyState(PartyState state)
    {
        if (!IsServer || _partyData == null) return;
        _partyData.State = state;
        _networkPartyState.Value = (byte)state;
        OnPartyStateChanged?.Invoke(state);
    }

    private void SyncNetworkVariables()
    {
        if (!IsServer) return;
        string partyId = _partyData?.PartyId ?? "";
        _networkPartyId.Value = new FixedString64Bytes(partyId);
        _networkPartyState.Value = (byte)(_partyData?.State ?? PartyState.Active);
        _networkFollowMode.Value = (byte)(_partyData?.FollowMode ?? PartyFollowMode.Strict);
    }

    private int GetMaxPartySize(string leaderId)
    {
        Character leader = Character.FindByUUID(leaderId);
        if (leader == null) return 2;
        int level = leader.CharacterSkills.GetSkillLevel(_leadershipSkill);
        return Mathf.Min(2 + level, 8);
    }

    private void GrantLeadershipSkillIfNeeded(string characterId)
    {
        Character c = Character.FindByUUID(characterId);
        if (c != null && !c.CharacterSkills.HasSkill(_leadershipSkill))
        {
            c.CharacterSkills.AddSkill(_leadershipSkill, 1);
            Debug.Log($"<color=cyan>[CharacterParty]</color> {c.CharacterName} gained Leadership skill through succession");
        }
    }

    // =============================================
    //  CLIENT RPCs
    // =============================================

    [Rpc(SendTo.NotServer)]
    private void NotifyJoinedPartyClientRpc(string partyId, string partyName)
    {
        if (_partyData == null)
        {
            // Client reconstructs minimal data for UI
            _partyData = PartyRegistry.GetParty(partyId);
        }
        OnJoinedParty?.Invoke(_partyData);
    }

    [Rpc(SendTo.NotServer)]
    private void NotifyLeftPartyClientRpc()
    {
        _partyData = null;
        OnLeftParty?.Invoke();
    }

    [Rpc(SendTo.NotServer)]
    private void NotifyPartyMemberJoinedClientRpc(string memberName)
    {
        // Toast: "[Name] joined the party"
    }

    [Rpc(SendTo.NotServer)]
    private void NotifyMemberKickedClientRpc(string characterId)
    {
        OnMemberKicked?.Invoke(characterId);
    }

    [Rpc(SendTo.NotServer)]
    private void NotifyLeaderChangedClientRpc(string newLeaderId)
    {
        if (_partyData != null)
            _partyData.LeaderId = newLeaderId;

        // Re-subscribe on client side
        UnsubscribeFromLeader();
        Character newLeader = Character.FindByUUID(newLeaderId);
        SubscribeToLeader(newLeader);
    }

    [Rpc(SendTo.NotServer)]
    private void NotifyKickedToastClientRpc(string partyName)
    {
        // Show "You were removed from [Party Name]" toast
    }

    // =============================================
    //  NETWORK VARIABLE CHANGE CALLBACKS (Client)
    // =============================================

    private void OnNetworkPartyIdChanged(FixedString64Bytes prev, FixedString64Bytes next)
    {
        // Client can use this to show/hide party panel
    }

    private void OnNetworkPartyStateChanged(byte prev, byte next)
    {
        OnPartyStateChanged?.Invoke((PartyState)next);
    }

    private void OnNetworkFollowModeChanged(byte prev, byte next)
    {
        OnFollowModeChanged?.Invoke((PartyFollowMode)next);
    }

    // =============================================
    //  CLEANUP
    // =============================================

    protected override void OnDisable()
    {
        UnsubscribeFromLeader();
        base.OnDisable();
    }
}
```

- [ ] **Step 3: Update `Character.cs`**

Read `Character.cs` lines 109, 146, 486-523 first.

**Remove** these old party members:
- Line 109: `private CharacterParty _currentParty;`
- Line 146: `public CharacterParty CurrentParty => _currentParty;`
- Lines 486-523: `IsInParty()`, `IsPartyLeader()`, `CreateParty()`, `SetParty()`, `Invite()`

**Add** the new subsystem reference following the existing pattern (look at how `_characterCombat`, `_characterJob`, etc. are declared):

Near other subsystem fields (around line 68-90):
```csharp
[SerializeField] private CharacterParty _characterParty;
public CharacterParty CharacterParty => _characterParty;
```

In the `Awake()` method (around line 290-340), add the GetComponentInChildren fallback:
```csharp
if (_characterParty == null) _characterParty = GetComponentInChildren<CharacterParty>();
```

**Also add convenience properties** that delegate to the new component (to minimize breakage in code that uses the old API):
```csharp
public bool IsInParty() => _characterParty != null && _characterParty.IsInParty;
public bool IsPartyLeader() => _characterParty != null && _characterParty.IsPartyLeader;
```

- [ ] **Step 4: Fix any remaining compilation errors**

Search the codebase for references to the old `CharacterParty` class (the plain C# one) — e.g., `new CharacterParty(`, `_currentParty`, `SetParty(`. Fix each reference to use the new API.

- [ ] **Step 5: Verify compilation**

Run: `console-get-logs` — no errors.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Character/CharacterParty/CharacterParty.cs Assets/Scripts/Character/Character.cs
git commit -m "feat(party): add CharacterParty CharacterSystem component, replace old stub"
```

---

## Task 7: PartyGatherZone — Trigger Forwarder

**Files:**
- Create: `Assets/Scripts/Character/CharacterParty/PartyGatherZone.cs`

- [ ] **Step 1: Create `PartyGatherZone.cs`**

Small MonoBehaviour placed on a child GameObject. Forwards trigger events to the parent `CharacterParty`.

```csharp
using UnityEngine;

/// <summary>
/// Placed on a child GameObject with a BoxCollider (isTrigger) on the "PartyGather" physics layer.
/// Forwards trigger events to the parent CharacterParty component.
/// </summary>
public class PartyGatherZone : MonoBehaviour
{
    private CharacterParty _owner;

    public void Initialize(CharacterParty owner)
    {
        _owner = owner;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_owner == null) return;
        if (other.TryGetComponent(out Character character))
        {
            _owner.OnGatherZoneEnter(character);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (_owner == null) return;
        if (other.TryGetComponent(out Character character))
        {
            _owner.OnGatherZoneExit(character);
        }
    }
}
```

- [ ] **Step 2: Add gather zone methods to `CharacterParty.cs`**

Add these public methods (called by `PartyGatherZone`):

```csharp
// --- Gather Zone State ---
private HashSet<string> _gatheredMemberIds = new();
private PartyGatherZone _gatherZone;

public void OnGatherZoneEnter(Character character)
{
    if (!IsServer || _partyData == null) return;
    if (_partyData.State != PartyState.Gathering) return;
    if (!_partyData.IsMember(character.CharacterId)) return;

    _gatheredMemberIds.Add(character.CharacterId);
    Debug.Log($"<color=cyan>[CharacterParty]</color> {character.CharacterName} entered gather zone ({_gatheredMemberIds.Count}/{_partyData.MemberCount})");
}

public void OnGatherZoneExit(Character character)
{
    if (!IsServer || _partyData == null) return;
    _gatheredMemberIds.Remove(character.CharacterId);
}
```

- [ ] **Step 3: Verify compilation**

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Character/CharacterParty/PartyGatherZone.cs Assets/Scripts/Character/CharacterParty/CharacterParty.cs
git commit -m "feat(party): add PartyGatherZone trigger forwarder"
```

---

## Task 8: CharacterParty — Follow Logic

**Files:**
- Modify: `Assets/Scripts/Character/CharacterParty/CharacterParty.cs`

**Read first:** `Assets/Scripts/Character/CharacterInvitation/CharacterInvitation.cs` lines 276-338 — `FollowTargetRoutine` for the pattern of setting blackboard flags and using CharacterMovement.

- [ ] **Step 1: Add follow management methods to `CharacterParty.cs`**

The follow logic sets/clears the blackboard flag. The BT node (Task 5) handles the actual pathfinding.

```csharp
// =============================================
//  FOLLOW LOGIC (Server-Only)
// =============================================

/// <summary>
/// Called every time party state changes to update the blackboard follow flag.
/// Server-only. Only affects NPCs.
/// </summary>
public void UpdateFollowState()
{
    if (!IsServer || !IsInParty) return;
    if (_character.IsPlayer()) return; // Player members follow on their own
    if (IsPartyLeader) return; // Leader doesn't follow anyone

    Character leader = Character.FindByUUID(_partyData.LeaderId);
    bool shouldFollow = _partyData.State == PartyState.Active
                     && _partyData.FollowMode == PartyFollowMode.Strict
                     && leader != null
                     && leader.IsAlive()
                     && IsOnSameMap(leader);

    NPCController controller = _character.Controller as NPCController;
    if (controller == null || controller.BehaviourTree == null) return;

    Blackboard bb = controller.BehaviourTree.Blackboard;
    if (bb == null) return;

    if (shouldFollow)
    {
        bb.Set(Blackboard.KEY_PARTY_FOLLOW, leader);
    }
    else
    {
        bb.Remove(Blackboard.KEY_PARTY_FOLLOW);
    }
}

/// <summary>
/// Clears follow flag unconditionally.
/// </summary>
public void ClearFollowState()
{
    if (_character.IsPlayer()) return;

    NPCController controller = _character.Controller as NPCController;
    if (controller == null) return;

    controller.BehaviourTree?.Blackboard?.Remove(Blackboard.KEY_PARTY_FOLLOW);
}

private bool IsOnSameMap(Character other)
{
    if (other == null) return false;
    // Compare via CharacterMapTracker or parent MapController
    var myMap = _character.GetComponentInParent<MapController>();
    var otherMap = other.GetComponentInParent<MapController>();
    if (myMap == null || otherMap == null) return false;
    return myMap.MapId == otherMap.MapId;
}
```

- [ ] **Step 2: Hook follow state updates into lifecycle methods**

Add `UpdateFollowState()` calls in the relevant places (after joining, after leader changes, after state changes, after follow mode changes):

- End of `JoinParty()` → `UpdateFollowState();`
- End of `SetFollowMode()` → broadcast `UpdateFollowState()` on all NPC members
- End of `OnLeaderWokeUp()` → broadcast `UpdateFollowState()` on all NPC members
- End of `SetPartyState()` → broadcast `UpdateFollowState()` on all NPC members
- In `LeaveParty()` and `HandleKicked()` → `ClearFollowState();`

To broadcast to all NPC members:
```csharp
private void UpdateAllMembersFollowState()
{
    if (_partyData == null) return;
    foreach (string memberId in _partyData.MemberIds)
    {
        Character member = Character.FindByUUID(memberId);
        if (member != null && member.CharacterParty != null)
            member.CharacterParty.UpdateFollowState();
    }
}
```

- [ ] **Step 3: Verify compilation**

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Character/CharacterParty/CharacterParty.cs
git commit -m "feat(party): add follow logic with BT blackboard integration"
```

---

## Task 9: CharacterParty — Gathering Logic

**Files:**
- Modify: `Assets/Scripts/Character/CharacterParty/CharacterParty.cs`

- [ ] **Step 1: Add gathering state and methods**

```csharp
// =============================================
//  GATHERING LOGIC (Server-Only)
// =============================================

private string _gatherTargetMapId;
private Vector3 _gatherTargetPosition;
private Coroutine _gatherCoroutine;
private GameObject _gatherZoneGO;

/// <summary>
/// Called by MapTransitionDoor or MapTransitionZone when the leader tries to transition
/// to a Region or Dungeon map.
/// </summary>
public void StartGathering(string targetMapId, Vector3 targetPosition)
{
    if (!IsServer || !IsInParty || !IsPartyLeader) return;
    if (_partyData.State == PartyState.Gathering) return;

    _gatherTargetMapId = targetMapId;
    _gatherTargetPosition = targetPosition;
    _gatheredMemberIds.Clear();
    _gatheredMemberIds.Add(_character.CharacterId); // Leader is always gathered

    // Stop leader movement
    _character.CharacterMovement.Stop();

    // Enable gather zone
    EnableGatherZone();

    SetPartyState(PartyState.Gathering);
    OnGatheringStarted?.Invoke();
    NotifyGatheringStartedClientRpc();

    // Notify NPC members to pathfind to leader
    UpdateAllMembersFollowState();

    // Start timeout for NPC leaders, or auto-check coroutine for player leaders
    if (!_character.IsPlayer())
    {
        _gatherCoroutine = StartCoroutine(NPCGatherTimeoutRoutine(30f));
    }
    else
    {
        _gatherCoroutine = StartCoroutine(PlayerGatherCheckRoutine());
    }

    Debug.Log($"<color=cyan>[CharacterParty]</color> {_character.CharacterName} started gathering for transition to {targetMapId}");
}

/// <summary>
/// For player leaders: checks every 0.5s if all free members are gathered.
/// Auto-proceeds when ready. If any are busy, prompts via UI.
/// </summary>
private System.Collections.IEnumerator PlayerGatherCheckRoutine()
{
    while (_partyData != null && _partyData.State == PartyState.Gathering)
    {
        yield return new WaitForSecondsRealtime(0.5f);

        int totalMembers = _partyData.MemberCount;
        int gathered = _gatheredMemberIds.Count;
        int busy = 0;

        foreach (string memberId in _partyData.MemberIds)
        {
            Character member = Character.FindByUUID(memberId);
            if (member != null && !member.IsFree() && !_gatheredMemberIds.Contains(memberId))
                busy++;
        }

        int freeUngathered = totalMembers - gathered - busy;

        if (freeUngathered <= 0 && busy == 0)
        {
            // All free members gathered, no busy members — auto-proceed
            ProceedTransition();
            yield break;
        }
        else if (freeUngathered <= 0 && busy > 0)
        {
            // All free members gathered, but some are busy — prompt player
            // "[Name] is still in combat. Leave without them?"
            // The UI_PartyPanel listens to OnPartyStateChanged and shows the prompt.
            // Player calls ProceedTransition() or CancelGathering() via UI.
            // Just wait for the player's decision.
        }
    }
}

/// <summary>
/// Proceed with the transition. All gathered members get CharacterMapTransitionAction.
/// </summary>
public void ProceedTransition()
{
    if (!IsServer || _partyData.State != PartyState.Gathering) return;

    if (_gatherCoroutine != null)
    {
        StopCoroutine(_gatherCoroutine);
        _gatherCoroutine = null;
    }

    // Execute transition for all gathered members
    foreach (string memberId in _gatheredMemberIds)
    {
        Character member = Character.FindByUUID(memberId);
        if (member == null) continue;

        // NOTE: Passing null for door. Verify that CharacterMapTransitionAction
        // handles a null door gracefully (check its OnStart/OnApplyEffect methods).
        // If it doesn't, either add a null guard there or pass a dummy reference.
        var transitionAction = new CharacterMapTransitionAction(
            member, null, _gatherTargetMapId, _gatherTargetPosition, 0.5f);
        member.CharacterActions.ExecuteAction(transitionAction);
    }

    DisableGatherZone();
    _gatheredMemberIds.Clear();
    SetPartyState(PartyState.Active);

    // Revert loose mode to strict when leaving a community region
    if (_partyData.FollowMode == PartyFollowMode.Loose)
    {
        SetFollowMode(PartyFollowMode.Strict);
    }

    OnGatheringComplete?.Invoke();

    Debug.Log($"<color=cyan>[CharacterParty]</color> Gathering complete, transitioning party to {_gatherTargetMapId}");
}

/// <summary>
/// Cancel gathering without transitioning.
/// </summary>
public void CancelGathering()
{
    if (!IsServer || _partyData.State != PartyState.Gathering) return;

    if (_gatherCoroutine != null)
    {
        StopCoroutine(_gatherCoroutine);
        _gatherCoroutine = null;
    }

    DisableGatherZone();
    _gatheredMemberIds.Clear();
    SetPartyState(PartyState.Active);
    UpdateAllMembersFollowState();
}

private System.Collections.IEnumerator NPCGatherTimeoutRoutine(float timeout)
{
    // Use WaitForSecondsRealtime — gathering is a "real-time" UI/network operation,
    // not simulation time. Per CLAUDE.md rule #24, must not be affected by GameSpeedController.
    yield return new WaitForSecondsRealtime(timeout);
    ProceedTransition();
}

// =============================================
//  GATHER ZONE MANAGEMENT
// =============================================

private void EnableGatherZone()
{
    if (_gatherZoneGO == null)
    {
        _gatherZoneGO = new GameObject("GatherZone");
        _gatherZoneGO.transform.SetParent(transform);
        _gatherZoneGO.transform.localPosition = Vector3.zero;
        _gatherZoneGO.layer = LayerMask.NameToLayer("PartyGather");

        BoxCollider col = _gatherZoneGO.AddComponent<BoxCollider>();
        col.isTrigger = true;
        col.size = new Vector3(6f, 4f, 6f);

        _gatherZone = _gatherZoneGO.AddComponent<PartyGatherZone>();
        _gatherZone.Initialize(this);
    }

    _gatherZoneGO.SetActive(true);
}

private void DisableGatherZone()
{
    if (_gatherZoneGO != null)
        _gatherZoneGO.SetActive(false);
}

[Rpc(SendTo.NotServer)]
private void NotifyGatheringStartedClientRpc()
{
    OnGatheringStarted?.Invoke();
}
```

- [ ] **Step 2: Add cleanup for gathering in `OnDisable()`**

In the existing `OnDisable()` override:
```csharp
if (_gatherCoroutine != null)
{
    StopCoroutine(_gatherCoroutine);
    _gatherCoroutine = null;
}
DisableGatherZone();
```

- [ ] **Step 3: Verify compilation**

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Character/CharacterParty/CharacterParty.cs
git commit -m "feat(party): add gathering logic with gather zone and timeout"
```

---

## Task 10: MapTransitionDoor — Party Leader Interception

**Files:**
- Modify: `Assets/Scripts/World/MapSystem/MapTransitionDoor.cs`

**Read first:** `MapTransitionDoor.cs` lines 58-107 — the transition section of `Interact()`.

- [ ] **Step 1: Add party leader check before transition**

In `MapTransitionDoor.Interact()`, after `targetMapId` and `dest` are resolved (around line 100, just before `var transitionAction = new CharacterMapTransitionAction(...)`) insert:

```csharp
// --- Party Leader Gathering Check ---
if (interactor.CharacterParty != null && interactor.CharacterParty.IsInParty && interactor.CharacterParty.IsPartyLeader)
{
    MapController targetMap = MapController.GetByMapId(targetMapId);
    if (targetMap != null && (targetMap.Type == MapType.Region || targetMap.Type == MapType.Dungeon))
    {
        interactor.CharacterParty.StartGathering(targetMapId, dest);
        return; // Do NOT create CharacterMapTransitionAction
    }
}
```

- [ ] **Step 2: Verify compilation**

- [ ] **Step 3: Test manually**

In Unity Editor:
1. Give a character the Leadership skill
2. Create a party via `CharacterParty.CreateParty()`
3. Add a member via `JoinParty()`
4. Have the leader interact with a `MapTransitionDoor` leading to a Region
5. Verify: leader stops, gather zone appears, transition action is NOT created

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/World/MapSystem/MapTransitionDoor.cs
git commit -m "feat(party): intercept party leader at MapTransitionDoor for gathering"
```

---

## Task 11: PartyInvitation — InteractionInvitation Subclass

**Files:**
- Create: `Assets/Scripts/Character/CharacterParty/PartyInvitation.cs`

**Read first:** `Assets/Scripts/Character/CharacterInteraction/InteractionInvitation.cs` (full) — the base class with `CanExecute`, `OnAccepted`, etc.

- [ ] **Step 1: Create `PartyInvitation.cs`**

```csharp
using UnityEngine;

public class PartyInvitation : InteractionInvitation
{
    private SkillSO _leadershipSkill;

    public PartyInvitation(SkillSO leadershipSkill)
    {
        _leadershipSkill = leadershipSkill;
    }

    public override bool CanExecute(Character source, Character target)
    {
        if (source == null || target == null) return false;
        if (source == target) return false;
        if (!target.IsAlive() || !target.IsFree()) return false;

        // Source must have Leadership skill
        if (!source.CharacterSkills.HasSkill(_leadershipSkill)) return false;

        // Target must not be in any party
        if (target.CharacterParty != null && target.CharacterParty.IsInParty) return false;

        // Check party capacity
        CharacterParty sourceParty = source.CharacterParty;
        if (sourceParty != null && sourceParty.IsInParty)
        {
            int maxSize = Mathf.Min(2 + source.CharacterSkills.GetSkillLevel(_leadershipSkill), 8);
            if (sourceParty.PartyData.IsFull(maxSize)) return false;
        }

        return true;
    }

    public override string GetInvitationMessage(Character source, Character target)
    {
        return "Want to join my group?";
    }

    public override void OnAccepted(Character source, Character target)
    {
        CharacterParty sourceParty = source.CharacterParty;

        // Auto-create party if source doesn't have one
        if (sourceParty != null && !sourceParty.IsInParty)
        {
            sourceParty.CreateParty();
        }

        if (sourceParty != null && sourceParty.IsInParty)
        {
            target.CharacterParty.JoinParty(sourceParty.PartyData.PartyId);
        }
    }

    public override void OnRefused(Character source, Character target)
    {
        // No relation impact — spec requirement
    }

    public override bool? EvaluateCustomInvitation(Character source, Character target)
    {
        // Fall through to default sociability/relationship evaluation
        return null;
    }
}
```

- [ ] **Step 2: Verify compilation**

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Character/CharacterParty/PartyInvitation.cs
git commit -m "feat(party): add PartyInvitation InteractionInvitation subclass"
```

---

## Task 12: Leadership SkillSO Asset

**Files:**
- Create: `Assets/Data/Skills/Leadership.asset` (via Unity MCP)

- [ ] **Step 1: Check if Skills data folder exists**

Use `assets-find` MCP tool to search for existing SkillSO assets: `t:SkillSO`.

- [ ] **Step 2: Create the Leadership SkillSO asset**

Use `assets-find` to locate the correct path for skill assets. Then use the Unity Editor to create a new SkillSO asset:

Via MCP `script-execute`:
```csharp
public class CreateLeadershipSkill
{
    public static void Execute()
    {
        var skill = ScriptableObject.CreateInstance<SkillSO>();
        skill.SkillID = "leadership";
        skill.SkillName = "Leadership";
        skill.Description = "The ability to lead and manage a party of adventurers.";
        skill.BaseProficiencyPerLevel = 1.0f;

        string path = "Assets/Data/Skills/Leadership.asset";
        string dir = System.IO.Path.GetDirectoryName(path);
        if (!System.IO.Directory.Exists(dir))
            System.IO.Directory.CreateDirectory(dir);

        UnityEditor.AssetDatabase.CreateAsset(skill, path);
        UnityEditor.AssetDatabase.SaveAssets();
        UnityEditor.AssetDatabase.Refresh();

        Debug.Log($"Created Leadership SkillSO at {path}");
    }
}
```

- [ ] **Step 3: Assign the Leadership SkillSO to CharacterParty on the Character prefab**

Use MCP to:
1. Open the Character_Default prefab (`assets-prefab-open`)
2. Find the CharacterParty component (will need to be added to the prefab first — see Task 13)
3. Set `_leadershipSkill` to the newly created asset

- [ ] **Step 4: Commit**

```bash
git add Assets/Data/Skills/Leadership.asset
git commit -m "feat(party): create Leadership SkillSO asset"
```

---

## Task 13: Prefab Setup — Add CharacterParty to Character Prefab

**Files:**
- Modify: Character_Default prefab (via Unity MCP)

- [ ] **Step 1: Open the Character prefab**

Use MCP `assets-prefab-open` to open `Character_Default.prefab`.

- [ ] **Step 2: Create the CharacterParty child GameObject**

Following the Character hierarchy pattern (each subsystem on its own child GO):

1. Use `gameobject-create` to create a child GO named "CharacterParty" under the root Character GO
2. Use `gameobject-component-add` to add the `CharacterParty` component to this child GO
3. Use `gameobject-component-modify` to set `_leadershipSkill` to the Leadership SkillSO asset

- [ ] **Step 3: Wire the reference in the root Character component**

Use `gameobject-component-modify` on the root Character component to set `_characterParty` to reference the CharacterParty child GO's component.

- [ ] **Step 4: Save and close the prefab**

Use `assets-prefab-save` then `assets-prefab-close`.

- [ ] **Step 5: Verify in console**

Run: `console-get-logs` — no errors.

- [ ] **Step 6: Commit**

```bash
git add Assets/Prefabs/Character/Character_Default.prefab
git commit -m "feat(party): add CharacterParty component to Character prefab"
```

---

## Task 14: HibernatedNPCData — Add PartyId Field

**Files:**
- Modify: `Assets/Scripts/World/MapSystem/MapSaveData.cs`

**Read first:** `MapSaveData.cs` — the `HibernatedNPCData` class (lines 26-69).

- [ ] **Step 1: Add `PartyId` field to `HibernatedNPCData`**

In `HibernatedNPCData` (after the Identity section, around line 30):

```csharp
// Party
public string PartyId;
```

- [ ] **Step 2: Ensure PartyId is set during hibernation serialization**

Search for where `HibernatedNPCData` is populated (likely in `MapController` or a hibernation manager). Add:

```csharp
data.PartyId = character.CharacterParty != null && character.CharacterParty.IsInParty
    ? character.CharacterParty.PartyData.PartyId
    : null;
```

- [ ] **Step 3: Ensure PartyId is restored during wake-up**

Search for where `HibernatedNPCData` is deserialized back into a Character. After the character spawns and `CharacterParty` is initialized:

```csharp
// PartyId reconnection happens automatically in CharacterParty.OnNetworkSpawn -> TryReconnectToParty
// which checks PartyRegistry. No manual restoration needed IF the party was loaded into PartyRegistry first.
```

- [ ] **Step 4: Verify compilation**

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/World/MapSystem/MapSaveData.cs
git commit -m "feat(party): add PartyId field to HibernatedNPCData"
```

---

## Task 14b: ICharacterData — Add PartyId Field

**Files:**
- Modify: `Assets/Scripts/Character/SaveLoad/` — find `ICharacterData` or equivalent save data interface/class

**Read first:** Search for `ICharacterData` in the codebase. Also search for character save/load code to find where character data is serialized.

- [ ] **Step 1: Find the character save data structure**

Use Grep to find `ICharacterData` or the character save data class. Search for how `CharacterId` is saved/loaded to find the right file.

- [ ] **Step 2: Add `PartyId` field**

Add to the save data structure:
```csharp
public string PartyId;
```

- [ ] **Step 3: Wire save — on disconnect/save, write PartyId**

Find where character data is saved (on disconnect, periodic saves). Add:
```csharp
saveData.PartyId = character.CharacterParty != null && character.CharacterParty.IsInParty
    ? character.CharacterParty.PartyData.PartyId
    : null;
```

- [ ] **Step 4: Wire load — update `TryReconnectToParty()` in `CharacterParty.cs`**

Replace the TODO in `TryReconnectToParty()` with actual load logic:
```csharp
private void TryReconnectToParty()
{
    // First check PartyRegistry (covers NPC hibernation wake-up case)
    PartyData existing = PartyRegistry.GetPartyForCharacter(_character.CharacterId);
    if (existing != null)
    {
        _partyData = existing;
        SyncNetworkVariables();
        SubscribeToLeader(Character.FindByUUID(_partyData.LeaderId));
        OnJoinedParty?.Invoke(_partyData);
        return;
    }

    // For player reconnect: read saved PartyId from character data
    // (exact code depends on the save system's API — read the file to determine)
    // If PartyId exists but party doesn't exist in registry → cleared (kicked while offline)
}
```

- [ ] **Step 5: Verify compilation**

- [ ] **Step 6: Commit**

```bash
git add -A Assets/Scripts/Character/SaveLoad/ Assets/Scripts/Character/CharacterParty/CharacterParty.cs
git commit -m "feat(party): add PartyId to character save data and wire reconnect"
```

---

## Task 14c: SaveManager — Party Persistence Boot Loading

**Files:**
- Modify: `Assets/Scripts/World/SaveLoad/SaveManager.cs` (or equivalent)

**Read first:** Search for `SaveManager` to find the exact file and understand the initialization sequence. Look for where `MapSaveData` or `HibernatedNPCData` is loaded.

- [ ] **Step 1: Find SaveManager and its init sequence**

Grep for `class SaveManager` and for where world save data is loaded on server boot.

- [ ] **Step 2: Add PartyData serialization to world save**

Add a `List<PartyData>` field to the world save data structure:
```csharp
public List<PartyData> SavedParties = new List<PartyData>();
```

- [ ] **Step 3: Save parties on world save**

In the world save method, collect all active parties:
```csharp
worldSaveData.SavedParties.Clear();
foreach (PartyData party in PartyRegistry.GetAllParties())
{
    worldSaveData.SavedParties.Add(party);
}
```

- [ ] **Step 4: Load parties on server boot**

In the `SaveManager` initialization sequence (before any `MapController.OnNetworkSpawn`):
```csharp
PartyRegistry.Clear();
if (worldSaveData.SavedParties != null)
{
    foreach (PartyData party in worldSaveData.SavedParties)
    {
        party.State = PartyState.Active; // Reset transient state
        PartyRegistry.Register(party);
    }
}
```

- [ ] **Step 5: Add `PartyRegistry.Clear()` call on server shutdown**

Find the server shutdown/cleanup path and add `PartyRegistry.Clear();`.

- [ ] **Step 6: Verify compilation**

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/World/SaveLoad/
git commit -m "feat(party): add PartyData persistence to SaveManager boot/save cycle"
```

---

## Task 14d: MacroSimulator — Party-Aware Position Snap

**Files:**
- Modify: `Assets/Scripts/World/Simulation/MacroSimulator.cs` (or equivalent)

**Read first:** Search for `MacroSimulator` to find the exact file. Look for the position snap logic during hibernation catch-up.

- [ ] **Step 1: Find MacroSimulator and position snap code**

Grep for `class MacroSimulator` and for position snap or hibernation catch-up logic.

- [ ] **Step 2: Add party-aware position grouping**

In the position snap section, after individual NPC positions are calculated:

```csharp
// Party-aware position snap: group party members near their leader
foreach (PartyData party in PartyRegistry.GetAllParties())
{
    // Find the leader's position in the hibernated data
    HibernatedNPCData leaderData = hibernatedNPCs.Find(n => n.CharacterId == party.LeaderId);
    if (leaderData == null) continue;

    Vector3 leaderPos = leaderData.Position;

    // Snap all party members near the leader
    foreach (string memberId in party.MemberIds)
    {
        if (memberId == party.LeaderId) continue;
        HibernatedNPCData memberData = hibernatedNPCs.Find(n => n.CharacterId == memberId);
        if (memberData == null) continue;

        // Place within 2m of leader
        Vector3 offset = new Vector3(
            UnityEngine.Random.Range(-2f, 2f), 0f, UnityEngine.Random.Range(-2f, 2f));
        memberData.Position = leaderPos + offset;
    }
}
```

- [ ] **Step 3: Verify compilation**

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/World/Simulation/
git commit -m "feat(party): add party-aware position snap in MacroSimulator"
```

---

## Task 15: MapTransitionZone — Doorless Region Borders

**Files:**
- Create: `Assets/Scripts/World/MapSystem/MapTransitionZone.cs`

- [ ] **Step 1: Create `MapTransitionZone.cs`**

```csharp
using UnityEngine;

/// <summary>
/// Trigger collider at the edge of a Region MapController (no door).
/// Handles party gathering for leaders and solo transitions for non-party characters.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class MapTransitionZone : MonoBehaviour
{
    [SerializeField] private string _targetMapId;
    [SerializeField] private Vector3 _targetPosition;
    [SerializeField] private Transform _targetSpawnPoint;

    public string TargetMapId => _targetMapId;
    public Vector3 TargetPosition => _targetSpawnPoint != null ? _targetSpawnPoint.position : _targetPosition;

    private void OnTriggerEnter(Collider other)
    {
        if (!other.TryGetComponent(out Character character)) return;
        if (!character.IsAlive()) return;

        // Only server processes logic
        if (character.NetworkObject != null && !character.NetworkObject.IsServer) return;

        CharacterParty party = character.CharacterParty;

        if (party != null && party.IsInParty)
        {
            if (party.IsPartyLeader)
            {
                // Leader → start gathering
                MapController targetMap = MapController.GetByMapId(_targetMapId);
                if (targetMap != null && (targetMap.Type == MapType.Region || targetMap.Type == MapType.Dungeon))
                {
                    party.StartGathering(_targetMapId, TargetPosition);
                    return;
                }
            }
            else
            {
                // Non-leader party member → toast notification to leader
                // "[Name] is approaching the border"
                Debug.Log($"<color=yellow>[MapTransitionZone]</color> Party member {character.CharacterName} approaching border");
                return;
            }
        }

        // Solo character → normal transition
        var transitionAction = new CharacterMapTransitionAction(
            character, null, _targetMapId, TargetPosition, 0.5f);
        character.CharacterActions.ExecuteAction(transitionAction);
    }
}
```

- [ ] **Step 2: Verify compilation**

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/World/MapSystem/MapTransitionZone.cs
git commit -m "feat(party): add MapTransitionZone for doorless region borders"
```

---

## Task 16: UI_PartyPanel — Basic HUD

**Files:**
- Create: `Assets/Scripts/UI/UI_PartyPanel.cs`

**Read first:** Existing UI scripts in `Assets/Scripts/UI/` to match the project's UI conventions (Canvas structure, event binding pattern).

- [ ] **Step 1: Create `UI_PartyPanel.cs`**

Minimal functional panel. Full visual design is separate work — this provides the logic layer.

```csharp
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Party HUD panel. Shows party members, leader controls, and gathering status.
/// </summary>
public class UI_PartyPanel : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameObject _panelRoot;
    [SerializeField] private GameObject _createPartySection;
    [SerializeField] private GameObject _partyViewSection;
    [SerializeField] private TMP_InputField _partyNameInput;
    [SerializeField] private Button _createPartyButton;
    [SerializeField] private Button _disbandButton;
    [SerializeField] private Button _leaveButton;
    [SerializeField] private Transform _memberListContainer;
    [SerializeField] private TMP_Text _partyNameText;
    [SerializeField] private TMP_Text _followModeText;

    private Character _localCharacter;
    private CharacterParty _localParty;

    private void Start()
    {
        if (_createPartyButton != null)
            _createPartyButton.onClick.AddListener(OnCreatePartyClicked);
        if (_disbandButton != null)
            _disbandButton.onClick.AddListener(OnDisbandClicked);
        if (_leaveButton != null)
            _leaveButton.onClick.AddListener(OnLeaveClicked);

        HideAll();
    }

    public void Bind(Character localCharacter)
    {
        Unbind();

        _localCharacter = localCharacter;
        _localParty = localCharacter?.CharacterParty;

        if (_localParty == null) return;

        _localParty.OnJoinedParty += HandleJoinedParty;
        _localParty.OnLeftParty += HandleLeftParty;
        _localParty.OnPartyStateChanged += HandleStateChanged;
        _localParty.OnFollowModeChanged += HandleFollowModeChanged;
        _localParty.OnMemberKicked += HandleMemberKicked;

        RefreshUI();
    }

    public void Unbind()
    {
        if (_localParty != null)
        {
            _localParty.OnJoinedParty -= HandleJoinedParty;
            _localParty.OnLeftParty -= HandleLeftParty;
            _localParty.OnPartyStateChanged -= HandleStateChanged;
            _localParty.OnFollowModeChanged -= HandleFollowModeChanged;
            _localParty.OnMemberKicked -= HandleMemberKicked;
        }

        _localCharacter = null;
        _localParty = null;
    }

    private void RefreshUI()
    {
        if (_localParty == null || !_localParty.IsInParty)
        {
            ShowCreateSection();
            return;
        }

        ShowPartyView();
    }

    private void ShowCreateSection()
    {
        if (_createPartySection != null) _createPartySection.SetActive(true);
        if (_partyViewSection != null) _partyViewSection.SetActive(false);

        // Only show create button if player has Leadership skill
        bool hasLeadership = _localParty != null
            && _localCharacter != null
            && _localCharacter.CharacterSkills != null;
        if (_createPartyButton != null)
            _createPartyButton.interactable = hasLeadership;
    }

    private void ShowPartyView()
    {
        if (_createPartySection != null) _createPartySection.SetActive(false);
        if (_partyViewSection != null) _partyViewSection.SetActive(true);

        PartyData data = _localParty?.PartyData;
        if (data == null) return;

        if (_partyNameText != null)
            _partyNameText.text = data.PartyName;

        if (_followModeText != null)
            _followModeText.text = _localParty.CurrentFollowMode.ToString();

        bool isLeader = _localParty.IsPartyLeader;
        if (_disbandButton != null)
            _disbandButton.gameObject.SetActive(isLeader);
        if (_leaveButton != null)
            _leaveButton.gameObject.SetActive(!isLeader);
    }

    private void HideAll()
    {
        if (_panelRoot != null) _panelRoot.SetActive(false);
    }

    // --- Button Handlers ---

    private void OnCreatePartyClicked()
    {
        if (_localParty == null) return;
        string name = _partyNameInput != null ? _partyNameInput.text : null;
        _localParty.CreateParty(string.IsNullOrWhiteSpace(name) ? null : name);
    }

    private void OnDisbandClicked()
    {
        _localParty?.DisbandParty();
    }

    private void OnLeaveClicked()
    {
        _localParty?.LeaveParty();
    }

    // --- Event Handlers ---

    private void HandleJoinedParty(PartyData data) => RefreshUI();
    private void HandleLeftParty() => RefreshUI();
    private void HandleStateChanged(PartyState state) => RefreshUI();
    private void HandleFollowModeChanged(PartyFollowMode mode) => RefreshUI();
    private void HandleMemberKicked(string characterId) => RefreshUI();

    private void OnDestroy()
    {
        Unbind();
    }
}
```

- [ ] **Step 2: Verify compilation**

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/UI/UI_PartyPanel.cs
git commit -m "feat(party): add UI_PartyPanel HUD logic layer"
```

---

## Task 17: Physics Layer Setup

The "PartyGather" layer must be configured in Unity's physics settings so it doesn't collide with Character or MapTrigger layers.

- [ ] **Step 1: Add "PartyGather" layer**

Use MCP `script-execute` to add the layer:
```csharp
public class AddPartyGatherLayer
{
    public static void Execute()
    {
        var tagManager = new UnityEditor.SerializedObject(
            UnityEditor.AssetDatabase.LoadMainAssetAtPath("ProjectSettings/TagManager.asset"));
        var layers = tagManager.FindProperty("layers");

        // Find first empty user layer (8-31)
        for (int i = 8; i < 32; i++)
        {
            var layer = layers.GetArrayElementAtIndex(i);
            if (string.IsNullOrEmpty(layer.stringValue))
            {
                layer.stringValue = "PartyGather";
                tagManager.ApplyModifiedProperties();
                Debug.Log($"Added 'PartyGather' layer at index {i}");
                return;
            }
        }
        Debug.LogError("No empty layer slots available!");
    }
}
```

- [ ] **Step 2: Configure physics layer collision matrix**

Disable collisions between "PartyGather" and all other layers except "Character":

```csharp
public class ConfigurePartyGatherPhysics
{
    public static void Execute()
    {
        int partyGatherLayer = LayerMask.NameToLayer("PartyGather");
        if (partyGatherLayer < 0)
        {
            Debug.LogError("PartyGather layer not found!");
            return;
        }

        // Disable ALL collisions for PartyGather
        for (int i = 0; i < 32; i++)
        {
            Physics.IgnoreLayerCollision(partyGatherLayer, i, true);
        }

        // Re-enable ONLY with the layer that Characters use
        int characterLayer = LayerMask.NameToLayer("Default"); // Or whatever layer Characters use
        Physics.IgnoreLayerCollision(partyGatherLayer, characterLayer, false);

        Debug.Log($"PartyGather layer ({partyGatherLayer}) configured: only collides with layer {characterLayer}");
    }
}
```

- [ ] **Step 3: Verify the layer exists and physics matrix is correct**

Use `console-get-logs` to check for success messages.

- [ ] **Step 4: Commit**

```bash
git add ProjectSettings/TagManager.asset ProjectSettings/DynamicsManager.asset
git commit -m "feat(party): add PartyGather physics layer and configure collision matrix"
```

---

## Task 18: Integration Verification

Manual end-to-end verification in the Unity Editor.

- [ ] **Step 1: Open Unity and verify no compilation errors**

Use `console-clear-logs` then `console-get-logs`.

- [ ] **Step 2: Verify Character prefab has CharacterParty**

Open Character_Default prefab → check CharacterParty child GO exists with component and Leadership SkillSO assigned.

- [ ] **Step 3: Test party creation**

In Play Mode:
1. Select the player character
2. In the Inspector, call `CharacterParty.CreateParty("Test Party")`
3. Verify: PartyRegistry has the party, NetworkVariable `_networkPartyId` is set

- [ ] **Step 4: Test party join/leave**

1. Find an NPC character
2. Call `npc.CharacterParty.JoinParty(playerParty.PartyId)`
3. Verify: NPC is in the party, follow blackboard flag is set
4. Call `npc.CharacterParty.LeaveParty()`
5. Verify: NPC is no longer in the party, blackboard flag cleared

- [ ] **Step 5: Test BT follow behavior**

1. Create a party with an NPC member
2. Walk the player leader around
3. Verify: NPC follows the leader (BT party follow node picks up the blackboard flag)

- [ ] **Step 6: Test gathering at MapTransitionDoor**

1. With a party active, have the leader interact with a door to a Region map
2. Verify: leader stops, gather zone appears, NPC members pathfind to gather zone
3. Verify: once all members are in the zone, transition can proceed

- [ ] **Step 7: Commit final integration verification notes**

```bash
git commit --allow-empty -m "chore(party): integration verification complete"
```

---

## Dependency Graph

```
Task 1 (Enums) ──────────────┐
Task 2 (PartyData) ──────────┤
Task 3 (PartyRegistry) ──────┼─→ Task 6 (CharacterParty Core) ─→ Task 8 (Follow) ─→ Task 9 (Gathering) ─→ Task 10 (Door Intercept)
Task 4 (MapType) ────────────┘                                                                              ↓
Task 5 (BT Nodes) ───────────────────────────────────────────────────→ Task 8 (Follow)                  Task 15 (MapTransitionZone)
Task 7 (GatherZone) ─────────────────────────────────────────────────→ Task 9 (Gathering)
Task 11 (PartyInvitation) ← depends on Task 6
Task 12 (Leadership Asset) → Task 13 (Prefab Setup) ← depends on Task 6
Task 14 (HibernatedNPCData) ← standalone
Task 14b (ICharacterData) ← depends on Task 6
Task 14c (SaveManager) ← depends on Tasks 2, 3, 14b
Task 14d (MacroSimulator) ← depends on Tasks 3, 14
Task 16 (UI_PartyPanel) ← depends on Task 6
Task 17 (Physics Layer) ← depends on Task 7
Task 18 (Verification) ← depends on ALL
```

**Parallelizable groups:**
- Tasks 1-5 can all run in parallel (no interdependencies)
- Tasks 7, 11, 12, 14, 16 can run in parallel after Task 6
- Task 8 requires Tasks 5 + 6
- Task 9 requires Tasks 7 + 8
- Task 10 requires Tasks 4 + 9
- Task 15 requires Tasks 4 + 9
- Task 13 requires Tasks 6 + 12
- Tasks 14b, 14c, 14d run sequentially after Task 6 + 14
- Task 17 requires Task 7
- Task 18 requires all others
