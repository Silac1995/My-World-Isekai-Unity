---
type: system
title: "Quest System"
tags: [quests, character, jobs, ui, hud, network, save, tier-2]
created: 2026-04-23
updated: 2026-04-24
sources:
  - "Assets/Scripts/Quest/IQuest.cs"
  - "Assets/Scripts/Quest/IQuestTarget.cs"
  - "Assets/Scripts/Quest/Targets/HarvestableTarget.cs"
  - "Assets/Scripts/Quest/Targets/WorldItemTarget.cs"
  - "Assets/Scripts/Quest/Targets/ZoneTarget.cs"
  - "Assets/Scripts/Quest/Targets/BuildingTarget.cs"
  - "Assets/Scripts/Quest/Targets/CharacterTarget.cs"
  - "Assets/Scripts/Character/CharacterQuestLog/CharacterQuestLog.cs"
  - "Assets/Scripts/Character/CharacterQuestLog/QuestLogSaveData.cs"
  - "Assets/Scripts/Character/CharacterQuestLog/QuestSnapshotEntry.cs"
  - "Assets/Scripts/UI/Quest/UI_QuestTracker.cs"
  - "Assets/Scripts/UI/Quest/UI_QuestLogWindow.cs"
  - "Assets/Scripts/UI/Quest/QuestWorldMarkerRenderer.cs"
  - "Assets/Scripts/World/Buildings/Tasks/BuildingTask.cs"
  - "Assets/Scripts/World/Buildings/CommercialBuilding.cs"
  - "Assets/Scripts/World/Jobs/BuyOrder.cs"
  - "Assets/Scripts/World/Jobs/TransportOrder.cs"
  - "Assets/Scripts/World/Jobs/CraftingOrder.cs"
  - ".agent/skills/quest-system/SKILL.md"
  - "docs/superpowers/specs/2026-04-23-quest-system-design.md"
  - "docs/superpowers/smoketests/2026-04-23-quest-system-smoketest.md"
related:
  - "[[character]]"
  - "[[character-job]]"
  - "[[commercial-building]]"
  - "[[building-logistics-manager]]"
  - "[[building-task-manager]]"
  - "[[jobs-and-logistics]]"
  - "[[worker-wages-and-performance]]"
  - "[[save-load]]"
  - "[[network]]"
status: stable
confidence: high
primary_agent: building-furniture-specialist
secondary_agents:
  - npc-ai-specialist
  - save-persistence-specialist
owner_code_path: "Assets/Scripts/Quest/"
depends_on:
  - "[[character]]"
  - "[[character-job]]"
  - "[[commercial-building]]"
  - "[[building-logistics-manager]]"
  - "[[building-task-manager]]"
  - "[[jobs-and-logistics]]"
  - "[[save-load]]"
  - "[[network]]"
depended_on_by:
  - "[[jobs-and-logistics]]"
  - "[[worker-wages-and-performance]]"
---

# Quest System

## Summary

A "Quest" in this codebase is the **unified work-instruction primitive** consumed by both players and NPCs. The Hybrid C unification has the existing work-primitive types (`BuildingTask` family + the three order types) implement the `MWI.Quests.IQuest` interface directly — NPC GOAP code (`BuildingTaskManager.ClaimBestTask<T>`) is unchanged because the returned objects just additionally satisfy `IQuest`. A new `CharacterQuestLog` subsystem on every Character holds claimed quest references + denormalized snapshots, syncs via `NetworkList<FixedString64Bytes>` + targeted `[ClientRpc]` snapshot push, and persists via `ICharacterSaveData<QuestLogSaveData>`. Player HUD is wired to the local-player's log only; world markers filter by `OriginMapId == localPlayer.CharacterMapTracker.CurrentMapID`.

## Purpose

Before this system, NPCs had `BuildingTask` for harvest/pickup work and `BuildingLogisticsManager.OrderBook` for procurement orders, but players had no way to participate in the loop and no on-screen tracker telling them what to do. The wage system (shipped 2026-04-22) made player work pay, but the player still had no instructions surface. The Quest system closes that gap with a single primitive that surfaces work to both consumers identically. The architecture is shaped so future producers (bounty board, main story, relationship/event quests) plug into the same `IQuest` data without reshaping consumers.

## Responsibilities

- Define the unified `IQuest` data shape (id, origin map, issuer, contributors, progress, target, lifecycle).
- Define `IQuestTarget` so a single renderer handles every target type uniformly.
- Hold per-character claimed quest snapshots (`CharacterQuestLog`).
- Auto-claim eligible published quests for on-shift workers (`CommercialBuilding.WorkerStartingShift`).
- Render local-player HUD: tracker widget (top-right), log window (L key), world markers (diamond/beacon/zone-fill).
- Persist claimed quests through save/load with map-aware reconciliation (dormant snapshots wake on map return).
- Server-authoritative mutations (TryJoin/TryLeave/RecordProgress/SetFocused) with ServerRpc routing for clients.

