---
type: gotcha
title: "FurnitureManager.LoadExistingFurniture must be additive, never replace-style"
tags: [building, furniture, network, ngo, registration-race]
created: 2026-04-25
updated: 2026-04-25
sources:
  - "Assets/Scripts/World/Buildings/FurnitureManager.cs"
  - "Assets/Scripts/World/Buildings/Rooms/Room.cs"
  - "Assets/Scripts/World/Buildings/CommercialBuilding.cs"
  - "2026-04-25 conversation with Kevin — Forge default-furniture wiped after placement"
related:
  - "[[building]]"
  - "[[commercial-building]]"
  - "[[furniture-grid]]"
  - "[[crafting-loop]]"
  - "[[network]]"
status: resolved
confidence: high
---

# FurnitureManager.LoadExistingFurniture must be additive, never replace-style

## Summary
`FurnitureManager._furnitures` is the canonical room-membership list — it's what `Room.GetFurniture*`, `ComplexRoom.GetFurnitureOfType<T>`, and every consumer downstream of them reads. **Programmatic registration via `RegisterSpawnedFurnitureUnchecked` cannot be assumed to mirror transform parenting**, because `CommercialBuilding._defaultFurnitureLayout` deliberately parents spawned furniture under the building root (NGO requires NetworkObject children to live under a NetworkObject ancestor; Room sits on a non-NO GameObject and reparenting under it throws `InvalidParentException`). Any `_furnitures = new List<Furniture>(GetComponentsInChildren<Furniture>(true))` assignment in `LoadExistingFurniture` will silently destroy those registrations on the next `Room.Start` / `Room.OnNetworkSpawn`, because the room's transform tree never contained them.

## Symptom
- Place a `CommercialBuilding` (e.g. Forge) authored with `_defaultFurnitureLayout` slots. Inspect `slot.TargetRoom.FurnitureManager._furnitures` — it's empty.
- `[CraftingBuilding] {name}: N CraftingStation(s) found in transform tree but missing from any Room.FurnitureManager._furnitures list...` warning prints from `GetCraftableItems` every time it's called.
- Crafting still works because `CraftingBuilding.GetCraftableItems` / `GetAllStations` carry a transform-tree fallback (`GetComponentsInChildren<CraftingStation>` on the building root), but the warning is a load-bearing diagnostic.
- Other `Room.GetFurniture*` consumers (e.g. UI that lists what's in a room, `Room.HasFurnitureWithTag`) return false-empty.

## Root cause
`Room.OnNetworkSpawn` and `Room.Start` both invoke `FurnitureManager.LoadExistingFurniture()` to handle a real bootstrap race — nested-prefab Furniture children can arrive late in the network/prefab spawn order, especially on clients (NGO sets the network position after `Awake`, and child `NetworkObject`s can spawn after the parent's `OnNetworkSpawn`). The original implementation re-built `_furnitures` from scratch on every invocation:

```csharp
_furnitures = new List<Furniture>(GetComponentsInChildren<Furniture>(true));
```

This is correct for any Furniture that is a transform descendant of the room. It is **silently destructive** for Furniture that has been registered into the room logically (`_furnitures.Add`) but lives in a sibling subtree. That's exactly the shape of `_defaultFurnitureLayout`:

1. Server's `CommercialBuilding.OnNetworkSpawn` calls `TrySpawnDefaultFurniture` → spawns the Furniture, parents it under `this.transform` (building root), then calls `slot.TargetRoom.FurnitureManager.RegisterSpawnedFurnitureUnchecked(instance, worldPos)`. **No transform reparent under the room.**
2. The target Room's `OnNetworkSpawn` (and `Start` one frame later) fires `LoadExistingFurniture` → `GetComponentsInChildren<Furniture>` on the room finds nothing (the Furniture is the room's sibling, not its child) → list reset to `[]`.

The result is "register, then immediately wipe." On clients the registration step doesn't even run (it's server-only), so `_furnitures` is also empty there — but for a different reason and with the same symptom.

