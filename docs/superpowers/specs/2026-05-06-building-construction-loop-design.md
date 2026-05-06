# Building Construction Loop — Phase 1 Design

**Date:** 2026-05-06
**Status:** Spec — awaiting plan
**Scope:** Single-owner construction loop. Multiplayer + persistence safe.
**Out of scope:** Community-manager console furniture, `JobBuilder` GOAP, NPC-owner free-time autonomy. (Phase 2.)

## Problem

`BuildingPlacementManager` currently has two paths: instant-build (debug) and a "real" path that spawns the building already in `UnderConstruction` state but with no gameplay loop attached — `Building.ContributeMaterial` is a code-only entry point nothing calls, and `CheckConstructionCompletion` would silently auto-flip the state if it ever did.

We want to deliver an actual construction loop:

1. The character places a **scaffolding-visual** building.
2. The owner delivers required items by dropping them onto the **building footprint**.
3. The owner runs a **continuous, ticked, cancelable** Construct action that consumes items and advances progress.
4. On completion, the visual swaps to the finished building and any leftover items are evicted to the perimeter.
5. The whole loop is server-authoritative, host/client/late-join safe, and persists through save/hibernation.

## Goals

- Distinct **construction-site visual** (scaffolding) on the same prefab as the completed building (no separate `NetworkObject` lifecycle).
- **Players drop items on the footprint** — matches gameplay instinct; the operational `_deliveryZone` keeps its post-build logistics role.
- **Continuous Construct action**: per-tick consumption, cancel on movement / combat / damage / order change / hibernation. Theft remains possible — items the owner has already consumed are gone, items still on the ground are still stealable.
- **Skill hook seated**: per-tick consumption rate is parameterized via a builder-skill formula so a future `BuilderSkill` system can plug in without changing the action.
- **Networking**: server-authoritative state and progress; late-join self-healing; no client-side reconstruction RPCs.
- **Persistence**: state, progress, delivered counts, items in zone all survive save/load and map hibernation.

## Non-Goals (Phase 1)

- NPC-owner autonomous delivery (free-time GOAP goal, perception "find harvestable producing item X", shop search). Phase 2.
- Community-manager city-management console furniture issuing builds. Phase 2.
- A dedicated `JobBuilder` job class. Phase 2.
- Multi-owner / co-owner support. Phase 2.
- Auto-eviction of orphaned construction sites whose owner deleted their profile. Future.
- Offline (hibernation) construction progress. Intentionally not modeled — construction needs a live map.

---

## Architecture

### Component map

```
Building (existing)
 ├─ _currentState : NetworkVariable<BuildingState>            [existing]
 ├─ _constructionRequirements : List<CraftingIngredient>      [existing]
 ├─ _deliveryZone : Zone                                      [existing — operational logistics]
 ├─ _buildingZone : Collider                                  [existing — footprint + construction drop area]
 ├─ _constructionVisualRoot : GameObject                      [NEW — scaffolding renderers]
 ├─ _completedVisualRoot    : GameObject                      [NEW — final renderers]
 ├─ ConstructionProgress : NetworkVariable<float>             [NEW]
 ├─ DeliveredMaterials   : NetworkList<DeliveredMaterialEntry>[NEW]
 └─ HandleStateChanged → toggles which visual root is active

ConstructionSiteScanner (NEW, server-only sub-component on Building)
 - 2 Hz tick gated on IsServer + IsUnderConstruction
 - Scans GetPhysicalItemsInCollider(_buildingZone), buckets by ItemSO
 - Writes ConstructionProgress + DeliveredMaterials NetworkVariables
 - Does NOT consume items — purely observational

BuildingInteractable (NEW, MonoBehaviour on Building root)
 - IInteractable surface for player click + future NPC GOAP
 - Phase 1 actions: { FinishConstruction (when IsUnderConstruction, owner-only) }
 - Stub seats for Phase 2: Abandon, Sell, OpenInterior

CharacterAction_Continuous (NEW abstract base)
 - Sibling of CharacterAction; condition-terminated, no fixed Duration
 - Server-ticked at TickIntervalSeconds; OnTick() returns true to finish
 - AllowsMovementDuringAction = false → cancels on player movement / NPC re-route

CharacterAction_FinishConstruction (NEW concrete action)
 - Inherits CharacterAction_Continuous (TickIntervalSeconds = 1f default)
 - Consumes per-tick from _buildingZone; sets state = Complete when progress hits 1
 - On Complete: triggers leftover-eviction + default-furniture spawn

BuildingPlacementManager (modified)
 - Defers default-furniture spawn until Complete
 - _isInstantMode preserved for debug

BuildingSaveData (modified)
 - Adds ConstructionProgress + DeliveredMaterials snapshot for wake-pre-warm

BuildingInspectorView (Dev tool, Rule #28 update)
 - Shows live progress, delivered breakdown, owner display name, "Force Finish" button
```

