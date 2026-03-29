# Furniture Placement HUD — Design Spec

**Date:** 2026-03-29
**Status:** Draft

---

## Overview

A furniture placement system usable by both players and NPCs. Characters carry furniture items in hand, then place them via a shared `CharacterAction`. For players, a ghost-based HUD provides visual feedback. NPCs use the same action without the HUD.

**Core principle:** Anything a player can do, an NPC can do. All gameplay effects go through `CharacterAction`. The player HUD is only a UI layer on top.

## Lifecycle

```
FurnitureCrate (WorldItem) → Pick Up (CharacterPickUpFurnitureAction) → Carried in hands (FurnitureItemInstance)
→ [Player: Press F → Ghost HUD → Click] or [NPC: AI decision]
→ CharacterPlaceFurnitureAction → Installed Furniture (on grid or freestanding)
```

Reverse flow:
```
Installed Furniture → CharacterPickUpFurnitureAction → FurnitureItemInstance in hands → FurnitureCrate visual
```

---

## New Data Types

### `FurnitureItemSO` (ScriptableObject, extends `ItemSO`)

**File:** `Assets/Resources/Data/Item/FurnitureItemSO.cs` (follows existing SO convention)

```csharp
[CreateAssetMenu(fileName = "New Furniture Item", menuName = "Items/Furniture Item")]
```

| Field | Type | Purpose |
|---|---|---|
| `_installedFurniturePrefab` | `Furniture` | The prefab instantiated when placed on the ground/grid |

- Must override `InstanceType => typeof(FurnitureItemInstance)`
- Must override `CreateInstance()` to return a `FurnitureItemInstance`
- Inherits all `ItemSO` fields (name, icon, category, `WorldItemPrefab`, etc.)
- `WorldItemPrefab` on the base `ItemSO` should reference the **FurnitureCrate** prefab (the portable crate visual)
- Bidirectional link: `FurnitureItemSO._installedFurniturePrefab` → Furniture prefab; `Furniture._furnitureItemSO` → back to `FurnitureItemSO`

### `FurnitureItemInstance` (extends `ItemInstance`)

**File:** `Assets/Scripts/Item/FurnitureItemInstance.cs`

- Minimal class — type marker so systems can check `if (carriedItem is FurnitureItemInstance)`
- No additional fields needed initially
- Created by `FurnitureItemSO.CreateInstance()` override

---

## Character Actions (Shared by Player & NPC)

### `CharacterPlaceFurnitureAction` (update existing)

**File:** `Assets/Scripts/Character/CharacterActions/CharacterPlaceFurnitureAction.cs`

The existing action already handles room + grid placement. It needs updating for:
1. **Network-aware spawning:** Server instantiates + `NetworkObject.Spawn()`, then registers on grid
2. **Freestanding support:** Place outside a room (no grid, just instantiate at position)
3. **Item consumption:** Remove `FurnitureItemInstance` from hands on success
4. **Accept `FurnitureItemSO`** instead of (or in addition to) a `Furniture` prefab — resolves the installed prefab internally

#### Updated Constructors

```csharp
// Constructor 1: Player path — position chosen by HUD, item consumed from hands
CharacterPlaceFurnitureAction(Character character, FurnitureItemSO furnitureItemSO, Vector3 targetPosition, Quaternion rotation)

// Constructor 2: NPC path — room + auto-find position (existing pattern)
CharacterPlaceFurnitureAction(Character character, Room room, Furniture furniturePrefab)

// Constructor 3: NPC path — room + explicit position (existing pattern)
CharacterPlaceFurnitureAction(Character character, Room room, Furniture furniturePrefab, Vector3 targetPosition)
```

#### `CanExecute()` Updates
- Existing room+grid validation stays for constructors 2/3
- Constructor 1: ground check + obstacle overlap + range check + optional grid check if inside a room
- Verify character has the furniture item in hands (constructor 1) or hands are free (constructors 2/3)

#### `OnStart()`
- Face target position
- Trigger placement animation