## How to avoid
- **`LoadExistingFurniture` is additive.** Prune fake-null entries up front, then merge any newly-discovered transform child via `Contains`-then-`Add`. Re-registering the grid on top is itself idempotent (`FurnitureGrid.RegisterFurniture` just writes `cell.Occupant`), so this is safe to call repeatedly.
- **Do not reintroduce a replace-style assignment** to `_furnitures` in `FurnitureManager` even if it looks cleaner. The "right" mental model is: this list is co-owned by transform-children discovery and programmatic registration; the rescan must respect both.
- **If a programmatic registration path is ever added that lives outside `RegisterSpawnedFurniture*`,** verify it's not undone by a `Room.OnNetworkSpawn` / `Room.Start` rescan (run the building once, inspect `_furnitures` after Start).
- **The same trap exists for any "snapshot from transform tree" rescan over a list that has both authored and runtime sources.** If the runtime-source items don't appear in the transform tree, the snapshot must merge, not replace.

## How to detect
- The `[CraftingBuilding] ... missing from any Room.FurnitureManager._furnitures list` warning is the canonical detector — it fires when `GetCraftableItems` finds a station via the fallback that the primary path missed. Treat it as an alarm bell, not a chronic noise floor.
- For non-crafting furniture there is no equivalent diagnostic; use the LoadExistingFurniture additivity contract as the trust boundary.

## How to fix (if reintroduced)
1. Open [FurnitureManager.cs](../../Assets/Scripts/World/Buildings/FurnitureManager.cs).
2. Replace any `_furnitures = new List<Furniture>(...)` assignment with the additive pattern:

   ```csharp
   _furnitures.RemoveAll(f => f == null);
   foreach (var f in GetComponentsInChildren<Furniture>(true))
   {
       if (f == null) continue;
       if (!_furnitures.Contains(f)) _furnitures.Add(f);
       _grid.RegisterFurniture(f, f.transform.position, f.SizeInCells);
   }
   ```
3. Verify on the Forge: place it, inspect `Room_Main.FurnitureManager._furnitures`. Should contain the `CraftingStation` after both `OnNetworkSpawn` and `Start` have run. The `GetCraftableItems` warning should stop printing.

## Open caveats
- Pure clients still see an empty `_furnitures` for default furniture today: `RegisterSpawnedFurnitureUnchecked` only runs on the server, the spawned Furniture's transform parent is the building root (not the room), and clients don't have a "logical room owner" hint to re-derive membership from. The transform-tree fallback in `CraftingBuilding` covers the only client-side consumer that matters today (crafting capability lookup). Adding a client-visible UI that reads `Room.Furnitures` would require networking the registration (e.g. a `NetworkList<FixedString64Bytes>` keyed on the Furniture's `NetworkObjectId`).

## Affected systems
- [[building]]
- [[commercial-building]]
- [[furniture-grid]]
- [[crafting-loop]]
- [[network]]

## Sources
- 2026-04-25 conversation with Kevin — Forge placed via `BuildingPlacementManager`, `Room_Main.FurnitureManager.Furnitures` empty after spawn; root-caused to `Room.Start` re-running `LoadExistingFurniture` after `RegisterSpawnedFurnitureUnchecked`.
- [Assets/Scripts/World/Buildings/FurnitureManager.cs](../../Assets/Scripts/World/Buildings/FurnitureManager.cs)
- [Assets/Scripts/World/Buildings/Rooms/Room.cs](../../Assets/Scripts/World/Buildings/Rooms/Room.cs)
- [Assets/Scripts/World/Buildings/CommercialBuilding.cs](../../Assets/Scripts/World/Buildings/CommercialBuilding.cs)
- [.agent/skills/building_system/SKILL.md](../../.agent/skills/building_system/SKILL.md) §"Furniture registration / lazy bootstrap"
