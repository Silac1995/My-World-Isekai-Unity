# WorldItem Physics & Pathing — Design

**Status:** Draft — awaiting user review
**Date:** 2026-04-22
**Scope:** Make dropped/spawned `WorldItem`s behave as full physics citizens, gain NavMeshObstacle-based AI avoidance, and stop locking themselves to the ground. Velocity-based impact damage is a separate spec.

---

## 1. Goal

Three concurrent behaviors for every `WorldItem` in the world:

1. **AI pathing** — NPCs and players-on-NavMeshAgent route *around* world items by default, instead of into them.
2. **Physics-reactive** — items respond to gravity, character bumps, explosions, projectiles. Hit by a force, they fly. Settle by themselves.
3. **Self-unsticking** — when a character does end up overlapping an item (drop at feet, knockback into a pile, narrow corridor), the character can push the item out of the way physically. No "stuck on dropped item" state.

## 2. Context

- **Current behavior** ([WorldItem.cs:328-339](../../../Assets/Scripts/Item/WorldItem.cs#L328-L339)): items have a non-kinematic `Rigidbody`, an optional `FreezeOnGround` flag that locks `isKinematic = true` on first ground contact. Drops via `CharacterDropItem.ExecutePhysicalDrop` ([CharacterDropItem.cs:56-84](../../../Assets/Scripts/Character/CharacterActions/CharacterDropItem.cs#L56-L84)) currently default `freeze=false`, but other entry points (death, incapacitation) pass `freeze=true`.
- **Current pain point**: when `FreezeOnGround` engages while a character is overlapping the item (typical for drop-at-feet), the item locks under the character and the NavMeshAgent has nothing to push against — the character gets stuck.
- **No NavMeshObstacle today** — the world `NavMeshSurface` is rebaked from scratch when buildings spawn ([Building.cs:164-179](../../../Assets/Scripts/World/Buildings/Building.cs#L164-L179)) but items don't trigger any rebake or carve.
- **NavMeshAgent setup** ([CharacterMovement.cs:66-70](../../../Assets/Scripts/Character/CharacterMovement/CharacterMovement.cs#L66-L70)) uses `HighQualityObstacleAvoidance` and has stuck-detection / sliding fallback ([CharacterMovement.cs:142-186](../../../Assets/Scripts/Character/CharacterMovement/CharacterMovement.cs#L142-L186)) that currently has to recover from item-stuck cases.
- **Prefab inspection** (`Assets/Prefabs/Items/WorldItem_prefab.prefab`):
  - `m_Layer: 8` = **"RigidBody"** (confirmed via `ProjectSettings/TagManager.asset`). This is the project's standard layer for physics-active objects that should collide with characters and with each other. Characters live on the `Default` layer (layer 0), and Default ↔ RigidBody collision is enabled in the project's matrix by design.
  - `Rigidbody` — `m_Mass: 30`, `m_LinearDamping: 0`, `m_AngularDamping: 0.05`, `m_IsKinematic: 0`, `m_CollisionDetection: 0` (Discrete), `m_Interpolate: 0` (None)
  - Two `BoxCollider`s — physical 1×1×1 (the body), trigger 7×7×7 (`_interactionZone` for `ItemInteractable`)
  - `NetworkObject` present
  - **No `NavMeshObstacle`**
- **World scale**: 11 Unity units = 1.67m (project rule 32). The current 1×1×1 collider ≈ 15cm cube — reasonable for an apple-or-bigger item, oversized for a coin.

## 3. Decision

**Items become full physics objects with no kinematic locking. AI pathing decoupled from physics via per-item `NavMeshObstacle` (carve = true), enabled on first ground contact.**

Approaches rejected during brainstorming:
- **Layer-isolation (Character ↔ WorldItem disabled in Physics matrix)** — kills the self-unsticking mechanic. If a character gets placed inside an item, no way to push out.
- **Kinematic on ground via `FreezeOnGround`** — root cause of the current stuck bug; also kills the "explosion blasts items" vision.
- **Local avoidance only (NavMeshObstacle, carve=false)** — agents plan paths *through* item piles and only swerve at the last moment, triggering the existing stuck-detection / sliding code. Per-frame avoidance cost is per-(agent × nearby-item), not free.
- **Custom physics throttling (every-Nth-frame Rigidbody updates)** — Unity's built-in Rigidbody Sleep already handles this for free. Manual throttling would lose collision events on off-frames and thrash the engine. NavMeshObstacle carving is also already event-driven (stationary obstacles cost ~0).

## 4. Architecture

Three concerns, cleanly separated:

### 4.1 Physics layer

`WorldItem` keeps its existing **"RigidBody"** layer (layer 8) and existing non-kinematic `Rigidbody`. **No collision-matrix changes** — characters (Default layer) ↔ RigidBody is already enabled, which is the entire point of the layer. This is what allows characters to push items out from under themselves.

No manual setup required — the layer and matrix are already configured correctly.

### 4.2 Rigidbody tuning (prefab edit)

Current values are wrong for the new behavior model. Update the base `WorldItem_prefab.prefab`:

| Field | Current | New | Why |
|---|---|---|---|
| `m_Mass` | 30 | **2** | 30 ≈ a microwave at the project's scale — characters can't nudge it. 2 ≈ a real apple/bottle. |
| `m_LinearDamping` | 0 | **3** | At 0, a pushed item slides forever. 3 stops it within ~1s. |
| `m_AngularDamping` | 0.05 | **4** | At 0.05, items tumble forever after explosions. 4 settles them quickly. |
| `m_CollisionDetection` | 0 (Discrete) | **0** (Discrete) | No change. Continuous would be needed for tunneling-prone fast objects; items aren't fast. |
| `m_Interpolate` | 0 (None) | **0** (None) | No change. Interpolation is a perf cost we don't need for low-importance physics objects. |
| `m_IsKinematic` | 0 | **0** | No change. Items are dynamic always. |

Per-`ItemSO` overrides (mass, drag) can be added later if/when item-specific tuning is needed — out of scope for v1.

### 4.3 NavMeshObstacle (prefab edit + runtime enable)

Add a `NavMeshObstacle` component to the `WorldItem_prefab.prefab`:

- **Shape:** Box, `Size = (1, 1, 1)`, `Center = (0, 0.5, 0)` — sits on top of the physical box collider, doesn't carve below the floor.
- **Carving:** `Carve = true`, `Move Threshold = 0.1`, `Time To Stationary = 0.5`, `Carve Only Stationary = true`.
- **Enabled in prefab:** `false` (disabled).

Why disabled at spawn: an item that spawns in the air would carve a hole at its mid-air position for one or two frames before falling, leaving a phantom obstacle visible to agents during that window. Enabling on first collision avoids this entirely.

**Why these carving values:**
- `Move Threshold = 0.1` — Unity skips re-rasterizing the carved hole if the obstacle has moved less than 0.1u since last bake. Items getting nudged by a character bump generally stay within this — no carve update for tiny pushes.
- `Time To Stationary = 0.5` — when the item *does* move past threshold, Unity waits 0.5s of stillness before re-baking. Stops thrashing during a tumble.
- `Carve Only Stationary = true` — while moving, the obstacle contributes nothing to the navmesh (agents do local avoidance only). Re-carves once it settles.

This combination means an exploded item flying through the air costs ~0 NavMesh CPU during its flight, then takes one carve hit when it lands.

### 4.4 `WorldItem` runtime hook

Replace the current `OnCollisionEnter` body (`FreezeOnGround` / `isKinematic = true` block) with NavMeshObstacle activation:

```csharp
[SerializeField] private NavMeshObstacle _navMeshObstacle;
private bool _obstacleActivated = false;

private void Awake()
{
    SortingGroup = GetComponent<UnityEngine.Rendering.SortingGroup>();
    if (_navMeshObstacle == null) _navMeshObstacle = GetComponent<NavMeshObstacle>();
}

private void OnCollisionEnter(Collision collision)
{
    if (_obstacleActivated) return;
    if (_itemInstance == null || _itemInstance.ItemSO == null) return;
    if (!_itemInstance.ItemSO.BlocksPathing) return;
    if (_navMeshObstacle == null) return;

    _navMeshObstacle.enabled = true;
    _obstacleActivated = true;
}
```

Notes:
- One-shot — once enabled, stays enabled. No need to disable on subsequent collisions.
- Picked-up items have their `WorldItem` GameObject `Despawn()`-ed by `RequestInteractServerRpc` ([WorldItem.cs:282-326](../../../Assets/Scripts/Item/WorldItem.cs#L282-L326)) — destroys the `NavMeshObstacle` and closes its hole automatically.
- An item dropped while still mid-air (e.g., over a pit) won't enable until first collision — desired (no phantom obstacle in mid-air).
- An item that lands on *another item* (stacking) still triggers `OnCollisionEnter` — fine, both carve.

### 4.5 `ItemSO.BlocksPathing` flag

New serialized field on `ItemSO`:

```csharp
[Header("Pathing")]
[Tooltip("If true, this item gets a NavMeshObstacle when it lands so AI agents path around it. " +
         "Set to false for trivial items (trash, coins, single grain) that shouldn't litter the navmesh.")]
[SerializeField] private bool _blocksPathing = true;
public bool BlocksPathing => _blocksPathing;
```

Default `true` so the new behavior applies broadly; trash/junk items opt out.

### 4.6 `FreezeOnGround` removal

Delete:
- `WorldItem.FreezeOnGround` field (line 19)
- The `FreezeOnGround` branch in `OnCollisionEnter` (replaced by 4.4)
- `freeze` parameter from `CharacterDropItem.ExecutePhysicalDrop` ([CharacterDropItem.cs:56](../../../Assets/Scripts/Character/CharacterActions/CharacterDropItem.cs#L56))
- `freezeOnGround` parameter from the `CharacterDropItem` constructor (line 9)
- `freeze` parameter from `CharacterActions.RequestItemDropServerRpc` and the `spawnedItem.FreezeOnGround = freeze` assignment in [CharacterActions.cs:180](../../../Assets/Scripts/Character/CharacterActions/CharacterActions.cs#L180)
- The `bool freeze` field in the ServerRpc payload, if any

The mechanism has zero callers that *need* it — the only callers passed `freeze=true` to address the same self-stuck problem this spec solves at its root.

### 4.7 Network model

NavMeshObstacle is a **runtime-only, local-only component** — same model as `Building.RebuildWorldNavMesh()` ([Building.cs:182-203](../../../Assets/Scripts/World/Buildings/Building.cs#L182-L203)). The NavMesh is not networked; each peer maintains its own carved holes.

But the **trigger** — `OnCollisionEnter` — fires on every peer that has a `Rigidbody` + `Collider` on the object. With Unity Netcode and a synchronized `NetworkTransform`:
- **Server**: physics-authoritative for the WorldItem (Ownership=1 in the prefab = ServerOwner). Item falls, hits ground, `OnCollisionEnter` fires server-side, obstacle enables locally on server.
- **Clients**: receive the position via NetworkTransform but do **not** simulate physics on a server-owned NetworkObject's Rigidbody by default — they just interpolate the position. Their `OnCollisionEnter` may not fire.

This is a real concern: clients need to enable their *local* NavMeshObstacle so their *local* NavMeshAgents path around the item.

**Resolution:** add a server-driven `NetworkVariable<bool> _obstacleActive`. When `OnCollisionEnter` fires server-side and decides to enable the obstacle, set this to true. Subscribe to `OnValueChanged` on every peer (server included for symmetry) and enable the local `NavMeshObstacle` in response.

```csharp
private NetworkVariable<bool> _obstacleActive = new NetworkVariable<bool>(
    false,
    NetworkVariableReadPermission.Everyone,
    NetworkVariableWritePermission.Server);

public override void OnNetworkSpawn()
{
    _networkItemData.OnValueChanged += OnItemDataChanged;
    _obstacleActive.OnValueChanged += OnObstacleActiveChanged;

    // Late-joiner: apply current state immediately
    if (_obstacleActive.Value && _navMeshObstacle != null)
        _navMeshObstacle.enabled = true;

    if (IsClient && !IsServer)
        ApplyNetworkData(_networkItemData.Value);
}

public override void OnNetworkDespawn()
{
    _networkItemData.OnValueChanged -= OnItemDataChanged;
    _obstacleActive.OnValueChanged -= OnObstacleActiveChanged;
}

private void OnObstacleActiveChanged(bool _, bool active)
{
    if (_navMeshObstacle != null) _navMeshObstacle.enabled = active;
}

private void OnCollisionEnter(Collision collision)
{
    if (!IsServer) return; // server is authoritative for the trigger
    if (_obstacleActive.Value) return;
    if (_itemInstance == null || _itemInstance.ItemSO == null) return;
    if (!_itemInstance.ItemSO.BlocksPathing) return;

    _obstacleActive.Value = true; // syncs to all clients via OnValueChanged
}
```

Late-joiner support is built in: the `NetworkVariable.Value` is the latest state on connect, applied in `OnNetworkSpawn`.

## 5. Files to change

| File | Change |
|---|---|
| `Assets/Prefabs/Items/WorldItem_prefab.prefab` | Add `NavMeshObstacle` (disabled, carve=true, sized to collider). Update Rigidbody mass/drag. Wire `_navMeshObstacle` ref on `WorldItem`. |
| `Assets/Scripts/Item/WorldItem.cs` | Add `_navMeshObstacle` field + `_obstacleActive` NetworkVariable + `OnObstacleActiveChanged` + revised `OnCollisionEnter`. Remove `FreezeOnGround` field. |
| `Assets/Scripts/Item/Data/ItemSO.cs` (or wherever `ItemSO` lives) | Add `_blocksPathing` field + `BlocksPathing` property. |
| `Assets/Scripts/Character/CharacterActions/CharacterDropItem.cs` | Drop `_freezeOnGround` field, constructor parameter, and `freeze`-related call sites. |
| `Assets/Scripts/Character/CharacterActions/CharacterActions.cs` | Drop `freeze` from `RequestItemDropServerRpc` signature and body. |
| `Assets/Scripts/Character/Character.cs` (if it constructs `CharacterDropItem`) | Update construction to drop the freeze argument. |
| Project Settings → Physics → Layer Collision Matrix | No change — RigidBody (8) ↔ Default (0) already enabled. |

Search-and-replace expected to surface: any callers of `CharacterDropItem(ctor, freeze: ...)`, `RequestItemDropServerRpc(..., freeze)`, `WorldItem.FreezeOnGround = ...`. Implementation phase will enumerate exhaustively.

## 6. Documentation to update

Per project rules 28, 29, 29b:

- **`.agent/skills/item_system/SKILL.md`** — add a section on the WorldItem physics & pathing model: non-kinematic Rigidbody, NavMeshObstacle on first contact, `BlocksPathing` flag, `_obstacleActive` NetworkVariable.
- **`.agent/skills/navmesh-agent/SKILL.md`** or **`pathing-system/SKILL.md`** — note that WorldItems now contribute carved holes; document the move-threshold/stationary tuning so future devs don't fight it.
- **`wiki/systems/world-items.md`** — bump `updated`, add a Change log entry, update Public API (no more `FreezeOnGround`), add a Gotchas note about the layer-matrix dependency.
- **`.claude/agents/item-inventory-specialist.md`** — add the new behaviors to the agent's domain summary.

## 7. Test plan

**Manual gameplay tests:**

1. **Drop & walk away** — drop an item, walk in a circle around it. Item stays put, agent paths around the carved hole.
2. **Drop & walk through (stuck recovery)** — drop an item directly under a moving NPC. NPC should physically push the item aside and continue, not get stuck.
3. **Drop in narrow doorway** — drop an item in a 2u-wide gap between two buildings. Other NPCs should detour or push through depending on space.
4. **Stack drop** — drop 5 items in the same spot. They form a small pile, NPCs path around the pile.
5. **Trash item** — set `BlocksPathing = false` on a test item, drop, confirm NPCs walk straight through visually but the physics-push still works (they nudge it).
6. **Pickup** — pick up an item from the pile. Carved hole disappears. Other items stay put.
7. **Multiplayer (Host + Client)** — host drops an item; client's NavMeshAgent paths around it. Reverse: client drops, host's NPCs path around.
8. **Late joiner** — host drops 3 items; client connects after the drops. Client sees obstacles in correct positions.

**Automated network validation** (post-implementation):
- Run `network-validator` agent over the modified files. Confirm Host↔Client, Client↔Client, Host/Client↔NPC parity for the obstacle state and the dropped item itself.

## 8. Out of scope (separate spec)

- **Velocity-based impact damage** — fast-moving items or characters dealing damage on contact. Builds on top of the physics foundation in this spec but introduces its own architecture (`CharacterImpactReceiver`, kinetic-energy formula, server-authority for damage events, integration with `CharacterCombat.TakeDamage`). Will be filed as `2026-MM-DD-impact-damage-design.md`.
- **Per-`ItemSO` mass/drag overrides** — defer until a real item demands different physical feel.
- **Distance-based NavMeshObstacle hibernation** — defer until profiling shows carving cost is a problem. Would add hysteresis-based enable/disable around agent proximity.

## 9. Open questions

- **`ItemSO` location** — confirm path in implementation phase (likely `Assets/Scripts/Item/Data/ItemSO.cs`).
- **Character Rigidbody collision detection mode** — if characters are kinematic-driven by NavMeshAgent and items are dynamic, does the kinematic-vs-dynamic collision response push items reliably? Standard Unity behavior says yes (kinematic acts as infinite mass), but worth a spot-check in test #2.

## 10. Risks

- **Stacked items and pickup order**: if 10 items are piled in one spot, picking up the bottom one could let the pile collapse with a brief physics flurry. Acceptable — Rigidbody Sleep settles them in <1s.
- **Carving with many items at once**: dropping 50 items simultaneously into a small area enables 50 obstacles within a few frames → 50 carve events. Manageable today but a candidate for the deferred hibernation system if it ever becomes a real workload.
- **`_obstacleActive` syncing during high tick rates**: NetworkVariable updates are batched per tick — fine for one-shot enable. No race because the value only ever transitions false→true.
