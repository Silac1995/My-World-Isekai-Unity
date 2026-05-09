# Character Party/Group System — Design Spec

**Date**: 2026-03-27
**Status**: Draft
**Scope**: Small-scale character groups with follow behavior, gathering-based map transitions, and persistent membership

---

## Overview

A party/group system for small-scale character groups where members stay together and follow the group leader. Distinct from the existing community/clan system — a party is 2-8 characters traveling and acting as a unit.

Both players and NPCs share the same `Character.cs` class. Everything an NPC can do, a player can do. The party system is fully unified — player-led and NPC-led parties use identical code paths.

---

## Architecture: Hybrid Component + Registry

Three classes with clear separation of concerns:

| Class | Type | Responsibility |
|-------|------|----------------|
| `PartyData` | Plain C# class | What the party **is** (data) |
| `PartyRegistry` | Static class | Where to **find** parties (lookup) |
| `CharacterParty` | `CharacterSystem` MonoBehaviour | What a character **does** in a party (behavior) |

All files live in `Assets/Scripts/Character/CharacterParty/`.

> **Naming note:** The component is named `CharacterParty` (not `CharacterPartyController`) to match the existing convention: `CharacterCombat`, `CharacterJob`, `CharacterInvitation`, etc. The old `CharacterParty.cs` data class is replaced by `PartyData.cs`.

---

## 1. Data Layer

### PartyData

Replaces the existing `CharacterParty.cs`. Plain C# class, no MonoBehaviour.

**Fields:**
- `PartyId` : string — GUID, generated on creation
- `PartyName` : string — defaults to `"{LeaderName}'s Party"` if no name provided
- `LeaderId` : string — CharacterId UUID (not a direct reference, survives hibernation)
- `MemberIds` : List\<string\> — CharacterId UUIDs
- `FollowMode` : PartyFollowMode enum — `{ Strict, Loose }`
- `State` : PartyState enum — `{ Active, LeaderlessHold, Gathering }` — **transient, not persisted.** Resets to `Active` on load. Gathering state is inherently transient (a mid-gather server restart simply cancels the gather).

Uses CharacterId UUIDs instead of direct `Character` references so party data survives hibernation, disconnects, and map transitions without dangling references. Live `Character` objects are resolved on demand via `Character.FindByUUID()`.

**Important:** All `Character.FindByUUID()` calls must null-check. For members on hibernated maps, the UUID exists in `PartyData.MemberIds` but resolves to null. Gathering only considers members where `FindByUUID() != null`. Toast notifications to players on hibernated maps are queued for reconnect.

### PartyFollowMode Enum

`Assets/Scripts/Character/CharacterParty/PartyFollowMode.cs`

- `Strict` — NPC members pathfind to the leader at all times when free. Default.
- `Loose` — NPC members act independently (schedule, GOAP, wander within community territory). Only available on `MapType.Region` maps owned by a community. **When the party transitions out of a community-owned Region, FollowMode automatically reverts to Strict.**

### PartyState Enum

`Assets/Scripts/Character/CharacterParty/PartyState.cs`

- `Active` — normal operation, follow behavior active
- `LeaderlessHold` — leader is unconscious. Members stop following, act independently. Resumes on wake-up.
- `Gathering` — leader is at a map transition point, waiting for members to converge

### PartyRegistry

Static class, no MonoBehaviour.

```
Dictionary<string, PartyData> _parties          // PartyId -> PartyData
Dictionary<string, string> _characterToParty    // CharacterId -> PartyId (reverse lookup)

GetParty(partyId) -> PartyData
GetPartyForCharacter(characterId) -> PartyData
Register(PartyData) / Unregister(partyId)
GetAllParties() -> IEnumerable<PartyData>  // for MacroSimulator enumeration
```

The reverse lookup `_characterToParty` gives O(1) for the most common query: "what party is this character in?"

**Boot loading:** On server start, `SaveManager` loads all persisted `PartyData` entries from the world save file and calls `PartyRegistry.Register()` for each. This happens during `SaveManager`'s existing initialization sequence, before any `MapController.OnNetworkSpawn` fires. A `PartyRegistry.Clear()` method resets both dictionaries on shutdown.

