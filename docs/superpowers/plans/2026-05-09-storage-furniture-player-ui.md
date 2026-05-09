# Storage Furniture Player UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wire a HUD panel that opens when the local player taps E on a `StorageFurniture`, lets them deposit items from their inventory or hands and withdraw items from the chest's slots — using the exact same `CharacterAction` classes that NPC GOAP already uses.

**Architecture:** `StorageFurniture` overrides `Furniture.OnInteract` (the existing tap-E entry on the furniture base) to call `PlayerUI.OpenStoragePanel(this, interactor)` for the local owner-player. The panel composes two `UI_StorageGrid` instances (a single renderer used for both player bag and chest) plus a hands sub-slot. Click handlers queue `CharacterStoreInFurnitureAction` (left side) or `CharacterTakeFromFurnitureAction` (right side) — both already server-authoritative + replicated via the existing `StorageFurnitureNetworkSync`. Zero new RPCs.

**Tech Stack:** Unity 2022.3 + Netcode for GameObjects (NGO) 2.x, TextMeshPro, UGUI (Button / Image / GridLayoutGroup), `MonoBehaviour` lifecycle hooks (`Awake` / `Update` / `OnDisable` / `OnDestroy`), no automated tests (project convention is manual play-mode verification — see Section "Testing approach" below).

---

## Spec corrections vs. design doc

The spec at [docs/superpowers/specs/2026-05-09-storage-furniture-player-ui-design.md](docs/superpowers/specs/2026-05-09-storage-furniture-player-ui-design.md) said the panel should *reuse* `UI_Inventory` for the player bag side. Closer inspection of `UI_ItemSlot.OnPointerClick` (only handles right-click → drop, no left-click hook) shows that reusing it would either:

1. force a left-click hook into `UI_ItemSlot` (touches the equipment UI slot behaviour), or
2. drag in a wrapper to intercept clicks before `UI_ItemSlot` sees them (awkward).

**Correction:** This plan uses a single `UI_StorageGrid` renderer for *both* halves of the panel (player bag side + chest side). Each side passes its own click callback. `UI_Inventory` and `UI_ItemSlot` are unchanged — `CharacterEquipmentUI` keeps using them. Right-click-drop behaviour is not replicated inside the storage panel for this iteration (single left-click = transfer, no other gestures).

Everything else in the spec stands.

---

## Testing approach

The project has no automated UI test suite. UI scripts are verified via Unity Play Mode with explicit, reproducible scenarios. Each scenario lists:

- Setup steps (scene, character, chest state)
- Expected Console output (search the Console for the listed `[StoreInFurniture]` / `[TakeFromFurniture]` log lines from the existing actions)
- Expected on-screen behaviour
- Pass criteria

Manual scenarios live in **Tasks 7 + 8**.

If any task fails its verification, do NOT advance — investigate. Do not silently skip.

---

## File Structure

### Create

| Path | Responsibility |
|---|---|
| `Assets/Scripts/UI/WorldUI/UI_StorageGrid.cs` | Pure renderer for `IReadOnlyList<ItemSlot>` with a left-click callback. Pool-based instantiation of slot button GameObjects. Used by `UI_StorageFurniturePanel` for both player-bag side and chest side. |
| `Assets/Scripts/UI/WorldUI/UI_StorageFurniturePanel.cs` | Panel controller. Initializes from `(StorageFurniture, Character)`, subscribes to events, polls hands + interaction zone each frame, handles ESC, queues store/take actions on slot clicks. |
| `Assets/UI/Player HUD/UI_StorageFurniturePanel.prefab` | Unity-authored prefab. RectTransform under PlayerHUD canvas, with two `UI_StorageGrid` children (left/right) + hands sub-slot button + close button + chest-name label. |
| `Assets/UI/Player HUD/UI_StorageGridSlot.prefab` | Unity-authored single-slot button prefab used by `UI_StorageGrid`'s pool. Contains `Button` + `Image` (icon) + `TextMeshProUGUI` (name). |
| `wiki/systems/storage-furniture-ui.md` | New wiki system page documenting this UI surface (architecture-only, links to existing actions). |

### Modify

| Path | Change |
|---|---|
| `Assets/Scripts/World/Furniture/StorageFurniture.cs` | Add `public override bool OnInteract(Character interactor)` that calls `PlayerUI.Instance.OpenStoragePanel(this, interactor)` for the owner-player only. |
| `Assets/Scripts/UI/PlayerUI.cs` | Add `[SerializeField] UI_StorageFurniturePanel _storagePanel`, `OpenStoragePanel(StorageFurniture, Character)`, `CloseStoragePanel()`. |
| `Assets/UI/Player HUD/UI_PlayerHUD.prefab` | Add the new panel as a child + wire the `_storagePanel` field on the `PlayerUI` component. (Unity-authoring step.) |
| `wiki/systems/storage-furniture.md` | Append change-log entry referencing the new UI page. Bump `updated:` field. Add link in `depended_on_by` if appropriate. |
| `.claude/agents/building-furniture-specialist.md` | Append knowledge of the new player UI surface (the spec / panel scripts / `OnInteract` override) to the agent's domain. |

### Untouched (verified)

- `PlayerController.cs` — no changes. Tap-E already routes through `Furniture.OnInteract`. ESC handling lives in the panel itself per rule #33's UI-input carve-out.
- `CharacterStoreInFurnitureAction.cs` — used as-is.
- `CharacterTakeFromFurnitureAction.cs` — used as-is.
- `UI_Inventory.cs`, `UI_ItemSlot.cs` — used by `CharacterEquipmentUI` only; unaffected.
- `StorageFurnitureNetworkSync.cs` — replication unchanged.

---

## Tasks

### Task 1: Create `UI_StorageGrid.cs`

**Files:**
- Create: `Assets/Scripts/UI/WorldUI/UI_StorageGrid.cs`

This is a leaf component — no dependencies on the panel. Write it first so the panel compiles cleanly in Task 2.

- [ ] **Step 1: Create the file with the full class body**

