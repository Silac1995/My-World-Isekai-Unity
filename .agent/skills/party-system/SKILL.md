---
name: party-system
description: Small-scale character groups with leader-based follow, gathering-based map transitions, and persistent membership. Distinct from the community/clan system.
---

# Party System

The Party System manages small-scale character groups (2-8 members) where members stay together and follow a leader. It is distinct from the Community System — a community is a large social/territorial group (village, guild); a party is a tight traveling unit.

Both players and NPCs share the same `Character.cs` class. The party system is fully unified — player-led and NPC-led parties use identical code paths. Invitations go through `CharacterInvitation` via the `InteractionInvitation` pipeline.

## When to use this skill
- When creating, modifying, or debugging party/group behavior
- When adding new party-related features (buffs, shared loot, formations)
- When modifying map transitions, gathering logic, or follow behavior
- When touching `CharacterParty`, `PartyData`, `PartyRegistry`, or BT follow nodes
- When implementing NPC AI that involves group coordination
- When modifying hibernation/persistence and parties must survive

## Architecture: Hybrid Component + Registry

Three classes with clear separation of concerns:

| Class | Type | Responsibility |
|-------|------|----------------|
| `PartyData` | Plain C# class | What the party **is** (data) |
| `PartyRegistry` | Static class | Where to **find** parties (lookup) |
| `CharacterParty` | `CharacterSystem` MonoBehaviour | What a character **does** in a party (behavior) |

**Rule:** All party operations are **server-authoritative**. Clients learn about party state through `NetworkVariable`s and `[Rpc(SendTo.NotServer)]` ClientRpcs.

**Rule:** Party data uses **CharacterId UUIDs** (strings), never direct `Character` references. This ensures data survives hibernation, disconnects, and map transitions.

**Rule:** `CharacterParty` is attached to **every** character, even when not in a party. It mirrors the pattern of `CharacterCombat`, `CharacterJob`, etc.

---

### 1. Data Layer

#### PartyData (`Assets/Scripts/Character/CharacterParty/PartyData.cs`)
Plain C# `[Serializable]` class. Stores party identity and membership.

- `PartyId` : string (GUID)
- `PartyName` : string (defaults to `"{LeaderName}'s Party"`)
- `LeaderId` : string (CharacterId UUID)
- `MemberIds` : List\<string\> (CharacterId UUIDs)
- `FollowMode` : `PartyFollowMode` enum (`Strict` | `Loose`)
- `State` : `PartyState` enum (`Active` | `LeaderlessHold` | `Gathering`) — **transient, `[NonSerialized]`**, resets to `Active` on load

Key methods: `AddMember()`, `RemoveMember()` (auto-promotes `MemberIds[0]` to leader if leader is removed), `IsLeader()`, `IsMember()`, `IsFull()`.

#### PartyRegistry (`Assets/Scripts/Character/CharacterParty/PartyRegistry.cs`)
Static class with dual dictionaries for O(1) lookups:
- `_parties`: PartyId -> PartyData
- `_characterToParty`: CharacterId -> PartyId (reverse lookup)

Key methods: `Register()`, `Unregister()`, `GetParty()`, `GetPartyForCharacter()`, `GetAllParties()`, `MapCharacterToParty()`, `UnmapCharacter()`, `Clear()`.

**Rule:** Call `MapCharacterToParty()` / `UnmapCharacter()` whenever modifying `PartyData.MemberIds` to keep the reverse lookup in sync.

#### Enums
- `PartyFollowMode` (`Assets/Scripts/Character/CharacterParty/PartyFollowMode.cs`): `Strict` (follow leader), `Loose` (wander in community territory)
- `PartyState` (`Assets/Scripts/Character/CharacterParty/PartyState.cs`): `Active`, `LeaderlessHold`, `Gathering`

---

### 2. CharacterParty Component (`Assets/Scripts/Character/CharacterParty/CharacterParty.cs`)

`CharacterParty : CharacterSystem` — the main MonoBehaviour on each character.

