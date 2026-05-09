---
name: door-lock-system
description: Lockable, breakable, tier-gated doors with keys, paired door sync, and Locksmith skill for key copying.
---

# Door Lock System

Doors (`MapTransitionDoor` and `BuildingInteriorDoor`) can be locked, unlocked with keys, broken through, and repaired. Two optional `NetworkBehaviour` components — `DoorLock` and `DoorHealth` — are added to the same GameObject as the door.

## When to use this skill
- When adding locks to new door types or buildings.
- When modifying key/lock interactions or tier gating.
- When debugging door state sync in multiplayer (lock state, jiggle SFX, paired doors).
- When implementing NPC door interactions (GOAP actions for unlock, break, repair).

## Architecture Overview

```
MapTransitionDoor (or BuildingInteriorDoor)
├── DoorLock : NetworkBehaviour       (optional — lock/unlock/jiggle)
├── DoorHealth : NetworkBehaviour, IDamageable  (optional — breakable/repair)
└── AudioSource                        (auto-created by DoorLock if missing)
```

Both components sit on the **same GameObject** as the door. They use the parent hierarchy's `NetworkObject` (e.g., the building root's NetworkObject). **Do NOT add a separate NetworkObject to the door child** — NGO does not support nested NetworkObjects for dynamically spawned objects.

## Key Files

| File | Purpose |
|------|---------|
| `Assets/Scripts/World/MapSystem/DoorLock.cs` | Networked lock state, RPCs, SFX, paired door sync |
| `Assets/Scripts/World/MapSystem/DoorHealth.cs` | Networked HP, damage resistance, breakable/repair, implements `IDamageable` |
| `Assets/Scripts/Combat/IDamageable.cs` | Shared damage interface for Characters and destructibles |
| `Assets/Data/Item/KeySO.cs` | Key ScriptableObject with `LockId`, extends `MiscSO` |
| `Assets/Scripts/Item/KeyInstance.cs` | Key runtime instance with runtime `LockId` override |
| `Assets/Scripts/World/MapSystem/MapTransitionDoor.cs` | Lock/broken guard in `Interact()`, hold menu options |
| `Assets/Scripts/World/Buildings/BuildingInteriorDoor.cs` | Same lock guard for building entry doors |

---

## 1. DoorLock Component

### NetworkVariable
- `NetworkVariable<bool> IsLocked` — server-authoritative, replicated to all clients.

### Serialized Fields
- `string _lockId` — links door to matching keys. **Leave empty on building doors** — auto-derived from `Building.BuildingId` at runtime.
- `int _requiredTier` — key must have `Tier >= RequiredTier` to open. 0 = any key.
- `bool _startsLocked` — initial state on spawn.
- `AudioClip _jiggleSFX`, `_unlockSFX`, `_lockSFX` — 3D spatial audio.

### LockId Auto-Generation (Buildings)
Each building instance has a unique `BuildingId` (GUID). On `OnNetworkSpawn()`, if `_lockId` is empty, `DoorLock` auto-derives it from `GetComponentInParent<Building>().BuildingId`. Interior exit doors get the same `LockId` set by `BuildingInteriorSpawner.SetLockId(record.BuildingId)` before spawn.

This means: **same prefab, different lock per instance.** A key for House A does not open House B.

### Static LockId Registry
All spawned `DoorLock` instances register in a static `Dictionary<string, List<DoorLock>>` keyed by `LockId`. This enables:
- **Paired door sync**: Lock/unlock on one door propagates to all doors with the same `LockId`.
- **Bidirectional jiggle**: Jiggle SFX plays on all paired doors.
- **Auto-cleanup**: Unregisters on `OnNetworkDespawn()`.

### RPCs
- `RequestUnlockServerRpc()` — sets `IsLocked = false` on this door + all paired doors.
- `RequestLockServerRpc()` — sets `IsLocked = true` (blocked if door is broken).
- `RequestJiggleServerRpc()` — plays jiggle SFX on this door + all paired doors via `PlayJiggleClientRpc()`.

### IsSpawned Guards
All `NetworkVariable` reads and RPC calls in `MapTransitionDoor`/`BuildingInteriorDoor` are guarded with `doorLock.IsSpawned` to handle cases where the `NetworkObject` hasn't spawned yet.

### Inspector Debug
Right-click the `DoorLock` component header → **"Lock"** or **"Unlock"** context menu items. Goes through `SetLockedStateWithSync()` so paired doors update.

---

## 2. DoorHealth Component

Implements `IDamageable` interface. Makes doors attackable by the existing combat system.

### NetworkVariables
- `NetworkVariable<float> CurrentHealth` — current HP.
- `NetworkVariable<bool> IsBroken` — true when HP reaches 0.

### Serialized Fields
- `bool _isBreakable` — if false, `CanBeDamaged()` returns false.
- `float _maxHealth` — starting HP.
- `float _damageResistance` — 0.0–1.0, percentage of incoming damage absorbed.
- `List<CraftingIngredient> _repairMaterials` — materials consumed on repair.

### Damage
`TakeDamage(float damage, Character attacker)` — server-only. Applies `damage * (1 - resistance)`. Sets `IsBroken = true` when HP <= 0.

### Repair
`RequestRepairServerRpc()` — validates the repairer has all required materials, consumes them, restores full HP, clears broken state. Lock state is preserved (a door locked before breaking returns to locked after repair).

### States

| State | Passable | Lockable |
|-------|----------|----------|
| Healthy + Unlocked | Yes | Yes |
| Healthy + Locked | Key holders only | Yes |
| Broken | Yes (anyone) | No (must repair first) |

