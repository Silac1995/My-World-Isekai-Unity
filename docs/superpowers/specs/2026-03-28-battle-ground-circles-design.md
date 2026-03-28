# Battle Ground Circle Indicators — Design Spec

## Summary

When a player character participates in a battle, colored circles are projected on the ground beneath every character in that battle. **Blue** for allies (same team), **Red** for enemies (opposing team). The circles are local-only — only battle participants see them, and color is relative to the viewer's team. Circles follow characters as they move.

## Goals

- Provide clear, at-a-glance team identification during battle
- Shader-driven visuals with extensible animation properties (pulse, glow, rotation)
- Local-only rendering — no network overhead
- Follow the project's Facade + Child Hierarchy pattern

## Non-Goals

- Network-synced indicator colors
- Indicators visible to non-participants
- Generic ground indicator interface (YAGNI — only battle circles needed now)

---

## Architecture

### Rendering: URP Decal Projector

Each circle is a `DecalProjector` component projecting downward onto the ground. This approach:

- Conforms to terrain and slopes automatically
- Requires the **Decal Renderer Feature** on the URP Renderer asset (must be enabled if not already)
- Uses a custom Shader Graph decal shader for full visual control

### Material Strategy (No MPB)

URP `DecalProjector` does **not** use a `Renderer` component, so `MaterialPropertyBlock` is not supported. Instead:

- **Two shared materials**: `BattleGroundCircle_Ally_Mat` (blue) and `BattleGroundCircle_Enemy_Mat` (red). All ally circles share one material, all enemy circles share the other. This preserves batching.
- **Per-instance opacity**: Controlled via `DecalProjector.fadeFactor` (built-in float [0-1]), used for fade-in/fade-out. This is the only per-instance property needed.

With a maximum of ~10-20 characters per battle, this approach is efficient. No per-instance material clones required.

### Components

#### 1. `BattleGroundCircle.cs`

**Location:** `Assets/Scripts/BattleManager/BattleGroundCircle.cs`

Plain `MonoBehaviour` on an instantiated prefab. Manages a single circle under one character.

**Responsibilities:**
- Holds reference to its `DecalProjector`
- `Initialize(Material material)` — assigns the shared ally or enemy material to the DecalProjector
- Fade-in on spawn via coroutine (animates `DecalProjector.fadeFactor` from 0 → 1)
- `Dim()` — reduces `fadeFactor` to a low value (e.g., 0.25) for incapacitated characters
- `Cleanup()` — fade-out (`fadeFactor` 1 → 0) then `Destroy(gameObject)`. Guarded against double-calls via a `_isCleaningUp` flag.
- Follows the target character automatically (parented as child of **root `Character` transform**, not the visual transform, to avoid flipping issues with 2D sprites)

**Time source:** All fade coroutines use `Time.unscaledDeltaTime` since these are non-gameplay visuals that must remain functional during pauses (Rule 24).

**Lifecycle:** Created and destroyed exclusively by `BattleCircleManager`.

**OnDestroy:** Stops any active fade coroutines. Null-checks `DecalProjector` reference in coroutine body to guard against destruction mid-fade.

#### 2. `BattleCircleManager.cs`

**Location:** `Assets/Scripts/BattleManager/BattleCircleManager.cs`