```csharp
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Generic slot-grid renderer used for both halves of <see cref="UI_StorageFurniturePanel"/>:
/// the player's bag inventory on the left and the chest's slots on the right.
///
/// Pool-based: keeps one <see cref="Button"/> per <see cref="_slotPrefab"/> instance and
/// reuses them across rebinds to avoid per-rebind allocations. Empty slots render as
/// non-interactable buttons labelled "(empty)".
///
/// Left-click delivers the slot's <see cref="ItemInstance"/> to the consumer-supplied
/// <see cref="_clickCallback"/>. The grid does not own the action — the panel does — so
/// this script stays UI-pure.
/// </summary>
public class UI_StorageGrid : MonoBehaviour
{
    [Header("Wiring")]
    [Tooltip("Prefab used for each slot button. Must have Button + child TMP_Text + child Image (icon).")]
    [SerializeField] private GameObject _slotPrefab;

    [Tooltip("Layout root that holds the instantiated slot buttons (typically a GridLayoutGroup).")]
    [SerializeField] private Transform _slotContainer;

    [Tooltip("Optional capacity label like '3 / 8'. May be null.")]
    [SerializeField] private TextMeshProUGUI _capacityLabel;

    private readonly List<SlotInstance> _pool = new List<SlotInstance>();
    private Action<ItemInstance> _clickCallback;
    private Func<bool> _interactableGate;

    private struct SlotInstance
    {
        public GameObject Root;
        public Button Button;
        public TextMeshProUGUI Label;
        public Image Icon;
    }

    /// <summary>
    /// Render <paramref name="slots"/>. <paramref name="onSlotLeftClicked"/> is invoked
    /// with the slot's <see cref="ItemInstance"/> on left-click of a populated slot.
    /// <paramref name="interactableGate"/> is queried per-frame in <see cref="RefreshInteractable"/>
    /// to gray out clicks while an action is in flight; pass null to skip the gate.
    /// </summary>
    public void Bind(IReadOnlyList<ItemSlot> slots, Action<ItemInstance> onSlotLeftClicked, Func<bool> interactableGate)
    {
        _clickCallback = onSlotLeftClicked;
        _interactableGate = interactableGate;

        // Hide all pooled slots first
        for (int i = 0; i < _pool.Count; i++)
        {
            if (_pool[i].Root != null) _pool[i].Root.SetActive(false);
        }

        if (slots == null)
        {
            UpdateCapacityLabel(0, 0);
            return;
        }

        int occupied = 0;
        for (int i = 0; i < slots.Count; i++)
        {
            SlotInstance inst = i < _pool.Count ? _pool[i] : Grow();
            inst.Root.SetActive(true);

            ItemSlot slot = slots[i];
            ItemInstance item = slot != null ? slot.ItemInstance : null;
            bool empty = item == null;

            if (inst.Label != null)
            {
                inst.Label.text = empty ? "<color=#666666>(empty)</color>" : item.CustomizedName;
            }
            if (inst.Icon != null)
            {
                if (!empty && item.ItemSO != null && item.ItemSO.Icon != null)
                {
                    inst.Icon.sprite = item.ItemSO.Icon;
                    inst.Icon.enabled = true;
                }
                else
                {
                    inst.Icon.enabled = false;
                }
            }

            inst.Button.onClick.RemoveAllListeners();
            int capturedIndex = i;  // capture for closure
            inst.Button.onClick.AddListener(() => OnSlotClicked(capturedIndex, slots));
            inst.Button.interactable = !empty;

            if (!empty) occupied++;
        }

        UpdateCapacityLabel(occupied, slots.Count);
    }

    /// <summary>
    /// Re-evaluate the interactable gate (typically "is the character idle?") on every
    /// pooled, populated slot. Call from the panel's Update() so a deposit-in-progress
    /// grays out further clicks until the action finishes.
    /// </summary>
    public void RefreshInteractable()
    {
        bool gate = _interactableGate == null || _interactableGate.Invoke();
        for (int i = 0; i < _pool.Count; i++)
        {
            if (_pool[i].Root == null || !_pool[i].Root.activeSelf) continue;
            // Don't override empty-slot disable; re-derive populated state from listener count.
            if (_pool[i].Button.onClick.GetPersistentEventCount() == 0)
            {
                // Listener already removed; leave as-is. (Defensive — should not happen mid-frame.)
            }
            // Empty buttons keep interactable=false (set in Bind). For the populated buttons
            // we OR with the gate.
            // Heuristic: if Label.text starts with "(empty)" marker, treat as empty.
            bool empty = _pool[i].Label != null && _pool[i].Label.text.StartsWith("<color=#666666>(empty)");
            _pool[i].Button.interactable = !empty && gate;
        }
    }

    private SlotInstance Grow()
    {
        if (_slotPrefab == null || _slotContainer == null)
        {
            Debug.LogError($"<color=red>[UI_StorageGrid]</color> {name}: _slotPrefab or _slotContainer not assigned.");
            return default;
        }

        GameObject go = Instantiate(_slotPrefab, _slotContainer);
        SlotInstance inst = new SlotInstance
        {
            Root = go,
            Button = go.GetComponent<Button>(),
            Label = go.GetComponentInChildren<TextMeshProUGUI>(true),
            Icon = go.GetComponentInChildren<Image>(true),
        };
        _pool.Add(inst);
        return inst;
    }

    private void OnSlotClicked(int index, IReadOnlyList<ItemSlot> slots)
    {
        if (slots == null || index < 0 || index >= slots.Count) return;
        var slot = slots[index];
        if (slot == null || slot.IsEmpty() || slot.ItemInstance == null) return;
        _clickCallback?.Invoke(slot.ItemInstance);
    }

    private void UpdateCapacityLabel(int occupied, int total)
    {
        if (_capacityLabel == null) return;
        _capacityLabel.text = $"{occupied} / {total}";
    }

    /// <summary>Clears the bound callback. Call before destroying the panel.</summary>
    public void Unbind()
    {
        _clickCallback = null;
        _interactableGate = null;
        for (int i = 0; i < _pool.Count; i++)
        {
            if (_pool[i].Button != null) _pool[i].Button.onClick.RemoveAllListeners();
            if (_pool[i].Root != null) _pool[i].Root.SetActive(false);
        }
    }
}
```

- [ ] **Step 2: Save the file. Wait for Unity to recompile.**

In Unity Editor, watch the bottom-right status bar for the spinner to finish. Open the Console (Window → General → Console). The script must compile with **zero errors**.

Expected: Console shows no compile errors related to `UI_StorageGrid`. If `Image` or `Button` ambiguity errors appear, ensure `using UnityEngine.UI;` is at the top.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/UI/WorldUI/UI_StorageGrid.cs Assets/Scripts/UI/WorldUI/UI_StorageGrid.cs.meta
git commit -m "feat(ui): add UI_StorageGrid renderer for storage furniture panel"
```

---

### Task 2: Create `UI_StorageFurniturePanel.cs`

**Files:**
- Create: `Assets/Scripts/UI/WorldUI/UI_StorageFurniturePanel.cs`

- [ ] **Step 1: Create the file**

```csharp
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HUD panel that opens when the local owner-player taps E on a <see cref="StorageFurniture"/>.
///
/// Composition:
/// - Left side: hands sub-slot (one button) + bag grid (a <see cref="UI_StorageGrid"/> bound to the
///   character's bag inventory slots).
/// - Right side: chest grid (a second <see cref="UI_StorageGrid"/> bound to the storage furniture's
///   <see cref="StorageFurniture.ItemSlots"/>).
///
/// Click on left = queue <see cref="CharacterStoreInFurnitureAction"/>.
/// Click on right = queue <see cref="CharacterTakeFromFurnitureAction"/>.
/// Both actions are server-authoritative; the panel just constructs and queues them — same as
/// what GOAP does for NPCs (see GoapAction_GatherStorageItems / GoapAction_DepositResources /
/// GoapAction_TakeFromSourceFurniture / GoapAction_StageItemForPickup).
///
/// Closes on: ESC, target despawn, character incapacitated, character entering combat, or the
/// player walking out of the storage's interaction zone (polled in Update).
/// </summary>
public class UI_StorageFurniturePanel : MonoBehaviour
{
    [Header("Wiring (assign in prefab)")]
    [SerializeField] private TextMeshProUGUI _chestNameLabel;
    [SerializeField] private Button _closeButton;

