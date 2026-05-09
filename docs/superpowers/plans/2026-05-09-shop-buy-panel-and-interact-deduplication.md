# Shop Buy Panel + Interact Deduplication Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate the duplicate `Interact()` call on E-tap (Bug #2 — Rule #33 violation) AND author the missing `Resources/UI/UI_ShopBuyPanel.prefab` (Bug #1) so the multiplayer cashier flow works end-to-end.

**Architecture:** Half A consolidates all E-key input into `PlayerController` (rule #33 owner) and reduces `PlayerInteractionDetector` to a proximity tracker + prompt renderer + helper API (`CurrentTarget`, `TriggerTapInteract`, `TriggerHoldMenu`, `SetPromptHoldProgress`). Half B authors the missing buy-panel prefab + row sub-prefab via the MCP `assets-prefab-create` toolchain and adds a defensive Awake Canvas/GraphicRaycaster guard mirroring `UI_StorageFurniturePanel`.

**Tech Stack:** Unity (C#, NGO-aware MonoBehaviour patterns), TextMeshPro, Unity UI (uGUI), Resources.Load prefab loading, MCP Unity Editor tools (`assets-prefab-create`, `gameobject-component-add`, `gameobject-component-modify`, `assets-refresh`, `tests-run`, `console-get-logs`, `editor-application-set-state`).

**Spec:** [`docs/superpowers/specs/2026-05-09-shop-buy-panel-and-interact-deduplication-design.md`](../specs/2026-05-09-shop-buy-panel-and-interact-deduplication-design.md)

---

## File Map

| File | Status | Responsibility |
|---|---|---|
| `Assets/Scripts/Character/PlayerInteractionDetector.cs` | Modified | Proximity tracker + prompt renderer + helper API. **Loses all `Input.*` reads.** |
| `Assets/Scripts/Character/CharacterControllers/PlayerController.cs` | Modified | Rule #33 input owner. Gains `_detector` field + extended HandleEKeyHeld + replaced HandleEKeyUp. **Loses `GetNearestVisibleInteractable`.** |
| `Assets/Scripts/Character/CharacterControllers/Commands/PlayerInteractCommand.cs` | Modified | Updates `TriggerInteract` → `TriggerTapInteract` callsite (rename only). |
| `Assets/Scripts/UI/Shop/UI_ShopBuyPanel.cs` | Modified | Adds defensive `Awake` Canvas + GraphicRaycaster guard. |
| `Assets/Resources/UI/UI_ShopBuyRow.prefab` | Created | Single catalog row UI prefab. |
| `Assets/Resources/UI/UI_ShopBuyPanel.prefab` | Created | Resources.Load target for `UI_ShopBuyPanel.Open()`. |
| `.agent/skills/interactable-system/SKILL.md` | Modified | Doc the rule #33 enforcement + new detector public API. |
| `.agent/skills/player_ui/SKILL.md` | Modified | Doc the shop buy panel prefab path. |
| `wiki/systems/character-interaction.md` | Modified | Architecture change log + Public API + Gotchas refresh. |
| `wiki/systems/shops.md` | Modified | Change log: prefab path noted. |
| `wiki/gotchas/double-interact-rule-33-violation.md` | Created | Regression-prevention page with diagnostic log signatures. |
| `.claude/agents/character-system-specialist.md` | Modified | Refresh Player Input Ownership section to reflect new dispatch flow. |

---

## Task 1: Detector — extract public helper API + rename TriggerInteract

**Files:**
- Modify: `Assets/Scripts/Character/PlayerInteractionDetector.cs`
- Modify: `Assets/Scripts/Character/CharacterControllers/Commands/PlayerInteractCommand.cs:44`

**Goal:** Add `CurrentTarget`, `TriggerHoldMenu`, `SetPromptHoldProgress` public helpers. Rename `TriggerInteract` → `TriggerTapInteract`. **Behaviour-preserving** — detector still has its old `Update()` E-key block; we only add API surface.

- [ ] **Step 1.1: Pre-validation — confirm current state compiles**

Run via MCP:
```
mcp__ai-game-developer__assets-refresh
```
Expected: AssetDatabase refresh completes, no compilation errors.

- [ ] **Step 1.2: Add public helpers + rename method body**

Open `Assets/Scripts/Character/PlayerInteractionDetector.cs`. Apply the changes below.

**Add after line 9 (just below `private UI_PlayerTargeting _targeting;`):**

```csharp
    /// <summary>
    /// Closest in-range interactable currently tracked by the detector. Mirrors what the
    /// prompt UI is rendering. Read by <see cref="PlayerController"/>'s E-key dispatch
    /// (rule #33 — input owner reads detector data, never the reverse).
    /// </summary>
    public InteractableObject CurrentTarget => _currentInteractableObjectTarget;
```

**Rename `TriggerInteract` → `TriggerTapInteract` at line 266:**

Find:
```csharp
    /// <summary>
    /// Triggers the standard interaction with a given target.
    /// Called by PlayerInteractCommand when auto-navigate arrives at the target.
    /// </summary>
    public void TriggerInteract(InteractableObject target)
    {
        if (target == null) return;

        // Temporarily set the current target so ExecuteNormalInteract picks it up
        _currentInteractableObjectTarget = target;
        ExecuteNormalInteract();
    }
```

Replace with:
```csharp
    /// <summary>
    /// Canonical tap-E entry point. Called by <see cref="PlayerController"/>'s
    /// HandleEKeyUp dispatch (rule #33 — input owner) and by
    /// <see cref="MWI.CharacterControllers.Commands.PlayerInteractCommand"/> on auto-nav arrival.
    /// Wraps the dialogue-NPC freeness gate and the
    /// <see cref="InteractableObject.Interact"/> dispatch.
    /// </summary>
    public void TriggerTapInteract(InteractableObject target)
    {
        if (target == null) return;

        // Temporarily set the current target so ExecuteNormalInteract picks it up
        _currentInteractableObjectTarget = target;
        ExecuteNormalInteract();
    }
```

**Add two new public helpers immediately after `TriggerTapInteract`:**

```csharp
    /// <summary>
    /// Opens the generic hold-interaction menu for a target if it has any
    /// <see cref="InteractableObject.GetHoldInteractionOptions"/>. Returns true
    /// if a menu was opened (so the caller can flip its E-menu-opened latch).
    /// Called by <see cref="PlayerController"/>'s HandleEKeyHeld threshold branch
    /// (rule #33 — input owner).
    /// </summary>
    public bool TriggerHoldMenu(InteractableObject target)
    {
        if (target == null) return false;
        var options = target.GetHoldInteractionOptions(Character);
        if (options == null || options.Count == 0) return false;
        if (_playerUI == null) _playerUI = UnityEngine.Object.FindAnyObjectByType<PlayerUI>(FindObjectsInactive.Include);
        if (_playerUI == null) return false;
        _playerUI.OpenInteractionMenu(options);
        return true;
    }

    /// <summary>
    /// Drives the prompt-fill bar from <c>0..1</c>. Called every frame from
    /// <see cref="PlayerController"/>.HandleEKeyHeld — the input owner ticks the
    /// hold timer and pushes progress here.
    /// </summary>
    public void SetPromptHoldProgress(float t01)
    {
        if (currentPromptComponent != null) currentPromptComponent.SetFillAmount(Mathf.Clamp01(t01));
    }
```

- [ ] **Step 1.3: Update PlayerInteractCommand callsite**

Open `Assets/Scripts/Character/CharacterControllers/Commands/PlayerInteractCommand.cs`.

Find line 44:
```csharp
                _detector.TriggerInteract(_target);
```

Replace with:
```csharp
                _detector.TriggerTapInteract(_target);
```

- [ ] **Step 1.4: Verify compile**

Run via MCP:
```
mcp__ai-game-developer__assets-refresh
mcp__ai-game-developer__console-get-logs (filter: errors)
```
Expected: zero compile errors. If errors, the most likely culprit is a missed `TriggerInteract` callsite — grep `Assets/Scripts` for any remaining usage and rename.

- [ ] **Step 1.5: Manual smoke — prove existing behaviour still works**

In the editor:
1. Press Play.
2. Walk to a Crate. Tap E. Confirm the storage panel opens.
3. Walk to a selected-but-out-of-range NPC (Tab to select, then walk away). Tap E. Confirm `PlayerInteractCommand` auto-navs and triggers interaction on arrival.

This verifies the rename didn't break the auto-nav path. The double-fire is **expected to still happen** — Task 4 fixes it.

- [ ] **Step 1.6: Commit**

```bash
git add Assets/Scripts/Character/PlayerInteractionDetector.cs Assets/Scripts/Character/CharacterControllers/Commands/PlayerInteractCommand.cs
git commit -m "$(cat <<'EOF'
refactor(interactable): expose detector public helper API

Renames TriggerInteract → TriggerTapInteract. Adds CurrentTarget getter,
TriggerHoldMenu, SetPromptHoldProgress. Behaviour-preserving — additive
API surface ahead of the rule #33 dedup that consolidates E-key input
into PlayerController.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: PlayerController — wire detector ref + extend HandleEKeyHeld

**Files:**
- Modify: `Assets/Scripts/Character/CharacterControllers/PlayerController.cs`

**Goal:** Add `_detector` field, resolve in `Initialize()`. Extend `HandleEKeyHeld` to drive prompt fill via the detector and dispatch the generic hold-menu (in addition to the existing harvestable-specific menu). **Behaviour overlap** — detector still also drives prompt fill + opens its own hold-menu, so this commit may visually double-tick the prompt fill or briefly open two menus. Task 4 collapses the overlap.

- [ ] **Step 2.1: Add _detector field**

Open `Assets/Scripts/Character/CharacterControllers/PlayerController.cs`.

Find line 16:
```csharp
    // --- TAB Targeting ---
    private UI_PlayerTargeting _targeting;
```

Replace with:
```csharp
    // --- TAB Targeting ---
    private UI_PlayerTargeting _targeting;

    // --- Interactable detection (proximity + prompt + helper API) ---
    [SerializeField] private PlayerInteractionDetector _detector;
```

- [ ] **Step 2.2: Resolve _detector in Initialize**

Find lines 29-39 (the existing `Initialize` override):
```csharp
    public override void Initialize()
    {
        base.Initialize();
        if (_character.Rigidbody != null)
        {
            if (IsOwner)
                _character.Rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            else
                _character.Rigidbody.interpolation = RigidbodyInterpolation.None; // Let NetworkTransform handle it
        }
    }
```

Replace with:
```csharp
    public override void Initialize()
    {
        base.Initialize();
        if (_character.Rigidbody != null)
        {
            if (IsOwner)
                _character.Rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            else
                _character.Rigidbody.interpolation = RigidbodyInterpolation.None; // Let NetworkTransform handle it
        }

        // Auto-resolve detector reference. PlayerInteractionDetector lives on a child
        // GameObject of Character (per the existing CharacterInteractionDetector parent
        // chain). _character is the Character root, so search its children.
        if (_detector == null && _character != null)
            _detector = _character.GetComponentInChildren<PlayerInteractionDetector>(true);
    }
```

- [ ] **Step 2.3: Extend HandleEKeyHeld for prompt-fill drive + generic hold-menu**

Find lines 358-370 (the existing `HandleEKeyHeld`):
```csharp
    /// <summary>While E is held, open the interaction menu once the hold threshold is crossed.</summary>
    private void HandleEKeyHeld()
    {
        if (_eMenuOpened) return;
        if (UnityEngine.Time.unscaledTime - _eHeldStartTime < E_HOLD_THRESHOLD) return;

        var nearest = GetNearestVisibleHarvestable();
        if (nearest != null)
        {
            MWI.UI.Interaction.UI_HarvestInteractionMenu.Open(_character, nearest, OnInteractionMenuClosed);
            _eMenuOpened = true;
        }
    }
```

Replace with:
```csharp
    /// <summary>
    /// While E is held: drive the prompt-fill bar via the detector, then once the
    /// hold threshold is crossed dispatch a hold-menu. Two priorities:
    /// (A) harvestable-specific menu (UI_HarvestInteractionMenu) — preserves existing UX,
    /// (B) generic interactable hold-menu via _detector.TriggerHoldMenu — moved out of
    /// PlayerInteractionDetector per rule #33 (input owner = PlayerController).
    /// </summary>
    private void HandleEKeyHeld()
    {
        if (_eMenuOpened) return;

        float t01 = (UnityEngine.Time.unscaledTime - _eHeldStartTime) / E_HOLD_THRESHOLD;
        _detector?.SetPromptHoldProgress(Mathf.Clamp01(t01));
        if (t01 < 1f) return;

        // Priority A: harvestable-specific menu.
        var harvestable = GetNearestVisibleHarvestable();
        if (harvestable != null)
        {
            MWI.UI.Interaction.UI_HarvestInteractionMenu.Open(_character, harvestable, OnInteractionMenuClosed);
            _eMenuOpened = true;
            return;
        }

        // Priority B: generic interactable hold-menu via detector helper.
        var generic = _detector?.CurrentTarget;
        if (generic != null && _detector.TriggerHoldMenu(generic))
            _eMenuOpened = true;
    }
```

- [ ] **Step 2.4: Verify compile**

Run via MCP:
```
mcp__ai-game-developer__assets-refresh
mcp__ai-game-developer__console-get-logs (filter: errors)
```
Expected: zero compile errors.

- [ ] **Step 2.5: Manual smoke**

In the editor:
1. Press Play.
2. Walk to a Crate or any prompt-rendering interactable.
3. **Hold** E. Observe the prompt-fill bar animate from 0→1. (At this point, both PlayerController AND the detector drive the fill — minor visual overlap acceptable.)
4. Walk to a Cashier (must have a vendor on duty) or any interactable. **Tap** E. The Crate panel / cashier transaction starts. Double-fire bug is **still present** — Task 4 fixes it.

- [ ] **Step 2.6: Commit**

```bash
git add Assets/Scripts/Character/CharacterControllers/PlayerController.cs
git commit -m "$(cat <<'EOF'
refactor(player-input): wire detector ref + extend hold-E dispatch

Adds _detector serialized field, auto-resolved in Initialize.
Extends HandleEKeyHeld to drive prompt fill via the detector and
dispatch the generic hold-menu in addition to the existing
harvestable-specific menu. Stepping stone — Task 3-4 complete the
rule #33 dedup.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: PlayerController — replace HandleEKeyUp with consolidated tap dispatch

**Files:**
- Modify: `Assets/Scripts/Character/CharacterControllers/PlayerController.cs`

**Goal:** Replace `HandleEKeyUp` with a dispatch that resolves target = `_targeting.SelectedInteractable ?? _detector.CurrentTarget`, routes out-of-range selected via `PlayerInteractCommand`, and otherwise calls `_detector.TriggerTapInteract(target)`. Delete `GetNearestVisibleInteractable` (now unused). **Double-fire still active** — both detector and PlayerController call `.Interact()` per tap. Task 4 collapses it.

- [ ] **Step 3.1: Replace HandleEKeyUp body**

Open `Assets/Scripts/Character/CharacterControllers/PlayerController.cs`.

Find lines 372-379 (the existing `HandleEKeyUp`):
```csharp
    /// <summary>On E release, if the menu wasn't opened (tap), run the immediate Interact path.</summary>
    private void HandleEKeyUp()
    {
        if (_eMenuOpened) return;

        var nearest = GetNearestVisibleInteractable();
        if (nearest != null) nearest.Interact(_character);
    }
```

Replace with:
```csharp
    /// <summary>
    /// On E release (tap): consolidated dispatch (rule #33 — only PlayerController
    /// reads E input). Resolves target as selected-from-targeting OR detector's
    /// current proximity target. Selected-but-out-of-range routes through
    /// PlayerInteractCommand for auto-nav. Otherwise delegates to the detector's
    /// canonical TriggerTapInteract helper (which encapsulates the dialogue-NPC
    /// freeness gate + InteractableObject.Interact dispatch).
    /// </summary>
    private void HandleEKeyUp()
    {
        if (_eMenuOpened) return;
        _detector?.SetPromptHoldProgress(0f);

        EnsureTargeting();
        var selected = _targeting?.SelectedInteractable;
        var target = selected ?? _detector?.CurrentTarget;
        if (target == null) return;

        // Selected target is out of range → auto-nav to it (existing UX, was at
        // PlayerInteractionDetector.cs:201-210 prior to dedup).
        if (selected != null && _detector != null && !_detector.IsTargetInRange(selected))
        {
            SetOrder(new MWI.CharacterControllers.Commands.PlayerInteractCommand(selected, _detector));
            return;
        }

        if (_detector != null) _detector.TriggerTapInteract(target);
    }
```

- [ ] **Step 3.2: Delete GetNearestVisibleInteractable**

Find lines 407-424 (the entire `GetNearestVisibleInteractable` method):

```csharp
    private InteractableObject GetNearestVisibleInteractable()
    {
        var awareness = _character.CharacterAwareness;
        if (awareness == null) return null;
        var visible = awareness.GetVisibleInteractables();
        if (visible == null || visible.Count == 0) return null;
        InteractableObject closest = null;
        float closestDist = float.MaxValue;
        for (int i = 0; i < visible.Count; i++)
        {
            var obj = visible[i];
            if (obj == null) continue;
            if (!obj.IsCharacterInInteractionZone(_character)) continue;
            float d = Vector3.Distance(_character.transform.position, obj.transform.position);
            if (d < closestDist) { closestDist = d; closest = obj; }
        }
        return closest;
    }
```

Delete the entire method (including the line above it if it was a blank). `GetNearestVisibleHarvestable` (lines 388-405) is still called by `HandleEKeyHeld` and **stays untouched**.

- [ ] **Step 3.3: Verify compile**

Run via MCP:
```
mcp__ai-game-developer__assets-refresh
mcp__ai-game-developer__console-get-logs (filter: errors)
```
Expected: zero compile errors. If a stale reference to `GetNearestVisibleInteractable` exists, grep `Assets/Scripts` and remove.

- [ ] **Step 3.4: Manual smoke — confirm double-fire is now PlayerController-driven**

In the editor:
1. Press Play.
2. Walk to a Crate. Tap E. The storage panel still opens.
3. Open the Console. You should see TWO `[Furniture] ... utilise Crate.` log lines (one from each path, with the dedup not yet active). The PlayerController-side log will have a ResolvePath route through `_detector.TriggerTapInteract`.

The double-fire is **still expected**. Task 4 deletes the detector-side input block which collapses to single-fire.

- [ ] **Step 3.5: Commit**

```bash
git add Assets/Scripts/Character/CharacterControllers/PlayerController.cs
git commit -m "$(cat <<'EOF'
refactor(player-input): consolidate tap-E dispatch in PlayerController

Replaces HandleEKeyUp with target-resolved dispatch (selected ??
detector.CurrentTarget). Out-of-range selected → PlayerInteractCommand;
in-range → detector.TriggerTapInteract. Deletes
GetNearestVisibleInteractable (replaced by detector.CurrentTarget as
single source of truth). Detector still also fires .Interact() — Task 4
deletes the obsolete input block.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: Detector — delete Update() E-key block + obsolete state, move proximity to LateUpdate

**Files:**
- Modify: `Assets/Scripts/Character/PlayerInteractionDetector.cs`

**Goal:** This is the **dedup-takes-effect** commit. Delete the detector's `Update()` E-key block (lines 199-248), delete the `eHoldTime` / `isHoldingE` / `HOLD_THRESHOLD` fields, delete `EnsurePlayerUI` and `EnsureTargeting` (PlayerController concerns now), delete the chat-input-field guard at lines 176-181 (PlayerController already gates at lines 149-151). Move `UpdateClosestTarget()` invocation into `LateUpdate` so proximity tracking still runs.

- [ ] **Step 4.1: Pre-validation — capture current double-fire log signature**

In the editor (Play mode):
1. Open the Console. Clear it.
2. Walk to a Crate. Tap E once.
3. Capture the log output. Expected: TWO `[Furniture] ... utilise Crate.` lines (or whatever the storage panel logs on Initialize). This is the BUG signature.

This is the "failing test" — record it so the post-fix run can be compared.

- [ ] **Step 4.2: Delete obsolete fields and constants**

Open `Assets/Scripts/Character/PlayerInteractionDetector.cs`.

Find lines 12-16:
```csharp
    private GameObject currentPromptUI;
    private InteractionPromptUI currentPromptComponent;
    private float eHoldTime = 0f;
    private bool isHoldingE = false;
    private const float HOLD_THRESHOLD = 0.4f;
    private PlayerUI _playerUI;
    private UI_PlayerTargeting _targeting;
```

Replace with:
```csharp
    private GameObject currentPromptUI;
    private InteractionPromptUI currentPromptComponent;
    private PlayerUI _playerUI;   // resolved on demand inside TriggerHoldMenu (event-driven path)
```

(Removes `eHoldTime`, `isHoldingE`, `HOLD_THRESHOLD`, and `_targeting` — all input-dispatch state that belongs in PlayerController per rule #33.)

- [ ] **Step 4.3: Delete EnsureTargeting helper**

Find lines 60-68 (the `EnsureTargeting` method):
```csharp
    /// <summary>
    /// Ensures the _targeting reference is resolved.
    /// </summary>
    private void EnsureTargeting()
    {
        if (_targeting == null)
            _targeting = UnityEngine.Object.FindAnyObjectByType<UI_PlayerTargeting>(FindObjectsInactive.Include);
    }
```

Delete the entire method.

(Keep `EnsurePlayerUI` at lines 50-58 — `TriggerHoldMenu` calls a fallback resolution there inline; if you prefer, you can keep `EnsurePlayerUI` and have `TriggerHoldMenu` call it. For minimal change, leave `EnsurePlayerUI` as-is.)

- [ ] **Step 4.4: Replace Update() with proximity-only LateUpdate**

Find lines 169-249 (the entire `Update` method, including the IsOwner guard, `UpdateClosestTarget` call, chat-input gate, `EnsureTargeting`, and the three `Input.GetKey…` blocks):

```csharp
    private void Update()
    {
        if (Character.TryGetComponent(out Unity.Netcode.NetworkObject netObj) && netObj.IsSpawned && !netObj.IsOwner) return;

        UpdateClosestTarget();

        // Prevent interacting if the player is currently typing in an input field (e.g. chat)
        if (UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject != null &&
            UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject.GetComponent<TMPro.TMP_InputField>() != null)
        {
            return;
        }

        // Dev mode suppresses gameplay input. Nearby-target tracking above still runs.
        if (DevModeManager.SuppressPlayerInput)
        {
            return;
        }

        if (_playerUI == null)
            _playerUI = UnityEngine.Object.FindAnyObjectByType<PlayerUI>(FindObjectsInactive.Include);

        EnsureTargeting();

        // --- E KEY INTERACTION ---
        // Determine the effective target for E-key:
        // If a selection exists but is NOT in range, pressing E will auto-navigate to it.
        InteractableObject selectedTarget = _targeting != null ? _targeting.SelectedInteractable : null;

        if (Input.GetKeyDown(KeyCode.E))
        {
            if (selectedTarget != null && !IsTargetInRange(selectedTarget))
            {
                // Selected target is not in InteractionZone — auto-navigate to it
                var playerController = Character.GetComponent<PlayerController>();
                if (playerController != null)
                {
                    Debug.Log($"<color=cyan>[PlayerInteractionDetector]</color> Selected target {selectedTarget.name} is out of range. Auto-navigating.");
                    playerController.SetOrder(new PlayerInteractCommand(selectedTarget, this));
                }
                return;
            }

            // Normal E-press: target is in range (or no selection, using proximity)
            if (_currentInteractableObjectTarget != null)
            {
                isHoldingE = true;
                eHoldTime = 0f;
                if (currentPromptComponent != null) currentPromptComponent.SetFillAmount(0f);
            }
        }

        if (Input.GetKey(KeyCode.E) && isHoldingE)
        {
            eHoldTime += Time.deltaTime;
            if (currentPromptComponent != null) currentPromptComponent.SetFillAmount(eHoldTime / HOLD_THRESHOLD);

            if (eHoldTime >= HOLD_THRESHOLD)
            {
                isHoldingE = false; // Stop tracking hold
                if (_currentInteractableObjectTarget == null) { eHoldTime = 0f; return; }
                var options = _currentInteractableObjectTarget.GetHoldInteractionOptions(Character);
                if (options != null && options.Count > 0)
                {
                    if (_playerUI != null) _playerUI.OpenInteractionMenu(options);
                }
                else
                {
                    ExecuteNormalInteract();
                }
            }
        }

        if (Input.GetKeyUp(KeyCode.E) && isHoldingE)
        {
            isHoldingE = false; // Released before threshold
            if (currentPromptComponent != null) currentPromptComponent.SetFillAmount(0f);
            ExecuteNormalInteract();
        }
    }
```

Replace with:
```csharp
    /// <summary>
    /// Proximity tracking only. All E-key input dispatch lives in
    /// <see cref="PlayerController"/> per rule #33 — this class no longer reads
    /// any keyboard input. <see cref="UpdateClosestTarget"/> is invoked from
    /// LateUpdate so PlayerController's input read sees a stable target snapshot.
    /// </summary>
    private void LateUpdate()
    {
        if (Character.TryGetComponent(out Unity.Netcode.NetworkObject netObj) && netObj.IsSpawned && !netObj.IsOwner) return;
        UpdateClosestTarget();
    }
```

(The chat-input gate at lines 176-181 is **already covered** by PlayerController:149-151. The dev-mode suppression at lines 184-187 is **already covered** by PlayerController's input loop.)

- [ ] **Step 4.5: Verify compile**

Run via MCP:
```
mcp__ai-game-developer__assets-refresh
mcp__ai-game-developer__console-get-logs (filter: errors)
```
Expected: zero compile errors. If `_targeting`, `eHoldTime`, `isHoldingE`, `HOLD_THRESHOLD`, or `EnsureTargeting` is referenced anywhere else in the file, the compile error pinpoints it — delete the references.

- [ ] **Step 4.6: Manual smoke — confirm dedup is in effect**

In the editor:
1. Press Play.
2. Open the Console. Clear it.
3. Walk to a Crate. **Tap** E.
   - Expected: ONE `[Furniture] ... utilise Crate.` log (or storage-panel `Initialize` log). **Bug fixed.**
   - The storage panel opens normally.
4. Walk to a Cashier (with a vendor present). **Tap** E.
   - Expected: ONE `CharacterAction_BuyFromShop` enqueue, ONE `OpenBuyPanelClientRpc` invocation. (UI panel still missing — that's Task 6/7.)
5. Walk to a Bed / chair / time clock. **Tap** E.
   - Expected: ONE corresponding action queued.
6. **Hold** E on a Harvestable.
   - Expected: prompt fill animates 0→1; `UI_HarvestInteractionMenu` opens once at threshold.
7. **Hold** E on a chest (which has no `GetHoldInteractionOptions`).
   - Expected: prompt fill animates 0→1; nothing happens at threshold (no menu opens). Tap-only on E-up.
8. Tab-select an out-of-range NPC. Tap E.
   - Expected: `PlayerInteractCommand` enqueued; player auto-navs; on arrival, ONE `Interact` fires.

If any scenario shows DOUBLE-fire, revert and inspect — the input-block deletion may be incomplete.

- [ ] **Step 4.7: Commit**

```bash
git add Assets/Scripts/Character/PlayerInteractionDetector.cs
git commit -m "$(cat <<'EOF'
fix(player-input): eliminate double-Interact on E tap (rule #33)

Deletes PlayerInteractionDetector's Update() E-key block and obsolete
state (eHoldTime, isHoldingE, HOLD_THRESHOLD, _targeting). Moves
UpdateClosestTarget into LateUpdate so proximity tracking still runs.
PlayerController is now the only Input.GetKey reader for player-character
control, restoring rule #33 compliance and collapsing tap-E to a single
.Interact() call.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 5: UI_ShopBuyPanel.cs — add defensive Awake guard

**Files:**
- Modify: `Assets/Scripts/UI/Shop/UI_ShopBuyPanel.cs`

**Goal:** Add the same Awake Canvas + GraphicRaycaster guard that `UI_StorageFurniturePanel` uses (lines 58-71 of that file). Defends against the storage-panel bug pattern where a Resources.Load'd panel doesn't render or doesn't receive clicks because its parent Canvas hierarchy isn't predictable.

- [ ] **Step 5.1: Add Awake method**

Open `Assets/Scripts/UI/Shop/UI_ShopBuyPanel.cs`.

Find line 33-34 (just below the `_rowPrefab` SerializeField, before the `_instance` static):
```csharp
        [SerializeField] private GameObject _rowPrefab;   // prefab carrying a UI_ShopBuyRow component

        private static UI_ShopBuyPanel _instance;
```

Insert between them:
```csharp
        [SerializeField] private GameObject _rowPrefab;   // prefab carrying a UI_ShopBuyRow component

        /// <summary>
        /// Programmatically ensure the panel root has its own Canvas + GraphicRaycaster
        /// so it renders and raycasts independently of whatever scene canvas it ends up
        /// under — Resources.Load → Instantiate places the prefab at the scene root by
        /// default. Mirrors the defensive guard in UI_StorageFurniturePanel.cs:58-71.
        /// </summary>
        private void Awake()
        {
            var canvas = GetComponent<UnityEngine.Canvas>();
            if (canvas == null) canvas = gameObject.AddComponent<UnityEngine.Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 50;

            if (GetComponent<UnityEngine.UI.GraphicRaycaster>() == null)
                gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        }

        private static UI_ShopBuyPanel _instance;
```

- [ ] **Step 5.2: Verify compile**

Run via MCP:
```
mcp__ai-game-developer__assets-refresh
mcp__ai-game-developer__console-get-logs (filter: errors)
```
Expected: zero compile errors.

- [ ] **Step 5.3: Commit**

```bash
git add Assets/Scripts/UI/Shop/UI_ShopBuyPanel.cs
git commit -m "$(cat <<'EOF'
feat(ui): UI_ShopBuyPanel defensive Awake Canvas/Raycaster guard

Mirrors UI_StorageFurniturePanel's guard so the panel renders and
receives clicks regardless of where Resources.Load instantiates it.
Prerequisite for the prefab authoring in the next commit.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 6: Author UI_ShopBuyRow.prefab via MCP

**Files:**
- Create: `Assets/Resources/UI/UI_ShopBuyRow.prefab`

**Goal:** Author the row prefab carrying a `UI_ShopBuyRow` component with all SerializeFields wired. Uses the MCP `gameobject-create` + `gameobject-component-add` + `assets-prefab-create` toolchain (same path that produced the storage panel prefabs).

- [ ] **Step 6.1: Create the in-scene row hierarchy**

Use MCP tools in this order. The exact tool call signatures use whatever your Unity-MCP harness provides — substitute concrete arguments from `mcp__ai-game-developer__tool-list` if you need parameter names.

```
mcp__ai-game-developer__gameobject-create
  name: UI_ShopBuyRow
  // creates an empty in the active scene
```

```
mcp__ai-game-developer__gameobject-component-add
  gameObjectPath: UI_ShopBuyRow
  componentType: RectTransform   (added automatically when UI components attach, but ensure)
```

```
mcp__ai-game-developer__gameobject-component-add
  gameObjectPath: UI_ShopBuyRow
  componentType: UnityEngine.UI.Image          // background graphic for raycasting
```

```
mcp__ai-game-developer__gameobject-component-add
  gameObjectPath: UI_ShopBuyRow
  componentType: UnityEngine.UI.Button
```

```
mcp__ai-game-developer__gameobject-component-add
  gameObjectPath: UI_ShopBuyRow
  componentType: UnityEngine.UI.HorizontalLayoutGroup
```

```
mcp__ai-game-developer__gameobject-component-add
  gameObjectPath: UI_ShopBuyRow
  componentType: UI_ShopBuyRow                  // the C# script
```

- [ ] **Step 6.2: Create the eight child elements**

Children must be named **exactly** as below (the "Icon" name is load-bearing).

```
Icon         (GameObject + UnityEngine.UI.Image)
NameText     (GameObject + TMPro.TextMeshProUGUI)
PriceText    (GameObject + TMPro.TextMeshProUGUI)
StockText    (GameObject + TMPro.TextMeshProUGUI)
MinusButton  (GameObject + UnityEngine.UI.Image + UnityEngine.UI.Button + child Text "-")
QuantityInput(GameObject + UnityEngine.UI.Image + TMPro.TMP_InputField + Text Area subtree)
PlusButton   (GameObject + UnityEngine.UI.Image + UnityEngine.UI.Button + child Text "+")
SubtotalText (GameObject + TMPro.TextMeshProUGUI)
```

For each child, use:
```
mcp__ai-game-developer__gameobject-create
  name: <ChildName>
  parentPath: UI_ShopBuyRow
```

Then attach the appropriate components per the table above using `gameobject-component-add`.

For the TMP_InputField, follow the standard TMP setup (it auto-creates a "Text Area / Text" child subtree on first add).

- [ ] **Step 6.3: Wire UI_ShopBuyRow SerializeFields**

Use MCP `gameobject-component-modify` (or `object-modify` against the UI_ShopBuyRow component on the root) to assign:

```
_icon          → UI_ShopBuyRow/Icon (Image)
_nameText      → UI_ShopBuyRow/NameText (TextMeshProUGUI)
_priceText     → UI_ShopBuyRow/PriceText (TextMeshProUGUI)
_stockText     → UI_ShopBuyRow/StockText (TextMeshProUGUI)
_subtotalText  → UI_ShopBuyRow/SubtotalText (TextMeshProUGUI)
_quantityInput → UI_ShopBuyRow/QuantityInput (TMP_InputField)
_plusButton    → UI_ShopBuyRow/PlusButton (Button)
_minusButton   → UI_ShopBuyRow/MinusButton (Button)
```

- [ ] **Step 6.4: Save the in-scene hierarchy as a prefab at the Resources path**

```
mcp__ai-game-developer__assets-create-folder
  parentFolder: Assets/Resources
  folderName: UI
```

(Skip if `Assets/Resources/UI` already exists — `assets-find` to check.)

```
mcp__ai-game-developer__assets-prefab-create
  gameObjectPath: UI_ShopBuyRow
  prefabPath: Assets/Resources/UI/UI_ShopBuyRow.prefab
```

- [ ] **Step 6.5: Verify the prefab loads + has correct components**

```
mcp__ai-game-developer__assets-find
  searchFilter: t:Prefab UI_ShopBuyRow
```
Expected: returns `Assets/Resources/UI/UI_ShopBuyRow.prefab`.

```
mcp__ai-game-developer__assets-get-data
  assetPath: Assets/Resources/UI/UI_ShopBuyRow.prefab
```
Expected: serialized data shows UI_ShopBuyRow component with all eight SerializeFields wired (no `m_FileID: 0` for `_icon`, `_nameText`, etc.). If any field is null, redo Step 6.3 for that field.

- [ ] **Step 6.6: Delete the in-scene authoring instance**

```
mcp__ai-game-developer__gameobject-destroy
  gameObjectPath: UI_ShopBuyRow
```

The prefab asset is what we ship; the scene instance was a builder fixture.

- [ ] **Step 6.7: Commit**

```bash
git add Assets/Resources/UI/UI_ShopBuyRow.prefab
git commit -m "$(cat <<'EOF'
feat(ui): UI_ShopBuyRow prefab authored via MCP

Single catalog row: icon, name, price, stock, +/- stepper, subtotal.
All UI_ShopBuyRow SerializeFields wired. The "Icon" child name matches
UI_StorageGrid convention so future grid pools reusing the row don't
trip the GetComponentInChildren-finds-bg bug.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 7: Author UI_ShopBuyPanel.prefab via MCP + wire row prefab

**Files:**
- Create: `Assets/Resources/UI/UI_ShopBuyPanel.prefab`

**Goal:** Author the buy panel prefab at the path `UI_ShopBuyPanel.Open()` Resources.Loads. Wire all seven SerializeFields including `_rowPrefab` → the just-authored `UI_ShopBuyRow.prefab`. Apply storage-panel raycast lessons (ScrollView + Viewport `raycastTarget=false`).

- [ ] **Step 7.1: Pre-validation — confirm Resources.Load is currently failing**

In the editor (Play mode):
1. Walk to a Cashier with a vendor on duty.
2. Tap E.
3. Open the Console. Expected: `[UI_ShopBuyPanel] prefab not found at Resources/UI/UI_ShopBuyPanel`. **Bug confirmed.**

- [ ] **Step 7.2: Create the panel root with required components**

```
mcp__ai-game-developer__gameobject-create
  name: UI_ShopBuyPanel
```

```
mcp__ai-game-developer__gameobject-component-add
  gameObjectPath: UI_ShopBuyPanel
  componentType: UnityEngine.RectTransform           (set anchors to stretch)

mcp__ai-game-developer__gameobject-component-add
  gameObjectPath: UI_ShopBuyPanel
  componentType: UnityEngine.Canvas                   (overrideSorting=true, sortingOrder=50)

mcp__ai-game-developer__gameobject-component-add
  gameObjectPath: UI_ShopBuyPanel
  componentType: UnityEngine.UI.GraphicRaycaster

mcp__ai-game-developer__gameobject-component-add
  gameObjectPath: UI_ShopBuyPanel
  componentType: UI_ShopBuyPanel                       (the C# script)
```

(The `Awake` guard added in Task 5 is defensive — having the Canvas authored on the prefab is the primary path.)

- [ ] **Step 7.3: Build the panel hierarchy**

Create the following children, modeled on the storage panel layout:

```
UI_ShopBuyPanel
└─ Panel  (Image bg, raycastTarget=true, RectTransform stretch with margin)
   ├─ HeaderRow  (RectTransform top-stretch, HorizontalLayoutGroup)
   │  ├─ Title         (TextMeshProUGUI)
   │  └─ CancelButton  (Image + Button + child Text "X")
   ├─ ScrollView  (Image raycastTarget=false, ScrollRect)
   │  └─ Viewport  (Image raycastTarget=false, Mask)
   │     └─ Content  (RectTransform, VerticalLayoutGroup, ContentSizeFitter [vertical=PreferredSize])
   └─ FooterRow  (RectTransform bottom-stretch, HorizontalLayoutGroup)
      ├─ WalletText    (TextMeshProUGUI)
      ├─ TotalText     (TextMeshProUGUI)
      └─ ConfirmButton (Image + Button + child Text "Confirm")
```

For each node, use `gameobject-create` (with `parentPath`) + `gameobject-component-add`.

**Critical:** Set `raycastTarget = false` on the **ScrollView's** `Image` and on the **Viewport's** `Image`. Without these, the storage-panel bug recurs — the ScrollView Image swallows clicks before they reach row buttons. Use `gameobject-component-modify` (or `object-modify`):

```
component: ScrollView/Image
property: m_RaycastTarget
value: false

component: ScrollView/Viewport/Image
property: m_RaycastTarget
value: false
```

The ScrollRect's content-target should point at `ScrollView/Viewport/Content`.

- [ ] **Step 7.4: Wire UI_ShopBuyPanel SerializeFields**

Use MCP `gameobject-component-modify` against the UI_ShopBuyPanel component on the root:

```
_titleText     → UI_ShopBuyPanel/Panel/HeaderRow/Title (TextMeshProUGUI)
_walletText    → UI_ShopBuyPanel/Panel/FooterRow/WalletText (TextMeshProUGUI)
_totalText     → UI_ShopBuyPanel/Panel/FooterRow/TotalText (TextMeshProUGUI)
_confirmButton → UI_ShopBuyPanel/Panel/FooterRow/ConfirmButton (Button)
_cancelButton  → UI_ShopBuyPanel/Panel/HeaderRow/CancelButton (Button)
_rowsParent    → UI_ShopBuyPanel/Panel/ScrollView/Viewport/Content (Transform)
_rowPrefab     → Assets/Resources/UI/UI_ShopBuyRow.prefab (GameObject reference)
```

For `_rowPrefab`, the value is a prefab asset reference. Use `assets-find` to resolve the prefab GUID:

```
mcp__ai-game-developer__assets-find
  searchFilter: t:Prefab UI_ShopBuyRow
```

Use the resulting path/GUID in the `gameobject-component-modify` payload as the `_rowPrefab` value. (The exact MCP arg shape for asset references is harness-specific; consult the MCP docs or use `object-get-data` on a precedent component to see the wire format.)

- [ ] **Step 7.5: Save as prefab**

```
mcp__ai-game-developer__assets-prefab-create
  gameObjectPath: UI_ShopBuyPanel
  prefabPath: Assets/Resources/UI/UI_ShopBuyPanel.prefab
```

- [ ] **Step 7.6: Verify the prefab + wiring**

```
mcp__ai-game-developer__assets-get-data
  assetPath: Assets/Resources/UI/UI_ShopBuyPanel.prefab
```
Expected: UI_ShopBuyPanel component shows all seven SerializeFields wired. `_rowPrefab` references the row prefab GUID. Canvas component shows `overrideSorting: 1`, `sortingOrder: 50`. ScrollView's Image and Viewport's Image both show `m_RaycastTarget: 0`.

- [ ] **Step 7.7: Delete in-scene authoring instance**

```
mcp__ai-game-developer__gameobject-destroy
  gameObjectPath: UI_ShopBuyPanel
```

- [ ] **Step 7.8: End-to-end manual play-test**

In the editor:
1. Press Play (single-player or Host).
2. Walk to a Cashier with a vendor present.
3. Tap E. **Expected:**
   - Console: ONE `CharacterAction_BuyFromShop` enqueue, ONE `OpenBuyPanelClientRpc`.
   - **Shop buy panel appears.** Title shows "Shop: <building name>". Wallet shows current balance. Each catalog item appears as a row with icon, name, price, stock count, +/- stepper.
4. Click +/- on a row. Subtotal updates. Total in footer recomputes. Confirm button enables when total ≤ wallet.
5. Click Cancel. Panel closes; cashier lock releases (`CashierNetSync.CurrentCustomerNetworkObjectId` becomes 0 on host).
6. Tap E again. Click + on bread row to set qty=1. Click Confirm.
7. Expected: row stock decrements; wallet balance debits; cashier till credits; panel closes.
8. Tap E on the cashier without a vendor present. Expected: `UI_Toast` says "No vendor on duty.", panel does NOT open.

If the buy panel renders but slot/stepper buttons don't respond, recheck Step 7.3 — the ScrollView/Viewport raycastTarget settings are the most likely culprit (storage-panel lesson).

- [ ] **Step 7.9: Commit**

```bash
git add Assets/Resources/UI/UI_ShopBuyPanel.prefab
git commit -m "$(cat <<'EOF'
feat(ui): UI_ShopBuyPanel prefab authored at Resources/UI path

Resources.Load contract fulfilled. Hierarchy matches the storage panel
pattern: programmatic Canvas + GraphicRaycaster, ScrollView/Viewport
raycastTarget=false (so row buttons receive clicks), VerticalLayout +
ContentSizeFitter for dynamic catalog. All seven UI_ShopBuyPanel
SerializeFields wired including _rowPrefab → UI_ShopBuyRow.prefab.

End-to-end multiplayer cashier flow now works: customer client opens
panel → server-authoritative Confirm/Cancel via existing CashierNetSync
ServerRpcs → till + wallet update via existing replication paths.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 8: Network validation pass

**Files:** none modified by this task — read-only audit. May produce a follow-up commit if any issue is found.

**Goal:** Use the `network-validator` agent to verify each `InteractableObject` subclass behaves correctly under the new single-call contract on Host↔Client, Client↔Client, and Host/Client↔NPC scenarios. The agent is read-only; if it identifies a bug, file a fix in a follow-up task — do NOT bundle it here.

- [ ] **Step 8.1: Dispatch the network-validator agent**

Use the `Agent` tool with `subagent_type: network-validator`. Prompt:

```
Audit the interact-deduplication change in commit chain Tasks 1–4 of
docs/superpowers/plans/2026-05-09-shop-buy-panel-and-interact-deduplication.md.

Scope: every InteractableObject subclass must fire exactly one effect per
E tap on Host↔Client + Client↔Client + Host/Client↔NPC.

Subclasses to verify (grep `: InteractableObject` to find any I missed):
- FurnitureInteractable (storage chest, shelf, barrel, wardrobe, cooking,
  crafting, seating, time clock variants)
- CashierInteractable
- BuildingInteractable (construction site, doors, beds, time-clock chair)
- Harvestable
- CharacterInteractable (dialogue NPC start/end + freeness gate)
- MapTransitionDoor

For each, check:
1. PlayerController.HandleEKeyUp dispatch route is correct for that subclass.
2. The dialogue-NPC freeness gate (TriggerTapInteract / ExecuteNormalInteract
   internal logic) preserves the existing CharacterInteractable behaviour.
3. Server authority is preserved (no client-side mutations introduced by
   the dispatch refactor).
4. The network sync paths (StorageFurnitureNetworkSync, CashierNetSync, etc.)
   are unaffected.
5. NPC-driven .Interact() callsites at MapTransitionDoor.cs:155 and
   CharacterParty.cs:1143 still work (they don't go through input).

Deliverable: a markdown audit table listing each subclass, the verification
result (PASS/CONCERN), and any concrete reproduction steps for concerns.
Do not make code changes.
```

- [ ] **Step 8.2: Read the audit report**

Save the agent's output. If any subclass shows CONCERN, capture the specific reproduction step and file a follow-up task. If all PASS, the validation pass is complete — proceed to Task 9.

- [ ] **Step 8.3: Commit (or skip if no doc generated)**

If the audit produces a markdown report worth keeping:

```bash
mkdir -p docs/superpowers/audits
# Write the audit to docs/superpowers/audits/2026-05-09-interact-dedup-audit.md
git add docs/superpowers/audits/2026-05-09-interact-dedup-audit.md
git commit -m "$(cat <<'EOF'
docs(audit): network validation for interact dedup

Records each InteractableObject subclass's behaviour under the new
single-call dispatch contract on all multiplayer scenarios.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

If no report file produced, skip this step — the audit lives in the agent's session output.

---

## Task 9: Documentation updates per Project Rules #28, #29, #29b

**Files:**
- Modify: `.agent/skills/interactable-system/SKILL.md`
- Modify: `.agent/skills/player_ui/SKILL.md`
- Modify: `wiki/systems/character-interaction.md`
- Modify: `wiki/systems/shops.md`
- Create: `wiki/gotchas/double-interact-rule-33-violation.md`
- Modify: `.claude/agents/character-system-specialist.md`

**Goal:** Refresh skill files, wiki pages, and agents so the docs match the new dispatch flow. Required by Rules #28 (SKILL.md), #29 (agents), #29b (wiki).

- [ ] **Step 9.1: Read the current state of each doc**

Run via MCP / Read in parallel:
```
Read .agent/skills/interactable-system/SKILL.md
Read .agent/skills/player_ui/SKILL.md
Read wiki/systems/character-interaction.md
Read wiki/systems/shops.md
Read .claude/agents/character-system-specialist.md
```
Read `wiki/CLAUDE.md` first per the wiki rule — it governs frontmatter, naming, wikilinks, sources, the diff-preview rule.

- [ ] **Step 9.2: Update interactable-system SKILL.md**

Refresh the **Public API** + **Behaviour** sections to reflect:
- E-key tap and hold dispatch lives in `PlayerController`.
- Detector exposes `CurrentTarget`, `IsTargetInRange`, `TriggerTapInteract`, `TriggerHoldMenu`, `SetPromptHoldProgress`.
- `TriggerInteract` is renamed to `TriggerTapInteract` — note the rename in a "Migration / breaking changes" subsection if the SKILL has one.

If the SKILL has a "Last reviewed" / "Updated" stamp, bump it to `2026-05-09`.

- [ ] **Step 9.3: Update player_ui SKILL.md**

Add or refresh a "Shop buy panel" section noting:
- `Resources/UI/UI_ShopBuyPanel.prefab` is the canonical buy panel asset.
- Wired SerializeFields per Task 7 wiring table.
- ScrollView/Viewport `raycastTarget=false` (storage-panel lesson, regression-prone).
- `Awake` programmatic Canvas/GraphicRaycaster guard (defensive).

- [ ] **Step 9.4: Update wiki/systems/character-interaction.md**

Per `wiki/CLAUDE.md`:
- Bump frontmatter `updated:` to `2026-05-09`.
- Append to `## Change log`:
  ```
  - 2026-05-09 — Rule #33 dedup: PlayerController owns all E-key input; PlayerInteractionDetector reduced to proximity tracker + helper API (CurrentTarget, TriggerTapInteract, TriggerHoldMenu, SetPromptHoldProgress). Eliminated double-Interact on tap. — claude
  ```
- Refresh the `## Public API` section with the renamed `TriggerTapInteract` and the three new helpers.
- Refresh the `## Gotchas` section with a pointer to the new `wiki/gotchas/double-interact-rule-33-violation.md`.
- Update `## Sources` to point to the dedup spec (this plan's spec).

- [ ] **Step 9.5: Update wiki/systems/shops.md**

Per `wiki/CLAUDE.md`:
- Bump `updated:` to `2026-05-09`.
- Append to `## Change log`:
  ```
  - 2026-05-09 — UI_ShopBuyPanel prefab authored at Resources/UI/UI_ShopBuyPanel.prefab; row sub-prefab at Resources/UI/UI_ShopBuyRow.prefab. Defensive Awake Canvas/GraphicRaycaster guard added. — claude
  ```
- If the page has a `## Key classes / files` or `## State & persistence` section listing prefabs, add the new paths there.

- [ ] **Step 9.6: Create wiki/gotchas/double-interact-rule-33-violation.md**

Use the `wiki/_templates/gotcha.md` template if one exists; otherwise model on a recent gotcha page (e.g. `wiki/gotchas/host-progressive-freeze-debug-log-spam.md`). Frontmatter must include:

```yaml
---
title: "Double Interact on E tap (Rule #33 violation)"
created: 2026-05-09
updated: 2026-05-09
related:
  - character-interaction
sources:
  - "[[../../docs/superpowers/specs/2026-05-09-shop-buy-panel-and-interact-deduplication-design]]"
  - "[[../../docs/superpowers/plans/2026-05-09-shop-buy-panel-and-interact-deduplication]]"
---
```

Body sections:
- **Symptom:** ` [Furniture] ... utilise <name>.` log line appears TWICE per E tap. Cashier flow fires two `CharacterAction_BuyFromShop`. Storage panel re-Initializes (harmless — but masks the bug for a long time).
- **Root cause:** two `Input.GetKeyUp(KeyCode.E)` paths each call `.Interact()`. PlayerController.HandleEKeyUp is canonical; PlayerInteractionDetector.Update was a parallel violator.
- **Fix:** delete the detector's `Update()` E-key block; `PlayerController` is the only `Input.GetKey…` reader for player-character control. Detector exposes data + helper API only.
- **Regression detection:** any new `Input.GetKey*` call outside `PlayerController.cs` for player-character control violates Rule #33. Code review: grep `Input.GetKey` and audit each callsite.

- [ ] **Step 9.7: Update .claude/agents/character-system-specialist.md**

Refresh the **"Player Input Ownership"** or equivalent section to:
- Note that `PlayerController` is the sole reader of E-key (and all other gameplay input).
- Note the `_detector` field + `CurrentTarget`, `TriggerTapInteract`, `TriggerHoldMenu`, `SetPromptHoldProgress` helper API on `PlayerInteractionDetector`.
- Bump the agent's "last reviewed" date if it has one.

- [ ] **Step 9.8: Verify wiki lint passes**

If a wiki linter exists (`/lint` skill or equivalent), run it:

```
Skill: lint
```
Expected: zero errors / warnings on the changed pages. Fix anything flagged.

- [ ] **Step 9.9: Commit**

```bash
git add .agent/skills/interactable-system/SKILL.md \
        .agent/skills/player_ui/SKILL.md \
        wiki/systems/character-interaction.md \
        wiki/systems/shops.md \
        wiki/gotchas/double-interact-rule-33-violation.md \
        .claude/agents/character-system-specialist.md
git commit -m "$(cat <<'EOF'
docs: refresh skill / wiki / agent for E-key dedup + shop panel

Per project rules #28 (SKILL.md), #29 (agents), #29b (wiki):
- interactable-system + player_ui SKILLs reflect new dispatch flow
- character-interaction wiki page change-log + Public API + Gotchas
- shops wiki page change-log: prefab paths recorded
- new gotcha page guards against rule-#33 regression
- character-system-specialist agent refreshed

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Final verification

After Tasks 1–9 are committed, run the full Section 8 testing scenarios from the spec end-to-end. Each must verify on Host↔Client + Client↔Client + Host/Client↔NPC (rule #19):

- [ ] **F1.** Tap E on Crate (storage): exactly one panel-open call; exactly one `[Furniture] ... utilise Crate.` log.
- [ ] **F2.** Tap E on Cashier: exactly one `CharacterAction_BuyFromShop`; one `OpenBuyPanelClientRpc`; **shop buy panel appears**.
- [ ] **F3.** Hold E on Harvestable: opens `UI_HarvestInteractionMenu`; no tap-Interact fires; prompt fill animates.
- [ ] **F4.** Hold E on dialogue-capable `CharacterInteractable`: opens dialogue options menu; no tap-Interact fires.
- [ ] **F5.** Tap E on selected-but-out-of-range target: `PlayerInteractCommand` enqueues; on arrival, exactly one Interact fires.
- [ ] **F6.** Walk away from target during hold: prompt fill resets to 0; no menu opens.
- [ ] **F7.** ESC during hold: cancels cleanly (no Interact, no menu).
- [ ] **F8.** Tap E on Bed / chair / time clock / map door: each fires one effect per tap.
- [ ] **F9.** Shop buy panel: ScrollView doesn't intercept slot/stepper button clicks.
- [ ] **F10.** Shop buy panel renders at `sortingOrder=50`.
- [ ] **F11.** Shop buy panel cancel/confirm: cashier lock acquired/released exactly once; till credited exactly once on confirm.
- [ ] **F12.** Late joiner: cashier till + lock state propagate via existing `CashierNetSync`.
- [ ] **F13.** NPC-driven `.Interact()` paths still work (`MapTransitionDoor.cs:155`, `CharacterParty.cs:1143`).
- [ ] **F14.** Two players: each player's `_detector.CurrentTarget` is local; no cross-talk; remote players see no spurious prompts.

If any scenario fails: capture the failing log signature + reproduction steps, file a follow-up task, and fix before merging the branch.

---

## Self-review notes

**Spec coverage check** (each spec section maps to plan tasks):

| Spec section | Plan tasks |
|---|---|
| §1 Problem statement | covered by §F1–F14 reproduction recipes |
| §2 Architecture | Tasks 1–4 (Half A) + Tasks 5–7 (Half B) |
| §3 Half A: deduplication | Tasks 1–4 |
| §3.1 Detector loses Update E-key block | Task 4 Step 4.4 |
| §3.2 Detector keeps proximity + helpers | Tasks 1, 4 |
| §3.3 Detector new public API | Task 1 |
| §3.4 PlayerController _detector field | Task 2 |
| §3.5 HandleEKeyDown unchanged | (no task — explicitly verified unchanged) |
| §3.6 HandleEKeyHeld extended | Task 2 Step 2.3 |
| §3.7 HandleEKeyUp replaced | Task 3 Step 3.1 |
| §3.8 PlayerInteractCommand rename | Task 1 Step 1.3 |
| §3.9 Cross-cutting (chat-input gate, dev-mode) | Task 4 Step 4.4 (deletion of duplicates) |
| §4 Network validation pass | Task 8 |
| §5 UI_ShopBuyPanel.cs Awake guard | Task 5 |
| §6 Prefab assets | Tasks 6 (row), 7 (panel) |
| §7 Files | File Map at top of plan |
| §8 Testing scenarios | Final verification (F1–F14) |
| §9 Risks | mitigated inline within each task |
| §10 Open dependencies | none |

**Type / name consistency check:**
- `_detector` (PlayerController field) — Tasks 2, 3
- `CurrentTarget`, `TriggerTapInteract`, `TriggerHoldMenu`, `SetPromptHoldProgress` — Task 1 defines, Tasks 2–4 consume
- `_rowPrefab` (UI_ShopBuyPanel field) — Task 7 wires
- `UI_ShopBuyRow` (component name + prefab name) — Tasks 6, 7
- All prefab paths match the canonical `Assets/Resources/UI/...` directory

**Placeholder scan:** no TBDs, no "implement later", no "fill in details". Every code block contains complete code.

---

*End of plan.*
