# Door Lock System Design

## Overview

Add lockable/breakable doors to the game. Players and NPCs can lock/unlock doors with keys, break through breakable doors, and repair broken ones. Includes a Locksmith skill for key copying and a tier system for items and locks.

## New Files

| File | Type | Location | Purpose |
|------|------|----------|---------|
| `KeySO.cs` | ScriptableObject | `Assets/Data/Item/` | Key definition with `LockId` |
| `KeyInstance.cs` | Runtime class | `Assets/Scripts/Item/` | Key item instance |
| `DoorLock.cs` | NetworkBehaviour | `Assets/Scripts/World/MapSystem/` | Networked lock state, jiggle SFX, interior sound propagation |
| `DoorHealth.cs` | NetworkBehaviour | `Assets/Scripts/World/MapSystem/` | Networked HP, damage resistance, broken state, repair |
| `IDamageable.cs` | Interface | `Assets/Scripts/Combat/` | Shared damage interface for Characters and destructibles |

## Modified Files

| File | Change |
|------|--------|
| `ItemSO.cs` | Add `int Tier` field (default 0 = no tier) to base item class |
| `MapTransitionDoor.cs` | Query same-GameObject `DoorLock`/`DoorHealth` before allowing transition |
| `CharacterEquipment.cs` | Add `FindKeyForLock(string lockId, int requiredTier)` method |
| `CombatStyleAttack.cs` | Detect `IDamageable` in addition to `Character` on hit |
| `CraftingIngredient.cs` | Add `bool IsConsumed` field (default true) for reference inputs |
| `BuildingInteriorSpawner.cs` | Link paired `DoorLock` components + store exterior door ref in registry |

## New Assets

| Asset | Type | Location |
|-------|------|----------|
| Locksmith skill | `SkillSO` | `Assets/Resources/Data/Skills/Locksmith.asset` |
| Key items | `KeySO` | `Assets/Resources/Data/Item/Keys/` (per-door or per-lock-type) |

---

## 1. Item Tier System

### 1.1 Tier on ItemSO (Base Class)

Add an `int Tier` field to `ItemSO` (the base class for all items). Default value is `0` (no tier / tier-less). This makes tier available to all items but optional — only items where tier matters (keys, crafting materials, equipment) need a non-zero value.

```csharp
// In ItemSO.cs
[Header("Tier")]
[SerializeField] private int _tier = 0;
public int Tier => _tier;
```

Tier is a simple integer (1, 2, 3...) rather than an enum, allowing unlimited progression. Tier 0 means "untiered" — the item has no tier restriction.

### 1.2 Tier on DoorLock

`DoorLock` gets a `[SerializeField] int RequiredTier` field. A key must have `KeySO.Tier >= DoorLock.RequiredTier` to open the door. A door with `RequiredTier = 0` accepts any key with a matching `LockId` (no tier check).

### 1.3 Tier on Locksmith Skill

The Locksmith `SkillSO` defines which key tiers can be copied at each skill level:
- Novice (0–14): Can copy Tier 1 keys
- Intermediate (15–34): Can copy Tier 2 keys
- Advanced (35–54): Can copy Tier 3 keys
- Professional (55–74): Can copy Tier 4 keys
- Master (75–94): Can copy Tier 5 keys
- Legendary (95–100): Can copy any tier

The max copyable tier is derived from `SkillTier` — no extra data needed. The crafting station checks `locksmithSkillLevel >= tierThreshold` before allowing the copy.

---

## 2. Key System

### 2.1 KeySO

Extends `MiscSO` (keys are simple non-equipment items, same category as wood/stone). Lives in `Assets/Data/Item/`. Inherits `Tier` from `ItemSO` — a Tier 3 key opens Tier 1–3 doors with matching `LockId`.

```
KeySO : MiscSO
├── string LockId          // Shared identifier between key and door
├── (inherited) int Tier   // From ItemSO — determines which door tiers this key can open
├── override Type InstanceType => typeof(KeyInstance)
└── override ItemInstance CreateInstance() => new KeyInstance(this)
```

`LockId` is the link between a key and its door. Multiple `KeySO` assets can share the same `LockId` (copies). Multiple `KeyInstance`s of the same `KeySO` are also valid (duplicates in different inventories).

### 2.2 KeyInstance