---

## 2. CharacterParty (Component)

`CharacterParty : CharacterSystem` — attached to **every** character, even if not in a party. Mirrors how `CharacterCombat`, `CharacterJob`, etc. work. Dormant until a party forms.

### State

- `_partyData` : PartyData — null if not in a party
- `_followCoroutine` : Coroutine — active follow loop (NPC-only)
- `_gatherCoroutine` : Coroutine — gathering loop
- `_isGathered` : bool — has this member reached the gather zone
- `_gatherZone` : BoxCollider — on a **child GameObject** (e.g., "GatherZone") with a dedicated layer/tag so it is ignored by `MapController.OnTriggerEnter` and other physics systems. Trigger collider, disabled by default. Enabled during gathering.
- `_gatheredMemberIds` : HashSet\<string\> — members currently inside the gather zone
- `[SerializeField] SkillSO _leadershipSkill` — reference to the Leadership SkillSO asset, assigned in the Character prefab. Used by `CreateParty()`, `CanExecute()`, and auto-promote logic.

### Network Synchronization

Party operations execute on the **server**. Clients need party state for UI (HUD panel, toast notifications, gathering progress). Synchronization uses:

- `NetworkVariable<FixedString64Bytes> _networkPartyId` — synced to all clients. The client's `UI_PartyPanel` reads this to know if the local player is in a party and which one.
- **ClientRpc** notifications for discrete events: `NotifyJoinedPartyClientRpc`, `NotifyLeftPartyClientRpc`, `NotifyGatheringStartedClientRpc`, `NotifyMemberKickedClientRpc`, etc. These fire the local C# events that the UI consumes.
- `NetworkVariable<byte> _networkPartyState` — synced to all clients (cast from `PartyState` enum). Drives gathering UI on member clients.
- `NetworkVariable<byte> _networkFollowMode` — synced to all clients. Drives follow mode toggle display.

The local C# events (`OnJoinedParty`, `OnLeftParty`, etc.) are raised **on both server and client** — on the server directly, on clients via the ClientRpc handlers.

### Follow Logic (server-only, NPC members only)

- When `FollowMode == Strict` and member is free (`IsFree()`): sets blackboard flag `IsInPartyFollow = true`. The BT node `BTCond_IsInPartyFollow` picks it up and pathfinds to the leader.
- When `FollowMode == Loose` on a community-owned region: clears the flag. Member runs normal BT (schedule, GOAP, wander within territory).
- **Player members are never auto-moved.** They follow on their own. They receive toast notifications if the leader starts gathering.
- **Follow uses `CharacterMovement` directly** (pathfind via `SetDestination`), NOT a `CharacterAction`. This means following does **not** affect `IsFree()` — no oscillation risk. The BT action node calls `CharacterMovement.SetDestination()` and `CharacterMovement.Stop()` directly, same pattern as `CharacterInvitation.FollowTargetRoutine`.

### BT Integration

New BT node: `BTCond_IsInPartyFollow` with child action `BTAction_FollowPartyLeader`.

**Exact insertion point** in `NPCBehaviourTree.BuildTree()`: insert after `_agressionSequence` (index 4) and before `_punchOutNode` (index 5) in the `BTSelector` children list. All subsequent nodes shift down one index.

```
0.  Legacy/Imperative
1.  Orders
2.  Combat
3.  Entraide (friend in danger)
4.  Aggression (enemy detected)
5.  Party Follow  ← NEW (inserted here)
6.  PunchOut (was index 5)
7.  Schedule (was index 6)
8.  GOAP (was index 7)
9.  Social (was index 8)
10. Wander (was index 9)
```