Extends `CharacterSystem` (inherits `_character` reference and `IsOwner` from the parent's `NetworkObject`). Lives on a **dedicated child GameObject** of the Character prefab, exposed via `[SerializeField]` on `Character.cs`.

**Only activates for the local player** via `IsOwner` guard. On non-owner instances, the component remains inert.

**Responsibilities:**
- Subscribes to `_character.CharacterCombat.OnBattleJoined` (new event) and `OnBattleLeft`
- On battle join:
  1. **Clears any existing circles first** (defensive: handles rapid re-engagement edge case)
  2. Caches its own reference to the `BattleManager` (does NOT rely on `CharacterCombat.CurrentBattleManager` for cleanup, since `LeaveBattle()` nulls it before firing `OnBattleLeft`)
  3. Resolves local player's team: `BattleTeams.FirstOrDefault(t => t.IsAlly(localPlayer))`
  4. Iterates all characters in both `BattleTeam`s
  5. Spawns a `BattleGroundCircle` prefab as child of each character's **root transform** (including self)
  6. Assigns shared material: same team → `_allyMaterial`, different team → `_enemyMaterial`
  7. Subscribes to `BattleManager.OnParticipantAdded` (new event) for mid-battle joiners
  8. Subscribes to each tracked character's `OnIncapacitated` to dim their circle on death
  9. Subscribes to each tracked character's `OnWakeUp` to restore their circle on revival
- On mid-battle join (`OnParticipantAdded`): spawns circle for new participant with correct color
- On character incapacitated: calls `Dim()` on that character's circle (circle stays but faded)
- On battle leave (`OnBattleLeft`): cleans up ALL remaining circles, unsubscribes from all events on the cached `BattleManager` reference (null-checked)
- Maintains `Dictionary<Character, BattleGroundCircle>` for tracking active circles

**OnDestroy:** Unsubscribes from all events:
- `CharacterCombat.OnBattleJoined`
- `CharacterCombat.OnBattleLeft`
- Cached `BattleManager.OnParticipantAdded` (if still valid)
- All tracked characters' `OnIncapacitated` and `OnWakeUp`
- Destroys any remaining `BattleGroundCircle` instances

### Prefab: `BattleGroundCircle`

- `DecalProjector` component: projects downward, ~2m width/height, small depth (~1m)
- `BattleGroundCircle.cs` component
- Material slot left empty (assigned at runtime by `BattleCircleManager`)

---

## Shader Design

**Type:** Shader Graph — URP Decal Shader Graph

**Exposed Properties:**

| Property | Type | Default | Purpose |
|----------|------|---------|---------|
| `_Color` | Color (HDR) | White | Team color, baked into the material (Blue or Red) |
| `_InnerRadius` | Float [0-1] | 0.3 | Inner edge of ring (0 = filled disc) |
| `_OuterRadius` | Float [0-1] | 0.5 | Outer edge of ring |
| `_Softness` | Float [0-1] | 0.05 | Edge feathering amount |
| `_PulseSpeed` | Float | 0.0 | Pulse animation speed (0 = disabled) |
| `_PulseIntensity` | Float [0-1] | 0.2 | Pulse opacity amplitude |

**Note:** `_Opacity` is removed from the shader. Per-instance opacity is handled by `DecalProjector.fadeFactor` which the URP decal system applies automatically.

**Shader Logic:**
1. Calculate distance from UV center (0.5, 0.5) → normalized radial distance
2. Create ring mask: smoothstep between `_InnerRadius` and `_InnerRadius + _Softness`, and between `_OuterRadius - _Softness` and `_OuterRadius`
3. Apply pulse: modulate alpha with `sin(_Time.y * _PulseSpeed) * _PulseIntensity`
4. Output: `_Color` with alpha = ring mask * pulse modifier
5. Discard fragments where alpha ≈ 0

**Time in shader:** `_Time.y` is scaled by `Time.timeScale`, so the pulse will pause when the game is paused and speed up during Giga Speed. This is **acceptable** — battle indicators are contextually tied to battle simulation, so pausing them with the game is correct behavior. If unscaled pulse is ever needed, `Time.unscaledTime` can be passed as a float property from `BattleGroundCircle.cs`.

---

## Required Codebase Modifications

### New Events (minimal, non-breaking additions)

#### `CharacterCombat.cs`
```csharp
public event Action<BattleManager> OnBattleJoined;
```
Fired at the end of `JoinBattle(BattleManager manager)` after setting `_currentBattleManager`. **Note:** This event fires on all clients (since `JoinBattle` is called from ClientRpc paths). The `IsOwner` guard on `BattleCircleManager` scopes the response to the local player only.

#### `BattleManager.cs`
```csharp
public event Action<Character> OnParticipantAdded;
```
Fired in `AddParticipantInternal()` after the character is added to a team and registered.

**`OnParticipantRemoved` is intentionally omitted.** The current codebase has no mid-battle single-character removal path. Characters who die remain in their `BattleTeam` (incapacitated, not removed). Full cleanup occurs via `OnBattleLeft` when the battle ends. Dead characters are handled separately via `OnIncapacitated` (dim circle).

### `Character.cs` Integration

```csharp
[SerializeField] private BattleCircleManager _battleCircleManager;
public BattleCircleManager BattleCircleManager => _battleCircleManager;
```

Auto-assigned in `Awake()` via `GetComponentInChildren<BattleCircleManager>()` as fallback.

---

## Team Color Determination

```
For each character in battle:
  localTeam = BattleTeams.FirstOrDefault(t => t.IsAlly(localPlayer))
  if localTeam.IsAlly(targetCharacter) → Ally material (Blue)
  else → Enemy material (Red)
```

Uses existing public API: `BattleManager.BattleTeams` and `BattleTeam.IsAlly()`.

---

## Event Flow

```
Player joins battle
  → CharacterCombat.JoinBattle(manager) sets _currentBattleManager
  → Fires OnBattleJoined(manager)                    ← NEW EVENT (fires on ALL clients)
  → BattleCircleManager receives callback (IsOwner guard: local player only)
  → Clears any leftover circles from prior battle (defensive)
  → Caches BattleManager reference locally
  → Resolves local player's team
  → Iterates BattleTeams → all characters
  → For each: instantiates BattleGroundCircle as child of character's root transform
  → Assigns shared material (ally blue / enemy red)
  → Subscribes to manager.OnParticipantAdded
  → Subscribes to each character's OnIncapacitated

New character joins mid-battle
  → BattleManager.AddParticipantInternal() adds to team
  → Fires OnParticipantAdded(newCharacter)            ← NEW EVENT
  → BattleCircleManager spawns circle with correct color
  → Subscribes to new character's OnIncapacitated and OnWakeUp

Character dies/incapacitated mid-battle
  → Character.OnIncapacitated fires
  → BattleCircleManager calls Dim() on that character's circle
  → Circle stays visible but faded (fadeFactor → 0.25)

Character revived mid-battle
  → Character.OnWakeUp fires
  → BattleCircleManager restores fadeFactor → 1.0 on that character's circle

Player leaves battle (or battle ends)
  → CharacterCombat.LeaveBattle() fires OnBattleLeft
  → BattleCircleManager:
    1. Unsubscribes from cached BattleManager.OnParticipantAdded (null-checked)
    2. Unsubscribes from all tracked characters' OnIncapacitated
    3. Calls Cleanup() on all BattleGroundCircle instances (fade-out → destroy)
    4. Clears Dictionary
    5. Nulls cached BattleManager reference
```

---

## Edge Cases

| Scenario | Handling |
|----------|----------|
| BattleManager destroyed/despawned before cleanup | `BattleCircleManager` uses its own cached reference, null-checks before unsubscribing |
| Rapid re-engagement (leave + join quickly) | `OnBattleJoined` handler clears existing circles before spawning new ones |
| Character dies mid-battle | Circle dims via `Dim()` (fadeFactor → 0.25), not removed |
| Character revived mid-battle | Subscribe to `OnWakeUp` to restore fadeFactor → 1.0 |
| `OnBattleLeft` fires but BattleManager already null | Cached reference used; dictionary cleanup still runs regardless |
| Character despawned while circle exists | Circle is parented to character → destroyed automatically |
| `BattleGroundCircle.Cleanup()` called twice | `_isCleaningUp` flag prevents double fade-out |

---

## URP Pipeline Requirement

The **Decal Renderer Feature** must be added to the active URP Renderer asset if not already present. This is a one-time project configuration change with no performance impact when no decals are active.

---

## File Summary

| File | Action | Purpose |
|------|--------|---------|
| `Assets/Shaders/BattleGroundCircle.shadergraph` | **Create** | Custom decal ring shader |
| `Assets/Materials/BattleGroundCircle_Ally_Mat.mat` | **Create** | Shared blue ally material |
| `Assets/Materials/BattleGroundCircle_Enemy_Mat.mat` | **Create** | Shared red enemy material |
| `Assets/Prefabs/BattleGroundCircle.prefab` | **Create** | DecalProjector + BattleGroundCircle.cs |
| `Assets/Scripts/BattleManager/BattleGroundCircle.cs` | **Create** | Per-character circle component |
| `Assets/Scripts/BattleManager/BattleCircleManager.cs` | **Create** | Local-player circle orchestrator (extends CharacterSystem) |
| `Assets/Scripts/Character/Character.cs` | **Modify** | Add BattleCircleManager reference |
| `Assets/Scripts/Character/CharacterCombat/CharacterCombat.cs` | **Modify** | Add OnBattleJoined event |
| `Assets/Scripts/BattleManager/BattleManager.cs` | **Modify** | Add OnParticipantAdded event |