    [Header("Left side — character")]
    [SerializeField] private Button _handsSlotButton;
    [SerializeField] private TextMeshProUGUI _handsSlotLabel;
    [SerializeField] private Image _handsSlotIcon;
    [SerializeField] private UI_StorageGrid _bagGrid;
    [SerializeField] private GameObject _bagGridRoot;
    [SerializeField] private TextMeshProUGUI _noBagLabel;

    [Header("Right side — chest")]
    [SerializeField] private UI_StorageGrid _chestGrid;

    private StorageFurniture _target;
    private Character _interactor;
    private FurnitureInteractable _targetInteractable;
    private ItemInstance _lastHandsItem;

    /// <summary>
    /// Called by <see cref="PlayerUI.OpenStoragePanel"/>. Activates the panel, wires up
    /// subscriptions, and paints the initial state. Calling this while already open
    /// re-binds to the new target cleanly.
    /// </summary>
    public void Initialize(StorageFurniture target, Character interactor)
    {
        if (target == null || interactor == null)
        {
            Debug.LogWarning("<color=orange>[StoragePanel]</color> Initialize called with null target or interactor.");
            return;
        }

        UnsubscribeAll();

        _target = target;
        _interactor = interactor;
        _targetInteractable = target.GetComponent<FurnitureInteractable>();
        _lastHandsItem = null;

        if (_chestNameLabel != null) _chestNameLabel.text = target.FurnitureName;

        if (_closeButton != null)
        {
            _closeButton.onClick.RemoveAllListeners();
            _closeButton.onClick.AddListener(Close);
        }
        if (_handsSlotButton != null)
        {
            _handsSlotButton.onClick.RemoveAllListeners();
            _handsSlotButton.onClick.AddListener(OnHandsSlotClicked);
        }

        _target.OnInventoryChanged += HandleStorageChanged;
        _interactor.CharacterEquipment.OnEquipmentChanged += HandleEquipmentChanged;

        gameObject.SetActive(true);

        RepaintAll();
    }

    public void Close()
    {
        UnsubscribeAll();
        _target = null;
        _interactor = null;
        _targetInteractable = null;
        _lastHandsItem = null;

        if (_chestGrid != null) _chestGrid.Unbind();
        if (_bagGrid != null) _bagGrid.Unbind();

        gameObject.SetActive(false);
    }

    private void UnsubscribeAll()
    {
        if (_target != null) _target.OnInventoryChanged -= HandleStorageChanged;
        if (_interactor != null && _interactor.CharacterEquipment != null)
            _interactor.CharacterEquipment.OnEquipmentChanged -= HandleEquipmentChanged;
    }

    private void OnDisable() => UnsubscribeAll();
    private void OnDestroy() => UnsubscribeAll();

    private void Update()
    {
        if (_target == null || _interactor == null) { Close(); return; }

        // ESC closes the panel (rule #33 carve-out: input that targets the UI itself stays in the UI).
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Close();
            return;
        }

        // Hands have no event — poll each frame, mirror CharacterEquipmentUI.RefreshHandsButton.
        var hands = _interactor.CharacterVisual?.BodyPartsController?.HandsController;
        ItemInstance carried = hands != null ? hands.CarriedItem : null;
        if (carried != _lastHandsItem)
        {
            _lastHandsItem = carried;
            RepaintHandsSlot();
        }

        // Auto-close when player walks out of interaction zone.
        if (_targetInteractable != null && !_targetInteractable.IsCharacterInInteractionZone(_interactor))
        {
            Close();
            return;
        }

