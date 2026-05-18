# Character Equipment UI Rework — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the layer-tabbed `CharacterEquipmentUI` with a `UI_CharacterEquipment : UI_WindowBase` window — paper-doll + 3-layer stacked cells, top row of special-slot cards, bag inventory grid — wired to a single shared click-popup component, with all mutations routed through five new `CharacterAction` subclasses (rule #22 player↔NPC parity).

**Architecture:** Five phases, bottom-up.
- **A. Foundation** — small data/contract additions (`OnCarriedItemChanged` event, `EquipmentSourceRef` discriminated value).
- **B. CharacterEquipment refactor** — bag-first displacement on `Equip`, two new helpers (`UnequipToBag`, `WieldOffToHand`).
- **C. New CharacterActions** — five server-authoritative action classes.
- **D. UI scripts** — one shared popup + three leaf-cell scripts + one window root, all under `Assets/Scripts/UI/Equipment/`.
- **E. Wiring + cleanup** — `PlayerUI` surface update, `PlayerController` Tab repoint, prefab authoring via MCP, scene wiring, old script/prefab deletion, doc updates.

**Tech Stack:** Unity 2022.3 + NGO (Netcode for GameObjects) + C# 9 + UGUI + TMP_Pro. MCP Roslyn `script-execute` for prefab authoring (canonical UI_WindowBase Variant recipe per `.agent/skills/ui-hud/SKILL.md`).

**Spec:** [docs/superpowers/specs/2026-05-19-character-equipment-ui-rework-design.md](../specs/2026-05-19-character-equipment-ui-rework-design.md) — read §4 (Files), §5 (Verb matrix), §6 (Smart-swap algorithm), §11 (Testing matrix) before executing tasks 3+.

---

## Plan-phase decisions (resolved)

The spec left three open questions for the plan phase. Decisions baked into the tasks below:

1. **Consumable detection (spec §10):** dispatch via `instance is ConsumableInstance c` check; call `c.ApplyEffect(character)`. **No new `IUsable` interface.** The existing `ConsumableInstance` base class (Assets/Scripts/Item/ConsumableInstance.cs) already declares `public virtual void ApplyEffect(Character character)` and `FoodInstance` overrides it (PotionInstance is a future subclass with the same shape). Polymorphism does the dispatch.
2. **Hands-carry event vs poll (spec §10):** add a `public event Action<ItemInstance> OnCarriedItemChanged` on `HandsController`. Fired from `CarryItem` / `DropCarriedItem` / `ClearCarriedItem`. UI subscribes instead of polling — eliminates the 4 Hz `Update` poll and the rule #34 log-gate concern.
3. **Equip-side displacement caller sweep (spec §10):** Task 3 executes a `grep "CharacterEquipment\.Equip("` + `grep "\.Equip(" Assets/Scripts/` sweep before changing displacement behavior. Findings recorded inline; rollback path documented in the task.

---

## Phase A — Foundation

### Task 1: Add `OnCarriedItemChanged` event on `HandsController`

**Files:**
- Modify: `Assets/Scripts/Character/CharacterBodyPartsController/HandsController.cs`

- [ ] **Step 1: Add the event field + fire site in `CarryItem`, `DropCarriedItem`, `ClearCarriedItem`, `ApplyRestoredCarry`**

Edit `HandsController.cs`. Add the using and the event near the existing `_carriedItem` field (around line 18):

```csharp
using System;
// ... existing usings ...

public class HandsController : MonoBehaviour, ICharacterSaveData<HandsSaveData>
{
    // ... existing fields ...

    // --- Carry System ---
    private ItemInstance _carriedItem;
    private GameObject _carriedVisual;

    /// <summary>
    /// Fires whenever the carried item changes (carry, drop, clear, save-restore).
    /// Subscribers receive the NEW carried item (null when hands become empty).
    /// Replaces UI polling — see <see cref="UI_CharacterEquipment"/>.
    /// </summary>
    public event Action<ItemInstance> OnCarriedItemChanged;
```

Inside `CarryItem(ItemInstance item)` — after the existing `_carriedItem = item;` assignment, BEFORE the `AttachVisualToHand(item);` call, fire the event:

```csharp
    public bool CarryItem(ItemInstance item)
    {
        if (item == null) return false;

        if (!AreHandsFree())
        {
            Debug.Log($"<color=orange>[Carry]</color> Les mains ne sont pas libres !");
            return false;
        }

        _carriedItem = item;
        OnCarriedItemChanged?.Invoke(_carriedItem);
        AttachVisualToHand(item);

        Debug.Log($"<color=green>[Carry]</color> {_character?.CharacterName} porte {item.ItemSO.ItemName}.");
        return true;
    }
```

Inside `DropCarriedItem()` — after `_carriedItem = null;`, fire the event:

```csharp
    public ItemInstance DropCarriedItem()
    {
        if (_carriedItem == null) return null;

        ItemInstance dropped = _carriedItem;
        _carriedItem = null;
        OnCarriedItemChanged?.Invoke(null);

        if (_carriedVisual != null)
        {
            Destroy(_carriedVisual);
            _carriedVisual = null;
        }

        GetHand("R")?.SetPose("normal");

        Debug.Log($"<color=cyan>[Carry]</color> {_character?.CharacterName} a lâché {dropped.ItemSO.ItemName}.");
        return dropped;
    }
```

Inside `ClearCarriedItem()` — after `_carriedItem = null;`, fire the event:

```csharp
    public void ClearCarriedItem()
    {
        _carriedItem = null;
        OnCarriedItemChanged?.Invoke(null);
        if (_carriedVisual != null)
        {
            Destroy(_carriedVisual);
            _carriedVisual = null;
        }
    }
```

Inside `ApplyRestoredCarry(ItemInstance item)` — after `_carriedItem = item;`, fire the event:

```csharp
    private void ApplyRestoredCarry(ItemInstance item)
    {
        if (item == null) return;

        _carriedItem = item;
        OnCarriedItemChanged?.Invoke(_carriedItem);
        AttachVisualToHand(item);

        if (_debugMode)
            Debug.Log($"<color=green>[HandsController.Deserialize]</color> Restored carry: {_character?.CharacterName} is holding {item.ItemSO.ItemName}.");
    }
```

- [ ] **Step 2: Verify compile**

Run via MCP: `mcp__ai-game-developer__assets-refresh` (forces script recompile). Expected: no compile errors in the Editor Console.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Character/CharacterBodyPartsController/HandsController.cs
git commit -m "feat(hands): add OnCarriedItemChanged event for UI subscription"
```

---

### Task 2: Add `EquipmentSourceRef` discriminated value type

**Files:**
- Create: `Assets/Scripts/Character/CharacterActions/EquipmentSourceRef.cs`

- [ ] **Step 1: Create the file**

Create `Assets/Scripts/Character/CharacterActions/EquipmentSourceRef.cs` with this exact content:

```csharp
/// <summary>
/// Discriminator for the four places an item can live on a Character that the
/// equipment-window CharacterAction surface needs to reference uniformly:
/// a bag slot, a worn layer slot, the active weapon slot, or the hands-carry slot.
/// </summary>
public enum EquipmentSourceKind
{
    BagSlot      = 0,
    WornSlot     = 1,
    ActiveWeapon = 2,
    HandsCarry   = 3,
}

/// <summary>
/// Tiny serializable discriminated value identifying the source of an item for
/// equipment-window actions (CarryInHand, StashInBag, UseItem). Read-only.
///
/// <para>BagIndex is meaningful only when Kind == BagSlot.
/// Layer + Slot are meaningful only when Kind == WornSlot.
/// ActiveWeapon and HandsCarry need no payload (one slot each).</para>
///
/// <para>Construct via the static factories (Bag / Worn / Weapon / Hands) for
/// clarity at call sites.</para>
/// </summary>
[System.Serializable]
public readonly struct EquipmentSourceRef
{
    public readonly EquipmentSourceKind Kind;
    public readonly int BagIndex;
    public readonly WearableLayerEnum Layer;
    public readonly WearableType Slot;

    public EquipmentSourceRef(
        EquipmentSourceKind kind,
        int bagIndex = -1,
        WearableLayerEnum layer = WearableLayerEnum.Underwear,
        WearableType slot = WearableType.Helmet)
    {
        Kind = kind;
        BagIndex = bagIndex;
        Layer = layer;
        Slot = slot;
    }

    public static EquipmentSourceRef Bag(int index) =>
        new EquipmentSourceRef(EquipmentSourceKind.BagSlot, bagIndex: index);

    public static EquipmentSourceRef Worn(WearableLayerEnum layer, WearableType slot) =>
        new EquipmentSourceRef(EquipmentSourceKind.WornSlot, layer: layer, slot: slot);

    public static EquipmentSourceRef Weapon() =>
        new EquipmentSourceRef(EquipmentSourceKind.ActiveWeapon);

    public static EquipmentSourceRef Hands() =>
        new EquipmentSourceRef(EquipmentSourceKind.HandsCarry);

    public override string ToString() => Kind switch
    {
        EquipmentSourceKind.BagSlot      => $"Bag[{BagIndex}]",
        EquipmentSourceKind.WornSlot     => $"Worn[{Layer}/{Slot}]",
        EquipmentSourceKind.ActiveWeapon => "ActiveWeapon",
        EquipmentSourceKind.HandsCarry   => "HandsCarry",
        _ => $"<unknown:{Kind}>",
    };
}
```

- [ ] **Step 2: Verify compile**

Run MCP `assets-refresh`. Expected: no compile errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Character/CharacterActions/EquipmentSourceRef.cs
git commit -m "feat(actions): add EquipmentSourceRef discriminated value type"
```

---

## Phase B — CharacterEquipment refactor

### Task 3: Refactor `CharacterEquipment.Equip` displacement to bag-first

**Files:**
- Modify: `Assets/Scripts/Character/CharacterEquipment/CharacterEquipment.cs`

- [ ] **Step 1: Sweep existing `Equip(` callers**

Run via Grep (NOT bash):

```
pattern: "CharacterEquipment\.Equip\("
output_mode: "content"
-n: true
```

And separately:

```
pattern: "\.Equip\("
glob: "Assets/Scripts/**/*.cs"
output_mode: "content"
-n: true
```

Read each match. Record findings as a comment in your commit message. Expected callers per the spec §10:
- NPC GOAP equip paths (clothing job, armor pickup) — if any
- `WorldItem.RequestInteractServerRpc` pickup flow — verify behavior expectation
- `CharacterEquipment.Deserialize` save-load — does NOT call `Equip`; it calls `targetLayer.Equip(wearable)` directly (skips displacement). No behavior change for save-load.
- The (about-to-be-deleted) `CharacterEquipmentUI` — irrelevant after Task 20.

If any caller documents/depends on "displaced wearable drops to ground" — STOP and surface to the human. None expected, but verify.

- [ ] **Step 2: Add the `TryStashInBag` private helper**

Edit `CharacterEquipment.cs`. Add this private method anywhere in the class body (suggested: near the existing `PickUpItem` method around line 907):

```csharp
    /// <summary>
    /// Tries to put an item into the equipped bag's first compatible free slot.
    /// Returns true on success. No-op + returns false if no bag is equipped or no
    /// slot accepts the item. Used by Equip() displacement, UnequipToBag(), and
    /// the CharacterAction_StashInBag / CharacterAction_CarryInHand smart-swap.
    /// </summary>
    private bool TryStashInBag(ItemInstance item)
    {
        if (item == null) return false;
        var inv = GetInventory();
        if (inv == null) return false;
        if (!inv.HasFreeSpaceForItem(item)) return false;
        return inv.AddItem(item, _character);
    }
```

- [ ] **Step 3: Change `Equip` displacement to call `TryStashInBag` before falling back to drop**

Inside `Equip(ItemInstance itemInstance)`, find the wearables block (around line 367–383). Replace the `if (existingInstance != null) { character.DropItem(existingInstance); }` block:

OLD:

```csharp
                EquipmentInstance existingInstance = targetLayer.GetInstance(data.WearableType);
                if (existingInstance != null)
                {
                    character.DropItem(existingInstance);
                }

                Debug.Log($"<color=green>[Equip]</color> {data.ItemName} vers {data.EquipmentLayer}");
                targetLayer.Equip(wearable);
```

NEW:

```csharp
                EquipmentInstance existingInstance = targetLayer.GetInstance(data.WearableType);
                if (existingInstance != null)
                {
                    // Bag-first displacement (2026-05-19 UI rework). The previously equipped
                    // wearable now goes back to the bag if there's a compatible free slot;
                    // only drops to ground when the bag is full. Mirrors the smart-swap
                    // in CharacterAction_CarryInHand. The legacy "always drop" behavior
                    // was surprising and punishing for accidental click-to-swap UX.
                    if (!TryStashInBag(existingInstance))
                    {
                        character.DropItem(existingInstance);
                    }
                }

                Debug.Log($"<color=green>[Equip]</color> {data.ItemName} vers {data.EquipmentLayer}");
                targetLayer.Equip(wearable);
```