Extends `MiscInstance`. No additional runtime fields needed — `LockId` and `Tier` live on the SO. Extending `MiscInstance` ensures keys are accepted by `MiscSlot.CanAcceptItem()` and follow the existing item hierarchy pattern.

```
KeyInstance : MiscInstance
└── KeySO KeyData => (KeySO)ItemSO
```

Keys are normal inventory items: can be stored in bags, carried in hands, dropped, traded, lost.

### 2.3 Key Lookup

New method on `CharacterEquipment`:

```csharp
public KeyInstance FindKeyForLock(string lockId, int requiredTier = 0)
```

Follows the same pattern as the existing `HasItemSO()` — scans inventory slots + `HandsController.CarriedItem` for any `KeyInstance` whose `KeySO.LockId` matches AND whose `KeySO.Tier >= requiredTier`. Returns the first match or null. If a character has multiple keys for the same lock, the first valid one is used (order: inventory slots first, then hands).

---

## 3. Door Lock

### 3.1 DoorLock Component

`NetworkBehaviour` added to the **same GameObject** as `MapTransitionDoor` (not a sibling or child — same GO, accessed via `GetComponent<DoorLock>()`).

**NetworkObject requirement:** `DoorLock` requires a `NetworkObject` in its parent hierarchy. For exterior doors on buildings, the building prefab must have a `NetworkObject` as an ancestor. For interior doors, this is satisfied by the interior prefab's root `NetworkObject` (spawned by `BuildingInteriorSpawner`).

```
DoorLock : NetworkBehaviour
├── [SerializeField] string LockId           // Must match KeySO.LockId
├── [SerializeField] int RequiredTier        // Key.Tier must be >= this to open (0 = any key)
├── [SerializeField] bool StartsLocked
├── [SerializeField] AudioClip JiggleSFX
├── [SerializeField] AudioClip UnlockSFX
├── [SerializeField] AudioClip LockSFX
├── NetworkVariable<bool> IsLocked
│
├── RequestUnlockServerRpc(ulong clientId)   // Client requests unlock
├── RequestLockServerRpc(ulong clientId)     // Client requests lock
├── PlayJiggleClientRpc()                    // Server broadcasts SFX to all clients
├── bool CanPass()                           // True if unlocked OR broken (checks sibling DoorHealth)
└── OnIsLockedChanged(bool prev, bool curr)  // NetworkVariable callback for toast/SFX
```

### 3.2 Paired Interior Door (Sound Propagation)

Instead of a direct C# reference (which wouldn't replicate), pairing uses the `BuildingInteriorRegistry`:

1. `BuildingInteriorRegistry.InteriorRecord` gains a new field: `NetworkObjectReference ExteriorDoorNetRef` — set when the building is placed.
2. When `BuildingInteriorSpawner` configures exit doors, it also stores the interior exit door's `NetworkObjectReference` back into the record.
3. When `DoorLock.PlayJiggleClientRpc()` fires, it looks up the paired door via the registry (server resolves the NetworkObjectReference and sends a second ClientRpc to the paired door).

This avoids direct cross-object C# references that don't survive NGO spawning.

### 3.3 Interaction Flow

**MapTransitionDoor.Interact(Character interactor)** gains an early guard:

```
DoorLock doorLock = GetComponent<DoorLock>();
DoorHealth doorHealth = GetComponent<DoorHealth>();

// Broken doors are always passable (lock bypassed)
if (doorHealth != null && doorHealth.IsBroken.Value)
    → proceed with transition

// Locked door check
if (doorLock != null && doorLock.IsLocked.Value)
    KeyInstance key = interactor.CharacterEquipment.FindKeyForLock(doorLock.LockId, doorLock.RequiredTier);
    if (key != null)
        → doorLock.RequestUnlockServerRpc(interactor.OwnerClientId)
        → return (don't walk through — toast shown via NetworkVariable callback)
    else
        → doorLock.RequestJiggleServerRpc()
        → return

// Unlocked, not broken → normal transition
→ proceed with transition
```

**Toast/SFX timing:** Unlock/lock state changes fire via `NetworkVariable<bool>.OnValueChanged` callback. The toast ("Door unlocked" / "Door locked") is shown in this callback, ensuring it only appears after the server has confirmed the state change. This avoids premature feedback from async RPCs.