        // Re-evaluate per-slot interactable so the action-busy gate visually grays buttons.
        if (_chestGrid != null) _chestGrid.RefreshInteractable();
        if (_bagGrid != null) _bagGrid.RefreshInteractable();
        RefreshHandsInteractable(carried);
    }

    private void HandleStorageChanged()
    {
        if (_target == null) return;
        _chestGrid?.Bind(_target.ItemSlots, OnChestSlotClicked, IsCharacterIdle);
    }

    private void HandleEquipmentChanged()
    {
        RepaintBagSide();
    }

    private void RepaintAll()
    {
        HandleStorageChanged();
        RepaintBagSide();
        RepaintHandsSlot();
    }

    private void RepaintBagSide()
    {
        if (_interactor == null) return;
        var equipment = _interactor.CharacterEquipment;
        bool haveBag = equipment != null && equipment.HaveInventory();

        if (_bagGridRoot != null) _bagGridRoot.SetActive(haveBag);
        if (_noBagLabel != null) _noBagLabel.gameObject.SetActive(!haveBag);

        if (haveBag && _bagGrid != null)
        {
            _bagGrid.Bind(equipment.GetInventory().ItemSlots, OnBagSlotClicked, IsCharacterIdle);
        }
        else if (_bagGrid != null)
        {
            _bagGrid.Unbind();
        }
    }

    private void RepaintHandsSlot()
    {
        if (_interactor == null) return;
        var hands = _interactor.CharacterVisual?.BodyPartsController?.HandsController;
        ItemInstance carried = hands != null ? hands.CarriedItem : null;

        if (_handsSlotLabel != null)
        {
            _handsSlotLabel.text = carried != null
                ? carried.CustomizedName
                : "<color=#666666>(empty)</color>";
        }
        if (_handsSlotIcon != null)
        {
            if (carried != null && carried.ItemSO != null && carried.ItemSO.Icon != null)
            {
                _handsSlotIcon.sprite = carried.ItemSO.Icon;
                _handsSlotIcon.enabled = true;
            }
            else
            {
                _handsSlotIcon.enabled = false;
            }
        }
        RefreshHandsInteractable(carried);
    }

    private void RefreshHandsInteractable(ItemInstance carried)
    {
        if (_handsSlotButton == null) return;
        _handsSlotButton.interactable = (carried != null) && IsCharacterIdle();
    }

    private bool IsCharacterIdle()
    {
        if (_interactor == null) return false;
        return _interactor.CharacterActions != null
            && _interactor.CharacterActions.CurrentAction == null;
    }

    private void OnBagSlotClicked(ItemInstance item) => QueueStore(item);
    private void OnHandsSlotClicked()
    {
        if (_interactor == null) return;
        var hands = _interactor.CharacterVisual?.BodyPartsController?.HandsController;
        ItemInstance carried = hands != null ? hands.CarriedItem : null;
        QueueStore(carried);
    }

    private void QueueStore(ItemInstance item)
    {
        if (item == null || _target == null || _interactor == null) return;
        if (_interactor.CharacterActions == null) return;
        if (_interactor.CharacterActions.CurrentAction != null) return;

        var action = new CharacterStoreInFurnitureAction(_interactor, item, _target);
        _interactor.CharacterActions.ExecuteAction(action);
    }

    private void OnChestSlotClicked(ItemInstance item)
    {
        if (item == null || _target == null || _interactor == null) return;
        if (_interactor.CharacterActions == null) return;
        if (_interactor.CharacterActions.CurrentAction != null) return;

        var action = new CharacterTakeFromFurnitureAction(_interactor, item, _target);
        _interactor.CharacterActions.ExecuteAction(action);
    }
}
```

- [ ] **Step 2: Save and wait for Unity to recompile.**

Expected: Console shows no compile errors.

Possible compile error: `'Character' does not contain a definition for 'CharacterEquipment'` or similar — if any reference doesn't resolve, double-check it against the existing types via:

```bash
grep -n "public CharacterEquipment CharacterEquipment" Assets/Scripts/Character/Character.cs
```

(All referenced members — `Character.CharacterEquipment`, `Character.CharacterVisual`, `Character.CharacterActions`, `CharacterEquipment.HaveInventory`, `CharacterEquipment.GetInventory`, `CharacterEquipment.OnEquipmentChanged`, `HandsController.CarriedItem`, `StorageFurniture.OnInventoryChanged`, `StorageFurniture.ItemSlots`, `StorageFurniture.FurnitureName`, `FurnitureInteractable.IsCharacterInInteractionZone`, `ItemInstance.CustomizedName`, `ItemInstance.ItemSO`, `ItemSO.Icon`, `CharacterStoreInFurnitureAction(Character, ItemInstance, StorageFurniture)`, `CharacterTakeFromFurnitureAction(Character, ItemInstance, StorageFurniture)` — were all confirmed during spec research and should compile.)

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/UI/WorldUI/UI_StorageFurniturePanel.cs Assets/Scripts/UI/WorldUI/UI_StorageFurniturePanel.cs.meta
git commit -m "feat(ui): add UI_StorageFurniturePanel script"
```

---

### Task 3: Add `OpenStoragePanel` / `CloseStoragePanel` to `PlayerUI.cs`

**Files:**
- Modify: `Assets/Scripts/UI/PlayerUI.cs`

- [ ] **Step 1: Add the field**

Open `Assets/Scripts/UI/PlayerUI.cs`. Find the `[Header("UI Windows")]` block (around line 38). Insert a new `[SerializeField]` line for the storage panel, alongside the other window references.

Locate this block:

```csharp
    [Header("UI Windows")]
    [SerializeField] private CharacterEquipmentUI _equipmentUI;
    [SerializeField] private UI_CharacterRelations _relationsUI;
    [SerializeField] private UI_CharacterStats _statsUI;
    [SerializeField] private MWI.UI.Building.UI_BuildingPlacementMenu _buildingUI;
```

Insert immediately after `_equipmentUI`:

```csharp
    [SerializeField] private UI_StorageFurniturePanel _storagePanel;
```

So the block becomes:

```csharp
    [Header("UI Windows")]
    [SerializeField] private CharacterEquipmentUI _equipmentUI;
    [SerializeField] private UI_StorageFurniturePanel _storagePanel;
    [SerializeField] private UI_CharacterRelations _relationsUI;
    [SerializeField] private UI_CharacterStats _statsUI;
    [SerializeField] private MWI.UI.Building.UI_BuildingPlacementMenu _buildingUI;
```

- [ ] **Step 2: Add the two public methods**

Find the `OpenInteractionMenu(...)` method (around line 346 — search for `public void OpenInteractionMenu`). Insert the storage helpers immediately above it:

```csharp
    /// <summary>
    /// Open the storage furniture exchange panel for <paramref name="storage"/>, bound
    /// to <paramref name="interactor"/>'s inventory + hands. Called from
    /// <see cref="StorageFurniture.OnInteract"/> when the local owner-player taps E.
    /// Re-binds cleanly if the panel is already open against a different target.
    /// </summary>
    public void OpenStoragePanel(StorageFurniture storage, Character interactor)
    {
        if (_storagePanel == null)
        {
            Debug.LogWarning("PlayerUI: UI_StorageFurniturePanel component not assigned!");
            return;
        }
        _storagePanel.Initialize(storage, interactor);
    }

    /// <summary>
    /// Close the storage furniture panel if it is currently open. Safe to call when the
    /// panel is already closed.
    /// </summary>
    public void CloseStoragePanel()
    {
        if (_storagePanel == null) return;
        if (_storagePanel.gameObject.activeSelf) _storagePanel.Close();
    }
```

- [ ] **Step 3: Save and wait for Unity to recompile.**

Expected: zero compile errors. The serialized field will appear as `None (UI_StorageFurniturePanel)` on the `PlayerUI` component in the Inspector — that's expected; we'll wire it in Task 6.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/UI/PlayerUI.cs
git commit -m "feat(ui): wire UI_StorageFurniturePanel into PlayerUI"
```

---

### Task 4: Add `OnInteract` override to `StorageFurniture.cs`

**Files:**
- Modify: `Assets/Scripts/World/Furniture/StorageFurniture.cs`

- [ ] **Step 1: Insert the override**

Open `Assets/Scripts/World/Furniture/StorageFurniture.cs`. Find the `protected virtual void Awake()` method (around line 86). Insert the `OnInteract` override immediately above it:

```csharp
    /// <summary>
    /// Tap-E entry for storage furniture. For the local owner-player, opens the
    /// <see cref="UI_StorageFurniturePanel"/>. For NPCs (which never reach this in
    /// practice — <see cref="FurnitureInteractable.Interact"/> is only triggered by
    /// <see cref="PlayerController.HandleEKeyUp"/> inside an <c>IsOwner</c> branch —
    /// returning true keeps the existing no-op contract).
    ///
    /// Returns true to satisfy the Furniture.OnInteract contract that "interaction
    /// was accepted" (the post-use callback fires either way).
    /// </summary>
    public override bool OnInteract(Character interactor)
    {
        if (interactor == null) return false;

        // Defence in depth — non-owner peers should never reach here through tap-E,
        // but if a future codepath does, we don't want a stray panel popping up.
        if (!interactor.IsOwner || !interactor.IsPlayer()) return true;

        if (PlayerUI.Instance == null) return true;

        PlayerUI.Instance.OpenStoragePanel(this, interactor);
        return true;
    }