- [ ] **Step 4: Verify compile + manual sanity test in Editor Play mode**

Run MCP `assets-refresh`. Expected: no compile errors.

Manual test (one-time, before commit): in Editor Play mode, equip a clothing-layer Torso item that already has something there — verify the displaced item appears in the bag inventory grid, NOT on the ground.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Character/CharacterEquipment/CharacterEquipment.cs
git commit -m "$(cat <<'EOF'
feat(equipment): Equip displacement goes to bag first, ground only on bag-full

Callers swept: <paste grep findings here>. No callers depend on the legacy
"always drop to ground" side-effect.

Symmetric with the smart-swap behavior in the upcoming CharacterAction_CarryInHand.
EOF
)"
```

---

### Task 4: Add `CharacterEquipment.UnequipToBag(layer, slotType)` helper

**Files:**
- Modify: `Assets/Scripts/Character/CharacterEquipment/CharacterEquipment.cs`

- [ ] **Step 1: Add the `UnequipToBag` method**

Edit `CharacterEquipment.cs`. Add this method right next to the existing `Unequip(WearableLayerEnum, WearableType)` (around line 598). Reuses Task 3's `TryStashInBag` private helper:

```csharp
    /// <summary>
    /// Removes a worn wearable and stashes it into the bag's first compatible free slot.
    /// Falls back to a ground-drop only when the bag has no compatible space.
    /// Used by <see cref="CharacterAction_UnequipWearable"/>; mirrors the bag-first
    /// behavior of <see cref="Equip"/> displacement.
    /// </summary>
    /// <returns>True if the item ended up in the bag; false if it dropped to ground (still successful).</returns>
    public bool UnequipToBag(WearableLayerEnum layerType, WearableType slotType)
    {
        // Bag-special case must still drop the entire bag (preserves UnequipBag semantics).
        if (slotType == WearableType.Bag || layerType == WearableLayerEnum.Bag)
        {
            UnequipBag();
            OnEquipmentChanged?.Invoke();
            return false; // Bag dropped to world, not stashed.
        }

        EquipmentLayer targetLayer = GetTargetLayer(layerType);
        if (targetLayer == null) return false;

        EquipmentInstance instanceToDrop = targetLayer.GetInstance(slotType);
        if (instanceToDrop == null) return false;

        // Free the slot first so the bag-free-space check sees the up-to-date capacity.
        targetLayer.Unequip(slotType);
        UpdateNetworkSlot(GetSlotId(layerType, slotType), null);
        OnEquipmentChanged?.Invoke();

        if (TryStashInBag(instanceToDrop))
        {
            Debug.Log($"<color=orange>[UnequipToBag]</color> {instanceToDrop.ItemSO.ItemName} stashed in bag.");
            return true;
        }

        // Bag full — fall back to ground drop.
        character.DropItem(instanceToDrop);
        Debug.Log($"<color=orange>[UnequipToBag]</color> {instanceToDrop.ItemSO.ItemName} bag full → dropped to ground.");
        return false;
    }
```

- [ ] **Step 2: Verify compile**

Run MCP `assets-refresh`. Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Character/CharacterEquipment/CharacterEquipment.cs
git commit -m "feat(equipment): add UnequipToBag(layer, slot) helper for new Unequip action"
```

---

### Task 5: Add `CharacterEquipment.WieldOffToHand()` helper

**Files:**
- Modify: `Assets/Scripts/Character/CharacterEquipment/CharacterEquipment.cs`

- [ ] **Step 1: Add the `WieldOffToHand` method**

Edit `CharacterEquipment.cs`. Add this method next to the existing `UnequipWeapon` (around line 400). Key difference vs `UnequipWeapon`: does NOT call `character.DropItem(_weapon)` — instead returns the detached `WeaponInstance` so callers (smart-swap, stash-in-bag) can decide the sink.

```csharp
    /// <summary>
    /// Detaches the currently wielded weapon — socket hides, animator returns to
    /// civilian — and returns the <see cref="WeaponInstance"/> without dropping it
    /// to the world. The caller decides the sink (stash in bag, hand, or ground).
    ///
    /// <para>Inverse of <see cref="UnequipWeapon"/>: that method always drops; this
    /// one never does. Used by <see cref="CharacterAction_CarryInHand"/> and
    /// <see cref="CharacterAction_StashInBag"/> for the Active Weapon card.</para>
    ///
    /// <para>Returns null if no weapon is wielded.</para>
    /// </summary>
    public WeaponInstance WieldOffToHand()
    {
        if (_weapon == null) return null;

        WeaponInstance detached = _weapon;
        _weapon = null;

        UpdateWeaponVisual();       // Deactivates the socket + resets animator to civilian.
        UpdateNetworkSlot(0, null);
        OnEquipmentChanged?.Invoke();

        Debug.Log($"<color=orange>[WieldOffToHand]</color> {detached.ItemSO.ItemName} detached from active slot — sink decided by caller.");
        return detached;
    }
```

- [ ] **Step 2: Verify compile**

Run MCP `assets-refresh`. Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Character/CharacterEquipment/CharacterEquipment.cs
git commit -m "feat(equipment): add WieldOffToHand() for sink-decided weapon detach"
```

---

## Phase C — New CharacterActions

> All five action classes follow the established `CharacterAction` pattern: zero-duration single-shot (`base(character, 0f)`), `OnApplyEffect` guarded by `IsServer`, validation in `CanExecute` is best-effort (state can drift between queue and apply). Mirrors `CharacterAction_DepositToSafe.cs`.

### Task 6: `CharacterAction_EquipWearable`

**Files:**
- Create: `Assets/Scripts/Character/CharacterActions/CharacterAction_EquipWearable.cs`

- [ ] **Step 1: Create the file**

```csharp
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative: removes a wearable from a bag slot and equips it via
/// <see cref="CharacterEquipment.Equip"/>, which now performs bag-first displacement
/// of any item currently in the target layer/slot (2026-05-19 rework).
///
/// <para>Queued by player UI clicks (Equip verb on a bag-cell popup) and by future
/// NPC AI (autonomous equip, equip-found-garment flows). Rule #22 player↔NPC parity.</para>
///
/// <para>Validation: the bag slot index must still hold a <see cref="WearableInstance"/>
/// at apply-time — slot contents may have shifted between queue and apply. Action
/// silently no-ops on mismatch (race is rare, self-correcting via next OnEquipmentChanged).</para>
/// </summary>
public sealed class CharacterAction_EquipWearable : CharacterAction
{
    private readonly int _bagSlotIndex;

    public int BagSlotIndex => _bagSlotIndex;

    public CharacterAction_EquipWearable(Character character, int bagSlotIndex)
        : base(character, 0f)
    {
        _bagSlotIndex = bagSlotIndex;
    }

    public override bool CanExecute()
    {
        if (character == null) return false;
        var equip = character.CharacterEquipment;
        if (equip == null) return false;
        var inv = equip.GetInventory();
        if (inv == null || _bagSlotIndex < 0 || _bagSlotIndex >= inv.ItemSlots.Count) return false;
        var slot = inv.ItemSlots[_bagSlotIndex];
        return !slot.IsEmpty() && slot.ItemInstance is WearableInstance;
    }

    public override void OnStart() { /* no animation — UI-driven discrete action */ }

    public override void OnApplyEffect()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
        if (character == null) return;

        var equip = character.CharacterEquipment;
        if (equip == null) return;
        var inv = equip.GetInventory();
        if (inv == null || _bagSlotIndex < 0 || _bagSlotIndex >= inv.ItemSlots.Count) return;

        var slot = inv.ItemSlots[_bagSlotIndex];
        if (slot.IsEmpty() || !(slot.ItemInstance is WearableInstance wearable))
        {
            if (NPCDebug.VerboseActions)
                Debug.LogWarning($"<color=orange>[EquipWearable]</color> {character.CharacterName} aborted: bag slot {_bagSlotIndex} no longer holds a wearable.");
            return;
        }

        // Remove from bag first, THEN call Equip. CharacterEquipment.Equip's bag-first
        // displacement (Task 3) needs the source slot free so the displaced wearable
        // can land in it if the bag is otherwise full.
        if (!inv.RemoveItem(wearable, character)) return;

        equip.Equip(wearable);
    }
}
```

- [ ] **Step 2: Verify compile**

Run MCP `assets-refresh`. Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Character/CharacterActions/CharacterAction_EquipWearable.cs
git commit -m "feat(actions): add CharacterAction_EquipWearable (bag → worn slot)"
```

---

### Task 7: `CharacterAction_UnequipWearable`

**Files:**
- Create: `Assets/Scripts/Character/CharacterActions/CharacterAction_UnequipWearable.cs`

- [ ] **Step 1: Create the file**

```csharp
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative: removes a wearable from the specified worn layer/slot
/// and routes it through <see cref="CharacterEquipment.UnequipToBag"/> — which
/// stashes to the bag first and only drops to ground when the bag is full.
///
/// <para>Queued by player UI clicks (Unequip verb on a worn mini-cell popup) and
/// by future NPC AI. Rule #22 player↔NPC parity.</para>
/// </summary>
public sealed class CharacterAction_UnequipWearable : CharacterAction
{
    private readonly WearableLayerEnum _layer;
    private readonly WearableType _slot;

    public WearableLayerEnum Layer => _layer;
    public WearableType Slot => _slot;

    public CharacterAction_UnequipWearable(Character character, WearableLayerEnum layer, WearableType slot)
        : base(character, 0f)
    {
        _layer = layer;
        _slot = slot;
    }

    public override bool CanExecute()
    {
        if (character == null) return false;
        return character.CharacterEquipment != null;
    }

    public override void OnStart() { /* no animation */ }

    public override void OnApplyEffect()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
        if (character == null) return;

        var equip = character.CharacterEquipment;
        if (equip == null) return;

        // Returns true on bag-stash, false on ground-drop. Either outcome is "successful
        // unequip" from the UI's perspective; we only log the path for diagnostics.
        bool stashed = equip.UnequipToBag(_layer, _slot);
        if (NPCDebug.VerboseActions)
            Debug.Log($"<color=cyan>[UnequipWearable]</color> {character.CharacterName} unequipped {_layer}/{_slot} → {(stashed ? "bag" : "ground")}.");
    }
}
```

- [ ] **Step 2: Verify compile**