### Authority model

- **Server-authoritative writes:** `_currentState`, `ConstructionProgress`, `DeliveredMaterials`, all item despawns, all state-flip side-effects.
- **Client reads only:** every networked value flows via `NetworkVariable` / `NetworkList` (Read=Everyone).
- **Server-only state:** scanner tick, `_contributedMaterials` ledger (never replicated; clients use `DeliveredMaterials` instead), `TrySpawnDefaultFurniture`, leftover eviction.

### Visual swap (Option A)

The same prefab carries two child GameObjects: `_constructionVisualRoot` and `_completedVisualRoot`. `Building.HandleStateChanged` runs on every peer (subscribed to `_currentState.OnValueChanged`) and toggles `SetActive` on each subtree. Single `NetworkObject`, no respawn churn, persistent `BuildingId` across the lifecycle.

Both visual roots ship with whatever colliders/renderers/animations the designer authors. The construction visual is expected to be an open scaffold — no obstacle layer collider blocks pedestrian traffic onto the footprint, so the owner can walk in and drop items.

---

## Data Flow & Lifecycle

### 1. Placement (modified)

```
Player click → BuildingPlacementManager.RequestPlacementServerRpc
  → server Instantiate(prefab) + NetworkObject.Spawn()
  → Building.Awake: _constructionRequirements.Count > 0 → state = UnderConstruction
  → HandleStateChanged(UnderConstruction) on every peer:
      _constructionVisualRoot.SetActive(true)
      _completedVisualRoot.SetActive(false)
  → Default furniture spawn DEFERRED (TrySpawnDefaultFurniture gated on Complete)
  → MapController registration + permit consumption (unchanged)
```

If the prefab has no `_constructionRequirements`, `Awake` flips to `Complete` immediately and `TrySpawnDefaultFurniture` runs as today — no behaviour change for instant-build prefabs.

### 2. Delivery (additive)

```
Owner walks up, drops item from inventory onto footprint
  → existing CharacterAction_DropItem creates a WorldItem
  → WorldItem settles inside _buildingZone bounds
  → ConstructionSiteScanner ticks (2 Hz):
      items = Building.GetPhysicalItemsInCollider(_buildingZone)
      buckets[item.ItemSO] += item.Amount
      For each requirement: delivered = min(buckets[req.Item], req.Amount)
      progress = sum(delivered) / sum(required)
      If changed: write ConstructionProgress + DeliveredMaterials NetworkVariables
  → Clients see meter update via OnValueChanged
```

The scanner is purely observational — no consumption. It exists so the owner can see meter feedback while delivering, *before* engaging the action.

### 3. Construct action (new — continuous)

The action terminates on a condition (state hits Complete), not a timer. Player input that produces movement intent cancels via the existing `AllowsMovementDuringAction = false` controller path.

```
Owner targets construction site → BuildingInteractable.OnInteract → "Finish Construction"
  → PlayerController.QueueAction(new CharacterAction_FinishConstruction(...))
  → action.RequestStartServerRpc(buildingNetworkId)

Server-side OnStart:
  Validate: state == UnderConstruction
            && actor.CharacterId == building.PlacedByCharacterId
            && actor inside _buildingZone bounds
  If invalid → Finish() immediately, no consumption.

Server-side OnTick (every TickIntervalSeconds):
  // Skill-modulated batch size — defaults to 1, plug-in via TryGetSkill
  consumeBudget = 1 + actor.GetSkillLevelOrZero(SkillId.Builder) / N

  totalConsumedThisTick = 0
  For each pending requirement (deterministic order):
    needed = required[i] - delivered[i]
    if (needed <= 0) continue
    take = min(needed, consumeBudget)

    fromZone = ConsumeFromZone(_buildingZone, req.Item, take)
    fromInv  = ConsumeFromActorInventory(actor, req.Item, take - fromZone)
    consumed = fromZone + fromInv

    delivered[i] += consumed
    totalConsumedThisTick += consumed
    consumeBudget -= consumed
    if (consumeBudget <= 0) break

  Recompute progress; update NetworkVariables.

  if (progress >= 1f):
    Finalize()                      // see step 4
    return true                     // action ends

  if (totalConsumedThisTick == 0):
    stallTicks++
    if (stallTicks >= MaxStallTicks):
      return true                   // graceful exit; meter preserved
  else:
    stallTicks = 0

  return false                      // keep ticking

OnCancel (movement / combat / damage / order change / hibernation):
  Already-consumed items stay consumed. Progress + state preserved.
  Owner can re-engage by re-targeting the site.
```

