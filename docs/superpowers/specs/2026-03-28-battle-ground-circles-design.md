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
- Terrain-conforming ring outlines (the URP Decal Projector handles terrain conformity natively)

---

## Architecture

### Rendering: URP Decal Projector

Each circle is a `DecalProjector` component projecting downward onto the ground. This approach:

- Conforms to terrain and slopes automatically
- Requires the **Decal Renderer Feature** on the URP Renderer asset (must be enabled if not already)
- Uses a custom Shader Graph decal shader for full visual control

### Components

#### 1. `BattleGroundCircle.cs`

**Location:** `Assets/Scripts/BattleManager/BattleGroundCircle.cs`

Self-contained MonoBehaviour on an instantiated prefab. Manages a single circle under one character.

**Responsibilities:**
- Holds reference to its `DecalProjector`
- `Initialize(Color color)` — sets color via **Material Property Block** (preserves batching)
- Fade-in on spawn via coroutine (animates `_Opacity` through MPB)
- `Cleanup()` — fade-out then `Destroy(gameObject)`
- Follows the target character (parented as child, so automatic)

**Lifecycle:** Created and destroyed exclusively by `BattleCircleManager`.

#### 2. `BattleCircleManager.cs`

**Location:** `Assets/Scripts/BattleManager/BattleCircleManager.cs`

Lives on a **dedicated child GameObject** of the Character prefab, exposed via `[SerializeField]` on `Character.cs`. Only activates for the **local player** (`IsOwner` guard).

**Responsibilities:**
- Subscribes to local player's `CharacterCombat.OnBattleJoined` (new event) and `OnBattleLeft`
- On battle join:
  1. Iterates all characters in both `BattleTeam`s via `BattleManager.BattleTeams`
  2. Spawns a `BattleGroundCircle` prefab as child of each character (including self)
  3. Determines color: checks if target is in the same team as local player → Blue, otherwise → Red
  4. Subscribes to `BattleManager.OnParticipantAdded` / `OnParticipantRemoved` (new events)
- On mid-battle join: spawns circle for new participant with correct color
- On mid-battle leave: calls `Cleanup()` on that character's circle, removes from tracking
- On battle leave: cleans up ALL remaining circles, unsubscribes from BattleManager events
- Maintains `Dictionary<Character, BattleGroundCircle>` for tracking active circles

### Prefab: `BattleGroundCircle`

- `DecalProjector` component: projects downward, ~2m width/height, small depth (~1m)
- `BattleGroundCircle.cs` component
- Material referencing the custom decal shader

---

## Shader Design

**Type:** Shader Graph — URP Decal Shader Graph

**Exposed Properties:**

| Property | Type | Default | Purpose |
|----------|------|---------|---------|
| `_Color` | Color (HDR) | White | Team color (Blue/Red), set via MPB |
| `_Opacity` | Float [0-1] | 1.0 | Master opacity for fade in/out |
| `_InnerRadius` | Float [0-1] | 0.3 | Inner edge of ring (0 = filled disc) |
| `_OuterRadius` | Float [0-1] | 0.5 | Outer edge of ring |
| `_Softness` | Float [0-1] | 0.05 | Edge feathering amount |
| `_PulseSpeed` | Float | 0.0 | Pulse animation speed (0 = disabled) |
| `_PulseIntensity` | Float [0-1] | 0.2 | Pulse opacity amplitude |

**Shader Logic:**
1. Calculate distance from UV center (0.5, 0.5) → normalized radial distance
2. Create ring mask: smoothstep between `_InnerRadius` and `_InnerRadius + _Softness`, and between `_OuterRadius - _Softness` and `_OuterRadius`
3. Apply pulse: modulate opacity with `sin(_Time.y * _PulseSpeed) * _PulseIntensity`
4. Output: `_Color` with alpha = ring mask * `_Opacity` * pulse
5. Discard fragments where alpha ≈ 0

---

## Required Codebase Modifications

### New Events (minimal, non-breaking additions)

#### `CharacterCombat.cs`
```csharp
public event Action<BattleManager> OnBattleJoined;
```
Fired at the end of `JoinBattle(BattleManager manager)` after setting `_currentBattleManager`.

#### `BattleManager.cs`
```csharp
public event Action<Character> OnParticipantAdded;
public event Action<Character> OnParticipantRemoved;
```
- `OnParticipantAdded` fired in `AddParticipantInternal()` after the character is added to a team
- `OnParticipantRemoved` fired when a character is removed from the battle (in `EndBattle()` loop or if a mid-battle removal method exists)

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
  if localTeam.IsAlly(targetCharacter) → Blue
  else → Red
```

Uses existing public API: `BattleManager.BattleTeams` and `BattleTeam.IsAlly()`.

---

## Event Flow

```
Player joins battle
  → CharacterCombat.JoinBattle(manager) sets _currentBattleManager
  → Fires OnBattleJoined(manager)              ← NEW EVENT
  → BattleCircleManager receives callback
  → Caches local player's team reference
  → Iterates BattleTeams → all characters
  → For each: instantiates BattleGroundCircle prefab as child
  → Sets color via MPB (Blue/Red based on team)
  → Subscribes to manager.OnParticipantAdded / OnParticipantRemoved

New character joins mid-battle
  → BattleManager.AddParticipantInternal() adds to team
  → Fires OnParticipantAdded(newCharacter)      ← NEW EVENT
  → BattleCircleManager spawns circle with correct color

Character leaves mid-battle
  → Fires OnParticipantRemoved(character)        ← NEW EVENT
  → BattleCircleManager fades out + destroys that circle

Player leaves battle
  → CharacterCombat.LeaveBattle() fires OnBattleLeft
  → BattleCircleManager cleans up ALL circles
  → Unsubscribes from BattleManager events
```

---

## URP Pipeline Requirement

The **Decal Renderer Feature** must be added to the active URP Renderer asset if not already present. This is a one-time project configuration change with no performance impact when no decals are active.

---

## File Summary

| File | Action | Purpose |
|------|--------|---------|
| `Assets/Shaders/BattleGroundCircle.shadergraph` | **Create** | Custom decal ring shader |
| `Assets/Materials/BattleGroundCircle_Mat.mat` | **Create** | Material using the shader |
| `Assets/Prefabs/BattleGroundCircle.prefab` | **Create** | DecalProjector + BattleGroundCircle.cs |
| `Assets/Scripts/BattleManager/BattleGroundCircle.cs` | **Create** | Per-character circle component |
| `Assets/Scripts/BattleManager/BattleCircleManager.cs` | **Create** | Local-player circle orchestrator |
| `Assets/Scripts/Character/Character.cs` | **Modify** | Add BattleCircleManager reference |
| `Assets/Scripts/Character/CharacterCombat/CharacterCombat.cs` | **Modify** | Add OnBattleJoined event |
| `Assets/Scripts/BattleManager/BattleManager.cs` | **Modify** | Add OnParticipantAdded/Removed events |