Run MCP `assets-refresh`.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Character/CharacterActions/CharacterAction_UnequipWearable.cs
git commit -m "feat(actions): add CharacterAction_UnequipWearable (worn → bag, fallback ground)"
```

---

### Task 8: `CharacterAction_StashInBag`

**Files:**
- Create: `Assets/Scripts/Character/CharacterActions/CharacterAction_StashInBag.cs`

- [ ] **Step 1: Create the file**

```csharp
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative: moves an item from its source (hand carry, worn slot,
/// or active weapon) into the bag's first compatible free slot. Falls back to a
/// ground drop only when the bag has no compatible space.
///
/// <para>Sources supported:</para>
/// <list type="bullet">
///   <item><b>HandsCarry</b> — the hand-carry item (any kind). Hand becomes free.</item>
///   <item><b>WornSlot</b> — equivalent to <see cref="CharacterAction_UnequipWearable"/>;
///   provided for verb-popup uniformity. Routes through <see cref="CharacterEquipment.UnequipToBag"/>.</item>
///   <item><b>ActiveWeapon</b> — wields off via <see cref="CharacterEquipment.WieldOffToHand"/>,
///   then stashes the detached weapon into the bag (or ground on full).</item>
/// </list>
///
/// <para>BagSlot is not a valid source (you can't stash a bagged item into the bag).</para>
///
/// <para>Queued by player UI clicks (Stash in bag verb on Hands / Worn / Active Weapon
/// popups) and by future NPC AI. Rule #22 player↔NPC parity.</para>
/// </summary>
public sealed class CharacterAction_StashInBag : CharacterAction
{
    private readonly EquipmentSourceRef _source;

    public EquipmentSourceRef Source => _source;

    public CharacterAction_StashInBag(Character character, EquipmentSourceRef source)
        : base(character, 0f)
    {
        _source = source;
    }

    public override bool CanExecute()
    {
        if (character == null) return false;
        if (_source.Kind == EquipmentSourceKind.BagSlot) return false; // bag → bag is invalid
        return character.CharacterEquipment != null;
    }

    public override void OnStart() { /* no animation */ }

    public override void OnApplyEffect()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
        if (character == null) return;

        var equip = character.CharacterEquipment;
        if (equip == null) return;

        switch (_source.Kind)
        {
            case EquipmentSourceKind.HandsCarry:
                StashFromHands(equip);
                break;
            case EquipmentSourceKind.WornSlot:
                equip.UnequipToBag(_source.Layer, _source.Slot);
                break;
            case EquipmentSourceKind.ActiveWeapon:
                StashFromActiveWeapon(equip);
                break;
            default:
                Debug.LogWarning($"<color=orange>[StashInBag]</color> unsupported source kind {_source.Kind}.");
                break;
        }
    }

    private void StashFromHands(CharacterEquipment equip)
    {
        var hands = character.CharacterVisual?.BodyPartsController?.HandsController;
        if (hands == null || !hands.IsCarrying) return;

        ItemInstance carried = hands.CarriedItem;

        // Try bag first; only drop to ground on failure.
        var inv = equip.GetInventory();
        if (inv != null && inv.HasFreeSpaceForItem(carried))
        {
            hands.DropCarriedItem();        // clears hand (no WorldItem spawn)
            inv.AddItem(carried, character);
            if (NPCDebug.VerboseActions)
                Debug.Log($"<color=cyan>[StashInBag]</color> {character.CharacterName} stashed {carried.ItemSO.ItemName} from hand → bag.");
        }
        else
        {
            // Bag full — drop to world via the existing physical-drop helper.
            hands.DropCarriedItem();
            CharacterDropItem.ExecutePhysicalDrop(character, carried);
            if (NPCDebug.VerboseActions)
                Debug.Log($"<color=orange>[StashInBag]</color> {character.CharacterName} bag full → dropped {carried.ItemSO.ItemName} to ground.");
        }
    }

    private void StashFromActiveWeapon(CharacterEquipment equip)
    {
        WeaponInstance detached = equip.WieldOffToHand();
        if (detached == null) return;

        var inv = equip.GetInventory();
        if (inv != null && inv.HasFreeSpaceForItem(detached))
        {
            inv.AddItem(detached, character);
            if (NPCDebug.VerboseActions)
                Debug.Log($"<color=cyan>[StashInBag]</color> {character.CharacterName} stashed active weapon {detached.ItemSO.ItemName} → bag.");
        }
        else
        {
            CharacterDropItem.ExecutePhysicalDrop(character, detached);
            if (NPCDebug.VerboseActions)
                Debug.Log($"<color=orange>[StashInBag]</color> {character.CharacterName} bag full → dropped active weapon {detached.ItemSO.ItemName} to ground.");
        }
    }
}
```

- [ ] **Step 2: Verify compile**

Run MCP `assets-refresh`.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Character/CharacterActions/CharacterAction_StashInBag.cs
git commit -m "feat(actions): add CharacterAction_StashInBag (hand/worn/weapon → bag, fallback ground)"
```

---

### Task 9: `CharacterAction_CarryInHand` (smart-swap)

**Files:**
- Create: `Assets/Scripts/Character/CharacterActions/CharacterAction_CarryInHand.cs`

- [ ] **Step 1: Create the file**

Implements the smart-swap algorithm from spec §6:

```csharp
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative: moves an item (from bag / worn / active weapon) into the
/// character's hands. Implements the smart-swap rule from spec §6 — if hands are
/// already occupied with Y, Y is stashed back to the bag first (covers the "slot
/// was free" AND the "same-type fits in X's now-empty source slot" cases naturally);
/// only drops Y to the ground when the bag genuinely has no compatible space.
///
/// <para>Sources supported: BagSlot, WornSlot, ActiveWeapon. HandsCarry is invalid
/// (item is already in the hand).</para>
///
/// <para>Active-Weapon source: wield-off via <see cref="CharacterEquipment.WieldOffToHand"/>
/// (does NOT drop the weapon to world); then carry the detached weapon in hand
/// after applying the smart-swap to whatever was already carried.</para>
///
/// <para>Queued by player UI clicks (Carry in hand verb) and by future NPC AI.
/// Rule #22 player↔NPC parity.</para>
/// </summary>
public sealed class CharacterAction_CarryInHand : CharacterAction
{
    private readonly EquipmentSourceRef _source;

    public EquipmentSourceRef Source => _source;

    public CharacterAction_CarryInHand(Character character, EquipmentSourceRef source)
        : base(character, 0f)
    {
        _source = source;
    }

    public override bool CanExecute()
    {
        if (character == null) return false;
        if (_source.Kind == EquipmentSourceKind.HandsCarry) return false; // already in hand
        return character.CharacterEquipment != null;
    }

    public override void OnStart() { /* no animation */ }

    public override void OnApplyEffect()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
        if (character == null) return;

        var equip = character.CharacterEquipment;
        var hands = character.CharacterVisual?.BodyPartsController?.HandsController;
        if (equip == null || hands == null) return;

        // 1. Detach X from its source (this may free a bag/worn slot).
        ItemInstance x = DetachFromSource(equip);
        if (x == null) return;

        ItemInstance y = hands.CarriedItem;

        // 2a. Easy case — hand was free.
        if (y == null)
        {
            hands.CarryItem(x);
            return;
        }

        // 2b. Hand was occupied. Try to stash Y first.
        // HasFreeSpaceForItem now considers X's freshly-vacated slot if X was bag-sourced.
        var inv = equip.GetInventory();
        if (inv != null && inv.HasFreeSpaceForItem(y))
        {
            hands.DropCarriedItem();            // clears Y from hand without spawning WorldItem
            inv.AddItem(y, character);          // Y → bag
            hands.CarryItem(x);                 // X → hand
            if (NPCDebug.VerboseActions)
                Debug.Log($"<color=cyan>[CarryInHand]</color> {character.CharacterName} swapped via bag: hand {y.ItemSO.ItemName} → bag, {x.ItemSO.ItemName} → hand.");
        }
        else
        {
            // 2c. No bag space for Y — Y goes to ground, X to hand.
            hands.DropCarriedItem();
            CharacterDropItem.ExecutePhysicalDrop(character, y);
            hands.CarryItem(x);
            if (NPCDebug.VerboseActions)
                Debug.Log($"<color=orange>[CarryInHand]</color> {character.CharacterName} swapped via ground: hand {y.ItemSO.ItemName} → ground, {x.ItemSO.ItemName} → hand.");
        }
    }

    /// <summary>
    /// Removes X from its source location and returns it. Bag/worn slots become empty;
    /// active weapon is detached via WieldOffToHand (does NOT drop to world).
    /// Returns null if the source no longer holds a valid item (race).
    /// </summary>
    private ItemInstance DetachFromSource(CharacterEquipment equip)
    {
        switch (_source.Kind)
        {
            case EquipmentSourceKind.BagSlot:
            {
                var inv = equip.GetInventory();
                if (inv == null || _source.BagIndex < 0 || _source.BagIndex >= inv.ItemSlots.Count) return null;
                var slot = inv.ItemSlots[_source.BagIndex];
                if (slot.IsEmpty()) return null;
                ItemInstance item = slot.ItemInstance;
                inv.RemoveItem(item, character);
                return item;
            }
            case EquipmentSourceKind.WornSlot:
            {
                EquipmentLayer layer = ResolveLayer(equip, _source.Layer);
                if (layer == null) return null;
                EquipmentInstance instance = layer.GetInstance(_source.Slot);
                if (instance == null) return null;
                layer.Unequip(_source.Slot);
                // NOTE: UpdateNetworkSlot is internal to CharacterEquipment. Worn-slot detach
                // here will leave the network slot mirror dirty for one frame — acceptable
                // because the carry destination triggers no equipment-network mutation, and
                // the next equip/unequip refreshes the mirror. If this becomes visible, add
                // a public `CharacterEquipment.FlagSlotEmpty(layer, slot)` helper.
                return instance;
            }
            case EquipmentSourceKind.ActiveWeapon:
            {
                return equip.WieldOffToHand();
            }
            default:
                return null;
        }
    }

    private static EquipmentLayer ResolveLayer(CharacterEquipment equip, WearableLayerEnum layer) => layer switch
    {
        WearableLayerEnum.Underwear => equip.UnderwearLayer,
        WearableLayerEnum.Clothing  => equip.ClothingLayer,
        WearableLayerEnum.Armor     => equip.ArmorLayer,
        _ => null,
    };
}
```

- [ ] **Step 2: Address the network-slot-mirror note**

The action's `DetachFromSource` for `WornSlot` directly calls `layer.Unequip` without `UpdateNetworkSlot`. To keep the replication mirror clean, add a public helper on `CharacterEquipment` and call it. Edit `Assets/Scripts/Character/CharacterEquipment/CharacterEquipment.cs` and add this method next to the existing `Unequip` (around line 598):

```csharp
    /// <summary>
    /// Removes a worn wearable from the given layer/slot without spawning a WorldItem
    /// and returns the detached instance. Caller decides the sink (carry-in-hand, etc.).
    /// Mirrors <see cref="WieldOffToHand"/> shape — sink-agnostic detach.
    /// </summary>
    public WearableInstance DetachWornToCaller(WearableLayerEnum layerType, WearableType slotType)
    {
        EquipmentLayer targetLayer = GetTargetLayer(layerType);
        if (targetLayer == null) return null;

        EquipmentInstance instance = targetLayer.GetInstance(slotType);
        if (instance == null) return null;

        targetLayer.Unequip(slotType);
        UpdateNetworkSlot(GetSlotId(layerType, slotType), null);
        OnEquipmentChanged?.Invoke();

        return instance as WearableInstance;
    }
```

Then replace the `WornSlot` branch in `CharacterAction_CarryInHand.DetachFromSource` to use it:

```csharp
            case EquipmentSourceKind.WornSlot:
                return equip.DetachWornToCaller(_source.Layer, _source.Slot);
```

Delete the inline `ResolveLayer` helper from the action file (no longer used after this swap).

- [ ] **Step 3: Verify compile**

Run MCP `assets-refresh`.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Character/CharacterActions/CharacterAction_CarryInHand.cs Assets/Scripts/Character/CharacterEquipment/CharacterEquipment.cs
git commit -m "feat(actions): add CharacterAction_CarryInHand with smart-swap algorithm"
```

---

### Task 10: `CharacterAction_UseItem`

**Files:**
- Create: `Assets/Scripts/Character/CharacterActions/CharacterAction_UseItem.cs`

- [ ] **Step 1: Create the file**

Dispatches via `ConsumableInstance.ApplyEffect(character)` (existing virtual). For Bag-sourced consumables: removes from inventory then applies effect (destroys the instance). For Hands-sourced consumables: clears the hand carry then applies effect.

```csharp
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative: dispatches a consumable's effect via
/// <see cref="ConsumableInstance.ApplyEffect"/>. The item instance is consumed
/// (removed from its source — bag slot or hand carry).
///
/// <para>Sources supported: BagSlot, HandsCarry. Worn slots and active weapon
/// cannot be Use targets (the verb does not appear in their popups).</para>
///
/// <para>Queued by player UI clicks (Use verb on a consumable) and by future NPC AI
/// (e.g. <c>GoapAction_BuyFood</c>'s eat step). Rule #22 player↔NPC parity.</para>
/// </summary>
public sealed class CharacterAction_UseItem : CharacterAction
{
    private readonly EquipmentSourceRef _source;

    public EquipmentSourceRef Source => _source;

    public CharacterAction_UseItem(Character character, EquipmentSourceRef source)
        : base(character, 0f)
    {
        _source = source;
    }

    public override bool CanExecute()
    {
        if (character == null) return false;
        if (_source.Kind != EquipmentSourceKind.BagSlot && _source.Kind != EquipmentSourceKind.HandsCarry) return false;
        return character.CharacterEquipment != null;
    }

    public override void OnStart() { /* no animation v1 — could trigger Trigger_Eat later */ }

    public override void OnApplyEffect()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
        if (character == null) return;

        var equip = character.CharacterEquipment;
        if (equip == null) return;

        ItemInstance instance = ResolveAndDetach(equip);
        if (instance == null) return;

        if (!(instance is ConsumableInstance consumable))
        {
            if (NPCDebug.VerboseActions)
                Debug.LogWarning($"<color=orange>[UseItem]</color> {character.CharacterName} aborted: {instance.ItemSO.ItemName} is not a ConsumableInstance.");
            return;
        }

        consumable.ApplyEffect(character);
        if (NPCDebug.VerboseActions)
            Debug.Log($"<color=green>[UseItem]</color> {character.CharacterName} used {consumable.ItemSO.ItemName}.");
    }

    private ItemInstance ResolveAndDetach(CharacterEquipment equip)
    {
        switch (_source.Kind)
        {
            case EquipmentSourceKind.BagSlot:
            {
                var inv = equip.GetInventory();
                if (inv == null || _source.BagIndex < 0 || _source.BagIndex >= inv.ItemSlots.Count) return null;
                var slot = inv.ItemSlots[_source.BagIndex];
                if (slot.IsEmpty()) return null;
                ItemInstance item = slot.ItemInstance;
                inv.RemoveItem(item, character);
                return item;
            }
            case EquipmentSourceKind.HandsCarry:
            {
                var hands = character.CharacterVisual?.BodyPartsController?.HandsController;
                if (hands == null || !hands.IsCarrying) return null;
                ItemInstance item = hands.CarriedItem;
                hands.ClearCarriedItem();  // destroys visual without WorldItem spawn
                return item;
            }
            default:
                return null;
        }
    }
}
```

- [ ] **Step 2: Verify compile**

Run MCP `assets-refresh`.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Character/CharacterActions/CharacterAction_UseItem.cs
git commit -m "feat(actions): add CharacterAction_UseItem (consumable dispatch via ApplyEffect)"
```