**Non-responsibilities:**
- Not responsible for *creating* quests procedurally — producers (TaskManager, OrderBook) are the source.
- Not responsible for the wage/reward layer — wages keep flowing through `WageSystemService` independently.
- Not responsible for narrative scripting / dialogue — main-story producer is future scope.
- Not responsible for player-issued quests (player → NPC explicit issuance) — also future scope.
- Not responsible for hibernated NPC offline quest progress — `MacroSimulator` doesn't yet feed quest progress (mirror of the worker-wages hibernated WorkLog gap).

## Key classes / files

| File | Role |
|---|---|
| [IQuest.cs](../../Assets/Scripts/Quest/IQuest.cs) | Interface + `QuestType` + `QuestState` enums. |
| [IQuestTarget.cs](../../Assets/Scripts/Quest/IQuestTarget.cs) | Target interface (world position, movement target, zone bounds, display name). |
| [HarvestableTarget.cs](../../Assets/Scripts/Quest/Targets/HarvestableTarget.cs) | Wraps a `Harvestable`. Diamond marker. |
| [WorldItemTarget.cs](../../Assets/Scripts/Quest/Targets/WorldItemTarget.cs) | Wraps a `WorldItem`. Diamond marker. |
| [ZoneTarget.cs](../../Assets/Scripts/Quest/Targets/ZoneTarget.cs) | Wraps a `Zone`. Zone-fill + center waypoint. |
| [BuildingTarget.cs](../../Assets/Scripts/Quest/Targets/BuildingTarget.cs) | Wraps a `Building`. Beacon at `DeliveryZone.Bounds.center`. |
| [CharacterTarget.cs](../../Assets/Scripts/Quest/Targets/CharacterTarget.cs) | Wraps a `Character`. Diamond marker over head. |
| [BuildingTask.cs](../../Assets/Scripts/World/Buildings/Tasks/BuildingTask.cs) | Abstract IQuest base (Hybrid C: existing task system unified). |
| [BuyOrder.cs](../../Assets/Scripts/World/Jobs/BuyOrder.cs), [TransportOrder.cs](../../Assets/Scripts/World/Jobs/TransportOrder.cs), [CraftingOrder.cs](../../Assets/Scripts/World/Jobs/CraftingOrder.cs) | The three order types — all implement `IQuest`. |
| [BuildingTaskManager.cs](../../Assets/Scripts/World/Buildings/BuildingTaskManager.cs) | Fires `OnTaskRegistered/Claimed/Unclaimed/Completed` events. |
| [LogisticsOrderBook.cs](../../Assets/Scripts/World/Buildings/Logistics/LogisticsOrderBook.cs) | Fires `OnBuyOrder/Transport/CraftingOrderAdded` + `OnAnyOrderRemoved`. |
| [CommercialBuilding.cs](../../Assets/Scripts/World/Buildings/CommercialBuilding.cs) | `GetAvailableQuests`, `GetQuestById`, `ResolveIssuer`, `PublishQuest`, `OnQuestPublished`/`OnQuestStateChanged`, auto-claim hook on `WorkerStartingShift`. |
| [CharacterQuestLog.cs](../../Assets/Scripts/Character/CharacterQuestLog/CharacterQuestLog.cs) | Subsystem (NetworkBehaviour + ICharacterSaveData). The hub. |
| [QuestSnapshotEntry.cs](../../Assets/Scripts/Character/CharacterQuestLog/QuestSnapshotEntry.cs) | DTO + `INetworkSerializable` for ClientRpc push. |
| [UI_QuestTracker.cs](../../Assets/Scripts/UI/Quest/UI_QuestTracker.cs) | Always-visible HUD widget. |
| [UI_QuestLogWindow.cs](../../Assets/Scripts/UI/Quest/UI_QuestLogWindow.cs) | Full panel (extends `UI_WindowBase`). |
| [QuestWorldMarkerRenderer.cs](../../Assets/Scripts/UI/Quest/QuestWorldMarkerRenderer.cs) | Spawns/despawns marker prefabs per active quest with map-id filter. |

## Public API / entry points

```csharp
// Server-side
foreach (var q in commercialBuilding.GetAvailableQuests()) { ... }
var quest = commercialBuilding.GetQuestById(questId);
commercialBuilding.OnQuestPublished += quest => { ... };

// Per-character
character.CharacterQuestLog.TryClaim(quest);     // routes via ServerRpc on clients
character.CharacterQuestLog.TryAbandon(quest);
character.CharacterQuestLog.SetFocused(quest);
character.CharacterQuestLog.OnQuestAdded += q => { ... };

// Reading state
var active = character.CharacterQuestLog.ActiveQuests;
var focused = character.CharacterQuestLog.FocusedQuest;
var snapshots = character.CharacterQuestLog.Snapshots;
```

