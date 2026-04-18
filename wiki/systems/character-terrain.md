---
type: system
title: "Character Terrain"
tags: [character, terrain, footstep, audio, tier-2]
created: 2026-04-19
updated: 2026-04-19
sources: []
related:
  - "[[character]]"
  - "[[terrain-and-weather]]"
  - "[[character-equipment]]"
  - "[[character-archetype]]"
  - "[[items]]"
  - "[[visuals]]"
  - "[[kevin]]"
status: wip
confidence: medium
primary_agent: character-system-specialist
secondary_agents:
  - world-system-specialist
owner_code_path: "Assets/Scripts/Character/CharacterTerrain/"
depends_on:
  - "[[character]]"
  - "[[terrain-and-weather]]"
  - "[[character-equipment]]"
  - "[[character-archetype]]"
depended_on_by:
  - "[[character]]"
---

# Character Terrain

## Summary
Character subsystem that reads the terrain cell beneath the character each frame and applies/exposes its effects. Server applies gameplay effects (speed multiplier, damage over time, slip factor). Clients resolve footstep audio locally using a terrain × boot-material matrix. Triggered by animation events on [[visuals|CharacterVisual]]. Foot material comes from [[character-equipment]]'s outermost boot slot; falls back to [[character-archetype]]'s `DefaultFootSurface` when the character is barefoot.

## Purpose
Translate the global terrain simulation ([[terrain-and-weather]]) into per-character reactions. Mud slows, lava burns, ice makes you slip, wood floors echo under metal boots, padded paws whisper on grass. The system is a pure consumer of terrain state — it does not mutate cells, only reads them. Equal for players and NPCs per the "anything player can do, NPC can do" rule.

## Responsibilities
- Each frame, resolve the terrain under the character's root position using a priority chain (sealed room → active MapController grid → world-map BiomeRegion default).
- Server-side: apply `TerrainType.SpeedMultiplier`, `DamagePerSecond`, and `SlipFactor` to the character.
- Client-side: expose `CurrentTerrainType`, `IsInWeatherFront`, `CurrentWeather` for audio and VFX consumers.
- Subscribe to `CharacterVisual.OnAnimationEvent("footstep")` and play a randomized clip from the appropriate `FootstepAudioProfile`.
- Stop applying effects on dead characters (`HandleDeath` / `HandleWakeUp` overrides).

**Non-responsibilities**
- Does **not** mutate cell state — that's [[terrain-and-weather|TerrainWeatherProcessor]].
- Does **not** implement the speed pipeline itself — it will feed into `CharacterMovement` when the speed-modifier API is exposed (see TODO).
- Does **not** own boot assets or audio clips — those live on ScriptableObject assets under `Resources/Data/Terrain/` and `Resources/Data/Audio/`.

## Key classes / files

- `Assets/Scripts/Character/CharacterTerrain/CharacterTerrainEffects.cs` — `CharacterSystem` subsystem (child GameObject of the Character root, per the facade pattern).
- `Assets/Scripts/Character/CharacterTerrain/FootstepAudioResolver.cs` — `MonoBehaviour` that subscribes to animation events.
- `Assets/Scripts/Audio/FootstepAudioProfile.cs` — `ScriptableObject` lookup table (`MaterialClipSet` entries keyed by `ItemMaterial` + `FootSurfaceType`).
- `Assets/Scripts/Items/ItemMaterial.cs` — enum (Leather, Iron, Steel, Wood, Cloth, Bone, Hide, ChainMail, Stone, Crystal, Fur) — added to [[items|ItemSO]] as `_material` field.
- `Assets/Scripts/Character/Archetype/FootSurfaceType.cs` — enum (BareSkin, Hooves, Padded, Clawed, Scaled) — added to [[character-archetype|CharacterArchetype]] as `_defaultFootSurface`.
- `Assets/Scripts/Character/CharacterEquipment/CharacterEquipment.cs` — new `GetFootMaterial()` method walking armor → clothing → underwear layers.

