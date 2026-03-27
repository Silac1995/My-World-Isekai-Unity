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
| `CharacterPartyController` | `CharacterSystem` MonoBehaviour | What a character **does** in a party (behavior) |

All files live in `Assets/Scripts/Character/CharacterParty/`.

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
- `State` : PartyState enum — `{ Active, LeaderlessHold, Gathering }`

Uses CharacterId UUIDs instead of direct `Character` references so party data survives hibernation, disconnects, and map transitions without dangling references. Live `Character` objects are resolved on demand via `Character.FindByUUID()`.

### PartyFollowMode Enum

`Assets/Scripts/Character/CharacterParty/PartyFollowMode.cs`

- `Strict` — NPC members pathfind to the leader at all times when free. Default.
- `Loose` — NPC members act independently (schedule, GOAP, wander within community territory). Only available on `MapType.Region` maps owned by a community.

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

---

## 2. CharacterPartyController

`CharacterPartyController : CharacterSystem` — attached to **every** character, even if not in a party. Mirrors how `CharacterCombat`, `CharacterJob`, etc. work. Dormant until a party forms.

### State

- `_partyData` : PartyData — null if not in a party
- `_followCoroutine` : Coroutine — active follow loop (NPC-only)
- `_gatherCoroutine` : Coroutine — gathering loop
- `_isGathered` : bool — has this member reached the gather zone
- `_gatherZone` : BoxCollider — trigger collider, disabled by default. Enabled during gathering.
- `_gatheredMemberIds` : HashSet\<string\> — members currently inside the gather zone

### Follow Logic (server-only, NPC members only)

- When `FollowMode == Strict` and member is free (`IsFree()`): sets blackboard flag `IsInPartyFollow = true`. The BT node `BTCond_IsInPartyFollow` picks it up and pathfinds to the leader.
- When `FollowMode == Loose` on a community-owned region: clears the flag. Member runs normal BT (schedule, GOAP, wander within territory).
- **Player members are never auto-moved.** They follow on their own. They receive toast notifications if the leader starts gathering.

### BT Integration

New BT node: `BTCond_IsInPartyFollow` with child action `BTAction_FollowPartyLeader`.

Priority position in `NPCBehaviourTree`:

```
1.  Legacy/Imperative
2.  Orders
3.  Combat
4.  Entraide (friend in danger)
5.  Aggression (enemy detected)
→   5.5 Party Follow  ← NEW
6.  PunchOut
7.  Schedule
8.  GOAP
9.  Social
10. Wander
```