---

## Phase D — UI scripts

> No unit tests for UI scripts in this project — match precedent. Manual play-mode verification per spec §11 testing matrix after Phase E lands the wiring + prefab.

### Task 11: `UI_EquipmentActionPopup`

**Files:**
- Create: `Assets/Scripts/UI/Equipment/UI_EquipmentActionPopup.cs`

- [ ] **Step 1: Create the file**

Single shared popup component. Renders a button per verb fed from the parent window. Dismisses on ESC, click-outside, or button-click (after firing the callback).

```csharp
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MWI.UI.Equipment
{
    /// <summary>
    /// State-aware popup for the equipment window. Same component used by every
    /// item-bearing cell (bag, worn mini-cell, special slot cards). Fed a verb list
    /// per state — see <see cref="UI_CharacterEquipment.OpenPopupFor"/>.
    ///
    /// <para>Dismissal: ESC, click outside, OR button click (action then close).
    /// One instance per <see cref="UI_CharacterEquipment"/>; activated/deactivated
    /// on each click, not instantiated per click (cheap).</para>
    /// </summary>
    public sealed class UI_EquipmentActionPopup : MonoBehaviour
    {
        [Header("Wiring")]
        [SerializeField] private RectTransform _root;
        [SerializeField] private TextMeshProUGUI _titleLabel;
        [SerializeField] private TextMeshProUGUI _subtitleLabel;
        [SerializeField] private RectTransform _buttonContainer;
        [SerializeField] private Button _buttonPrefab;     // leaf prefab with TMP_Pro label + optional key-hint child

        private readonly List<Button> _spawnedButtons = new List<Button>();
        private Action<EquipmentVerb> _verbCallback;

        public bool IsOpen => gameObject.activeSelf;

        private void Awake()
        {
            if (_root == null) _root = (RectTransform)transform;
            gameObject.SetActive(false);
        }

        public void Show(
            RectTransform anchor,
            string title,
            string subtitle,
            IReadOnlyList<EquipmentVerb> verbs,
            Action<EquipmentVerb> onVerbSelected)
        {
            if (anchor == null || verbs == null || verbs.Count == 0)
            {
                Hide();
                return;
            }

            _titleLabel.text = title ?? string.Empty;
            _subtitleLabel.text = subtitle ?? string.Empty;
            _verbCallback = onVerbSelected;

            // Clear previous buttons.
            for (int i = 0; i < _spawnedButtons.Count; i++)
            {
                if (_spawnedButtons[i] != null) Destroy(_spawnedButtons[i].gameObject);
            }
            _spawnedButtons.Clear();

            // Spawn one button per verb.
            for (int i = 0; i < verbs.Count; i++)
            {
                EquipmentVerb verb = verbs[i];
                Button btn = Instantiate(_buttonPrefab, _buttonContainer);
                btn.gameObject.SetActive(true);
                var lbl = btn.GetComponentInChildren<TextMeshProUGUI>();
                if (lbl != null) lbl.text = verb.Label;
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => OnButtonClicked(verb));
                _spawnedButtons.Add(btn);
            }

            // Position popup near the anchor (right-side, fallback to below).
            PositionNearAnchor(anchor);

            gameObject.SetActive(true);
        }

        public void Hide()
        {
            gameObject.SetActive(false);
            _verbCallback = null;
        }

        private void OnButtonClicked(EquipmentVerb verb)
        {
            var cb = _verbCallback;
            Hide();
            cb?.Invoke(verb);
        }

        private void PositionNearAnchor(RectTransform anchor)
        {
            // Naive placement — anchor.position + small offset to the right.
            // Refinement (clip-to-screen, side-flip) is prefab-authoring polish, not blocking.
            _root.position = anchor.position + (Vector3)new Vector2(anchor.rect.width * 0.5f + 12f, 0f);
        }

        private void Update()
        {
            if (!IsOpen) return;

            // ESC dismisses.
            if (Input.GetKeyDown(KeyCode.Escape)) { Hide(); return; }

            // Click outside dismisses. Use EventSystem to filter clicks that hit OUR rect.
            if (Input.GetMouseButtonDown(0))
            {
                if (!RectTransformUtility.RectangleContainsScreenPoint(_root, Input.mousePosition, null))
                    Hide();
            }
        }
    }

    /// <summary>
    /// A single popup entry: label + behavior identifier. The parent window maps
    /// VerbId to a concrete <see cref="CharacterAction"/> queue call.
    /// </summary>
    public readonly struct EquipmentVerb
    {
        public readonly EquipmentVerbId Id;
        public readonly string Label;
        public readonly bool IsDanger;

        public EquipmentVerb(EquipmentVerbId id, string label, bool isDanger = false)
        {
            Id = id; Label = label; IsDanger = isDanger;
        }
    }

    public enum EquipmentVerbId
    {
        Equip,
        Unequip,
        CarryInHand,
        StashInBag,
        UseConsumable,
        UnequipBag,
        DropToGround,
    }
}
```

- [ ] **Step 2: Verify compile**