## Public API / entry points

### CharacterTerrainEffects
```csharp
// Exposed state
TerrainType CurrentTerrainType      { get; }
bool        IsInWeatherFront        { get; }
WeatherType CurrentWeather          { get; }

// Events
event Action<TerrainType> OnTerrainChanged;

// Overrides (from CharacterSystem)
protected override void HandleDeath(Character)    // halts Update effects
protected override void HandleWakeUp(Character)   // resumes
```

### FootstepAudioResolver
```csharp
void PlayFootstep()   // public — can be called manually; also fires on animation event "footstep"
```

### FootstepAudioProfile
```csharp
(AudioClip clip, float volume, float pitchVariation) GetClip(ItemMaterial boot, FootSurfaceType surface)
// Resolution order:
//   1. Exact boot material match (if != None)
//   2. Foot surface match (if boot == None)
//   3. Fallback clips on the profile
```

### CharacterEquipment
```csharp
ItemMaterial GetFootMaterial()
// Returns ArmorLayer.Boots.Material ?? ClothingLayer.Boots.Material ?? UnderwearLayer.Boots.Material ?? None
```

### Terrain resolution chain (in `CharacterTerrainEffects.UpdateTerrainDetection`)

```
1. (TODO) CharacterLocations.CurrentRoom != null && !Room.IsExposed
         → Room.FloorTerrainType
2. MapController.GetMapAtPosition(pos) returns a map
         → map.GetComponent<TerrainCellGrid>().GetTerrainAt(pos)
3. BiomeRegion.GetRegionAtPosition(pos) returns a region
         → region.GetDefaultTerrainType()
4. null (unknown terrain)
```

### Footstep resolution chain (in `FootstepAudioResolver.PlayFootstep`)

```
1. CurrentTerrainType from CharacterTerrainEffects → its FootstepAudioProfile
2. CharacterEquipment.GetFootMaterial()
         if None → fall through to archetype
3. CharacterArchetype.DefaultFootSurface
4. FootstepAudioProfile.GetClip(bootMaterial, footSurface)
5. Play one-shot at character position with random pitch variation
```

## Data flow

```
Animation event "footstep" (from CharacterVisual.RaiseAnimationEvent)
         │
         ▼
FootstepAudioResolver.HandleAnimationEvent
         │
         ├── reads CharacterTerrainEffects.CurrentTerrainType
         │        └── (every frame) UpdateTerrainDetection reads the grid
         │
         ├── reads _character.CharacterEquipment.GetFootMaterial()
         │        └── walks armor → clothing → underwear boot slots
         │
         ├── reads _character.Archetype.DefaultFootSurface (fallback)
         │
         └── TerrainType.FootstepProfile.GetClip(material, surface)
                  └── AudioSource.PlayOneShot

Per-frame (server only):
CharacterTerrainEffects.ApplyTerrainEffects
         └── TerrainType.SpeedMultiplier / DamagePerSecond / SlipFactor
              → (TODO) feeds CharacterMovement and health pipeline
```

## Dependencies

### Upstream
- [[character]] — lives as a child GameObject under the Character root per the facade pattern; reads `CharacterEquipment`, `Archetype`, `CharacterVisual` via the facade (never via `GetComponentInChildren` from inside its logic).
- [[terrain-and-weather]] — reads `TerrainCellGrid.GetTerrainAt`, `BiomeRegion.GetDefaultTerrainType`, `BiomeRegion.ActiveFronts`.
- [[character-equipment]] — calls `GetFootMaterial()`.
- [[character-archetype]] — reads `DefaultFootSurface` fallback.
- [[items]] — reads `ItemSO.Material` through the equipment instance chain.
- [[visuals|CharacterVisual]] — subscribes to `OnAnimationEvent`.

### Downstream
- [[character]] — `TerrainEffects` property is exposed on `Character.cs` for external systems to query current terrain.