#### Network Synchronization
Three `NetworkVariable`s sync party state to clients:
- `_networkPartyId` (`FixedString64Bytes`) — which party this character is in
- `_networkPartyState` (`byte`) — current `PartyState`
- `_networkFollowMode` (`byte`) — current `PartyFollowMode`

Client-facing events are fired both server-side (directly) and client-side (via ClientRpc handlers):
```
OnJoinedParty(PartyData)
OnLeftParty()
OnFollowModeChanged(PartyFollowMode)
OnPartyStateChanged(PartyState)
OnGatheringStarted()
OnGatheringComplete()
OnMemberKicked(string characterId)
```

#### Party Lifecycle (all server-only)

| Method | Description |
|--------|-------------|
| `CreateParty(name)` | Requires Leadership skill. Generates PartyData, registers in PartyRegistry. |
| `JoinParty(partyId)` | Auto-leaves current party if in one. Checks capacity. |
| `JoinCharacterParty(Character)` | Convenience: joins the party of a given character. |
| `LeaveParty()` | Removes self. Auto-promotes next member if leader leaves. Disbands if empty. |
| `KickMember(characterId)` | Leader-only. Works even if target is offline (UUID-based). |
| `PromoteLeader(characterId)` | Leader-only. Grants Leadership skill if new leader lacks it. |
| `SetFollowMode(mode)` | Leader-only. Toggles Strict/Loose. |
| `DisbandParty()` | Leader-only. Notifies all members, unregisters from PartyRegistry. |

#### Party Size
```csharp
MaxPartySize = Mathf.Min(2 + leadershipLevel, 8)
```
Base: 2 (leader + 1). Per Leadership skill level: +1. Hard cap: 8.

#### Leader Event Subscriptions
Each member subscribes to the **leader's** `OnDeath`, `OnIncapacitated`, `OnWakeUp` events (not their own — the base `CharacterSystem` handles self-events).

- **Leader dies** -> auto-promote `MemberIds[0]`, grant Leadership skill at level 1. Guard against duplicate processing via `IsMember(leader.CharacterId)` check.
- **Leader unconscious** -> `PartyState.LeaderlessHold` (members stop following, act independently)
- **Leader wakes up** -> `PartyState.Active` (follow resumes)

**Rule:** Always `UnsubscribeFromLeader()` before subscribing to a new one. Always unsubscribe in `OnDisable()` / `OnNetworkDespawn()`.

#### Reconnect Flow
On `OnNetworkSpawn()` (server), `TryReconnectToParty()` checks `PartyRegistry.GetPartyForCharacter()`. If found, re-links to the existing party. If not found (kicked while offline), clears the saved PartyId.

---

### 3. Follow Logic

Follow behavior is driven through the **Behaviour Tree**, not coroutines or CharacterActions.

**How it works:**
1. `CharacterParty.UpdateFollowState()` sets or removes `Blackboard.KEY_PARTY_FOLLOW` on the NPC's blackboard
2. `BTCond_IsInPartyFollow` checks for the key; if present, delegates to `BTAction_FollowPartyLeader`
3. `BTAction_FollowPartyLeader` calls `CharacterMovement.SetDestination()` directly (NOT a `CharacterAction` — following does NOT affect `IsFree()`)

**BT priority position** (inserted after Aggression, before PunchOut):
```
0. Legacy/Imperative
1. Orders
2. Combat
3. Entraide
4. Aggression
5. Party Follow  <-- HERE
6. PunchOut
7. Schedule
8. GOAP
9. Social
10. Wander
```

Combat/aggression override party follow. Party follow overrides schedule/wander/social.

**Follow modes:**
- `Strict`: NPC members pathfind to leader (default, works everywhere)
- `Loose`: NPC members act independently (only on community-owned Region maps). Auto-reverts to Strict when transitioning out.

**Player members are never auto-moved.** They follow on their own and receive toast notifications.

**Rule:** `UpdateFollowState()` must be called after any state change that affects following: joining, leaving, follow mode change, leader wake-up, party state change.

