---
name: character-terrain
description: Character subsystem that reads the terrain type under a character's feet and resolves footstep audio. CharacterTerrainEffects detects terrain and weather; FootstepAudioResolver maps terrain × boot material to the correct audio clip.
---

# Character Terrain System

## 0. Purpose

The Character Terrain system bridges the terrain-weather simulation layer and individual characters. It answers two questions every frame for every living character:

1. **What surface am I standing on right now?** (`CharacterTerrainEffects`)
2. **What sound should my footsteps make on this surface?** (`FootstepAudioResolver`)

Both scripts live on the same child GameObject under the character hierarchy (the `CharacterTerrain` child GO). `CharacterTerrainEffects` is a `CharacterSystem` (registered in the capability registry); `FootstepAudioResolver` is a plain `MonoBehaviour` that depends on it.

## 1. When to Use This Skill

- Hooking into terrain detection to apply movement speed penalties or DoT damage
- Adding new audio clips for a new terrain type or boot material
- Debugging why footstep audio is missing or playing the wrong clip
- Implementing a visual effect that reacts to the current terrain (e.g., mud splash particles)
- Extending terrain detection priority (e.g., adding Room.FloorTerrainType as the sealed-room source)

## 2. CharacterTerrainEffects

**Class:** `CharacterTerrainEffects : CharacterSystem`
**File:** `Assets/Scripts/Character/CharacterTerrain/CharacterTerrainEffects.cs`

Runs on both server and client every `Update`. The server uses `CurrentTerrainType` to apply gameplay effects. Clients use it as the data source for audio and VFX.

### Public API

```csharp
// Properties
TerrainType CurrentTerrainType { get; }   // Terrain type under character's feet right now; null if unresolvable
bool IsInWeatherFront { get; }            // True if any active WeatherFront covers the character's position
WeatherType CurrentWeather { get; }       // Type of the nearest overlapping front (if IsInWeatherFront)

// Events
event Action<TerrainType> OnTerrainChanged;   // Fires when CurrentTerrainType changes (both server and client)
```

### Terrain Detection Priority

Detection runs in `UpdateTerrainDetection()` every frame. The first non-null result wins:

| Priority | Source | Condition |
|---|---|---|
| 1 | `Room.FloorTerrainType` | Character is inside a sealed room (NOT yet implemented — awaits CharacterLocations integration) |
| 2 | `TerrainCellGrid.GetTerrainAt(pos)` | The map's active terrain grid covers the position |
| 3 | `BiomeRegion.GetDefaultTerrainType()` | Open-world fallback from the biome's `BiomeClimateProfile.DefaultTerrainType` |

`MapController.GetMapAtPosition(pos)` resolves the correct `MapController` for multi-map worlds. The `TerrainCellGrid` component is fetched from that `MapController`'s `GameObject`.

```csharp
// Internal detection flow (simplified)
var map = MapController.GetMapAtPosition(pos);
var grid = map?.GetComponent<TerrainCellGrid>();
newTerrain = grid?.GetTerrainAt(pos);

if (newTerrain == null)
    newTerrain = BiomeRegion.GetRegionAtPosition(pos)?.GetDefaultTerrainType();
```

### Weather Front Detection

After terrain detection, `CharacterTerrainEffects` queries the character's `BiomeRegion` for overlapping fronts. Distance check: `Vector3.Distance(pos, front.transform.position) < front.Radius.Value`.

### Gameplay Effects (ApplyTerrainEffects — server only)

Currently the framework is established but effects are pending API exposure from the relevant subsystems:

- **Speed multiplier**: `CurrentTerrainType.SpeedMultiplier` is the value to pass to `CharacterMovement` when that API is available.
- **Damage over time**: `CurrentTerrainType.HasDamage` and `DamagePerSecond` are the inputs. Damage application is pending the character health/combat damage API.

> When implementing these: route damage through the character's stats/combat system, not directly into HP. Speed multiplier must be applied as a terrain modifier that stacks correctly with other movement modifiers.

### Lifecycle Overrides

```csharp
protected override void HandleDeath(Character character)   // Sets _isDead = true; Update skips detection
protected override void HandleWakeUp(Character character)  // Sets _isDead = false; detection resumes
```

