# Shop buy panel + interact deduplication — design

> Two bugs blocking the shop / cashier flow in multiplayer (branch `multiplayyer`).
> Bundled into a single spec because shipping one without the other leaves the
> cashier flow broken.

**Status:** brainstorming complete; ready for `superpowers:writing-plans`.

**Author:** generated through guided brainstorming, 2026-05-09.

**Scope decision:** ONE combined spec. Two specs were considered; bundled because
the cashier flow is unusable until both ship — the dedup alone leaves the player
with no buy UI, the prefab alone double-fires `CharacterAction_BuyFromShop`.

**Out of scope (already deferred to its own session):** Inventory and
`HandsController.CarriedItem` server→client replication.

**Out of scope (intentional):** any defense-in-depth idempotency guard inside
`InteractableObject.Interact` — we fix the root cause in the input layer; we do
not paper over it in the contract surface.

---

## Section 1 — Problem statement

### Bug #1 — UI_ShopBuyPanel prefab missing

`UI_ShopBuyPanel.Open()` calls `Resources.Load<GameObject>("UI/UI_ShopBuyPanel")`,
which returns null because no prefab exists at that path. Code path:

```
CharacterAction_BuyFromShop.OnStart
  → CashierNetSync.OpenBuyPanelClientRpc
    → UI_ShopBuyPanel.Open
      → Resources.Load → null → LogError → bail
```

The C# class is fully written
([UI_ShopBuyPanel.cs](../../Assets/Scripts/UI/Shop/UI_ShopBuyPanel.cs),
[UI_ShopBuyRow.cs](../../Assets/Scripts/UI/Shop/UI_ShopBuyRow.cs)). Only the
prefab assets are missing.

### Bug #2 — Furniture.OnInteract fires twice on a single E press

Two parallel `Input.GetKeyUp(KeyCode.E)` paths each call `.Interact()` on every
tap:

- [PlayerInteractionDetector.cs:243-248](../../Assets/Scripts/Character/PlayerInteractionDetector.cs)
  → `ExecuteNormalInteract` (line 275) → line 302
  `_currentInteractableObjectTarget.Interact(Character)`.
- [PlayerController.cs:373-379](../../Assets/Scripts/Character/CharacterControllers/PlayerController.cs)
  → `HandleEKeyUp` → `nearest.Interact(_character)`.

User-confirmed evidence in logs (Crate and Cashier both):

```
[Furniture] ... utilise Crate.   ← from PlayerController path
[Furniture] ... utilise Crate.   ← from PlayerInteractionDetector path
```

Storage furniture happens to be tolerant — its panel `Initialize` re-binds
cleanly. The cashier is not — two `CharacterAction_BuyFromShop` actions fire,
which can produce two `OpenBuyPanelClientRpc` invocations and (if both reach
commit) a double-debit.

The detector additionally has `Input.GetKeyDown / GetKey` reads at lines 199 and
222. Per Project Rule #33:

> All player input that controls the player character lives in
> [PlayerController.cs]. Do not scatter `Input.GetKey…` /
> `Input.GetMouseButton…` calls for player-character control across HUD scripts,
> UI managers, ad-hoc MonoBehaviours, or other character subsystems.

The detector is the only Rule #33 violator outside dev tools and UI panels (the
latter being valid carve-outs for input that targets the UI itself).
`Input.GetKey` callsites elsewhere in the codebase have been audited and are
permitted: UI managers (`PlayerUI`, `PauseMenuController`, `UI_ChatBar`),
dev-mode tooling (`DevModeManager`, `DevSpawnModule`, `DevSelectionModule`),
placement-mode UI (`CropPlacementManager`, `BuildingPlacementManager`,
`FurniturePlacementManager`), dialogue UI (`DialogueManager`,
`UI_HarvestInteractionMenu`), and read-only HUD widgets.

`.Interact()` callsites elsewhere are non-input-driven: `MapTransitionDoor.cs:155`
(programmatic action callback) and `CharacterParty.cs:1143` (party-leader
auto-interact). They remain untouched.

---

## Section 2 — Architecture

### 2.1 Half A — Interact deduplication