`ConsumeFromZone` despawns matching `WorldItem`s in priority order (closest to actor first). `ConsumeFromActorInventory` is a bonus path so owners who carry items in inventory don't have to drop-then-consume.

### 4. Finalize (server, called from OnTick when progress hits 1)

```
Finalize():
  _currentState.Value = Complete       // atomic state flip; replicates via NetworkVariable

  HandleStateChanged(Complete) on every peer:
    _constructionVisualRoot.SetActive(false)
    _completedVisualRoot.SetActive(true)

  // Server-only post-flip steps (each wrapped in try/catch — Rule #31):
  TrySpawnDefaultFurniture()           // already exists; was gated on Complete
  EvictLeftoversToPerimeter()          // see below
  OnConstructionComplete?.Invoke()     // existing event
```

#### EvictLeftoversToPerimeter

```
leftovers = GetPhysicalItemsInCollider(_buildingZone)
foreach (item in leftovers):
  if (item == null || item.IsBeingCarried) continue
  ejectPoint = NearestPerimeterPoint(_buildingZone, item.transform.position)
              + outwardNormal * 0.5f          // small clearance margin
  if (NavMesh.SamplePosition(ejectPoint, out hit, 2f, AllAreas))
    item.transform.position = hit.position
  else
    item.transform.position = ejectPoint     // free-fall fallback
```

`WorldItem`'s existing `NetworkTransform` replicates the move. Each item's eject is wrapped in `try/catch` so a single eject failure can't block the rest.

### 5. Hibernation (existing path, unchanged in behaviour)

- Scanner stops when no player on the map (server gate already handled by `IsServer + active map` predicate).
- Building state + progress + delivered snapshot serialize via `BuildingSaveData`.
- `WorldItem`s in `_buildingZone` persist via the existing world-item save path (no new code).
- On wake: state restores → `HandleStateChanged` fires → correct visual; first scanner tick reconciles meter against actual physical items present.

No offline catch-up math — construction is a live-map activity by design.

---

## Networking

### NetworkVariable layout

```csharp
public NetworkVariable<float> ConstructionProgress = new NetworkVariable<float>(
    0f, Read=Everyone, Write=Server);

public NetworkList<DeliveredMaterialEntry> DeliveredMaterials = new NetworkList<...>();

[System.Serializable]
public struct DeliveredMaterialEntry : INetworkSerializable
{
    public int RequirementIndex;   // index into _constructionRequirements (compact)
    public int Delivered;          // current amount in zone, clamped to required
}
```

- `ConstructionProgress` updates only when `|new - old| > 0.001f` to avoid idle traffic.
- `DeliveredMaterials` writes only when a bucket actually changes.

### RPC path

- Placement: existing `BuildingPlacementManager.RequestPlacementServerRpc`. No change.
- Delivery: existing `CharacterAction_DropItem`. No change.
- Construct action: `CharacterAction_FinishConstruction.RequestStartServerRpc(buildingNetworkId)`. Server validates ownership + state on every tick; `OnTick` runs server-side; state-flip auto-replicates via `NetworkVariable`.

### Late-join

A client joining mid-construction sees:
- Building `NetworkObject` auto-spawn → `Awake` → `HandleStateChanged(UnderConstruction)` fires locally from the initial `_currentState` payload → visual swaps correctly.
- `ConstructionProgress` + `DeliveredMaterials` flow as part of the spawn payload → meter renders on first frame.
- `WorldItem`s in `_buildingZone` replicate via standard NGO.

No client-side reconstruction RPCs are required.

### Host/Client/NPC matrix (Rule #19)