#### `OnApplyEffect()` Updates
- **Inside Room:** Server instantiates from prefab → `NetworkObject.Spawn()` → `FurnitureManager.RegisterSpawnedFurniture(furniture, position)` (grid + list, no double-instantiate)
- **Outside Room:** Server instantiates → `NetworkObject.Spawn()` (freestanding, no grid)
- **Constructor 1 (player):** Consume `FurnitureItemInstance` from hands
- **Constructors 2/3 (NPC):** No item to consume (NPC uses prefab directly)

### `CharacterPickUpFurnitureAction` (new)

**File:** `Assets/Scripts/Character/CharacterActions/CharacterPickUpFurnitureAction.cs`

Mirrors `CharacterPickUpItem` pattern. Used by both players (via interaction) and NPCs (via AI).

#### Constructor

```csharp
CharacterPickUpFurnitureAction(Character character, Furniture targetFurniture)
```

- Caches the target `Furniture` reference
- Gets animation duration from animator (like `CharacterPickUpItem`)

#### `CanExecute()`
- `targetFurniture` is not null and not destroyed
- `targetFurniture.FurnitureItemSO` is assigned (otherwise not a pickable furniture)
- `!targetFurniture.IsOccupied` — can't pick up furniture someone is using
- Character's hands are free: `character.CharacterVisual.BodyPartsController.HandsController.AreHandsFree()`
- Character is close enough to the furniture (proximity check)

#### `OnStart()`
- Face the furniture
- Trigger pickup animation

#### `OnApplyEffect()`
1. Create `FurnitureItemInstance` via `targetFurniture.FurnitureItemSO.CreateInstance()`
2. Put in character's hands via `character.CharacterVisual.BodyPartsController.HandsController.CarryItem(instance)`
3. Detect parent `Room`:
   - **Inside Room:** `FurnitureManager.UnregisterAndRemove(furniture)` — grid unregister + list removal (no destroy)
   - **Outside Room:** No grid work
4. Despawn: `NetworkObject.Despawn(destroy: true)` if networked, otherwise `Destroy()`
5. If hands were taken between `CanExecute` and `OnApplyEffect` (edge case): drop the item at character's feet instead

---

## `FurniturePlacementManager` (Player HUD — CharacterSystem)

**File:** `Assets/Scripts/World/Buildings/FurniturePlacementManager.cs`

### Responsibility
Player-only UI layer: ghost visual, mouse-based positioning, validation feedback. On confirm, **queues a `CharacterPlaceFurnitureAction`** — does NOT directly spawn furniture.

### Fields

| Field | Type | Purpose |
|---|---|---|
| `_groundLayer` | `LayerMask` | Raycast target for mouse positioning |
| `_obstacleLayer` | `LayerMask` | Overlap check (includes "Furniture" layer) |
| `_placementKey` | `KeyCode` | Default `KeyCode.F` — triggers placement mode |
| `_ghostMaterialValid` | `Material` | Green ghost material |
| `_ghostMaterialInvalid` | `Material` | Red ghost material |
| `_toastChannel` | `ToastNotificationChannel` | Player feedback |
| `_maxPlacementRange` | `float` | Max distance from character to place (default 10f) |

### State

| Field | Type | Purpose |
|---|---|---|
| `_isPlacementActive` | `bool` | Currently in placement mode |
| `_ghostInstance` | `GameObject` | The ghost visual |
| `_activeFurnitureItemSO` | `FurnitureItemSO` | What we're placing |
| `_ghostRotation` | `Quaternion` | Current ghost rotation |
| `_isDebugMode` | `bool` | Bypasses carry requirement |

### Entry Points

1. **Normal mode:** `Update()` detects `_placementKey` pressed + character is carrying a `FurnitureItemInstance` → `StartPlacement(FurnitureItemSO)`
2. **Debug mode:** `StartPlacementDebug(FurnitureItemSO)` — called externally by `DebugScript`, sets `_isDebugMode = true`