---

## 3. Key System

### KeySO (`MiscSO` subclass)
- `string _lockId` — for static doors (dungeons, quest doors). Leave empty for building keys.
- Inherits `int Tier` from `ItemSO` — determines which door tiers this key can open.

### KeyInstance (`MiscInstance` subclass)
- `string _runtimeLockId` — runtime override, set via `SetLockId(string)`.
- `string LockId` property — returns runtime override if set, else `KeySO.LockId`.
- For building keys: create from a generic "House Key" SO, then call `key.SetLockId(building.BuildingId)`.

### Key Lookup
`CharacterEquipment.FindKeyForLock(string lockId, int requiredTier = 0)`:
- Scans inventory slots, then hands (`HandsController.CarriedItem`).
- Matches on `KeyInstance.LockId == lockId && KeySO.Tier >= requiredTier`.
- Returns first match or null.

---

## 4. Interaction Flow

### Tap E (Quick Action)
1. If door is broken → skip lock check, proceed with transition.
2. If door is locked + player has key → `RequestUnlockServerRpc()`, return (don't transition).
3. If door is locked + no key → `RequestJiggleServerRpc()`, return.
4. If door is unlocked → normal transition.

### Hold E (Extended Options)
- **"Unlock"** — shown when locked, player has key, door not broken.
- **"Lock"** — shown when unlocked, player has key, door not broken.
- **"Repair"** — shown when door is broken.

---

## 5. Item Tier System

`ItemSO` base class has `int _tier` (default 0 = untiered). Used by:
- **Keys**: `KeySO.Tier >= DoorLock.RequiredTier` to open.
- **Locksmith**: Skill tier determines max key tier that can be copied.

---

## 6. Locksmith Skill

`SkillSO` asset at `Assets/Resources/Data/Skills/Locksmith.asset`.
- **Stat influences**: Dexterity (0.5), Intelligence (0.3).
- **Tier gating** via `SkillTier.GetMaxCopyableTier()`:
  - Novice → Tier 1, Intermediate → Tier 2, ..., Legendary → any tier.
- **Key copying**: Crafting recipe with original key as `IsReferenceOnly = true` (not consumed).

---

## 7. Persistence & Hibernation

`BuildingInteriorRegistry.InteriorRecord` stores:
- `bool IsLocked` — persisted lock state.
- `float DoorCurrentHealth` — persisted health (-1 = use prefab default).

**Write path (server-authoritative):**
- `DoorLock.SetLockedStateWithSync` calls `PersistLockState` after every lock/unlock — writes the new value into the matching record (keyed by `lockId == BuildingId`).
- `DoorHealth.CurrentHealth.OnValueChanged` (gated on `IsServer`) calls `PersistHealthState` — writes new HP into the same record.
- Both helpers are no-ops if the registry has no record yet for that lockId (e.g. before first interior entry).

**Read path (server-authoritative):**
- `DoorLock.OnNetworkSpawn` prefers `record.IsLocked` over the authored `_startsLocked` default when a record exists.
- `DoorHealth.OnNetworkSpawn` prefers `record.DoorCurrentHealth` over `_maxHealth` when `>= 0`.
- `BuildingInteriorSpawner` re-applies both values after `NetworkObject.Spawn()` for interior doors (defensive — `OnNetworkSpawn` already covers this for any door whose record exists at spawn time).

**Restore-time race fix (exterior doors):**
- Exterior building doors spawn from the scene **before** `BuildingInteriorRegistry.RestoreState` runs (ISaveable ordering), so `OnNetworkSpawn` finds an empty registry and falls back to defaults.
- `BuildingInteriorRegistry.RestoreState` calls `DoorLock.ApplyLockState(lockId, isLocked)` + `DoorHealth.ApplyHealthState(lockId, health)` for each restored record to retroactively patch already-spawned doors.

**Pre-record snapshot (unlock-before-first-entry):**
- The registry record is lazy-created on first interior entry. If a player unlocks the exterior door before ever entering, the record doesn't exist yet — there's nothing to write to.
- `RegisterInterior` snapshots the live exterior door state via `DoorLock.GetCurrentLockState(buildingId)` + `DoorHealth.GetCurrentHealth(buildingId)` so the new record inherits the live values rather than reverting to field defaults.

---

## 8. Combat Integration

`IDamageable` interface (`TakeDamage`, `CanBeDamaged`) is detected by `CombatStyleAttack.OnTriggerEnter()` as a fallback after the existing `Character` detection. Damage is applied immediately (doors don't go through battle priority). Only breakable, non-broken doors receive damage.

---

## Important Gotchas

- **No nested NetworkObjects**: `DoorLock`/`DoorHealth` use the parent building's `NetworkObject`. Never add a `NetworkObject` to the door child.
- **IsSpawned guards**: Always check `doorLock.IsSpawned` before reading `NetworkVariable.Value` or calling RPCs.
- **Empty LockId on prefabs**: Building door prefabs must have empty `_lockId` — it's auto-generated from `BuildingId` per instance.
- **Interior door LockId**: Set by `BuildingInteriorSpawner` before `Spawn()`, not by the prefab.
- **Key runtime LockId**: Building keys need `KeyInstance.SetLockId(buildingId)` at creation time. The `KeySO.LockId` is for static doors only.
- **CraftingIngredient.IsReferenceOnly**: Default `false` (consumed). Set `true` for key copying recipes where the original key is a reference input.
