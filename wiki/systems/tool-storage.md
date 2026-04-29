---
type: system
title: "Tool Storage Primitive"
tags: [building, character-job, item, ai, hud, network, save, tier-2]
created: 2026-04-29
updated: 2026-04-29
sources: []
related:
  - "[[commercial-building]]"
  - "[[character-job]]"
  - "[[character-schedule]]"
  - "[[items]]"
  - "[[ai-actions]]"
  - "[[storage-furniture]]"
  - "[[player-ui]]"
  - "[[kevin]]"
status: stable
confidence: high
primary_agent: building-furniture-specialist
secondary_agents:
  - npc-ai-specialist
  - character-system-specialist
owner_code_path: "Assets/Scripts/AI/GOAP/Actions/"
depends_on:
  - "[[commercial-building]]"
  - "[[storage-furniture]]"
  - "[[character-job]]"
  - "[[character-schedule]]"
  - "[[items]]"
depended_on_by:
  - "[[jobs-and-logistics]]"
---

# Tool Storage Primitive

## Summary
A generic role assigned to any existing `StorageFurniture` via the `_toolStorageFurniture` reference on `CommercialBuilding`. Workers fetch tools, use them for a task, return them. Items fetched from a tool storage are stamped with the building's stable `BuildingId` via `ItemInstance.OwnerBuildingId`. The punch-out gate prevents workers (player or NPC) from ending their shift while still carrying a stamped tool — player workers see a UI toast routed through the existing global notification channel.

The primitive is generic and reusable across all worker types. Phase 1 (this rollout) ships it; Plan 3 wires the **Farmer** as the first consumer with a per-task pickup pattern (fetch a watering can, water a cell, return it). Phase 2 (deferred) retrofits Woodcutter / Miner / Forager / Transporter with the **shift-long pickup pattern** (fetch on punch-in, return on punch-out) plus a bag → carry-capacity bonus model.

## Purpose
Adds management gameplay: tool stocking determines parallel work capacity. More watering cans = more parallel waterers; an empty tool storage stalls work and drives a `BuyOrder` for resupply (via the existing logistics chain in [[jobs-and-logistics]]). Without the primitive, "tools" would be implicit / always-available, removing the gameplay tension of "do I buy more axes to scale up the lumber mill?"

## Responsibilities
- Designating a `StorageFurniture` as the tool source for a building (single designer-set field).
- Stamping fetched items with `Building.BuildingId` via `ItemInstance.OwnerBuildingId`.
- Clearing the stamp when the item lands back in its origin storage (GOAP path AND player drop-in path).
- Gating shift-end punch-out via `CharacterJob.CanPunchOut`.
- Notifying player workers of blocked punch-out via a UI toast.
- Auto-returning unreturned tools when a worker quits / is fired / dies.

## Non-responsibilities
- **Does not** manage tool durability or breakage — Phase 2.
- **Does not** model carry-capacity bonuses from bags — Phase 2.
- **Does not** create new furniture types — uses existing `StorageFurniture`.
- **Does not** define which items are "tools" — that's the GOAP action's `toolItem` parameter, set by the calling Job.
- **Does not** auto-stock tool storages — designer pre-stocks at scene authoring; logistics chain via `BuyOrder` (Plan 2 + Plan 3) replenishes.

## Key classes / files

| File | Role |
|---|---|
| [Assets/Scripts/Item/ItemInstance.cs](../../Assets/Scripts/Item/ItemInstance.cs) | `OwnerBuildingId` field |
| [Assets/Scripts/World/Buildings/CommercialBuilding.cs](../../Assets/Scripts/World/Buildings/CommercialBuilding.cs) | `_toolStorageFurniture`, `WorkerCarriesUnreturnedTools`, `NotifyPunchOutBlockedClientRpc` |
| [Assets/Scripts/World/Furniture/StorageFurniture.cs](../../Assets/Scripts/World/Furniture/StorageFurniture.cs) | `AddItem` clears `OwnerBuildingId` on origin match |
| [Assets/Scripts/Character/CharacterJob/CharacterJob.cs](../../Assets/Scripts/Character/CharacterJob/CharacterJob.cs) | `CanPunchOut`, `QuitJob` auto-return |
| [Assets/Scripts/Character/CharacterSchedule/CharacterSchedule.cs](../../Assets/Scripts/Character/CharacterSchedule/CharacterSchedule.cs) | `EvaluateSchedule` Work→non-Work transition gate |
| [Assets/Scripts/AI/GOAP/Actions/GoapAction_FetchToolFromStorage.cs](../../Assets/Scripts/AI/GOAP/Actions/GoapAction_FetchToolFromStorage.cs) | generic fetch |
| [Assets/Scripts/AI/GOAP/Actions/GoapAction_ReturnToolToStorage.cs](../../Assets/Scripts/AI/GOAP/Actions/GoapAction_ReturnToolToStorage.cs) | generic return |
| [Assets/Scripts/UI/PlayerHUD/UI_ToolReturnReminderToast.cs](../../Assets/Scripts/UI/PlayerHUD/UI_ToolReturnReminderToast.cs) | thin wrapper around `UI_Toast` |

## Public API / entry points
See [[tool-storage|SKILL.md]] for full method signatures. Headline:
- `building.ToolStorage` (read-only).
- `building.WorkerCarriesUnreturnedTools(worker, out unreturned)`.
- `worker.CharacterJob.CanPunchOut() : (bool, string)`.
- `new GoapAction_FetchToolFromStorage(building, toolItem)` / `new GoapAction_ReturnToolToStorage(building, toolItem)`.
- `instance.OwnerBuildingId` (read/write, persisted).

## Data flow

