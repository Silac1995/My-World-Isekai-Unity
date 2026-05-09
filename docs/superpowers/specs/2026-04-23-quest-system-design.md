# Quest System Design

**Date:** 2026-04-23
**Branch:** `multiplayyer`
**Status:** Approved Design

## Problem Statement

The job logistics cycle (shipped 2026-04-22) makes NPCs work autonomously: NPCs claim `BuildingTask`s from `BuildingTaskManager`, the `JobLogisticsManager` consumes from `BuildingLogisticsManager.OrderBook`, and wages are paid at punch-out via `WageSystemService`. **Players cannot participate in this loop.** When a player takes a job — `Character.CharacterJob.TakeJob(job, building)` — the assignment lands and the wage hooks fire on punch-out, but nothing tells the player *what to actually do*. There is no per-character work-instruction surface, no on-screen tracker, no world markers pointing at the harvestable trees they should chop or the shop they should deliver to.

The project also has no concept of "give an instruction to a character" outside of the implicit auto-claim path NPCs use. The user wants a unified primitive that the LogisticsManager Character (player or NPC) can dispatch from, that both NPC and player workers consume identically, and that future producers (main story, bounty, relationship/event) can plug into.

This spec introduces **Quest** — a unified work-instruction primitive consumed by both players and NPCs, issued by Character-roles (LogisticsManager today; expandable to any Character later), with a player-facing HUD that highlights zones, places world markers, and shows a tracker widget + log window. All wired through the existing logistics primitives so NPC behavior is unchanged.

### Requirements

1. **Quest is a unified primitive consumed by both players and NPCs.** A Quest's receiver is a `Character`. Whether that Character is a player or an NPC determines only whether the local Quest HUD renders for it; the data model, save shape, and producer-side hooks are identical.
2. **The Hybrid C unification.** Existing `BuildingTask`, `BuyOrder`, `TransportOrder`, `CraftingOrder` types implement (or are wrapped by adapters that implement) the new `IQuest` interface. NPC GOAP keeps its existing `ClaimBestTask<T>(...)` API — it just internally returns an object that also satisfies `IQuest`. Zero behavior change for NPC AI; one source of truth for the data.
3. **Flat single-objective Quests** (no multi-stage chaining for v1). One `BuyOrder` = one Quest. One `HarvestResourceTask` = one Quest. Future "narrative arc" grouping (Quest.Parent) is out of scope.
4. **Shared-capable from day one.** Each Quest carries `MaxContributors`, a `Contributors` list, and a per-character `Contribution` dictionary. Solo Quests use `MaxContributors = 1`; multi-worker Quests (HarvestResourceTask, multi-batch CraftingOrder) use higher caps. Per-character contribution attribution prevents credit-sniping.
5. **Auto-accept for job Quests.** When a player is on-shift at a building, Quests published from that building auto-claim onto their `CharacterQuestLog` (subject to `MaxContributors` and JobType eligibility). No manual "Accept" click. Player can `Abandon` to release back to the queue. Mirrors today's NPC auto-claim path.
6. **Issuer is a Character (LogisticsManager).** Every Quest's `Issuer` field points at the LogisticsManager Character of the publishing building (falls back to building owner if no LogisticsManager assigned, falls back to null if neither). Same value whether the LogisticsManager is a player or an NPC.
7. **Player-facing HUD with three visible elements** — all gated to the local player only via `Character.IsOwner` + `PlayerUI.Initialize` registration:
   - **Tracker widget** (top-right, minimal): two lines — Quest title + instruction line. Optional contributor avatars row when shared.
   - **Quest log window** (full panel, bound to L key): two columns — list grouped by category, details panel with description / contributors / Abandon / Set Focused.
   - **World-space markers**: floating gold diamond on object/action targets (style A); vertical light-shaft + ground ring on pure-location targets (style B); semi-transparent gold zone fill + crisp edge on region targets.