```

- [ ] **Step 2: Save and wait for Unity to recompile.**

Expected: zero compile errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/World/Furniture/StorageFurniture.cs
git commit -m "feat(furniture): wire StorageFurniture.OnInteract to player storage panel"
```

---

### Task 5: Author the slot button prefab `UI_StorageGridSlot.prefab`

**Files:**
- Create: `Assets/UI/Player HUD/UI_StorageGridSlot.prefab`

This is a Unity-Editor-only step. The prefab is a simple button used as the slot template in `UI_StorageGrid._slotPrefab`.

- [ ] **Step 1: Open Unity Editor.**

Switch to Unity. Make sure the `multiplayyer` branch is checked out.

- [ ] **Step 2: Create the slot prefab**

In the Project window, navigate to `Assets/UI/Player HUD/`. Right-click → Create → Prefab → name it **`UI_StorageGridSlot`**. Then double-click to open it in Prefab Mode.

Build this hierarchy under the prefab root:

```
UI_StorageGridSlot          (RectTransform, Image (background, dark gray), Button, Layout Element)
  └─ Icon                   (RectTransform, Image — sprite slot, preserve aspect)
  └─ Label                  (RectTransform, TextMeshProUGUI — "Item name", autosize 10-14)
```

Settings:
- Root `Image` color: `#26262640` (semi-transparent dark gray)
- Root `Button.transition`: ColorTint (Normal `#FFFFFFFF`, Highlighted `#CCCCCCFF`, Pressed `#999999FF`, Disabled `#FFFFFF60`)
- `Icon`: anchored top-left, size ~48×48
- `Label`: anchored full-stretch with left padding past the icon, vertical-center alignment

Save the prefab (Ctrl+S) and exit Prefab Mode.

- [ ] **Step 3: Verify the prefab compiles cleanly**

Drag the prefab into a scene's Canvas as a sanity check. The button should render. Delete the test instance after verifying.

- [ ] **Step 4: Commit**

```bash
git add "Assets/UI/Player HUD/UI_StorageGridSlot.prefab" "Assets/UI/Player HUD/UI_StorageGridSlot.prefab.meta"
git commit -m "feat(ui): add UI_StorageGridSlot prefab"
```

---

### Task 6: Author the panel prefab `UI_StorageFurniturePanel.prefab`

**Files:**
- Create: `Assets/UI/Player HUD/UI_StorageFurniturePanel.prefab`
- Modify: `Assets/UI/Player HUD/UI_PlayerHUD.prefab` (add child + wire `_storagePanel` reference)

This is the bulk Unity-authoring task.

- [ ] **Step 1: Create the panel prefab skeleton**

In the Project window: `Assets/UI/Player HUD/` → Right-click → Create → Prefab → name **`UI_StorageFurniturePanel`** → open in Prefab Mode.

Build this hierarchy:

```
UI_StorageFurniturePanel         (RectTransform centered, ~600x500, Image (modal bg))
  ├─ ChestNameLabel              (TextMeshProUGUI, top-center, "Chest")
  ├─ CloseButton                 (Button, top-right corner, "X")
  ├─ LeftSide                    (Vertical Layout Group)
  │    ├─ HandsSlotRoot          (Horizontal Layout, label "Hands:")
  │    │    └─ HandsSlot         (Button, child Icon Image, child Label TMP_Text)
  │    ├─ NoBagLabel             (TMP_Text, "(no bag equipped)", initially hidden)
  │    └─ BagGridRoot            (Vertical Layout)
  │         └─ ScrollView         (ScrollRect)
  │              └─ Viewport
  │                   └─ Content (Grid Layout Group, 4×N — UI_StorageGrid host)
  │                        + UI_StorageGrid script
  │                        + ChildControlHeight=true on parent VLG (per memory feedback_scrollrect_with_tmp_layout_chain)
  └─ RightSide                   (Vertical Layout Group)
       └─ ChestGridRoot
            └─ ScrollView
                 └─ Viewport
                      └─ Content (Grid Layout Group + UI_StorageGrid script)
```

Layout numbers can match `UI_CharacterEquipment.prefab` for stylistic consistency — open it side-by-side and copy the visual style.

- [ ] **Step 2: Add the panel script + wire serialized fields**

Add the `UI_StorageFurniturePanel` component to the prefab root. In the Inspector, drag references:

| Field | Drag from |
|---|---|
| `_chestNameLabel` | `ChestNameLabel` TextMeshProUGUI |
| `_closeButton` | `CloseButton` Button |
| `_handsSlotButton` | `HandsSlot` Button |
| `_handsSlotLabel` | `HandsSlot/Label` TextMeshProUGUI |
| `_handsSlotIcon` | `HandsSlot/Icon` Image |
| `_bagGrid` | `LeftSide/BagGridRoot/ScrollView/Viewport/Content` `UI_StorageGrid` component |
| `_bagGridRoot` | `LeftSide/BagGridRoot` GameObject |
| `_noBagLabel` | `LeftSide/NoBagLabel` TextMeshProUGUI |
| `_chestGrid` | `RightSide/ChestGridRoot/ScrollView/Viewport/Content` `UI_StorageGrid` component |

- [ ] **Step 3: Wire the two `UI_StorageGrid` components**

For both `UI_StorageGrid` components (one per side):

| Field | Drag from |
|---|---|
| `_slotPrefab` | `Assets/UI/Player HUD/UI_StorageGridSlot.prefab` (from project) |
| `_slotContainer` | The same `Content` GameObject (the Grid Layout Group host — i.e. the same transform the script sits on) |
| `_capacityLabel` | (optional, leave empty for left side; for right side, drag a "n / total" TMP_Text if authored) |

- [ ] **Step 4: Save the prefab. Disable the prefab root's GameObject by default.**

Set the prefab root's `activeSelf` to `false` so it doesn't show on Initialize. The panel script flips this in `Initialize`.

Save (Ctrl+S). Exit Prefab Mode.

- [ ] **Step 5: Embed in `UI_PlayerHUD.prefab`**

Open `Assets/UI/Player HUD/UI_PlayerHUD.prefab`. Find the canvas child where other windows live (next to `UI_CharacterEquipment`). Drag `UI_StorageFurniturePanel` in as a sibling.

On the `PlayerUI` component (top-level of the HUD prefab), drag the new panel instance into the `_storagePanel` slot in the Inspector.

Save the HUD prefab.

- [ ] **Step 6: Commit**