**Hold interaction menu** (via `GetHoldInteractionOptions`):

```
if (doorLock != null && interactor has matching key)
    if (doorLock.IsLocked.Value)
        → option: "Unlock" → calls RequestUnlockServerRpc
    else
        → option: "Lock" → calls RequestLockServerRpc

if (doorHealth != null && doorHealth.IsBroken.Value)
    if (interactor has repair materials)
        → option: "Repair" → calls RequestRepairServerRpc
```

### 3.4 Networking

`IsLocked` is a `NetworkVariable<bool>` with server write permission. All lock/unlock operations go through ServerRpc → server validates → modifies NetworkVariable → all clients see the update. No MonoBehaviour replication issues.

`OnNetworkSpawn`: sets `IsLocked.Value = StartsLocked` (server only).

---

## 4. Breakable Doors

### 4.1 IDamageable Interface

New interface that both `Character` (via `CharacterCombat`) and `DoorHealth` implement:

```csharp
public interface IDamageable
{
    void TakeDamage(float damage, Character attacker);
    bool CanBeDamaged();
}
```

`CombatStyleAttack.OnTriggerEnter()` is updated to detect `IDamageable` in addition to the existing `Character` check. This allows the existing attack system to damage doors without a dedicated attack action.

### 4.2 DoorHealth Component

`NetworkBehaviour` on the same GameObject as `MapTransitionDoor` and `DoorLock`.

```
DoorHealth : NetworkBehaviour, IDamageable
├── [SerializeField] bool IsBreakable
├── [SerializeField] float MaxHealth
├── [SerializeField] float DamageResistance    // 0.0–1.0, percentage reduction
├── [SerializeField] List<CraftingIngredient> RepairMaterials
├── NetworkVariable<float> CurrentHealth
├── NetworkVariable<bool> IsBroken
│
├── void TakeDamage(float damage, Character attacker)  // Server-only
│   → if (!IsBreakable || IsBroken.Value) return
│   → effectiveDamage = damage * (1 - DamageResistance)
│   → CurrentHealth -= effectiveDamage
│   → if CurrentHealth <= 0: IsBroken = true
│
├── RequestRepairServerRpc(ulong clientId)              // Client requests repair
│   → validate: character has required materials
│   → consume materials via inventory RemoveItem per ingredient
│   → CurrentHealth = MaxHealth
│   → IsBroken = false
│   → DoorLock.IsLocked is NOT changed (lock state preserved)
│
└── bool CanBeDamaged()
    → return IsBreakable && !IsBroken.Value
```

### 4.3 Locked AND Broken State

When a door breaks, `IsLocked` is **not** modified — it remains at whatever value it had. The `IsBroken` flag takes priority: `DoorLock.CanPass()` returns `true` if `!IsLocked.Value || (doorHealth != null && doorHealth.IsBroken.Value)`.

When repaired, `IsBroken` becomes `false` and the preserved `IsLocked` value takes effect again. A door that was locked before being broken returns to locked after repair.

### 4.4 States

| State | Visual | Passable | Lockable |
|-------|--------|----------|----------|
| Healthy + Unlocked | Normal | Yes | Yes |
| Healthy + Locked | Normal | Key holders only | Yes |
| Damaged + Unlocked | Normal (cracked later) | Yes | Yes |
| Damaged + Locked | Normal (cracked later) | Key holders only | Yes |
| Broken | Destroyed/open | Yes (anyone) | No (must repair first) |

### 4.5 Targeting

Unbreakable doors (`IsBreakable = false`): `CanBeDamaged()` returns false. The attack system skips them.

Breakable doors: The `IDamageable` interface makes them valid targets for `CombatStyleAttack`. The door collider serves as the hit zone.

### 4.6 Repair

- Hold E on broken door → "Repair" option (if character has required materials)
- Server validates materials via `CharacterEquipment.GetInventory()` and consumes them with `Inventory.RemoveItem()` for each `CraftingIngredient` in `RepairMaterials`
- Can optionally tie into Locksmith skill for speed bonus (future enhancement)
- NPCs with materials can also repair (GOAP action, future scope)

---

## 5. Locksmith Skill & Key Copying

### 5.1 SkillSO Asset

Created as `Assets/Resources/Data/Skills/Locksmith.asset`:
- **Stat influences:** Dexterity, Intelligence
- **Level bonuses:** Standard progression milestones