Combat/aggression override party follow (members can fight). Party follow overrides schedule/wander/social (members don't wander off). The controller sets/clears the blackboard flag; the BT node reads it.

### Gathering Logic (server-only)

Triggered when the leader interacts with a `MapTransitionDoor` or enters a `MapTransitionZone` and the target map is `MapType.Region` or `MapType.Dungeon`.

**Flow:**

1. Leader interacts with a `MapTransitionDoor` or enters a `MapTransitionZone`
2. `MapTransitionDoor.Interact()` checks: is interactor a party leader AND target map is Region/Dungeon?
3. **Yes** → calls `CharacterParty.StartGathering(targetMapId, targetPosition)` **instead of** creating the `CharacterMapTransitionAction`. The transition action is never started — no race condition.
4. **No** (solo, interior, arena) → current behavior unchanged, `CharacterMapTransitionAction` executes normally
5. `StartGathering(targetMapId, targetPosition)`:
   - Stop leader's movement (immobilize)
   - Enable `_gatherZone` BoxCollider at leader's position
   - Set `PartyData.State = Gathering`
   - Notify all members: BT flag for NPCs, toast for player members ("Your party leader is waiting for you")
6. NPC members pathfind to the gather collider
7. `OnTriggerEnter` on the gather zone child object: member enters → add to `_gatheredMemberIds`
8. `OnTriggerExit` on the gather zone child object: member leaves → remove from `_gatheredMemberIds`
9. **Player leader**: once all free members are gathered, auto-proceed. If any are busy → prompt: "[Name] is still in combat. Leave without them? [Yes / Wait]"
10. **NPC leader**: configurable timeout (e.g., 30s) → proceed with whoever is gathered

**On proceed (`ProceedTransition`):**

1. For each member in `_gatheredMemberIds`: execute `CharacterMapTransitionAction` on them (full transition pipeline — fade, warp, map tracker update, future travel logic)
2. Leader transitions via `CharacterMapTransitionAction`
3. Members **outside** the gather collider: nothing. They stay. If you're not in the box, you're not coming.
4. Busy members: left behind, stay on current map
5. Disable `_gatherZone`
6. Set `PartyData.State = Active`

### Gather Collider

- `BoxCollider _gatherZone` on a **child GameObject** of the character (e.g., "GatherZone"), set to a dedicated physics layer (e.g., "PartyGather") that does **not** interact with the "Character" or "MapTrigger" layers. This prevents `MapController.OnTriggerEnter` from double-counting or registering phantom entries.
- `isTrigger = true`, disabled by default
- Enabled when `State = Gathering`, disabled when gathering ends
- Size configurable (e.g., 3x3 around the leader)
- The child GameObject has a small script (`PartyGatherZone`) that forwards `OnTriggerEnter`/`OnTriggerExit` events to the parent `CharacterParty` component.

### Leader Enters Interior

When the leader enters a `MapType.Interior` map (e.g., walks into a shop):
- Party stays `Active` — no disband, no gathering
- Follow flag is cleared for members on a different map (they're now "separated")
- NPC members idle near the door the leader entered (last known leader position on the exterior map)
- When the leader exits back to the same exterior map, follow resumes automatically
- If the leader is on a different map from a member, that member's `CharacterParty` skips follow ticks (leader `FindByUUID()` returns a character on a different map — detected via `CharacterMapTracker`)

### Non-Leader Members at Transition Points

Non-leader party members can use `MapTransitionDoor` and `MapTransitionZone` **freely** without triggering gathering. Only the leader triggers the gathering flow. If a non-leader member transitions to a different map:
- Toast notification to the player leader: "[Name] has left the region"
- Member becomes "separated" — still in the party, follow resumes if they return to the leader's map

### Party Lifecycle (server-only)

- `CreateParty(name = null)` — requires Leadership skill (checked via `_leadershipSkill` SerializeField reference). Name defaults to `$"{leader.CharacterName}'s Party"`. Generates PartyData, registers in PartyRegistry.
- `JoinCharacterParty(Character leader)` — convenience method: checks if leader has a party, if so joins it.
- `JoinParty(partyId)` — called after `PartyInvitation` accepted. Adds to `PartyData.MemberIds`. **Precondition:** if the character is already in a different party, they must `LeaveParty()` first. This is enforced in `JoinParty()` — auto-leave the old party before joining the new one.
- `LeaveParty()` — removes from member list, clears blackboard flag, stops coroutines, clears local state.
- `KickMember(characterId)` — leader-only. Works even if target is offline (removes UUID from member list).
- `PromoteLeader(characterId)` — leader-only. Transfers leadership.
- Leader unconscious → `PartyState.LeaderlessHold`, clears follow flags. On wake-up → resume `PartyState.Active`.
- Leader dies → auto-promote next member. New leader gets Leadership skill at level 1 via `CharacterSkills.AddSkill(_leadershipSkill)` if they don't have it. If no members left → disband and unregister from PartyRegistry.

### Reconnect Flow (server-only)

1. Player reconnects, character loads `PartyId` from save file
2. Server checks `PartyRegistry.GetParty(partyId)` — is this character still a member?
3. **Yes** → rejoin, resume follow behavior
4. **No** (kicked while offline) → clear `PartyId` from character data, toast: "You were removed from [Party Name]"

The server is always the authority on party membership. The local `PartyId` is just a reconnection hint.

### Events

```
OnJoinedParty(PartyData)
OnLeftParty()
OnFollowModeChanged(PartyFollowMode)
OnPartyStateChanged(PartyState)
OnGatheringStarted()
OnGatheringComplete()
OnMemberKicked(string characterId)
```

These events fire on both server and client (server directly, client via ClientRpc handlers).

### Cleanup (OnDestroy)

Two subscription layers to clean up:

1. **Inherited `CharacterSystem` subscriptions** (auto-managed by base class): `_character.OnDeath`, `_character.OnIncapacitated`, `_character.OnWakeUp` — these handle self-incapacitation (e.g., this member falls unconscious).

2. **Manual leader subscriptions** (managed by `CharacterParty`): when this character joins a party, it subscribes to the **leader's** `Character.OnDeath`, `Character.OnUnconsciousChanged`, `Character.OnWakeUp` events. These must be explicitly unsubscribed in `OnDestroy` and when the leader changes (promote, leader dies, leave party).

Additional cleanup:
- Stop `_followCoroutine` and `_gatherCoroutine`
- Clear blackboard flag (`IsInPartyFollow = false`)
- If this was the last member, disband and unregister from `PartyRegistry`

---

## 3. MapType Enum + Transition Integration

### MapType Enum

New file: `Assets/Scripts/World/MapSystem/MapType.cs`

```
Region,      // Outdoor areas, cities — party gathering triggers here
Interior,    // Building interiors — no party gathering, members don't follow inside
Dungeon,     // Party gathering applies
Arena         // No party gathering
```

### Changes to MapController

- Add `[SerializeField] MapType _mapType = MapType.Region;`
- Replace `IsInteriorOffset` bool with `_mapType == MapType.Interior` check
- Public property: `MapType Type => _mapType;`

### MapTransitionZone (new)

`Assets/Scripts/World/MapSystem/MapTransitionZone.cs`

For doorless region borders. A trigger collider at the edge of a `MapController.Region`.

- `OnTriggerEnter`: detects a `Character` entering the zone
  - **Party leader** → stop movement, call `CharacterParty.StartGathering()`
  - **Non-leader party member** → toast notification to the player leader: "[Name] is approaching the border"
  - **Solo character** → normal transition behavior
- Holds `TargetMapId` and `TargetPosition` like `MapTransitionDoor`
- Calls into `CharacterParty.StartGathering()` — same as door. Gathering logic lives in the controller, not the transition point.

### Changes to MapTransitionDoor

In `MapTransitionDoor.Interact()`, before creating `CharacterMapTransitionAction`:

1. Resolve the target `MapController` via `MapController.GetByMapId(targetMapId)`
2. Check: is interactor a party leader AND `targetMap.Type` is `Region` or `Dungeon`?
3. **Yes** → call `interactor.CharacterParty.StartGathering(targetMapId, dest)` and **return** (do not create `CharacterMapTransitionAction`)
4. **No** → current behavior unchanged

---

## 4. Party Invitation

### PartyInvitation : InteractionInvitation

New file: `Assets/Scripts/Character/CharacterParty/PartyInvitation.cs`

Plugs into the existing `CharacterInvitation` pipeline with zero new infrastructure.

```
CanExecute(source, target):
  - source must have Leadership skill
  - source must be party leader (or will auto-create party)
  - target must not already be in ANY party (not just source's party)
  - target must be alive and free
  - party must not be full (Leadership skill level determines max size)

GetInvitationMessage(source, target):
  → "Want to join my group?"

OnAccepted(source, target):
  → target.CharacterParty.JoinParty(source.CurrentParty.PartyId)

OnRefused(source, target):
  → Empty. No relation impact.

EvaluateCustomInvitation(source, target):
  → return null (fall through to default sociability/relationship evaluation)
```

**How it's triggered:**
- Player → hold-interaction on a character → "Invite to Party" (only shows if player has Leadership skill)
- NPC → future scope: NPCs with Leadership can form parties via GOAP (not in base implementation)

**Flow:**
1. `InteractionInvitation.Execute(source, target)` — source says invitation
2. Source follows target during thinking delay (existing `StartFollowingTarget`)
3. Target's `CharacterInvitation` evaluates (relationship + sociability)
4. Accepted → `OnAccepted` calls `JoinParty`
5. Refused → nothing happens

---

## 5. Leadership Skill

Uses the existing `SkillSO` system — no framework changes needed.

### Leadership SkillSO Asset

- `SkillID`: "leadership"
- `SkillName`: "Leadership"
- `StatInfluences`: Sociability scaling (via `CharacterTraits.GetSociability()`)
- `LevelBonuses`: passive stat boosts at milestones (future)

Referenced at runtime via `[SerializeField] SkillSO _leadershipSkill` on `CharacterParty`, assigned in the Character prefab. No `Resources.Load` needed.

### Party Size Formula

```
MaxPartySize = Mathf.Min(2 + leadershipLevel, 8)
```

Base: 2 (leader + 1 member). Per Leadership level: +1. Hard cap: 8 regardless of level.

Example: Leadership level 5 → max 7 members. Leadership level 10 → still max 8.

### Skill Acquisition

- **Creating a party** requires the Leadership skill (checked in `CreateParty()` and `PartyInvitation.CanExecute()`)
- **Inheriting leadership** (auto-promote on leader death) grants Leadership at level 1 via `CharacterSkills.AddSkill(_leadershipSkill)` if the character doesn't have it
- Leadership XP gained naturally through leading (future: XP on successful gathering, transitions, party activities)

---

## 6. Party HUD — UI_PartyPanel

### Create Party

- "Create Party" button visible when: player has Leadership skill AND is not in a party
- Optional name input field — defaults to `"{PlayerName}'s Party"` if left empty
- Creates party immediately via `CharacterParty.CreateParty()`

### Party View

- Visible only when the local player is in a party (reads `_networkPartyId` NetworkVariable)
- Shows: party name, follow mode, member list with status indicators
- Each member entry: name, health bar (if visible), status icon (following, in combat, gathering, separated)

### Leader Controls (only shown to the leader)

- Kick button per member
- Follow mode toggle (Strict / Loose — Loose only enabled on community-owned Region maps)
- Promote button per member
- Disband button

### Gathering UI

- Progress view during gathering: who's gathered, who's en route, who's busy
- Player leader prompt: "[Name] is in combat. Leave without them? [Yes / Wait]"

### Events Consumed

```
OnJoinedParty → show panel, populate members
OnLeftParty → hide panel
OnPartyStateChanged → update gathering UI
OnFollowModeChanged → update toggle state
OnMemberKicked → remove entry
```

These events are raised on the client via ClientRpc handlers in `CharacterParty`.

---

## 7. Toast Notifications

Via existing `ToastNotificationChannel`:

| Event | Toast Message |
|-------|--------------|
| Join party | "You joined [Party Name]" |
| Member joins | "[Name] joined the party" |
| Member leaves | "[Name] left the party" |
| Kicked while offline | "You were removed from [Party Name]" |
| Member near border | "[Name] is approaching the border" |
| Leader gathering | "Your party leader is leaving the region" |
| Leader waiting | "Your party leader is waiting for you" |
| Member left region | "[Name] has left the region" |

---

## 8. Persistence & Hibernation

### Player Character Save (ICharacterData)

- Add `PartyId : string` field (GUID, null if not in a party)

### PartyData Serialization (server-side, saved with world state)

Saved fields: `PartyId`, `PartyName`, `LeaderId`, `MemberIds`, `FollowMode`

`State` is **not** persisted — it is transient and resets to `Active` on load.

**Boot sequence:** During `SaveManager` initialization (before any `MapController.OnNetworkSpawn`), all persisted `PartyData` entries are loaded and registered via `PartyRegistry.Register()`. This ensures parties are available for lookup when characters spawn or maps wake up. `PartyRegistry.Clear()` is called on server shutdown.

### HibernatedNPCData

- Add `PartyId : string` field
- On hibernation: NPC's PartyId is preserved
- On wake-up: look up PartyId in `PartyRegistry`, re-link `CharacterParty`

### Macro-Simulation Catch-Up (Party-Aware)

- During hibernation, NPC party members are treated as a **group** for position snap — members are placed near their leader's position instead of randomly scattered
- Follow mode, membership, leader status — all pure data, no Unity objects needed. Survives hibernation.

### Cross-Map Parties

- Members can be on different maps (e.g., one got left behind). `PartyData.MemberIds` is map-agnostic — just UUIDs.
- Follow logic only activates for members on the **same map** as the leader (checked via `CharacterMapTracker`)
- Separated members entering the leader's map later → follow resumes automatically

---

## File Summary

| File | Location | Type |
|------|----------|------|
| `PartyData.cs` | `Assets/Scripts/Character/CharacterParty/` | Plain C# class |
| `PartyRegistry.cs` | `Assets/Scripts/Character/CharacterParty/` | Static class |
| `PartyFollowMode.cs` | `Assets/Scripts/Character/CharacterParty/` | Enum |
| `PartyState.cs` | `Assets/Scripts/Character/CharacterParty/` | Enum |
| `CharacterParty.cs` | `Assets/Scripts/Character/CharacterParty/` | CharacterSystem MonoBehaviour |
| `PartyGatherZone.cs` | `Assets/Scripts/Character/CharacterParty/` | MonoBehaviour (trigger forwarder on child GO) |
| `PartyInvitation.cs` | `Assets/Scripts/Character/CharacterParty/` | InteractionInvitation subclass |
| `MapType.cs` | `Assets/Scripts/World/MapSystem/` | Enum |
| `MapTransitionZone.cs` | `Assets/Scripts/World/MapSystem/` | MonoBehaviour (trigger collider) |
| `BTCond_IsInPartyFollow.cs` | `Assets/Scripts/AI/BehaviourTree/` | BT condition node |
| `BTAction_FollowPartyLeader.cs` | `Assets/Scripts/AI/BehaviourTree/` | BT action node |
| `UI_PartyPanel.cs` | `Assets/Scripts/UI/` | MonoBehaviour (HUD panel) |
| Leadership SkillSO | `Assets/Data/Skills/` | ScriptableObject asset |

### Modified Existing Files

| File | Change |
|------|--------|
| `Character.cs` | Add `CharacterParty` reference + property. Remove old party stubs (`_currentParty`, `CreateParty()`, `SetParty()`, `Invite()`). |
| `MapController.cs` | Add `MapType _mapType` field. Replace `IsInteriorOffset` with `MapType` check. |
| `MapTransitionDoor.cs` | In `Interact()`: check for party leader + Region/Dungeon target → call `CharacterParty.StartGathering()` instead of creating transition action. |
| `NPCBehaviourTree.cs` | Insert `BTCond_IsInPartyFollow` node after `_agressionSequence` (index 4) and before `_punchOutNode` in the `BTSelector` children list. |
| `HibernatedNPCData` | Add `PartyId` field. |
| `ICharacterData` | Add `PartyId` field. |
| `MacroSimulator` | Party-aware position snap during catch-up. |
| `SaveManager` | Load persisted `PartyData` into `PartyRegistry` during initialization. |