**Files:**
- `Assets/Scripts/AI/Core/Blackboard.cs` — `KEY_PARTY_FOLLOW` constant
- `Assets/Scripts/AI/Actions/BTAction_FollowPartyLeader.cs` — pathfinds to leader, `FOLLOW_DISTANCE = 3f`
- `Assets/Scripts/AI/Conditions/BTCond_IsInPartyFollow.cs` — condition check + action delegation
- `Assets/Scripts/AI/NPCBehaviourTree.cs` — `_partyFollowNode` inserted at index 5

---

### 4. Gathering Logic

When a party leader tries to transition between Region or Dungeon maps, the transition is intercepted and replaced by a gathering phase.

**Flow:**
1. Leader interacts with `MapTransitionDoor` or enters `MapTransitionZone`
2. `MapTransitionDoor.Interact()` / `MapTransitionZone.OnTriggerEnter()` detects: party leader + Region/Dungeon target
3. Calls `CharacterParty.StartGathering(targetMapId, targetPosition)` **instead of** creating `CharacterMapTransitionAction` (no race condition)
4. Leader is immobilized, a `BoxCollider` gather zone spawns on a child GO
5. NPC members pathfind to the gather zone. Player members get a toast: "Your party leader is waiting for you"
6. Members entering the collider are tracked in `_gatheredMemberIds`
7. **NPC leader**: 30s real-time timeout → auto-proceed
8. **Player leader**: auto-proceed when all free members gathered; UI prompt if some are busy
9. `ProceedTransition()` executes `CharacterMapTransitionAction` on each gathered member
10. Members outside the gather zone are left behind

**Gathering does NOT trigger for:**
- Interior maps (building interiors) — members don't follow inside
- Arena maps
- Solo characters
- Non-leader party members (they transition freely)

**Gather zone physics:**
- Child `GameObject` named "GatherZone" with `BoxCollider` (trigger, 6x4x6)
- Physics layer "PartyGather" (index 11) — only collides with Default layer (characters)
- `PartyGatherZone.cs` forwards `OnTriggerEnter`/`OnTriggerExit` to `CharacterParty`