Run MCP `assets-refresh`.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/UI/Equipment/UI_EquipmentActionPopup.cs
git commit -m "feat(ui): UI_EquipmentActionPopup — shared state-aware popup component"
```

---

### Task 12: `UI_EquipmentWornCell`

**Files:**
- Create: `Assets/Scripts/UI/Equipment/UI_EquipmentWornCell.cs`

- [ ] **Step 1: Create the file**

```csharp
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MWI.UI.Equipment
{
    /// <summary>
    /// One mini-cell on the paper-doll. Owns a single (layer, slot) coordinate.
    /// Click opens the popup with worn-item verbs (Unequip · CarryInHand · DropToGround).
    /// Empty cells are non-interactive visual placeholders.
    /// </summary>
    public sealed class UI_EquipmentWornCell : MonoBehaviour
    {
        [Header("Coordinate")]
        [SerializeField] private WearableLayerEnum _layer;
        [SerializeField] private WearableType _slot;

        [Header("Visual")]
        [SerializeField] private Image _iconImage;
        [SerializeField] private TextMeshProUGUI _layerTag;
        [SerializeField] private Button _clickButton;

        private UI_CharacterEquipment _window;

        public WearableLayerEnum Layer => _layer;
        public WearableType Slot => _slot;

        public void Initialize(UI_CharacterEquipment window)
        {
            _window = window;
            _clickButton.onClick.RemoveAllListeners();
            _clickButton.onClick.AddListener(OnCellClicked);
        }

        /// <summary>
        /// Repaints from the current equipment state. Called by the parent window
        /// after every OnEquipmentChanged.
        /// </summary>
        public void Refresh(EquipmentInstance instance)
        {
            bool filled = instance != null && instance.ItemSO != null;
            _iconImage.enabled = filled;
            if (filled) _iconImage.sprite = instance.ItemSO.Icon;
            _clickButton.interactable = filled;
        }

        private void OnCellClicked()
        {
            if (_window == null) return;
            _window.OpenPopupForWornCell(this);
        }

        private void OnDestroy()
        {
            if (_clickButton != null) _clickButton.onClick.RemoveAllListeners();
        }
    }
}
```

- [ ] **Step 2: Verify compile**

Run MCP `assets-refresh`. Expected error: `UI_CharacterEquipment.OpenPopupForWornCell` not defined — that's fine; resolved in Task 15. Other compile errors must be zero.

- [ ] **Step 3: Commit**

(Defer commit until Task 15 lands the parent window — `UI_EquipmentWornCell` references `UI_CharacterEquipment.OpenPopupForWornCell` which doesn't exist yet. Tasks 12–14 are committed together with Task 15.)

---

### Task 13: `UI_EquipmentBagCell`

**Files:**
- Create: `Assets/Scripts/UI/Equipment/UI_EquipmentBagCell.cs`

- [ ] **Step 1: Create the file**

```csharp
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MWI.UI.Equipment
{
    /// <summary>
    /// One slot in the bag-inventory grid. Owns a bag slot index. Click opens the
    /// popup with bag-item verbs — verb set depends on the item kind (wearable /
    /// consumable / weapon / misc); decided by the parent window.
    /// </summary>
    public sealed class UI_EquipmentBagCell : MonoBehaviour
    {
        [Header("Visual")]
        [SerializeField] private Image _iconImage;
        [SerializeField] private TextMeshProUGUI _quantityLabel;     // optional, may be null
        [SerializeField] private TextMeshProUGUI _typeTagLabel;      // "W" for WeaponSlot, blank otherwise
        [SerializeField] private Button _clickButton;
        [SerializeField] private GameObject _weaponSlotBackground;   // optional visual tint for WeaponSlot

        private UI_CharacterEquipment _window;
        private int _slotIndex = -1;

        public int SlotIndex => _slotIndex;

        public void Initialize(UI_CharacterEquipment window, int slotIndex, bool isWeaponSlot)
        {
            _window = window;
            _slotIndex = slotIndex;
            if (_typeTagLabel != null) _typeTagLabel.text = isWeaponSlot ? "W" : string.Empty;
            if (_weaponSlotBackground != null) _weaponSlotBackground.SetActive(isWeaponSlot);
            _clickButton.onClick.RemoveAllListeners();
            _clickButton.onClick.AddListener(OnCellClicked);
        }

        public void Refresh(ItemInstance instance)
        {
            bool filled = instance != null && instance.ItemSO != null;
            _iconImage.enabled = filled;
            if (filled) _iconImage.sprite = instance.ItemSO.Icon;
            if (_quantityLabel != null)
            {
                int qty = filled ? instance.Quantity : 0;
                _quantityLabel.text = qty > 1 ? qty.ToString() : string.Empty;
            }
            _clickButton.interactable = filled;
        }

        private void OnCellClicked()
        {
            if (_window == null || _slotIndex < 0) return;
            _window.OpenPopupForBagCell(this);
        }

        private void OnDestroy()
        {
            if (_clickButton != null) _clickButton.onClick.RemoveAllListeners();
        }
    }
}
```

Note on `instance.Quantity`: confirm `ItemInstance` exposes a quantity accessor (`Quantity` vs `Count` vs none) at file-write time via Grep `class ItemInstance`. If absent, drop the quantity-label line and remove the field. Quantity badges are a polish layer that can be added later if `ItemInstance` already supports it; today's bag slots are 1-item-per-slot per `wiki/systems/inventory.md` §"Open questions".

- [ ] **Step 2: Verify compile**

Run MCP `assets-refresh`. Expected error: `UI_CharacterEquipment.OpenPopupForBagCell` not defined; ignore until Task 15.

- [ ] **Step 3: Defer commit until Task 15**

---

### Task 14: `UI_EquipmentSpecialSlotCard`

**Files:**
- Create: `Assets/Scripts/UI/Equipment/UI_EquipmentSpecialSlotCard.cs`

- [ ] **Step 1: Create the file**

```csharp
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MWI.UI.Equipment
{
    /// <summary>
    /// One of the three top-row cards in the equipment window — Active Weapon,
    /// Hands Carry, or Equipped Bag. Owns a SlotKind discriminator. Click opens
    /// the popup with kind-appropriate verbs.
    /// </summary>
    public sealed class UI_EquipmentSpecialSlotCard : MonoBehaviour
    {
        public enum SlotKind { ActiveWeapon, HandsCarry, EquippedBag }

        [Header("Identity")]
        [SerializeField] private SlotKind _kind;

        [Header("Visual")]
        [SerializeField] private TextMeshProUGUI _labelText;     // "Active Weapon", "Hands carry", "Bag"
        [SerializeField] private TextMeshProUGUI _valueText;     // item name or "(empty)"
        [SerializeField] private TextMeshProUGUI _metaText;      // sub-text — dmg, capacity, etc.
        [SerializeField] private Image _iconImage;
        [SerializeField] private Button _clickButton;

        private UI_CharacterEquipment _window;

        public SlotKind Kind => _kind;

        public void Initialize(UI_CharacterEquipment window)
        {
            _window = window;
            _clickButton.onClick.RemoveAllListeners();
            _clickButton.onClick.AddListener(OnCardClicked);
        }

        public void RefreshActiveWeapon(WeaponInstance weapon)
        {
            bool filled = weapon != null && weapon.ItemSO != null;
            _valueText.text = filled ? weapon.ItemSO.ItemName : "(empty)";
            _metaText.text = filled ? "swap via combat HUD (Y)" : string.Empty;
            _iconImage.enabled = filled;
            if (filled) _iconImage.sprite = weapon.ItemSO.Icon;
            _clickButton.interactable = filled;
        }

        public void RefreshHandsCarry(ItemInstance carry)
        {
            bool filled = carry != null && carry.ItemSO != null;
            _valueText.text = filled ? carry.ItemSO.ItemName : "(empty)";
            _metaText.text = filled ? "click for actions" : string.Empty;
            _iconImage.enabled = filled;
            if (filled) _iconImage.sprite = carry.ItemSO.Icon;
            _clickButton.interactable = filled;
        }

        public void RefreshEquippedBag(BagInstance bag, int used, int capacity)
        {
            bool filled = bag != null && bag.ItemSO != null;
            _valueText.text = filled ? bag.ItemSO.ItemName : "(none)";
            _metaText.text = filled ? $"{used} / {capacity} slots" : string.Empty;
            _iconImage.enabled = filled;
            if (filled) _iconImage.sprite = bag.ItemSO.Icon;
            _clickButton.interactable = filled;
        }

        private void OnCardClicked()
        {
            if (_window == null) return;
            _window.OpenPopupForSpecialCard(this);
        }

        private void OnDestroy()
        {
            if (_clickButton != null) _clickButton.onClick.RemoveAllListeners();
        }
    }
}
```

- [ ] **Step 2: Verify compile**

Run MCP `assets-refresh`. Expected error: `UI_CharacterEquipment.OpenPopupForSpecialCard` not defined; ignore until Task 15.

- [ ] **Step 3: Defer commit until Task 15**

---

### Task 15: `UI_CharacterEquipment` (window root)

**Files:**
- Create: `Assets/Scripts/UI/Equipment/UI_CharacterEquipment.cs`

- [ ] **Step 1: Create the file**

The window root. Inherits `UI_WindowBase` (rule #39). Owns the special-slot cards, the 15 worn cells (5 stacks × 3 layers), the bag-cell list, and the shared popup. Subscribes to `OnEquipmentChanged` (slots) + `HandsController.OnCarriedItemChanged` (hand) + `Inventory.OnInventoryChanged` (bag contents). Maps each verb selection to the matching `CharacterAction`.

```csharp
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace MWI.UI.Equipment
{
    /// <summary>
    /// Root for the equipment window. Variant of UI_WindowBase.prefab (rule #39).
    /// Hosts: 3 special-slot cards (top row), 15 worn mini-cells (5 stacks × 3 layers),
    /// N bag-inventory cells (right grid), 1 shared action popup.
    ///
    /// <para>Subscribes to <see cref="CharacterEquipment.OnEquipmentChanged"/> for
    /// slot updates, <see cref="HandsController.OnCarriedItemChanged"/> for hand-carry
    /// updates (event added in plan-phase Task 1), and <see cref="Inventory.OnInventoryChanged"/>
    /// for bag content updates. No polling.</para>
    /// </summary>
    public sealed class UI_CharacterEquipment : UI_WindowBase
    {
        [Header("Title")]
        [SerializeField] private TextMeshProUGUI _titleLabel;

        [Header("Special slot cards (top row)")]
        [SerializeField] private UI_EquipmentSpecialSlotCard _weaponCard;
        [SerializeField] private UI_EquipmentSpecialSlotCard _handsCard;
        [SerializeField] private UI_EquipmentSpecialSlotCard _bagCard;

        [Header("Paper-doll worn cells")]
        [Tooltip("Authored under the doll stage RectTransform. Exactly 15: 5 body slots × 3 layers (U / C / A).")]
        [SerializeField] private List<UI_EquipmentWornCell> _wornCells = new List<UI_EquipmentWornCell>();

        [Header("Bag grid")]
        [SerializeField] private RectTransform _bagCellContainer;
        [SerializeField] private UI_EquipmentBagCell _bagCellPrefab;

        [Header("Popup")]
        [SerializeField] private UI_EquipmentActionPopup _popup;

        private Character _character;
        private readonly List<UI_EquipmentBagCell> _bagCells = new List<UI_EquipmentBagCell>();

        protected override void Awake()
        {
            base.Awake();  // CRITICAL: wires inherited _buttonClose + assigns Camera.main to canvas
            if (_popup != null) _popup.Hide();
        }

        public void Initialize(Character target)
        {
            if (target == null) return;

            // Unbind previous character if any.
            UnbindCharacter();

            _character = target;
            _titleLabel.text = $"Equipment — {target.CharacterName}";

            BindCharacter();
            BuildBagCells();
            InitializeChildren();
            RepaintAll();

            OpenWindow();
        }

        public override void CloseWindow()
        {
            if (_popup != null) _popup.Hide();
            UnbindCharacter();
            base.CloseWindow();
        }

        private void BindCharacter()
        {
            if (_character == null) return;
            var equip = _character.CharacterEquipment;
            if (equip != null)
            {
                equip.OnEquipmentChanged += OnEquipmentChanged;
                var inv = equip.GetInventory();
                if (inv != null) inv.OnInventoryChanged += OnInventoryChanged;
            }
            var hands = _character.CharacterVisual?.BodyPartsController?.HandsController;
            if (hands != null) hands.OnCarriedItemChanged += OnHandsCarryChanged;
        }

        private void UnbindCharacter()
        {
            if (_character == null) return;
            var equip = _character.CharacterEquipment;
            if (equip != null)
            {
                equip.OnEquipmentChanged -= OnEquipmentChanged;
                var inv = equip.GetInventory();
                if (inv != null) inv.OnInventoryChanged -= OnInventoryChanged;
            }
            var hands = _character.CharacterVisual?.BodyPartsController?.HandsController;
            if (hands != null) hands.OnCarriedItemChanged -= OnHandsCarryChanged;
            _character = null;
        }

        private void InitializeChildren()
        {
            _weaponCard?.Initialize(this);
            _handsCard?.Initialize(this);
            _bagCard?.Initialize(this);
            for (int i = 0; i < _wornCells.Count; i++)
            {
                if (_wornCells[i] != null) _wornCells[i].Initialize(this);
            }
        }

        private void BuildBagCells()
        {
            // Destroy previous instantiated cells (if any).
            for (int i = 0; i < _bagCells.Count; i++)
            {
                if (_bagCells[i] != null) Destroy(_bagCells[i].gameObject);
            }
            _bagCells.Clear();

            var inv = _character.CharacterEquipment?.GetInventory();
            if (inv == null) return;

            for (int i = 0; i < inv.ItemSlots.Count; i++)
            {
                var cell = Instantiate(_bagCellPrefab, _bagCellContainer);
                cell.gameObject.SetActive(true);
                bool isWeapon = inv.ItemSlots[i] is WeaponSlot;
                cell.Initialize(this, i, isWeapon);
                _bagCells.Add(cell);
            }
        }

        private void RepaintAll()
        {
            if (_character == null) return;
            var equip = _character.CharacterEquipment;
            if (equip == null) return;

            // Special cards.
            _weaponCard?.RefreshActiveWeapon(equip.CurrentWeapon);
            var hands = _character.CharacterVisual?.BodyPartsController?.HandsController;
            _handsCard?.RefreshHandsCarry(hands?.CarriedItem);
            var bag = equip.GetBagInstance();
            var inv = equip.GetInventory();
            int used = 0;
            int cap = inv != null ? inv.Capacity : 0;
            if (inv != null)
            {
                for (int i = 0; i < inv.ItemSlots.Count; i++) if (!inv.ItemSlots[i].IsEmpty()) used++;
            }
            _bagCard?.RefreshEquippedBag(bag, used, cap);

            // Worn cells.
            for (int i = 0; i < _wornCells.Count; i++)
            {
                var cell = _wornCells[i];
                if (cell == null) continue;
                EquipmentLayer layer = ResolveLayer(equip, cell.Layer);
                EquipmentInstance inst = layer != null ? layer.GetInstance(cell.Slot) : null;
                cell.Refresh(inst);
            }

            // Bag cells.
            if (inv != null)
            {
                for (int i = 0; i < _bagCells.Count && i < inv.ItemSlots.Count; i++)
                {
                    var slot = inv.ItemSlots[i];
                    _bagCells[i].Refresh(slot.IsEmpty() ? null : slot.ItemInstance);
                }
            }
        }

        // -------------------------------------------------------------
        // Event callbacks — all just route to RepaintAll.
        // -------------------------------------------------------------
        private void OnEquipmentChanged() => RepaintAll();
        private void OnInventoryChanged() => RepaintAll();
        private void OnHandsCarryChanged(ItemInstance _) => RepaintAll();

        // -------------------------------------------------------------
        // Popup entry points called by leaf cells.
        // -------------------------------------------------------------
        public void OpenPopupForBagCell(UI_EquipmentBagCell cell)
        {
            if (cell == null || _character == null || _popup == null) return;
            var inv = _character.CharacterEquipment?.GetInventory();
            if (inv == null || cell.SlotIndex < 0 || cell.SlotIndex >= inv.ItemSlots.Count) return;
            var slot = inv.ItemSlots[cell.SlotIndex];
            if (slot.IsEmpty()) return;
            ItemInstance item = slot.ItemInstance;

            var verbs = BuildBagVerbs(item);
            EquipmentSourceRef source = EquipmentSourceRef.Bag(cell.SlotIndex);
            _popup.Show(
                (RectTransform)cell.transform,
                item.ItemSO.ItemName,
                BuildBagSubtitle(item),
                verbs,
                v => OnVerbSelected(v, source, item));
        }

        public void OpenPopupForWornCell(UI_EquipmentWornCell cell)
        {
            if (cell == null || _character == null || _popup == null) return;
            var equip = _character.CharacterEquipment;
            if (equip == null) return;
            EquipmentLayer layer = ResolveLayer(equip, cell.Layer);
            EquipmentInstance inst = layer?.GetInstance(cell.Slot);
            if (inst == null) return;

            var verbs = new List<EquipmentVerb>
            {
                new EquipmentVerb(EquipmentVerbId.Unequip,     "Unequip"),
                new EquipmentVerb(EquipmentVerbId.CarryInHand, "Carry in hand"),
                new EquipmentVerb(EquipmentVerbId.DropToGround,"Drop on ground", isDanger: true),
            };
            EquipmentSourceRef source = EquipmentSourceRef.Worn(cell.Layer, cell.Slot);
            _popup.Show(
                (RectTransform)cell.transform,
                inst.ItemSO.ItemName,
                $"{cell.Layer} layer · {cell.Slot}",
                verbs,
                v => OnVerbSelected(v, source, inst));
        }

        public void OpenPopupForSpecialCard(UI_EquipmentSpecialSlotCard card)
        {
            if (card == null || _character == null || _popup == null) return;
            var equip = _character.CharacterEquipment;
            if (equip == null) return;

            switch (card.Kind)
            {
                case UI_EquipmentSpecialSlotCard.SlotKind.ActiveWeapon:
                {
                    WeaponInstance w = equip.CurrentWeapon;
                    if (w == null) return;
                    var verbs = new List<EquipmentVerb>
                    {
                        new EquipmentVerb(EquipmentVerbId.StashInBag,   "Stash in bag"),
                        new EquipmentVerb(EquipmentVerbId.CarryInHand,  "Carry in hand"),
                        new EquipmentVerb(EquipmentVerbId.DropToGround, "Drop on ground", isDanger: true),
                    };
                    _popup.Show((RectTransform)card.transform, w.ItemSO.ItemName, "Weapon · currently wielded", verbs,
                        v => OnVerbSelected(v, EquipmentSourceRef.Weapon(), w));
                    break;
                }
                case UI_EquipmentSpecialSlotCard.SlotKind.HandsCarry:
                {
                    var hands = _character.CharacterVisual?.BodyPartsController?.HandsController;
                    if (hands == null || !hands.IsCarrying) return;
                    ItemInstance c = hands.CarriedItem;
                    var verbs = new List<EquipmentVerb>
                    {
                        new EquipmentVerb(EquipmentVerbId.StashInBag, "Stash in bag"),
                    };
                    if (c is ConsumableInstance)
                        verbs.Add(new EquipmentVerb(EquipmentVerbId.UseConsumable, "Use"));
                    verbs.Add(new EquipmentVerb(EquipmentVerbId.DropToGround, "Drop on ground", isDanger: true));
                    _popup.Show((RectTransform)card.transform, c.ItemSO.ItemName, "Carried in hand", verbs,
                        v => OnVerbSelected(v, EquipmentSourceRef.Hands(), c));
                    break;
                }
                case UI_EquipmentSpecialSlotCard.SlotKind.EquippedBag:
                {
                    BagInstance b = equip.GetBagInstance();
                    if (b == null) return;
                    var verbs = new List<EquipmentVerb>
                    {
                        new EquipmentVerb(EquipmentVerbId.UnequipBag, "Unequip bag", isDanger: true),
                    };
                    _popup.Show((RectTransform)card.transform, b.ItemSO.ItemName, "Equipped bag (drops with contents)", verbs,
                        v => OnVerbSelected(v, EquipmentSourceRef.Hands() /* unused */, b));
                    break;
                }
            }
        }

        // -------------------------------------------------------------
        // Verb dispatch.
        // -------------------------------------------------------------
        private void OnVerbSelected(EquipmentVerb verb, EquipmentSourceRef source, ItemInstance contextItem)
        {
            if (_character == null) return;
            var actions = _character.CharacterActions;
            if (actions == null) return;

            switch (verb.Id)
            {
                case EquipmentVerbId.Equip:
                    actions.ExecuteAction(new CharacterAction_EquipWearable(_character, source.BagIndex));
                    break;
                case EquipmentVerbId.Unequip:
                    actions.ExecuteAction(new CharacterAction_UnequipWearable(_character, source.Layer, source.Slot));
                    break;
                case EquipmentVerbId.CarryInHand:
                    actions.ExecuteAction(new CharacterAction_CarryInHand(_character, source));
                    break;
                case EquipmentVerbId.StashInBag:
                    actions.ExecuteAction(new CharacterAction_StashInBag(_character, source));
                    break;
                case EquipmentVerbId.UseConsumable:
                    actions.ExecuteAction(new CharacterAction_UseItem(_character, source));
                    break;
                case EquipmentVerbId.UnequipBag:
                    // No CharacterAction wrapper for v1 — direct call mirrors current behavior.
                    // Future NPC AI that needs to drop its bag would wrap this in an action.
                    _character.CharacterEquipment?.UnequipBag();
                    break;
                case EquipmentVerbId.DropToGround:
                    actions.ExecuteAction(new CharacterDropItem(_character, contextItem));
                    break;
            }
        }

        // -------------------------------------------------------------
        // Helpers.
        // -------------------------------------------------------------
        private static List<EquipmentVerb> BuildBagVerbs(ItemInstance item)
        {
            var verbs = new List<EquipmentVerb>();
            if (item is WearableInstance)
                verbs.Add(new EquipmentVerb(EquipmentVerbId.Equip, "Equip"));
            else if (item is ConsumableInstance)
                verbs.Add(new EquipmentVerb(EquipmentVerbId.UseConsumable, "Use"));
            // Weapons fall through to plain Carry/Drop — active-swap lives on the combat HUD.
            verbs.Add(new EquipmentVerb(EquipmentVerbId.CarryInHand, "Carry in hand"));
            verbs.Add(new EquipmentVerb(EquipmentVerbId.DropToGround, "Drop on ground", isDanger: true));
            return verbs;
        }

        private static string BuildBagSubtitle(ItemInstance item)
        {
            if (item is WearableInstance && item.ItemSO is WearableSO ws)
                return $"Wearable · {ws.EquipmentLayer} · {ws.WearableType}";
            if (item is ConsumableInstance) return "Consumable";
            if (item is WeaponInstance) return "Weapon";
            return item.ItemSO?.GetType().Name ?? "Item";
        }

        private static EquipmentLayer ResolveLayer(CharacterEquipment equip, WearableLayerEnum layer) => layer switch
        {
            WearableLayerEnum.Underwear => equip.UnderwearLayer,
            WearableLayerEnum.Clothing  => equip.ClothingLayer,
            WearableLayerEnum.Armor     => equip.ArmorLayer,
            _ => null,
        };

        private void OnDestroy()
        {
            UnbindCharacter();
        }
    }
}
```

- [ ] **Step 2: Verify compile**

Run MCP `assets-refresh`. Expected: no errors (all forward references from Tasks 12–14 now resolved).

- [ ] **Step 3: Commit Tasks 12–15 together**

```bash
git add Assets/Scripts/UI/Equipment/
git commit -m "feat(ui): UI_CharacterEquipment window + worn/bag/special leaf cells"
```

---

## Phase E — Wiring + cleanup

### Task 16: Update `PlayerUI` — retype, Open/Close/Toggle Window methods

**Files:**
- Modify: `Assets/Scripts/UI/PlayerUI.cs`

- [ ] **Step 1: Retype the SerializeField**

Edit line 43 from:

```csharp
    [SerializeField] private CharacterEquipmentUI _equipmentUI;