```
┌──────────────────────┐    LateUpdate           ┌──────────────────────────┐
│   PlayerController   │ ◄─────────────────────  │ PlayerInteractionDetector│
│ (rule #33 owner)     │                         │  (proximity + prompt)    │
│  Update()            │                         │  OnTriggerEnter/Exit     │
│   ├ HandleEKeyDown   │                         │  UpdateClosestTarget     │
│   ├ HandleEKeyHeld ──┼── SetPromptHoldProgress─►│  drives prompt fill     │
│   │   └ TriggerHoldMenu◄────── helper ─────────┤  CurrentTarget getter   │
│   └ HandleEKeyUp ────┼── TriggerTapInteract ──►│  IsTargetInRange         │
└──────────────────────┘                         └──────────────────────────┘
```

Single owner gate (`PlayerController.Update`'s `IsOwner` check); a single chat-
input gate already at PlayerController:149-151; a single dev-mode suppression
gate. Detector reads zero `Input.*` for player-character control.

### 2.2 Half B — Shop buy panel prefab

```
Resources/UI/UI_ShopBuyPanel.prefab   ← Resources.Load contract
   [Canvas overrideSorting=true sortingOrder=50, GraphicRaycaster, UI_ShopBuyPanel script]
   └─ HeaderRow + ScrollView(Content=_rowsParent) + FooterRow
        _rowPrefab → Resources/UI/UI_ShopBuyRow.prefab
```

UI_ShopBuyPanel.cs gains the same defensive `Awake` Canvas/raycaster guard that
UI_StorageFurniturePanel uses (storage-panel lesson, May 9 session).

---

## Section 3 — Half A: Interact deduplication

### 3.1 PlayerInteractionDetector — what it loses

Delete the `Update()` E-key block (lines 199-248):

- `Input.GetKeyDown(KeyCode.E)` block (lines 199-220) — selected-but-out-of-range
  auto-navigate decision and the `isHoldingE = true` start.
- `Input.GetKey(KeyCode.E)` block (lines 222-241) — hold-time accumulation,
  prompt fill drive, threshold-reached `_playerUI.OpenInteractionMenu` /
  `ExecuteNormalInteract`.
- `Input.GetKeyUp(KeyCode.E)` block (lines 243-248) — release-before-threshold
  `ExecuteNormalInteract`.
- The `eHoldTime` / `isHoldingE` fields and `HOLD_THRESHOLD` const.
- The chat-input-field guard at lines 176-181 (already in PlayerController).
- The `EnsurePlayerUI` / `EnsureTargeting` helpers (PlayerController concerns
  now — PlayerController already has its own `EnsureTargeting`).

Move `UpdateClosestTarget()` invocation from `Update` into `LateUpdate` so
proximity tracking runs without sitting next to deleted input code. The method
itself is unchanged.

### 3.2 PlayerInteractionDetector — what it keeps

| Member | Visibility | Notes |
|---|---|---|
| `OnTriggerEnter` / `OnTriggerExit` | protected override | unchanged |
| `nearbyInteractables` (List) | private | unchanged |
| `UpdateClosestTarget` | private | now called from `LateUpdate` |
| `CreatePrompt` / `DestroyPrompt` | private | unchanged |
| `HandleInteractionStateChanged`, `HandlePlayerTurnStarted`, `HandlePlayerTurnEnded`, `HandlePlayerTurnTimerUpdated` | private | event-driven dialogue-menu open/close — not input-driven, untouched |

### 3.3 PlayerInteractionDetector — new public API

```csharp
public InteractableObject CurrentTarget => _currentInteractableObjectTarget;

// Already exists; signature unchanged.
public bool IsTargetInRange(InteractableObject target);

// Renamed from TriggerInteract. Same body (the dialogue-NPC freeness gate
// + EndInteraction shortcut + target.Interact(Character) at line 302).
// PlayerInteractCommand callers update to the new name in the same pass.
public void TriggerTapInteract(InteractableObject target);

// New. Returns true if a hold-menu was opened (so the caller knows to set
// _eMenuOpened). Encapsulates the GetHoldInteractionOptions → OpenInteractionMenu
// path that previously lived inside the deleted Update() block.
public bool TriggerHoldMenu(InteractableObject target);

// New. Single setter for the prompt-fill bar. Drives currentPromptComponent
// .SetFillAmount on every PlayerController.HandleEKeyHeld tick.
public void SetPromptHoldProgress(float t01);
```

### 3.4 PlayerController — new field

```csharp
[SerializeField] private PlayerInteractionDetector _detector;

protected override void Awake()
{
    base.Awake();
    if (_detector == null) _detector = GetComponentInChildren<PlayerInteractionDetector>();
}
```

Matches the Character facade auto-resolution pattern. PlayerInteractionDetector
already calls `Character.GetComponent<PlayerController>()` at line 204 — they are
already aware of each other; we are just formalising the back-reference.

### 3.5 PlayerController — HandleEKeyDown (unchanged)

Existing logic at lines 318-356 stays as-is. It already correctly handles
placement-active items (seed, watering can), placement-active mode (no-op),
consumables, and falls through to the held/up dispatch. No change.

### 3.6 PlayerController — HandleEKeyHeld (extended)

```csharp
private void HandleEKeyHeld()
{
    if (_eMenuOpened) return;

    float t01 = (UnityEngine.Time.unscaledTime - _eHeldStartTime) / E_HOLD_THRESHOLD;
    _detector?.SetPromptHoldProgress(Mathf.Clamp01(t01));
    if (t01 < 1f) return;

    // Priority A: harvestable-specific menu (existing UX).
    var harvestable = GetNearestVisibleHarvestable();
    if (harvestable != null)
    {
        MWI.UI.Interaction.UI_HarvestInteractionMenu.Open(_character, harvestable, OnInteractionMenuClosed);
        _eMenuOpened = true;
        return;
    }

    // Priority B: generic interactable hold-menu (was in detector).
    var generic = _detector?.CurrentTarget;
    if (generic != null && _detector.TriggerHoldMenu(generic))
        _eMenuOpened = true;
}
```

`GetNearestVisibleHarvestable` (line 388) is awareness-based and stays. It is
not an input-read.

### 3.7 PlayerController — HandleEKeyUp (replaced)

```csharp
private void HandleEKeyUp()
{
    if (_eMenuOpened) return;
    _detector?.SetPromptHoldProgress(0f);

    EnsureTargeting();
    var selected = _targeting?.SelectedInteractable;
    var target = selected ?? _detector?.CurrentTarget;
    if (target == null) return;

    // Selected target out of range → enqueue auto-nav command (existing UX,
    // previously triggered from PlayerInteractionDetector.Update line 201-211).
    if (selected != null && _detector != null && !_detector.IsTargetInRange(selected))
    {
        SetOrder(new MWI.CharacterControllers.Commands.PlayerInteractCommand(selected, _detector));
        return;
    }

    _detector.TriggerTapInteract(target);
}
```

Delete `GetNearestVisibleInteractable` from PlayerController (line 407) —
`_detector.CurrentTarget` is the single source of truth (it owns the prompt the
user sees). `GetNearestVisibleHarvestable` stays (used by HandleEKeyHeld).

### 3.8 PlayerInteractCommand — rename callsite

`PlayerInteractCommand`'s arrival callback currently calls
`_detector.TriggerInteract(target)`. After the rename it calls
`_detector.TriggerTapInteract(target)`. Same body, new name. No behaviour change.

### 3.9 Cross-cutting

- **Dev-mode suppression**: `DevModeManager.SuppressPlayerInput` is checked
  inside both today; only PlayerController needs it after dedup.
- **Chat-input-field guard**: PlayerController:149-151 already skips on active
  TMP_InputField. Detector's copy at lines 176-181 deletes with the rest of the
  block — no behaviour lost.
- **PlayerInteractionDetector.OnDestroy**: already unsubscribes the dialogue
  menu event handlers (Rule #16). No change.

---

## Section 4 — Half A: Network validation pass

Each `InteractableObject` subclass below must fire exactly one effect per E tap
on every multiplayer scenario (Rule #19): Host↔Client + Client↔Client +
Host/Client↔NPC.

| Subclass | Tap effect | Hold effect | Verification scenario |
|---|---|---|---|
| `FurnitureInteractable` (chest, shelf, barrel, wardrobe) | Open `UI_StorageFurniturePanel` | none today | Tap E on Crate: one panel-open, one Interact log |
| `FurnitureInteractable` (cooking, crafting, seating, time clock) | Use furniture → action queued | optional `GetHoldInteractionOptions` | Tap E: one action queued |
| `CashierInteractable` | `RequestStartBuyServerRpc` | none | Tap E: one ServerRpc, one OpenBuyPanelClientRpc |
| `BuildingInteractable` (construction site, building doors) | `RequestStartFinishConstructionServerRpc` / enter | none today | Tap E on construction site: one progress action |
| `Harvestable` | full harvest action | `UI_HarvestInteractionMenu` (PlayerController priority A) | Tap E: one harvest action; Hold E: one menu open |
| `CharacterInteractable` (dialogue NPC) | `StartInteraction` (with freeness gate) | dialogue options menu | Tap E on free NPC: one StartInteraction; tap when busy: one toast |
| `MapTransitionDoor` | enqueue door action | none | Tap E on door: one transition |

Acceptance criteria:

- For each subclass, a fresh debug counter or log added during the validation
  pass shows exactly one entry per E tap.
- For multiplayer scenarios, the same counter on both Host and Client shows
  exactly one each per local tap (and zero on the non-tapper).
- For Host↔NPC and Client↔NPC: NPC-driven `.Interact()` callsites
  (`MapTransitionDoor.cs:155`, `CharacterParty.cs:1143`) continue to work — they
  don't go through the input layer at all.

The validation pass uses the `network-validator` agent.

---

## Section 5 — Half B: UI_ShopBuyPanel.cs Awake guard

Add at the top of the class:

```csharp
private void Awake()
{
    var canvas = GetComponent<UnityEngine.Canvas>();
    if (canvas == null) canvas = gameObject.AddComponent<UnityEngine.Canvas>();
    canvas.overrideSorting = true;
    canvas.sortingOrder = 50;

    if (GetComponent<UnityEngine.UI.GraphicRaycaster>() == null)
        gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
}
```

Mirrors [UI_StorageFurniturePanel.cs:58-71](../../Assets/Scripts/UI/WorldUI/UI_StorageFurniturePanel.cs).
Reasoning: a Resources.Load → Instantiate panel needs its own Canvas +
GraphicRaycaster to receive clicks regardless of its parent's setup. Storage
panel hit this exact bug; the shop panel will too without the guard.

---

## Section 6 — Half B: Prefab assets

### 6.1 UI_ShopBuyPanel.prefab

Path (load-bearing — matches `Resources.Load<GameObject>("UI/UI_ShopBuyPanel")`):

`Assets/Resources/UI/UI_ShopBuyPanel.prefab`

Hierarchy:

```
UI_ShopBuyPanel
   [RectTransform stretch]
   [Canvas overrideSorting=true sortingOrder=50]
   [GraphicRaycaster]
   [UI_ShopBuyPanel script]
├─ Panel  [Image bg, raycastTarget=true]
│  ├─ HeaderRow  [HorizontalLayoutGroup]
│  │  ├─ Title         (TMP_Text → _titleText)
│  │  └─ CancelButton  (Button   → _cancelButton)
│  ├─ ScrollView  [Image raycastTarget=false, ScrollRect]
│  │  └─ Viewport  [Image raycastTarget=false, Mask]
│  │     └─ Content  [Transform → _rowsParent, VerticalLayoutGroup, ContentSizeFitter]
│  └─ FooterRow  [HorizontalLayoutGroup]
│     ├─ WalletText    (TMP_Text → _walletText)
│     ├─ TotalText     (TMP_Text → _totalText)
│     └─ ConfirmButton (Button   → _confirmButton)
```

ScrollView and Viewport `raycastTarget=false` is the storage-panel lesson — a
raycast-blocking ScrollView Image swallows slot/button clicks.

Wired SerializeFields on root:

| Field | Target |
|---|---|
| `_titleText`     | HeaderRow/Title (TMP_Text) |
| `_walletText`    | FooterRow/WalletText (TMP_Text) |
| `_totalText`     | FooterRow/TotalText (TMP_Text) |
| `_confirmButton` | FooterRow/ConfirmButton (Button) |
| `_cancelButton`  | HeaderRow/CancelButton (Button) |
| `_rowsParent`    | ScrollView/Viewport/Content (Transform) |
| `_rowPrefab`     | `Assets/Resources/UI/UI_ShopBuyRow.prefab` (UI_ShopBuyRow component) |

### 6.2 UI_ShopBuyRow.prefab

Path: `Assets/Resources/UI/UI_ShopBuyRow.prefab`.

Hierarchy:

```
UI_ShopBuyRow  [Button, UI_ShopBuyRow script, HorizontalLayoutGroup]
├─ Icon          (child *named exactly "Icon"*, Image → _icon)
├─ NameText      (TMP_Text → _nameText)
├─ PriceText     (TMP_Text → _priceText)
├─ StockText     (TMP_Text → _stockText)
├─ MinusButton   (Button   → _minusButton)
├─ QuantityInput (TMP_InputField → _quantityInput)
├─ PlusButton    (Button   → _plusButton)
└─ SubtotalText  (TMP_Text → _subtotalText)
```

The "Icon" name is a convention match with
[UI_StorageGrid.cs:144-146](../../Assets/Scripts/UI/WorldUI/UI_StorageGrid.cs)
so any future grid pool reusing the row doesn't trip the
`GetComponentInChildren<Image>`-finds-bg bug.

### 6.3 Author method

Build via the existing
`mcp__ai-game-developer__assets-prefab-create` +
`mcp__ai-game-developer__gameobject-component-add` +
`mcp__ai-game-developer__gameobject-component-modify` tool chain — same path
that produced the storage panel prefabs in the May 9 session. No
builder-utility C# script needed.

### 6.4 Visual reference

Layout from the original shop spec at
`docs/superpowers/specs/2026-05-07-shop-buy-sell-system-design.md` Section 5:

```
┌───────────────────────────────────────────────┐
│  Shop: <buildingName>          [X cancel]     │
├───────────────────────────────────────────────┤
│  [icon] Bread             8 g  [3 in stock]   │
│           [-] [ 2 ] [+]              =  16 g  │
│  [icon] Apple             3 g  [12 in stock]  │
│           [-] [ 0 ] [+]              =   0 g  │
│  …                                            │
├───────────────────────────────────────────────┤
│  Wallet: 50 g     Total: 16 g     [Confirm]   │
└───────────────────────────────────────────────┘
```

---

## Section 7 — Files

### Modified

- `Assets/Scripts/Character/CharacterControllers/PlayerController.cs`
  - new field `_detector` resolved in `Awake`
  - extend `HandleEKeyHeld` to dispatch generic hold-menu and drive prompt fill
  - replace `HandleEKeyUp` with consolidated tap dispatch (selected vs proximity, in-range vs out-of-range)
  - delete `GetNearestVisibleInteractable`
  - keep `GetNearestVisibleHarvestable`
- `Assets/Scripts/Character/PlayerInteractionDetector.cs`
  - delete the `Update()` E-key block, the `eHoldTime` / `isHoldingE` /
    `HOLD_THRESHOLD` fields, the chat-input-field guard, the `EnsurePlayerUI` /
    `EnsureTargeting` helpers
  - move `UpdateClosestTarget()` invocation into `LateUpdate`
  - rename `TriggerInteract` → `TriggerTapInteract`
  - add `CurrentTarget` getter, `TriggerHoldMenu`, `SetPromptHoldProgress`
- `Assets/Scripts/UI/Shop/UI_ShopBuyPanel.cs`
  - add `Awake` Canvas + GraphicRaycaster guard
- `Assets/Scripts/Character/CharacterControllers/Commands/PlayerInteractCommand.cs`
  - update the call from `TriggerInteract` to `TriggerTapInteract`

### Added

- `Assets/Resources/UI/UI_ShopBuyPanel.prefab`
- `Assets/Resources/UI/UI_ShopBuyRow.prefab`

### Deleted

*(none — no `InteractableObject` subclass changes)*

### Documentation (Project Rules #28, #29, #29b)

- `.agent/skills/character/SKILL.md` (or whichever player-input SKILL exists)
  — change-log entry: PlayerController owns all E input; detector exposes
  helper API
- `.agent/skills/shop-system/SKILL.md` — change-log entry: UI_ShopBuyPanel
  prefab now exists at `Resources/UI/UI_ShopBuyPanel.prefab`
- `wiki/systems/player-input.md` (or interactable-system page) — change-log
  entry, refresh `Public API` section with new detector helpers, refresh
  `Gotchas` with the rule #33 enforcement
- `wiki/systems/shop-system.md` — change-log entry: prefab path noted
- `wiki/gotchas/double-interact-rule-33-violation.md` — new gotcha so this
  doesn't regress (with the diagnostic log signature from the user's report)
- Evaluate creating or extending `.claude/agents/` per Project Rule #29 — likely
  a refresh to `character-system-specialist.md` (E-input flow) is enough; no
  new agent warranted.

---

## Section 8 — Testing scenarios

Each verified on Host↔Client + Client↔Client + Host/Client↔NPC (Rule #19):

1. **Tap E on Crate (storage)** — exactly one `UI_StorageFurniturePanel` open call; exactly one `[Furniture] ... utilise Crate.` log.
2. **Tap E on Cashier** — exactly one `CharacterAction_BuyFromShop` enqueue; exactly one `OpenBuyPanelClientRpc` invocation.
3. **Hold E on Harvestable** — opens `UI_HarvestInteractionMenu`; no tap-Interact fires; prompt fill animates 0→1.
4. **Hold E on dialogue-capable CharacterInteractable** — opens dialogue options menu; no tap-Interact fires.
5. **Tap E on selected-but-out-of-range target** — `PlayerInteractCommand` enqueues; on arrival, exactly one Interact fires.
6. **Walk away from target during hold** — prompt fill resets to 0; no menu opens; `SetPromptHoldProgress(0f)` invoked.
7. **ESC during hold** — cancels cleanly (no Interact, no menu).
8. **Tap E on Bed / chair / time clock / map door** — each fires one effect per tap.
9. **Shop buy panel opens on cashier tap** — ScrollView and Viewport raycastTarget=false; row buttons receive clicks; +/- and confirm/cancel all functional.
10. **Shop buy panel renders at sortingOrder 50** — programmatic Canvas overrides apply when prefab has its own; nested HUD doesn't override.
11. **Shop buy panel cancel/confirm round-trip** — cashier lock acquired exactly once and released exactly once; till credited exactly once on confirm.
12. **Late joiner sees cashier till + lock state** — existing `CashierNetSync` replication unchanged.
13. **NPC-driven `.Interact()` paths still work** — `MapTransitionDoor.cs:155` (programmatic) and `CharacterParty.cs:1143` (party leader) untouched.
14. **Two players in same scene** — each player's `_detector.CurrentTarget` is local; no cross-talk; remote players see no spurious prompts.

---

## Section 9 — Risks & mitigations

| Risk | Mitigation |
|---|---|
| Detector's `LateUpdate` proximity update introduces one-frame lag for the closest selection | Acceptable — proximity tracking is event-driven (OnTriggerEnter/Exit); `UpdateClosestTarget` is just a sort over a tiny set. One frame of lag is invisible to humans. |
| `_targeting` reference resolution race in PlayerController.HandleEKeyUp | `EnsureTargeting()` already exists in PlayerController and is called every Update; no behaviour change. |
| Some `InteractableObject` subclass depends on the detector's dialogue-NPC freeness gate | Gate is preserved verbatim inside `TriggerTapInteract` — relocated, not removed. Section 4 validation pass exercises every subclass. |
| `UI_ShopBuyPanel.cs` `_rowPrefab` SerializeField unwired after prefab creation | Author panel and row in the same MCP session; wire `_rowPrefab` at panel-creation time before saving. Section 6.1 wiring table is the contract. |
| Canvas `sortingOrder=50` collides with another panel | Storage panel uses 50 too; the two panels never coexist (mutually exclusive interactions). If observed in QA, raise shop panel to 60. Out of scope to refactor a global sorting registry. |
| Renaming `TriggerInteract` → `TriggerTapInteract` breaks an unknown caller | Only callers per grep: detector internal, `PlayerInteractCommand`, and (after dedup) PlayerController. All updated atomically. |
| Some hold-menu UX (the prompt fill) becomes one frame slower because PlayerController calls `SetPromptHoldProgress` after the input read instead of inside it | Acceptable — same frame, different ordering. Visually identical. |
| Detector's `_currentInteractableObjectTarget` and PlayerController's awareness-based `GetNearestVisibleInteractable` could disagree on which target is "closest" | Detector wins. The detector owns the prompt the user is looking at; tap-E hits what the prompt shows. `GetNearestVisibleInteractable` is deleted — divergence elimination, not a regression. |

---

## Section 10 — Open dependencies

None. Both halves are independent of any pending parallel session. Inventory /
hands replication is intentionally out-of-scope and does not block this work.

---

*End of design.*