**Rule:** Use `WaitForSecondsRealtime` for gathering timeouts — gathering is a real-time operation, not affected by `GameSpeedController` (CLAUDE.md rule #24).

**Files:**
- `Assets/Scripts/Character/CharacterParty/PartyGatherZone.cs` — trigger forwarder on child GO
- `Assets/Scripts/World/MapSystem/MapTransitionDoor.cs` — party leader check in `Interact()`
- `Assets/Scripts/World/MapSystem/MapTransitionZone.cs` — doorless region border trigger

---

### 5. MapType Enum (`Assets/Scripts/World/MapSystem/MapType.cs`)

```csharp
public enum MapType : byte { Region, Interior, Dungeon, Arena }
```

Added to `MapController` as `[SerializeField] private MapType _mapType` with `public MapType Type` property. Replaces the old `IsInteriorOffset` bool (kept as deprecated backward-compat property).

**Rule:** Use `mapController.Type == MapType.Interior` instead of the deprecated `IsInteriorOffset`.

---

### 6. Party Invitation (`Assets/Scripts/Character/CharacterParty/PartyInvitation.cs`)

`PartyInvitation : InteractionInvitation` — plugs into the existing `CharacterInvitation` pipeline.

**Preconditions (CanExecute):**
- Source has Leadership skill
- Target is alive and free
- Target is not in ANY party
- Party is not full (based on leader's Leadership level)

**On accepted:** auto-creates party if source doesn't have one, then `target.CharacterParty.JoinParty()`.

**On refused:** nothing (no relation impact).

**Rule:** Party invitations must always go through `CharacterInvitation`. Never call `JoinParty()` directly from gameplay code without going through the invitation flow (except for server-side testing/admin).

---

### 7. Leadership Skill

`SkillSO` asset at `Assets/Data/Skills/Leadership.asset`. Uses the existing skill system — no framework changes.

- **Creating** a party requires the Leadership skill
- **Inheriting** leadership (auto-promote on leader death) grants Leadership at level 1 via `CharacterSkills.AddSkill()`
- Party size scales with Leadership level: `MaxPartySize = Mathf.Min(2 + level, 8)`

Referenced via `[SerializeField] SkillSO _leadershipSkill` on `CharacterParty`.

---

### 8. Persistence & Hibernation

- `HibernatedNPCData.PartyId` — preserved during map hibernation, used for reconnection on wake-up
- `PartyRegistry` is **runtime-only** (static dictionaries). Parties must be loaded from world save into the registry on server boot, and saved on server shutdown.
- `PartyData.State` is **transient** (`[NonSerialized]`) — resets to `Active` on load
- `TryReconnectToParty()` on `OnNetworkSpawn()` checks `PartyRegistry` for existing party data

**Not yet implemented:**
- `ICharacterData.PartyId` for player character save files
- `SaveManager` party persistence (save/load `PartyData` to world save)
- `MacroSimulator` party-aware position snap during hibernation catch-up

---

### 9. UI (`Assets/Scripts/UI/UI_PartyPanel.cs`)

Logic-layer panel that binds to `CharacterParty` events. Provides:
- "Create Party" button (requires Leadership skill)
- Party view with member list, follow mode, party name
- Leader controls: kick, promote, disband, follow mode toggle
- Leave button for non-leaders

Uses `Bind(Character)` / `Unbind()` pattern. All event subscriptions cleaned up in `OnDestroy()`.

---

## File Reference

| File | Location | Type |
|------|----------|------|
| `PartyData.cs` | `Assets/Scripts/Character/CharacterParty/` | Plain C# data |
| `PartyRegistry.cs` | `Assets/Scripts/Character/CharacterParty/` | Static registry |
| `PartyFollowMode.cs` | `Assets/Scripts/Character/CharacterParty/` | Enum |
| `PartyState.cs` | `Assets/Scripts/Character/CharacterParty/` | Enum |
| `CharacterParty.cs` | `Assets/Scripts/Character/CharacterParty/` | CharacterSystem component |
| `PartyGatherZone.cs` | `Assets/Scripts/Character/CharacterParty/` | Trigger forwarder |
| `PartyInvitation.cs` | `Assets/Scripts/Character/CharacterParty/` | InteractionInvitation subclass |
| `MapType.cs` | `Assets/Scripts/World/MapSystem/` | Enum |
| `MapTransitionZone.cs` | `Assets/Scripts/World/MapSystem/` | Doorless border trigger |
| `BTCond_IsInPartyFollow.cs` | `Assets/Scripts/AI/Conditions/` | BT condition node |
| `BTAction_FollowPartyLeader.cs` | `Assets/Scripts/AI/Actions/` | BT follow action |
| `UI_PartyPanel.cs` | `Assets/Scripts/UI/` | HUD panel logic |
| `Leadership.asset` | `Assets/Data/Skills/` | SkillSO asset |

### Modified Existing Files

| File | Change |
|------|--------|
| `Character.cs` | `CharacterParty` subsystem reference + `IsInParty()`/`IsPartyLeader()` convenience |
| `MapController.cs` | `MapType _mapType` field, deprecated `IsInteriorOffset` |
| `MapTransitionDoor.cs` | Party leader gathering interception in `Interact()` |
| `NPCBehaviourTree.cs` | `BTCond_IsInPartyFollow` at index 5, debug tracking |
| `Blackboard.cs` | `KEY_PARTY_FOLLOW` constant |
| `MapSaveData.cs` | `HibernatedNPCData.PartyId` field |
| `BuildingInteriorSpawner.cs` | Migrated from `IsInteriorOffset = true` to `SetMapType(MapType.Interior)` |

---

## Design Spec
Full design document: `docs/superpowers/specs/2026-03-27-character-party-system-design.md`

## Implementation Plan
Full task breakdown: `docs/superpowers/plans/2026-03-28-character-party-system.md`