8. **WorldId + MapId on every Quest** so save/load survives portal-gate transitions and HUD markers correctly filter to the player's current map.
9. **Server-authoritative mutations** (per project rule #18). `TryJoin`, `TryLeave`, `RecordProgress` execute on the server. Clients route via ServerRpc; state replicates back via NetworkList for the per-character claim list, ClientRpc for per-quest progress events.
10. **Persisted via `ICharacterSaveData<QuestLogSaveData>`** (per project rule #20). `CharacterQuestLog` saves snapshots of claimed Quests so the HUD can render even when the underlying Quest source is unloaded (different map, hibernating). On load, snapshots whose `OriginMapId` doesn't match the current map are held dormant; matching ones reconcile against the live source.
11. **Documentation shipped alongside code** (rules #28, #29b). New SKILL.md for `quest-system`; new wiki page at `wiki/systems/quest-system.md`; updates to `jobs-and-logistics`, `building-task-manager`, `building-logistics-manager`, `commercial-building`, and `worker-wages-and-performance` to reflect the cross-link.

### Non-Goals

- **Bounty producer.** Public bounty board with anyone-can-accept semantics, time-limited rewards. Future spec.
- **Main-story producer.** Scripted triggers, dialogue-tree integration, branching narrative. Future spec.
- **Relationship / event producer.** Date invitations, party formation, friend requests as Quests. Future spec.
- **Player → Character explicit issuance UI.** Owner orders an employee, friend asks help. Needs notification + accept/refuse + permission gate UI. Future spec.
- **Manual dispatch UI for player-as-LogisticsManager.** Browse the queue, hand-pick which Quest to assign to which worker. Auto-claim covers v1.
- **Multi-stage Quests** (Quest.Parent grouping). UI sugar; can land later without breaking flat semantics.
- **Reward layer beyond wages.** XP, reputation, quest-only currency. Slots into `IQuest` as optional fields when needed.
- **Hidden / fog-of-war Quests.** Stub interface (`IQuestTarget.IsVisibleToPlayer`) is in place but always returns true.
- **Quest cooldown after abandon.** Clean re-claim allowed; add cooldown only if abuse appears.
- **Quest chains, prerequisites, time-of-day gates.** Out of scope.
- **Per-category color theming** (Job=gold, Bounty=red, Main=blue, Event=pink). v1 ships gold for all categories; per-category color is a future polish pass.
- **Cross-world / cross-map quests.** `OriginWorldId` + `OriginMapId` are saved; `TargetWorldId` / `TargetMapId` are *not* — each Quest is single-map for v1.

---

## Architecture Overview

### Approach: Thin facade over existing primitives + new per-character receiver subsystem

`IQuest` is an interface implemented by the existing work-primitive types. No central Quest store — Quests live inside their existing containers (`BuildingTaskManager`, `BuildingLogisticsManager.OrderBook`). New code reads them through `IQuest`; old NPC code keeps using its existing typed APIs unchanged.

`CharacterQuestLog` is a new `CharacterSystem` subsystem on every `Character` (sibling of `CharacterWallet`, `CharacterWorkLog`). It holds references to claimed Quests + denormalized snapshots for save/load + HUD subscription targets.

```
[BuildingTaskManager]                   [BuildingLogisticsManager.OrderBook]
   ├ HarvestResourceTask                    ├ BuyOrder
   └ PickupLooseItemTask                    ├ TransportOrder
        │                                    └ CraftingOrder
        │
        └─────────── all implement IQuest ──────────────┐
                                                         │
                 [Building.GetAvailableQuests()]  ◄──────┤
                          │                              │
                          │                              │
       Existing NPC API ──┴── New player path            │
       (ClaimBestTask<T>)     (CharacterQuestLog auto-claim)
                                       │
                                       ▼
                          [PlayerUI Quest HUD] (local-player only)
                          ├── Tracker widget (top-right)
                          ├── Log window (L key)
                          └── World markers + zone highlights
```

### Data ownership

| Data | Owner | Persisted? |
|---|---|---|
| Quest mechanical state (progress, contributors, expiration) | The producer (BuildingTask, BuyOrder, etc.) | Yes — via the existing producer save path |
| Per-character claimed quest IDs + snapshots | `CharacterQuestLog` | Yes — new `QuestLogSaveData` |
| Focused-quest preference (which to show in tracker) | `CharacterQuestLog._focusedQuestId` | Yes — saved with QuestLogSaveData |
| HUD state (tracker visible? log open?) | `PlayerUI` widgets | No — UI state, not persisted |

---

## Section 1: Data Model

### `IQuest` (interface)

```csharp
public interface IQuest
{
    // Identity & origin
    string QuestId { get; }                  // server-generated GUID
    string OriginWorldId { get; }            // save-world the quest was issued in
    string OriginMapId { get; }              // map within that world (issuer building's map)
    Character Issuer { get; }                // the LogisticsManager Character (or building owner fallback)
    QuestType Type { get; }                  // enum: Job, Main, Bounty, Event, Custom (only Job in use v1)

    // Display data
    string Title { get; }                    // "Harvest Wood"
    string InstructionLine { get; }          // "Chop 10 logs at the East Woods"
    string Description { get; }              // longer text for the log window

    // Lifecycle
    QuestState State { get; }                // Open | Full | Completed | Abandoned | Expired
    bool IsExpired { get; }
    int RemainingDays { get; }               // matches existing order expiration

    // Progress
    int TotalProgress { get; }               // sum of all contributions
    int Required { get; }                    // completion target
    int MaxContributors { get; }             // 1 = solo; N = capped; int.MaxValue = open

    // Contributors
    IReadOnlyList<Character> Contributors { get; }
    IReadOnlyDictionary<string, int> Contribution { get; }   // characterId -> contribution

    // Mutations (server-authoritative)
    bool TryJoin(Character character);
    bool TryLeave(Character character);
    void RecordProgress(Character character, int amount);

    // Targeting
    IQuestTarget Target { get; }

    // Events (fire on every machine for Contributors)
    event Action<IQuest> OnStateChanged;
    event Action<IQuest, Character, int> OnProgressRecorded;  // (quest, contributor, amount)
}

public enum QuestType { Job = 0, Main = 1, Bounty = 2, Event = 3, Custom = 99 }

public enum QuestState { Open, Full, Completed, Abandoned, Expired }
```

### `IQuestTarget` (interface)

```csharp
public interface IQuestTarget
{
    Vector3 GetWorldPosition();              // for floating marker (style A)
    Vector3? GetMovementTarget();            // null for object targets; non-null for "go-here" (style B)
    Bounds? GetZoneBounds();                 // null for point targets; non-null for region (zone fill)
    string GetDisplayName();                 // for HUD text ("the East Woods", "Bob's Smithy")
    bool IsVisibleToPlayer(Character viewer); // v1: always returns true
}
```

Concrete targets shipped in v1:
- `HarvestableTarget` — wraps a `Harvestable`. Object marker (style A). No zone (zone is provided by the parent zone-fill Quest, not here).
- `WorldItemTarget` — wraps a `WorldItem` to pick up. Object marker.
- `ZoneTarget` — wraps a `Zone` collider (e.g., a HarvestingBuilding's `HarvestableZone`). Zone fill + a center waypoint.
- `BuildingTarget` — wraps a `Building`. Movement-target style (B beacon at the building's `DeliveryZone` or interaction point).
- `CharacterTarget` — wraps an NPC to talk to / deliver to. Object marker over the NPC's head.

A single Quest can compose targets — e.g., the harvest-wood Quest publishes a `ZoneTarget(HarvestableZone)` for the area highlight AND publishes a separate per-tree marker via the same Quest's `Target` returning a composite. v1 keeps it simple: one `Target` per Quest. The renderer iterates whatever's non-null on that one target.

### Lifecycle state machine

```
                ┌─────────[Open]────join────[Open]──join (==MaxContributors)──[Full]
                │           │                                                      │
                │           ├───leave (drops to 0)──┐                              │
                │           │                       │                              │
                │           │                       ▼                              │
                │           │                    [Open]                            │
                │           │                                                      │
                │           ▼                                                      │
                │       progress >= Required ────> [Completed]                    │
                │                                                                  │
                ├──RemainingDays = 0──> [Expired]                                  │
                │                                                                  │
                └──────────────────────────────────────────────────────────────────┘
                                  (also reachable from Full via leave)
```

`Full` quests are hidden from "Available" lists but still visible to their contributors. Server-side state machine; ClientRpc broadcasts state changes.

### `CharacterQuestLog`

New `CharacterSystem` subsystem on every `Character`.

```csharp
public class CharacterQuestLog : CharacterSystem, ICharacterSaveData<QuestLogSaveData>
{
    public IReadOnlyList<IQuest> ActiveQuests { get; }
    public IQuest FocusedQuest { get; }       // for the tracker widget

    // Mutations (server-authoritative; clients route via ServerRpc)
    public bool TryClaim(IQuest quest);       // calls quest.TryJoin(_character)
    public bool TryAbandon(IQuest quest);     // calls quest.TryLeave(_character)
    public void SetFocused(IQuest quest);     // local-only HUD preference, broadcast via NetVar

    // Events (HUD subscribes for the local player only)
    public event Action<IQuest> OnQuestAdded;
    public event Action<IQuest> OnQuestRemoved;
    public event Action<IQuest> OnQuestProgressChanged;
    public event Action<IQuest> OnFocusedChanged;

    // ICharacterSaveData
    public string SaveKey => "CharacterQuestLog";
    public int LoadPriority => 70;            // after CharacterJob (60) + CharacterWorkLog (65)
    public QuestLogSaveData Serialize();
    public void Deserialize(QuestLogSaveData data);
}
```

### `QuestLogSaveData` (DTO)

```csharp
[Serializable]
public class QuestLogSaveData
{
    public List<QuestSnapshotEntry> activeQuests = new();
    public string focusedQuestId;
}

[Serializable]
public class QuestSnapshotEntry
{
    public string questId;
    public string originWorldId;
    public string originMapId;
    public string issuerCharacterId;
    public int questType;                     // QuestType enum as int

    // Display data (denormalized so HUD works when source unloaded)
    public string title;
    public string instructionLine;
    public string description;

    // Progress snapshot
    public int totalProgress;
    public int required;
    public int maxContributors;
    public int myContribution;                // this character's contribution

    public int state;                         // QuestState enum as int

    // Target snapshot (what the renderer needs even if the live target is gone)
    public Vector3 targetPosition;
    public bool hasZoneBounds;
    public Vector3 zoneCenter;
    public Vector3 zoneSize;
    public string targetDisplayName;
}
```

Snapshots survive the producer being unloaded (different map, hibernating). On load:
1. If `originMapId == currentMapId` → try to resolve the live `IQuest` by ID; if found, re-attach (re-add character to `Contributors`); if not found, drop snapshot and log warning.
2. If `originMapId != currentMapId` → keep snapshot dormant; HUD shows it in the log marked "Pending — return to [Map name]".

---

## Section 2: Producers

### Refactor: existing types implement IQuest

Each existing work-primitive type implements `IQuest` directly (no adapter wrappers). This is the Hybrid C choice — adding the interface to `BuildingTask` and to each order class is the smallest possible change that gives both NPC consumers and player consumers a unified read surface.

| Existing primitive | New `IQuest` mapping | MaxContributors |
|---|---|---|
| `BuildingTask` (abstract) | Direct `: IQuest`. `MaxContributors = MaxWorkers`. `Issuer` = building's LogisticsManager. | varies |
| `HarvestResourceTask` | Inherits from BuildingTask; `Target = HarvestableTarget(_harvestableTarget)`. `Required = node.RemainingYield`. | 10 |
| `PickupLooseItemTask` | `Target = WorldItemTarget(item)`. `Required = 1`. | 1 |
| `BuyOrder` | Direct `: IQuest`. `Target = BuildingTarget(supplier)`. `Required = qty`. | 1 (one trip) |
| `TransportOrder` | `Target = BuildingTarget(destination)`. `Required = qty`. | 1 |
| `CraftingOrder` | `Target = BuildingTarget(workshop)`. `Required = Quantity`. | int.MaxValue (multiple crafters) |

The existing fields stay where they are — `BuyOrder.Quantity` still exists; `IQuest.Required` is just a getter that returns it. Same for `BuildingTask.MaxWorkers` → `IQuest.MaxContributors`.

### Building publish API

`CommercialBuilding` (parent class for both TaskManager + LogisticsManager users) gets:

```csharp
public IEnumerable<IQuest> GetAvailableQuests(QuestType filter = (QuestType)(-1));
public event Action<IQuest> OnQuestPublished;       // fires when a new quest enters either queue
public event Action<IQuest> OnQuestStateChanged;    // fires when any tracked quest changes state
```

Internally these aggregate over `_taskManager.AvailableTasks` + `_logisticsManager.OrderBook.AvailableQuests` (a new accessor on `LogisticsOrderBook`).

### Auto-claim path

When a player is on-shift at a building, a hook on `OnQuestPublished` filters the new Quest by:
1. Player's current `CharacterJob.ActiveJobs[i].AssignedJob.Type` matches the Quest's eligible JobType (e.g., a player Crafter can claim a CraftingOrder, not a BuyOrder).
2. Quest's `MaxContributors` not yet reached.
3. Player isn't already in `Contributors`.

If all pass, server calls `quest.TryJoin(player)` and the player's `CharacterQuestLog` adds it. NPC auto-claim continues to work via the existing `ClaimBestTask<T>` path on the NPC's GOAP — both paths call into the same underlying `TryJoin`.

### Issuer resolution

```csharp
Character ResolveIssuer(CommercialBuilding building)
{
    // 1. LogisticsManager Character if present
    var lm = building.GetWorkerInRole(JobType.LogisticsManager);
    if (lm != null) return lm;

    // 2. Owner Character
    if (building.HasOwner) return building.GetOwner();

    // 3. null (system-issued)
    return null;
}
```

The issuer field is for display + (future) reputation effects + (future) quest dialogue. Quests with `Issuer == null` are still functional.

---

## Section 3: HUD

### Tracker widget

`UI_QuestTracker : MonoBehaviour` — registered on `PlayerUI` alongside the existing widgets (`_equipmentUI`, `_relationsUI`, etc.).

Layout:
```
Anchor: top-right
Size: ~280px wide, ~60px tall (grows when contributors row visible)

┌─────────────────────────────┐
│ Harvest Wood                │   ← FocusedQuest.Title
│ Chop 10 logs at East Woods  │   ← FocusedQuest.InstructionLine (truncate to ~40 chars)
│ [👤][👤][👤] +2 more         │   ← optional contributor avatars (max 4)
└─────────────────────────────┘
```

Behavior:
- Visible iff `CharacterQuestLog.FocusedQuest != null`.
- On `OnFocusedChanged` → swap content + brief flash animation.
- On `OnQuestProgressChanged(focusedQuest)` → update progress in instruction line if formatted ("Chop 10 logs" → "Chop 7 / 10 logs").
- Clicking the widget opens the quest log window scrolled to the focused quest.

### Quest log window

`UI_QuestLogWindow : UI_WindowBase` — opened/closed via L key (placeholder; rebindable in input config).

Layout:
```
┌─────────────────────────────────────────────────────────┐
│  Active Quests                                  [X]     │
├──────────────────┬──────────────────────────────────────┤
│ ▼ JOB            │ HARVEST WOOD                          │
│   • Harvest Wood │                                       │
│   • Deliver Iron │ Issued by: Marie (LogisticsManager)   │
│   • Craft Sword  │ World: Solo World 1 · Map: Bob's Town │
│                  │ Progress: 47 / 100 logs               │
│ ▽ MAIN  (empty)  │                                       │
│ ▽ BOUNTY (empty) │ <Description text from Quest>         │
│ ▽ EVENT (empty)  │                                       │
│                  │ Contributors (3):                     │
│                  │   You (12 logs)                       │
│                  │   Aldo (24 logs)                      │
│                  │   Brent (11 logs)                     │
│                  │                                       │
│                  │ [ Set as Focused ]   [ Abandon ]      │
└──────────────────┴──────────────────────────────────────┘
```

Behavior:
- Left column auto-groups quests by `QuestType`. Empty future categories (Main, Bounty, Event) shown grayed.
- Pending-snapshot quests (saved but `OriginMapId != currentMapId`) show with a "[Pending — return to {mapName}]" badge, no Set Focused / Abandon (they're frozen until you return).
- "Set as Focused" calls `CharacterQuestLog.SetFocused(quest)`.
- "Abandon" calls `CharacterQuestLog.TryAbandon(quest)` → server `quest.TryLeave(player)` → state updates broadcast back. Quest disappears from the list once removed.

No completed-quest history tab in v1.

### World-space markers

`QuestWorldMarkerRenderer : MonoBehaviour` — one instance on the local-player HUD canvas (or a dedicated worldspace canvas). On `CharacterQuestLog.OnQuestAdded/Removed`, spawns/despawns marker prefabs for that quest's target.

Three prefab variants:
- `QuestMarker_Diamond.prefab` — floating gold diamond + pulse + dashed beam to ground. Billboards to `Camera.main`. Used when `IQuestTarget.GetMovementTarget() == null` (object/action targets).
- `QuestMarker_Beacon.prefab` — vertical light shaft + pulsing ring decal. Used when `IQuestTarget.GetMovementTarget() != null` (movement targets).
- `QuestZone_Fill.prefab` — flat semi-transparent gold mesh sized to `IQuestTarget.GetZoneBounds()` + crisp perimeter line via existing `BattleZoneOutline` pattern. Used when `IQuestTarget.GetZoneBounds() != null`.

A single Quest may render *both* a zone fill and an object marker (e.g., HarvestWood = zone fill on the HarvestableZone + diamond on the closest harvestable). v1: one Target per Quest, so the renderer simply checks all three target methods and renders whatever's non-null.

Filtering: a Quest's marker renders only if `quest.OriginMapId == localPlayer.CharacterMapTracker.CurrentMapId`. When the player crosses a map boundary, the renderer stops drawing markers for off-map quests automatically.

### Player POV gating

All HUD components subscribe to the **local player's** `CharacterQuestLog`. Resolution path:

```csharp
// In PlayerUI.Initialize(playerCharacter):
var log = playerCharacter.CharacterQuestLog;
log.OnQuestAdded += _trackerUI.HandleQuestAdded;
log.OnQuestRemoved += _trackerUI.HandleQuestRemoved;
log.OnFocusedChanged += _trackerUI.HandleFocusChanged;
log.OnQuestAdded += _logWindow.RefreshList;
log.OnQuestRemoved += _logWindow.RefreshList;
log.OnQuestAdded += _markerRenderer.SpawnMarkers;
log.OnQuestRemoved += _markerRenderer.DespawnMarkers;
```

When `Character.SwitchToNPC()` runs, these subscriptions clear (per existing pattern in `PlayerUI`). NPCs never trigger quest HUD because their `CharacterQuestLog` events go nowhere.

---

## Section 4: Networking

Server-authoritative throughout. Three sync surfaces:

### CharacterQuestLog (per-character claimed list)

Fields:
- `_claimedQuestIds : NetworkList<FixedString64Bytes>` — IDs of currently-claimed quests on this Character. Server-write.
- `_focusedQuestId : NetworkVariable<FixedString64Bytes>` — server-write, everyone-read. Persists across disconnect/reconnect because it lives on the server-authoritative Character.

Mutations (`TryClaim`, `TryAbandon`, `SetFocused`) route through ServerRpc:

```csharp
[ServerRpc(RequireOwnership = false)]
private void TryClaimServerRpc(string questId);

[ServerRpc(RequireOwnership = false)]
private void TryAbandonServerRpc(string questId);

[ServerRpc(RequireOwnership = false)]
private void SetFocusedServerRpc(string questId);
```

Server validates → mutates list → NetworkList sync propagates.

### Per-quest snapshot push (denormalized for client HUD)

When `_claimedQuestIds` adds a new id on the server, the server pushes the full `QuestSnapshotEntry` to the owning client via:

```csharp
[ClientRpc]
private void PushQuestSnapshotClientRpc(QuestSnapshotEntry snapshot, ClientRpcParams target);
```

The owning client caches the snapshot in a local `Dictionary<string, QuestSnapshotEntry>` keyed by quest id; the HUD reads from this cache.

When per-quest progress changes (a contributor records a unit), server fires:

```csharp
[ClientRpc]
private void QuestProgressUpdatedClientRpc(string questId, int newTotalProgress, int newMyContribution, ClientRpcParams target);
```

This avoids replicating the full quest object across the network — only the dynamic numbers change.

### Late-joiner

NetworkList syncs the claimed quest IDs on join. The server pushes a `PushQuestSnapshotClientRpc` for each id. Same pattern as the wallet's known ClientRpc-on-change limitation: clients see snapshots from join-time forward.

### Validation matrix

| Scenario | Behavior |
|---|---|
| Host claims a quest for an NPC | Server mutates; ClientRpc to NPC owner (server itself); NPC has no HUD subscriber, no visible effect. |
| Client player claims a quest | Client → ServerRpc → server validates (player on-shift, quest has room) → server adds to NetworkList → snapshot push → client HUD updates. |
| Two clients try to claim the last slot of a Full quest | Server processes ServerRpcs in order; first wins, second gets back a denied state (no snapshot pushed; client HUD shows nothing happened). |
| Client abandons a quest | Client → ServerRpc → server removes from NetworkList → ClientRpc to remove from local cache → HUD refresh. |
| Late-joiner | NetworkList initial sync delivers ids; per-quest ClientRpc delivers snapshots; HUD populates. |
| Player crosses map boundary | `CharacterMapTracker.CurrentMapId` changes; `QuestWorldMarkerRenderer` filters out quests where `OriginMapId != CurrentMapId`; markers disappear without code intervention. |

---

## Section 5: Save / Load

`CharacterQuestLog` implements `ICharacterSaveData<QuestLogSaveData>` per the established pattern.

```csharp
public string SaveKey => "CharacterQuestLog";
public int LoadPriority => 70;     // after CharacterJob (60), CharacterWorkLog (65)
```

`Serialize()` emits a `QuestSnapshotEntry` for each claimed quest, denormalizing all display + targeting data (so the snapshot is self-sufficient even if the producer is unloaded).

`Deserialize(QuestLogSaveData data)`:
1. For each saved entry:
   - If `entry.originMapId == currentMapId`:
     - Attempt to resolve the live `IQuest` by ID via `BuildingManager.FindBuildingById(...)?.GetQuestById(entry.questId)`.
     - If found and quest is not Completed/Expired → re-attach (`quest.TryJoin(_character)`); add to `ActiveQuests`.
     - If found but Completed/Expired → drop entry, log info.
     - If not found → keep snapshot dormant (live source missing); log warning.
   - If `entry.originMapId != currentMapId` → keep snapshot dormant; appears in log marked "Pending — return to {mapName}".
2. Restore `_focusedQuestId` from `data.focusedQuestId`.

Pending dormant snapshots reconcile when the player returns to that map: a hook on `CharacterMapTracker.OnMapChanged` re-runs the resolution path for snapshots whose `originMapId` matches the new map.

---

## Section 6: Hibernation Interplay

Per project rule #30, anything that changes over time needs an offline catch-up formula in `MacroSimulator`.

Quests on hibernated maps:
- The producer's existing hibernation path (BuyOrder expiry, CraftingOrder progress via `JobYieldRecipe`) continues to apply per the wage-system Tasks 26 TODO. Quest state mirrors whatever the underlying primitive does.
- Player snapshots tied to a hibernating map stay dormant in the player's `CharacterQuestLog` — they're frozen, displayed in the log as Pending, no HUD markers rendered.
- On wake-up (player returns to that map), the reconciliation path re-resolves snapshots; if the quest was completed offline by the macro-sim, the player's snapshot drops with a notification.
- **Open question** (deferred to a future spec): if a player abandons a Quest while on a different map, does the queue see it? v1 answer: the abandon is queued locally and applied to the live source when the player's map is awake. Cleanest is to disallow abandon on dormant snapshots — UI greys out the button. **v1 ships with the disallow; abandon-on-dormant is a future polish.**

---

## Section 7: Configuration & Authoring

No new ScriptableObject assets for v1 — Quest data is fully derived from existing logistics primitives. Future producers (bounty, main story) will introduce SO authoring (`BountyDefinitionSO`, `MainQuestSO`).

The `QuestType` enum is the sole authoring surface for v1: only `Job` is in active use; `Main`, `Bounty`, `Event`, `Custom` are placeholder values reserved for future producers.

---

## Section 8: UI Surface (Data Contract for Future Polish)

This spec ships the three HUD elements above; future polish UI passes can build on:

- `Character.CharacterQuestLog.ActiveQuests` — full claim list.
- `Character.CharacterQuestLog.FocusedQuest` — current tracker target.
- `Character.CharacterQuestLog.OnQuestAdded/Removed/ProgressChanged/FocusedChanged` — push events.
- `IQuest.Type` + per-category color (future) for visual theming.
- `IQuestTarget.GetDisplayName()` for breadcrumb-style "go to {name}" text.

---

## Section 9: Edge Cases & Defensive Behavior

Per project rule #31:

| Case | Handling |
|---|---|
| Player claims a quest, then their job is force-revoked mid-shift | `ForceAssignJob` cleanup hook removes player from all quests of the revoked job's building. NetworkList sync pushes the removal. |
| Quest's underlying primitive is destroyed (building demolition mid-quest) | Building destruction fires `OnQuestStateChanged(quest, Abandoned)`. CharacterQuestLog removes from active list. Snapshot stays in save until next save (then gone). |
| Player tries to join a Quest at the exact moment it transitions to Full | Server's `TryJoin` returns false; client HUD does not update. No log spam — silent fail. |
| Player is offline (disconnected) when a contributed Quest completes | Other contributors finalize the quest; on player reconnect, snapshot resolves to "Completed since last session" notification + drop. |
| Quest's Issuer Character is destroyed (e.g., the LogisticsManager NPC dies) | `Issuer` becomes null; tracker widget shows "Issued by: —"; quest still functional. |
| `CharacterMapTracker.CurrentMapId` is unset (new character, no map binding) | Marker renderer skips quest filtering (no map known); falls back to drawing all markers. Defensive — should not happen post-spawn. |
| `QuestLogSaveData` from an old version missing fields | Newtonsoft fills missing fields with defaults; missing `originMapId` falls back to "[unknown]"; snapshot stays Pending until the player visits a building that publishes a matching ID. |
| Player abandons a Pending dormant snapshot | UI prevents this in v1 — Abandon button greyed out for Pending quests. |
| Two players both try to `SetFocused` on the same quest | No conflict — focus is per-CharacterQuestLog, not global. |

---

## Section 10: Documentation Deliverables

Per rules #28, #29, #29b — every system change updates SKILL.md, evaluates agents, updates the wiki.

### New SKILL.md

- `.agent/skills/quest-system/SKILL.md` — IQuest/IQuestTarget contracts, CharacterQuestLog API, integration hooks, gotchas.

### Updated SKILL.md

- `.agent/skills/job_system/SKILL.md` — append "Quest integration" section: BuildingTask now also IQuest; CharacterQuestLog auto-claim path on player on-shift.
- `.agent/skills/logistics_cycle/SKILL.md` — append "Quest integration" section: BuyOrder / TransportOrder / CraftingOrder now also IQuest; auto-publish on enqueue.
- `.agent/skills/save-load-system/SKILL.md` — append `CharacterQuestLog` (SaveKey "CharacterQuestLog", LoadPriority 70).
- `.agent/skills/player_ui/SKILL.md` — append `UI_QuestTracker`, `UI_QuestLogWindow`, `QuestWorldMarkerRenderer` registration.

### Wiki

- **NEW:** `wiki/systems/quest-system.md` — architecture overview.
- **UPDATE:** `wiki/systems/jobs-and-logistics.md`, `wiki/systems/commercial-building.md`, `wiki/systems/building-logistics-manager.md`, `wiki/systems/building-task-manager.md`, `wiki/systems/worker-wages-and-performance.md` — change-log entries + reciprocal wikilinks.

### Agent evaluation (rule #29)

- `building-furniture-specialist` — gains awareness of IQuest interface on building's task/order primitives + new building publish events.
- `npc-ai-specialist` — gains awareness that `BuildingTask.ClaimBestTask<T>` now returns IQuest (via inheritance) and that NPC's CharacterQuestLog tracks the claim server-side.

A new `quest-system-specialist` agent could be created if the Quest surface keeps growing across producers (bounty, main story, etc.) — for now, the existing two cover the v1 surface adequately. **No new agent for v1.**

---

## Section 11: Open Questions / Future Work

Captured here so they're not lost; **not** in scope for this spec:

1. **Bounty producer** — public bounty board, anyone-can-accept, time-limited.
2. **Main-story producer** — scripted triggers, dialogue tree, branching.
3. **Relationship / event producer** — date / party / friend-request quests.
4. **Player → Character explicit issuance UI** — owner orders an employee, friend asks help.
5. **Manual dispatch UI for player-as-LogisticsManager** — browse queue, hand-pick.
6. **Multi-stage quests** (`Quest.Parent` grouping for narrative arcs).
7. **Reward layer beyond wages** — XP, reputation, quest-only currency.
8. **Per-category color theming** in HUD (Job=gold, Bounty=red, Main=blue, Event=pink).
9. **Hidden / fog-of-war quests** — `IQuestTarget.IsVisibleToPlayer` becomes meaningful.
10. **Quest cooldown after abandon** — anti-griefing if abuse appears.
11. **Cross-map / cross-world quests** — `TargetWorldId` / `TargetMapId` fields.
12. **Abandon on dormant snapshots** — currently disallowed; future polish.
13. **Completed-quest history tab** in the log window.
14. **Quest chains / prerequisites / time-of-day gates**.
15. **The "you're close" outline-target style (waypoint option C)** — fades floating marker, applies object outline within ~3 units of the target.

---

## References — Existing Code

- `Assets/Scripts/Character/CharacterJob/CharacterJob.cs` — `ActiveJobs`, `TakeJob`, `JobAssignment`.
- `Assets/Scripts/World/Buildings/CommercialBuilding.cs` — `WorkerStartingShift` / `WorkerEndingShift` (wage hooks lined up; quest auto-claim hooks to be added near `WorkerStartingShift`).
- `Assets/Scripts/World/Buildings/BuildingTaskManager.cs` — `AvailableTasks`, `InProgressTasks`, `ClaimBestTask<T>`.
- `Assets/Scripts/World/Buildings/BuildingLogisticsManager.cs` — `OrderBook`, `_activeOrders`, `_placedBuyOrders`, etc.
- `Assets/Scripts/World/Buildings/Tasks/BuildingTask.cs` — abstract base; gains `IQuest` implementation.
- `Assets/Scripts/World/Buildings/Tasks/HarvestResourceTask.cs` — `_harvestableTarget`, `MaxWorkers`.
- `Assets/Scripts/Interactable/Harvestable.cs` — wrapped by `HarvestableTarget`.
- `Assets/Scripts/Item/WorldItem.cs` — wrapped by `WorldItemTarget`.
- `Assets/Scripts/World/Buildings/Building.cs` — `BuildingId`, `BuildingDisplayName`; wrapped by `BuildingTarget`.
- `Assets/Scripts/Character/Character.cs` — facade; gets new `CharacterQuestLog` SerializeField + property.
- `Assets/Scripts/Character/CharacterMapTracker.cs` — `CurrentMapId` for the marker filter.
- `Assets/Scripts/Character/SaveLoad/ICharacterSaveData.cs` — save contract.
- `Assets/Scripts/Character/SaveLoad/CharacterDataCoordinator.cs` — auto-discovery.
- `Assets/Scripts/UI/PlayerUI.cs` — initialize quest UI for local player.
- `Assets/Scripts/UI/UI_WindowBase.cs` — base for the log window.
- `Assets/Scripts/BattleManager/BattleZoneOutline.cs` — perimeter outline pattern reused for zone-fill crisp edge.
- `Assets/Scripts/Core/SaveLoad/WorldAssociation.cs` — source of `OriginWorldId`.
- `Assets/Scripts/World/MapSystem/MapController.cs` + `MapRegistry.cs` — source of `OriginMapId`.

## References — Existing Documentation

- `.agent/skills/job_system/SKILL.md`
- `.agent/skills/logistics_cycle/SKILL.md`
- `.agent/skills/save-load-system/SKILL.md`
- `.agent/skills/player_ui/SKILL.md`
- `wiki/systems/jobs-and-logistics.md`
- `wiki/systems/commercial-building.md`
- `wiki/systems/building-logistics-manager.md`
- `wiki/systems/building-task-manager.md`
- `wiki/systems/worker-wages-and-performance.md`
- `docs/superpowers/specs/2026-04-22-worker-wages-and-performance-design.md`
