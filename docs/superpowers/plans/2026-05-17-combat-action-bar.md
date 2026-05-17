# Combat action bar — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the single-button `UI_CombatActionMenu` with a multi-cluster combat action bar (weapon · abilities · utility), add an Items sub-window (UI_WindowBase variant), and surface a player-only initiative bar + queued-action label — all backed by new `CharacterAction_Reload` / `CharacterAction_SwapWeapon` actions, a magazine-state network sync, and a hotkey block in `PlayerController`.

**Architecture:** Active-weapon-only verbs (Option B layout, Option A chrome from the brainstorm). Rule #39 — Items window is a Prefab Variant of `UI_WindowBase.prefab`; the bar + init bar + queued label are leaf HUD elements (no close button). Rule #22 — new CharacterActions are NPC-callable. Rule #33 — all hotkeys read in `PlayerController.Update()`. Rule #19b — ammo + reload state replicated via `NetworkVariable<int>` + `NetworkVariable<bool>` on `CharacterEquipment` (host-only state today, replicated for clients).

**Tech Stack:** Unity 6.x, C#, NGO (Netcode for GameObjects), Unity UI (uGUI + TMP), Unity Test Framework (EditMode where realistic). MCP tools available: `ai-game-developer__*` for Editor mutation, `ai-game-developer__console-get-logs` for compile checks, `ai-game-developer__assets-prefab-*` for prefab authoring, `ai-game-developer__gameobject-component-*` for scene wiring.

**Spec:** [docs/superpowers/specs/2026-05-17-combat-action-bar-design.md](../specs/2026-05-17-combat-action-bar-design.md)

---

## File map

**New files (scripts):**
- `Assets/Scripts/Character/CharacterActions/CharacterAction_Reload.cs`
- `Assets/Scripts/Character/CharacterActions/CharacterAction_SwapWeapon.cs`
- `Assets/Scripts/UI/Combat/UI_CombatAbilitySlot.cs`
- `Assets/Scripts/UI/Combat/UI_CombatInitiativeBar.cs`
- `Assets/Scripts/UI/Combat/UI_CombatQueuedLabel.cs`
- `Assets/Scripts/UI/Combat/UI_CombatItemsWindow.cs`
- `Assets/Scripts/UI/Combat/UI_CombatItemRow.cs`

**New files (Unity assets):**
- `Assets/UI/Player HUD/UI_CombatItemsWindow.prefab` (Variant of `UI_WindowBase.prefab`)
- `Assets/UI/Player HUD/UI_CombatItemRow.prefab` (leaf)
- `Assets/UI/Player HUD/Combat/UI_CombatAbilitySlot.prefab` (leaf)
- `Assets/UI/Player HUD/Combat/UI_CombatInitiativeBar.prefab` (leaf)
- `Assets/UI/Player HUD/Combat/UI_CombatQueuedLabel.prefab` (leaf)

**Modified files (scripts):**
- `Assets/Scripts/UI/UI_CombatActionMenu.cs` — rewrite as multi-cluster bar
- `Assets/Scripts/UI/PlayerUI.cs` — add `_combatItemsWindow` SerializeField + `OpenCombatItemsWindow` / `CloseCombatItemsWindow` / `IsCombatItemsWindowOpen` / `ToggleCombatItemsWindow`
- `Assets/Scripts/Character/CharacterCombat/CharacterCombat.cs` — add `OnInitiativeChanged` / `OnActionIntentCleared` events + `TryQueueReload` / `TryQueueSwapWeapon` / `TryQueueUseItem` helpers
- `Assets/Scripts/Character/CharacterAbilities/CharacterAbilities.cs` — add `TryUseSlot(int, Character)`
- `Assets/Scripts/Character/CharacterEquipment/CharacterEquipment.cs` — add `ActiveWeaponIndex` + `SwapToNextWeapon` + 2 NetworkVariables
- `Assets/Scripts/Character/CharacterControllers/PlayerController.cs` — add `HandleCombatHotkeys()` + preempt `HandleEKeyDown` + Space inverse branch
- `Assets/Scripts/Item/Equipment/MagazineWeaponInstance.cs` — add `CancelReload()`
- `Assets/Scripts/Item/Inventory.cs` (verify path) — add `GetConsumables()` + `GetWeaponInstances()`
- `Assets/Resources/Data/Item/ConsumableSO.cs` — add `IsUsableInCombat` flag
- `Assets/Resources/Data/Item/FoodSO.cs` — override `IsUsableInCombat => false`

**Modified files (Unity assets):**
- The scene that hosts `PlayerUI` — wire `_combatItemsWindow` field
- The scene that hosts `UI_PlayerHUD` — replace/edit the existing `UI_CombatActionMenu` prefab instance

**Documentation updates (final task):**
- `.agent/skills/combat_system/SKILL.md`
- `.agent/skills/ui-hud/SKILL.md`
- `wiki/systems/combat.md`
- `wiki/systems/character-combat.md`
- `wiki/systems/player-hud.md`
- `.claude/agents/combat-gameplay-architect.md`
- `.claude/agents/ui-hud-specialist.md`

---

## Task 1: Data-layer foundations (`IsUsableInCombat` + `CancelReload` + `Inventory` helpers)

Smallest, safest changes first. Three unrelated additions bundled in one commit because each is a 1-3-line edit with no behavioral risk.

**Files:**
- Modify: `Assets/Resources/Data/Item/ConsumableSO.cs`
- Modify: `Assets/Resources/Data/Item/FoodSO.cs`
- Modify: `Assets/Scripts/Item/Equipment/MagazineWeaponInstance.cs`
- Modify: `Assets/Scripts/Item/Inventory.cs` (verify the actual filename — could be `Inventory.cs`, `CharacterInventory.cs`, or live inside `CharacterEquipment`)

- [ ] **Step 1.1: Locate `Inventory` class**

Run:
```
Grep tool, pattern "class Inventory", glob "*.cs"
```
Expected: one file declaring `public class Inventory` or `public sealed class Inventory`. Note the path; use it in step 1.4.

If grep finds nothing standalone, the `Inventory` type may be nested inside `CharacterEquipment.cs` or live under a different name. Read `CharacterEquipment.cs:495-509` (the existing `UpdateWeaponVisualOnBag` filter) — whichever `GetInventory()` returns is where we add the helpers.

- [ ] **Step 1.2: Add `IsUsableInCombat` to `ConsumableSO`**

Read `Assets/Resources/Data/Item/ConsumableSO.cs` to confirm current shape (it should have `_destroyOnUse` + `effects` per spec §1).

Edit to add (insert below the existing `[SerializeField] private bool _destroyOnUse = true;` line):

```csharp
[SerializeField] private bool _isUsableInCombat = true;

public bool IsUsableInCombat => _isUsableInCombat;
```

The default `true` is intentional — Health Potions, Smoke Bombs, etc. are usable. `FoodSO` overrides (next step).

- [ ] **Step 1.3: Override `IsUsableInCombat` in `FoodSO`**

Read `Assets/Resources/Data/Item/FoodSO.cs`. If it extends `ConsumableSO` (or a subclass thereof), add inside the class body:

```csharp
public new bool IsUsableInCombat => false;
```

If `IsUsableInCombat` is virtual on the parent, use `override` instead of `new`. If `FoodSO` does NOT inherit from `ConsumableSO`, instead expose the same property on `FoodSO` directly returning `false` — call sites that check the food's combat-usability via `consumableInstance.Data is ConsumableSO so && so.IsUsableInCombat` will short-circuit on `is ConsumableSO` and food will read as "not usable in combat" automatically. **Verify the inheritance chain before editing** by reading the `class FoodSO : <parent>` header line.

- [ ] **Step 1.4: Add `CancelReload()` to `MagazineWeaponInstance`**

Edit `Assets/Scripts/Item/Equipment/MagazineWeaponInstance.cs` — append to the class body (after `FinishReload`):

```csharp
/// <summary>
/// Aborts an in-progress reload without changing the magazine state.
/// Called by CharacterAction_Reload.OnInterrupt when a reload is cancelled
/// (knockback, death, swap-during-reload). Leaves CurrentAmmo at its pre-reload value.
/// </summary>
public void CancelReload()
{
    _isReloading = false;
}
```

- [ ] **Step 1.5: Add `GetConsumables()` + `GetWeaponInstances()` helpers on `Inventory`**

Inside whichever class owns `ItemSlots` (per Step 1.1), add:

```csharp
using System.Collections.Generic;

/// <summary>
/// Filters ItemSlots for consumable instances. Used by combat Items sub-window
/// and any future "use consumable" gameplay flow.
/// </summary>
public IEnumerable<ConsumableInstance> GetConsumables()
{
    foreach (var slot in ItemSlots)
    {
        if (slot == null || slot.IsEmpty()) continue;
        if (slot.ItemInstance is ConsumableInstance ci) yield return ci;
    }
}

/// <summary>
/// Filters ItemSlots for weapon instances in the canonical inventory order.
/// Used by CharacterEquipment.SwapToNextWeapon for cycle order + the combat
/// action bar Swap button preview. Mirrors the inline filter at
/// CharacterEquipment.UpdateWeaponVisualOnBag (~line 500-509).
/// </summary>
public IReadOnlyList<WeaponInstance> GetWeaponInstances()
{
    var result = new List<WeaponInstance>();
    foreach (var slot in ItemSlots)
    {
        if (slot == null || slot.IsEmpty()) continue;
        if (slot is WeaponSlot && slot.ItemInstance is WeaponInstance wi)
            result.Add(wi);
    }
    return result;
}
```

If `ItemSlots` is named differently (e.g., `Slots`, `_itemSlots`), substitute. The existing inline filter at `CharacterEquipment.cs:500-509` is the source of truth for the correct property name + filter shape.

Optional follow-up (not required this step): refactor `CharacterEquipment.UpdateWeaponVisualOnBag` to use `GetWeaponInstances()` instead of duplicating the inline filter. Skip unless trivial — defer to a future cleanup.

- [ ] **Step 1.6: Compile check**

```
mcp__ai-game-developer__console-clear-logs
mcp__ai-game-developer__assets-refresh
mcp__ai-game-developer__console-get-logs
```
Expected: zero errors. If `WeaponInstance` / `ConsumableInstance` / `WeaponSlot` types fail to resolve, add the missing `using` directives at the top of `Inventory.cs` (or wherever the helpers landed).

- [ ] **Step 1.7: Commit**

```bash
git add Assets/Resources/Data/Item/ConsumableSO.cs Assets/Resources/Data/Item/FoodSO.cs Assets/Scripts/Item/Equipment/MagazineWeaponInstance.cs Assets/Scripts/Item/Inventory.cs
git commit -m "feat(combat-bar): data-layer foundations for combat action bar

- ConsumableSO.IsUsableInCombat flag (default true)
- FoodSO override → false
- MagazineWeaponInstance.CancelReload() for interrupted reloads
- Inventory.GetConsumables() + GetWeaponInstances() helpers"
```

Substitute the `Inventory.cs` path if the actual file is elsewhere.

---

## Task 2: `CharacterCombat` events + helper stubs

Add the two events (`OnInitiativeChanged`, `OnActionIntentCleared`) that drive the new UI elements + three stubbed helpers (`TryQueueReload`, `TryQueueSwapWeapon`, `TryQueueUseItem`) that the UI calls. Real action enqueue happens in Tasks 5-7; this task makes them compile-clean and event-emitting.

**Files:**
- Modify: `Assets/Scripts/Character/CharacterCombat/CharacterCombat.cs`