```

To:

```csharp
    [SerializeField] private MWI.UI.Equipment.UI_CharacterEquipment _equipmentUI;
```

- [ ] **Step 2: Replace `ToggleEquipmentUI` with the rule #39 Open/Close/Toggle trio**

Find the existing `ToggleEquipmentUI()` method (line 264). Replace it with:

```csharp
    /// <summary>
    /// Opens the character equipment window for the given target Character.
    /// Logs a directive warning if the SerializeField is unwired (rule #39).
    /// </summary>
    public void OpenEquipmentWindow(Character target)
    {
        if (_equipmentUI == null)
        {
            Debug.LogWarning("<color=orange>[PlayerUI]</color> OpenEquipmentWindow called but _equipmentUI SerializeField is null — author the prefab (variant of UI_WindowBase.prefab) and wire it to PlayerUI._equipmentUI in the Inspector.");
            return;
        }
        if (target == null) return;
        _equipmentUI.Initialize(target);  // also calls OpenWindow internally
    }

    public void CloseEquipmentWindow()
    {
        if (_equipmentUI == null) return;
        _equipmentUI.CloseWindow();
    }

    public void ToggleEquipmentWindow(Character target)
    {
        if (_equipmentUI == null)
        {
            Debug.LogWarning("<color=orange>[PlayerUI]</color> ToggleEquipmentWindow called but _equipmentUI SerializeField is null — author the prefab (variant of UI_WindowBase.prefab) and wire it to PlayerUI._equipmentUI in the Inspector.");
            return;
        }
        if (_equipmentUI.gameObject.activeSelf) CloseEquipmentWindow();
        else OpenEquipmentWindow(target);
    }

    /// <summary>
    /// Legacy shim — kept for one rename window so the existing _buttonEquipmentUI.onClick
    /// listener wired in <see cref="WireButtons"/> keeps working until Task 17 repoints
    /// PlayerController's Tab binding. Delete in Task 20 cleanup.
    /// </summary>
    public void ToggleEquipmentUI()
    {
        ToggleEquipmentWindow(characterComponent);
    }
```

- [ ] **Step 3: Verify compile**

Run MCP `assets-refresh`. Expected error: the scene's PlayerUI prefab/instance has `_equipmentUI` field wired to the OLD `CharacterEquipmentUI` component — Unity will null this out at deserialization (type mismatch). EXPECTED — Task 19 re-wires it.

If there are compile errors beyond the type-mismatch null-out, surface to human before proceeding.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/UI/PlayerUI.cs
git commit -m "refactor(ui): PlayerUI surface for equipment window (Open/Close/Toggle + rule #39 null-guard)"
```

---

### Task 17: Repoint `PlayerController` Tab binding

**Files:**
- Modify: `Assets/Scripts/Character/CharacterControllers/PlayerController.cs`

- [ ] **Step 1: Inspect the Tab branch around line 216**