## Data flow

```
[Building event]                                                                 │
TaskManager.RegisterTask  /  OrderBook.AddPlacedBuyOrder  /  AddPlacedTransport  /  AddActiveCrafting
   │                                                                             │
   ▼                                                                             │
[Aggregator: CommercialBuilding]                                                 │
PublishQuest(quest)                                                              │
   ├─► stamps Issuer (LogisticsManager Worker > Owner > null)                    │
   ├─► stamps OriginWorldId (empty v1) + OriginMapId (from MapController)        │
   ├─► subscribes to quest.OnStateChanged (forwards as OnQuestStateChanged)      │
   └─► fires OnQuestPublished                                                    │
                          │                                                      │
                          ▼                                                      │
       ┌─────────────── auto-claim on-shift workers ────────────────┐            │
       │                                                              │            │
       ▼                                                              ▼            │
[NPC: ClaimBestTask<T> already running]      [Player: CharacterQuestLog.TryClaim] │
       │                                                              │            │
       │                                                              ▼            │
       │                                              [ClientRpc PushQuestSnapshot]│
       │                                                              │            │
       │                                                              ▼            │
       │                                                     [Local HUD updates]   │
       └─────────────────── progress hooks fire `RecordProgress` ───┴───┐         │
                                                                         │         │
                                                                         ▼         │
                                                  [ClientRpc QuestProgressUpdated]│
```

Server authority: all mutations (`TryJoin`/`TryLeave`/`RecordProgress`/`SetFocused`) execute server-side. Client calls route via ServerRpc → server validates → state replicates.

## Dependencies

### Upstream

- [[character]] — `CharacterQuestLog` is a subsystem on the Character facade.
- [[character-job]] — `CharacterJob.ActiveJobs` powers eligibility checks.
- [[commercial-building]] — publishes quests, auto-claims on punch-in.
- [[building-task-manager]] — fires `OnTaskRegistered/Claimed/Unclaimed/Completed`.
- [[building-logistics-manager]] — `OrderBook.OnXxxAdded/Removed` events.
- [[save-load]] — `ICharacterSaveData<QuestLogSaveData>` + `BuildingManager.FindBuildingById` resolution.
- [[network]] — NetworkList, NetworkVariable, ClientRpc, INetworkSerializable.
- [[jobs-and-logistics]] — the work pipeline; this system unifies its consumers.

### Downstream

- [[jobs-and-logistics]] — bidirectional: hooks live in the logistics primitives.
- [[worker-wages-and-performance]] — orthogonal but co-located on the Character facade. Wage payment continues to flow through `WageSystemService` independently.
- Future quest producers (bounty, main story, relationship/event) will plug in via the same `IQuest` interface.
- Future Quest HUD polish (per-category color, completion history tab).

## State & persistence

| Data | Owner | Persisted? |
|---|---|---|
| Live `IQuest` references | `CharacterQuestLog._liveQuests` (server-only) | Indirect (via building source) |
| Per-quest snapshots | `CharacterQuestLog._snapshots` (server + owning client) | Yes — flattened into `QuestLogSaveData.activeQuests` |
| Dormant snapshots (off-map) | `CharacterQuestLog._dormantSnapshots` | Yes — same list |
| Claimed quest ids | `_claimedQuestIds : NetworkList<FixedString64Bytes>` | No (rebuilt from snapshots on load) |
| Focused quest id | `_focusedQuestId : NetworkVariable<FixedString64Bytes>` | Yes — `QuestLogSaveData.focusedQuestId` |
| Quest mechanical state (progress, contributors) | The producer (BuildingTask, BuyOrder, etc.) | Yes — through existing producer save path |
| HUD state (tracker visible, log open) | `PlayerUI` widgets | No — UI state |

`SaveKey == "CharacterQuestLog"`, `LoadPriority == 70` (after CharacterJob 60 + CharacterWorkLog 65).

`QuestSnapshotEntry` implements `INetworkSerializable` so it can pass through `[ClientRpc]`. **Adding new reference-typed fields requires updating `NetworkSerialize` to handle them.**

## Known gotchas