- [ ] **Step 2.1: Add the two events**

Open `CharacterCombat.cs`. Locate the existing events block (`OnCombatModeChanged`, `OnDamageTaken`, `OnBattleLeft`, `OnBattleJoined`, `OnActionIntentDecided`). Add inside the same block:

```csharp
/// <summary>
/// Fired whenever the per-character Initiative value changes (in tick percent 0..1).
/// Drives UI_CombatInitiativeBar repaint. Fire from UpdateInitiativeTick after the
/// stat mutation; do NOT fire on every frame regardless of change.
/// </summary>
public event Action<float> OnInitiativeChanged;

/// <summary>
/// Fired when the queued action intent is cleared (player cancelled, action consumed,
/// battle ended). Drives UI_CombatQueuedLabel.Hide.
/// </summary>
public event Action OnActionIntentCleared;
```

- [ ] **Step 2.2: Fire `OnInitiativeChanged` from `UpdateInitiativeTick`**

Locate `UpdateInitiativeTick` (per spec §1, around line 413). At the end of the method, after the stat is updated, add:

```csharp
// Surface initiative percent 0..1 to UI subscribers (rule #34 — only fires when value changes).
float pct = _character.CharacterStats != null && _character.CharacterStats.MaxInitiative > 0f
    ? Mathf.Clamp01(_character.CharacterStats.Initiative / _character.CharacterStats.MaxInitiative)
    : 0f;
OnInitiativeChanged?.Invoke(pct);
```

Adjust the divisor / property names to whatever `CharacterStats` actually exposes (could be `InitiativeMax`, `_initiative01`, etc. — verify before pasting). The pattern is: read current absolute + max, normalise to 0..1, invoke the event.

If `MaxInitiative` doesn't exist and Initiative is already stored as 0..1, just pass it through.

- [ ] **Step 2.3: Fire `OnActionIntentCleared` from `ClearActionIntent`**

Locate `ClearActionIntent` (per spec §1 reference, currently exists at the top of `CharacterCombat.cs`). After the existing body that nulls `PlannedAction` + `PlannedTarget`, add:

```csharp
OnActionIntentCleared?.Invoke();
```

Also fire from any other place that nulls `PlannedAction` (e.g., after an attack consumes — search the file for assignments `PlannedAction = null` and verify each one fires the event).

- [ ] **Step 2.4: Add the three helper methods (stubbed but functional)**

Append to the `CharacterCombat` class body (anywhere coherent — near the existing intent methods):

```csharp
/// <summary>
/// Queues a reload action if the active weapon is a magazine type with room to reload
/// and no reload already in progress. Player UI + R hotkey entry point. NPC AI can
/// call the same helper. Returns true if the action was queued.
/// </summary>
public bool TryQueueReload()
{
    // Resolve active weapon instance — verify accessor path on CharacterEquipment.
    var equipment = _character?.CharacterEquipment;
    if (equipment == null) return false;
    var weapons = equipment.GetInventory()?.GetWeaponInstances();
    if (weapons == null || weapons.Count == 0) return false;

    int activeIdx = equipment.ActiveWeaponIndex;
    if (activeIdx < 0 || activeIdx >= weapons.Count) return false;
    var active = weapons[activeIdx];

    if (active is not MagazineWeaponInstance mag) return false;
    if (mag.IsReloading) return false;
    if (mag.CurrentAmmo >= mag.MagazineSize) return false;

    // Action class implemented in Task 5 — direct instantiation here.
    _character.CharacterActions?.ExecuteAction(new CharacterAction_Reload(_character, mag));
    return true;
}

/// <summary>
/// Queues a weapon-swap action if the character carries 2+ weapons and no swap is
/// already in flight. Player UI + Y hotkey entry. NPC AI can call the same helper.
/// </summary>
public bool TryQueueSwapWeapon()
{
    var equipment = _character?.CharacterEquipment;
    if (equipment == null) return false;
    var weapons = equipment.GetInventory()?.GetWeaponInstances();
    if (weapons == null || weapons.Count < 2) return false;

    // Reject if a swap is already running (the action class enforces server-side too).
    if (_character.CharacterActions?.CurrentAction is CharacterAction_SwapWeapon) return false;

    _character.CharacterActions?.ExecuteAction(new CharacterAction_SwapWeapon(_character));
    return true;
}

/// <summary>
/// Queues a combat item use against a target (self for healing, planned target for
/// thrown). Routes through whatever existing CharacterAction handles consumable
/// use — verify in Task 7 whether CharacterUseConsumableAction is queue-ready or
/// fires immediately. For now, route through ExecuteAction; the action class can
/// observe Initiative if needed.
/// </summary>
public bool TryQueueUseItem(ConsumableInstance consumable, Character target)
{
    if (consumable == null) return false;
    if (consumable.Data is ConsumableSO so && !so.IsUsableInCombat) return false;

    // For throw items target must be non-null; for self-target items target should be _character.
    // The downstream action class decides what to do with the target reference.
    if (target == null) target = _character;

    _character.CharacterActions?.ExecuteAction(new CharacterUseConsumableAction(_character, consumable));
    return true;
}
```

**Note:** `TryQueueReload` references `CharacterAction_Reload` (Task 5) and `TryQueueSwapWeapon` references `CharacterAction_SwapWeapon` (Task 6). The file will not compile until those classes exist. **Stub them with empty class bodies temporarily** so this task compiles standalone — see Step 2.5.

- [ ] **Step 2.5: Add temporary forward-declaration stubs for the two new actions**

Create `Assets/Scripts/Character/CharacterActions/CharacterAction_Reload.cs` with:

```csharp
using UnityEngine;

/// <summary>STUB — real implementation in Task 5.</summary>
public sealed class CharacterAction_Reload : CharacterAction
{
    public CharacterAction_Reload(Character character, MagazineWeaponInstance mag) : base(character) { }
}
```

Create `Assets/Scripts/Character/CharacterActions/CharacterAction_SwapWeapon.cs` with:

```csharp
using UnityEngine;

/// <summary>STUB — real implementation in Task 6.</summary>
public sealed class CharacterAction_SwapWeapon : CharacterAction
{
    public CharacterAction_SwapWeapon(Character character) : base(character) { }
}
```

Adjust the base constructor signature to match `CharacterAction`'s actual API (might be `(Character, duration)` or `(duration)` with character set later). Read `Assets/Scripts/Character/CharacterActions/CharacterAction.cs` to confirm. The stubs exist solely to satisfy the compiler in this task.

- [ ] **Step 2.6: Compile check**

```
mcp__ai-game-developer__console-clear-logs
mcp__ai-game-developer__assets-refresh
mcp__ai-game-developer__console-get-logs
```
Expected: zero errors.

Likely failures + fixes:
- `_character.CharacterEquipment` undefined → confirm property name (could be `Equipment` or `CharEquipment`).
- `MaxInitiative` undefined → check `CharacterStats` for the actual property exposing the cap; if Initiative is already 0..1, drop the divisor.
- `ActiveWeaponIndex` undefined → it's added in Task 4. Temporarily stub on `CharacterEquipment` as `public int ActiveWeaponIndex => 0;` to unblock; Task 4 implements properly.

- [ ] **Step 2.7: Commit**

```bash
git add Assets/Scripts/Character/CharacterCombat/CharacterCombat.cs Assets/Scripts/Character/CharacterActions/CharacterAction_Reload.cs Assets/Scripts/Character/CharacterActions/CharacterAction_SwapWeapon.cs
git commit -m "feat(combat-bar): CharacterCombat events + queue helpers (skeletons)

- OnInitiativeChanged (drives init bar)
- OnActionIntentCleared (drives queued label hide)
- TryQueueReload / TryQueueSwapWeapon / TryQueueUseItem
- CharacterAction_Reload / CharacterAction_SwapWeapon stubs (Tasks 5-6 fill)"
```

---

## Task 3: `CharacterAbilities.TryUseSlot`

Hotkey 1-6 + UI ability slot click entry point.

**Files:**
- Modify: `Assets/Scripts/Character/CharacterAbilities/CharacterAbilities.cs`

- [ ] **Step 3.1: Read existing ability-trigger API**