### `StartPlacement(FurnitureItemSO)`

1. Instantiate ghost from `FurnitureItemSO._installedFurniturePrefab`
2. Disable all colliders on the ghost (prevents self-detection in overlap checks)
3. Set ghost layer to `Ignore Raycast` (prevents blocking ground raycast, prevents pushing characters)
4. Disable `Rigidbody` (kinematic) and `NetworkObject` on ghost
5. Apply valid ghost material
6. Reuse `_character.SetBuildingState(true)` to mark character as busy

### Update Loop (while placement active)

1. Raycast from camera through mouse onto `_groundLayer`
2. Position ghost at hit point with current `_ghostRotation`
3. **Rotation:** Q/E keys rotate ghost by 90 degrees
4. Run `ValidatePlacement(position)`:
   - Ground hit required
   - Range check: `Vector3.Distance(character.position, position) <= _maxPlacementRange`
   - `Physics.OverlapBox` against `_obstacleLayer` — ghost colliders are disabled so it never detects itself; detects other installed furniture on "Furniture" layer
   - If position is inside a `Room` with a `FurnitureGrid`: additionally check `grid.CanPlaceFurniture()`
5. Swap ghost material (green/red) based on validity

### Input Handling

- **Left-click** + valid: Queue `CharacterPlaceFurnitureAction` on the character → clear ghost. The action handles network spawn and item consumption.
- **Q / E**: Rotate ghost 90 degrees counter-clockwise / clockwise
- **Right-click / Escape**: Cancel → destroy ghost, keep item in hands.

### Auto-Interruption

Overrides from `CharacterSystem`:
- `HandleIncapacitated()` → `CancelPlacement()`
- `HandleCombatStateChanged(inCombat)` → if true, `CancelPlacement()`

### `OnDestroy()`

Calls `CancelPlacement()` to destroy any lingering ghost (per CLAUDE.md rule 16).

---

## Pick Up Interaction (Existing `FurnitureInteractable`)

A `FurnitureInteractable` class already exists for furniture usage interactions. Add a **"Pick Up" hold-interaction option** via `GetHoldInteractionOptions()`.

### Guard Conditions (for showing the option)
- Furniture has `_furnitureItemSO` assigned (otherwise not pickable)
- Furniture is not occupied (`!furniture.IsOccupied`)

### On "Pick Up" Selected
Queue a `CharacterPickUpFurnitureAction` on the interacting character. The action handles all validation (hands free, proximity), animation, network despawn, and item-to-hands.

---

## Modifications to Existing Files

### `Furniture.cs`

Add back-reference field:
```csharp
[Header("Item Data")]
[SerializeField] private FurnitureItemSO _furnitureItemSO;
public FurnitureItemSO FurnitureItemSO => _furnitureItemSO;
```

### `Furniture` Prefab Requirements (for player-placed furniture)

Player-placed furniture prefabs must include:
- `NetworkObject` component (required for network spawn/despawn)
- `Furniture` component with `_furnitureItemSO` assigned
- `FurnitureInteractable` component (for pickup interaction)

Pre-placed editor furniture (NPC-driven, from `Building.AttemptInstallFurniture()`) may lack `NetworkObject` — the existing non-networked flow still works for those.

### `FurnitureManager.cs`

Add two new methods (existing methods untouched):

```csharp
/// Registers an already-instantiated and network-spawned Furniture onto the grid and list.
/// Does NOT instantiate — the caller is responsible for spawning.
public bool RegisterSpawnedFurniture(Furniture furniture, Vector3 targetPosition)
{
    if (_grid == null || furniture == null) return false;
    if (!_grid.CanPlaceFurniture(targetPosition, furniture.SizeInCells)) return false;

    _grid.RegisterFurniture(furniture, targetPosition, furniture.SizeInCells);
    _furnitures.Add(furniture);
    furniture.transform.SetParent(transform);
    return true;
}

/// Unregisters furniture from grid and list without destroying the GameObject.
/// Caller handles destruction/despawn.
public void UnregisterAndRemove(Furniture furniture)
{
    if (furniture == null) return;
    if (_grid != null) _grid.UnregisterFurniture(furniture);
    _furnitures.Remove(furniture);
}
```