- **`QuestId` is per-instance auto-Guid** — not stable across server restarts. Saved snapshots may go dormant on reload if the building reconstructs fresh task instances. Future fix: hash-based id from (BuildingId + TaskTypeName + TargetId).
- **`OriginWorldId` is empty in v1** — no source for it yet (`WorldAssociation` singleton not accessible to buildings). Map-id filtering still works.
- **`ResolveQuest` is O(buildings × quests)** — server-side linear scan. Future `QuestRegistry` singleton would make it O(1).
- **HarvestResourceTask.Required is dynamic** — drops as the resource depletes; late joiners see smaller `Required`.
- **Late-joiner snapshot gap** — same v1 limitation as wallet's ClientRpc-on-change. Snapshots only push from join time forward.
- **`OnQuestRemoved` event passes `null` on clients** — clients have no live `IQuest` reference; HUD reads `_snapshots` dict to identify the removed quest.
- **`Issuer` setter is concrete-class-public**, not interface-public — `CommercialBuilding.PublishQuest` casts to the concrete type to stamp it.
- **`HandleClaimedListChanged` must branch on `Remove` AND `RemoveAt`** — `ServerTryAbandon` removes by index (`_claimedQuestIds.RemoveAt(i)`), which fires `NetworkListEvent<T>.EventType.RemoveAt` on the wire, not `EventType.Remove`. Handling only one of the two leaves clients with stale snapshots (HUD tracker / log / world marker stuck after Abandon or punch-out). Fixed 2026-04-24; pattern lifted from `CharacterEquipment.cs:73-74`.
- **Two parallel claim paths on `BuildingTask`** — `BuildingTaskManager.ClaimBestTask<T>` (NPC GOAP) and `IQuest.TryJoin/TryLeave` (player via `CharacterQuestLog`). The latter historically only mutated `ClaimedByWorkers`, leaving the manager's `Available`/`InProgress` buckets stale ("Unknown Worker" rows in the debug HUD; tasks not re-pickable after abandon). Fixed 2026-04-24 with `BuildingTask.Manager` back-ref + `Manager.NotifyTaskExternallyClaimed` / `NotifyTaskExternallyUnclaimed` hooks. See [[building-task-manager]].
- **Hibernated NPCs don't accrue quest progress offline** — same gap as worker-wages WorkLog; `MacroSimulator` would need to feed `RecordProgress` on hibernated quests for offline workers.
- **Abandon on dormant snapshots disabled** in v1 UI — Abandon button greyed because the live source isn't reachable.
- **`JobBlacksmith.Type` (and apprentice) latent bug from wage system was already fixed** — needed for crafter quest eligibility too.

## Open questions / TODO

- [ ] When does the bounty-producer spec land?
- [ ] When does the player → Character explicit issuance UI land? (player-as-LogisticsManager manual dispatch is the most natural follow-on.)
- [ ] Promote `QuestId` to a stable hash so saved snapshots survive server restarts.
- [ ] Source `OriginWorldId` from the active `WorldAssociation` singleton.
- [ ] Add a `QuestRegistry` singleton for O(1) cross-building lookup.
- [ ] Multi-stage Quests (`Quest.Parent` grouping) for narrative arcs.
- [ ] Hibernated NPC quest progress accrual (mirror of worker-wages WorkLog gap).
- [ ] Per-category color theming in HUD (Job=gold, Bounty=red, Main=blue, Event=pink).
- [ ] Quest cooldown after abandon (anti-griefing if abuse appears).
- [ ] `IQuestTarget.IsVisibleToPlayer` becomes meaningful (fog-of-war / hidden quests).
- [ ] Manual dispatch UI for player-as-LogisticsManager.

## Change log

- 2026-04-24 — Fixed two multiplayer regressions: (1) client snapshot removal on Abandon / punch-out — `HandleClaimedListChanged` now handles `RemoveAt` (and `Clear`) in addition to `Remove`; (2) `BuildingTaskManager` `Available`/`InProgress` buckets desyncing from `IQuest.TryJoin/TryLeave` — `BuildingTask.Manager` back-ref + `NotifyTaskExternally{Claimed,Unclaimed}` hooks. Diagnostic `_verboseLogs` flag added on `QuestWorldMarkerRenderer` for future marker triage. — claude
- 2026-04-23 — Initial implementation. Tasks 1-21 (code) + Task 28 (smoke test) + Tasks 29-33 (docs) per `docs/superpowers/plans/2026-04-23-quest-system.md`. — claude

## Sources

- [docs/superpowers/specs/2026-04-23-quest-system-design.md](../../docs/superpowers/specs/2026-04-23-quest-system-design.md) — full design spec.
- [docs/superpowers/plans/2026-04-23-quest-system.md](../../docs/superpowers/plans/2026-04-23-quest-system.md) — implementation plan.
- [docs/superpowers/smoketests/2026-04-23-quest-system-smoketest.md](../../docs/superpowers/smoketests/2026-04-23-quest-system-smoketest.md) — manual Play Mode tests.
- [.agent/skills/quest-system/SKILL.md](../../.agent/skills/quest-system/SKILL.md) — procedural docs.
- All source files listed in the frontmatter `sources:` block.
