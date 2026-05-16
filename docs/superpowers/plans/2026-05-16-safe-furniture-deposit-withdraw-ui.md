# SafeFurniture deposit/withdraw player UI — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship a player-facing UI that lets a player open a SafeFurniture, see balances per `CurrencyId`, and deposit/withdraw money to/from their `CharacterWallet` — server-authoritative, multiplayer-correct, NPC-parity-ready.

**Architecture:** Mirror StorageFurniture pattern — client-local panel open on `OnInteract`, mutations through `RequestDepositServerRpc` / `RequestWithdrawServerRpc` on `SafeFurnitureNetworkSync` that queue new `CharacterAction_DepositToSafe` / `CharacterAction_WithdrawFromSafe` (rule #22). UI uses the existing `_networkBalances` `NetworkList` replication (data side is already done) and `CharacterWallet`'s ClientRpc broadcast. Permissionless v1: anyone in the `InteractionZone` can transact (locks/keys/lockpicking are a future orthogonal system).

**Tech Stack:** Unity 6.x, C#, NGO (Netcode for GameObjects), Unity UI (uGUI + TMP), Unity Test Framework (EditMode where realistic). MCP tools available: `ai-game-developer__*` for Editor mutation, `ai-game-developer__console-get-logs` for compile checks, `ai-game-developer__tests-run` for EditMode test execution, `ai-game-developer__assets-prefab-*` for prefab authoring.

**Spec:** [docs/superpowers/specs/2026-05-16-safe-furniture-deposit-withdraw-ui-design.md](../specs/2026-05-16-safe-furniture-deposit-withdraw-ui-design.md)

---

## File map

**New files (scripts):**
- `Assets/Scripts/Character/CharacterActions/CharacterAction_DepositToSafe.cs`
- `Assets/Scripts/Character/CharacterActions/CharacterAction_WithdrawFromSafe.cs`
- `Assets/Scripts/UI/Furniture/UI_SafePanel.cs`
- `Assets/Scripts/UI/Furniture/UI_SafeCurrencyRow.cs`
- `Assets/Tests/EditMode/CharacterActions/CharacterAction_SafeTransferTests.cs` (only if base CharacterAction is mockable without NetworkManager — see Task 2 / Task 3)

**New files (Unity assets):**
- `Assets/Prefabs/UI/UI_SafePanel.prefab`
- `Assets/Prefabs/UI/UI_SafeCurrencyRow.prefab`

**Modified files (scripts):**
- `Assets/Scripts/World/Furniture/SafeFurniture.cs` — override `OnInteract`
- `Assets/Scripts/World/Furniture/SafeFurnitureNetworkSync.cs` — add 2 ServerRpcs + 1 ClientRpc
- `Assets/Scripts/UI/PlayerUI.cs` — add `OpenSafePanel` / `CloseSafePanel` + `SerializeField _safePanel`

**Modified files (Unity assets):**
- All Safe prefabs / scene instances — verify `FurnitureInteractable` + `InteractionZone` collider authored (Task 1)
- The scene that hosts `PlayerUI` — wire `_safePanel` field

**Documentation updates (final phase):**
- `.agent/skills/building_system/SKILL.md`
- `wiki/systems/commercial-treasury.md` (bump `updated:`, add change log line, refresh dep graph)
- `.claude/agents/building-furniture-specialist.md`

---

## Task 1: Verify SafeFurniture prefab has the proximity gate (Prereq Step 0)

Per spec §4, **before writing any code** confirm every SafeFurniture prefab and scene instance carries `FurnitureInteractable` + an `InteractionZone` collider. The proximity gate is non-negotiable (rule #36) — the client `OnInteract` path and the server-side anti-cheat re-validation both depend on it.

**Files:**
- Verify (read-only at first): every `*.prefab` containing a `SafeFurniture` component
- Verify: every scene with a baked Safe instance (most likely the test scenes hosting commercial buildings)

- [ ] **Step 1.1: Locate every SafeFurniture instance**

Run via MCP:
```
mcp__ai-game-developer__assets-find with searchFilter "t:Prefab SafeFurniture"
```
Also grep the project for direct refs:
```
Grep tool, pattern "SafeFurniture", glob "*.prefab"
Grep tool, pattern "SafeFurniture", glob "*.unity"
```
List every result. Expected: the Safe.prefab (or similarly named) plus any building prefab that has a Safe child (Forge / Shop / Farming Building / etc. — these may host safes via the BaseTreasury seed path).

- [ ] **Step 1.2: Inspect each instance for `FurnitureInteractable` + `InteractionZone`**

For each prefab found:
```
mcp__ai-game-developer__gameobject-component-list-all  (to confirm component names)
mcp__ai-game-developer__assets-prefab-open with the prefab path
mcp__ai-game-developer__gameobject-find with name containing "Safe"
mcp__ai-game-developer__gameobject-component-get with the Safe GameObject + componentName "FurnitureInteractable"
mcp__ai-game-developer__gameobject-component-get with the Safe GameObject + componentName "InteractableObject"  (the base)
```
Then read `_interactionZone` field on the `InteractableObject` — it must reference a `Collider` (BoxCollider preferred, `isTrigger=true`).

Expected outcomes per prefab:
- ✅ Has both → no change.
- ❌ Missing `FurnitureInteractable` → **stop and ask Kevin** before modifying. The Safe was authored without the standard interactable wiring; we need to understand why before adding it.
- ❌ Has `FurnitureInteractable` but no `InteractionZone` collider → add a child `BoxCollider` with `isTrigger=true`. Size it generously: roughly `(2 × Safe.x, 2 × Safe.y, 2 × Safe.z)` so the NavMesh-sampled landing point per rule #36 always falls inside. Wire it into `InteractableObject._interactionZone`.

- [ ] **Step 1.3: Close prefabs, refresh asset DB**

```
mcp__ai-game-developer__assets-prefab-close (per prefab opened)
mcp__ai-game-developer__assets-refresh
```

- [ ] **Step 1.4: Commit (only if changes were made)**

```bash
git add Assets/Prefabs/.../SafeFurniture<prefab>.prefab
git commit -m "chore(safe): ensure InteractionZone authored on safe prefab(s) for Interact gate"
```

If no changes were needed (every prefab already had the gate), skip this step and add a note in the next commit message that the audit found everything in order.

---

## Task 2: `CharacterAction_DepositToSafe` server-side action

The deposit half of the rule #22 NPC parity pair. Atomic: pull from `CharacterWallet`, push to `SafeFurniture`. Both writes are server-side; if the wallet pull fails (insufficient coins), the safe push must NOT happen.

**Files:**
- Create: `Assets/Scripts/Character/CharacterActions/CharacterAction_DepositToSafe.cs`
- Read first (do not modify): `Assets/Scripts/Character/CharacterActions/CharacterAction.cs` and the closest existing peer action (likely `CharacterStoreInFurnitureAction.cs` — find via `Grep`). Read these to understand the base shape (constructor, `OnStart` / `Execute` / `OnFinish` lifecycle, duration, server-only invariants).

- [ ] **Step 2.1: Read the base class and the closest existing action**

Use `Glob "Assets/Scripts/Character/CharacterActions/CharacterAction*.cs"` to enumerate. Read:
- `CharacterAction.cs` (the base) — note constructor signature, abstract methods, `Duration`, server-only invariants.
- The store-in-furniture / take-from-furniture action (closest peer) — note how it wraps a single-shot mutation with `Duration = 0` (or near-zero) and where the mutation fires (in `OnStart` or `Execute`).

Write down (in your head or scratch) the exact method names and signatures you'll override.

- [ ] **Step 2.2: Write the action class**

Create `Assets/Scripts/Character/CharacterActions/CharacterAction_DepositToSafe.cs`:

```csharp
using MWI.Economy;
using UnityEngine;

namespace MWI.Character.Actions
{
    /// <summary>
    /// Server-side atomic transfer: pulls `Amount` of `Currency` from the character's
    /// CharacterWallet and credits it to the target SafeFurniture. Pairs with
    /// CharacterAction_WithdrawFromSafe for the inverse direction. Queued by
    /// SafeFurnitureNetworkSync.RequestDepositServerRpc (player UI path); the same
    /// action can be queued by future NPC banker / treasurer AI (rule #22).
    /// </summary>
    public sealed class CharacterAction_DepositToSafe : CharacterAction
    {
        private readonly SafeFurniture _safe;
        private readonly CurrencyId _currency;
        private readonly int _amount;

        public CharacterAction_DepositToSafe(SafeFurniture safe, CurrencyId currency, int amount)
            : base(/* duration = */ 0f)
        {
            _safe = safe;
            _currency = currency;
            _amount = amount;
        }

        protected override void OnStart()
        {
            // Server-only. RPC caller has already validated zone + amount + sender.
            if (_safe == null) { Finish(); return; }
            if (_amount <= 0) { Finish(); return; }

            var wallet = Character.CharacterWallet;
            if (wallet == null) { Finish(); return; }

            // Atomic: only credit the safe if the wallet debit succeeded.
            if (!wallet.RemoveCoins(_currency, _amount, "safe-deposit"))
            {
                // Insufficient wallet. Notify via the requesting NetSync's ClientRpc.
                _safe.NetSync?.NotifyOperationResult(
                    Character.OwnerClientId,
                    success: false,
                    reason: "insufficient-wallet");
                Finish();
                return;
            }

            _safe.Credit(_currency, _amount, "player-deposit");
            // Success path: rely on safe.OnBalanceChanged + wallet broadcast for UI repaint.
            Finish();
        }
    }
}
```

**Adjust** the base class invocation, override signatures, and `Finish()` call to match what you found in Step 2.1. The above is the design intent — exact method names depend on the base API.

**Why this shape:** Spec §4 + §9. The atomic guard (wallet first, then safe) prevents the "double withdraw" hazard. The failure ClientRpc routes through the NetSync because the action doesn't own a network channel itself.

- [ ] **Step 2.3: Compile check**

```
mcp__ai-game-developer__console-clear-logs
mcp__ai-game-developer__assets-refresh
mcp__ai-game-developer__console-get-logs
```
Expected: zero errors. If any compile error mentions `Character.CharacterWallet`, `SafeFurniture.NetSync`, `Character.OwnerClientId`, or `NotifyOperationResult`, fix by:
- Confirm the actual property name on `Character` (e.g., maybe `Wallet` not `CharacterWallet`). Read `Character.cs` to verify.
- `SafeFurniture.NetSync` may not exist yet as a public accessor — if not, add it in Task 4 and leave a temporary `_safe.GetComponent<SafeFurnitureNetworkSync>()` here for now.
- `NotifyOperationResult` doesn't exist yet — it's defined in Task 4. Stub it here as a `// TODO Task 4` and the compile will still fail until Task 4. **Acceptable interim state.** Mark it as a known gap.

- [ ] **Step 2.4: Commit**

```bash
git add Assets/Scripts/Character/CharacterActions/CharacterAction_DepositToSafe.cs
git commit -m "feat(character-action): add CharacterAction_DepositToSafe atomic transfer"
```

Note: the compile may not be green yet because `NotifyOperationResult` is added in Task 4. That's intentional — we commit the action skeleton first and resolve the cross-dependency next.

---

## Task 3: `CharacterAction_WithdrawFromSafe` server-side action

Inverse of Task 2. Atomic: pull from `SafeFurniture`, push to `CharacterWallet`. Same compile dependencies.

**Files:**
- Create: `Assets/Scripts/Character/CharacterActions/CharacterAction_WithdrawFromSafe.cs`

- [ ] **Step 3.1: Write the action class**

Create the file with the same shape as Task 2.2, but inverted:

```csharp
using MWI.Economy;
using UnityEngine;

namespace MWI.Character.Actions
{
    /// <summary>
    /// Server-side atomic transfer: debits `Amount` of `Currency` from the target
    /// SafeFurniture and credits it to the character's CharacterWallet. See
    /// CharacterAction_DepositToSafe for the inverse direction and the rule #22 rationale.
    /// </summary>
    public sealed class CharacterAction_WithdrawFromSafe : CharacterAction
    {
        private readonly SafeFurniture _safe;
        private readonly CurrencyId _currency;
        private readonly int _amount;

        public CharacterAction_WithdrawFromSafe(SafeFurniture safe, CurrencyId currency, int amount)
            : base(/* duration = */ 0f)
        {
            _safe = safe;
            _currency = currency;
            _amount = amount;
        }

        protected override void OnStart()
        {
            if (_safe == null) { Finish(); return; }
            if (_amount <= 0) { Finish(); return; }

            var wallet = Character.CharacterWallet;
            if (wallet == null) { Finish(); return; }

            // Atomic: safe first, then wallet. If safe debit fails, do not credit wallet.
            if (!_safe.TryDebit(_currency, _amount, "player-withdraw"))
            {
                _safe.NetSync?.NotifyOperationResult(
                    Character.OwnerClientId,
                    success: false,
                    reason: "insufficient-safe");
                Finish();
                return;
            }

            wallet.AddCoins(_currency, _amount, "safe-withdraw");
            Finish();
        }
    }
}
```

Same caveats as Task 2.2 — base class shape adapts to the real API found in Step 2.1.

- [ ] **Step 3.2: Compile check** (same as Step 2.3)

- [ ] **Step 3.3: Commit**

```bash
git add Assets/Scripts/Character/CharacterActions/CharacterAction_WithdrawFromSafe.cs
git commit -m "feat(character-action): add CharacterAction_WithdrawFromSafe atomic transfer"
```

---

## Task 4: `SafeFurnitureNetworkSync` — add deposit/withdraw RPCs

The network surface that bridges the player UI to the server-side actions. Two ServerRpcs (deposit + withdraw) and one ClientRpc (operation result, targeted to the requester only).

**Files:**
- Modify: `Assets/Scripts/World/Furniture/SafeFurnitureNetworkSync.cs`

- [ ] **Step 4.1: Read the file**

Read `SafeFurnitureNetworkSync.cs` end-to-end. Note:
- The existing NGO RPC attribute style (legacy `[ServerRpc(RequireOwnership=false)]` or newer `[Rpc(SendTo.Server)]`). **Match what's already in the file** — don't introduce a new style. The audit report noted `StorageFurnitureNetworkSync` uses `[Rpc(SendTo.Server)]`; check whether `SafeFurnitureNetworkSync` does the same or not.
- Where `_safe` is stored (the back-pointer to the `SafeFurniture` MonoBehaviour).
- Existing `OnNetworkSpawn` to confirm late-joiner replication is already wired (spec §7).

- [ ] **Step 4.2: Add the public `NotifyOperationResult` helper**

Inside `SafeFurnitureNetworkSync`, add:

```csharp
/// <summary>
/// Server-only. Fires OperationResultClientRpc targeted to a single client (the requester).
/// Called from CharacterAction_DepositToSafe / CharacterAction_WithdrawFromSafe on failure paths.
/// </summary>
public void NotifyOperationResult(ulong targetClientId, bool success, string reason)
{
    if (!IsServer) return;
    var rpcParams = new ClientRpcParams
    {
        Send = new ClientRpcSendParams { TargetClientIds = new[] { targetClientId } }
    };
    OperationResultClientRpc(success, new FixedString64Bytes(reason ?? string.Empty), rpcParams);
}

[ClientRpc]
private void OperationResultClientRpc(bool success, FixedString64Bytes reason, ClientRpcParams rpcParams = default)
{
    // Fires only on the requester. Route to the open panel if any.
    if (PlayerUI.Instance != null)
    {
        PlayerUI.Instance.OnSafeOperationResult(_safe, success, reason.ToString());
    }
}
```

If the file already uses `[Rpc(SendTo.Server)]` (newer style), use the matching `[Rpc(SendTo.SpecifiedInParams)]` equivalent for the client-targeted RPC instead. Stay consistent with the file.

- [ ] **Step 4.3: Add the deposit ServerRpc**

```csharp
[ServerRpc(RequireOwnership = false)]
public void RequestDepositServerRpc(NetworkBehaviourReference characterRef, int currencyRawId, int amount, ServerRpcParams p = default)
{
    if (!IsServer) return;

    // 1. Resolve character.
    if (!characterRef.TryGet(out Character character) || character == null)
    {
        if (Debug.isDebugBuild) Debug.LogWarning($"[SafeFurnitureNetworkSync] RequestDepositServerRpc: character ref failed to resolve.");
        return;
    }

    // 2. Anti-cheat: sender must own the character they claim to be.
    ulong senderClientId = p.Receive.SenderClientId;
    if (character.OwnerClientId != senderClientId)
    {
        if (Debug.isDebugBuild) Debug.LogWarning($"[SafeFurnitureNetworkSync] RequestDepositServerRpc: sender {senderClientId} does not own character {character.OwnerClientId}.");
        return;
    }

    // 3. Anti-cheat: proximity (rule #36).
    var interactable = _safe.GetComponent<InteractableObject>();
    if (interactable == null || !interactable.IsCharacterInInteractionZone(character))
    {
        NotifyOperationResult(senderClientId, success: false, reason: "out-of-zone");
        return;
    }

    // 4. Amount sanity.
    if (amount <= 0)
    {
        NotifyOperationResult(senderClientId, success: false, reason: "invalid-amount");
        return;
    }

    // 5. Queue the server-side action. Wallet-balance check lives inside the action
    //    (RemoveCoins returns false on shortfall) — we don't double-check here to keep
    //    the source of truth in CharacterWallet.
    var currency = new CurrencyId(currencyRawId);
    character.CharacterActions.ExecuteAction(new CharacterAction_DepositToSafe(_safe, currency, amount));
}
```

- [ ] **Step 4.4: Add the withdraw ServerRpc**

Inverse shape — same validation chain, queues `CharacterAction_WithdrawFromSafe`:

```csharp
[ServerRpc(RequireOwnership = false)]
public void RequestWithdrawServerRpc(NetworkBehaviourReference characterRef, int currencyRawId, int amount, ServerRpcParams p = default)
{
    if (!IsServer) return;

    if (!characterRef.TryGet(out Character character) || character == null)
    {
        if (Debug.isDebugBuild) Debug.LogWarning($"[SafeFurnitureNetworkSync] RequestWithdrawServerRpc: character ref failed to resolve.");
        return;
    }

    ulong senderClientId = p.Receive.SenderClientId;
    if (character.OwnerClientId != senderClientId)
    {
        if (Debug.isDebugBuild) Debug.LogWarning($"[SafeFurnitureNetworkSync] RequestWithdrawServerRpc: sender {senderClientId} does not own character {character.OwnerClientId}.");
        return;
    }

    var interactable = _safe.GetComponent<InteractableObject>();
    if (interactable == null || !interactable.IsCharacterInInteractionZone(character))
    {
        NotifyOperationResult(senderClientId, success: false, reason: "out-of-zone");
        return;
    }

    if (amount <= 0)
    {
        NotifyOperationResult(senderClientId, success: false, reason: "invalid-amount");
        return;
    }

    var currency = new CurrencyId(currencyRawId);
    character.CharacterActions.ExecuteAction(new CharacterAction_WithdrawFromSafe(_safe, currency, amount));
}
```

- [ ] **Step 4.5: Expose `_safe` as `NetSync` getter on `SafeFurniture` (if not already public)**

The action classes (Task 2/3) call `_safe.NetSync?.NotifyOperationResult(...)`. Verify whether `SafeFurniture` already has a `NetSync` public accessor. If not, add one:

In `Assets/Scripts/World/Furniture/SafeFurniture.cs`:
```csharp
private SafeFurnitureNetworkSync _netSync;
public SafeFurnitureNetworkSync NetSync
{
    get
    {
        if (_netSync == null) _netSync = GetComponent<SafeFurnitureNetworkSync>();
        return _netSync;
    }
}
```

If a `NetSync` accessor already exists with the same shape, skip this step.

- [ ] **Step 4.6: Compile check**

```
mcp__ai-game-developer__console-clear-logs
mcp__ai-game-developer__assets-refresh
mcp__ai-game-developer__console-get-logs
```
Expected: zero errors. The action files from Task 2 and Task 3 should now compile cleanly because `NotifyOperationResult` and `NetSync` exist.

If errors remain:
- `PlayerUI.OnSafeOperationResult` is referenced in Step 4.2 but defined in Task 7 — that's an expected cross-dependency. Either stub `OnSafeOperationResult` on `PlayerUI` now (empty body) or accept the temporary compile error until Task 7. **Stub now** to keep main green:

Add to `PlayerUI.cs` (one line):
```csharp
public void OnSafeOperationResult(SafeFurniture safe, bool success, string reason) { /* fleshed out in Task 7 */ }
```

- [ ] **Step 4.7: Commit**

```bash
git add Assets/Scripts/World/Furniture/SafeFurnitureNetworkSync.cs Assets/Scripts/World/Furniture/SafeFurniture.cs Assets/Scripts/UI/PlayerUI.cs
git commit -m "feat(safe-net): add deposit/withdraw ServerRpcs + result ClientRpc"
```

---

## Task 5: `SafeFurniture.OnInteract` override

The client-local entry point. Pressing E on a Safe in the InteractionZone now opens the UI panel (today: does nothing).

**Files:**
- Modify: `Assets/Scripts/World/Furniture/SafeFurniture.cs`

- [ ] **Step 5.1: Read the existing `OnInteract` in `Furniture` base + the precedent**

Read `Assets/Scripts/World/Furniture/Furniture.cs` (or wherever the base `OnInteract` lives) to confirm the method signature. Read `StorageFurniture.OnInteract` (which is the canonical client-local-open pattern) for the exact shape:

```
Grep tool, pattern "OnInteract", glob "Assets/Scripts/World/Furniture/*.cs"
```

Expected `StorageFurniture` shape (per the audit report):
```csharp
public override bool OnInteract(Character interactor)
{
    if (!interactor.IsOwner || !interactor.IsPlayer()) return false;
    PlayerUI.Instance.OpenStoragePanel(this, interactor);
    return true;
}
```

- [ ] **Step 5.2: Add the override to `SafeFurniture`**

In `Assets/Scripts/World/Furniture/SafeFurniture.cs`:
```csharp
public override bool OnInteract(Character interactor)
{
    if (interactor == null) return false;
    if (!interactor.IsOwner || !interactor.IsPlayer()) return false;
    PlayerUI.Instance?.OpenSafePanel(this, interactor);
    return true;
}
```

If the base class uses `void` instead of `bool`, drop the `return`. Match the base signature exactly.

- [ ] **Step 5.3: Compile check** (same MCP pattern as Step 4.6)

Expected: one new dependency on `PlayerUI.OpenSafePanel(SafeFurniture, Character)` — that method is added in Task 7. Stub it now to keep compile green:

In `Assets/Scripts/UI/PlayerUI.cs`:
```csharp
public void OpenSafePanel(SafeFurniture safe, Character interactor) { /* fleshed out in Task 7 */ }
public void CloseSafePanel() { /* fleshed out in Task 7 */ }
```

Re-run the compile check. Expected: zero errors.

- [ ] **Step 5.4: Commit**

```bash
git add Assets/Scripts/World/Furniture/SafeFurniture.cs Assets/Scripts/UI/PlayerUI.cs
git commit -m "feat(safe): wire OnInteract to open the (stub) safe panel"
```

---

## Task 6: `UI_SafeCurrencyRow.cs` — per-currency row script

The row that owns one currency's deposit + withdraw UI. Stateless about the safe (parent panel passes references); fully reactive to balance changes.

**Files:**
- Create: `Assets/Scripts/UI/Furniture/UI_SafeCurrencyRow.cs`

- [ ] **Step 6.1: Read peer row scripts for naming conventions**

```
Grep tool, pattern "MonoBehaviour", glob "Assets/Scripts/UI/Shop/UI_*Row*.cs"
Glob "Assets/Scripts/UI/**/UI_*Row*.cs"
```
Look at one or two existing rows (UI_ShopBuyRow if it exists, or any other row script) to mirror the conventions for serialized fields (TMP_Text, TMP_InputField, Button references) and event wiring.

- [ ] **Step 6.2: Write the row script**

Create `Assets/Scripts/UI/Furniture/UI_SafeCurrencyRow.cs`:

```csharp
using System;
using MWI.Economy;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MWI.UI.Furniture
{
    /// <summary>
    /// One row of the SafeFurniture player UI — one CurrencyId. Owns the deposit/withdraw
    /// inputs + submit buttons for a single currency. Bound by UI_SafePanel.
    /// All gameplay effect routes through the parent panel's RPC call surface.
    /// </summary>
    public sealed class UI_SafeCurrencyRow : MonoBehaviour
    {
        [Header("Identity")]
        [SerializeField] private TMP_Text _currencyLabel;
        [SerializeField] private TMP_Text _safeBalanceLabel;
        [SerializeField] private TMP_Text _walletBalanceLabel;

        [Header("Deposit")]
        [SerializeField] private TMP_InputField _depositInput;
        [SerializeField] private Button _depositPlusButton;
        [SerializeField] private Button _depositMinusButton;
        [SerializeField] private Button _depositMaxButton;
        [SerializeField] private Button _depositSubmitButton;

        [Header("Withdraw")]
        [SerializeField] private TMP_InputField _withdrawInput;
        [SerializeField] private Button _withdrawPlusButton;
        [SerializeField] private Button _withdrawMinusButton;
        [SerializeField] private Button _withdrawMaxButton;
        [SerializeField] private Button _withdrawSubmitButton;

        // State
        private CurrencyId _currency;
        private Func<int> _getSafeBalance;
        private Func<int> _getWalletBalance;
        private Action<int> _onDepositSubmit;
        private Action<int> _onWithdrawSubmit;

        public void Initialize(
            CurrencyId currency,
            string displayName,
            Func<int> getSafeBalance,
            Func<int> getWalletBalance,
            Action<int> onDepositSubmit,
            Action<int> onWithdrawSubmit)
        {
            _currency = currency;
            _getSafeBalance = getSafeBalance;
            _getWalletBalance = getWalletBalance;
            _onDepositSubmit = onDepositSubmit;
            _onWithdrawSubmit = onWithdrawSubmit;

            if (_currencyLabel != null) _currencyLabel.text = displayName;

            // Wire buttons
            _depositPlusButton?.onClick.AddListener(() => Step(_depositInput, +1));
            _depositMinusButton?.onClick.AddListener(() => Step(_depositInput, -1));
            _depositMaxButton?.onClick.AddListener(() => _depositInput.text = _getWalletBalance().ToString());
            _depositSubmitButton?.onClick.AddListener(OnDepositClicked);

            _withdrawPlusButton?.onClick.AddListener(() => Step(_withdrawInput, +1));
            _withdrawMinusButton?.onClick.AddListener(() => Step(_withdrawInput, -1));
            _withdrawMaxButton?.onClick.AddListener(() => _withdrawInput.text = _getSafeBalance().ToString());
            _withdrawSubmitButton?.onClick.AddListener(OnWithdrawClicked);

            _depositInput?.onValueChanged.AddListener(_ => RefreshSubmitAvailability());
            _withdrawInput?.onValueChanged.AddListener(_ => RefreshSubmitAvailability());

            Refresh();
        }

        public void Refresh()
        {
            if (_safeBalanceLabel != null) _safeBalanceLabel.text = $"Safe: {FormatAmount(_getSafeBalance())}";
            if (_walletBalanceLabel != null) _walletBalanceLabel.text = $"Wallet: {FormatAmount(_getWalletBalance())}";
            RefreshSubmitAvailability();
        }

        private void RefreshSubmitAvailability()
        {
            int dep = ParseAmount(_depositInput);
            int wd  = ParseAmount(_withdrawInput);
            if (_depositSubmitButton != null) _depositSubmitButton.interactable = (dep > 0 && dep <= _getWalletBalance());
            if (_withdrawSubmitButton != null) _withdrawSubmitButton.interactable = (wd > 0 && wd <= _getSafeBalance());
        }

        private void OnDepositClicked()
        {
            int amount = ParseAmount(_depositInput);
            if (amount <= 0) return;
            _onDepositSubmit?.Invoke(amount);
            _depositInput.text = "0";
        }

        private void OnWithdrawClicked()
        {
            int amount = ParseAmount(_withdrawInput);
            if (amount <= 0) return;
            _onWithdrawSubmit?.Invoke(amount);
            _withdrawInput.text = "0";
        }

        private static void Step(TMP_InputField field, int delta)
        {
            if (field == null) return;
            int cur = int.TryParse(field.text, out int n) ? n : 0;
            int next = Mathf.Max(0, cur + delta);
            field.text = next.ToString();
        }

        private static int ParseAmount(TMP_InputField field)
        {
            if (field == null) return 0;
            if (!int.TryParse(field.text, out int n)) return 0;
            return Mathf.Max(0, n);
        }

        private static string FormatAmount(int v) => v.ToString("N0");
    }
}
```

**Why this shape:** Spec §6. Row is decoupled from `SafeFurniture` / `CharacterWallet` directly — receives balance getters + submit callbacks from the parent panel. This keeps the row testable in isolation and makes the panel the single owner of the safe/wallet refs.

- [ ] **Step 6.3: Compile check** (MCP pattern). Expected: zero errors.

- [ ] **Step 6.4: Commit**

```bash
git add Assets/Scripts/UI/Furniture/UI_SafeCurrencyRow.cs
git commit -m "feat(ui): UI_SafeCurrencyRow per-currency deposit/withdraw row"
```

---

## Task 7: `UI_SafePanel.cs` + `PlayerUI` integration

The panel that owns the safe reference, builds rows, subscribes to balance events, and forwards row submits to the NetSync RPCs.

**Files:**
- Create: `Assets/Scripts/UI/Furniture/UI_SafePanel.cs`
- Modify: `Assets/Scripts/UI/PlayerUI.cs` (replace the stubs from Task 4 + Task 5 with real bodies)

- [ ] **Step 7.1: Read the StorageFurniture panel for the exact lifecycle**

Read `Assets/Scripts/UI/WorldUI/UI_StorageFurniturePanel.cs` end-to-end. Note:
- `Initialize(...)` shape — what gets passed in.
- How it subscribes to `OnInventoryChanged` and the bag/hands events.
- How it polls `IsCharacterInInteractionZone` for auto-close.
- How it unsubscribes in `OnDisable` / `OnDestroy` (rule #16).
- How it handles `ESC` / close button / target despawn.

Match the structure beat-for-beat — the goal is "this looks like the storage panel, but for a safe."

- [ ] **Step 7.2: Write `UI_SafePanel.cs`**

Create `Assets/Scripts/UI/Furniture/UI_SafePanel.cs`:

```csharp
using System.Collections.Generic;
using MWI.Economy;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace MWI.UI.Furniture
{
    /// <summary>
    /// Player UI panel for SafeFurniture. Opens via PlayerUI.OpenSafePanel from
    /// SafeFurniture.OnInteract. Builds one UI_SafeCurrencyRow per CurrencyId
    /// present in the safe. Closes on out-of-zone / ESC / safe despawn.
    /// </summary>
    public sealed class UI_SafePanel : MonoBehaviour
    {
        [Header("Header")]
        [SerializeField] private TMP_Text _titleLabel;
        [SerializeField] private Button _closeButton;

        [Header("Rows")]
        [SerializeField] private RectTransform _rowContainer;
        [SerializeField] private UI_SafeCurrencyRow _rowPrefab;

        [Header("Feedback")]
        [SerializeField] private TMP_Text _statusLabel;          // transient toast for failures
        [SerializeField] private float _statusVisibleSeconds = 3f;

        // State
        private SafeFurniture _safe;
        private Character _customer;
        private float _autoClosePollTimer;
        private float _statusHideAt;
        private readonly Dictionary<CurrencyId, UI_SafeCurrencyRow> _rows = new();

        private const float AutoClosePollInterval = 1f; // rule #34: cheap 1Hz poll

        public void Initialize(SafeFurniture safe, Character customer)
        {
            _safe = safe;
            _customer = customer;

            if (_titleLabel != null) _titleLabel.text = "Safe"; // role-based label can come later

            _closeButton?.onClick.RemoveAllListeners();
            _closeButton?.onClick.AddListener(Close);

            // Subscribe to authoritative state.
            _safe.OnBalanceChanged += HandleSafeBalanceChanged;
            _customer.CharacterWallet.OnBalanceChanged += HandleWalletBalanceChanged;
            if (_safe.TryGetComponent(out NetworkObject no))
                no.OnNetworkDespawn += HandleSafeDespawned;

            RebuildRows();
            gameObject.SetActive(true);
        }

        private void OnDisable()
        {
            UnbindAll();
            ClearRows();
        }

        private void OnDestroy()
        {
            UnbindAll();
        }

        private void UnbindAll()
        {
            if (_safe != null)
            {
                _safe.OnBalanceChanged -= HandleSafeBalanceChanged;
                if (_safe.TryGetComponent(out NetworkObject no))
                    no.OnNetworkDespawn -= HandleSafeDespawned;
            }
            if (_customer != null && _customer.CharacterWallet != null)
                _customer.CharacterWallet.OnBalanceChanged -= HandleWalletBalanceChanged;
        }

        private void Update()
        {
            // Auto-close on out-of-zone (1Hz cadence).
            _autoClosePollTimer += Time.unscaledDeltaTime; // rule #26: UI uses unscaled time
            if (_autoClosePollTimer >= AutoClosePollInterval)
            {
                _autoClosePollTimer = 0f;
                if (_safe == null || _customer == null) { Close(); return; }
                var interactable = _safe.GetComponent<InteractableObject>();
                if (interactable != null && !interactable.IsCharacterInInteractionZone(_customer))
                {
                    Close();
                    return;
                }
            }

            // ESC closes.
            if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }

            // Hide status toast on timeout.
            if (_statusLabel != null && _statusLabel.gameObject.activeSelf && Time.unscaledTime >= _statusHideAt)
                _statusLabel.gameObject.SetActive(false);
        }

        private void RebuildRows()
        {
            ClearRows();
            if (_safe == null) return;

            // Iterate the safe's currencies. safe.Balances allocates per call — fine at
            // panel-open cadence (rule #34: not a hot path).
            foreach (var entry in _safe.Balances)
            {
                var row = Instantiate(_rowPrefab, _rowContainer);
                row.Initialize(
                    entry.Currency,
                    DisplayNameFor(entry.Currency),
                    getSafeBalance: () => _safe != null ? _safe.GetBalance(entry.Currency) : 0,
                    getWalletBalance: () => _customer != null ? _customer.CharacterWallet.GetBalance(entry.Currency) : 0,
                    onDepositSubmit: amount => SubmitDeposit(entry.Currency, amount),
                    onWithdrawSubmit: amount => SubmitWithdraw(entry.Currency, amount));
                _rows[entry.Currency] = row;
            }

            // If safe has zero entries (fresh None-role safe), show at least Default so the
            // player can seed it from their wallet.
            if (_rows.Count == 0)
            {
                var c = CurrencyId.Default;
                var row = Instantiate(_rowPrefab, _rowContainer);
                row.Initialize(
                    c,
                    DisplayNameFor(c),
                    getSafeBalance: () => _safe != null ? _safe.GetBalance(c) : 0,
                    getWalletBalance: () => _customer != null ? _customer.CharacterWallet.GetBalance(c) : 0,
                    onDepositSubmit: amount => SubmitDeposit(c, amount),
                    onWithdrawSubmit: amount => SubmitWithdraw(c, amount));
                _rows[c] = row;
            }
        }

        private void ClearRows()
        {
            foreach (var row in _rows.Values)
                if (row != null) Destroy(row.gameObject);
            _rows.Clear();
        }

        private void HandleSafeBalanceChanged()
        {
            // Refresh existing rows; rebuild fully if a new currency appeared.
            bool needsRebuild = false;
            foreach (var entry in _safe.Balances)
            {
                if (!_rows.ContainsKey(entry.Currency)) { needsRebuild = true; break; }
            }
            if (needsRebuild) RebuildRows();
            else foreach (var row in _rows.Values) row.Refresh();
        }

        private void HandleWalletBalanceChanged(CurrencyId currency, int oldVal, int newVal)
        {
            if (_rows.TryGetValue(currency, out var row)) row.Refresh();
        }

        private void HandleSafeDespawned()
        {
            Close();
        }

        private void SubmitDeposit(CurrencyId currency, int amount)
        {
            if (_safe?.NetSync == null || _customer == null) return;
            var charRef = new NetworkBehaviourReference(_customer);
            _safe.NetSync.RequestDepositServerRpc(charRef, currency.Id, amount);
        }

        private void SubmitWithdraw(CurrencyId currency, int amount)
        {
            if (_safe?.NetSync == null || _customer == null) return;
            var charRef = new NetworkBehaviourReference(_customer);
            _safe.NetSync.RequestWithdrawServerRpc(charRef, currency.Id, amount);
        }

        public void OnOperationResult(bool success, string reason)
        {
            if (success || _statusLabel == null) return;
            _statusLabel.text = TranslateReason(reason);
            _statusLabel.gameObject.SetActive(true);
            _statusHideAt = Time.unscaledTime + _statusVisibleSeconds;
        }

        public void Close()
        {
            UnbindAll();
            ClearRows();
            gameObject.SetActive(false);
        }

        private static string DisplayNameFor(CurrencyId c)
        {
            // v1: only Default exists. Kingdom currencies will need a registry.
            if (c.Id == CurrencyId.Default.Id) return "Coins";
            return $"Currency #{c.Id}";
        }

        private static string TranslateReason(string raw) => raw switch
        {
            "insufficient-wallet" => "Not enough coins in wallet.",
            "insufficient-safe"   => "Not enough coins in safe.",
            "out-of-zone"         => "Too far from the safe.",
            "invalid-amount"      => "Invalid amount.",
            _                     => string.IsNullOrEmpty(raw) ? "Failed." : raw,
        };
    }
}
```

**Why these shapes:**
- Subscribe-on-Init / unsubscribe-on-Disable: rule #16.
- `Time.unscaledDeltaTime` + `Time.unscaledTime`: rule #26 (UI ignores GameSpeedController).
- 1 Hz auto-close poll: rule #34 (cheap, no per-frame zone math).
- All log paths can be added gated behind `if (Debug.isDebugBuild)` later if needed; the initial version emits no Update-rate logs (rule #34).

- [ ] **Step 7.3: Replace the PlayerUI stubs with real bodies**

In `Assets/Scripts/UI/PlayerUI.cs`:

```csharp
[Header("Safe panel")]
[SerializeField] private UI_SafePanel _safePanel;

public void OpenSafePanel(SafeFurniture safe, Character interactor)
{
    if (safe == null || interactor == null || _safePanel == null) return;
    _safePanel.Initialize(safe, interactor);
}

public void CloseSafePanel()
{
    if (_safePanel == null) return;
    _safePanel.Close();
}

public void OnSafeOperationResult(SafeFurniture safe, bool success, string reason)
{
    if (_safePanel == null) return;
    _safePanel.OnOperationResult(success, reason);
}
```

Replace the temporary stubs added in Task 4 / Task 5 with these real bodies. Mirror existing PlayerUI conventions for `[Header]` grouping.

- [ ] **Step 7.4: Compile check** (MCP pattern). Expected: zero errors.

- [ ] **Step 7.5: Commit**

```bash
git add Assets/Scripts/UI/Furniture/UI_SafePanel.cs Assets/Scripts/UI/PlayerUI.cs
git commit -m "feat(ui): UI_SafePanel + PlayerUI integration"
```

---

## Task 8: Author the UI prefabs (Unity Editor work via MCP)

Two prefabs: `UI_SafeCurrencyRow.prefab` and `UI_SafePanel.prefab`. The panel hosts the row prefab in its `_rowPrefab` slot and a `RectTransform` container.

**Files:**
- Create: `Assets/Prefabs/UI/UI_SafeCurrencyRow.prefab`
- Create: `Assets/Prefabs/UI/UI_SafePanel.prefab`

If the project stores UI prefabs elsewhere, follow the existing convention — check where `UI_StorageFurniturePanel.prefab` lives via `assets-find searchFilter "t:Prefab UI_StorageFurniturePanel"` and place the new prefabs in the same folder.

- [ ] **Step 8.1: Find the existing UI prefab folder**

```
mcp__ai-game-developer__assets-find with searchFilter "t:Prefab UI_StorageFurniturePanel"
```
Note the parent folder. Place the new safe prefabs alongside it.

- [ ] **Step 8.2: Create `UI_SafeCurrencyRow.prefab`**

Author manually via Unity Editor (preferred — easier to wire TMP_Text fonts and visual styling correctly) OR via MCP. The minimum structure:

```
UI_SafeCurrencyRow (RectTransform + Horizontal/VerticalLayoutGroup + UI_SafeCurrencyRow script)
├── CurrencyLabel (TMP_Text)        → wired to _currencyLabel
├── SafeBalanceLabel (TMP_Text)     → _safeBalanceLabel
├── WalletBalanceLabel (TMP_Text)   → _walletBalanceLabel
├── DepositGroup
│   ├── Input (TMP_InputField)      → _depositInput
│   ├── PlusBtn (Button)            → _depositPlusButton
│   ├── MinusBtn (Button)           → _depositMinusButton
│   ├── MaxBtn (Button)             → _depositMaxButton
│   └── DepositBtn (Button)         → _depositSubmitButton
└── WithdrawGroup
    ├── Input (TMP_InputField)      → _withdrawInput
    ├── PlusBtn (Button)            → _withdrawPlusButton
    ├── MinusBtn (Button)           → _withdrawMinusButton
    ├── MaxBtn (Button)             → _withdrawMaxButton
    └── WithdrawBtn (Button)        → _withdrawSubmitButton
```

Set the TMP_InputField `Content Type` to `Integer Number` so non-digit input is filtered.

**Recommended:** author in the Unity Editor by hand and tell the executing agent to do the same — UI prefab authoring via MCP is slow and error-prone for multi-layered visual layouts. If you do go via MCP, use `gameobject-create` + `gameobject-component-add` per node, then `assets-prefab-create`.

- [ ] **Step 8.3: Create `UI_SafePanel.prefab`**

Structure:
```
UI_SafePanel (RectTransform + UI_SafePanel script + Canvas? — likely uses parent canvas)
├── Background (Image, optional)
├── Header
│   ├── TitleLabel (TMP_Text)        → _titleLabel
│   └── CloseButton (Button)         → _closeButton
├── RowContainer (RectTransform + VerticalLayoutGroup + ContentSizeFitter)
│                                    → _rowContainer
└── StatusLabel (TMP_Text, initially inactive) → _statusLabel
```

In the `_rowPrefab` SerializeField on the panel, drag the `UI_SafeCurrencyRow.prefab` asset.

Match the visual style of `UI_StorageFurniturePanel.prefab` for consistency.

- [ ] **Step 8.4: Place the panel in the PlayerUI scene hierarchy and wire `_safePanel`**

The existing `UI_StorageFurniturePanel` is a child of `PlayerUI`. Mirror that:
- Open the scene that hosts `PlayerUI` (find via `assets-find searchFilter "t:Scene"` and look for the main game scene).
- Drag `UI_SafePanel.prefab` as a child of the PlayerUI hierarchy (alongside the storage panel).
- Wire the panel into `PlayerUI._safePanel` SerializeField.
- Disable the panel GameObject by default (`SetActive(false)`).

- [ ] **Step 8.5: Smoke-compile via Editor**

```
mcp__ai-game-developer__console-clear-logs
mcp__ai-game-developer__assets-refresh
mcp__ai-game-developer__console-get-logs
```
Expected: zero errors, zero warnings about missing references.

- [ ] **Step 8.6: Commit**

```bash
git add Assets/Prefabs/UI/UI_SafePanel.prefab Assets/Prefabs/UI/UI_SafeCurrencyRow.prefab Assets/Scenes/<scene-name>.unity
git commit -m "feat(ui): author UI_SafePanel + UI_SafeCurrencyRow prefabs and wire into PlayerUI"
```

---

## Task 9: Single-player smoke test in Unity Editor

Before multiplayer testing, validate the basic flow works as a host-only session.

- [ ] **Step 9.1: Open the Unity Editor scene with a SafeFurniture**

```
mcp__ai-game-developer__scene-list-opened
mcp__ai-game-developer__scene-open with the main game scene path
```
Locate a SafeFurniture in the scene (or place one via the existing dev-mode spawn module if needed).

- [ ] **Step 9.2: Enter Play mode**

```
mcp__ai-game-developer__editor-application-set-state to playmode=true
```

- [ ] **Step 9.3: Walk the player up to the safe, press E**

User-driven step — Kevin (or the executor) must drive the player character into the InteractionZone and press E.

Expected:
- Panel opens.
- One row labeled "Coins" appears (CurrencyId.Default).
- Safe balance label matches whatever the safe was seeded with (BaseTreasury value if it's a Treasury safe in a CommercialBuilding).
- Wallet balance label matches the player's wallet.

Capture screenshot:
```
mcp__ai-game-developer__screenshot-game-view
```

- [ ] **Step 9.4: Deposit 10 coins**

User types `10` in the deposit input, clicks Deposit.

Expected:
- Wallet decreases by 10.
- Safe increases by 10.
- Input field resets to 0.
- No error toast.

If insufficient wallet: error toast appears, wallet unchanged.

- [ ] **Step 9.5: Withdraw 10 coins**

User types `10` in the withdraw input, clicks Withdraw.

Expected:
- Safe decreases by 10.
- Wallet increases by 10.
- Input resets to 0.

- [ ] **Step 9.6: Test the Max buttons**

Click `Max` next to Deposit. Expected: input populates with the full wallet balance.
Click `Max` next to Withdraw. Expected: input populates with the full safe balance.

- [ ] **Step 9.7: Walk out of the zone**

Move the player away from the safe. Expected: panel auto-closes within ~1 second.

- [ ] **Step 9.8: Test ESC + Close button**

Re-open panel, press ESC. Expected: panel closes.
Re-open panel, click Close button. Expected: panel closes.

- [ ] **Step 9.9: Exit play mode and check for errors**

```
mcp__ai-game-developer__editor-application-set-state to playmode=false
mcp__ai-game-developer__console-get-logs
```
Expected: zero red errors. Yellow warnings reviewed but accepted unless they reference the new code.

- [ ] **Step 9.10: If anything failed, debug and patch**

If a step failed, capture the symptom (log line / screenshot), diagnose, fix the root cause, re-run from Step 9.2. Do not move on to Task 10 until Task 9 is fully green.

---

## Task 10: Multiplayer late-joiner + anti-cheat repro (rule #19b — MANDATORY)

The rule #19b late-joiner audit is non-negotiable. **Do not claim this feature done without running this task.** Capture the result in the final commit message.

This task requires two Unity Editor sessions (or one Editor + one standalone build). Kevin's setup typically uses the **ParrelSync clone** or a standalone build + Editor host. Follow the existing project convention.

- [ ] **Step 10.1: Build a standalone client (or open ParrelSync clone)**

If using ParrelSync: ensure the clone is up to date. If standalone: trigger a Development Build per rule #37 (PDB setup for crash diagnostics — not strictly needed unless something crashes, but cheap insurance).

- [ ] **Step 10.2: Host scenario — host modifies safe, late-joiner connects**

Sub-steps:
1. Start host in Editor.
2. As host player, walk to a Safe and deposit 100 coins (so safe.balance = seeded + 100).
3. Start client (ParrelSync clone or standalone). Connect to host.
4. Move the client player into the same Safe's InteractionZone. Press E.

Expected (client side):
- Panel opens.
- Safe balance label shows the post-deposit value (seeded + 100). Client must see what host mutated **before the client connected**.
- Wallet balance label shows the client's own wallet.

If client shows the pre-deposit value or 0: late-joiner replication is broken. Investigate `SafeFurnitureNetworkSync.OnNetworkSpawn`. (Per spec §7 it's already wired — but verify.)

- [ ] **Step 10.3: Both players deposit and withdraw**

1. Client deposits 10. Expected: host sees the change too.
2. Host withdraws 5. Expected: client sees the change too.
3. Client opens panel, checks both labels reflect the latest state.

- [ ] **Step 10.4: Concurrent transaction (race test)**

1. Reduce safe to exactly 10 coins (deposit/withdraw both clients until safe.balance == 10).
2. Both players open the panel.
3. Client types 10 in withdraw, host types 10 in withdraw.
4. Both click Withdraw at nearly the same time (within 100 ms).

Expected:
- First request processed: succeeds, wallet +10, safe = 0.
- Second request processed: fails with `insufficient-safe` toast on the loser's screen.
- No client desync; both clients agree safe.balance == 0.

- [ ] **Step 10.5: Anti-cheat sanity (developer-driven, optional)**

If feasible, manually craft an RPC where the client sends a spoofed `characterRef` for a character not owned by the sender. Expected: server rejects, logs `does not own character`, no mutation.

Skip this step if no easy harness exists — code review of Task 4's RPC validation block (Step 4.3 / 4.4) covers the same ground.

- [ ] **Step 10.6: Exit-zone autoclose on remote player**

While client has the panel open, drive the client player out of zone. Expected: client panel auto-closes; host sees client walk away.

- [ ] **Step 10.7: Disconnect mid-transaction**

While host has the panel open, kill the client (Alt+F4 or stop ParrelSync). Expected: host panel stays open (host hasn't left). Host can continue depositing/withdrawing.

While client has the panel open, kill the host. Expected: client panel closes (or disconnects gracefully).

- [ ] **Step 10.8: Capture results**

If all scenarios pass: write a short summary of what was tested.
If any scenario fails: stop, diagnose, fix the root cause, re-run from Step 10.2.

---

## Task 11: Documentation updates (rules #28 / #29 / #29b)

After implementation is verified green, update the documentation surface so the SKILL.md, agent description, and wiki all reflect the new feature.

**Files:**
- Modify: `.agent/skills/building_system/SKILL.md`
- Modify: `wiki/systems/commercial-treasury.md`
- Modify: `.claude/agents/building-furniture-specialist.md`

- [ ] **Step 11.1: `.agent/skills/building_system/SKILL.md`**

Read the file. Find the section that documents SafeFurniture / treasury / commercial buildings. Append (or insert in the appropriate section) a brief subsection:

```markdown
### SafeFurniture player UI (2026-05-16)

A player can press E on a SafeFurniture inside its InteractionZone to open a
deposit/withdraw panel (one row per CurrencyId). Mutations route through
`CharacterAction_DepositToSafe` / `CharacterAction_WithdrawFromSafe` (rule #22).
Server-authoritative via two ServerRpcs on `SafeFurnitureNetworkSync`. v1 is
permissionless — locks/keys/lockpicking will be a future orthogonal system.

Key files: UI_SafePanel.cs, UI_SafeCurrencyRow.cs, CharacterAction_DepositToSafe.cs,
CharacterAction_WithdrawFromSafe.cs, SafeFurnitureNetworkSync.cs (RPCs added).

See spec: docs/superpowers/specs/2026-05-16-safe-furniture-deposit-withdraw-ui-design.md
```

- [ ] **Step 11.2: `wiki/systems/commercial-treasury.md`**

Read `wiki/CLAUDE.md` first (per rule #29b, always read this before editing wiki).

Then in `wiki/systems/commercial-treasury.md`:
- Bump `updated:` in frontmatter to `2026-05-16`.
- Add a line to `## Change log`: `- 2026-05-16 — Player UI for SafeFurniture deposit/withdraw (UI_SafePanel + CharacterAction_Deposit/WithdrawFromSafe + 2 ServerRpcs) — claude`.
- Refresh `depended_on_by` to list `UI_SafePanel` if relevant.
- Add a subsection under `## Public API` titled "Player UI surface" briefly describing the panel and pointing to the spec.

- [ ] **Step 11.3: `.claude/agents/building-furniture-specialist.md`**

Read the file. Find the description block listing the agent's domain. Append:
- `UI_SafePanel` + `UI_SafeCurrencyRow` (player-facing deposit/withdraw)
- `CharacterAction_DepositToSafe` + `CharacterAction_WithdrawFromSafe`
- The two new ServerRpcs on `SafeFurnitureNetworkSync`

Keep the description format consistent with the existing entries.

- [ ] **Step 11.4: Commit docs**

```bash
git add .agent/skills/building_system/SKILL.md wiki/systems/commercial-treasury.md .claude/agents/building-furniture-specialist.md
git commit -m "docs(safe): document SafeFurniture player UI in skill + wiki + agent"
```

---

## Task 12: Final integration check + push

- [ ] **Step 12.1: Full project compile sanity**

```
mcp__ai-game-developer__assets-refresh
mcp__ai-game-developer__console-clear-logs
mcp__ai-game-developer__console-get-logs
```
Expected: zero red errors, zero warnings about missing references in any of the new scripts/prefabs.

- [ ] **Step 12.2: Quick re-run of single-player smoke test**

Repeat Task 9 Steps 9.2–9.9 to confirm nothing regressed after the docs commit. Should be under 5 minutes.

- [ ] **Step 12.3: Push to origin**

```bash
git log --oneline -10
git push -u origin claude/nostalgic-neumann-3d69a7
```

Expected: clean push, no force-push needed.

- [ ] **Step 12.4: Confirm the late-joiner repro line in the commit history**

Per rule #19b: every multiplayer commit must state, in writing, that the late-joiner repro was performed. Confirm that the Task 10 work was either captured in commit messages or follow up with a "verified late-joiner repro" commit/annotation if the work wasn't committed inline.

If not yet captured, write a tiny no-op commit (or amend the relevant feature commit) documenting:
```
verified: late-joiner repro passed (host deposits 100, client connects, client opens
panel, sees post-deposit value). Replication channel: existing _networkBalances
NetworkList + CharacterWallet ClientRpc broadcast. No new replicated state.
```

---

## Self-review (post-write check)

**Spec coverage:**
- §3.1 Permission model (anyone) → Task 4 RPCs have no auth gate beyond proximity + amount sanity. ✅
- §3.2 Multi-currency UI → Task 7 RebuildRows iterates `safe.Balances`. ✅
- §3.5 NPC parity via CharacterAction → Task 2 + Task 3 build the actions; Task 4 queues them. ✅
- §3.6 Proximity gate → Task 1 verifies prefab authoring; Task 4 server re-validates; Task 7 client polls. ✅
- §3.7 Late-joiner audit → Task 10. ✅
- §4 File table → all 4 new scripts + 3 edits + 2 prefabs scheduled across Task 2 / 3 / 4 / 5 / 6 / 7 / 8. ✅
- §5 Multi-currency strategy → Task 7 RebuildRows + HandleSafeBalanceChanged. ✅
- §6 Input pattern → Task 6 UI_SafeCurrencyRow. ✅
- §7 Late-joiner 6-question audit → all six points covered by Tasks 4 / 7 / 10. ✅
- §9 Error handling table → Task 4 RPC validation + Task 2/3 action-level rejection + Task 7 OnOperationResult toast. ✅
- §11 Testing matrix → Task 9 (single-player) + Task 10 (multiplayer). All ten matrix rows mapped: deposit/withdraw happy path (9.4-9.5), late-joiner (10.2), concurrent (10.4), out-of-zone (9.7, 10.6), B2B regression (covered by 10 by not touching B2B paths), save+load persistence (not explicitly tested — flagged in §10 below). ✅ except save+load.
- §12 Docs updates → Task 11. ✅

**Gap found in spec coverage:** §11 testing matrix lists "Save+load with non-zero safe balance" but no task in the plan exercises save/load. The underlying mechanism (BuildingSaveData.TreasurySeeded idempotency) is already pinned by the test `06ba3f4d test(save): pin BuildingSaveData backward-compat for old saves` — so the regression surface is already covered by the existing test. Not adding a task; flagged here for the executor's awareness.

**Placeholder scan:** No "TODO" / "TBD" / "implement later" in plan steps. Two known cross-task compile-state issues are explicitly flagged with the stub strategy (Task 4 Step 4.6, Task 5 Step 5.3) — these are bounded and documented, not placeholders.

**Type consistency:**
- `NotifyOperationResult(ulong, bool, string)` is defined in Step 4.2 and called from Step 2.2 / 3.1. ✅
- `OnSafeOperationResult(SafeFurniture, bool, string)` defined in Step 7.3, called from Step 4.2's ClientRpc. ✅
- `OpenSafePanel(SafeFurniture, Character)` / `CloseSafePanel()` defined in Step 7.3, called from Step 5.2. ✅
- `SafeFurniture.NetSync` property defined in Step 4.5, called from Step 2.2 / 3.1 / 7.2. ✅
- `UI_SafeCurrencyRow.Initialize(...)` signature in Step 6.2 matches the call site in Step 7.2 (`RebuildRows`). ✅
- `UI_SafeCurrencyRow.Refresh()` called from Step 7.2's `HandleSafeBalanceChanged` / `HandleWalletBalanceChanged`. ✅
- `RequestDepositServerRpc(NetworkBehaviourReference, int, int, ServerRpcParams)` defined in Step 4.3, called from Step 7.2 with three args (the `ServerRpcParams` is auto-supplied by NGO). ✅
- Same for `RequestWithdrawServerRpc`. ✅

**No issues found that require revision.**

---

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-05-16-safe-furniture-deposit-withdraw-ui.md`. Two execution options:

**1. Subagent-Driven (recommended)** — fresh subagent per task, review between tasks, fast iteration. Best for this plan because Tasks 8 (prefab authoring) and Tasks 9 / 10 (in-Editor verification) benefit from clean context per task.

**2. Inline Execution** — execute tasks in this session using `superpowers:executing-plans`, batch execution with checkpoints for review.

Which approach?