### 5.2 Key Copying

Performed at a crafting station (same system as existing crafting):
- **Input:** Original key (not consumed — marked with `IsConsumed = false` on the `CraftingIngredient`)
- **Skill required:** Locksmith
- **Tier gate:** Locksmith skill level must meet the key's tier threshold (see Section 1.3). A Novice locksmith cannot copy a Tier 3 key.
- **Output:** New `KeyInstance` with the same `LockId` and `Tier`
- **Skill effect:** Higher Locksmith level → faster crafting speed + unlocks higher tier copying

**CraftingIngredient change:** Add `bool IsConsumed = true` field to `CraftingIngredient`. Existing recipes default to consuming all ingredients (backward compatible). For key copying, the original key ingredient has `IsConsumed = false` — it's checked for presence but not removed from inventory.

The crafting recipe lives on the `KeySO` itself (inherited from `ItemSO._craftingRecipe`). The `_requiredCraftingSkill` field points to the Locksmith `SkillSO`.

### 5.3 NPC Key Copying

NPCs use the same crafting system. An NPC with Locksmith skill and access to a crafting station can copy keys. This enables NPC locksmiths as a job/service.

---

## 6. NPC Parity

All door interaction logic operates on `Character`, not player-specific classes. NPCs follow the same rules:

- NPC with key → can unlock/lock doors
- NPC without key + locked door → blocked (pathfinding should account for this)
- NPC can attack breakable doors
- NPC with materials + skill → can repair broken doors

**Future GOAP actions** (out of initial implementation scope, but API designed to support):
- `GoapAction_UnlockDoor` — NPC approaches locked door, uses key to unlock
- `GoapAction_BreakDoor` — Hostile NPC attacks breakable locked door
- `GoapAction_RepairDoor` — NPC with materials repairs broken door
- `GoapAction_CopyKey` — NPC locksmith copies a key at crafting station

---

## 7. Interior Sound Propagation

When a locked exterior door is jiggled:
1. Client calls `RequestJiggleServerRpc()` on the exterior `DoorLock`
2. Server sends `PlayJiggleClientRpc()` — all clients near the exterior hear the SFX
3. Server looks up the paired interior door via `BuildingInteriorRegistry` (using stored `NetworkObjectReference`)
4. If found, server sends `PlayJiggleClientRpc()` on the interior `DoorLock` — players inside hear rattling

This works because the server has references to both doors. Remote clients only receive the ClientRpc and play audio locally.

---

## 8. Persistence & Hibernation

Door lock/health state must survive map hibernation cycles.

**Problem:** When a map hibernates, if the door's `NetworkObject` is despawned, `NetworkVariable` state is lost.

**Solution:** Before hibernation, `MapController.Hibernate()` serializes door states:
- For each `DoorLock` in the map: save `{LockId, IsLocked}` to the map's hibernation data
- For each `DoorHealth` in the map: save `{CurrentHealth, IsBroken}` to the map's hibernation data

On `MapController.WakeUp()`, restore saved state to the `NetworkVariable`s.

For building interiors (lazy-spawned), the `BuildingInteriorRegistry.InteriorRecord` already persists across the session. Add `bool IsLocked` and `float DoorHealth` fields to the record. The spawner reads these when re-instantiating an interior.

---

## 9. Implementation Order

1. **ItemSO.Tier** — Add tier field to base item class
2. **KeySO + KeyInstance** — Data model, extend MiscSO/MiscInstance hierarchy
3. **FindKeyForLock** — CharacterEquipment method (with tier check)
4. **IDamageable** — Interface + update CombatStyleAttack
5. **DoorLock** — NetworkBehaviour, lock/unlock RPCs, NetworkVariable, SFX, RequiredTier
6. **DoorHealth** — Breakable/repair logic, IDamageable implementation
7. **MapTransitionDoor integration** — Guard + hold menu options
8. **CraftingIngredient.IsConsumed** — Add field for non-consumed reference inputs
9. **Locksmith SkillSO** — Asset creation with tier thresholds
10. **Key copying recipe** — Crafting integration with IsConsumed=false + tier gate
11. **BuildingInteriorSpawner** — Paired door linking via registry
12. **Hibernation persistence** — Serialize/restore door state
13. **GOAP actions** — Future scope, not in initial implementation