```bash
git add "Assets/UI/Player HUD/UI_StorageFurniturePanel.prefab" "Assets/UI/Player HUD/UI_StorageFurniturePanel.prefab.meta" "Assets/UI/Player HUD/UI_PlayerHUD.prefab"
git commit -m "feat(ui): add UI_StorageFurniturePanel prefab + wire into UI_PlayerHUD"
```

---

### Task 7: Manual smoke test — solo play mode

Verify the basic open / store / take / close cycle in single-player.

- [ ] **Step 1: Start a fresh play session**

Open the main test scene (typically `Assets/Scenes/MainTest.unity` or whichever scene the user uses for ad-hoc testing). Press Play.

Spawn a player character. Make sure the character has:
- A bag equipped with at least one item
- Empty hands
- A `StorageFurniture` (chest) within walking distance

Use the dev mode (`/devmode` chat command, then Space+LMB) to spawn a test chest if none exist.

- [ ] **Step 2: Open the panel**

Walk the player character to within the chest's interaction zone. Tap **E**.

**Expected:**
- The `UI_StorageFurniturePanel` opens on screen.
- Left side shows the player's hands sub-slot ("(empty)") + bag inventory slots.
- Right side shows the chest's slots — empty if the chest is fresh.
- Console shows no errors.

- [ ] **Step 3: Store an item**

Click an item in the left bag grid.

**Expected:**
- Console shows `[StoreInFurniture] <CharacterName> stored <ItemName> in <ChestName>.` (from `CharacterStoreInFurnitureAction.OnApplyEffect`).
- The item disappears from the left bag grid.
- The item appears in the right chest grid.

- [ ] **Step 4: Take an item back**

Click the item in the right chest grid.

**Expected:**
- Console shows `[TakeFromFurniture] <CharacterName> took <ItemName> from <ChestName>.`
- The item disappears from the right chest grid.
- The item appears in the left hands sub-slot (since `CharacterTakeFromFurnitureAction` places into hands).

- [ ] **Step 5: Store the held item via hands sub-slot**

Click the hands sub-slot.

**Expected:**
- Console shows `[StoreInFurniture] ...` again.
- Hands sub-slot becomes "(empty)" again.
- Item reappears in the chest.

- [ ] **Step 6: Verify auto-close on walking out**

Walk the character out of the chest's interaction zone (visible by debug draw of `InteractionZone` collider, or by distance — the zone is typically 2-3 Unity units around the chest).

**Expected:** the panel closes automatically as the character leaves the zone.

- [ ] **Step 7: Verify ESC closes**

Re-open the panel (walk back, tap E). Press ESC.

**Expected:** the panel closes immediately.

- [ ] **Step 8: Verify multiple chests re-bind**

Open chest A. With panel open, walk to chest B and tap E.

**Expected:** the panel switches to chest B without flickering or duplicate panels.

- [ ] **Step 9: Stop play. Commit any incidental fixes.**

If issues found in steps 2-8, capture them, fix them, re-run the section that failed, then commit fixes:

```bash
git add <touched files>
git commit -m "fix(ui): <description of issue found in solo smoke test>"
```

If everything passed, no commit needed for this task.

---

### Task 8: Manual smoke test — multiplayer (host + client)

Verify that the panel works correctly across the multiplayer authority boundary.

- [ ] **Step 1: Set up host + client**

Build the project (File → Build Settings → Build) into a separate folder, e.g. `Builds/MWI_Standalone.exe`. Launch the Editor and the standalone build side by side. One starts as Host, the other Joins.

- [ ] **Step 2: Host scenario — host taps E on a chest**

On the host, walk a character to a chest, tap E.

**Expected:**
- Panel opens on **host's screen only**. Client sees nothing UI-wise.
- Click an item in the bag grid → host's console shows `[StoreInFurniture]` log. Item moves to chest.
- Client's view of the chest reflects the new item (if the client has the chest in view, e.g. via debug panel or by spawning their own character nearby — chest contents are network-replicated by `StorageFurnitureNetworkSync`).

- [ ] **Step 3: Client scenario — client taps E on the same chest**

On the client, walk to the same chest, tap E.

**Expected:**
- Panel opens on **client's screen only**. Host sees no panel popping.
- Click an item → client's console shows the action queued; server (host) processes; chest content updates on both peers.

- [ ] **Step 4: NPC-collision scenario**

While the client has the panel open, have an NPC harvester deposit an item into the same chest (let GOAP run naturally, or use dev tools to trigger). The NPC will use `GoapAction_GatherStorageItems` → `CharacterStoreInFurnitureAction`.

**Expected:**
- The client's open panel **repaints** to show the NPC's deposit appear in the chest grid in real time.
- No console errors.

- [ ] **Step 5: Two-client race**

If a second client is connected, both open the panel on the same chest simultaneously. Both click items.

**Expected:**
- Each click is queued via `CharacterActions.ExecuteAction` independently.
- Server processes them sequentially.
- Both panels' chest grids converge to the same final state (consistent across clients).
- No NetworkVariable desync warnings in the Console on either side.

- [ ] **Step 6: Late-joining client**

After items have been stored, have a fresh client join. They walk to the chest and tap E.

**Expected:**
- The panel opens with the chest grid already showing **all current items** (late-join sync via `StorageFurnitureNetworkSync.OnNetworkSpawn → ApplyFullStateOnClient`).

- [ ] **Step 7: Stop both peers. Commit any fixes.**

If issues found, capture them, fix them, re-run scenarios that failed, then commit fixes:

```bash
git add <touched files>
git commit -m "fix(ui): <description of multiplayer issue>"
```

---

### Task 9: Create wiki page `wiki/systems/storage-furniture-ui.md` + update existing storage-furniture page

**Files:**
- Create: `wiki/systems/storage-furniture-ui.md`
- Modify: `wiki/systems/storage-furniture.md`

- [ ] **Step 1: Create the new wiki page**

Path: `wiki/systems/storage-furniture-ui.md`

Content:

```markdown
---
type: system
title: "Storage Furniture — Player UI"
tags: [ui, hud, storage, furniture, inventory]
created: 2026-05-09
updated: 2026-05-09
sources:
  - Assets/Scripts/UI/WorldUI/UI_StorageFurniturePanel.cs
  - Assets/Scripts/UI/WorldUI/UI_StorageGrid.cs
  - Assets/UI/Player HUD/UI_StorageFurniturePanel.prefab
  - docs/superpowers/specs/2026-05-09-storage-furniture-player-ui-design.md
related:
  - "[[storage-furniture]]"
  - "[[character-actions]]"
  - "[[player-ui-hud]]"
status: stable
confidence: high
primary_agent: building-furniture-specialist
secondary_agents: [character-system-specialist]
owner_code_path: Assets/Scripts/UI/WorldUI/
depends_on:
  - "[[storage-furniture]]"
  - "[[character-equipment]]"
  - "[[character-actions]]"
depended_on_by: []
---

# Storage Furniture — Player UI

## Summary

Player-side HUD panel that opens when the local owner-player taps E on a `StorageFurniture` chest. Shows the player's bag inventory + hands on the left and the chest's slots on the right. Click-to-transfer in either direction, routing through the same `CharacterStoreInFurnitureAction` and `CharacterTakeFromFurnitureAction` that NPC GOAP already uses. No new RPCs — the UI is a thin shell over existing server-authoritative actions.

## Purpose

Close the API/UI gap on `StorageFurniture`: NPCs already store and retrieve items via GOAP, but no player-facing surface queues those same actions. This system is that surface.

## Responsibilities

- React to `Furniture.OnInteract` from the owner-player.
- Render the chest's slots and the player's bag inventory + hands item.
- Subscribe to `StorageFurniture.OnInventoryChanged` and `CharacterEquipment.OnEquipmentChanged` to repaint live.
- Poll `HandsController.CarriedItem` (no event fires for hands carry).
- Auto-close on ESC, walk-out-of-zone, target despawn, character incapacitated, or combat entry.
- Construct + queue `CharacterStoreInFurnitureAction` / `CharacterTakeFromFurnitureAction` instances on slot clicks.

## Key classes / files

- `Assets/Scripts/UI/WorldUI/UI_StorageFurniturePanel.cs` — panel controller. Initializes from `(StorageFurniture, Character)`, owns the lifecycle, click handlers, and polling.
- `Assets/Scripts/UI/WorldUI/UI_StorageGrid.cs` — generic slot-grid renderer used for both halves of the panel (player bag + chest).
- `Assets/UI/Player HUD/UI_StorageFurniturePanel.prefab` — authored prefab.
- `Assets/UI/Player HUD/UI_StorageGridSlot.prefab` — slot button template.
- `Assets/Scripts/World/Furniture/StorageFurniture.cs` — `OnInteract(Character)` override is the open path.
- `Assets/Scripts/UI/PlayerUI.cs` — `OpenStoragePanel(StorageFurniture, Character)` / `CloseStoragePanel()` helpers.

## Public API / entry points

- `PlayerUI.Instance.OpenStoragePanel(StorageFurniture, Character)` — the only sanctioned way to open the panel. Called by `StorageFurniture.OnInteract`.
- `PlayerUI.Instance.CloseStoragePanel()` — programmatic close. ESC and auto-close paths invoke `UI_StorageFurniturePanel.Close()` directly.

## Data flow

```
Player taps E on chest (in InteractionZone)
  → PlayerController.HandleEKeyUp (owner-only)
  → nearest.Interact(player) on the chest's FurnitureInteractable
  → Furniture.OnInteract(Character) -- StorageFurniture override
  → PlayerUI.Instance.OpenStoragePanel(this, player)
  → UI_StorageFurniturePanel.Initialize(target, interactor)
  → SetActive(true), bind both grids, subscribe to events
  → repaint each frame as state changes via StorageFurniture.OnInventoryChanged
    + CharacterEquipment.OnEquipmentChanged + Update()-poll on HandsController

Player clicks bag slot or hands sub-slot
  → UI_StorageFurniturePanel.QueueStore(item)
  → new CharacterStoreInFurnitureAction(interactor, item, target)
  → interactor.CharacterActions.ExecuteAction(action)
  → existing client→server RPC inside CharacterActions
  → server runs OnApplyEffect: removes from inventory/hands, calls target.AddItem
  → fires StorageFurniture.OnInventoryChanged server-side
  → StorageFurnitureNetworkSync.HandleServerInventoryChanged rewrites NetworkList
  → all clients (including owner) mirror via ApplySyncedSlotsFromNetwork
  → local OnInventoryChanged fires → panel repaints

Player clicks chest slot
  → UI_StorageFurniturePanel.OnChestSlotClicked
  → new CharacterTakeFromFurnitureAction(interactor, item, target)
  → interactor.CharacterActions.ExecuteAction(action)
  → server: target.RemoveItem + HandsController.CarryItem
  → existing replication paths (storage NetworkList for chest, hands sync for character)
```

## Dependencies

- **Upstream:** `Furniture` / `StorageFurniture` / `FurnitureInteractable` (the open path), `CharacterStoreInFurnitureAction` / `CharacterTakeFromFurnitureAction` (the action layer), `StorageFurnitureNetworkSync` (replication), `Character.CharacterEquipment` + `HandsController` (left side data), `PlayerUI` + `UI_PlayerHUD` (host canvas).
- **Downstream:** none. This is a leaf consumer.

## State & persistence

- The panel itself is ephemeral. No save data. No NetworkVariables.
- All persisted state lives on `StorageFurniture` / `StorageFurnitureNetworkSync` (chest contents) and `CharacterEquipment` (bag inventory). The panel only renders.

## Known gotchas / edge cases

- **Owner gate is critical.** Without it, panels would pop on every replicated peer when any character calls `OnInteract`. Verified via `interactor.IsOwner && interactor.IsPlayer()`.
- **Hands has no event.** `HandsController.CarriedItem` changes are not signalled. Panel polls each frame in `Update()`. Same pattern as `CharacterEquipmentUI.RefreshHandsButton`.
- **`UI_Inventory` not reused.** `UI_ItemSlot` only handles right-click-drop, no left-click hook. Reusing it for the panel's bag side would require modifying the equipment-UI slot behaviour, so the panel uses its own `UI_StorageGrid` renderer for both halves.
- **ESC handling lives in the panel itself.** Rule #33's "input that targets the UI" carve-out applies — same pattern as `PauseMenuController`, `BuildingPlacementManager`, etc.
- **Re-bind on tap-E to a different chest works without explicit close.** `Initialize` calls `UnsubscribeAll` first.

## Open questions / TODO

- _(none currently)_

## Change log

- 2026-05-09 — Created. Initial implementation. — claude

## Sources

- `docs/superpowers/specs/2026-05-09-storage-furniture-player-ui-design.md`
- `docs/superpowers/plans/2026-05-09-storage-furniture-player-ui.md`
- `wiki/systems/storage-furniture.md`
- `wiki/systems/character-actions.md`
```

- [ ] **Step 2: Update the existing storage-furniture wiki page**

Open `wiki/systems/storage-furniture.md`. Make the following changes:

1. Bump the `updated:` field in frontmatter to `2026-05-09`.
2. If the page has a `depended_on_by:` list, add `"[[storage-furniture-ui]]"` to it.
3. Add a change-log entry under `## Change log`:

```markdown
- 2026-05-09 — Player-side UI surface added — see [[storage-furniture-ui]] for the new HUD panel that wires `Furniture.OnInteract` to `CharacterStoreInFurnitureAction` / `CharacterTakeFromFurnitureAction`. — claude
```

