# Door Lock System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add lockable, breakable, tier-gated doors with key items and a Locksmith skill for key copying.

**Architecture:** Doors gain two optional `NetworkBehaviour` components (`DoorLock`, `DoorHealth`) on the same GameObject as `MapTransitionDoor`. Keys extend the `MiscSO`/`MiscInstance` item hierarchy. A base `Tier` field on `ItemSO` provides universal item tiering. Lock/health state is networked via `NetworkVariable`s, with hibernation persistence via `MapController`.

**Tech Stack:** Unity 2022+, Netcode for GameObjects (NGO), C# ScriptableObjects

**Spec:** `docs/superpowers/specs/2026-03-26-door-lock-system-design.md`

---

## File Map

### New Files
| File | Responsibility |
|------|---------------|
| `Assets/Data/Item/KeySO.cs` | Key ScriptableObject with `LockId`, extends `MiscSO` |
| `Assets/Scripts/Item/KeyInstance.cs` | Key runtime instance, extends `MiscInstance` |
| `Assets/Scripts/World/MapSystem/DoorLock.cs` | Networked lock state, RPCs, SFX, tier check |
| `Assets/Scripts/World/MapSystem/DoorHealth.cs` | Networked HP, damage resistance, breakable/repair |
| `Assets/Scripts/Combat/IDamageable.cs` | Shared damage interface for Characters + destructibles |

### Modified Files
| File | Change |
|------|--------|
| `Assets/Data/Item/ItemSO.cs` | Add `[SerializeField] int _tier` field + property |
| `Assets/Scripts/Character/CharacterEquipment/CharacterEquipment.cs` | Add `FindKeyForLock(string, int)` method |
| `Assets/Scripts/World/MapSystem/MapTransitionDoor.cs` | Lock/broken guard in `Interact()`, hold menu in `GetHoldInteractionOptions()` |
| `Assets/Scripts/Character/CharacterCombat/CombatStyleAttack.cs` | Detect `IDamageable` in `OnTriggerEnter()` |
| `Assets/Scripts/Character/CharacterSkills/SkillTier.cs` | Add `GetMaxCopyableTier()` extension method |
| `Assets/Scripts/World/Buildings/BuildingInteriorRegistry.cs` | Add `IsLocked`, `DoorCurrentHealth` fields to `InteriorRecord` |
| `Assets/Scripts/World/Buildings/BuildingInteriorSpawner.cs` | Link `DoorLock` components, read persisted lock/health state |

---

## Task 1: Add Tier Field to ItemSO

**Files:**
- Modify: `Assets/Data/Item/ItemSO.cs:16` (after `_weight` field)

- [ ] **Step 1: Add the tier field and property**

In `Assets/Data/Item/ItemSO.cs`, add after line 16 (`private ItemWeight _weight`):

```csharp
[Header("Tier")]
[Tooltip("Item tier level. 0 = untiered. Higher tiers unlock progression-gated content.")]
[SerializeField] private int _tier = 0;
```

And add the property after line 26 (`public ItemWeight Weight => _weight;`):

```csharp
public int Tier => _tier;
```

- [ ] **Step 2: Verify compilation**

Open Unity, wait for script recompilation. Confirm no errors in Console.

- [ ] **Step 3: Verify existing assets unaffected**

Open any existing ItemSO asset (e.g., `Assets/Resources/Data/Item/Misc/001-Wood.asset`) in the Inspector. Confirm the new "Tier" header appears with value 0. No data loss on existing assets.

- [ ] **Step 4: Commit**

```bash
git add Assets/Data/Item/ItemSO.cs
git commit -m "feat: add Tier field to ItemSO base class"
```

---

## Task 2: Create KeySO and KeyInstance

**Files:**
- Create: `Assets/Data/Item/KeySO.cs`
- Create: `Assets/Scripts/Item/KeyInstance.cs`

- [ ] **Step 1: Create KeyInstance**

Create `Assets/Scripts/Item/KeyInstance.cs`:

```csharp
using UnityEngine;

[System.Serializable]
public class KeyInstance : MiscInstance
{
    public KeyInstance(ItemSO data) : base(data) { }

    /// <summary>
    /// Typed accessor for the KeySO data. Returns null if ItemSO is not a KeySO.
    /// </summary>
    public KeySO KeyData => ItemSO as KeySO;
}
```

- [ ] **Step 2: Create KeySO**

Create `Assets/Data/Item/KeySO.cs`:

```csharp
using UnityEngine;

[CreateAssetMenu(fileName = "KeyItem", menuName = "Scriptable Objects/Items/Key")]
public class KeySO : MiscSO
{
    [Header("Key Settings")]
    [Tooltip("Shared ID between this key and compatible DoorLock components.")]
    [SerializeField] private string _lockId;

    public string LockId => _lockId;

    public override System.Type InstanceType => typeof(KeyInstance);
    public override ItemInstance CreateInstance() => new KeyInstance(this);
}
```

- [ ] **Step 3: Verify compilation**

Open Unity, wait for recompilation. No errors.

- [ ] **Step 4: Create a test key asset**

In Unity: Right-click `Assets/Resources/Data/Item/` → Create folder `Keys`. Right-click `Keys/` → Create → Scriptable Objects → Items → Key. Name it `TestKey`. Set LockId to `test_lock_001`, Tier to `1`.

- [ ] **Step 5: Commit**

```bash
git add Assets/Data/Item/KeySO.cs Assets/Scripts/Item/KeyInstance.cs
git commit -m "feat: add KeySO and KeyInstance for door lock keys"
```

---

## Task 3: Add FindKeyForLock to CharacterEquipment

**Files:**
- Modify: `Assets/Scripts/Character/CharacterEquipment/CharacterEquipment.cs` (after `HasItemSO` method, ~line 745)

- [ ] **Step 1: Add the FindKeyForLock method**

Add after the `HasItemSO` method (after line 745) in `CharacterEquipment.cs`:

```csharp
/// <summary>
/// Searches inventory and hands for a KeyInstance whose LockId matches
/// and whose Tier meets or exceeds the required tier.
/// Returns the first match or null.
/// </summary>
public KeyInstance FindKeyForLock(string lockId, int requiredTier = 0)
{
    if (string.IsNullOrEmpty(lockId)) return null;

    // Check bag inventory first
    var inventory = GetInventory();
    if (inventory != null)
    {
        foreach (var slot in inventory.ItemSlots)
        {
            if (slot.IsEmpty()) continue;
            if (slot.ItemInstance is KeyInstance key &&
                key.KeyData != null &&
                key.KeyData.LockId == lockId &&
                key.KeyData.Tier >= requiredTier)
            {
                return key;
            }
        }
    }

    // Check hands
    var handsController = _character.CharacterVisual?.BodyPartsController?.HandsController;
    if (handsController != null &&
        handsController.CarriedItem is KeyInstance handKey &&
        handKey.KeyData != null &&
        handKey.KeyData.LockId == lockId &&
        handKey.KeyData.Tier >= requiredTier)
    {
        return handKey;
    }

    return null;
}
```

- [ ] **Step 2: Verify compilation**

Open Unity, wait for recompilation. No errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Character/CharacterEquipment/CharacterEquipment.cs
git commit -m "feat: add FindKeyForLock method to CharacterEquipment"
```

---

## Task 4: Create IDamageable Interface

**Files:**
- Create: `Assets/Scripts/Combat/IDamageable.cs`

- [ ] **Step 1: Create the interface**

Create `Assets/Scripts/Combat/IDamageable.cs`:

```csharp
/// <summary>
/// Shared interface for anything that can receive damage: Characters, doors, destructibles.
/// </summary>
public interface IDamageable
{
    void TakeDamage(float damage, Character attacker);
    bool CanBeDamaged();
}
```

- [ ] **Step 2: Verify compilation**

Open Unity, wait for recompilation. No errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Combat/IDamageable.cs
git commit -m "feat: add IDamageable interface for shared damage handling"
```

---

## Task 5: Update CombatStyleAttack to Detect IDamageable