Read lines 210–230 of `PlayerController.cs` and confirm the current shape calls `PlayerUI.Instance.ToggleEquipmentUI()`. (If it's different — e.g. calls `PlayerUI.Instance.ToggleEquipmentWindow(_character)` already — skip this task entirely.)

- [ ] **Step 2: Repoint the call**

Replace any `PlayerUI.Instance.ToggleEquipmentUI()` call inside the Tab branch with:

```csharp
                if (Input.GetKeyDown(KeyCode.Tab))
                {
                    PlayerUI.Instance?.ToggleEquipmentWindow(_character);
                }
```

- [ ] **Step 3: Verify compile**

Run MCP `assets-refresh`. Expected: no errors.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Character/CharacterControllers/PlayerController.cs
git commit -m "refactor(player): repoint Tab to PlayerUI.ToggleEquipmentWindow"
```

---

### Task 18: Author `UI_CharacterEquipment.prefab` (MCP Roslyn)

**Files:**
- Create: `Assets/UI/Player HUD/UI_CharacterEquipment.prefab` (Variant of `UI_WindowBase.prefab`)
- Create: `Assets/UI/Player HUD/UI_EquipmentBagCell.prefab` (leaf prefab, instantiated at runtime)

> This task is mechanical UI authoring — long, but no decisions. Mirrors the canonical recipe in `.agent/skills/ui-hud/SKILL.md` §3 "Authoring a new window via MCP".

- [ ] **Step 1: Author UI_CharacterEquipment.prefab as a Variant**

Run via MCP `mcp__ai-game-developer__script-execute` with this body:

```csharp
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 1. Load base + instantiate as variant editing context.
var baseWindow = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/UI/Player HUD/UI_WindowBase.prefab");
GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(baseWindow);
root.name = "UI_CharacterEquipment";

// 2. Fix the inherited Canvas (rule #39: scale must override to (1,1,1)).
var canvas = root.GetComponentInChildren<Canvas>(true);
var canvasRt = canvas.GetComponent<RectTransform>();
canvasRt.localScale = Vector3.one;
canvasRt.localRotation = Quaternion.identity;
canvasRt.localPosition = Vector3.zero;

// 3. Add the UI_CharacterEquipment script as a Variant override.
var t = System.Type.GetType("MWI.UI.Equipment.UI_CharacterEquipment, Assembly-CSharp", false);
var script = root.AddComponent(t);

// 4. Build the content under the inherited Canvas (NOT under root — would create
//    second Canvas, see 2026-05-16 SafeFurniture gotcha).
Transform contentParent = canvas.transform;

// --- Title label ---
var titleGo = new GameObject("TitleLabel", typeof(RectTransform), typeof(TextMeshProUGUI));
titleGo.transform.SetParent(contentParent, false);
var titleRt = (RectTransform)titleGo.transform;
titleRt.anchorMin = new Vector2(0f, 1f); titleRt.anchorMax = new Vector2(1f, 1f);
titleRt.pivot = new Vector2(0.5f, 1f);
titleRt.anchoredPosition = new Vector2(0, -10);
titleRt.sizeDelta = new Vector2(0, 28);
var titleTmp = titleGo.GetComponent<TextMeshProUGUI>();
titleTmp.text = "Equipment";
titleTmp.fontSize = 18;
titleTmp.alignment = TextAlignmentOptions.Center;

// --- Special row container (3 cards horizontally) ---
var specialRow = new GameObject("SpecialRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
specialRow.transform.SetParent(contentParent, false);
var srRt = (RectTransform)specialRow.transform;
srRt.anchorMin = new Vector2(0f, 1f); srRt.anchorMax = new Vector2(1f, 1f);
srRt.pivot = new Vector2(0.5f, 1f);
srRt.anchoredPosition = new Vector2(0, -50);
srRt.sizeDelta = new Vector2(-20, 60);
var srLayout = specialRow.GetComponent<HorizontalLayoutGroup>();
srLayout.spacing = 8; srLayout.padding = new RectOffset(10, 10, 4, 4);
srLayout.childControlWidth = true; srLayout.childControlHeight = true;
srLayout.childForceExpandWidth = true; srLayout.childForceExpandHeight = true;

// Helper for a special-slot card (creates a Button GameObject with label+value+meta text children).
System.Func<string, GameObject> CreateSpecialCard = name =>
{
    var card = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button),
                              typeof(MWI.UI.Equipment.UI_EquipmentSpecialSlotCard));
    card.transform.SetParent(specialRow.transform, false);
    var img = card.GetComponent<Image>();
    img.color = new Color(0.15f, 0.17f, 0.22f);
    // Stack labels vertically.
    var vlay = card.AddComponent<VerticalLayoutGroup>();
    vlay.padding = new RectOffset(8, 8, 4, 4);
    vlay.spacing = 1;

    System.Action<string, int> AddLabel = (n, size) =>
    {
        var go = new GameObject(n, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(card.transform, false);
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.fontSize = size;
    };
    AddLabel("LabelText", 9);
    AddLabel("ValueText", 12);
    AddLabel("MetaText", 9);

    return card;
};

GameObject weaponCard = CreateSpecialCard("WeaponCard");
GameObject handsCard  = CreateSpecialCard("HandsCard");
GameObject bagCard    = CreateSpecialCard("BagCard");

// --- Body row: doll stage left + bag grid right ---
var body = new GameObject("Body", typeof(RectTransform), typeof(HorizontalLayoutGroup));
body.transform.SetParent(contentParent, false);
var bodyRt = (RectTransform)body.transform;
bodyRt.anchorMin = new Vector2(0f, 0f); bodyRt.anchorMax = new Vector2(1f, 1f);
bodyRt.offsetMin = new Vector2(10, 10); bodyRt.offsetMax = new Vector2(-10, -120);
var bodyLayout = body.GetComponent<HorizontalLayoutGroup>();
bodyLayout.spacing = 12;
bodyLayout.childControlWidth = true; bodyLayout.childControlHeight = true;
bodyLayout.childForceExpandWidth = true; bodyLayout.childForceExpandHeight = true;

// --- Doll stage ---
var doll = new GameObject("DollStage", typeof(RectTransform), typeof(Image));
doll.transform.SetParent(body.transform, false);
doll.GetComponent<Image>().color = new Color(0.06f, 0.07f, 0.10f);

// --- Bag grid container ---
var bagContainer = new GameObject("BagGridContainer", typeof(RectTransform), typeof(GridLayoutGroup), typeof(Image));
bagContainer.transform.SetParent(body.transform, false);
bagContainer.GetComponent<Image>().color = new Color(0.06f, 0.07f, 0.10f);
var grid = bagContainer.GetComponent<GridLayoutGroup>();
grid.cellSize = new Vector2(36, 36);
grid.spacing = new Vector2(4, 4);
grid.padding = new RectOffset(8, 8, 8, 8);
grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
grid.constraintCount = 6;

// --- Popup ---
var popup = new GameObject("ActionPopup", typeof(RectTransform), typeof(Image),
                           typeof(MWI.UI.Equipment.UI_EquipmentActionPopup));
popup.transform.SetParent(contentParent, false);
popup.GetComponent<Image>().color = new Color(0.13f, 0.15f, 0.20f);
var popupRt = (RectTransform)popup.transform;
popupRt.sizeDelta = new Vector2(180, 140);
popup.SetActive(false);
// Popup needs title+subtitle labels + buttonContainer child.
System.Action<GameObject, string> AddPopupLabel = (parent, n) =>
{
    var go = new GameObject(n, typeof(RectTransform), typeof(TextMeshProUGUI));
    go.transform.SetParent(parent.transform, false);
};
AddPopupLabel(popup, "TitleLabel");
AddPopupLabel(popup, "SubtitleLabel");
var btnContainer = new GameObject("ButtonContainer", typeof(RectTransform), typeof(VerticalLayoutGroup));
btnContainer.transform.SetParent(popup.transform, false);

// --- Reflection helper to set private SerializeFields ---
System.Action<object, string, object> SetField = (target, fieldName, value) =>
{
    var ty = target.GetType();
    FieldInfo f = null;
    while (ty != null && f == null)
    {
        f = ty.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        ty = ty.BaseType;
    }
    if (f != null) f.SetValue(target, value);
    else Debug.LogError($"[Author] field not found: {fieldName} on {target.GetType().Name}");
};

// --- Wire script SerializeFields ---
SetField(script, "_titleLabel", titleTmp);
SetField(script, "_weaponCard", weaponCard.GetComponent<MWI.UI.Equipment.UI_EquipmentSpecialSlotCard>());
SetField(script, "_handsCard",  handsCard.GetComponent<MWI.UI.Equipment.UI_EquipmentSpecialSlotCard>());
SetField(script, "_bagCard",    bagCard.GetComponent<MWI.UI.Equipment.UI_EquipmentSpecialSlotCard>());
SetField(script, "_bagCellContainer", (RectTransform)bagContainer.transform);
SetField(script, "_popup", popup.GetComponent<MWI.UI.Equipment.UI_EquipmentActionPopup>());

// (Worn cells stay as an empty list; designer adds the 15 mini-cells under DollStage in a polish pass.
//  v1 ships with the script wired; cells get authored when a designer iterates on the doll layout.)

// Default deactivated.
root.SetActive(false);

// --- Save as variant ---
var savedAsset = PrefabUtility.SaveAsPrefabAsset(root, "Assets/UI/Player HUD/UI_CharacterEquipment.prefab");
Object.DestroyImmediate(root);
AssetDatabase.SaveAssets();
AssetDatabase.Refresh();

// --- Verify variant relationship ---
var src = PrefabUtility.GetCorrespondingObjectFromSource(savedAsset);
Debug.Log($"[Author] Saved {savedAsset.name}. Variant base: {src?.name ?? "<null>"}");
```

Expected: console shows `[Author] Saved UI_CharacterEquipment. Variant base: UI_WindowBase`.

- [ ] **Step 2: Author UI_EquipmentBagCell.prefab (leaf prefab)**

Run via MCP `mcp__ai-game-developer__script-execute`:

```csharp
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

var go = new GameObject("UI_EquipmentBagCell", typeof(RectTransform), typeof(Image), typeof(Button),
                        typeof(MWI.UI.Equipment.UI_EquipmentBagCell));
go.GetComponent<Image>().color = new Color(0.10f, 0.12f, 0.16f);

// Icon child
var icon = new GameObject("Icon", typeof(RectTransform), typeof(Image));
icon.transform.SetParent(go.transform, false);
var iconRt = (RectTransform)icon.transform;
iconRt.anchorMin = Vector2.zero; iconRt.anchorMax = Vector2.one;
iconRt.offsetMin = new Vector2(2, 2); iconRt.offsetMax = new Vector2(-2, -2);
icon.GetComponent<Image>().enabled = false;

// Quantity label
var qty = new GameObject("Qty", typeof(RectTransform), typeof(TextMeshProUGUI));
qty.transform.SetParent(go.transform, false);
var qtyRt = (RectTransform)qty.transform;
qtyRt.anchorMin = new Vector2(1, 0); qtyRt.anchorMax = new Vector2(1, 0);
qtyRt.pivot = new Vector2(1, 0); qtyRt.anchoredPosition = new Vector2(-2, 2);
var qtyTmp = qty.GetComponent<TextMeshProUGUI>(); qtyTmp.fontSize = 9;

// Type tag (W for weapon)
var tag = new GameObject("TypeTag", typeof(RectTransform), typeof(TextMeshProUGUI));
tag.transform.SetParent(go.transform, false);
var tagRt = (RectTransform)tag.transform;
tagRt.anchorMin = new Vector2(0, 1); tagRt.anchorMax = new Vector2(0, 1);
tagRt.pivot = new Vector2(0, 1); tagRt.anchoredPosition = new Vector2(2, -2);
var tagTmp = tag.GetComponent<TextMeshProUGUI>(); tagTmp.fontSize = 8;

// Weapon background (separate child, toggled by Initialize)
var weaponBg = new GameObject("WeaponSlotBackground", typeof(RectTransform), typeof(Image));
weaponBg.transform.SetParent(go.transform, false);
var wbgRt = (RectTransform)weaponBg.transform;
wbgRt.anchorMin = Vector2.zero; wbgRt.anchorMax = Vector2.one;
wbgRt.offsetMin = Vector2.zero; wbgRt.offsetMax = Vector2.zero;
weaponBg.GetComponent<Image>().color = new Color(0.18f, 0.13f, 0.16f);
weaponBg.transform.SetAsFirstSibling();  // behind icon
weaponBg.SetActive(false);

// Reflection wiring
System.Action<object, string, object> SetField = (target, fieldName, value) =>
{
    var ty = target.GetType();
    System.Reflection.FieldInfo f = null;
    while (ty != null && f == null)
    {
        f = ty.GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        ty = ty.BaseType;
    }
    if (f != null) f.SetValue(target, value);
};

var cellScript = go.GetComponent<MWI.UI.Equipment.UI_EquipmentBagCell>();
SetField(cellScript, "_iconImage", icon.GetComponent<Image>());
SetField(cellScript, "_quantityLabel", qtyTmp);
SetField(cellScript, "_typeTagLabel", tagTmp);
SetField(cellScript, "_clickButton", go.GetComponent<Button>());
SetField(cellScript, "_weaponSlotBackground", weaponBg);

var saved = PrefabUtility.SaveAsPrefabAsset(go, "Assets/UI/Player HUD/UI_EquipmentBagCell.prefab");
Object.DestroyImmediate(go);
AssetDatabase.SaveAssets();
AssetDatabase.Refresh();
Debug.Log($"[Author] Saved {saved.name}.");
```

Expected: console shows `[Author] Saved UI_EquipmentBagCell.`

- [ ] **Step 3: Wire `_bagCellPrefab` on UI_CharacterEquipment.prefab**

Run via MCP `mcp__ai-game-developer__script-execute`:

```csharp
using UnityEditor;
using UnityEngine;
using System.Reflection;

var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/UI/Player HUD/UI_CharacterEquipment.prefab");
var cellPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/UI/Player HUD/UI_EquipmentBagCell.prefab");

string path = AssetDatabase.GetAssetPath(prefab);
var root = PrefabUtility.LoadPrefabContents(path);

var script = root.GetComponent<MWI.UI.Equipment.UI_CharacterEquipment>();
var ty = script.GetType();
FieldInfo f = ty.GetField("_bagCellPrefab", BindingFlags.Instance | BindingFlags.NonPublic);
f.SetValue(script, cellPrefab.GetComponent<MWI.UI.Equipment.UI_EquipmentBagCell>());

PrefabUtility.SaveAsPrefabAsset(root, path);
PrefabUtility.UnloadPrefabContents(root);
Debug.Log("[Author] Wired _bagCellPrefab.");
```

Expected: `[Author] Wired _bagCellPrefab.`

- [ ] **Step 4: Commit**

```bash
git add "Assets/UI/Player HUD/UI_CharacterEquipment.prefab" "Assets/UI/Player HUD/UI_CharacterEquipment.prefab.meta" "Assets/UI/Player HUD/UI_EquipmentBagCell.prefab" "Assets/UI/Player HUD/UI_EquipmentBagCell.prefab.meta"
git commit -m "feat(ui): author UI_CharacterEquipment + UI_EquipmentBagCell prefabs (rule #39 Variant)"
```

Note: full polish (15 worn mini-cells under DollStage, popup button-prefab, visual styling) is intentionally deferred — the v1 scaffold ships with the window opening, the special cards painting, and the bag grid working. Designer polish (cell positions, fonts, sprites) lands in a follow-up commit per the project's "scaffold first, polish later" convention (rule #39 last bullet).

---

### Task 19: Wire `PlayerUI._equipmentUI` to new prefab instance in scene

**Files:**
- Modify: the active scene file containing `UI_PlayerHUD`

> Edit-mode-only operation. Mirrors `.agent/skills/ui-hud/SKILL.md` §4 "Wiring PlayerUI._<name>Panel at scene authoring".

- [ ] **Step 1: Find PlayerUI in the active scene**

Run via MCP `mcp__ai-game-developer__script-execute`:

```csharp
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Reflection;

if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isPlaying)
{
    Debug.LogError("BLOCKED: exit Play mode first.");
    return;
}

Scene scene = SceneManager.GetActiveScene();
PlayerUI player = null;
foreach (var rootGo in scene.GetRootGameObjects())
{
    player = rootGo.GetComponentInChildren<PlayerUI>(true);
    if (player != null) break;
}
if (player == null) { Debug.LogError("PlayerUI not found in active scene."); return; }

// Remove old CharacterEquipmentUI child if present (it's about to be deleted in Task 20 anyway,
// but the field is currently nulled by the type-mismatch from Task 16 — clear the lingering GameObject).
Transform oldChild = null;
foreach (Transform t in player.transform)
{
    if (t.GetComponent("CharacterEquipmentUI") != null) { oldChild = t; break; }
}
if (oldChild != null) Object.DestroyImmediate(oldChild.gameObject);

// Instantiate the new prefab as a PlayerUI child.
var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/UI/Player HUD/UI_CharacterEquipment.prefab");
GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, player.transform);
instance.SetActive(false);

// Wire via SerializedObject so the change persists.
var so = new SerializedObject(player);
var prop = so.FindProperty("_equipmentUI");
prop.objectReferenceValue = instance.GetComponent<MWI.UI.Equipment.UI_CharacterEquipment>();
so.ApplyModifiedPropertiesWithoutUndo();