## State & persistence

- **No persistent state.** Everything is recomputed each frame from the synchronized terrain grid + equipment.
- Foot material is derived from equipped boots — already synchronized via the [[character-equipment]] system.
- Terrain under feet is derived from the synchronized cell grid — already handled by [[terrain-and-weather]].
- Audio plays locally on each client; no RPC needed.

## Known gotchas / edge cases

- **Character facade rule** — `FootstepAudioResolver` goes through `_character.CharacterEquipment` and `_character.CharacterVisual`, NOT `GetComponentInChildren`. The character facade is the single dependency point per the root `CLAUDE.md` rule.
- **`CharacterVisual.OnAnimationEvent`** uses the string `"footstep"` — animation clips must fire this exact event name.
- **Missing boot → foot surface fallback** — if no boot is equipped in any layer, `GetFootMaterial()` returns `None` and the resolver falls through to the archetype's `DefaultFootSurface`. Profiles must author both boot-material variants AND foot-surface variants per terrain type to cover both cases.
- **Dead characters** — `HandleDeath` sets `_isDead = true` and the `Update()` early-outs. `HandleWakeUp` restores.
- **Server vs client split** — terrain detection runs on both (same data, same result). Gameplay effects (speed/damage) run server-only. Audio runs client-locally.

## Open questions / TODO

- [ ] **Room detection not yet wired.** The spec's priority chain step 1 (sealed room → `Room.FloorTerrainType`) is stubbed in the code with a comment. Needs `CharacterLocations` to expose `CurrentRoom` before it can be consulted. Until then, the grid always wins inside buildings.
- [ ] **Speed modifier pipeline not applied.** `ApplyTerrainEffects` computes the value but `CharacterMovement` doesn't yet expose a speed-multiplier API to feed. Wiring to be done once [[character-movement]] supports modifiers.
- [ ] **Damage pipeline not applied.** Similar — waits for a Character health/damage API to accept DoT.
- [ ] **Wetness tracking** (spec §7.3) — deferred to Phase 2 along with player-visible rain particles.
- [ ] **Full audio clip authoring** — framework ships empty. Designers must create `FootstepAudioProfile` SO assets per terrain and populate `MaterialClipSet` entries.

## Change log

- 2026-04-19 — Stub created pre-merge. — Claude / [[kevin]]
- 2026-04-19 — Full pass after implementation landed on `feature/character-archetype-system`. Populated all required system sections. Confidence raised to **medium** (framework is complete, gameplay pipes awaiting downstream APIs). — Claude / [[kevin]]

## Sources
- [Assets/Scripts/Character/CharacterTerrain/CharacterTerrainEffects.cs](../../Assets/Scripts/Character/CharacterTerrain/CharacterTerrainEffects.cs)
- [Assets/Scripts/Character/CharacterTerrain/FootstepAudioResolver.cs](../../Assets/Scripts/Character/CharacterTerrain/FootstepAudioResolver.cs)
- [Assets/Scripts/Audio/FootstepAudioProfile.cs](../../Assets/Scripts/Audio/FootstepAudioProfile.cs)
- [Assets/Scripts/Items/ItemMaterial.cs](../../Assets/Scripts/Items/ItemMaterial.cs)
- [Assets/Scripts/Character/Archetype/FootSurfaceType.cs](../../Assets/Scripts/Character/Archetype/FootSurfaceType.cs)
- [Assets/Scripts/Character/CharacterEquipment/CharacterEquipment.cs](../../Assets/Scripts/Character/CharacterEquipment/CharacterEquipment.cs) — `GetFootMaterial()` helper.
- [.agent/skills/character-terrain/SKILL.md](../../.agent/skills/character-terrain/SKILL.md) — procedural source of truth.
- Feature branch commit `e1f99bb feat(terrain): add CharacterTerrainEffects, FootstepAudioResolver, FootstepAudioProfile`.
- Parent system: [[terrain-and-weather]].