**Files:**
- Modify: `Assets/Scripts/Character/CharacterCombat/CombatStyleAttack.cs:149-164` (OnTriggerEnter method)

- [ ] **Step 1: Add IDamageable detection alongside existing Character detection**

In `CombatStyleAttack.cs`, modify the `OnTriggerEnter` method. After the existing Character detection logic (line 156-164), add a fallback check for `IDamageable` on non-Character objects. Replace the method body:

```csharp
private void OnTriggerEnter(Collider other)
{
    // ONLY the Server registers potential targets
    if (Unity.Netcode.NetworkManager.Singleton != null &&
        Unity.Netcode.NetworkManager.Singleton.IsListening &&
        !Unity.Netcode.NetworkManager.Singleton.IsServer) return;

    // Existing Character detection
    Character target = other.GetComponentInParent<Character>();

    if (target != null)
    {
        if (target == _character) return;
        if (!target.IsAlive()) return;
        if (_potentialTargets.Contains(target)) return;

        _potentialTargets.Add(target);
        return;
    }

    // Non-Character IDamageable (doors, destructibles)
    IDamageable damageable = other.GetComponentInParent<IDamageable>();
    if (damageable != null && damageable.CanBeDamaged())
    {
        // Directly apply damage since doors don't go through battle priority
        damageable.TakeDamage(_finalDamage, _character);
    }
}
```

**Note:** `_finalDamage` is already computed in `ApplyHit()` setup. Check the exact field name in the file — it may be `_attackData.Damage` or a computed value. Read the `ApplyHit` method to confirm the damage field name and use the correct one. If damage is only computed during `ApplyHit()` iteration, store a base damage reference or compute it at trigger time.

- [ ] **Step 2: Verify compilation**

Open Unity, wait for recompilation. No errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Character/CharacterCombat/CombatStyleAttack.cs
git commit -m "feat: CombatStyleAttack detects IDamageable for destructible objects"
```

---

## Task 6: Create DoorLock NetworkBehaviour

**Files:**
- Create: `Assets/Scripts/World/MapSystem/DoorLock.cs`

- [ ] **Step 1: Create the DoorLock component**

Create `Assets/Scripts/World/MapSystem/DoorLock.cs`:

```csharp
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Networked lock component for doors. Add to the same GameObject as MapTransitionDoor.
/// Requires a NetworkObject in the parent hierarchy.
/// </summary>
public class DoorLock : NetworkBehaviour
{
    [Header("Lock Settings")]
    [SerializeField] private string _lockId;
    [SerializeField] private int _requiredTier = 0;
    [SerializeField] private bool _startsLocked = true;

    [Header("Audio")]
    [SerializeField] private AudioClip _jiggleSFX;
    [SerializeField] private AudioClip _unlockSFX;
    [SerializeField] private AudioClip _lockSFX;

    public NetworkVariable<bool> IsLocked = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public string LockId => _lockId;
    public int RequiredTier => _requiredTier;