- [ ] **Step 3: Regenerate the wiki INDEX**

Run the wiki map slash command:

```
/map
```

This regenerates `wiki/INDEX.md` to include the new page.

- [ ] **Step 4: Commit**

```bash
git add wiki/systems/storage-furniture-ui.md wiki/systems/storage-furniture.md wiki/INDEX.md
git commit -m "docs(wiki): add storage-furniture-ui system page"
```

---

### Task 10: Update `building-furniture-specialist` agent

**Files:**
- Modify: `.claude/agents/building-furniture-specialist.md`

- [ ] **Step 1: Locate the agent file and find the appropriate section**

Open `.claude/agents/building-furniture-specialist.md`. The file's domain description (in the frontmatter `description:` field and in the body's "domain knowledge" section) currently covers `StorageFurniture` slot-based containers and `StorageVisualDisplay`. The new player UI is a peer surface that the agent should know about.

- [ ] **Step 2: Append a knowledge entry**

In the agent's domain description and/or body, append a paragraph similar to:

```markdown
- **StorageFurniture player UI** (`UI_StorageFurniturePanel` + `UI_StorageGrid` under
  `Assets/Scripts/UI/WorldUI/`): tap-E on a `StorageFurniture` opens a HUD panel showing
  the player's bag inventory + hands on the left and the chest's slots on the right.
  Click-to-transfer routes through the existing `CharacterStoreInFurnitureAction` /
  `CharacterTakeFromFurnitureAction` (the same actions NPC GOAP queues), no new RPCs.
  Open path: `StorageFurniture.OnInteract` → `PlayerUI.Instance.OpenStoragePanel`.
  Reference: [wiki/systems/storage-furniture-ui.md](../../wiki/systems/storage-furniture-ui.md).
```

The exact insertion point depends on the agent file's structure — look for where other UI-adjacent knowledge is organised and match that style.

- [ ] **Step 3: Verify the agent file still reads coherently**

Skim the agent file end-to-end. The new entry must not duplicate existing content; it should compose with the agent's other knowledge entries.

- [ ] **Step 4: Commit**

```bash
git add .claude/agents/building-furniture-specialist.md
git commit -m "docs(agent): teach building-furniture-specialist about storage UI"
```

---

### Task 11: Final acceptance run + summary commit

- [ ] **Step 1: Run the full acceptance criteria checklist from the spec**

Open `docs/superpowers/specs/2026-05-09-storage-furniture-player-ui-design.md` → "Acceptance criteria" section. Walk through all 9 criteria one more time and confirm each:

1. Tap-E opens panel for owner-player only — verified Task 7 step 2 + Task 8 step 2-3.
2. Layout matches spec — verified Task 7 step 2.
3. Store works — verified Task 7 steps 3, 5.
4. Take works — verified Task 7 step 4.
5. NPC parity — verified Task 8 step 4.
6. Close on ESC, zone exit, etc. — verified Task 7 steps 6-7.
7. Multiplayer scenarios — verified Task 8 steps 2-6.
8. No GOAP regression — verify NPC harvesters still deposit normally (run a HarvestingBuilding for one in-game minute, confirm normal deposits).
9. Documentation — verified Tasks 9-10.

- [ ] **Step 2: Run editor-mode tests if any exist**

```bash
# From the project root, via the unity-mcp tool surface:
# Use the existing Tests Run flow if a test for character-actions / storage exists.
```

If no automated tests exist (project convention), skip this step. The project's manual verification gates in Tasks 7 + 8 are the test.

- [ ] **Step 3: Verify the commit log is clean and tells the story**

```bash
git log --oneline origin/multiplayyer..HEAD
```

You should see roughly:
```
docs(spec): storage furniture player UI design                                    (Task 0 — already shipped)
feat(ui): add UI_StorageGrid renderer for storage furniture panel                 (Task 1)
feat(ui): add UI_StorageFurniturePanel script                                     (Task 2)
feat(ui): wire UI_StorageFurniturePanel into PlayerUI                             (Task 3)
feat(furniture): wire StorageFurniture.OnInteract to player storage panel         (Task 4)
feat(ui): add UI_StorageGridSlot prefab                                           (Task 5)
feat(ui): add UI_StorageFurniturePanel prefab + wire into UI_PlayerHUD            (Task 6)
fix(ui): <if any>                                                                 (Tasks 7-8 — optional)
docs(wiki): add storage-furniture-ui system page                                  (Task 9)
docs(agent): teach building-furniture-specialist about storage UI                 (Task 10)
```

- [ ] **Step 4: No final summary commit needed**

The work is complete. If a follow-up arises (additional UX polish like drag-and-drop or take-all), capture it in `wiki/projects/optimisation-backlog.md` or as a new spec.

---

## Reference: where the existing pieces live

For the implementing engineer's quick navigation:

| Need | Location |
|---|---|
| Existing `Furniture.OnInteract` virtual | `Assets/Scripts/World/Furniture/Furniture.cs:63-66` |
| Existing `FurnitureInteractable.Interact` | `Assets/Scripts/World/Furniture/FurnitureInteractable.cs` |
| `StorageFurniture` API | `Assets/Scripts/World/Furniture/StorageFurniture.cs` (`ItemSlots`, `OnInventoryChanged`, `IsLocked`, `AddItem`, `RemoveItem`, `HasFreeSpaceForItem`) |
| Network sync layer | `Assets/Scripts/World/Furniture/StorageFurnitureNetworkSync.cs` |
| `Character.CharacterEquipment` | `Assets/Scripts/Character/CharacterEquipment/CharacterEquipment.cs` (`GetInventory`, `HaveInventory`, `OnEquipmentChanged`) |
| Hands controller | `Assets/Scripts/Character/.../HandsController.cs` (`CarriedItem`, `IsCarrying`, `AreHandsFree`) |
| Existing precedents | `CharacterEquipmentUI.cs`, `UI_Inventory.cs`, `UI_ItemSlot.cs` |
| Action classes (do NOT modify) | `Assets/Scripts/Character/CharacterActions/CharacterStoreInFurnitureAction.cs`, `CharacterTakeFromFurnitureAction.cs` |
| GOAP precedents (read-only reference) | `Assets/Scripts/AI/GOAP/Actions/GoapAction_GatherStorageItems.cs:334`, `GoapAction_DepositResources.cs:183/235`, `GoapAction_TakeFromSourceFurniture.cs:175`, `GoapAction_StageItemForPickup.cs:225` |
| `PlayerUI` HUD facade | `Assets/Scripts/UI/PlayerUI.cs` |
| HUD prefab | `Assets/UI/Player HUD/UI_PlayerHUD.prefab` |

---

## Hand-off

After Task 11 passes, the feature ships. The spec's acceptance criteria are the contract; if any gap remains, return to the failing task and remediate before declaring done.