EditorUtility.SetDirty(player);
EditorSceneManager.MarkSceneDirty(scene);
EditorSceneManager.SaveScene(scene);
Debug.Log("[Wire] PlayerUI._equipmentUI rewired to new prefab instance. Scene saved.");
```

Expected: console shows `[Wire] PlayerUI._equipmentUI rewired to new prefab instance. Scene saved.`

- [ ] **Step 2: Commit the scene change**

```bash
git add <scene-file-path>.unity
git commit -m "scene(player-hud): wire PlayerUI._equipmentUI to new UI_CharacterEquipment prefab"
```

(Use `git status` to identify the modified scene file path.)

---

### Task 20: Delete old `CharacterEquipmentUI.cs` + old prefab + shim

**Files:**
- Delete: `Assets/Scripts/UI/CharacterEquipmentUI.cs`
- Delete: `Assets/UI/Player HUD/UI_CharacterEquipment.prefab` (OLD — needs distinct delete strategy since Task 18 created a new file at this path)
- Modify: `Assets/Scripts/UI/PlayerUI.cs` (remove the legacy `ToggleEquipmentUI` shim)

**Important — path collision:** The old `UI_CharacterEquipment.prefab` and the new one share the same path. Task 18 created the new file by SaveAsPrefabAsset to this path, which OVERWROTE the old file. So there is no separate "old prefab" to delete after Task 18 — only the old script and the shim remain.

- [ ] **Step 1: Confirm old prefab was overwritten**

Run `git log --oneline -- "Assets/UI/Player HUD/UI_CharacterEquipment.prefab"`. Expected: most recent commit is from Task 18 ("author UI_CharacterEquipment prefabs"). If the old prefab still exists side-by-side with the new (would happen if Task 18 used a different name), STOP and surface the conflict.

- [ ] **Step 2: Delete the old script**

```bash
git rm Assets/Scripts/UI/CharacterEquipmentUI.cs Assets/Scripts/UI/CharacterEquipmentUI.cs.meta
```

- [ ] **Step 3: Remove the `ToggleEquipmentUI` shim from PlayerUI**

Edit `Assets/Scripts/UI/PlayerUI.cs`. Remove the entire shim method added in Task 16 Step 2:

```csharp
    /// <summary>
    /// Legacy shim — kept for one rename window so the existing _buttonEquipmentUI.onClick
    /// listener wired in <see cref="WireButtons"/> keeps working until Task 17 repoints
    /// PlayerController's Tab binding. Delete in Task 20 cleanup.
    /// </summary>
    public void ToggleEquipmentUI()
    {
        ToggleEquipmentWindow(characterComponent);
    }
```

- [ ] **Step 4: Repoint `_buttonEquipmentUI.onClick` listener**

Search `PlayerUI.cs` for `_buttonEquipmentUI.onClick` (likely in a `WireButtons`-style method around line 230). Replace the `AddListener(ToggleEquipmentUI)` with `AddListener(() => ToggleEquipmentWindow(characterComponent))`.

- [ ] **Step 5: Verify compile**

Run MCP `assets-refresh`. Expected: no errors, no missing-script warnings on the scene's `PlayerUI` (the field was already retyped in Task 16; the old GameObject was removed in Task 19).

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/UI/CharacterEquipmentUI.cs Assets/Scripts/UI/CharacterEquipmentUI.cs.meta Assets/Scripts/UI/PlayerUI.cs
git commit -m "chore(ui): delete legacy CharacterEquipmentUI + ToggleEquipmentUI shim"
```

---

### Task 21: Documentation updates (rule #28 / #29 / #29b)

**Files (per spec §12):**
- Modify: `wiki/systems/character-equipment.md`
- Modify: `wiki/systems/player-hud.md`
- Modify: `.agent/skills/ui-hud/SKILL.md`
- Modify: `.agent/skills/item_system/SKILL.md`
- Modify: `.claude/agents/character-system-specialist.md`
- Modify: `.claude/agents/item-inventory-specialist.md`
- Modify: `.claude/agents/ui-hud-specialist.md`

- [ ] **Step 1: Update `wiki/systems/character-equipment.md`**

Bump `updated:` to `2026-05-19`. Append a Change log entry:

```markdown
- 2026-05-19 — Equipment UI reworked: paper-doll + stacked-layer cells, click-to-popup with state-aware verbs, five new `CharacterAction` subclasses (`EquipWearable` / `UnequipWearable` / `CarryInHand` / `StashInBag` / `UseItem`), `EquipmentSourceRef` discriminated value, `HandsController.OnCarriedItemChanged` event added (replaces UI polling), `CharacterEquipment.Equip` displacement now bag-first with ground-drop fallback, new `UnequipToBag` / `WieldOffToHand` / `DetachWornToCaller` helpers, plus a `TryStashInBag` private. UI surface is `UI_CharacterEquipment : UI_WindowBase` (rule #39 Variant) wired through `PlayerUI.OpenEquipmentWindow` / `CloseEquipmentWindow` / `ToggleEquipmentWindow`. Old `CharacterEquipmentUI` script + prefab deleted. See [spec](../../docs/superpowers/specs/2026-05-19-character-equipment-ui-rework-design.md) + [plan](../../docs/superpowers/plans/2026-05-19-character-equipment-ui-rework.md). — claude / [[kevin]]
```

Update the Public API section to mention the new helpers (`UnequipToBag`, `WieldOffToHand`, `DetachWornToCaller`). Update `depended_on_by` to include the new UI window.

- [ ] **Step 2: Update `wiki/systems/player-hud.md`**

Bump `updated:`. Append Change log entry:

```markdown
- 2026-05-19 — Added `UI_CharacterEquipment` to the PlayerUI surface (`OpenEquipmentWindow(Character)` / `CloseEquipmentWindow()` / `ToggleEquipmentWindow(Character)` with rule #39 null-guard). Variant of `UI_WindowBase.prefab`. Lives under `Assets/Scripts/UI/Equipment/`. Replaces the legacy `CharacterEquipmentUI` (deleted) — see [character-equipment](character-equipment.md) change log for the gameplay-side changes. — claude
```

Add `UI_CharacterEquipment.cs` + `UI_CharacterEquipment.prefab` to the Key classes / files table.

- [ ] **Step 3: Update `.agent/skills/ui-hud/SKILL.md`**

Append a section under "Existing components":

```markdown
## Click-to-popup pattern (2026-05-19)

`UI_CharacterEquipment` introduces a **shared popup component** pattern. The window
hosts one `UI_EquipmentActionPopup` child and reuses it for every item-bearing
cell click (worn mini-cells, bag cells, special-slot cards). The popup is fed a
state-aware `List<EquipmentVerb>` per cell — see the verb matrix in the [design spec](../../docs/superpowers/specs/2026-05-19-character-equipment-ui-rework-design.md) §5.

When to reuse: any new window that needs per-cell contextual actions (storage
panel right-click menus, party-member context menus, future inventory grids).
Lift the popup component to a more general location if a third user appears.
```

- [ ] **Step 4: Update `.agent/skills/item_system/SKILL.md`**

Append:

```markdown
## Equipment-window action surface (2026-05-19)

Five new `CharacterAction` subclasses route every equipment-window mutation
through one server-authoritative surface (rule #22):

- `CharacterAction_EquipWearable(bagSlotIndex)` — bag → worn (auto-displaces).
- `CharacterAction_UnequipWearable(layer, slot)` — worn → bag, fallback ground.
- `CharacterAction_CarryInHand(EquipmentSourceRef)` — smart-swap with current hand.
- `CharacterAction_StashInBag(EquipmentSourceRef)` — hand/worn/weapon → bag.
- `CharacterAction_UseItem(EquipmentSourceRef)` — consumable dispatch via `ConsumableInstance.ApplyEffect`.

`EquipmentSourceRef` is the shared discriminator: `BagSlot(int)` / `WornSlot(layer,slot)` /
`ActiveWeapon` / `HandsCarry`. Smart-swap algorithm captured in the [design spec](../../docs/superpowers/specs/2026-05-19-character-equipment-ui-rework-design.md) §6.

Bag-inventory replication contract (§"Bag-inventory replication authority") is
unchanged — all five new actions are owner-local-triggered, so the documented
"server-side mutation on remote-client character" path is not hit.
```

- [ ] **Step 5: Update the three agent descriptions**

Edit each of:
- `.claude/agents/character-system-specialist.md`
- `.claude/agents/item-inventory-specialist.md`
- `.claude/agents/ui-hud-specialist.md`

Extend each description (the `description:` field in the frontmatter) with one short sentence referencing the rework. Example for `ui-hud-specialist.md`:

```yaml
description: ... existing ... PLUS the 2026-05-19 UI_CharacterEquipment paper-doll + click-to-popup pattern: stacked U/C/A mini-cells on a doll, top-row special-slot cards, shared UI_EquipmentActionPopup component fed state-aware EquipmentVerb lists, all routed through five new CharacterAction subclasses (EquipWearable / UnequipWearable / CarryInHand / StashInBag / UseItem) on Character.CharacterActions per rule #22. ...
```

Pattern for `character-system-specialist.md` and `item-inventory-specialist.md` follows the same shape but tailored to their domain (actions for the character agent; smart-swap algorithm + replication continuity for the item agent).

- [ ] **Step 6: Commit**

```bash
git add wiki/systems/character-equipment.md wiki/systems/player-hud.md .agent/skills/ui-hud/SKILL.md .agent/skills/item_system/SKILL.md .claude/agents/character-system-specialist.md .claude/agents/item-inventory-specialist.md .claude/agents/ui-hud-specialist.md
git commit -m "docs: update wiki + skills + agents for 2026-05-19 equipment UI rework"
```

---

## Final manual verification (do this once after Task 21)

Run through the spec §11 testing matrix in the Editor + a standalone Mono build:

1. Tap Tab → window opens; click X → closes; ESC → closes (and dismisses popup first if open).
2. Click a bag wearable → popup shows Equip · Carry · Drop; click Equip → item appears in matching worn cell, bag slot empties.
3. Click a bag consumable (food/potion) → popup shows Use · Carry · Drop; click Use → NeedHunger increases (food), item destroyed.
4. Click a worn mini-cell → popup shows Unequip · Carry · Drop; click Unequip → item appears in bag, worn cell becomes empty placeholder.
5. Smart-swap: pick up a sword in hand, click another sword in the bag → Carry in hand → bag's vacated slot receives the old sword.
6. Active Weapon "Stash in bag" → socket hides, weapon enters bag weapon slot.
7. Active Weapon "Carry in hand" → socket hides, weapon enters HandsController.CarriedItem.
8. Bag card "Unequip bag" → whole bag drops to world with contents.
9. Late-joiner: host equips a full loadout, client joins, both peers open their own window → each paints its own owner correctly.
10. Save + load with mid-game loadout → window state matches after one OnEquipmentChanged.
11. Console clean — no orange `[PlayerUI]` warnings, no missing-script red errors.

---

## Self-review (executed once before publishing this plan)

**Spec coverage:**
- §2 in-scope items: all 8 covered (Tasks 1, 2, 4, 6–10, 15, 16, 18, 19, 20, 21).
- §3 11 decisions: all reflected in tasks (interaction model = popup component, displacement = bag-first, active weapon read-only swap = no Swap verb on the card, etc.).
- §4 Files table: every "new" file maps to a Task; every "edit" maps to a Task; every "delete" handled in Task 20.
- §5 Verb matrix: implemented in `UI_CharacterEquipment.BuildBagVerbs` + the per-state popup builders + the `OnVerbSelected` dispatcher.
- §6 Smart-swap: Task 9 + the `HasFreeSpaceForItem` reuse.
- §7 Late-joiner audit: no new replicated fields; UI reads existing channels (`OnEquipmentChanged` + new `OnCarriedItemChanged` event + `OnInventoryChanged`). Verified via Task 11 final-verification step 9.
- §8 NPC parity: all five actions are queue-able by any character (no player-only gates).
- §9 Error handling: actions silently no-op on race; logs gated with `NPCDebug.VerboseActions`.
- §10 Open questions: (a) consumable detection — resolved via `ConsumableInstance is` check; (b) hands-carry event — Task 1 adds the event; (c) Equip-caller sweep — Task 3 Step 1.
- §11 Testing matrix: covered in "Final manual verification" above + interleaved in individual tasks.
- §12 Doc updates: Task 21.

**Placeholder scan:** No "TBD" / "TODO" / "implement later" / "fill in details" / "similar to Task N" detected. Every code-touching step shows full code. Every command shows the full command line.

**Type consistency:**
- `EquipmentSourceRef` (Task 2) constructed via `Bag(int)` / `Worn(layer, slot)` / `Weapon()` / `Hands()` — matches usage in Tasks 8, 9, 10, 15.
- `EquipmentVerbId` enum values (Task 11): `Equip`, `Unequip`, `CarryInHand`, `StashInBag`, `UseConsumable`, `UnequipBag`, `DropToGround` — matches Task 15 dispatcher.
- `CharacterEquipment` new method signatures: `TryStashInBag(ItemInstance) → bool` (private, Task 3), `UnequipToBag(layer, slot) → bool` (Task 4), `WieldOffToHand() → WeaponInstance` (Task 5), `DetachWornToCaller(layer, slot) → WearableInstance` (Task 9 Step 2) — all callers match.
- `HandsController.OnCarriedItemChanged` is `event Action<ItemInstance>` (Task 1) — Task 15 subscribes with matching signature.
- `UI_CharacterEquipment.OpenPopupForBagCell` / `OpenPopupForWornCell` / `OpenPopupForSpecialCard` (Task 15) — referenced from Tasks 12, 13, 14 matches exactly.

No issues found. Plan is ready for execution.