    private AudioSource _audioSource;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer)
        {
            IsLocked.Value = _startsLocked;
        }

        IsLocked.OnValueChanged += OnIsLockedChanged;

        _audioSource = GetComponent<AudioSource>();
        if (_audioSource == null)
        {
            _audioSource = gameObject.AddComponent<AudioSource>();
            _audioSource.spatialBlend = 1f; // 3D sound
            _audioSource.playOnAwake = false;
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        IsLocked.OnValueChanged -= OnIsLockedChanged;
    }

    /// <summary>
    /// Returns true if the door can be passed through (unlocked OR broken).
    /// </summary>
    public bool CanPass()
    {
        // Broken doors are always passable
        var doorHealth = GetComponent<DoorHealth>();
        if (doorHealth != null && doorHealth.IsBroken.Value)
            return true;

        return !IsLocked.Value;
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestUnlockServerRpc(ServerRpcParams rpcParams = default)
    {
        if (!IsLocked.Value) return;
        IsLocked.Value = false;
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestLockServerRpc(ServerRpcParams rpcParams = default)
    {
        if (IsLocked.Value) return;

        // Can't lock a broken door
        var doorHealth = GetComponent<DoorHealth>();
        if (doorHealth != null && doorHealth.IsBroken.Value) return;

        IsLocked.Value = true;
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestJiggleServerRpc(ServerRpcParams rpcParams = default)
    {
        PlayJiggleClientRpc();

        // Propagate to paired interior door via registry
        PropagateJiggleToPairedDoor();
    }

    [ClientRpc]
    private void PlayJiggleClientRpc()
    {
        if (_audioSource != null && _jiggleSFX != null)
        {
            _audioSource.PlayOneShot(_jiggleSFX);
        }
    }

    private void OnIsLockedChanged(bool previousValue, bool newValue)
    {
        if (_audioSource == null) return;

        if (newValue && _lockSFX != null)
            _audioSource.PlayOneShot(_lockSFX);
        else if (!newValue && _unlockSFX != null)
            _audioSource.PlayOneShot(_unlockSFX);
    }

    /// <summary>
    /// Server-only: find the paired door via BuildingInteriorRegistry and play jiggle there too.
    /// </summary>
    private void PropagateJiggleToPairedDoor()
    {
        if (!IsServer) return;
        if (BuildingInteriorRegistry.Instance == null) return;

        // Find the record for this door's building
        var record = BuildingInteriorRegistry.Instance.FindRecordByDoorPosition(transform.position);
        if (record == null) return;

        // Find the interior MapController and its DoorLock
        var interiorMap = MapController.GetByMapId(record.InteriorMapId);
        if (interiorMap == null) return;

        var pairedLock = interiorMap.GetComponentInChildren<DoorLock>();
        if (pairedLock != null && pairedLock != this)
        {
            pairedLock.PlayJiggleClientRpc();
        }
    }
}
```

- [ ] **Step 2: Verify compilation**

Open Unity, wait for recompilation. Confirm no errors. (Note: `FindRecordByDoorPosition` will be added to `BuildingInteriorRegistry` in Task 11. For now this method won't be found — add a stub or accept the compile error to fix in Task 11.)

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/World/MapSystem/DoorLock.cs
git commit -m "feat: add DoorLock NetworkBehaviour with lock/unlock RPCs and SFX"
```

---

## Task 7: Create DoorHealth NetworkBehaviour

**Files:**
- Create: `Assets/Scripts/World/MapSystem/DoorHealth.cs`

- [ ] **Step 1: Create the DoorHealth component**

Create `Assets/Scripts/World/MapSystem/DoorHealth.cs`:

```csharp
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Networked health component for breakable doors.
/// Add to the same GameObject as MapTransitionDoor and DoorLock.
/// </summary>
public class DoorHealth : NetworkBehaviour, IDamageable
{
    [Header("Health Settings")]
    [SerializeField] private bool _isBreakable = true;
    [SerializeField] private float _maxHealth = 100f;
    [Tooltip("Percentage of incoming damage absorbed (0.0 = no resistance, 1.0 = invulnerable)")]
    [SerializeField, Range(0f, 1f)] private float _damageResistance = 0f;

    [Header("Repair")]
    [SerializeField] private List<CraftingIngredient> _repairMaterials = new List<CraftingIngredient>();

    public NetworkVariable<float> CurrentHealth = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<bool> IsBroken = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public bool IsBreakable => _isBreakable;
    public float MaxHealth => _maxHealth;
    public float DamageResistance => _damageResistance;
    public List<CraftingIngredient> RepairMaterials => _repairMaterials;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer)
        {
            CurrentHealth.Value = _maxHealth;
            IsBroken.Value = false;
        }
    }

    public bool CanBeDamaged()
    {
        return _isBreakable && !IsBroken.Value;
    }

    public void TakeDamage(float damage, Character attacker)
    {
        if (!IsServer) return;
        if (!CanBeDamaged()) return;

        float effectiveDamage = damage * (1f - _damageResistance);
        CurrentHealth.Value = Mathf.Max(0f, CurrentHealth.Value - effectiveDamage);

        Debug.Log($"<color=red>[DoorHealth]</color> '{name}' took {effectiveDamage:F1} damage (raw={damage:F1}, resist={_damageResistance:P0}). HP: {CurrentHealth.Value:F1}/{_maxHealth}");

        if (CurrentHealth.Value <= 0f)
        {
            IsBroken.Value = true;
            Debug.Log($"<color=red>[DoorHealth]</color> '{name}' is now BROKEN.");
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestRepairServerRpc(ServerRpcParams rpcParams = default)
    {
        if (!IsBroken.Value) return;

        ulong clientId = rpcParams.Receive.SenderClientId;
        NetworkObject playerObj = NetworkManager.ConnectedClients[clientId].PlayerObject;
        if (playerObj == null) return;

        Character repairer = playerObj.GetComponent<Character>();
        if (repairer == null) return;

        // Validate and consume materials
        var inventory = repairer.CharacterEquipment.GetInventory();
        if (inventory == null) return;

        // Check all materials are available
        foreach (var ingredient in _repairMaterials)
        {
            if (ingredient.Item == null) continue;
            if (!repairer.CharacterEquipment.HasItemSO(ingredient.Item))
            {
                Debug.Log($"<color=orange>[DoorHealth]</color> Repair failed: {repairer.CharacterName} missing {ingredient.Item.ItemName}");
                return;
            }
        }

        // Consume materials
        foreach (var ingredient in _repairMaterials)
        {
            if (ingredient.Item == null) continue;
            for (int i = 0; i < ingredient.Amount; i++)
            {
                // Find and remove one instance of this item
                foreach (var slot in inventory.ItemSlots)
                {
                    if (!slot.IsEmpty() && slot.ItemInstance.ItemSO == ingredient.Item)
                    {
                        inventory.RemoveItem(slot.ItemInstance, repairer);
                        break;
                    }
                }
            }
        }

        // Restore door
        CurrentHealth.Value = _maxHealth;
        IsBroken.Value = false;

        Debug.Log($"<color=green>[DoorHealth]</color> '{name}' repaired by {repairer.CharacterName}. HP restored to {_maxHealth}.");
    }
}
```

- [ ] **Step 2: Verify compilation**

Open Unity, wait for recompilation. No errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/World/MapSystem/DoorHealth.cs
git commit -m "feat: add DoorHealth NetworkBehaviour with breakable/repair logic"
```

---

## Task 8: Integrate DoorLock/DoorHealth into MapTransitionDoor

**Files:**
- Modify: `Assets/Scripts/World/MapSystem/MapTransitionDoor.cs`

- [ ] **Step 1: Add lock/broken guard to Interact()**

In `MapTransitionDoor.cs`, add the lock check at the beginning of `Interact()`, right after the existing guards (line 15-21). Insert before line 23 (`string targetMapId = TargetMapId;`):

```csharp
        // --- Door Lock / Broken Check ---
        DoorLock doorLock = GetComponent<DoorLock>();
        DoorHealth doorHealth = GetComponent<DoorHealth>();

        // Broken doors are always passable (lock bypassed)
        bool isBroken = doorHealth != null && doorHealth.IsBroken.Value;

        if (!isBroken && doorLock != null && doorLock.IsLocked.Value)
        {
            // Check if interactor has a matching key
            KeyInstance key = interactor.CharacterEquipment?.FindKeyForLock(doorLock.LockId, doorLock.RequiredTier);
            if (key != null)
            {
                // Unlock the door (don't walk through yet)
                doorLock.RequestUnlockServerRpc();
                return;
            }
            else
            {
                // No key — jiggle + feedback
                doorLock.RequestJiggleServerRpc();
                return;
            }
        }
```

- [ ] **Step 2: Add hold interaction options**

Override `GetHoldInteractionOptions` in `MapTransitionDoor.cs`. Add this method after `Interact()`:

```csharp
    public override System.Collections.Generic.List<InteractionOption> GetHoldInteractionOptions(Character interactor)
    {
        var options = new System.Collections.Generic.List<InteractionOption>();

        DoorLock doorLock = GetComponent<DoorLock>();
        DoorHealth doorHealth = GetComponent<DoorHealth>();

        // Lock/Unlock options (requires matching key)
        if (doorLock != null)
        {
            bool isBroken = doorHealth != null && doorHealth.IsBroken.Value;
            KeyInstance key = interactor.CharacterEquipment?.FindKeyForLock(doorLock.LockId, doorLock.RequiredTier);

            if (key != null && !isBroken)
            {
                if (doorLock.IsLocked.Value)
                {
                    options.Add(new InteractionOption
                    {
                        Name = "Unlock",
                        Action = () => doorLock.RequestUnlockServerRpc()
                    });
                }
                else
                {
                    options.Add(new InteractionOption
                    {
                        Name = "Lock",
                        Action = () => doorLock.RequestLockServerRpc()
                    });
                }
            }
        }

        // Repair option (broken door)
        if (doorHealth != null && doorHealth.IsBroken.Value)
        {
            // TODO: validate repair materials before showing option
            options.Add(new InteractionOption
            {
                Name = "Repair",
                Action = () => doorHealth.RequestRepairServerRpc()
            });
        }

        return options.Count > 0 ? options : null;
    }
```

- [ ] **Step 3: Verify compilation**

Open Unity, wait for recompilation. No errors.

- [ ] **Step 4: Manual test — lock/unlock flow**

1. Add `DoorLock` component to a `MapTransitionDoor` in the scene
2. Set `LockId` to `test_lock_001`, `StartsLocked` = true, `RequiredTier` = 1
3. Give the player the TestKey asset (LockId=`test_lock_001`, Tier=1)
4. Press E on door → should unlock it (no transition)
5. Press E again → should walk through
6. Hold E → should show "Lock" option

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/World/MapSystem/MapTransitionDoor.cs
git commit -m "feat: integrate DoorLock and DoorHealth guards into MapTransitionDoor"
```

---

## Task 9: Add Locksmith Tier Thresholds to SkillTier

**Files:**
- Modify: `Assets/Scripts/Character/CharacterSkills/SkillTier.cs`

- [ ] **Step 1: Add GetMaxCopyableTier extension method**

In `SkillTier.cs`, add to the `SkillTierExtensions` class:

```csharp
    /// <summary>
    /// Returns the maximum key tier a locksmith of this skill tier can copy.
    /// Legendary locksmiths can copy any tier (returns int.MaxValue).
    /// </summary>
    public static int GetMaxCopyableTier(this SkillTier tier)
    {
        switch (tier)
        {
            case SkillTier.Novice: return 1;
            case SkillTier.Intermediate: return 2;
            case SkillTier.Advanced: return 3;
            case SkillTier.Professional: return 4;
            case SkillTier.Master: return 5;
            case SkillTier.Legendary: return int.MaxValue;
            default: return 1;
        }
    }
```

- [ ] **Step 2: Verify compilation**

Open Unity, wait for recompilation. No errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Character/CharacterSkills/SkillTier.cs
git commit -m "feat: add GetMaxCopyableTier to SkillTier for locksmith tier gating"
```

---

## Task 10: Add IsConsumed to CraftingIngredient

**Files:**
- Modify: `Assets/Data/Item/ItemSO.cs:44-49` (CraftingIngredient struct)

- [ ] **Step 1: Add IsConsumed field**

In `ItemSO.cs`, modify the `CraftingIngredient` struct at line 44-49:

```csharp
[System.Serializable]
public struct CraftingIngredient
{
    public ItemSO Item;
    public int Amount;
    [Tooltip("If false, the item is required but not consumed (e.g., original key for copying).")]
    public bool IsConsumed;

    // Default constructor workaround: Unity serializes bool default as false,
    // but we want true as default. Use Reset() or set in Inspector.
}
```

**Important:** Since C# struct fields default to `false` and Unity serialization uses field defaults, existing recipes will have `IsConsumed = false` after this change. To avoid breaking existing recipes, we must handle this in code: treat `IsConsumed = false` AND `Amount > 0` as consumed (legacy behavior), OR initialize the field. The safest approach is to add a runtime check:

In any code that consumes ingredients, check: `if (!ingredient.IsConsumed) continue;` — but since all existing recipes have `IsConsumed = false`, we need the opposite default. Instead, rename to `IsReferenceOnly` (default false = consumed):

```csharp
[System.Serializable]
public struct CraftingIngredient
{
    public ItemSO Item;
    public int Amount;
    [Tooltip("If true, the item is required as a reference but not consumed (e.g., original key for copying).")]
    public bool IsReferenceOnly;
}
```

This way existing assets keep `IsReferenceOnly = false` (meaning consumed = normal behavior). Key copying recipes set `IsReferenceOnly = true`.

- [ ] **Step 2: Verify compilation and existing assets**

Open Unity. Check existing crafting recipes still show `IsReferenceOnly = false` (unchecked). No behavior change for existing items.

- [ ] **Step 3: Commit**

```bash
git add Assets/Data/Item/ItemSO.cs
git commit -m "feat: add IsReferenceOnly to CraftingIngredient for non-consumed inputs"
```

---

## Task 11: Add Paired Door Lookup to BuildingInteriorRegistry

**Files:**
- Modify: `Assets/Scripts/World/Buildings/BuildingInteriorRegistry.cs`

- [ ] **Step 1: Add persistence fields to InteriorRecord**

In `BuildingInteriorRegistry.cs`, add to the `InteriorRecord` class (after line 23):

```csharp
        // Door state persistence (survives hibernation)
        public bool IsLocked = true;
        public float DoorCurrentHealth = -1f; // -1 means use prefab default
```

- [ ] **Step 2: Add FindRecordByDoorPosition method**

Add a public method to `BuildingInteriorRegistry` (after the existing lookup methods):

```csharp
    /// <summary>
    /// Finds an InteriorRecord by matching the exterior door position.
    /// Used by DoorLock for paired interior door sound propagation.
    /// </summary>
    public InteriorRecord FindRecordByDoorPosition(Vector3 doorPosition, float tolerance = 1f)
    {
        foreach (var record in _records.Values)
        {
            if (Vector3.Distance(record.ExteriorDoorPosition, doorPosition) <= tolerance)
                return record;
        }
        return null;
    }
```

- [ ] **Step 3: Verify compilation**

Open Unity, wait for recompilation. No errors. The `DoorLock.PropagateJiggleToPairedDoor()` method from Task 6 should now resolve.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/World/Buildings/BuildingInteriorRegistry.cs
git commit -m "feat: add door state persistence and paired door lookup to BuildingInteriorRegistry"
```

---

## Task 12: Update BuildingInteriorSpawner to Link DoorLock Components

**Files:**
- Modify: `Assets/Scripts/World/Buildings/BuildingInteriorSpawner.cs`

- [ ] **Step 1: Add DoorLock configuration in SpawnInterior**

In `BuildingInteriorSpawner.cs`, inside the `foreach (var door in doors)` loop (after the existing exit door configuration at line 78), add DoorLock linking:

```csharp
            // Configure DoorLock on exit door if present
            DoorLock exitDoorLock = door.GetComponent<DoorLock>();
            if (exitDoorLock != null)
            {
                // Exit doors should share the same LockId as the exterior door.
                // The lock state is read from the persisted record.
                // Note: LockId is set in the prefab; we only restore persisted state.
            }

            // Configure DoorHealth on exit door if present
            DoorHealth exitDoorHealth = door.GetComponent<DoorHealth>();
            if (exitDoorHealth != null)
            {
                // Health state will be restored after Spawn() via NetworkVariables
            }
```

After `netObj.Spawn(true)` and the existing NetworkVariable writes (after line 107), add state restoration:

```csharp
        // Restore persisted door lock/health state
        DoorLock[] doorLocks = instance.GetComponentsInChildren<DoorLock>(true);
        foreach (var dl in doorLocks)
        {
            dl.IsLocked.Value = record.IsLocked;
        }

        DoorHealth[] doorHealths = instance.GetComponentsInChildren<DoorHealth>(true);
        foreach (var dh in doorHealths)
        {
            if (record.DoorCurrentHealth >= 0f)
            {
                dh.CurrentHealth.Value = record.DoorCurrentHealth;
                dh.IsBroken.Value = record.DoorCurrentHealth <= 0f;
            }
        }
```

- [ ] **Step 2: Verify compilation**

Open Unity, wait for recompilation. No errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/World/Buildings/BuildingInteriorSpawner.cs
git commit -m "feat: restore door lock/health state when spawning interiors"
```

---

## Task 13: Create Locksmith SkillSO Asset

This task creates the Locksmith skill asset in the Unity Editor. No code changes.

- [ ] **Step 1: Create Locksmith SkillSO**

In Unity: Navigate to `Assets/Resources/Data/Skills/`. Right-click → Create → Scriptable Objects → SkillSO. Name it `Locksmith`.

Configure:
- SkillID: `locksmith`
- SkillName: `Locksmith`
- Description: `The art of creating and duplicating keys, and understanding lock mechanisms.`
- BaseProficiencyPerLevel: `1`
- StatInfluences: Add two entries:
  - Dexterity with ProficiencyPerPoint: `0.5`
  - Intelligence with ProficiencyPerPoint: `0.3`

- [ ] **Step 2: Commit the asset**

```bash
git add Assets/Resources/Data/Skills/Locksmith.asset
git commit -m "feat: create Locksmith SkillSO asset"
```

---

## Task 14: Manual Integration Test

No code changes — this is a play-mode verification task.

- [ ] **Step 1: Set up test scene**

1. Place a building with an interior prefab in the scene
2. Add `DoorLock` to the building's entry door: LockId=`test_lock_001`, RequiredTier=1, StartsLocked=true
3. Add `DoorLock` to the interior's exit door: same LockId
4. Add `DoorHealth` to the entry door: IsBreakable=true, MaxHealth=50, DamageResistance=0.2
5. Ensure both doors have an `AudioSource` component
6. Give the host player a TestKey (LockId=`test_lock_001`, Tier=1) in inventory

- [ ] **Step 2: Test host lock/unlock**

1. Press E on locked door → door unlocks (SFX plays)
2. Press E again → transition into interior
3. Hold E on exit door → "Lock" option appears
4. Select Lock → door locks
5. Press E → door unlocks again
6. Press E → exit to exterior

- [ ] **Step 3: Test remote client lock/unlock**

1. Connect a second client
2. Give client the TestKey
3. Client presses E on locked door → unlocks
4. Client enters interior
5. Client exits interior
6. Verify host sees correct lock state throughout

- [ ] **Step 4: Test locked door without key**

1. Remove key from client inventory
2. Client presses E on locked door → jiggle SFX, "This door is locked"
3. Host inside interior hears jiggle SFX (paired door propagation)

- [ ] **Step 5: Test breakable door**

1. Lock the door
2. Attack the door with a weapon → damage log appears
3. Continue attacking until HP reaches 0 → door breaks
4. Walk through broken door without key
5. Hold E → "Repair" option (if materials available)

- [ ] **Step 6: Document any issues found**

Note any bugs for follow-up fixes.

---

## Summary

| Task | What | Files |
|------|------|-------|
| 1 | Tier on ItemSO | `ItemSO.cs` |
| 2 | KeySO + KeyInstance | `KeySO.cs`, `KeyInstance.cs` |
| 3 | FindKeyForLock | `CharacterEquipment.cs` |
| 4 | IDamageable interface | `IDamageable.cs` |
| 5 | CombatStyleAttack update | `CombatStyleAttack.cs` |
| 6 | DoorLock component | `DoorLock.cs` |
| 7 | DoorHealth component | `DoorHealth.cs` |
| 8 | MapTransitionDoor integration | `MapTransitionDoor.cs` |
| 9 | Locksmith tier thresholds | `SkillTier.cs` |
| 10 | CraftingIngredient.IsReferenceOnly | `ItemSO.cs` |
| 11 | Paired door lookup + persistence | `BuildingInteriorRegistry.cs` |
| 12 | Interior spawner state restore | `BuildingInteriorSpawner.cs` |
| 13 | Locksmith SkillSO asset | Unity Editor |
| 14 | Integration test | Play mode |
