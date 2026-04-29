# Tool Storage System

Generic, reusable primitive that designates any existing `StorageFurniture` as a building's "tool storage." Workers (player or NPC) fetch tools, use them for a task, return them. The punch-out gate prevents shift end while a worker still carries a stamped tool.

## Public API

### `CommercialBuilding`
```csharp
StorageFurniture ToolStorage         // designer-set reference, may be null
bool HasToolStorage                  // convenience predicate

// Server-authoritative scan: walks worker's hand + inventory for items whose
// OwnerBuildingId == this building's BuildingId. Always allocates a fresh List.
bool WorkerCarriesUnreturnedTools(Character worker, out List<ItemInstance> unreturned)

// Server-side wrapper that fires a targeted ClientRpc to the named owner client.
// Receiver shows the tool-return toast.
void NotifyPunchOutBlockedToClient(string reason, ulong targetClientId)
```

### `ItemInstance`
```csharp
string OwnerBuildingId { get; set; }   // empty string = unowned. Persisted via JsonUtility.
```

### `CharacterJob`
```csharp
// Read-only check. Returns (true, null) if no workplace OR no unreturned tools.
// Iterates ALL active workplaces (multi-job workers) and aggregates unreturned tools
// across them into a single reason string.
(bool canPunchOut, string reasonIfBlocked) CanPunchOut()
```

`QuitJob(Job)` automatically attempts to return tools owned by the leaving workplace before clearing the assignment. If storage is unreachable / full / destroyed, `OwnerBuildingId` is cleared manually so the worker isn't permanently gated; the item stays in their inventory ("salvaged").

### GOAP actions (generic, ItemSO-parameterised)
```csharp
new GoapAction_FetchToolFromStorage(building, toolItem)
new GoapAction_ReturnToolToStorage(building, toolItem)
```
Cost = 1 each. Companion pair — same Preconditions/Effects key shape (`hasToolInHand_{itemSO.name}`, `toolNeededForTask_{itemSO.name}`, `taskCompleteForTool_{itemSO.name}`, `toolReturned_{itemSO.name}`) so the planner can chain them in any worker plan that needs a building-owned tool.

`IsValid` gates:
- **Fetch:** worker hands free, building has ToolStorage, storage contains a matching ItemSO.
- **Return:** worker hand carries an instance with matching `ItemSO` AND `OwnerBuildingId == building.BuildingId`, storage is non-null and not full.

## Integration points

- **`StorageFurniture.AddItem(ItemInstance)`** auto-clears `OwnerBuildingId` when the destination storage matches the item's origin building. Covers BOTH the GOAP return path AND the player drop-in path (player walks to the chest, drops a tool via the existing player UI — no GOAP involved).
- **`CharacterSchedule.EvaluateSchedule`** calls `CharacterJob.CanPunchOut` on every Work→non-Work transition. If blocked, the activity stays in `Work`; for player-owned workers, fires `NotifyPunchOutBlockedToClient` (rate-limited to once per 30s real-time, `Time.unscaledTime` per rule #26). NPCs replan via GOAP — no toast.
- **`CharacterJob.QuitJob`** runs `TryAutoReturnTools(workplace, list)` BEFORE final removal of the assignment.
- **`UI_ToolReturnReminderToast`** is a thin wrapper around the existing `MWI.UI.Notifications.UI_Toast` channel — Title "Return tools", Warning severity, 4s real-time duration.

## Events

None. The primitive is callback-free; downstream systems poll `CanPunchOut` / read `OwnerBuildingId` / call the GOAP actions directly.

## Dependencies

- `StorageFurniture` (existing) — the actual container.
- `ItemInstance` / `ItemSO` (existing) — `OwnerBuildingId` lives on ItemInstance.
- `HandsController` (existing) — for hand inspection + `DropCarriedItem` / `CarryItemInHand`.
- `Building.BuildingId` (existing) — the stable GUID. **Never introduce a parallel ID scheme.**

## Gotchas

- **`OwnerBuildingId` persists across save/load** via existing `JsonUtility` serialisation on `ItemInstance`. No migration needed for old saves (default empty string). Field MUST stay `[SerializeField]` — `JsonUtility` skips plain private fields.
- **Tool storage destroyed mid-shift** → `CanPunchOut` auto-passes after one log warning; tool stays in worker inventory with stale `OwnerBuildingId`. v1 leaves the stale value (cosmetic only); a periodic cleanup is Phase 2.
- **Storage full at return-time** → fallback puts tool back in worker inventory + clears `OwnerBuildingId` so the worker isn't permanently gated.
- **Cross-map carry** (player goes through portal mid-shift) → `OwnerBuildingId` persists on the item, but the gate only checks the *current workplace's* BuildingId. Crossing back triggers the gate correctly.
- **Multi-job workers** — `CanPunchOut` aggregates unreturned tools across ALL active workplaces. `QuitJob` for one workplace only auto-returns tools owned by THAT workplace; tools from other concurrent workplaces remain stamped + carried.
- **NPC GOAP planner expectations** — the per-task pattern (FetchTool → DoTask → ReturnTool) is the v1 consumption shape (used by JobFarmer in Plan 3). Phase 2 adds `Job.OnShiftStart` / `OnShiftEnd` lifecycle hooks for the shift-long pickup pattern (Woodcutter axe, Transporter bag, etc.) — same primitive.

## See also

- Spec: [docs/superpowers/specs/2026-04-29-farmer-job-and-tool-storage-design.md](../../docs/superpowers/specs/2026-04-29-farmer-job-and-tool-storage-design.md) §3.4 / §3.5 / §4.3 / §4.5 / §5 / §11.1
- Plan: [docs/superpowers/plans/2026-04-29-tool-storage-primitive.md](../../docs/superpowers/plans/2026-04-29-tool-storage-primitive.md)
- Smoketest: [docs/superpowers/smoketests/2026-04-29-tool-storage-primitive-smoketest.md](../../docs/superpowers/smoketests/2026-04-29-tool-storage-primitive-smoketest.md)
- Wiki page: [wiki/systems/tool-storage.md](../../wiki/systems/tool-storage.md)

## Follow-ups

1. **Item.asmdef extraction** — `ItemInstance` lives in `Assembly-CSharp` today, which means future EditMode tests touching `ItemInstance` need either reflection scaffolding (Task 1's pattern) or a PlayMode test runner. Extracting `Assets/Scripts/Item/Item.asmdef` (or `MWI.Item.Pure` mirroring `MWI.Farming.Pure`) would let downstream tests reference `ItemInstance` directly. Captured but deferred — not blocking Plan 1 / Plan 2 / Plan 3.
2. **Phase 2 retrofit** — wire Woodcutter / Miner / Forager / Transporter onto this primitive with the **shift-long pickup pattern** (fetch on punch-in, return on punch-out). Adds `Job.OnShiftStart` / `OnShiftEnd` lifecycle hooks. Bag → carry-capacity bonus model. Optional tool durability.