Combat/aggression override party follow (members can fight). Party follow overrides schedule/wander/social (members don't wander off). The controller sets/clears the blackboard flag; the BT node reads it.

### Gathering Logic (server-only)

Triggered when the leader's `CharacterMapTransitionAction` is detected and the target map is `MapType.Region` or `MapType.Dungeon`.

**Flow:**

1. Leader interacts with a `MapTransitionDoor` or enters a `MapTransitionZone`
2. `CharacterMapTransitionAction` starts on the leader
3. System detects: leader is in a party AND target is Region/Dungeon
4. **Cancel** the transition action
5. Leader's `CharacterPartyController.StartGathering(targetMapId, targetPosition)`:
   - Stop leader's movement (immobilize)
   - Enable `_gatherZone` BoxCollider at leader's position
   - Set `PartyData.State = Gathering`
   - Notify all members: BT flag for NPCs, toast for player members
6. NPC members pathfind to the gather collider
7. `OnTriggerEnter`: member enters gather zone → add to `_gatheredMemberIds`
8. `OnTriggerExit`: member leaves gather zone → remove from `_gatheredMemberIds`
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

- `BoxCollider _gatherZone` on `CharacterPartyController`, `isTrigger = true`, disabled by default
- Enabled when `State = Gathering`, disabled when gathering ends
- Size configurable (e.g., 3x3 around the leader)
- `OnTriggerEnter`/`OnTriggerExit` track which party members are inside

### Party Lifecycle (server-only)

- `CreateParty(name = null)` — requires Leadership skill. Name defaults to `$"{leader.CharacterName}'s Party"`. Generates PartyData, registers in PartyRegistry.
- `JoinCharacterParty(Character leader)` — convenience method: checks if leader has a party, if so joins it.
- `JoinParty(partyId)` — called after `PartyInvitation` accepted. Adds to `PartyData.MemberIds`.
- `LeaveParty()` — removes from member list, clears blackboard flag, stops coroutines, clears local state.
- `KickMember(characterId)` — leader-only. Works even if target is offline (removes UUID from member list).
- `PromoteLeader(characterId)` — leader-only. Transfers leadership.
- Leader unconscious → `PartyState.LeaderlessHold`, clears follow flags. On wake-up → resume `PartyState.Active`.
- Leader dies → auto-promote next member. New leader gets Leadership skill at level 1 via `CharacterSkills.AddSkill()` if they don't have it. If no members left → disband and unregister from PartyRegistry.

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

### Cleanup (OnDestroy)

- Stop `_followCoroutine` and `_gatherCoroutine`
- Unsubscribe from leader's `OnDeath`, `OnUnconsciousChanged`, `OnWakeUp`
- Clear blackboard flag
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
  - **Party leader** → stop movement, start gathering flow
  - **Non-leader party member** → toast notification to the player leader: "[Name] is approaching the border"
  - **Solo character** → normal transition behavior
- Holds `TargetMapId` and `TargetPosition` like `MapTransitionDoor`
- Calls into `CharacterPartyController.StartGathering()` — same as door. Gathering logic lives in the controller, not the transition point.

### Changes to MapTransitionDoor

- Before starting `CharacterMapTransitionAction`, check: is interactor a party leader AND target map is Region/Dungeon?
- **Yes** → the `CharacterMapTransitionAction` starts but gets cancelled by the `CharacterPartyController` which takes over with gathering mode
- **No** (solo, interior, arena) → current behavior unchanged

---

## 4. Party Invitation

### PartyInvitation : InteractionInvitation

New file: `Assets/Scripts/Character/CharacterParty/PartyInvitation.cs`

Plugs into the existing `CharacterInvitation` pipeline with zero new infrastructure.

```
CanExecute(source, target):
  - source must have Leadership skill
  - source must be party leader (or will auto-create party)
  - target must not already be in source's party
  - target must be alive and free
  - party must not be full (Leadership skill level determines max size)

GetInvitationMessage(source, target):
  → "Want to join my group?"

OnAccepted(source, target):
  → target.CharacterPartyController.JoinParty(source.CurrentParty.PartyId)

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

### Party Size Formula

Base party size: 2 (leader + 1 member)
Per Leadership level: +1 member
Example: Leadership level 5 → max 7 members

### Skill Acquisition

- **Creating a party** requires the Leadership skill (checked in `CreateParty()` and `PartyInvitation.CanExecute()`)
- **Inheriting leadership** (auto-promote on leader death) grants Leadership at level 1 via `CharacterSkills.AddSkill()` if the character doesn't have it
- Leadership XP gained naturally through leading (future: XP on successful gathering, transitions, party activities)

---

## 6. Party HUD — UI_PartyPanel

### Create Party

- "Create Party" button visible when: player has Leadership skill AND is not in a party
- Optional name input field — defaults to `"{PlayerName}'s Party"` if left empty
- Creates party immediately via `CharacterPartyController.CreateParty()`

### Party View

- Visible only when the local player is in a party
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

---

## 8. Persistence & Hibernation

### Player Character Save (ICharacterData)

- Add `PartyId : string` field (GUID, null if not in a party)

### PartyData Serialization (server-side, saved with world state)

Saved fields: `PartyId`, `PartyName`, `LeaderId`, `MemberIds`, `FollowMode`

On server boot, all PartyData entries are loaded into `PartyRegistry`.

### HibernatedNPCData

- Add `PartyId : string` field
- On hibernation: NPC's PartyId is preserved
- On wake-up: look up PartyId in `PartyRegistry`, re-link `CharacterPartyController`

### Macro-Simulation Catch-Up (Party-Aware)

- During hibernation, NPC party members are treated as a **group** for position snap — members are placed near their leader's position instead of randomly scattered
- Follow mode, membership, leader status — all pure data, no Unity objects needed. Survives hibernation.

### Cross-Map Parties

- Members can be on different maps (e.g., one got left behind). `PartyData.MemberIds` is map-agnostic — just UUIDs.
- Follow logic only activates for members on the **same map** as the leader
- Separated members entering the leader's map later → follow resumes automatically

---

## File Summary

| File | Location | Type |
|------|----------|------|
| `PartyData.cs` | `Assets/Scripts/Character/CharacterParty/` | Plain C# class |
| `PartyRegistry.cs` | `Assets/Scripts/Character/CharacterParty/` | Static class |
| `PartyFollowMode.cs` | `Assets/Scripts/Character/CharacterParty/` | Enum |
| `PartyState.cs` | `Assets/Scripts/Character/CharacterParty/` | Enum |
| `CharacterPartyController.cs` | `Assets/Scripts/Character/CharacterParty/` | CharacterSystem MonoBehaviour |
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
| `Character.cs` | Add `CharacterPartyController` reference + property. Remove old `CharacterParty` stubs. |
| `MapController.cs` | Add `MapType _mapType` field. Replace `IsInteriorOffset` with `MapType` check. |
| `MapTransitionDoor.cs` | Check for party leader before transition — delegate to gathering if applicable. |
| `NPCBehaviourTree.cs` | Add `BTCond_IsInPartyFollow` node at priority 5.5. |
| `HibernatedNPCData` | Add `PartyId` field. |
| `ICharacterData` | Add `PartyId` field. |
| `MacroSimulator` | Party-aware position snap during catch-up. |