Dead characters do not tick terrain detection or apply terrain effects.

---

## 3. FootstepAudioResolver

**Class:** `FootstepAudioResolver : MonoBehaviour`
**File:** `Assets/Scripts/Character/CharacterTerrain/FootstepAudioResolver.cs`

Listens for the `"footstep"` animation event from `ICharacterVisual.OnAnimationEvent`. When received, resolves the correct audio clip by crossing the current terrain type against the character's boot material or foot surface type.

### Setup Requirements

- Requires a sibling `CharacterTerrainEffects` component (fetched via `GetComponent<CharacterTerrainEffects>()` in `Awake`).
- Requires a parent `Character` component (fetched via `GetComponentInParent<Character>()`).
- Requires an `AudioSource` assigned to `_footstepAudioSource` (SerializeField).
- Subscribe/unsubscribe from `_character.CharacterVisual.OnAnimationEvent` in `OnEnable`/`OnDisable`.

### Resolution Logic

`FootstepAudioResolver.PlayFootstep()` runs the following lookup in order:

1. **Boot material match** — if the character has `CharacterEquipment` and `equipment.GetFootMaterial()` returns a non-`None` `ItemMaterial`, searches `FootstepAudioProfile._materialClips` for an entry where `BootMaterial == bootMaterial`.
2. **Foot surface match** — if no boot match, searches for an entry where `BootMaterial == None && FootSurface == footSurface`. `footSurface` comes from `character.Archetype.DefaultFootSurface`.
3. **Fallback** — plays a random clip from `FootstepAudioProfile._fallbackClips`.

Playback uses `AudioSource.PlayOneShot()` with pitch randomized by `±pitchVariation`.

```csharp
// Called by animation event, or directly for manual triggering
footstepResolver.PlayFootstep();
```

---

## 4. FootstepAudioProfile

**Class:** `FootstepAudioProfile : ScriptableObject`
**File:** `Assets/Scripts/Audio/FootstepAudioProfile.cs`
**Create menu:** `MWI/Audio/Footstep Audio Profile`

Lookup table assigned to `TerrainType.FootstepProfile`. Contains a list of `MaterialClipSet` entries (one per boot material or foot surface combination) and a fallback clip array.

```csharp
(AudioClip clip, float volume, float pitchVariation) GetClip(
    ItemMaterial bootMaterial, FootSurfaceType footSurface);
```

**`MaterialClipSet` (inner class):**

| Field | Type | Purpose |
|---|---|---|
| `BootMaterial` | `ItemMaterial` | Boot material this set responds to (`None` = foot surface match) |
| `FootSurface` | `FootSurfaceType` | Creature foot type used when `BootMaterial == None` |
| `Clips` | `AudioClip[]` | Random pool of clips for this combination |
| `VolumeMultiplier` | `float` | Volume scale (default 1.0) |
| `PitchVariation` | `float` (0–0.3) | Random pitch offset range |

---

## 5. Supporting Enums

**`ItemMaterial` (enum : byte)**
**File:** `Assets/Scripts/Items/ItemMaterial.cs`

Represents the physical material of an equipped item (boots, armor, etc.). Used as the primary footstep resolution key.

```
None, Cloth, Leather, Hide, Wood, Bone, Iron, Steel, ChainMail, Stone, Crystal, Fur
```

`CharacterEquipment.GetFootMaterial()` returns the `ItemMaterial` of whatever is equipped in the foot slot. Returns `ItemMaterial.None` if nothing is equipped.

**`FootSurfaceType` (enum : byte)**
**File:** `Assets/Scripts/Character/Archetype/FootSurfaceType.cs`

Represents the natural foot type of a creature when no boots are worn. Defined on `CharacterArchetype.DefaultFootSurface`.

```
BareSkin, Hooves, Padded, Clawed, Scaled
```

---

## 6. Integration Points

### Depends On

| System | Why |
|---|---|
| `terrain-weather` | `TerrainType`, `TerrainCellGrid`, `BiomeRegion` are all queried in `UpdateTerrainDetection` |
| `character-archetype` | `CharacterSystem` base class, capability registry, `CharacterArchetype.DefaultFootSurface` |
| `character_core` | `Character` facade, `CharacterEquipment.GetFootMaterial()`, `ICharacterVisual.OnAnimationEvent` |
| `building_system` | `Room.FloorTerrainType` is the planned Priority 1 terrain source (pending `CharacterLocations` integration) |