```
Worker plan needs tool → GoapAction_FetchToolFromStorage(building, tool)
        ├─ IsValid: hands free + storage has matching tool
        ├─ walk to building.ToolStorage (InteractableObject.IsCharacterInInteractionZone gate)
        ├─ take 1 ItemInstance matching tool from storage
        ├─ stamp instance.OwnerBuildingId = building.BuildingId
        └─ equip in HandsController

Worker uses tool (e.g. CharacterAction_WaterCrop in Plan 3) — no plumbing change here.

Worker plan finishes use → GoapAction_ReturnToolToStorage(building, tool)
        ├─ IsValid: hand carries matching tool with matching OwnerBuildingId, storage not full
        ├─ walk to building.ToolStorage
        ├─ remove from hand, AddItem to storage
        └─ AddItem clears OwnerBuildingId via origin-match hook

Schedule transitions Work → non-Work (CharacterSchedule.EvaluateSchedule):
        ├─ CharacterJob.CanPunchOut() — aggregates unreturned tools across all active workplaces
        ├─ if blocked: stay in Work, fire NotifyPunchOutBlockedToClient (player only, rate-limited 30s real-time)
        └─ else: transition normally

CharacterJob.QuitJob (worker quits / is fired / dies):
        ├─ Workplace.WorkerCarriesUnreturnedTools(worker) → list scoped to leaving workplace only
        ├─ TryAutoReturnTools — drop from hand or remove from inventory, AddItem to storage
        └─ Fallback if storage unreachable / full: clear OwnerBuildingId manually + keep in worker inventory ("salvaged")

Player drop-in path (no GOAP):
        Player walks to chest, drops tool via existing UI
        └─ StorageFurniture.AddItem(item) — origin-match hook clears OwnerBuildingId
```

## Dependencies

### Upstream
- [[commercial-building]] — owns the `_toolStorageFurniture` reference + the `WorkerCarriesUnreturnedTools` helper + the `NotifyPunchOutBlockedClientRpc`.
- [[storage-furniture]] — the actual container; `AddItem` carries the origin-clear hook.
- [[items]] — `ItemInstance.OwnerBuildingId` lives there.
- [[character-job]] — `CanPunchOut` gate caller, `QuitJob` auto-return path.
- [[character-schedule]] — calls `CanPunchOut` on Work→non-Work transitions.

### Downstream
- [[jobs-and-logistics]] — Plan 2 + Plan 3 wire seeds + tools as `IStockProvider` inputs so the logistics chain auto-orders replacements.
- (Future) `JobFarmer` (Plan 3) — first consumer using the per-task pickup pattern for `WateringCan`.
- (Phase 2) `JobHarvester` / `JobTransporter` / `JobBlacksmith` — shift-long pickup retrofit.

## State & persistence

- `ItemInstance.OwnerBuildingId` — string GUID (`Building.BuildingId`), persisted via `JsonUtility` round-trip on the existing inventory + storage save paths. **No new save fields.** Default value on load for pre-existing items: empty string (treated as unowned).
- `_toolStorageFurniture` — designer reference, no runtime mutation, no save.

## Known gotchas / edge cases

- **`OwnerBuildingId` MUST stay `[SerializeField]`** — `JsonUtility` skips plain private fields. Removing the attribute would silently drop the field across every save/load. A `// NOTE:` comment in `ItemInstance.cs` documents this.
- **Tool storage destroyed mid-shift** → gate auto-passes; tool stays salvaged. Cosmetic only — periodic cleanup is Phase 2.
- **Storage full at return** → fallback keeps tool in worker inventory, clears stamp. Worker not gated.
- **Cross-map carry** → marker persists; gate only checks current workplace's BuildingId. Re-entering original map re-blocks correctly.
- **Multi-job workers** → `CanPunchOut` aggregates across all active workplaces; `QuitJob` is scoped to the leaving workplace only.
- **`Building.BuildingId` is the stable GUID** (matches `Building.NetworkBuildingId.Value.ToString()`). Renaming the GameObject does NOT break the marker. Do NOT introduce a parallel ID scheme.

## Open questions / TODO

- **Item.asmdef extraction** (testability follow-up): `ItemInstance` lives in `Assembly-CSharp`. EditMode tests touching it need either reflection scaffolding (used in Task 1) or PlayMode tests. Extracting `Assets/Scripts/Item/Item.asmdef` would let tests reference it directly (mirrors `MWI.Farming.Pure` pattern). Captured but deferred — not blocking Plans 1/2/3.
- **NPC owner GOAP for hiring** (Phase 2 placeholder, see also [[commercial-building]] §15 of the spec): when Plan 2 adds owner-controlled hiring, NPCs that own buildings should have GOAP actions to open/close hiring + edit Help Wanted signs. Reuses the same `TryOpenHiring` / `TryCloseHiring` / `TrySetDisplayText` API.

## Change log

- 2026-04-29 — Initial implementation, Plan 1 of 3 in the Farmer rollout. Tasks 1-9 committed across `506adce8` … `43c855a4`. — claude

## Sources

- [docs/superpowers/specs/2026-04-29-farmer-job-and-tool-storage-design.md](../../docs/superpowers/specs/2026-04-29-farmer-job-and-tool-storage-design.md) §3.4 / §3.5 / §4.3 / §4.5 / §5 / §11.1
- [docs/superpowers/plans/2026-04-29-tool-storage-primitive.md](../../docs/superpowers/plans/2026-04-29-tool-storage-primitive.md)
- [docs/superpowers/smoketests/2026-04-29-tool-storage-primitive-smoketest.md](../../docs/superpowers/smoketests/2026-04-29-tool-storage-primitive-smoketest.md)
- [.agent/skills/tool-storage/SKILL.md](../../.agent/skills/tool-storage/SKILL.md)
- 2026-04-29 conversation with [[kevin]]