Read `CharacterAbilities.cs` to find:
- Whether `_activeSlots[i]` is an `AbilityInstance` or something else (per spec §1 it's `AbilityInstance[]`).
- Whether `AbilityInstance` exposes `TryTrigger(Character target)` / `CanUse()` / similar.
- Whether there's an existing `UseSlot` / `Activate` method already (the spec assumed no; verify).

Note the canonical trigger signature. The wrapper below must call it.

- [ ] **Step 3.2: Add `TryUseSlot`**

Append to `CharacterAbilities` class body:

```csharp
/// <summary>
/// Hotkey 1-6 + UI ability slot click entry point. Validates slot index, slot non-null,
/// and delegates to AbilityInstance.TryTrigger. Returns true on success.
/// </summary>
public bool TryUseSlot(int slotIndex, Character target)
{
    if (slotIndex < 0 || slotIndex >= ACTIVE_SLOT_COUNT) return false;
    var ability = _activeSlots[slotIndex];
    if (ability == null) return false;
    return ability.TryTrigger(target);
}
```

If the actual ability trigger method is named differently (e.g., `Activate(target)`, `Use(target)`), substitute. The contract: returns `true` if the ability fired (or was queued for initiative).

- [ ] **Step 3.3: Compile + commit**

```
mcp__ai-game-developer__console-clear-logs
mcp__ai-game-developer__assets-refresh
mcp__ai-game-developer__console-get-logs
```
Expected: zero errors.

```bash
git add Assets/Scripts/Character/CharacterAbilities/CharacterAbilities.cs
git commit -m "feat(combat-bar): CharacterAbilities.TryUseSlot for hotkey 1-6"
```

---

## Task 4: `CharacterEquipment` — `ActiveWeaponIndex` + `SwapToNextWeapon` + NetworkVariables

The biggest single-class change. Adds the swap-cursor + the two NetworkVariables that replicate the active magazine weapon's per-shot state. Late-joiner correctness (rule #19b) hinges on this task.

**Files:**
- Modify: `Assets/Scripts/Character/CharacterEquipment/CharacterEquipment.cs`

- [ ] **Step 4.1: Read existing equip/unequip paths**

Read `CharacterEquipment.cs` end-to-end (it's the largest file in the diff). Note:
- Whether the class extends `NetworkBehaviour` (per spec §5, it should — verify).
- The current equip/unequip method names (likely `EquipWeapon(WeaponInstance)`, `UnequipWeapon()`, or `EquipItem(...)`).
- Whether there's already a current-active-weapon accessor (e.g., `CurrentWeapon`, `ActiveWeapon`).
- Where Attack consumes ammo today (likely lives in `CharacterCombat.Attack` calling into a weapon-instance method; search there too if needed).
- Whether existing equip changes fire a `NetworkVariable<>.OnValueChanged` callback or a custom event.

- [ ] **Step 4.2: Add `ActiveWeaponIndex` field + property + NetworkVariables**

At the top of the class (with other `NetworkVariable<>` fields if any exist), add:

```csharp
// Combat action bar — server-authoritative cursor into Inventory.GetWeaponInstances().
private readonly NetworkVariable<int> _activeWeaponIndexNet =
    new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

// Combat action bar — replicates the active MagazineWeaponInstance's per-shot state.
// _activeAmmoNet = -1 sentinel means active weapon is not a magazine (melee, charging, or none).
private readonly NetworkVariable<int> _activeAmmoNet =
    new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
private readonly NetworkVariable<bool> _isReloadingNet =
    new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

public int ActiveWeaponIndex => _activeWeaponIndexNet.Value;
public int ActiveAmmo => _activeAmmoNet.Value;
public bool IsActiveReloading => _isReloadingNet.Value;

public event Action<int> OnActiveAmmoChanged;
public event Action<bool> OnActiveReloadingChanged;
```

Add the matching event-subscription block inside `OnNetworkSpawn` (and unsubscribe in `OnNetworkDespawn`):

```csharp
public override void OnNetworkSpawn()
{
    base.OnNetworkSpawn();
    _activeAmmoNet.OnValueChanged += (_, n) => OnActiveAmmoChanged?.Invoke(n);
    _isReloadingNet.OnValueChanged += (_, n) => OnActiveReloadingChanged?.Invoke(n);

    // Immediate-fire on spawn so late-joiners paint the current state without waiting
    // for a server-side change.
    OnActiveAmmoChanged?.Invoke(_activeAmmoNet.Value);
    OnActiveReloadingChanged?.Invoke(_isReloadingNet.Value);
}

public override void OnNetworkDespawn()
{
    // Lambdas were anonymous — re-subscribe protection isn't critical here. If you
    // want strict unsubscription, store the handlers in fields and detach them here.
    base.OnNetworkDespawn();
}
```

- [ ] **Step 4.3: Add `RecomputeActiveWeaponSentinel()` server-side helper**

Append to the class body:

```csharp
/// <summary>
/// Re-evaluates which carried weapon is active and pushes ammo + reload state to
/// the network variables. Server-only. Call after any equip change, weapon swap,
/// or inventory mutation that could affect the active weapon's slot.
/// </summary>
private void RecomputeActiveWeaponSentinel()
{
    if (!IsServer) return;

    var weapons = GetInventory()?.GetWeaponInstances();
    if (weapons == null || weapons.Count == 0)
    {
        _activeWeaponIndexNet.Value = 0;
        _activeAmmoNet.Value = -1;
        _isReloadingNet.Value = false;
        return;
    }

    int idx = Mathf.Clamp(_activeWeaponIndexNet.Value, 0, weapons.Count - 1);
    if (idx != _activeWeaponIndexNet.Value) _activeWeaponIndexNet.Value = idx;

    var active = weapons[idx];
    if (active is MagazineWeaponInstance mag)
    {
        _activeAmmoNet.Value = mag.CurrentAmmo;
        _isReloadingNet.Value = mag.IsReloading;
    }
    else
    {
        _activeAmmoNet.Value = -1;
        _isReloadingNet.Value = false;
    }
}
```

- [ ] **Step 4.4: Add `SwapToNextWeapon()` server-only method**

Append to the class body:

```csharp
/// <summary>
/// Advances ActiveWeaponIndex to the next carried weapon and re-equips. Server-only.
/// Called by CharacterAction_SwapWeapon.OnComplete (Task 6). Do NOT invoke from
/// client input directly — route through the action so the swap respects initiative
/// pacing + anti-spam delay (per spec §2 / §4 data flow).
/// </summary>
public void SwapToNextWeapon()
{
    if (!IsServer) return;

    var weapons = GetInventory()?.GetWeaponInstances();
    if (weapons == null || weapons.Count < 2) return;

    int newIdx = (_activeWeaponIndexNet.Value + 1) % weapons.Count;

    // Re-use existing equip flow: unequip current, equip new.
    // Verify the actual method names — substitute if different.
    var oldActive = weapons[_activeWeaponIndexNet.Value];
    var newActive = weapons[newIdx];

    UnequipWeapon();              // existing method — pseudoname
    EquipWeapon(newActive);       // existing method — pseudoname

    _activeWeaponIndexNet.Value = newIdx;
    RecomputeActiveWeaponSentinel();
}
```

If the existing equip API expects different arguments (slot index, item instance, etc.), substitute. The principle is: reuse the existing flow rather than reimplementing equip.

- [ ] **Step 4.5: Hook ammo write paths**

Wherever Attack consumes ammo today (likely `CharacterCombat.Attack` → calls into the weapon instance's `ConsumeAmmo()` or equivalent on the server), append after the consume:

```csharp
// Mirror server-side state to clients via NetworkVariable.
_character.CharacterEquipment?.RecomputeActiveWeaponSentinel();
```

**This requires `RecomputeActiveWeaponSentinel` to be `internal` or `public`.** Change the visibility from `private` to `internal` (or `public` if cross-assembly).

If there's no existing centralised "ammo consume" hook in `CharacterCombat.Attack`, locate where `MagazineWeaponInstance.ConsumeAmmo` is currently called — if nowhere yet (i.e., consuming-ammo-on-attack isn't wired), this is the moment to wire it. Read the attack path in `CharacterCombat.cs` + `CombatStyleAttack.cs` to find the firing seam.

- [ ] **Step 4.6: Compile check**

```
mcp__ai-game-developer__console-clear-logs
mcp__ai-game-developer__assets-refresh
mcp__ai-game-developer__console-get-logs
```
Expected: zero errors.

Likely failures:
- `NetworkVariable<int>` requires `using Unity.Netcode;` — add at top of file if missing.
- `UnequipWeapon` / `EquipWeapon` method names don't match — substitute with actual names from Step 4.1.
- If `CharacterEquipment` does NOT already extend `NetworkBehaviour`, `OnNetworkSpawn` / `IsServer` won't resolve — **stop and escalate to Kevin**. The spec assumes it does (which is consistent with the existing `NetworkList<NetworkEquipmentSyncData>` field per the iter-1 review). Verify before proceeding.

- [ ] **Step 4.7: Commit**

```bash
git add Assets/Scripts/Character/CharacterEquipment/CharacterEquipment.cs Assets/Scripts/Character/CharacterCombat/CharacterCombat.cs
git commit -m "feat(combat-bar): CharacterEquipment swap cursor + ammo/reload NetworkVariables

- ActiveWeaponIndex (server-authoritative cursor into GetWeaponInstances)
- _activeAmmoNet + _isReloadingNet replicate active magazine state to all clients
- SwapToNextWeapon() rotates active equip (called by CharacterAction_SwapWeapon)
- RecomputeActiveWeaponSentinel() re-syncs after equip / swap / reload / consume
- OnActiveAmmoChanged / OnActiveReloadingChanged events drive UI repaint
- Ammo-consume on Attack now mirrors to network (was host-only state)"
```

---

## Task 5: `CharacterAction_Reload` (real implementation)

Replace the stub from Task 2 with the real continuous action.

**Files:**
- Modify: `Assets/Scripts/Character/CharacterActions/CharacterAction_Reload.cs`

- [ ] **Step 5.1: Read the base continuous-action class**

Per the system reminder context, there's a `CharacterAction_Continuous` base added 2026-05-06. Read it:
```
Glob: Assets/Scripts/Character/CharacterActions/CharacterAction_Continuous.cs
```
Note: lifecycle methods (`OnStart`, `OnTick`, `OnComplete`, `OnInterrupt`), how duration is set (constructor or property), and how `Finish()` / `Cancel()` are signalled.

Also read `CharacterAction.cs` (the base) to understand the inheritance shape.

- [ ] **Step 5.2: Replace the stub with the real implementation**

Rewrite `Assets/Scripts/Character/CharacterActions/CharacterAction_Reload.cs`:

```csharp
using UnityEngine;

/// <summary>
/// Continuous server-side action that reloads a magazine weapon over time.
/// Duration = MagazineRangedCombatStyleSO.ReloadTime (2s default).
/// Cancellable: OnInterrupt resets the IsReloading flag without changing ammo.
/// NPC-callable (rule #22) — same code path serves player UI + AI combat.
/// </summary>
public sealed class CharacterAction_Reload : CharacterAction_Continuous
{
    private readonly MagazineWeaponInstance _mag;
    private readonly float _reloadDuration;

    public CharacterAction_Reload(Character character, MagazineWeaponInstance mag)
        : base(character, duration: ResolveDuration(character, mag))
    {
        _mag = mag;
        _reloadDuration = ResolveDuration(character, mag);
    }

    private static float ResolveDuration(Character character, MagazineWeaponInstance mag)
    {
        // Prefer the combat style's authored ReloadTime; fall back to 2s if the style
        // doesn't expose one (defensive — shouldn't happen for a magazine weapon).
        var style = character?.CharacterCombat?.CurrentCombatStyleExpertise?.Style;
        if (style is MagazineRangedCombatStyleSO mag_style)
            return mag_style.ReloadTime;
        return 2f;
    }

    public override bool ShouldPlayGenericActionAnimation => false; // per combat skill — let weapon-specific anim play

    protected override void OnStart()
    {
        if (_mag == null) { Finish(); return; }
        if (_mag.IsReloading) { Finish(); return; }
        if (_mag.CurrentAmmo >= _mag.MagazineSize) { Finish(); return; }

        _mag.StartReload();
        // Mirror server state to clients (rule #19b).
        Character.CharacterEquipment?.RecomputeActiveWeaponSentinel();
    }

    protected override void OnTick(float deltaTime)
    {
        // No per-tick logic — duration alone drives completion. Subclass-required override.
    }

    protected override void OnComplete()
    {
        if (_mag == null) return;
        _mag.FinishReload();
        Character.CharacterEquipment?.RecomputeActiveWeaponSentinel();
    }

    protected override void OnInterrupt()
    {
        if (_mag == null) return;
        _mag.CancelReload();
        Character.CharacterEquipment?.RecomputeActiveWeaponSentinel();
    }
}
```

Adjust:
- The constructor base call to match `CharacterAction_Continuous`'s actual signature.
- The override method names (`OnStart` / `OnTick` / `OnComplete` / `OnInterrupt`) to match the base contract.
- `ShouldPlayGenericActionAnimation` — only keep if the base exposes this property (per the combat skill §3 NOTE).

- [ ] **Step 5.3: Compile check + commit**

```
mcp__ai-game-developer__console-clear-logs
mcp__ai-game-developer__assets-refresh
mcp__ai-game-developer__console-get-logs
```
Expected: zero errors.

```bash
git add Assets/Scripts/Character/CharacterActions/CharacterAction_Reload.cs
git commit -m "feat(combat-bar): CharacterAction_Reload — continuous action with interrupt handling

- Duration = MagazineRangedCombatStyleSO.ReloadTime
- StartReload on OnStart, FinishReload on OnComplete, CancelReload on OnInterrupt
- Re-syncs CharacterEquipment NetworkVariables after each state change
- NPC-callable (rule #22 parity)"
```

---

## Task 6: `CharacterAction_SwapWeapon` (real implementation)

Replace the Task 2 stub with the real ~0.5s swap action.

**Files:**
- Modify: `Assets/Scripts/Character/CharacterActions/CharacterAction_SwapWeapon.cs`

- [ ] **Step 6.1: Decide swap duration**

Spec §4 says "~0.5s anti-spam". Hardcode as a constant on the action class — designer can promote to an SO field later if tuning becomes a concern. **No SO-driven duration this task** (YAGNI).

- [ ] **Step 6.2: Write the action class**

Rewrite `Assets/Scripts/Character/CharacterActions/CharacterAction_SwapWeapon.cs`:

```csharp
using UnityEngine;

/// <summary>
/// Continuous server-side action that swaps the character to the next carried weapon.
/// Hardcoded 0.5s duration represents the stow-and-draw window (anti-spam + visual cue).
/// NPC-callable (rule #22).
/// </summary>
public sealed class CharacterAction_SwapWeapon : CharacterAction_Continuous
{
    private const float SWAP_DURATION = 0.5f;

    public CharacterAction_SwapWeapon(Character character)
        : base(character, duration: SWAP_DURATION) { }

    public override bool ShouldPlayGenericActionAnimation => false;

    protected override void OnStart()
    {
        // No-op — visible "stowing" effect happens via the character animator
        // (future polish). For now duration alone gates the actual equip flip.
    }

    protected override void OnTick(float deltaTime) { /* no per-tick logic */ }

    protected override void OnComplete()
    {
        Character.CharacterEquipment?.SwapToNextWeapon();
    }

    protected override void OnInterrupt()
    {
        // Interrupted swap = no-op. Player still wields original weapon; can retry.
    }
}
```

Adjust the base constructor + override signatures to match Task 5's reading of `CharacterAction_Continuous`.

- [ ] **Step 6.3: Compile check + commit**

```
mcp__ai-game-developer__console-clear-logs
mcp__ai-game-developer__assets-refresh
mcp__ai-game-developer__console-get-logs
```
Expected: zero errors.

```bash
git add Assets/Scripts/Character/CharacterActions/CharacterAction_SwapWeapon.cs
git commit -m "feat(combat-bar): CharacterAction_SwapWeapon — 0.5s continuous swap action

- OnComplete calls CharacterEquipment.SwapToNextWeapon (server-only)
- OnInterrupt is no-op (no partial swap state to clean up)
- NPC-callable (rule #22 parity)"
```

---

## Task 7: `UI_CombatItemRow` leaf prefab + script

Smallest UI element. Author + script first so the Items window in Task 9 has rows to instantiate.

**Files:**
- Create: `Assets/Scripts/UI/Combat/UI_CombatItemRow.cs`
- Create: `Assets/UI/Player HUD/UI_CombatItemRow.prefab`

- [ ] **Step 7.1: Write the row script**

Create `Assets/Scripts/UI/Combat/UI_CombatItemRow.cs`:

```csharp
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MWI.UI.Combat
{
    /// <summary>
    /// Leaf row inside UI_CombatItemsWindow. One ConsumableInstance per row.
    /// Disabled when the consumable is not usable in combat (e.g., food).
    /// </summary>
    public class UI_CombatItemRow : MonoBehaviour
    {
        [Header("Wiring")]
        [SerializeField] private Image _icon;
        [SerializeField] private TMP_Text _nameText;
        [SerializeField] private TMP_Text _qtyText;
        [SerializeField] private TMP_Text _effectText;
        [SerializeField] private TMP_Text _hotkeyText;
        [SerializeField] private Button _rowButton;
        [SerializeField] private CanvasGroup _canvasGroup;

        private ConsumableInstance _instance;
        private Character _customer;
        private Action<ConsumableInstance> _onUseClicked;

        public void Initialize(
            ConsumableInstance instance,
            Character customer,
            int hotkeyNumber,
            Action<ConsumableInstance> onUseClicked)
        {
            _instance = instance;
            _customer = customer;
            _onUseClicked = onUseClicked;

            if (_icon != null && instance?.Data?.Icon != null) _icon.sprite = instance.Data.Icon;
            if (_nameText != null) _nameText.text = instance?.Data?.ItemName ?? "(null)";
            if (_qtyText != null) _qtyText.text = $"×{instance?.StackCount ?? 0}";

            bool usable = instance?.Data is ConsumableSO so && so.IsUsableInCombat;
            if (_effectText != null)
            {
                _effectText.text = usable
                    ? FormatEffectLine(instance.Data as ConsumableSO)
                    : "<color=#a55>Not usable in combat.</color>";
            }
            if (_hotkeyText != null) _hotkeyText.text = usable && hotkeyNumber > 0 ? hotkeyNumber.ToString() : "—";
            if (_canvasGroup != null) { _canvasGroup.alpha = usable ? 1f : 0.4f; _canvasGroup.interactable = usable; }

            if (_rowButton != null)
            {
                _rowButton.onClick.RemoveAllListeners();
                if (usable) _rowButton.onClick.AddListener(OnRowClicked);
            }
        }

        private static string FormatEffectLine(ConsumableSO so)
        {
            if (so == null || so.Effects == null || so.Effects.Count == 0) return "";
            // ConsumableSO.Effects today is List<string> (stringly-typed effect names) per spec §1.
            // Render the first line; richer formatting waits on effect-system rework.
            return so.Effects[0];
        }

        private void OnRowClicked() { _onUseClicked?.Invoke(_instance); }

        private void OnDestroy()
        {
            if (_rowButton != null) _rowButton.onClick.RemoveAllListeners();
        }
    }
}
```

If `ItemSO` doesn't have an `Icon` property, substitute with the actual sprite accessor. If `StackCount` is named differently on `ItemInstance` (e.g., `Quantity`), substitute.

- [ ] **Step 7.2: Compile check**

```
mcp__ai-game-developer__console-clear-logs
mcp__ai-game-developer__assets-refresh
mcp__ai-game-developer__console-get-logs
```
Expected: zero errors.

- [ ] **Step 7.3: Author the prefab via MCP**

Per `.agent/skills/ui-hud/SKILL.md`, leaf prefabs are NOT UI_WindowBase variants — just plain MonoBehaviour prefabs. Author via MCP:

```
mcp__ai-game-developer__gameobject-create with name "UI_CombatItemRow" (in the active scene root, temporarily)
mcp__ai-game-developer__gameobject-component-add with componentName "RectTransform"
mcp__ai-game-developer__gameobject-component-add with componentName "MWI.UI.Combat.UI_CombatItemRow"
```

Build the row visual tree (under the root):
- `Icon` — Image GameObject, 28×28 anchor, references the `_icon` field
- `Meta` — Vertical Layout Group with two TMP_Text children: `Name` (with inline qty) + `Effect`
- `Hotkey` — TMP_Text with a small dark background frame
- `Button` — full-row Button covering all of the above

Wire the `[SerializeField]` references on the row script:
```
mcp__ai-game-developer__gameobject-component-modify (using pathPatches) to set _icon / _nameText / _qtyText / _effectText / _hotkeyText / _rowButton / _canvasGroup
```

Save as prefab:
```
mcp__ai-game-developer__assets-prefab-create with sourcePrefabAssetPath null + gameObjectRef <the scene GameObject> + path "Assets/UI/Player HUD/UI_CombatItemRow.prefab"
```

Delete the temporary scene GameObject:
```
mcp__ai-game-developer__gameobject-destroy
```

**Visual polish (placeholder OK for v1):** plain dark background `rgba(42,42,53,1)`, 6 px padding, 24 px row height. Final styling is a separate authoring pass per rule #39 ("Visual styling is a separate authoring pass").

- [ ] **Step 7.4: Commit**

```bash
git add Assets/Scripts/UI/Combat/UI_CombatItemRow.cs "Assets/UI/Player HUD/UI_CombatItemRow.prefab" "Assets/UI/Player HUD/UI_CombatItemRow.prefab.meta"
git commit -m "feat(combat-bar): UI_CombatItemRow leaf prefab + script"
```

---

## Task 8: `UI_CombatItemsWindow` (UI_WindowBase variant + script + PlayerUI wiring)

The first UI_WindowBase variant in this feature. Mirrors the UI_SafePanel pattern from the May-16 spec.

**Files:**
- Create: `Assets/Scripts/UI/Combat/UI_CombatItemsWindow.cs`
- Create: `Assets/UI/Player HUD/UI_CombatItemsWindow.prefab` (Variant of `UI_WindowBase.prefab`)
- Modify: `Assets/Scripts/UI/PlayerUI.cs`

- [ ] **Step 8.1: Read UI_SafePanel as the precedent**

Read `Assets/Scripts/UI/Furniture/UI_SafePanel.cs` + `Assets/UI/Player HUD/UI_SafePanel.prefab` (via `mcp__ai-game-developer__gameobject-find` after opening the prefab). Note:
- The `UI_WindowBase` base call pattern in `Awake` / `OpenWindow` / `CloseWindow`.
- The row-pool / row-list pattern.
- The auto-close hooks (1 Hz poll, OnDisable, target despawn).

Also re-read `.agent/skills/ui-hud/SKILL.md` for the canonical MCP authoring recipe.

- [ ] **Step 8.2: Write the window script**

Create `Assets/Scripts/UI/Combat/UI_CombatItemsWindow.cs`:

```csharp
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MWI.UI.Combat
{
    /// <summary>
    /// UI_WindowBase variant. Lists usable consumables. Anchored above-right of the
    /// Items button in the combat action bar. Auto-closes on: use, combat end, ESC,
    /// second-click toggle, OnDisable. Hotkeys 1-9 (window-scoped) fire row N.
    /// </summary>
    public class UI_CombatItemsWindow : UI_WindowBase
    {
        [Header("Wiring")]
        [SerializeField] private RectTransform _rowContainer;
        [SerializeField] private UI_CombatItemRow _rowPrefab;
        [SerializeField] private TMP_Text _headerCountText;

        private Character _customer;
        private readonly List<UI_CombatItemRow> _rows = new List<UI_CombatItemRow>();
        private readonly List<ConsumableInstance> _hotkeyOrder = new List<ConsumableInstance>();

        public bool IsOpen => gameObject.activeSelf;

        public void Initialize(Character customer)
        {
            _customer = customer;
            ClearRows();

            if (_customer == null) return;

            // Subscribe to combat-end auto-close.
            if (_customer.CharacterCombat != null)
            {
                _customer.CharacterCombat.OnBattleLeft -= CloseWindow;
                _customer.CharacterCombat.OnBattleLeft += CloseWindow;
            }

            BuildRows();
        }

        private void BuildRows()
        {
            if (_customer == null) return;
            var inventory = _customer.CharacterEquipment?.GetInventory();
            if (inventory == null) return;

            int hotkeyIdx = 1;
            int enabledCount = 0;
            foreach (var consumable in inventory.GetConsumables())
            {
                var row = Instantiate(_rowPrefab, _rowContainer);
                bool usable = consumable.Data is ConsumableSO so && so.IsUsableInCombat;
                int assignedKey = (usable && hotkeyIdx <= 9) ? hotkeyIdx : 0;
                row.Initialize(consumable, _customer, assignedKey, OnRowUsed);
                _rows.Add(row);

                if (usable)
                {
                    enabledCount++;
                    if (hotkeyIdx <= 9)
                    {
                        // Track hotkey-to-row mapping for keyboard fire.
                        while (_hotkeyOrder.Count < hotkeyIdx) _hotkeyOrder.Add(null);
                        _hotkeyOrder[hotkeyIdx - 1] = consumable;
                        hotkeyIdx++;
                    }
                }
            }

            if (_headerCountText != null) _headerCountText.text = $"{enabledCount} available";
        }

        private void ClearRows()
        {
            foreach (var row in _rows) if (row != null) Destroy(row.gameObject);
            _rows.Clear();
            _hotkeyOrder.Clear();
        }

        private void OnRowUsed(ConsumableInstance instance)
        {
            if (_customer == null || instance == null) { CloseWindow(); return; }

            // For self-target consumables, target = customer. For thrown items, use PlannedTarget.
            Character target = _customer.CharacterCombat?.PlannedTarget ?? _customer;
            _customer.CharacterCombat?.TryQueueUseItem(instance, target);
            CloseWindow();
        }

        private void Update()
        {
            if (!IsOpen) return;

            // Window-scoped hotkeys 1-9 for row use.
            for (int i = 0; i < _hotkeyOrder.Count && i < 9; i++)
            {
                var key = (KeyCode)(KeyCode.Alpha1 + i);
                if (Input.GetKeyDown(key) && _hotkeyOrder[i] != null)
                {
                    OnRowUsed(_hotkeyOrder[i]);
                    return;
                }
            }

            // ESC closes the window. ESC handling is intentionally local — PlayerController
            // does not own this binding while the window is open.
            if (Input.GetKeyDown(KeyCode.Escape)) CloseWindow();
        }

        public override void CloseWindow()
        {
            if (_customer?.CharacterCombat != null)
            {
                _customer.CharacterCombat.OnBattleLeft -= CloseWindow;
            }
            _customer = null;
            ClearRows();
            base.CloseWindow();
        }

        private void OnDisable()
        {
            // Defensive cleanup if SetActive(false) is called externally.
            if (_customer?.CharacterCombat != null)
            {
                _customer.CharacterCombat.OnBattleLeft -= CloseWindow;
            }
        }
    }
}
```

- [ ] **Step 8.3: Wire `OpenCombatItemsWindow` on `PlayerUI`**

Edit `Assets/Scripts/UI/PlayerUI.cs`. In the `SerializeField` block (near other window refs):

```csharp
[SerializeField] private UI_CombatItemsWindow _combatItemsWindow;
```

Add public methods (mirror `OpenSafePanel` pattern):

```csharp
public bool IsCombatItemsWindowOpen => _combatItemsWindow != null && _combatItemsWindow.IsOpen;

public void OpenCombatItemsWindow(Character customer)
{
    if (_combatItemsWindow == null)
    {
        Debug.LogWarning("<color=orange>[PlayerUI]</color> OpenCombatItemsWindow called but _combatItemsWindow SerializeField is null — author the prefab (variant of UI_WindowBase.prefab) and wire it to PlayerUI._combatItemsWindow in the Inspector.");
        return;
    }
    _combatItemsWindow.Initialize(customer);
    _combatItemsWindow.OpenWindow();
}

public void CloseCombatItemsWindow()
{
    if (_combatItemsWindow == null) return;
    _combatItemsWindow.CloseWindow();
}

public void ToggleCombatItemsWindow(Character customer)
{
    if (IsCombatItemsWindowOpen) CloseCombatItemsWindow();
    else OpenCombatItemsWindow(customer);
}
```

- [ ] **Step 8.4: Author the window prefab (Variant of UI_WindowBase.prefab)**

Per rule #39 + `.agent/skills/ui-hud/SKILL.md` MCP authoring recipe:

```
mcp__ai-game-developer__assets-find with searchFilter "t:Prefab UI_WindowBase" → resolve UI_WindowBase.prefab path

mcp__ai-game-developer__assets-prefab-instantiate with prefabRef <UI_WindowBase.prefab> → instantiates in scene as base

mcp__ai-game-developer__gameobject-modify (rename root) to "UI_CombatItemsWindow"

mcp__ai-game-developer__gameobject-component-add with componentName "MWI.UI.Combat.UI_CombatItemsWindow"
```

Inside the inherited Canvas child, add:
- `Header` panel (with a TMP_Text for the title "Use Item" + count + the inherited `_buttonClose`)
- `ScrollView` with `Viewport` + `Content` (Content has `ContentSizeFitter` vertical-fit per rule #39 + a `VerticalLayoutGroup`)

Set `Content` as the `_rowContainer` SerializeField on the window script. Assign the row prefab from Task 7 to `_rowPrefab`.

Set Canvas `sortingOrder` to 60 (per spec §6 — above the action bar at 50).

Verify variant relationship via:
```
mcp__ai-game-developer__assets-get-data with path "Assets/UI/Player HUD/UI_CombatItemsWindow.prefab"
```
Expected: `PrefabUtility.GetCorrespondingObjectFromSource` resolves to `UI_WindowBase.prefab`.

Save:
```
mcp__ai-game-developer__assets-prefab-create with sourcePrefabAssetPath <the base UI_WindowBase> + gameObjectRef + path "Assets/UI/Player HUD/UI_CombatItemsWindow.prefab" + connectGameObjectToPrefab true
```

Use the prefab variant creation flow specifically (see SKILL.md if unsure).

Destroy the scene instance after saving.

- [ ] **Step 8.5: Wire the SerializeField in the scene that hosts PlayerUI**

```
mcp__ai-game-developer__scene-list-opened → find the main play scene
mcp__ai-game-developer__gameobject-find with name "PlayerUI" or wherever PlayerUI script lives
```

Drag the new window prefab into the `_combatItemsWindow` SerializeField. Via MCP:
```
mcp__ai-game-developer__assets-prefab-instantiate with prefabRef <UI_CombatItemsWindow.prefab> as a child of UI_PlayerHUD
mcp__ai-game-developer__gameobject-modify on the new instance to deactivate (SetActive false)
mcp__ai-game-developer__gameobject-component-modify on PlayerUI to set _combatItemsWindow → reference the scene instance
mcp__ai-game-developer__scene-save
```

The wiring step requires `SerializedObject.ApplyModifiedPropertiesWithoutUndo + EditorSceneManager.SaveScene` per rule #39 — see SKILL.md for the Roslyn snippet that does the wiring properly without play-mode volatility.

- [ ] **Step 8.6: Compile check + commit**

```
mcp__ai-game-developer__console-clear-logs
mcp__ai-game-developer__assets-refresh
mcp__ai-game-developer__console-get-logs
```
Expected: zero errors.

```bash
git add Assets/Scripts/UI/Combat/UI_CombatItemsWindow.cs Assets/Scripts/UI/PlayerUI.cs "Assets/UI/Player HUD/UI_CombatItemsWindow.prefab" "Assets/UI/Player HUD/UI_CombatItemsWindow.prefab.meta" <scene-file>
git commit -m "feat(combat-bar): UI_CombatItemsWindow — UI_WindowBase variant + PlayerUI surface

- Prefab variant per rule #39 (anchored above-right of Items button)
- Scrolling row list of consumables; filters via ConsumableSO.IsUsableInCombat
- Window-scoped hotkeys 1-9 + ESC close
- Auto-closes on combat end (OnBattleLeft) + OnDisable
- PlayerUI.OpenCombatItemsWindow / CloseCombatItemsWindow / IsCombatItemsWindowOpen / ToggleCombatItemsWindow
- Scene wiring of _combatItemsWindow SerializeField"
```

---

## Task 9: `UI_CombatAbilitySlot` leaf prefab + script

Per-slot icon for the 6 active abilities.

**Files:**
- Create: `Assets/Scripts/UI/Combat/UI_CombatAbilitySlot.cs`
- Create: `Assets/UI/Player HUD/Combat/UI_CombatAbilitySlot.prefab`

- [ ] **Step 9.1: Write the slot script**

Create `Assets/Scripts/UI/Combat/UI_CombatAbilitySlot.cs`:

```csharp
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MWI.UI.Combat
{
    /// <summary>
    /// Single ability slot inside the action bar. Renders icon, cooldown overlay,
    /// resource readout, hotkey label, empty/hatched placeholder when slot is null.
    /// </summary>
    public class UI_CombatAbilitySlot : MonoBehaviour
    {
        [Header("Wiring")]
        [SerializeField] private Image _icon;
        [SerializeField] private Image _cooldownOverlay;
        [SerializeField] private TMP_Text _hotkeyText;
        [SerializeField] private GameObject _emptyPlaceholder;
        [SerializeField] private Button _clickButton;

        private Character _character;
        private int _slotIndex;

        public void Initialize(int slotIndex, Character character)
        {
            _slotIndex = slotIndex;
            _character = character;

            if (_hotkeyText != null) _hotkeyText.text = (slotIndex + 1).ToString();
            if (_clickButton != null)
            {
                _clickButton.onClick.RemoveAllListeners();
                _clickButton.onClick.AddListener(OnSlotClicked);
            }

            Refresh();
        }

        private void Update()
        {
            // Cheap visual refresh — cooldown overlay needs per-frame updates. Slot
            // identity changes via OnActiveSlotChanged on CharacterAbilities; subscribe
            // to that event for a strictly event-driven path in a future polish pass.
            Refresh();
        }

        private void Refresh()
        {
            if (_character?.CharacterAbilities == null) { ShowEmpty(); return; }

            // CharacterAbilities exposes _activeSlots[] but not necessarily a public getter.
            // Add one in Task 3-adjacent OR substitute here with the correct accessor.
            var ability = _character.CharacterAbilities.GetActiveSlot(_slotIndex);
            if (ability == null) { ShowEmpty(); return; }

            if (_emptyPlaceholder != null) _emptyPlaceholder.SetActive(false);
            if (_icon != null) { _icon.enabled = true; if (ability.Data?.Icon != null) _icon.sprite = ability.Data.Icon; }

            if (_cooldownOverlay != null)
            {
                float cdPct01 = ability.GetCooldownPct01();   // 1.0 = full cooldown, 0.0 = ready
                _cooldownOverlay.enabled = cdPct01 > 0f;
                _cooldownOverlay.fillAmount = cdPct01;
            }
        }

        private void ShowEmpty()
        {
            if (_icon != null) _icon.enabled = false;
            if (_cooldownOverlay != null) _cooldownOverlay.enabled = false;
            if (_emptyPlaceholder != null) _emptyPlaceholder.SetActive(true);
        }

        private void OnSlotClicked()
        {
            if (_character?.CharacterAbilities == null) return;
            var target = _character.CharacterCombat?.PlannedTarget;
            _character.CharacterAbilities.TryUseSlot(_slotIndex, target);
        }

        private void OnDestroy()
        {
            if (_clickButton != null) _clickButton.onClick.RemoveAllListeners();
        }
    }
}
```

If `CharacterAbilities` doesn't expose `GetActiveSlot(int)`, add it as a small public method (returns `_activeSlots[i]`). Same for `AbilityInstance.GetCooldownPct01` — if absent, derive from `(_cooldownRemaining / _cooldownTotal)`. **Task 3's `TryUseSlot` is the canonical "use" entry; this slot script only adds READ accessors if missing.**

- [ ] **Step 9.2: Author the prefab**

Follow the same MCP recipe as Task 7.3, but for the ability-slot visual (24×24 icon, hotkey label bottom-right corner, hatched empty placeholder GameObject toggleable, full-cover click button).

Save to `Assets/UI/Player HUD/Combat/UI_CombatAbilitySlot.prefab`.

- [ ] **Step 9.3: Compile check + commit**

```
mcp__ai-game-developer__console-clear-logs
mcp__ai-game-developer__assets-refresh
mcp__ai-game-developer__console-get-logs
```
Expected: zero errors. If `GetActiveSlot` / `GetCooldownPct01` accessors are missing, add them and re-compile.

```bash
git add Assets/Scripts/UI/Combat/UI_CombatAbilitySlot.cs Assets/Scripts/Character/CharacterAbilities/CharacterAbilities.cs "Assets/UI/Player HUD/Combat/UI_CombatAbilitySlot.prefab" "Assets/UI/Player HUD/Combat/UI_CombatAbilitySlot.prefab.meta"
git commit -m "feat(combat-bar): UI_CombatAbilitySlot leaf — icon + cooldown + hotkey + empty state"
```

---

## Task 10: `UI_CombatInitiativeBar` leaf prefab + script

Player-only initiative bar above the action bar.

**Files:**
- Create: `Assets/Scripts/UI/Combat/UI_CombatInitiativeBar.cs`
- Create: `Assets/UI/Player HUD/Combat/UI_CombatInitiativeBar.prefab`

- [ ] **Step 10.1: Write the bar script**

Create `Assets/Scripts/UI/Combat/UI_CombatInitiativeBar.cs`:

```csharp
using UnityEngine;
using UnityEngine.UI;

namespace MWI.UI.Combat
{
    /// <summary>
    /// 200x6 px initiative bar. Subscribes to CharacterCombat.OnInitiativeChanged.
    /// Hidden via parent container when not in battle.
    /// </summary>
    public class UI_CombatInitiativeBar : MonoBehaviour
    {
        [Header("Wiring")]
        [SerializeField] private Image _fill;

        private Character _character;

        public void Initialize(Character character)
        {
            Unsubscribe();
            _character = character;
            if (_character?.CharacterCombat != null)
            {
                _character.CharacterCombat.OnInitiativeChanged += HandleInitiative;
            }
            HandleInitiative(0f);
        }

        private void HandleInitiative(float pct01)
        {
            if (_fill != null) _fill.fillAmount = Mathf.Clamp01(pct01);
        }

        private void Unsubscribe()
        {
            if (_character?.CharacterCombat != null)
            {
                _character.CharacterCombat.OnInitiativeChanged -= HandleInitiative;
            }
        }

        private void OnDestroy() { Unsubscribe(); }
    }
}
```

- [ ] **Step 10.2: Author the prefab**

200×6 RectTransform, dark background frame, fill Image set to `Image.Type.Filled` with `Horizontal` fill method and a gradient sprite or color (orange→yellow per the mockup).

Save to `Assets/UI/Player HUD/Combat/UI_CombatInitiativeBar.prefab`.

- [ ] **Step 10.3: Compile check + commit**

```
mcp__ai-game-developer__console-clear-logs
mcp__ai-game-developer__assets-refresh
mcp__ai-game-developer__console-get-logs
```
Expected: zero errors.

```bash
git add Assets/Scripts/UI/Combat/UI_CombatInitiativeBar.cs "Assets/UI/Player HUD/Combat/UI_CombatInitiativeBar.prefab" "Assets/UI/Player HUD/Combat/UI_CombatInitiativeBar.prefab.meta"
git commit -m "feat(combat-bar): UI_CombatInitiativeBar leaf — player initiative fill"
```

---

## Task 11: `UI_CombatQueuedLabel` leaf prefab + script

The "▶ Queued: …" pill above the initiative bar.

**Files:**
- Create: `Assets/Scripts/UI/Combat/UI_CombatQueuedLabel.cs`
- Create: `Assets/UI/Player HUD/Combat/UI_CombatQueuedLabel.prefab`

- [ ] **Step 11.1: Write the label script**

Create `Assets/Scripts/UI/Combat/UI_CombatQueuedLabel.cs`:

```csharp
using System;
using TMPro;
using UnityEngine;

namespace MWI.UI.Combat
{
    /// <summary>
    /// Floating pill showing "▶ Queued: &lt;icon&gt; &lt;action name&gt; → &lt;target name&gt;".
    /// Shown on OnActionIntentDecided; hidden on OnActionIntentCleared.
    /// </summary>
    public class UI_CombatQueuedLabel : MonoBehaviour
    {
        [Header("Wiring")]
        [SerializeField] private TMP_Text _label;
        [SerializeField] private GameObject _visualRoot;

        private Character _character;

        public void Initialize(Character character)
        {
            Unsubscribe();
            _character = character;
            if (_character?.CharacterCombat != null)
            {
                _character.CharacterCombat.OnActionIntentDecided += HandleIntentDecided;
                _character.CharacterCombat.OnActionIntentCleared += HandleIntentCleared;
            }
            Hide();
        }

        private void HandleIntentDecided(Character target, Func<bool> action)
        {
            if (_visualRoot != null) _visualRoot.SetActive(true);
            if (_label != null)
            {
                // ResolveActionName is intentionally simple — refine when ability/item
                // visuals + names need richer dispatch. For v1: best-effort heuristic.
                string actionName = ResolveActionName(action);
                string targetName = target != null ? target.CharacterName : "—";
                _label.text = $"▶ Queued: {actionName} → {targetName}";
            }
        }

        private void HandleIntentCleared() { Hide(); }

        private void Hide()
        {
            if (_visualRoot != null) _visualRoot.SetActive(false);
        }

        private string ResolveActionName(Func<bool> action)
        {
            // The PlannedAction closure doesn't carry semantic identity. For v1 we
            // emit a generic "Action" label. Future polish: enrich SetActionIntent
            // with an enum / SO reference to render the actual icon + name.
            return "Action";
        }

        private void Unsubscribe()
        {
            if (_character?.CharacterCombat != null)
            {
                _character.CharacterCombat.OnActionIntentDecided -= HandleIntentDecided;
                _character.CharacterCombat.OnActionIntentCleared -= HandleIntentCleared;
            }
        }

        private void OnDestroy() { Unsubscribe(); }
    }
}
```

**Note:** richer label content (action icon + name) is deferred — the `PlannedAction` closure doesn't carry semantic identity today. A future polish task adds an `ActionDescriptor` parameter to `SetActionIntent`. For v1, "▶ Queued: Action → \<target>" is acceptable and matches spec §7 contract loosely (the "Action" placeholder is the only deviation; flag in the commit message).

- [ ] **Step 11.2: Author the prefab**

Rounded background `rgba(26,58,107,0.95)`, border `#3a78c8`, glow box-shadow (Outline component), TMP_Text inside.

Save to `Assets/UI/Player HUD/Combat/UI_CombatQueuedLabel.prefab`.

- [ ] **Step 11.3: Compile check + commit**

```
mcp__ai-game-developer__console-clear-logs
mcp__ai-game-developer__assets-refresh
mcp__ai-game-developer__console-get-logs
```
Expected: zero errors.

```bash
git add Assets/Scripts/UI/Combat/UI_CombatQueuedLabel.cs "Assets/UI/Player HUD/Combat/UI_CombatQueuedLabel.prefab" "Assets/UI/Player HUD/Combat/UI_CombatQueuedLabel.prefab.meta"
git commit -m "feat(combat-bar): UI_CombatQueuedLabel leaf — pill above init bar

- Subscribes to OnActionIntentDecided (show) + OnActionIntentCleared (hide)
- v1 label is generic 'Action' — richer descriptor deferred (PlannedAction
  closure lacks semantic identity today)"
```

---

## Task 12: `UI_CombatActionMenu` rewrite (the bar itself)

Largest single-file change in the UI layer. Restructures the bar as 3 clusters with conditional weapon-state rendering.

**Files:**
- Modify: `Assets/Scripts/UI/UI_CombatActionMenu.cs`
- Modify: the existing `UI_CombatActionMenu` prefab + scene instance

- [ ] **Step 12.1: Rewrite the script**

Replace the entire `Assets/Scripts/UI/UI_CombatActionMenu.cs`:

```csharp
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using MWI.UI.Combat;

/// <summary>
/// Combat action bar: three clusters (weapon · abilities · utility). Shown when
/// CharacterCombat.IsInBattle. Hidden otherwise. The weapon cluster mutates based
/// on the active WeaponInstance shape (Melee / Charging / Magazine).
///
/// Leaf children (UI_CombatAbilitySlot ×6, UI_CombatInitiativeBar, UI_CombatQueuedLabel)
/// are authored as children of _menuContainer in the prefab.
/// </summary>
public class UI_CombatActionMenu : MonoBehaviour
{
    [Header("Container")]
    [SerializeField] private GameObject _menuContainer;

    [Header("Weapon Cluster")]
    [SerializeField] private Button _attackButton;
    [SerializeField] private TextMeshProUGUI _attackButtonText;
    [SerializeField] private GameObject _ammoBadgeRoot;
    [SerializeField] private TextMeshProUGUI _ammoBadgeText;
    [SerializeField] private GameObject _reloadButtonRoot;
    [SerializeField] private Button _reloadButton;

    [Header("Abilities Cluster")]
    [SerializeField] private UI_CombatAbilitySlot[] _abilitySlots; // length 6

    [Header("Utility Cluster")]
    [SerializeField] private Button _swapButton;
    [SerializeField] private TextMeshProUGUI _swapFromText;
    [SerializeField] private TextMeshProUGUI _swapToText;
    [SerializeField] private CanvasGroup _swapCanvasGroup;
    [SerializeField] private Button _itemsButton;

    [Header("Chrome")]
    [SerializeField] private UI_CombatInitiativeBar _initiativeBar;
    [SerializeField] private UI_CombatQueuedLabel _queuedLabel;

    private Character _character;

    public void Initialize(Character character)
    {
        Unsubscribe();
        _character = character;

        if (_character?.CharacterCombat != null)
            _character.CharacterCombat.OnCombatModeChanged += HandleCombatModeChanged;

        WireButtons();
        InitializeSubElements();
        UpdateMenuVisibility();
    }

    private void WireButtons()
    {
        if (_attackButton != null) { _attackButton.onClick.RemoveAllListeners(); _attackButton.onClick.AddListener(OnAttackClicked); }
        if (_reloadButton != null) { _reloadButton.onClick.RemoveAllListeners(); _reloadButton.onClick.AddListener(OnReloadClicked); }
        if (_swapButton != null) { _swapButton.onClick.RemoveAllListeners(); _swapButton.onClick.AddListener(OnSwapClicked); }
        if (_itemsButton != null) { _itemsButton.onClick.RemoveAllListeners(); _itemsButton.onClick.AddListener(OnItemsClicked); }
    }

    private void InitializeSubElements()
    {
        if (_abilitySlots != null)
            for (int i = 0; i < _abilitySlots.Length; i++)
                if (_abilitySlots[i] != null) _abilitySlots[i].Initialize(i, _character);

        if (_initiativeBar != null) _initiativeBar.Initialize(_character);
        if (_queuedLabel != null) _queuedLabel.Initialize(_character);
    }

    private void Update()
    {
        if (_character == null) return;
        UpdateMenuVisibility();
        if (_menuContainer != null && _menuContainer.activeSelf) UpdateVisuals();
    }

    private void HandleCombatModeChanged(bool isInCombat) { UpdateMenuVisibility(); }

    private void UpdateMenuVisibility()
    {
        if (_menuContainer == null) return;
        bool shouldShow = _character?.CharacterCombat?.IsInBattle == true;
        if (_menuContainer.activeSelf != shouldShow) _menuContainer.SetActive(shouldShow);
    }

    private void UpdateVisuals()
    {
        // Resolve active weapon shape via CharacterEquipment NetworkVariables (Task 4).
        var equipment = _character.CharacterEquipment;
        var weapons = equipment?.GetInventory()?.GetWeaponInstances();
        int carriedCount = weapons?.Count ?? 0;

        int activeIdx = equipment != null ? equipment.ActiveWeaponIndex : 0;
        WeaponInstance active = (weapons != null && activeIdx >= 0 && activeIdx < weapons.Count) ? weapons[activeIdx] : null;

        bool isMag = active is MagazineWeaponInstance;
        bool isRanged = active is RangedWeaponInstance;

        // Attack label + ammo badge
        if (_attackButtonText != null)
        {
            string label = isRanged ? "Ranged Attack" : "Melee Attack";
            bool queued = _character.CharacterCombat?.HasPlannedAction == true;
            _attackButtonText.text = queued ? $"<color=#9bf>{label} [Queued]</color>" : label;
        }
        if (_ammoBadgeRoot != null) _ammoBadgeRoot.SetActive(isMag);
        if (_ammoBadgeText != null && isMag)
            _ammoBadgeText.text = $"{equipment.ActiveAmmo}/{((MagazineWeaponInstance)active).MagazineSize}";

        // Attack enable: disabled when magazine empty & not reloading-yet (rely on auto-queue if pressed)
        if (_attackButton != null)
        {
            bool canFire = !isMag || (equipment.ActiveAmmo > 0 && !equipment.IsActiveReloading);
            _attackButton.interactable = canFire;
        }

        // Reload slot — visible only for magazine weapons; greyed when full and not reloading
        if (_reloadButtonRoot != null) _reloadButtonRoot.SetActive(isMag);
        if (_reloadButton != null && isMag)
        {
            var mag = (MagazineWeaponInstance)active;
            bool needsReload = equipment.ActiveAmmo < mag.MagazineSize && !equipment.IsActiveReloading;
            _reloadButton.interactable = needsReload;
        }

        // Swap button + preview
        if (_swapButton != null && _swapCanvasGroup != null)
        {
            bool canSwap = carriedCount >= 2;
            _swapButton.interactable = canSwap;
            _swapCanvasGroup.alpha = canSwap ? 1f : 0.4f;

            if (_swapFromText != null) _swapFromText.text = WeaponIconGlyph(active);
            if (_swapToText != null)
            {
                WeaponInstance next = canSwap ? weapons[(activeIdx + 1) % carriedCount] : null;
                _swapToText.text = next != null ? WeaponIconGlyph(next) : "—";
            }
        }
    }

    private static string WeaponIconGlyph(WeaponInstance w)
    {
        if (w == null) return "—";
        if (w is RangedWeaponInstance) return "🏹";
        return "⚔";
    }

    private void OnAttackClicked()
    {
        if (_character?.CharacterCombat == null) return;

        var combat = _character.CharacterCombat;
        var equipment = _character.CharacterEquipment;
        var weapons = equipment?.GetInventory()?.GetWeaponInstances();
        var active = (weapons != null && equipment.ActiveWeaponIndex >= 0 && equipment.ActiveWeaponIndex < weapons.Count) ? weapons[equipment.ActiveWeaponIndex] : null;

        // Empty-magazine auto-queue Reload (spec decision #4)
        if (active is MagazineWeaponInstance mag && mag.CurrentAmmo == 0 && !mag.IsReloading)
        {
            combat.TryQueueReload();
            return;
        }

        // Toggle behavior preserved from prior implementation: cancel an existing queued action.
        if (combat.HasPlannedAction) { combat.ClearActionIntent(); return; }

        // Resolve target same way the legacy UI_CombatActionMenu did.
        Character initialTarget = combat.PlannedTarget;
        var bm = combat.CurrentBattleManager;
        if (initialTarget != null && bm != null && (bm.GetTeamOf(initialTarget) == null || !initialTarget.IsAlive()))
            initialTarget = null;
        initialTarget ??= bm?.GetBestTargetFor(_character);
        if (initialTarget == null) return;

        combat.SetActionIntent(() => combat.Attack(combat.PlannedTarget), initialTarget);
    }

    private void OnReloadClicked() { _character?.CharacterCombat?.TryQueueReload(); }
    private void OnSwapClicked() { _character?.CharacterCombat?.TryQueueSwapWeapon(); }
    private void OnItemsClicked()
    {
        if (_character == null) return;
        PlayerUI.Instance?.ToggleCombatItemsWindow(_character);
    }

    private void Unsubscribe()
    {
        if (_character?.CharacterCombat != null)
            _character.CharacterCombat.OnCombatModeChanged -= HandleCombatModeChanged;
    }

    private void OnDestroy() { Unsubscribe(); }
}
```

- [ ] **Step 12.2: Update the prefab tree**

Open the existing `UI_CombatActionMenu` prefab via MCP. The current prefab has just `_menuContainer` with one button.

Rebuild the structure (inside `_menuContainer`):
- `Cluster_Weapon` (HorizontalLayoutGroup):
  - `Btn_Attack` (Button + TMP_Text label inside)
  - `AmmoBadge` (TMP_Text on a small chip background — initially inactive)
  - `Btn_Reload` (Button + TMP_Text — initially inactive)
- `Sep_1` (1px vertical line)
- `Cluster_Abilities` (HorizontalLayoutGroup):
  - 6 instances of the `UI_CombatAbilitySlot.prefab` (from Task 9) as children
- `Sep_2`
- `Cluster_Utility` (HorizontalLayoutGroup):
  - `Btn_Swap` (Button + 3 TMP_Texts: from / arrow / to)
  - `Btn_Items` (Button + TMP_Text)
- Above the bar: instance of `UI_CombatInitiativeBar.prefab` (anchored center-top, 4px offset)
- Above the init bar: instance of `UI_CombatQueuedLabel.prefab` (anchored center-top, 3px offset)

Wire every SerializeField on the rewritten `UI_CombatActionMenu` script (via `mcp__ai-game-developer__gameobject-component-modify` with pathPatches).

- [ ] **Step 12.3: Verify the scene instance still works**

```
mcp__ai-game-developer__scene-list-opened
```
Find the scene that hosts the existing `UI_CombatActionMenu` (likely the same scene as `PlayerUI`). The prefab override should propagate the new structure automatically. Run a play-mode smoke test:
```
mcp__ai-game-developer__editor-application-set-state with playmode=true
```
Walk through a melee battle, confirm the bar appears, click Attack, watch the queued label, etc. Stop playmode:
```
mcp__ai-game-developer__editor-application-set-state with playmode=false
```

- [ ] **Step 12.4: Compile check + commit**

```
mcp__ai-game-developer__console-clear-logs
mcp__ai-game-developer__assets-refresh
mcp__ai-game-developer__console-get-logs
```
Expected: zero errors.

```bash
git add Assets/Scripts/UI/UI_CombatActionMenu.cs "Assets/UI/Player HUD/UI_CombatActionMenu.prefab" "Assets/UI/Player HUD/UI_CombatActionMenu.prefab.meta" <scene-file>
git commit -m "feat(combat-bar): rewrite UI_CombatActionMenu as 3-cluster bar

- Weapon cluster: Attack (label + ammo badge) + Reload (magazine-only)
- Abilities cluster: 6 UI_CombatAbilitySlot instances
- Utility cluster: Swap (with from→to preview) + Items
- Chrome: UI_CombatInitiativeBar + UI_CombatQueuedLabel as children of _menuContainer
- Empty-magazine click auto-queues Reload (spec decision #4)
- Swap button greyed when carriedCount < 2"
```

---

## Task 13: `PlayerController` hotkey routing

The final code change. Adds the in-battle hotkey block + preempts the existing E dispatcher.

**Files:**
- Modify: `Assets/Scripts/Character/CharacterControllers/PlayerController.cs`

- [ ] **Step 13.1: Add `HandleCombatHotkeys()` method**

Append inside the `PlayerController` class (near `HandleEKeyDown`):

```csharp
/// <summary>
/// In-battle hotkey block: Space (attack), R (reload), Y (swap), 1-6 (abilities).
/// Owner + IsInBattle gated. E (items toggle) is handled inside HandleEKeyDown
/// because it needs to preempt the existing 5-priority E dispatcher.
/// </summary>
private void HandleCombatHotkeys()
{
    if (_character?.CharacterCombat?.IsInBattle != true) return;

    // Suppress 1-6 ability hotkeys when the items window owns numeric input.
    bool itemsWindowOpen = PlayerUI.Instance != null && PlayerUI.Instance.IsCombatItemsWindowOpen;

    // Space → in-battle attack queue (paired with existing out-of-battle Space at line 295).
    if (Input.GetKeyDown(KeyCode.Space))
    {
        var combat = _character.CharacterCombat;
        if (combat.HasPlannedAction) { combat.ClearActionIntent(); return; }
        var target = combat.PlannedTarget ?? combat.CurrentBattleManager?.GetBestTargetFor(_character);
        if (target != null) combat.SetActionIntent(() => combat.Attack(combat.PlannedTarget), target);
        return;
    }

    if (Input.GetKeyDown(KeyCode.R)) { _character.CharacterCombat.TryQueueReload(); return; }
    if (Input.GetKeyDown(KeyCode.Y)) { _character.CharacterCombat.TryQueueSwapWeapon(); return; }

    if (!itemsWindowOpen)
    {
        for (int i = 0; i < 6; i++)
        {
            if (Input.GetKeyDown((KeyCode)(KeyCode.Alpha1 + i)))
            {
                _character.CharacterAbilities?.TryUseSlot(i, _character.CharacterCombat.PlannedTarget);
                return;
            }
        }
    }
}
```

Call `HandleCombatHotkeys()` from `PlayerController.Update()` — locate the existing `if (IsOwner) { ... Input ... }` block and insert the call inside it (anywhere reasonable; before the existing battle-target dispatch is fine).

- [ ] **Step 13.2: Preempt the existing E dispatcher**

Edit `HandleEKeyDown` (line ~335). At the very top of the method (before any of the 5 priorities):

```csharp
// PRIORITY 0 (new): in-battle E toggles the combat items window. Short-circuits
// the entire 5-priority chain — combat consumable use goes through the window,
// not the field-eat path.
if (_character?.CharacterCombat?.IsInBattle == true)
{
    PlayerUI.Instance?.ToggleCombatItemsWindow(_character);
    _eMenuOpened = true;
    return;
}
```

The remaining priorities (placement-active, interactable intent, consumable, hold-menu) remain unchanged for out-of-battle E presses.

- [ ] **Step 13.3: Verify Space coexistence**

Locate `PlayerController.cs:295` (`if (!_character.CharacterCombat.IsInBattle && Input.GetKeyDown(KeyCode.Space))`). This line stays unchanged — it handles **out-of-battle** Space (direct `Attack(null)`). The new in-battle Space binding lives in `HandleCombatHotkeys()` (Step 13.1). Both are mutually exclusive via the `IsInBattle` gate.

- [ ] **Step 13.4: Compile check**

```
mcp__ai-game-developer__console-clear-logs
mcp__ai-game-developer__assets-refresh
mcp__ai-game-developer__console-get-logs
```
Expected: zero errors.

- [ ] **Step 13.5: Commit**

```bash
git add Assets/Scripts/Character/CharacterControllers/PlayerController.cs
git commit -m "feat(combat-bar): PlayerController in-battle hotkey block

- HandleCombatHotkeys(): Space (attack queue), R (reload), Y (swap), 1-6 (abilities)
- HandleEKeyDown preempts 5-priority dispatcher with Priority 0 'in-battle → toggle
  items window' (rule #33: input ownership stays in PlayerController)
- Out-of-battle Space binding at line 295 preserved unchanged
- 1-6 ability hotkeys suppressed when CombatItemsWindow is open
  (window owns 1-9 row-select binding)"
```

---

## Task 14: Manual playtest matrix (spec §14)

No code changes. Walk through the 18-row testing matrix from spec §14 manually. Each row produces either ✅ or a bug to fix.

- [ ] **Step 14.1: Single-player smoke test**

Enter playmode with a fresh character. Run through these rows from spec §14:
- Sword equipped → click Attack → existing flow + queued label appears ✅
- Bow equipped → click Attack → existing queue + fire flow (the **charge-progress sub-bar inside the Attack button is intentionally NOT implemented in v1** — see Open follow-ups; verify nothing crashes when the bow charges)
- Pistol equipped → fire 3 shots → ammo readout 6→5→4→3
- Pistol empty → click Attack → toast + auto-queue Reload
- Click Reload while sword equipped → no-op (button shouldn't be visible)
- Swap with 1 weapon → button greyed
- Swap with 2 weapons → 0.5s swap, cluster re-renders
- Open Items window → world stays visible
- Click Health Potion (self-target) → window closes + queued label
- Click Food row → disabled
- Combat ends with window open → window auto-closes
- ESC inside window → closes
- Press 1-6 with window open → row N used; ability NOT triggered

For each row: write outcome in a scratch file. Any FAIL → log a follow-up issue, fix inline if trivial.

- [ ] **Step 14.2: Multiplayer smoke test (host + client)**

Per rule #19b — late-joiner repro is mandatory:
- Host fires 2 rounds from pistol
- Join client
- Client looks at host → host's ammo state ?? (Option A scope: only host's own UI shows ammo; remote view doesn't surface ammo readout — acceptable but verify nothing crashes)
- Client fires their own pistol → their ammo readout updates
- Host swaps weapons → host sees swap; client sees host's visual weapon flip (verify equip replication works; flag if not — that's the §12 blocking-for-planning item)

Any visible desync = fix before claiming done.

- [ ] **Step 14.3: Save/load**

Save game with pistol at 3/6 ammo. Reload save. Confirm pistol still at 3/6 via existing `WeaponInstance` serialization (no new work — just verify).

- [ ] **Step 14.4: Commit findings**

If any tests revealed bugs that were fixed inline:
```bash
git add <files>
git commit -m "fix(combat-bar): playtest matrix issues — <short list>"
```

If everything green:
```bash
git commit --allow-empty -m "chore(combat-bar): playtest matrix passed (manual)"
```

---

## Task 15: Documentation updates (spec §15)

**Files (each gets a targeted edit):**
- Modify: `.agent/skills/combat_system/SKILL.md`
- Modify: `.agent/skills/ui-hud/SKILL.md`
- Modify: `wiki/systems/combat.md`
- Modify: `wiki/systems/character-combat.md`
- Modify: `wiki/systems/player-hud.md`
- Modify: `.claude/agents/combat-gameplay-architect.md`
- Modify: `.claude/agents/ui-hud-specialist.md`

- [ ] **Step 15.1: Read wiki/CLAUDE.md before touching wiki/**

Per the project rules, `wiki/CLAUDE.md` governs everything under `wiki/`. Re-read it briefly to confirm frontmatter rules, change log format, and the >5-file diff-preview rule (this task touches ~7 files; preview the diff before bulk-edit).

- [ ] **Step 15.2: Update `.agent/skills/combat_system/SKILL.md`**

Append a section:

```markdown
## CharacterAction_Reload + CharacterAction_SwapWeapon (2026-05-17)

Two new continuous CharacterActions, both NPC-callable (rule #22):

- `CharacterAction_Reload` — duration = `MagazineRangedCombatStyleSO.ReloadTime`.
  OnStart → `mag.StartReload()`, OnComplete → `mag.FinishReload()`,
  OnInterrupt → `mag.CancelReload()`. Queue via
  `_character.CharacterCombat.TryQueueReload()` (validates magazine type,
  not-already-reloading, ammo < max).
- `CharacterAction_SwapWeapon` — hardcoded 0.5s duration.
  OnComplete → `CharacterEquipment.SwapToNextWeapon()`. Queue via
  `_character.CharacterCombat.TryQueueSwapWeapon()` (validates carried >= 2).

Replication channel: `NetworkVariable<int> _activeAmmoNet` + `NetworkVariable<bool>
_isReloadingNet` on `CharacterEquipment`. `-1` ammo sentinel = active weapon is
not a magazine. Server writes via `CharacterEquipment.RecomputeActiveWeaponSentinel()`
after every equip change, attack ammo-consume, reload state flip, or swap.
Late-joiner correctness via `OnNetworkSpawn` immediate-fire of OnValueChanged.

Hotkey map (PlayerController per rule #33): Space (attack), R (reload), Y (swap),
1-6 (abilities), E (toggle items window). E preempts the 5-priority dispatcher
when `IsInBattle == true`.
```

- [ ] **Step 15.3: Update `.agent/skills/ui-hud/SKILL.md`**

Append a worked example of authoring `UI_CombatItemsWindow` as a `UI_WindowBase` variant + the leaf-inside-HUD pattern for init bar / queued label / ability slots (children of `UI_CombatActionMenu._menuContainer`, not separate windows). Reference the new prefabs by path.

- [ ] **Step 15.4: Update `wiki/systems/combat.md`**

Bump `updated:` frontmatter to `2026-05-17`. Append to `## Change log`:

```
- 2026-05-17 — Combat action bar: multi-cluster UI (weapon · abilities · utility),
  Items sub-window (UI_WindowBase variant), player initiative bar + queued label,
  new CharacterAction_Reload / CharacterAction_SwapWeapon, ammo + reload
  NetworkVariables on CharacterEquipment. — claude
```

Add a subsection under `## Public API / entry points`:

```
**Player action bar (combat HUD)**
- `UI_CombatActionMenu` (rewritten) — multi-cluster bar shown when IsInBattle.
- `UI_CombatItemsWindow` — UI_WindowBase variant, anchored above-right of Items button.
- `PlayerUI.OpenCombatItemsWindow(Character)` / `ToggleCombatItemsWindow(Character)`.
- Hotkeys: Space / R / Y / 1-6 / E (PlayerController per rule #33).
```

Refresh `depended_on_by` to include the new UI scripts.

- [ ] **Step 15.5: Update `wiki/systems/character-combat.md`**

Bump `updated:`. Append to change log. Document the new events (`OnInitiativeChanged`, `OnActionIntentCleared`) + helper methods (`TryQueueReload`, `TryQueueSwapWeapon`, `TryQueueUseItem`).

- [ ] **Step 15.6: Update `wiki/systems/player-hud.md`**

Bump `updated:`. Append to change log entry referencing the new combat HUD elements. Add `[[combat-action-bar]]` or equivalent to `depended_on_by` (or just note in change log if no separate system page is being added).

- [ ] **Step 15.7: Update `.claude/agents/combat-gameplay-architect.md`**

Extend the agent's description block to mention `CharacterAction_Reload`, `CharacterAction_SwapWeapon`, and the `_activeAmmoNet` / `_isReloadingNet` replication channel.

- [ ] **Step 15.8: Update `.claude/agents/ui-hud-specialist.md`**

Extend the description to mention `UI_CombatItemsWindow` as a canonical UI_WindowBase variant example + the leaf-inside-HUD pattern (init bar / queued label / ability slots as `_menuContainer` children).

- [ ] **Step 15.9: Diff preview + commit**

Wiki rule: for >5 file edits, preview the diff first.
```
git diff --stat
```
Confirm the file list matches expectations (7 files in this case). Then:
```bash
git add .agent/skills/combat_system/SKILL.md .agent/skills/ui-hud/SKILL.md wiki/systems/combat.md wiki/systems/character-combat.md wiki/systems/player-hud.md .claude/agents/combat-gameplay-architect.md .claude/agents/ui-hud-specialist.md
git commit -m "docs(combat-bar): wiki + SKILL + agent updates for new combat HUD"
```

---

## Task 16: Self-review checklist

Before opening a PR / declaring done, re-check the spec section by section.

- [ ] **Step 16.1: Spec coverage walk-through**

Open the spec. For each numbered section, point at the task that implements it:
- §2 Concrete API additions → Tasks 1-6 each cover one or more rows
- §3 Decisions captured → all enforced by Tasks 12-13 (bar + hotkeys)
- §4 Files table → Tasks 7-12 ship every new file; Tasks 1-6 + 13 ship every modification
- §4.1 Carried weapons data model → Task 4 (SwapToNextWeapon + cycle via Inventory.GetWeaponInstances)
- §5 Network sync → Task 4 (the two NetworkVariables + RecomputeActiveWeaponSentinel)
- §6 UI_CombatItemsWindow per rule #39 → Task 8
- §7 Initiative + queued label → Tasks 10 + 11
- §8 Hotkey map + dispatcher coexistence → Task 13
- §9 Late-joiner audit → enforced by Task 4 + verified in Task 14.2
- §10 NPC parity → Tasks 5 + 6 (no owner gate; documented in Task 15.2)
- §11 Error handling → Task 12 (UI side) + Task 5/6 (action interrupt paths)
- §12 Blocking-for-planning → both items deferred for planning (equip-change replication audit + CharacterUseConsumableAction shape) — flag in PR if either issue surfaces during implementation
- §13 Out-of-band follow-up → tracked separately (post-PR)
- §14 Testing matrix → Task 14
- §15 Documentation → Task 15

Any spec requirement without a task = ADD a task and patch the plan.

- [ ] **Step 16.2: Cross-task type consistency check**

Grep the plan for the canonical names; confirm no rename drift:
- `TryUseSlot` (not `UseSlot`)
- `TryQueueReload` / `TryQueueSwapWeapon` / `TryQueueUseItem`
- `OnInitiativeChanged` / `OnActionIntentCleared`
- `CancelReload`
- `ActiveWeaponIndex` / `_activeAmmoNet` / `_isReloadingNet`
- `RecomputeActiveWeaponSentinel`
- `SwapToNextWeapon`
- `OpenCombatItemsWindow` / `CloseCombatItemsWindow` / `ToggleCombatItemsWindow` / `IsCombatItemsWindowOpen`

Any drift = fix the plan + add a note in the PR description.

- [ ] **Step 16.3: Final commit + offer PR**

If the plan needed self-review fixes:
```bash
git add docs/superpowers/plans/2026-05-17-combat-action-bar.md
git commit -m "docs(plan): self-review fixes for combat-action-bar plan"
```

Otherwise no commit needed.

---

## Open follow-ups (post-implementation)

- **Ranged-weapon melee-damage cleanup** (user directive 2026-05-17). Remove melee attack damage from `WeaponSO` / `RangedWeaponInstance`. Update `wiki/systems/combat.md`, `wiki/systems/items.md`, `.agent/skills/combat_system/SKILL.md`. Out of scope here; tracked as the next session's task.
- **Bow charge-progress sub-bar inside Attack button** (spec §3 decision #5). Visualizing `ChargingWeaponInstance.ChargeProgress / ChargingTime` requires a third NetworkVariable on `CharacterEquipment` (charge state is host-only today). Deferred from v1 — bow players see the existing animation + queued label; the sub-bar fill is polish. Plan: add `NetworkVariable<float> _activeChargePct01Net` paired with the existing ammo/reload pair, and a small `Image fillAmount` overlay inside the Attack button driven by `OnActiveChargeChanged`.
- **Queued label semantic enrichment** — `PlannedAction` closure doesn't carry icon/name today. Future polish adds an `ActionDescriptor` parameter to `SetActionIntent`.
- **Hold-Y radial weapon picker** — for 3+ weapon loadouts, replace tap-cycle with a radial pop-up.
- **"Pin item to ability slot"** — drag a Health Potion onto slot 5 to bind it.
- **Shift-click multi-use in Items window** — power-user shortcut.
- **Enemy initiative bars / Party panel chrome** — Option B + Option C from the chrome brainstorm. Resurfacable if enemy threat readout becomes a felt need.
