# Storage Furniture — Player Interaction UI — design

> Player-side UI to deposit / withdraw items into a `StorageFurniture` chest.
> Covers the existing API gap: NPCs already use `CharacterStoreInFurnitureAction`
> + `CharacterTakeFromFurnitureAction` via GOAP, but no player surface queues
> them. This spec wires a HUD panel that does exactly that.

**Status:** brainstorming complete; ready for `superpowers:writing-plans`.

**Author:** generated through guided brainstorming, 2026-05-09.

**Explicitly out of scope** (see [Section 10](#section-10--out-of-scope)):

- Storage lock / key system. `StorageFurniture._isLocked` stays a stub flag.
- Drag-and-drop UX. Click-to-transfer only.
- "Store-all" / "Take-all" bulk buttons.
- Storage role / sell-shelf / tool-storage management — that lives on the
  Management Panel and is unaffected by this UI.

---

## Section 1 — Architecture overview

### Component map

```
StorageFurniture (existing — Furniture subclass)
  ├─ override Furniture.OnInteract(Character)   // NEW — opens panel for owner-player only
  ├─ existing API (ItemSlots, AddItem, RemoveItem, OnInventoryChanged)  [unchanged]
  └─ existing StorageFurnitureNetworkSync sibling                        [unchanged]

PlayerUI (existing HUD facade)
  ├─ [SerializeField] UI_StorageFurniturePanel _storagePanel            // NEW
  ├─ OpenStoragePanel(StorageFurniture, Character)                       // NEW
  └─ CloseStoragePanel()                                                 // NEW

UI_StorageFurniturePanel : MonoBehaviour                                 // NEW script
  ├─ Initialize(StorageFurniture target, Character interactor)
  ├─ Left side  : hands sub-slot + UI_Inventory (player bag)
  ├─ Right side : UI_StorageGrid (chest slots)
  ├─ Subscribes to:
  │    target.OnInventoryChanged       (chest contents change)
  │    interactor.CharacterEquipment.OnEquipmentChanged (bag/equipment change)
  ├─ Polls HandsController.CarriedItem each frame in Update()
  ├─ Polls FurnitureInteractable.IsCharacterInInteractionZone(interactor) each
  │    frame; auto-closes when false
  └─ Closes on ESC, target despawn, character incapacitated

UI_StorageGrid : MonoBehaviour                                           // NEW script
  ├─ Pure renderer for IReadOnlyList<ItemSlot> (chest slots)
  ├─ Bind(IReadOnlyList<ItemSlot>, Action<ItemInstance> onSlotClicked)
  └─ Pool of UI_ItemSlot-shaped buttons (mirror UI_Inventory pattern)

(PlayerController is NOT modified — see Section 7 for the ESC ruling.)
```

### Player↔NPC parity (rule #22)

The panel is a UI shell. Every gameplay effect — removing an item from the
character's bag/hands, inserting into the chest, removing from the chest,
placing in the character's hands — is performed by the **same two
`CharacterAction` subclasses NPCs already use through GOAP**:

| Direction              | Action class                          | NPC call site                                                                  |
| ---------------------- | ------------------------------------- | ------------------------------------------------------------------------------ |
| Character → chest      | `CharacterStoreInFurnitureAction`     | `GoapAction_GatherStorageItems.cs:334`, `GoapAction_DepositResources.cs:183/235` |
| Chest → character hands | `CharacterTakeFromFurnitureAction`   | `GoapAction_TakeFromSourceFurniture.cs:175`, `GoapAction_StageItemForPickup.cs:225` |

No new RPCs, no new server logic, no new replication paths. The UI just
constructs and queues the same actions.

### Server-authority boundary (rule #18)

```
Player click in UI (owner client)
  → interactor.CharacterActions.ExecuteAction(new CharacterStoreInFurnitureAction(...))
  → CharacterActions routes through its existing client→server RPC plumbing
  → Server runs OnApplyEffect:
        - Remove item from character (CharacterEquipment.GetInventory().RemoveItem
          OR HandsController.DropCarriedItem)
        - StorageFurniture.AddItem(item)  →  fires OnInventoryChanged server-side
  → StorageFurnitureNetworkSync.HandleServerInventoryChanged
  → NetworkList rewrite → all clients receive
  → Each client mirrors back via StorageFurniture.ApplySyncedSlotsFromNetwork
  → Each client's StorageFurniture.OnInventoryChanged fires locally
  → The owning player's UI_StorageFurniturePanel rebuilds its right side
```

Same shape for the take direction (`StorageFurniture.RemoveItem` server-side,
hand item placed in `HandsController` server-side; clients see the visual
through the existing equipment/hands sync).

### Existing-code disposition

- `Furniture.OnInteract(Character)` — kept as the universal tap-E entry.
  `StorageFurniture` is currently the only direct-`Furniture` subclass without
  an override; this spec adds one. Other `Furniture` subclasses (`BedFurniture`,
  chair, cashier, etc.) are unchanged.
- `FurnitureInteractable.Interact` — unchanged. Already routes E to
  `Furniture.OnInteract`.
- `PlayerController` E-key dispatcher — unchanged. Tap-E already reaches
  `Furniture.OnInteract` through `nearest.Interact(_character)` (line 378 in
  current code). PlayerController is NOT modified by this spec.
- `UI_Inventory` (existing renderer used by `CharacterEquipmentUI`) — reused
  as-is for the player bag side. No modification.
- `CharacterStoreInFurnitureAction` / `CharacterTakeFromFurnitureAction` —
  unchanged. They already validate lock + free-space + item presence, handle
  inventory-vs-hands removal/placement, and broadcast animations.
- `StorageFurniture._isLocked` — stays a stub. The action's existing
  `IsLocked` check still gates store/take, but no player UI surface flips it
  in this spec.

---

## Section 2 — Open path

### Override on `StorageFurniture`

```csharp
public override bool OnInteract(Character interactor)
{
    if (interactor == null) return false;

    // Owner + player gate. NPCs never reach this (FurnitureInteractable.Interact
    // is only triggered by PlayerController.HandleEKeyUp inside an IsOwner branch),
    // but defence in depth. Returns true so FurnitureInteractable still treats the
    // tap as accepted (even though we do nothing for NPCs).
    if (!interactor.IsOwner || !interactor.IsPlayer()) return true;

    if (PlayerUI.Instance == null) return true;
    PlayerUI.Instance.OpenStoragePanel(this, interactor);
    return true;
}
```

### Owner gate rationale

`Furniture.OnInteract` runs on the peer that called `Interact()`. The owner-only
gate is belt-and-suspenders:

- `PlayerController.HandleEKeyUp` (line 373-379) only runs inside `if (IsOwner)`.
- Even if a future code path reached `OnInteract` through a non-owner peer
  (server-side simulation tick, e.g.), `IsOwner` filters it out so the panel
  never opens on the wrong screen.

### Already-open guard

`PlayerUI.OpenStoragePanel` re-binds the panel to whatever storage the player
just tapped E on. If the panel is already showing chest A and the player taps
chest B, it switches to B. Same shape as `ToggleEquipmentUI` re-`Initialize` on
re-open.

---

## Section 3 — UI layout

```
┌─ UI_StorageFurniturePanel ──────────────────────────────────────────┐
│  [chest name label]                              [Close X]          │
│                                                                      │
│  ┌─ LEFT (character) ────────┐  ┌─ RIGHT (storage) ─────────────┐  │
│  │ Hands sub-slot:           │  │ Chest grid:                   │  │
│  │  [carried item icon/name] │  │  [slot 0] [slot 1] [slot 2]   │  │
│  │                           │  │  [slot 3] [slot 4] [slot 5]   │  │
│  │ Bag inventory grid:       │  │  ...                          │  │
│  │  [UI_Inventory reused]    │  │  (UI_StorageGrid)             │  │
│  └───────────────────────────┘  └───────────────────────────────┘  │
└──────────────────────────────────────────────────────────────────────┘
```

### Left side — character

- **Hands sub-slot:** single visible button. Renders `HandsController.CarriedItem`
  if non-null, else "(empty)". Click queues a store action for that item. Polled
  in `Update()` because hands carry has no event (precedent:
  `CharacterEquipmentUI.RefreshHandsButton`).
- **Bag inventory grid:** `UI_Inventory` reused. Initialized with
  `interactor.CharacterEquipment.GetInventory()` and `interactor`. Click on a
  slot queues a store action for that slot's item.
  - Caveat: `UI_Inventory.Initialize` must be (re)called on `OnEquipmentChanged`
    to surface bag swaps mid-session. Same pattern as `CharacterEquipmentUI.UpdateUI`.
- **Hidden states:**
  - If `interactor.CharacterEquipment.HaveInventory() == false` (no bag):
    grid root inactive, label "(no bag equipped)".

### Right side — storage

- **Chest grid:** `UI_StorageGrid`. Pool of buttons mirroring `UI_Inventory`'s
  approach. Bound to `target.ItemSlots` (read-only view). Click on a populated
  slot queues a take action. **Empty slots:** render the slot button visible
  but `interactable = false` and label "(empty)" — same shape as how
  `CharacterEquipmentUI.UpdateSlotText` handles a null `ItemInstance`.

### Layout numbers

The prefab itself is authored in Unity. Layout nuances (button size, grid
spacing, spacing between left/right blocks) match the existing
`UI_CharacterEquipment.prefab` family for stylistic consistency. Concrete pixel
sizes are a Unity-side authoring decision and are out of code-spec scope.

---

## Section 4 — Click handlers

### Store (left side click)

```csharp
private void OnPlayerSlotClicked(ItemInstance item)
{
    if (item == null) return;
    if (_target == null || _interactor == null) return;
    if (_interactor.CharacterActions == null) return;
    if (_interactor.CharacterActions.CurrentAction != null) return;  // busy guard

    var action = new CharacterStoreInFurnitureAction(_interactor, item, _target);
    _interactor.CharacterActions.ExecuteAction(action);
}
```

### Take (right side click)

```csharp
private void OnStorageSlotClicked(ItemInstance item)
{
    if (item == null) return;
    if (_target == null || _interactor == null) return;
    if (_interactor.CharacterActions == null) return;
    if (_interactor.CharacterActions.CurrentAction != null) return;  // busy guard

    var action = new CharacterTakeFromFurnitureAction(_interactor, item, _target);
    _interactor.CharacterActions.ExecuteAction(action);
}
```

### Click-busy guard

While `CharacterActions.CurrentAction != null`, the panel must reject further
clicks. Two implementations are acceptable; spec leaves it to plan:

- **(A) Per-button interactable flag**, refreshed each frame. Mirrors
  `CharacterEquipmentUI._dropHandsItemButton.interactable = (carried != null) && !busy;`.
- **(B) Reject inside the click handler with no visual.** Simpler, slightly worse
  UX feedback.

Default for the plan: **A** — visible feedback while a deposit/take animation
is playing.

### Re-validation lives in the action

The actions already re-validate inside `CanExecute` and `OnApplyEffect`. The UI
need not double-check `IsLocked`, `HasFreeSpaceForItem`, or slot presence. If
the chest fills between click and apply, the action logs a warning and no-ops
gracefully (existing behaviour, see `CharacterStoreInFurnitureAction.cs:53-63`).

---

## Section 5 — Refresh

### Subscriptions (lifetime = panel open → close)

```csharp
void Initialize(StorageFurniture target, Character interactor)
{
    Unsubscribe();  // idempotent

    _target = target;
    _interactor = interactor;

    _target.OnInventoryChanged += HandleStorageChanged;
    _interactor.CharacterEquipment.OnEquipmentChanged += HandleEquipmentChanged;

    // Initial paint
    HandleStorageChanged();
    HandleEquipmentChanged();
}

void Unsubscribe()
{
    if (_target != null) _target.OnInventoryChanged -= HandleStorageChanged;
    if (_interactor != null && _interactor.CharacterEquipment != null)
        _interactor.CharacterEquipment.OnEquipmentChanged -= HandleEquipmentChanged;
}

void OnDisable() => Unsubscribe();
void OnDestroy() => Unsubscribe();
```

### Hands polling

```csharp
void Update()
{
    if (_interactor == null) return;
    var hands = _interactor.CharacterVisual?.BodyPartsController?.HandsController;
    var carried = hands != null ? hands.CarriedItem : null;
    if (carried != _lastHandsItem)
    {
        _lastHandsItem = carried;
        RepaintHandsSlot();
    }
    // Out-of-zone close (see Section 7)
    PollAutoClose();
}
```

### Cross-peer parity

- **Owner-player taps E, deposits an item:** owner-client posts the action via
  `CharacterActions.ExecuteAction` (existing client→server RPC). Server applies,
  fires `StorageFurniture.OnInventoryChanged`, `StorageFurnitureNetworkSync`
  rewrites the NetworkList, every client (including the owner) mirrors. The
  owner's `UI_StorageFurniturePanel` repaints from `HandleStorageChanged`.
- **Another peer's NPC deposits while the owner-player has the panel open:**
  same chain — server-side `OnInventoryChanged` propagates to the owner via
  the NetworkList → `ApplySyncedSlotsFromNetwork` → local `OnInventoryChanged`
  → panel repaints in real time.
- **Same chest, two players in different sessions:** out of scope —
  multiplayer in-game is single-shard, no cross-shard.

---

## Section 6 — Close conditions

The panel closes when ANY of these become true:

| Condition                                | How detected                                                                                  |
| ---------------------------------------- | --------------------------------------------------------------------------------------------- |
| ESC pressed                              | `UI_StorageFurniturePanel.Update` reads ESC directly (see Section 7 for rule #33 ruling)      |
| Player walked out of interaction zone    | Polled each frame: `target.GetComponent<FurnitureInteractable>().IsCharacterInInteractionZone(interactor)` (rule per memory `feedback_interactable_proximity_api`) |
| Storage despawned                        | `if (_target == null) Close();` in `Update` — covers building destruction / map hibernation   |
| Character incapacitated                  | Subscribe to `Character.OnIncapacitated` (existing event)                                     |
| Character entered combat                 | Subscribe to `CharacterCombat.OnCombatStateChanged` (existing) — match equipment-UI behaviour   |
| Player presses E on a different chest    | `OpenStoragePanel` re-`Initialize`s with the new target — no separate "close" needed          |

`Close()` itself sets `gameObject.SetActive(false)` and unsubscribes.

---

## Section 7 — ESC handling lives on the panel (rule #33 ruling)

Rule #33 mandates that input driving the *player character* (movement, combat,
targeting, action queueing) lives in `PlayerController.cs`. It explicitly
excludes input that targets the UI itself:

> "UI widgets ... may read input that targets *the UI itself* (opening menus,
> navigating panels, text fields) ... but as soon as the input result is 'the
> player character should do X,' it must be queued via PlayerController."

ESC closing the storage panel = UI navigation, not character control. So ESC
handling lives **inside `UI_StorageFurniturePanel.Update()`**, not in
`PlayerController`. This matches every existing UI consumer of ESC in the
codebase (`PauseMenuController`, `BuildingPlacementManager`,
`CropPlacementManager`, `UI_HarvestInteractionMenu`,
`UI_DisplayTextReader`, `DevModeManager`) — no centralised dispatcher exists,
and creating one for a single new panel is out of scope.

```csharp
// Inside UI_StorageFurniturePanel.Update():
if (Input.GetKeyDown(KeyCode.Escape))
{
    Close();
    return;
}
```

This keeps `PlayerController.cs` untouched and follows the established UI
input pattern.

---

## Section 8 — Networking validation (rule #19)

Required scenarios:

| Scenario                              | Expectation                                                                                                                                 |
| ------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------- |
| Host taps E on a chest                | Host opens panel locally. Click → server-side action runs locally (host == server). Replicated to clients via existing sync. Other players' screens unaffected. |
| Client taps E on a chest              | Client opens panel locally. Click → `CharacterActions.ExecuteAction` triggers existing client→server RPC. Server runs action. State replicates back. Client's panel repaints. Host's UI unaffected. |
| Two clients tap E on the same chest   | Each client opens its own panel. Each click queues an independent action. Server applies them sequentially via `CharacterActions` queue. Both panels repaint as state replicates. No race because the action's `OnApplyEffect` re-validates `HasFreeSpaceForItem` / `IsLocked` / item presence at apply time. |
| NPC deposits while a player has panel open | NPC's GOAP queues `CharacterStoreInFurnitureAction` server-side. Same `OnInventoryChanged` → NetworkList → client mirror chain fires. Player's panel repaints. |
| Late-joining client opens a chest     | `StorageFurnitureNetworkSync.OnNetworkSpawn` already runs `ApplyFullStateOnClient` for late joiners. Panel reads fully-populated `target.ItemSlots`. |

No new RPCs introduced. All multiplayer behaviour is inherited from existing
infrastructure.

---

## Section 9 — Files

### Create

| Path                                                                | Lines (est) | Purpose                                                                          |
| ------------------------------------------------------------------- | ----------- | -------------------------------------------------------------------------------- |
| `Assets/Scripts/UI/WorldUI/UI_StorageFurniturePanel.cs`             | ~180        | Panel script: lifecycle, subscriptions, click handlers, polling                  |
| `Assets/Scripts/UI/WorldUI/UI_StorageGrid.cs`                       | ~80         | Slot-grid renderer for `IReadOnlyList<ItemSlot>` (chest side)                    |
| `Assets/UI/Player HUD/UI_StorageFurniturePanel.prefab`              | (Unity)     | Authored prefab — placed under PlayerHUD canvas alongside `UI_CharacterEquipment` |
| `wiki/systems/storage-furniture-ui.md`                              | ~60         | New wiki page per rule #29b — architecture-only, links to action skills          |

### Modify

| Path                                                          | Change                                                                                            |
| ------------------------------------------------------------- | ------------------------------------------------------------------------------------------------- |
| `Assets/Scripts/World/Furniture/StorageFurniture.cs`          | Add `public override bool OnInteract(Character interactor)`                                        |
| `Assets/Scripts/UI/PlayerUI.cs`                               | Add `_storagePanel` field, `OpenStoragePanel`, `CloseStoragePanel`                                 |
| `wiki/systems/storage-furniture.md`                           | Append change-log entry referencing the new UI page; refresh `depended_on_by` if it lists UIs      |

### Touch (event-driven only — no API change)

- `wiki/INDEX.md` — regenerate via `/map` after wiki page added.

---

## Section 10 — Out of scope

- **Storage lock / key system.** `_isLocked` stays a stub. Future spec can
  mirror `DoorLock` + `KeyInstance` if desired.
- **Drag-and-drop.** Click-to-transfer only.
- **Bulk store / bulk take.** Each click = one action = one item. NPCs deposit
  one item per `CharacterAction`; player parity demands the same.
- **Stack splitting.** Items are 1-instance-per-slot today (see
  `StorageFurniture.AddItem` strict-first slot priority); no quantity field
  exists on `ItemInstance` for arbitrary items.
- **Showing chest-side stack counts vs slot counts.** Slots are 1-item — no
  count to show.
- **Re-arranging slots inside the chest.** Chest is bag-style; slot priority
  belongs to the server.
- **Showing the chest's storage role (Tool / Sell-shelf / etc.).** That's a
  Management Panel concern.
- **Mobile / gamepad input.** Mouse + ESC only. Gamepad routing handled by
  the input remapping layer (out of scope).
- **Animations.** The existing `CharacterAction.OnStart` already triggers
  drop / pickup anims. No new VFX/SFX in this spec.

---

## Section 11 — Edge cases & failure modes

| Edge case                                              | Handling                                                                                            |
| ------------------------------------------------------ | --------------------------------------------------------------------------------------------------- |
| Player has no bag equipped                             | Bag grid hidden / labelled "(no bag equipped)". Hands sub-slot still works for hands deposit.       |
| Player's hands are empty                               | Hands sub-slot disabled, label "(empty)".                                                           |
| Chest is full when player clicks Store                 | Action's `CanExecute` returns false → `ExecuteAction` returns false → no-op. UI button could pre-disable but server is the truth. |
| Chest item disappears between click and apply (NPC race) | Action's `OnApplyEffect` re-validates and warns. UI repaints from the next `OnInventoryChanged`.    |
| Player walks out of zone while panel is open           | Auto-close (Section 6). In-flight action continues server-side regardless.                          |
| Player's character incapacitated mid-deposit            | Existing actions handle: `CharacterCombat.HandleIncapacitated` drops hands; deposit aborts.        |
| Building hibernates (no players nearby)                 | Storage despawns → `_target == null` → panel closes.                                               |
| Two clients race-click the last item from chest        | First server-side `OnApplyEffect` succeeds, second's `RemoveItem` returns false → second client's hands stay free, no extraction. UI repaints. |
| Bag swap mid-session (player picks up new bag)         | `OnEquipmentChanged` fires → `UI_Inventory` re-`Initialize` with the new inventory.                |
| Storage role flipped to "Tool storage" by owner mid-open | Tool-stamping logic in `StorageFurniture.AddItem` already runs server-side; UI need not change.    |

---

## Section 12 — Skill / agent / wiki updates (rule #28 / #29 / #29b)

### Skills (`.agent/skills/`)

- **`character-actions/SKILL.md`** — no change. The actions used (`CharacterStoreInFurnitureAction`,
  `CharacterTakeFromFurnitureAction`) are already documented.
- **`building-furniture/SKILL.md`** — append a "Storage Furniture player UI"
  paragraph if the skill currently documents storage UI as absent. Plan-phase
  to confirm.

### Agents (`.claude/agents/`)

- **`building-furniture-specialist.md`** — append knowledge of the new player
  UI surface. The agent already covers `StorageFurniture` + slots; it needs to
  know the player-UI hook lives on `Furniture.OnInteract` override + `PlayerUI`.
- **No new agent.** This feature is too small to warrant one.

### Wiki (`wiki/systems/`)

- **CREATE** `wiki/systems/storage-furniture-ui.md`. Architecture-only:
  Purpose, Responsibilities, Key classes (PlayerUI, UI_StorageFurniturePanel,
  UI_StorageGrid, StorageFurniture override), Public API, Data flow (open →
  click → action → server → replicate → repaint), Dependencies (CharacterAction
  pipeline, StorageFurnitureNetworkSync, UI_Inventory), State & persistence
  (none — UI is ephemeral), Gotchas (owner gate, hands polling, no double-RPC),
  Open questions (none), Change log, Sources (link to action SKILLs, link to
  this spec).
- **UPDATE** `wiki/systems/storage-furniture.md` — bump `updated:`, append
  change log entry, refresh `depended_on_by` if it tracks UI consumers.
- **REGENERATE** `wiki/INDEX.md` via `/map` slash command after the new page
  is added.

---

## Acceptance criteria

A change passes review when:

1. **Trigger.** Tapping E within a `StorageFurniture`'s `InteractionZone` opens
   the panel **on the local owner-player's screen only**.
2. **Layout.** Left side shows hands sub-slot + bag inventory grid for the
   interactor. Right side shows the chest's slots.
3. **Store.** Clicking a populated bag slot or the hands sub-slot queues
   `CharacterStoreInFurnitureAction(interactor, item, target)` and the item
   ends up in the chest, removed from inventory/hands.
4. **Take.** Clicking a populated chest slot queues
   `CharacterTakeFromFurnitureAction(interactor, item, target)` and the item
   ends up in the interactor's hands, removed from the chest.
5. **NPC parity.** A running NPC GOAP cycle (e.g. `JobLogisticsManager`
   gathering loose items into the same chest) continues to function with the
   panel open. Both UI and NPC see consistent chest state via existing
   replication.
6. **Close.** ESC, walking out of the interaction zone, character incapacitated,
   storage despawn — each closes the panel cleanly without leaking event
   subscriptions.
7. **Multiplayer.** All five scenarios in [Section 8](#section-8--networking-validation-rule-19)
   pass without divergence between host and clients.
8. **No regressions.** The existing GOAP store / take paths
   (`GoapAction_GatherStorageItems`, `GoapAction_DepositResources`,
   `GoapAction_TakeFromSourceFurniture`, `GoapAction_StageItemForPickup`)
   continue to work without change.
9. **Documentation.** New wiki page exists; `building-furniture-specialist`
   agent updated; existing storage-furniture wiki page has a change-log entry.

---

## Hand-off

Spec approved → invoke `superpowers:writing-plans` to produce
`docs/superpowers/plans/2026-05-09-storage-furniture-player-ui-plan.md`.