### What Depends on This

| System | How it uses CharacterTerrainEffects |
|---|---|
| `FootstepAudioResolver` | Reads `CurrentTerrainType` on every footstep event |
| Future movement system | Should read `CurrentTerrainType.SpeedMultiplier` to apply movement penalty |
| Future combat/health system | Should read `CurrentTerrainType.HasDamage` and `DamagePerSecond` to apply DoT |
| Future VFX system | Can subscribe to `OnTerrainChanged` or read `CurrentTerrainType` for particle effects (e.g., mud splash, snow puff) |
| Future UI/HUD | Can subscribe to `OnTerrainChanged` for terrain indicator in the player HUD |

---

## 7. Adding a New Footstep Combination

1. Confirm the terrain type has a `FootstepAudioProfile` assigned on its `TerrainType` SO.
2. Open the `FootstepAudioProfile` SO in the Inspector.
3. Add a new `MaterialClipSet` entry:
   - Set `BootMaterial` to the specific material, OR set it to `None` and set `FootSurface` for a creature foot type.
   - Assign one or more `AudioClip` assets to `Clips`.
   - Tune `VolumeMultiplier` and `PitchVariation`.
4. If this is a new `ItemMaterial` or `FootSurfaceType` value: add it to the respective enum file. Both enums are `byte`-backed — do not reorder existing values.
5. Ensure `CharacterEquipment.GetFootMaterial()` can return the new material when the relevant foot-slot item is equipped.

## 8. Completing the Room Priority (Pending)

When `CharacterLocations` exposes an API to query the `Room` a character is currently inside:

1. In `CharacterTerrainEffects.UpdateTerrainDetection()`, add a Priority 1 branch before the `TerrainCellGrid` query.
2. Check if `_character` has a valid current room via `CharacterLocations`.
3. If the room is sealed (`!room.IsExposed`), return `room.FloorTerrainType` immediately.
4. If `room.IsExposed`, continue to the grid query (weather can still affect the room).

## 9. Key File Locations

| File | Purpose |
|---|---|
| `Assets/Scripts/Character/CharacterTerrain/CharacterTerrainEffects.cs` | CharacterSystem subsystem — terrain detection and gameplay effect hooks |
| `Assets/Scripts/Character/CharacterTerrain/FootstepAudioResolver.cs` | Audio resolver — animation event → terrain × material → clip |
| `Assets/Scripts/Audio/FootstepAudioProfile.cs` | ScriptableObject lookup table per terrain type |
| `Assets/Scripts/Items/ItemMaterial.cs` | ItemMaterial enum — boot/item material classification |
| `Assets/Scripts/Character/Archetype/FootSurfaceType.cs` | FootSurfaceType enum — creature natural foot type |
| `Assets/Scripts/World/Buildings/Rooms/Room.cs` | Room.FloorTerrainType and Room.IsExposed (planned Priority 1 source) |

## 10. Golden Rules

1. **CharacterTerrainEffects runs on both server and client.** Only `ApplyTerrainEffects()` is gated to `IsServer`. Detection always runs so clients have `CurrentTerrainType` available for audio and VFX.
2. **Never apply gameplay effects (speed, damage) on the client.** Gate all stat modifications behind `if (IsServer)` inside `ApplyTerrainEffects`.
3. **All gameplay effects must route through the character system.** Do not write directly to `Rigidbody.velocity` or subtract HP directly. Use the appropriate character subsystem APIs when they expose the relevant hooks.
4. **FootstepAudioResolver must unsubscribe in OnDisable.** The subscription to `ICharacterVisual.OnAnimationEvent` must be removed in `OnDisable` to avoid stale callbacks on dead or destroyed characters.
5. **TerrainType null checks are mandatory.** Both `CurrentTerrainType` and `TerrainType.FootstepProfile` can be null (unresolvable position, missing SO assignment). Guard every access.
6. **Do not cache MapController or BiomeRegion between frames.** Characters can cross map boundaries. Re-query `MapController.GetMapAtPosition` and `BiomeRegion.GetRegionAtPosition` every tick.
