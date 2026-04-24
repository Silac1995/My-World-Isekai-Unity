---
name: quest-system
description: Unified Quest primitive consumed by both players and NPCs. Hybrid C unification — BuildingTask + 3 order types implement IQuest directly. CharacterQuestLog subsystem holds claimed snapshots, syncs via NetworkList + ClientRpc, persists via ICharacterSaveData.
---

# Quest System

A "Quest" in this project is a **work-instruction primitive**, not just a player-facing thing. Both NPCs and players consume the same `IQuest` data — only the rendering layer (HUD) is player-specific. The system is the Hybrid C unification: existing work-primitives (`BuildingTask` + `BuyOrder`/`TransportOrder`/`CraftingOrder`) implement `IQuest` directly. Zero behavior change for NPC GOAP — `BuildingTaskManager.ClaimBestTask<T>` still returns the same types, which now also satisfy `IQuest`.

## When to use this skill

- Adding a new producer that should publish work to players + NPCs (a bounty board, a main-story step, an NPC's "fetch me X" request).
- Adding a new `IQuestTarget` for a new world-entity type that quests should point at.
- Wiring a player HUD element that reads from `CharacterQuestLog`.
- Debugging "why isn't this player auto-claiming" or "why is this quest stuck".
- Extending the `JobType` eligibility mapping in `CommercialBuilding.DoesJobTypeAcceptQuest`.

## Public API

### IQuest (the unified primitive)

```csharp
namespace MWI.Quests
{
    public interface IQuest
    {
        string QuestId { get; }                  // server-generated GUID, unique within process lifetime
        string OriginWorldId { get; }            // save-world id (empty in v1 — no WorldAssociation lookup yet)
        string OriginMapId { get; }              // map within that world (set by CommercialBuilding.PublishQuest)
        Character Issuer { get; }                // LogisticsManager Worker > Owner > null

        QuestType Type { get; }                  // Job, Main, Bounty, Event, Custom — only Job in active use
        string Title { get; }                    // short, e.g. "Harvest Resource"
        string InstructionLine { get; }          // imperative, e.g. "Harvest 5 Tree (0 / 5)"
        string Description { get; }              // longer text for log window

        QuestState State { get; }                // Open | Full | Completed | Abandoned | Expired
        bool IsExpired { get; }
        int RemainingDays { get; }

        int TotalProgress { get; }
        int Required { get; }
        int MaxContributors { get; }             // 1 = solo; N = capped; int.MaxValue = unlimited

        IReadOnlyList<Character> Contributors { get; }
        IReadOnlyDictionary<string, int> Contribution { get; }   // characterId -> contribution

        IQuestTarget Target { get; }

        bool TryJoin(Character character);       // server-authoritative
        bool TryLeave(Character character);
        void RecordProgress(Character character, int amount);

        event Action<IQuest> OnStateChanged;
        event Action<IQuest, Character, int> OnProgressRecorded;
    }

    public enum QuestType { Job = 0, Main = 1, Bounty = 2, Event = 3, Custom = 99 }
    public enum QuestState { Open, Full, Completed, Abandoned, Expired }
}
```

### IQuestTarget (where the quest points at)

```csharp
public interface IQuestTarget
{
    Vector3 GetWorldPosition();          // diamond marker position
    Vector3? GetMovementTarget();        // non-null = beacon style (style B); null = object marker (style A)
    Bounds? GetZoneBounds();             // non-null = zone fill renders
    string GetDisplayName();             // for HUD text ("the East Woods", "Bob's Smithy")
    bool IsVisibleToPlayer(Character viewer);  // v1: always true
}
```

Concrete v1 targets (`Assets/Scripts/Quest/Targets/`):
- `HarvestableTarget` — wraps `Harvestable`. Diamond marker.
- `WorldItemTarget` — wraps `WorldItem`. Diamond marker.
- `ZoneTarget` — wraps `Zone`. Zone-fill + center waypoint.
- `BuildingTarget` — wraps `Building`. Beacon at `Building.DeliveryZone.Bounds.center` (or transform.position fallback).
- `CharacterTarget` — wraps `Character`. Diamond marker over head.

### CharacterQuestLog (per-character)

```csharp
public class CharacterQuestLog : CharacterSystem, ICharacterSaveData<QuestLogSaveData>
{
    public IReadOnlyList<IQuest> ActiveQuests { get; }
    public IQuest FocusedQuest { get; }                     // server-authoritative; tracker widget reads this
    public IReadOnlyDictionary<string, QuestSnapshotEntry> Snapshots { get; }
    public IReadOnlyDictionary<string, QuestSnapshotEntry> DormantSnapshots { get; }

    public bool TryClaim(IQuest quest);                      // routes via ServerRpc on clients
    public bool TryAbandon(IQuest quest);
    public void SetFocused(IQuest quest);

    public event Action<IQuest> OnQuestAdded;
    public event Action<IQuest> OnQuestRemoved;
    public event Action<IQuest> OnQuestProgressChanged;
    public event Action<IQuest> OnFocusedChanged;
}
```

`SaveKey == "CharacterQuestLog"`, `LoadPriority == 70` (after CharacterJob 60 + CharacterWorkLog 65).

### CommercialBuilding aggregator

```csharp
public IEnumerable<MWI.Quests.IQuest> GetAvailableQuests();
public MWI.Quests.IQuest GetQuestById(string questId);
public event Action<MWI.Quests.IQuest> OnQuestPublished;
public event Action<MWI.Quests.IQuest> OnQuestStateChanged;
```

`OnQuestPublished` fires once per new quest after `Issuer` + `OriginWorldId/MapId` are stamped. The auto-claim hook in `WorkerStartingShift` subscribes to this for each on-shift worker; new published quests during the shift auto-claim immediately.

## Server-only state, client snapshots

- `_liveQuests` — server-only Dictionary of live `IQuest` references.
- `_snapshots` — both server and owning client. Server populates on claim; pushes via `[ClientRpc]` to the owning client. Client reads from this for HUD rendering.
- `_dormantSnapshots` — saved snapshots whose `originMapId` doesn't match the current map. Wake on `CharacterMapTracker.CurrentMapID` change.
- `_claimedQuestIds : NetworkList<FixedString64Bytes>` — server-write, sync to all. Used as the canonical "who has what" source.
- `_focusedQuestId : NetworkVariable<FixedString64Bytes>` — server-write, persists across disconnect/reconnect.

`QuestSnapshotEntry` implements `INetworkSerializable` (manually, with `string ??= string.Empty` coercion on the writer side) so it can pass through `[ClientRpc]`. **Don't add reference-typed fields to it without re-confirming `NetworkSerialize` covers them.**

## Auto-claim flow (Task 21)

`CommercialBuilding.WorkerStartingShift`:
1. Existing punch-in time + WorkLog hooks run (wage system).
2. `TryAutoClaimExistingQuests(worker)` — loop `GetAvailableQuests()` and `TryClaim` each that passes `IsQuestEligibleForWorker(quest, worker)`.
3. `SubscribeWorkerQuestAutoClaim(worker)` — register a handler on `OnQuestPublished` so newly-published quests during this shift auto-claim too. Handlers tracked in `_questAutoClaimHandlers` dict and unsubscribed on `WorkerEndingShift`.

Eligibility is per-(JobType, IQuest concrete type) — see `DoesJobTypeAcceptQuest` switch. Add new mappings here when introducing new job types or quest types.

## HUD layer (player-only)

Three MonoBehaviours under `Assets/Scripts/UI/Quest/`:

- **`UI_QuestTracker`** — top-right minimal widget. Title + InstructionLine (with `(N / M)` progress suffix when Required > 0). Subscribes to `OnFocusedChanged` + `OnQuestProgressChanged` + `OnQuestAdded`. Refreshes on each event.
- **`UI_QuestLogWindow`** (extends `UI_WindowBase`) — 2-column list/details panel. Bound to L key in `PlayerUI.Update`. Set Focused / Abandon buttons mutate via `CharacterQuestLog`.
- **`QuestWorldMarkerRenderer`** — spawns one of three prefabs per quest target: diamond (object), beacon (movement), zone-fill (region). Filter: `quest.OriginMapId == localPlayer.CharacterMapTracker.CurrentMapID.Value.ToString()`. Hot-refreshes on map change.

All three are wired by `PlayerUI.Initialize(playerCharacter)` to the local player's `CharacterQuestLog`. `Character.SwitchToNPC` clears the wiring (per the existing PlayerUI lifecycle pattern).

## Save / Load

`Serialize` flattens `_snapshots` + `_dormantSnapshots` into a single `activeQuests` list + `focusedQuestId`.

`Deserialize`:
1. For each entry: if `originMapId == currentMapId`, try to resolve via `BuildingManager.Instance.allBuildings → CommercialBuilding.GetQuestById`. If found and not Completed/Expired, re-attach (`quest.TryJoin(_character)`); else drop.
2. If `originMapId != currentMapId`, snapshot enters `_dormantSnapshots`.
3. `_focusedQuestId` restored on server.

On map transition, `HandleMapChanged` promotes matching dormant snapshots back to live. **Backward-compatible:** old saves without `CharacterQuestLog` deserialize as empty.

## Gotchas

- **`Issuer` setter is public** on each concrete type so `CommercialBuilding.PublishQuest` can stamp it externally. The `IQuest` interface declares `Issuer { get; }` only — the writability is concrete-class-specific.
- **`QuestSnapshotEntry` requires `INetworkSerializable`** for ClientRpc to work. Adding new reference-typed fields needs a corresponding `SerializeString` (or equivalent) call in `NetworkSerialize`.
- **`HarvestResourceTask.Required` is dynamic** — drops as the resource depletes. If you join late, the Required you see is the remaining yield, not the original. Documented in spec section 9.
- **`QuestId` is per-instance auto-Guid** — not stable across server restarts. v1 acceptance: saved snapshots may go dormant on reload if the building reconstructs fresh task instances. Future fix: hash-based id from (BuildingId + TaskTypeName + TargetId).
- **`OriginWorldId` is empty in v1** — no source yet. Map-id filtering works correctly; world-id filtering is a no-op until the WorldAssociation singleton is accessible to buildings.
- **`ResolveQuest` is O(buildings × quests)** — server-side linear scan. Future `QuestRegistry` singleton would make this O(1).
- **`Abandon` on dormant snapshots is disallowed** in v1 UI (button greyed) — the live source isn't reachable so we can't `TryLeave` the underlying quest.
- **Late-joiner client snapshots gap** — same as wallet's known limitation. Snapshots only push from join-time forward.
- **`OnQuestRemoved` event passes `null` on clients** — clients don't have live `IQuest` references; HUD reads from `_snapshots` dict directly.
- **Two parallel claim paths on `BuildingTask`**: `BuildingTaskManager.ClaimBestTask<T>` (NPC GOAP) AND `IQuest.TryJoin/TryLeave` (player via `CharacterQuestLog.TryClaim/TryAbandon`). The latter would historically only mutate `ClaimedByWorkers` and leave `BuildingTaskManager`'s `Available`/`InProgress` buckets stale. Fix: `BuildingTask.Manager` is a back-reference assigned in `BuildingTaskManager.RegisterTask`; `TryJoin`/`TryLeave` now call `Manager.NotifyTaskExternallyClaimed` / `NotifyTaskExternallyUnclaimed` so the buckets stay consistent (no more "Unknown Worker" rows in the debug HUD, and unclaimed tasks return to the available pool for the next claimer).

## Related

- `.agent/skills/character-wallet/SKILL.md` — sibling subsystem (wages paid into wallet on punch-out).
- `.agent/skills/character-worklog/SKILL.md` — sibling subsystem (per-character work counters).
- `.agent/skills/job_system/SKILL.md` — `BuildingTask` is the abstract base that became `IQuest`.
- `.agent/skills/logistics_cycle/SKILL.md` — `BuyOrder`/`TransportOrder`/`CraftingOrder` likewise.
- `.agent/skills/save-load-system/SKILL.md` — `CharacterQuestLog` adds another `ICharacterSaveData<T>`.
- `.agent/skills/player_ui/SKILL.md` — HUD widget registration pattern this skill follows.
- `wiki/systems/quest-system.md` — architecture overview.

## Source files

- `Assets/Scripts/Quest/IQuest.cs`, `IQuestTarget.cs`
- `Assets/Scripts/Quest/Targets/{HarvestableTarget,WorldItemTarget,ZoneTarget,BuildingTarget,CharacterTarget}.cs`
- `Assets/Scripts/Character/CharacterQuestLog/{CharacterQuestLog,QuestLogSaveData,QuestSnapshotEntry}.cs`
- `Assets/Scripts/UI/Quest/{UI_QuestTracker,UI_QuestLogWindow,QuestWorldMarkerRenderer}.cs`
- `Assets/Scripts/World/Buildings/Tasks/BuildingTask.cs` (the abstract IQuest base)
- `Assets/Scripts/World/Buildings/CommercialBuilding.cs` (`PublishQuest`, `GetAvailableQuests`, auto-claim hook)
- `Assets/Scripts/World/Jobs/{BuyOrder,TransportOrder,CraftingOrder}.cs`