| Path | Result |
|------|--------|
| Host places building | State replicates via NetworkVariable |
| Client places building | ServerRpc, server spawns + replicates |
| Host drops item | WorldItem spawns server-side, replicates |
| Client drops item | Existing CharacterAction_DropItem path |
| Host clicks Finish | ServerRpc, server runs continuous action |
| Client clicks Finish | ServerRpc, server validates owner, runs action |
| Non-owner clicks Finish | IsValid false → silent no-op |
| Two clients race Finish | Server processes serially; second is no-op |
| NPC drops item (Phase 2) | Same CharacterAction path |
| NPC finishes (Phase 2) | Same CharacterAction path |

### Crash safety

`Finalize` orders the state-flip *before* the leftover-eviction loop. If the server crashes between the state-flip and the eject loop, the building remains `Complete` on next load with a few items still on the footprint — harmless. There is never a "paid but no building" failure mode.

---

## Persistence

### `BuildingSaveData` extension

```csharp
public class BuildingSaveData
{
    // ...existing fields (BuildingId, PrefabId, Position, Rotation, State, etc.)
    public float ConstructionProgress;                           // NEW
    public List<DeliveredMaterialEntryDTO> DeliveredMaterials;   // NEW
}

[System.Serializable]
public class DeliveredMaterialEntryDTO
{
    public string ItemAssetGuid;   // ItemSO ref by AssetGuid
    public int Delivered;
}
```

`DeliveredMaterials` is referenced by `AssetGuid` (not requirement index) so the snapshot survives a designer-time edit to the prefab's `_constructionRequirements` order.

`WorldItem`s in the footprint persist via the existing world-item save pipeline. The `DeliveredMaterials` snapshot is a UX pre-warm so the meter doesn't blink to 0 between map-wake and the next scanner tick.

### Reload flow

```
MapController.WakeUp / SpawnSavedBuildings:
  Building restored from BuildingSaveData
  state, ConstructionProgress, DeliveredMaterials all pre-set from save
  HandleStateChanged fires → correct visual
  ConstructionSiteScanner first tick reconciles snapshot against physical items
```

The snapshot is "best-known previous state"; the next scanner tick is the source of truth and overwrites it.

### Player profile (`ICharacterSaveData`)

No new typed save data on the character side. Ownership lives on the building (`PlacedByCharacterId`) and round-trips through `BuildingSaveData`. Rule #20 satisfied: nothing required for portable character profiles.

---

## Edge Cases

### Owner lifecycle

- Owner deletes profile / never returns → building stays `UnderConstruction` indefinitely. Phase 2 (community manager) introduces the transfer path.
- Owner incapacitated mid-action → `CharacterCombat.HandleIncapacitated` cancels via existing hook. No items consumed.
- Owner enters combat mid-action → existing combat-gate cancels.
- Owner walks off footprint mid-action → `IsValid()` returns false on next server tick → action aborts cleanly.

### Items lifecycle