### `Character.cs`

Expose `FurniturePlacementManager` reference:
```csharp
[SerializeField] private FurniturePlacementManager _furniturePlacementManager;
public FurniturePlacementManager FurniturePlacementManager => _furniturePlacementManager;
```

### `DebugScript.cs`

- Change `_testFurniturePrefab` field from `Furniture` to `FurnitureItemSO`
- Update `TestInstallFurniture()` to call `FurniturePlacementManager.StartPlacementDebug(furnitureItemSO)` on the player's character
- Remove the old room-search and random-placement logic

### `FurnitureInteractable.cs` (existing)

- Add "Pick Up" to `GetHoldInteractionOptions()` (guarded by `_furnitureItemSO != null && !IsOccupied`)
- On selection: queue `CharacterPickUpFurnitureAction` on the interactor

---

## Ghost Collision Handling

The "Furniture" layer is treated as an obstacle. To prevent the ghost from blocking itself:

1. **Ghost colliders:** All disabled on spawn → `Physics.OverlapBox` never finds the ghost
2. **Ghost layer:** Set to `Ignore Raycast` → doesn't block ground raycasts, doesn't interact with characters
3. **Result:** Overlap check detects other installed furniture (on "Furniture" layer) but never the ghost itself

---

## Network Considerations

- Ghost is client-local only (NetworkObject disabled on the ghost)
- **Placement:** `CharacterPlaceFurnitureAction.OnApplyEffect()` runs on the server. Server instantiates, spawns NetworkObject, registers with grid if in a room.
- **Pick-up:** `CharacterPickUpFurnitureAction.OnApplyEffect()` runs on the server. Server validates, unregisters from grid, despawns NetworkObject, creates item for hands.
- `FurnitureManager.RemoveFurniture()` (existing) calls `Destroy()` — this remains for non-networked NPC furniture. For networked player furniture, use `UnregisterAndRemove()` + `NetworkObject.Despawn()`.

---

## Freestanding Furniture Persistence

Furniture placed outside a Room has no grid registration. Persistence during map hibernation is **out of scope** for this spec — freestanding furniture is transient. A `FurnitureSaveData` system can be added later if needed.

---

## Files Summary

### New Files
| File | Type | Purpose |
|---|---|---|
| `Assets/Resources/Data/Item/FurnitureItemSO.cs` | ScriptableObject | Portable furniture data, links to installed prefab |
| `Assets/Scripts/Item/FurnitureItemInstance.cs` | Class | Type marker for carried furniture |
| `Assets/Scripts/World/Buildings/FurniturePlacementManager.cs` | CharacterSystem | Player-only ghost HUD, queues CharacterAction on confirm |
| `Assets/Scripts/Character/CharacterActions/CharacterPickUpFurnitureAction.cs` | CharacterAction | Shared pickup action (player + NPC) |

### Modified Files
| File | Change |
|---|---|
| `CharacterPlaceFurnitureAction.cs` | Add player constructor (FurnitureItemSO + position + rotation), network spawn, freestanding support, item consumption |
| `Furniture.cs` | Add `_furnitureItemSO` back-reference field |
| `FurnitureInteractable.cs` | Add "Pick Up" hold-interaction, queues `CharacterPickUpFurnitureAction` |
| `FurnitureManager.cs` | Add `RegisterSpawnedFurniture()` and `UnregisterAndRemove()` methods |
| `Character.cs` | Expose `FurniturePlacementManager` reference |
| `DebugScript.cs` | Route test button through `FurniturePlacementManager.StartPlacementDebug()` |

### Untouched
- `BuildingPlacementManager` — no changes
- `FurnitureGrid` — reused as-is
- `HandsController` — reused as-is