- Item destroyed by world event → next scanner tick recomputes; meter self-heals.
- Item picked up by another character mid-tick → caught at two layers: (a) `IsValid()` server tick before consume, (b) defensive re-scan inside the consume block (Rule #31). If requirements no longer satisfiable at the consume moment, that tick consumes 0 and the stall counter advances.
- Owner over-delivers → only `min(delivered, required)` counts; extras stay on the footprint until eviction at completion.
- Wrong item type (not in `_constructionRequirements`) → ignored by scanner; sits on footprint until owner picks it back up.

### Theft dynamics (preserved by design)

`WorldItem`s in `_buildingZone` remain regular interactable items — anyone can pick them up at any moment, including thieves. The continuous-tick model only changes the *granularity* of the loss:

- **Theft from empty site (owner not present):** thief takes everything still on the ground. Unchanged.
- **Theft during build (owner present and acting):** thief and owner race. Each tick the owner converts items into permanent progress (no longer stealable); each second the thief delays gives the owner another tick. `BuilderSkill` raising `consumeBudget` lets the owner win the race faster.
- **Stall after complete drain:** action stalls for `MaxStallTicks` (~5s) and exits. Already-locked progress is **not** refunded. Owner re-delivers and re-engages.

### Building lifecycle

- Player places + immediately quits → server-spawned building persists; on reload state is `UnderConstruction`, progress 0.
- Building destroyed mid-build (combat / dev tool) → standard `NetworkObject.Despawn`; items in zone remain (independent NOs); save row removed via existing `BuildingManager.UnregisterBuilding`.

### Networking races

- Two clients press Finish same instant → server processes serially; first wins, second is silent no-op.
- Map hibernates while action running → action cancels; no items consumed; resume on next wake.
- Server crash mid-Finalize → state-flip ordering ensures never "paid but no building" (see Networking § Crash safety).

### Defensive logging (Rule #27)

Verbose-gated `Debug.Log` at every state-flipping branch:

```
[Building.Construction] state==X, progress==Y, owner==Z, actor==W
```

Specifically at:
- `ConstructionSiteScanner` tick — gated on `NPCDebug.VerboseConstruction` (NEW toggle).
- `CharacterAction_FinishConstruction.IsValid()` each branch.
- `Finalize()` consume + state-flip + evict.
- `HandleStateChanged()` callback.

All gated to avoid the host-progressive-freeze pattern (Rule #34).

### Performance budget (Rule #34)

- `GetPhysicalItemsInCollider` migrates to `OverlapBoxNonAlloc` with a reused `Collider[]` buffer (small refactor of the existing `GetPhysicalItemsInZone` path).
- Bucket dictionary cached as a field; cleared and reused per tick.
- Scanner skips entire tick when `state != UnderConstruction`.
- No LINQ in `OnTick` or scanner tick (Rule #34 — hot path).
- 2 Hz scanner × O(n_items_in_zone) per under-construction building. Bounded; profiler-checked at 10 simultaneous sites in Section 5 testing.

---

## Builder Skill Hook (seated, deferred)

The per-tick consume budget is parameterized:

```csharp
int consumeBudget = 1 + (actor.GetSkillLevelOrZero(SkillId.Builder) / N);
```

`GetSkillLevelOrZero` is a stub on `Character` that returns 0 in Phase 1, so the formula reduces to `consumeBudget = 1` (one unit per tick) — identical for every actor. `N` is unspecified in Phase 1 because it has no observable effect; it becomes a tunable when the actual builder skill system lands. An optional second knob ("reduce required count" multiplier) is also seated for that phase. The action signature does not change.

---

## File Changes Summary

### New files

- `Assets/Scripts/Character/CharacterActions/CharacterAction_Continuous.cs` — abstract base.
- `Assets/Scripts/Character/CharacterActions/CharacterAction_FinishConstruction.cs` — concrete action.
- `Assets/Scripts/World/Buildings/Construction/ConstructionSiteScanner.cs` — server-side 2 Hz scanner sub-component.
- `Assets/Scripts/World/Buildings/Construction/BuildingInteractable.cs` — interactable surface for player + future NPC.
- `Assets/Scripts/World/Buildings/Construction/DeliveredMaterialEntry.cs` — `INetworkSerializable` struct.

### Modified files

- `Assets/Scripts/World/Buildings/Building.cs`:
  - Add `_constructionVisualRoot`, `_completedVisualRoot` SerializeField.
  - Add `ConstructionProgress` NetworkVariable + `DeliveredMaterials` NetworkList.
  - Refactor `GetPhysicalItemsInZone(Zone)` → expose sibling `GetPhysicalItemsInCollider(Collider)` so the scanner can pass `_buildingZone` directly.
  - Wire `HandleStateChanged` to toggle visual roots.
  - Defer `TrySpawnDefaultFurniture` until `Complete`.
  - Add `Finalize()` server-side method (called from action on progress == 1).
  - Add `EvictLeftoversToPerimeter()` server-side method.
  - Convert `GetPendingMaterials` callsites to use the new `DeliveredMaterials` (server) — pure refactor.
- `Assets/Scripts/World/Buildings/BuildingPlacementManager.cs`:
  - No structural change; just ensure deferred-furniture path lines up.
- `Assets/Scripts/World/MapSystem/BuildingSaveData.cs`:
  - Add `ConstructionProgress` + `DeliveredMaterials` fields.
  - Update `FromBuilding` / `Restore` to round-trip them.
- `Assets/Scripts/Character/CharacterControllers/PlayerController.cs`:
  - Wire input path that targets a `BuildingInteractable` and queues `CharacterAction_FinishConstruction` (Rule #33).
- `Assets/Scripts/Character/Character.cs`:
  - Add `GetSkillLevelOrZero(SkillId)` stub returning 0 — pluggable for future builder skill.
- `Assets/Scripts/Debug/DevMode/Inspect/BuildingInspectorView.cs`:
  - Surface live progress, delivered breakdown, owner, "Force Finish" dev button (Rule #28).

### Documentation

- `.agent/skills/building/SKILL.md` — update for new construction loop.
- `.agent/skills/character-actions/SKILL.md` — document `CharacterAction_Continuous` base.
- `wiki/systems/building.md` — bump `updated:` date, add change-log entry, refresh public API section.
- `wiki/systems/character-actions.md` — same.
- `.claude/agents/building-furniture-specialist.md` — extend to cover the construction loop.
- `.claude/agents/character-system-specialist.md` — extend to cover `CharacterAction_Continuous`.

---

## Testing Approach

### Unit (EditMode)

- `Building.GetPendingMaterials` empty / partial / over-delivered / multi-type.
- Progress formula: 0 / partial / exact / over-delivered / multi-type — clamped sum.
- `DeliveredMaterialEntry` (de)serialization round-trip.
- `BuildingSaveData` round-trip with the new fields.
- `NearestPerimeterPoint` geometry — items inside / on edge / off-axis.
- Builder-skill formula stub — level 0 / level N / cap.

### PlayMode — Solo

- Place a building → verify `UnderConstruction` state, scaffold visual active, no default furniture.
- Drop logs into footprint → meter ticks up at 2 Hz; replicates locally; per-type counts match.
- Pick up a delivered log → meter drops accordingly.
- Start `CharacterAction_FinishConstruction`:
  - (a) empty zone → action stalls 5 ticks → exits gracefully.
  - (b) full zone → action consumes per-tick, despawns matching `WorldItem`s, completes.
  - (c) player walks (WASD) mid-action → cancels via `AllowsMovementDuringAction`.
  - (d) player takes damage mid-action → cancels via combat gate.
- Completion: visual swap, default furniture spawns, leftover items eject to perimeter, building enters operational state.

### PlayMode — Multiplayer (Rule #19 matrix)

- **Host places, Client joins:** client sees scaffold visual, correct progress, drops logs that count, host clicks Finish → visual swap + furniture + eviction visible to client.
- **Client places, Host watches:** ServerRpc spawns building; both sides correct; only owner can finalize.
- **Client A places, Client B drops items:** B's logs count toward A's meter; B clicks Finish → blocked (not owner); A succeeds.
- **Late join (Client C joins mid-build):** sees current state, progress, delivered list, scaffold visual.
- **NPC parity stub:** spawn an NPC, queue `CharacterAction_FinishConstruction` via dev tool, identical execution path.

### Persistence

- Save mid-build → reload → state, progress, delivered counts, items in zone all restored. Re-engage and finish.
- Save mid-`FinishConstruction` action → reload → action did not persist (correct; transient). Re-engage manually.
- Hibernate mid-build → catch-up no-op. Re-enter map → state resumes exactly as left.

### Defensive / softlock guards

- Action with `Complete` state → IsValid false → exits same frame.
- Actor outside `_buildingZone` → IsValid false → exits same frame.
- Non-owner action queue → IsValid false → exits same frame.
- Two clients race Finish → second is silent no-op.
- Server crash mid-`Finalize` → never "paid but no building" (state-flip-first ordering).
- `WorldItem` despawn fails mid-consume → log + abort consume cycle, retain locked credits.

### Dev tools

- `BuildingInspectorView` shows: state, progress (live), delivered breakdown, pending breakdown, owner display name, "Force Finish" button.
- `DevModeManager` spawn path with `_isInstantMode = true` bypasses the whole loop.

### Profiler checkpoints (Rule #34)

- 10 simultaneous under-construction buildings on one map → scanner cost < 0.1 ms/frame (2 Hz × 10 × `OverlapBoxNonAlloc` reused buffer).
- GC.Alloc per scanner tick = 0 (verify with Allocation Tracking).
- NetworkVariable update bandwidth: only on actual change (verify in NGO profiler that idle buildings emit no traffic).

---

## Open Questions

None blocking. Phase 2 will resolve:

- Multi-owner / co-owner semantics (community manager).
- NPC owner perception query for "harvestable producing item X".
- NPC owner shop-search for "shop selling item X with price ≤ wallet".
- Auto-eviction policy for orphaned construction sites whose owner deleted their profile.
