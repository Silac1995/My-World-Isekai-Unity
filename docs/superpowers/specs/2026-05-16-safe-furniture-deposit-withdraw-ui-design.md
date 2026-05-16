---
title: SafeFurniture deposit/withdraw player UI
date: 2026-05-16
status: approved
author: Kevin (Silac) + claude
related:
  - docs/superpowers/plans/2026-05-16-building-so-and-treasury.md
  - wiki/systems/commercial-treasury.md
  - .agent/skills/building_system/SKILL.md
---

# SafeFurniture deposit/withdraw player UI — design

## 1. Context

The 2026-05-16 BuildingSO + BaseTreasury refactor (see [plan](../plans/2026-05-16-building-so-and-treasury.md)) ships the server-authoritative side of `SafeFurniture`: `Credit` / `TryDebit` mutators, `SafeFurnitureNetworkSync` replicating `_networkBalances` as a `NetworkList<BuildingTreasuryEntry>` with late-joiner catch-up, `BuildingCommercialSO.BaseTreasury` seed flow, and `BuildingSaveData.TreasurySeeded` idempotency. The B2B paths (wages, restock, treasury seeding) already drive `safe.Credit` / `safe.TryDebit` server-side.

What ships today is **invisible to the player**: there is no UI to inspect a safe's balance, no way to move money between a player's `CharacterWallet` and a safe. Pressing E on a safe currently does literally nothing (`SafeFurniture.OnInteract` is not overridden; the generic `FurnitureInteractable` routes the tap to a no-op).

This spec covers the player-facing UI: walk up to a safe, press E (or click), open a panel that shows balance(s) per `CurrencyId` and lets the player deposit from / withdraw to their `CharacterWallet`.

## 2. Scope

### In scope (v1)

- Player UI panel that opens when an owning player presses E inside a `SafeFurniture`'s `InteractionZone`.
- One row per `CurrencyId` present in the safe (forward-compat for Kingdom currencies; today only `CurrencyId.Default`).
- Per-row deposit + withdraw with numeric input, ± steppers, and Max button.
- Server-authoritative atomic mutation via two new `CharacterAction`s (rule #22: player↔NPC parity).
- Multiplayer correctness: late-joiner replication, host↔client↔NPC validation, anti-cheat re-validation on every RPC.
- Auto-close on out-of-zone, target despawn, ESC, close button.

### Out of scope (deferred)

- **Permission gating.** v1 is permissionless: anyone in zone can deposit and withdraw. Locks / keys / lockpicking will be added as a separate orthogonal system that gates at the `Interact` level (probably on the `InteractableObject`).
- **NPC banker / pickpocket / treasurer AI.** Action classes are built; AI integration is future work.
- **Currency-as-item drop.** Already deferred per `Cashier.cs:178`. Coins remain int-valued in v1.
- **`SafeRoleType` split (Personal vs Treasury for the player UI).** Auth is uniform, so no role split is needed today. `Treasury` keeps its `BaseTreasury` seed behavior; `None` works the same way under the UI.
- **Transaction history / audit log.** Server-side logs via `Debug.Log` with `reason` strings; no in-game UI.
- **Banker fee / interest / commission.** Pure 1:1 transfer.

## 3. Decisions captured

| # | Question | Decision | Rationale |
|---|---|---|---|
| 1 | Who can deposit/withdraw? | **Anyone in InteractionZone** | Kevin's call. Locks/keys + thief/lockpicking become a separate orthogonal system later. Matches `StorageFurniture` (also permissionless). |
| 2 | Multi-currency display? | **Yes — list of `UI_SafeCurrencyRow`** | Forward-compat for Kingdom currencies. Today renders exactly 1 row (`Default`). No UI change when new currencies are minted. |
| 3 | Treasury withdraw-able by player? | **Yes (full bidirectional)** | Follows from Q1: no auth difference between Treasury and None safes. |
| 4 | New `SafeRoleType.Personal`? | **No** | Auth is uniform, no per-role UI gating. Role still drives seeding behavior on construction-complete (Treasury seeds via `BaseTreasury`). |
| 5 | Route mutation through `CharacterAction`? | **Yes — `CharacterAction_DepositToSafe` + `CharacterAction_WithdrawFromSafe`** | Mirrors `StorageFurniture` / `Cashier` precedent. Rule #22 compliant from day one. A future NPC banker just enqueues the same action. |
| 6 | InteractionZone gate? | **Authoritative on `IsCharacterInInteractionZone` (rule #36)** | Both client-side (auto-close poll) and server-side (RPC re-validation as anti-cheat). Prereq: verify `SafeFurniture` prefab has `FurnitureInteractable` + `InteractionZone` collider. |
| 7 | Late-joiner audit (rule #19b)? | **Done — uses existing `_networkBalances` channel** | See §7 below. The data side already replicates; the panel just subscribes to `safe.OnBalanceChanged` + `wallet.OnBalanceChanged`. |

## 4. Architecture

Pattern: **mirror `StorageFurniture` exactly** — client-local panel open, server-authoritative mutation via ServerRpc + `CharacterAction`. Cashier-shaped (server-driven open with session lock) is overkill for single-shot atomic transfers; multiple players inspecting the same safe is fine.

### Files

| File | Status | Purpose |
|---|---|---|
| `Assets/Scripts/World/Furniture/SafeFurniture.cs` | edit | Override `OnInteract`: client-local, owning-player-only, calls `PlayerUI.Instance.OpenSafePanel(this, interactor)`. |
| `Assets/Scripts/World/Furniture/SafeFurnitureNetworkSync.cs` | edit | Add `RequestDepositServerRpc(NetworkBehaviourReference characterRef, int currencyRawId, int amount, ServerRpcParams)` and `RequestWithdrawServerRpc(...)`. Validate sender, ownership, zone, amount; queue action via `character.CharacterActions.ExecuteAction(...)`. Required: `OperationResultClientRpc(bool success, FixedString64Bytes reason, ClientRpcParams)` targeted to the requester only (not broadcast) — fired on failure paths inside the action. Success path can skip the RPC and rely on the `OnBalanceChanged` repaint. |
| `Assets/Scripts/Character/CharacterActions/CharacterAction_DepositToSafe.cs` | new | Server-side action. Atomic: `if (wallet.RemoveCoins(currency, amount, "safe-deposit")) safe.Credit(currency, amount, "player-deposit");`. Duration 0. |
| `Assets/Scripts/Character/CharacterActions/CharacterAction_WithdrawFromSafe.cs` | new | Inverse atomic: `if (safe.TryDebit(currency, amount, "player-withdraw")) wallet.AddCoins(currency, amount, "safe-withdraw");`. |
| `Assets/Scripts/UI/Furniture/UI_SafePanel.cs` | new | Mirror `UI_StorageFurniturePanel`. Header → row container → close. Subscribes to `safe.OnBalanceChanged` + `customer.CharacterWallet.OnBalanceChanged`. Auto-close on out-of-zone (1 Hz poll), ESC, target despawn, `OnDisable`. |
| `Assets/Scripts/UI/Furniture/UI_SafeCurrencyRow.cs` | new | Per-currency row. Balance label + wallet label + deposit input + button + Max + withdraw input + button + Max. Clamps client-side; server re-validates. |
| `Assets/Scripts/UI/PlayerUI.cs` | edit | Add `OpenSafePanel(SafeFurniture, Character)` + `CloseSafePanel()`. `SerializeField _safePanel`. Mirror `OpenStoragePanel`. |
| `Assets/Prefabs/.../Safe.prefab` (and any building that hosts a safe) | edit (verify) | **Prereq Step 0** — confirm prefab carries `FurnitureInteractable` + `InteractionZone` collider (BoxCollider, isTrigger, generous size — author it a few times the agent's stopping distance so NavMesh-sampled landings stay inside per rule #36). Add if missing. |
| `Assets/Prefabs/UI/UI_SafePanel.prefab` (or wherever panels live) | new | Authored panel + row prefab wired into `PlayerUI._safePanel`. |

### Data flow (deposit example)

```
[Player presses E inside InteractionZone]
  → FurnitureInteractable.Interact(player)
  → SafeFurniture.OnInteract(player)              [CLIENT-LOCAL, gated on IsOwner && IsPlayer()]
  → PlayerUI.Instance.OpenSafePanel(this, player)
  → UI_SafePanel.Initialize(safe, player)
       subscribes safe.OnBalanceChanged + player.CharacterWallet.OnBalanceChanged
       builds one UI_SafeCurrencyRow per safe.Balances entry

[Player types amount, clicks Deposit on row]
  → UI_SafeCurrencyRow.OnDepositClicked
  → safe.NetSync.RequestDepositServerRpc(playerRef, currency.Id, amount)
  → [SERVER]
       validate characterRef.TryGet(out Character character)
       validate character.OwnerClientId == p.Receive.SenderClientId  (anti-cheat: spoofed sender)
       validate safe.GetComponent<InteractableObject>().IsCharacterInInteractionZone(character)  (rule #36)
       validate amount > 0
       (NOT validated server-side: wallet balance — RemoveCoins returns false on shortfall)
       character.CharacterActions.ExecuteAction(new CharacterAction_DepositToSafe(safe, currency, amount))
  → [SERVER tick] action.Execute():
       if (!wallet.RemoveCoins(currency, amount, "safe-deposit"))
           → OperationResultClientRpc(false, "insufficient-wallet", requesterParams); return;
       safe.Credit(currency, amount, "player-deposit");
       OperationResultClientRpc(true, "", requesterParams);
  → wallet.RemoveCoins broadcasts globally via existing CharacterWallet ClientRpc
  → safe.Credit fires OnBalanceChanged → NetSync re-pushes _networkBalances NetworkList
  → [EVERY CLIENT] safe.OnBalanceChanged + wallet.OnBalanceChanged fire → UI rows repaint
```

Withdraw is the inverse, gated by `safe.TryDebit` returning `true` before `wallet.AddCoins`.

## 5. Multi-currency UI

`UI_SafePanel.Initialize` reads `safe.Balances` (already client-side mirror) and instantiates one `UI_SafeCurrencyRow` per entry. `OnBalanceChanged` triggers a re-evaluation:

- If a currency exists in `safe.Balances` and has no row → instantiate.
- If a row exists for a currency that has dropped to 0 *and* the player's wallet has 0 of the same currency → leave it visible (do not destroy mid-interaction); will hide on next panel open if still 0.
- Otherwise refresh the row's labels in place.

`safe.Balances` allocates a list on every call (line 191 of SafeFurniture.cs); acceptable at human cadence (~10 Hz max repaint), but rows should query `safe.GetBalance(currency)` (dict lookup, no alloc) for per-row labels — only the row list itself uses `Balances` for membership.

For the player's wallet rows, use `player.CharacterWallet.GetBalance(currency)`. Wallet replicates globally via existing ClientRpc.

## 6. Input pattern per row

| Element | Behavior |
|---|---|
| Balance label | "Safe: 1,250" — formatted from `safe.GetBalance(currency)`. |
| Wallet label | "Wallet: 350" — from `player.CharacterWallet.GetBalance(currency)`. |
| Deposit input | Numeric TMP_InputField. Range: 1 ≤ x ≤ wallet balance. Clamps on blur. |
| Deposit ± steppers | +1 / -1 quick adjust. |
| Deposit Max button | Set input to wallet balance. |
| Deposit submit button | Fires `RequestDepositServerRpc`. Disabled when input is 0, null, or > wallet balance. |
| Withdraw input | Same shape, bounded by safe balance instead. |
| Withdraw Max button | Set input to safe balance. |
| Withdraw submit button | Same shape. |

Enter key on a focused input submits the corresponding direction. ESC closes the panel. Tab cycles inputs.

Inputs reset to 0 after a successful transaction. After a failed transaction (server rejected), the input retains its value so the player can adjust.

## 7. Late-joiner audit (rule #19b)

The mandatory six-question audit:

1. **Who writes / who reads.**
   - Writer: server (via the new `CharacterAction`s). Existing B2B writers (wages, restock, treasury seed) remain untouched.
   - Readers: every client (UI rows + B2B systems on the server).

2. **Replication channel.**
   - Safe balances: existing `NetworkList<BuildingTreasuryEntry> _networkBalances` on `SafeFurnitureNetworkSync` (auto-replicates).
   - Wallet balances: existing `BroadcastBalanceChangeClientRpc` on `CharacterWallet` (broadcast to all clients).
   - No new replicated fields introduced.

3. **Late-joiner repro (mandatory before claiming done).**
   - **Repro steps:** host the session, mutate a safe balance (via debug or by depositing as host), join a fresh second client, walk that client to the same safe, press E. Expect: panel opens, safe balance label matches host, wallet label matches the joining client's wallet.
   - The existing `OnNetworkSpawn` catch-up in `SafeFurnitureNetworkSync` already pushes the snapshot via `ApplyFullBalancesOnClient`. No new code required for this gate; only verify behavior.

4. **Client-side pre-gate.**
   - UI subscribes to `safe.OnBalanceChanged` (fires on every peer via the existing `ApplyBalancesFromNetwork` drive-through). Matches authoritative state.
   - UI subscribes to `customer.CharacterWallet.OnBalanceChanged`. Wallet's ClientRpc broadcast guarantees every peer sees the change.

5. **`GetComponentInParent` in `Awake` (spawn-race risk).**
   - No new `GetComponentInParent` calls introduced. `UI_SafePanel` lives as a `SerializeField` sibling on `PlayerUI` and is bound at scene authoring (mirror of `UI_StorageFurniturePanel`).
   - The new `RequestDepositServerRpc` resolves the safe's `InteractableObject` via a sibling `GetComponent<InteractableObject>()` on the same GameObject as `SafeFurniture` — same GameObject, no spawn-race.

6. **Proximity gate (rule #36).**
   - Client opens the panel only via `FurnitureInteractable.Interact` (which already gates on `IsCharacterInInteractionZone`). No raw `Vector3.Distance`.
   - Server re-validates `IsCharacterInInteractionZone` inside both ServerRpcs as anti-cheat.
   - The auto-close poll uses `IsCharacterInInteractionZone` (1 Hz cadence, ungated logs forbidden per rule #34).

**Replication channel chosen:** existing `NetworkList<BuildingTreasuryEntry>` (safe) + existing `CharacterWallet` ClientRpc broadcast (wallet). No new channels.

## 8. NPC parity (rule #22)

The two `CharacterAction` classes are the canonical surface for "deposit into a safe" / "withdraw from a safe". The player UI is one consumer; a future banker NPC AI / pickpocket NPC AI is the other. Both call `character.CharacterActions.ExecuteAction(action)`.

The raw `safe.Credit` / `safe.TryDebit` server APIs continue to serve B2B paths (wage payment via `CommercialBuilding.TryDebitTreasury`, restock proceeds via `CommercialBuilding.CreditTreasury`). Those callers do **not** route through `CharacterAction` because they are building↔building flows, not character↔building.

## 9. Error handling

| Failure | Server behavior | Client UX |
|---|---|---|
| Wallet insufficient (deposit) | `RemoveCoins` returns false → action no-ops | `OperationResultClientRpc(false, "insufficient-wallet")` → row flashes red + transient toast |
| Safe insufficient (withdraw) | `TryDebit` returns false → action no-ops | Same shape, "insufficient-safe" |
| Out of `InteractionZone` | RPC rejects | Client also auto-closes via 1 Hz poll; toast "out of range" |
| Negative / zero amount | RPC rejects | Submit button greyed; explicit toast on manual call |
| Safe despawned mid-transaction | n/a | `NetworkObject.OnNetworkDespawn` closes panel |
| Wrong sender (spoofed `OwnerClientId`) | RPC rejects, logs anti-cheat warning | Silently ignored |
| `characterRef.TryGet` fails | RPC rejects, logs | Silently ignored |
| Concurrent transactions (two players, last 10 coins) | Server is single-threaded — first RPC succeeds, second sees updated balance → `TryDebit` fails | Loser sees "insufficient-safe" toast |

All `Debug.Log` calls in hot paths (1 Hz auto-close poll, OnBalanceChanged refresh) must be gated behind `if (NPCDebug.VerboseActions)` or equivalent per rule #34. RPC validation logs are gated behind `if (Debug.isDebugBuild)`.

## 10. Open questions / risks

- **Numeric input scale.** Will real balances reach 6+ digits? Probably yes (Kingdom-scale treasuries). The TMP_InputField content-type must accept large integers. Confirm formatting (comma grouping, no scientific notation).
- **Authority context for future locks.** When locks/keys land, the gate will live on the `Interact` side (probably `FurnitureInteractable.Interact` checks `safe.IsUnlocked` or similar). No change to the deposit/withdraw RPC shape — locks just block the panel from opening.
- **Currency display name.** `CurrencyId` is currently `int Id`; there's no human-readable name registry yet. v1 should hardcode "Coins" for `Default`. The Kingdom system will need a `CurrencySO` or similar — out of scope here.

## 11. Testing matrix (multiplayer mandatory)

| Scenario | Expected |
|---|---|
| Host deposits 100 then withdraws 50 | Safe balance updates instantly; wallet matches. |
| Host hosts, mutates safe to 1000, then client joins late and opens panel | Client's panel shows 1000 (late-joiner catch-up via existing `OnNetworkSpawn`). |
| Two players, same safe, simultaneous withdraw of last 10 coins | First RPC succeeds; second sees `insufficient-safe` toast. |
| Player walks out of zone mid-transaction (input field focused, not yet submitted) | Panel auto-closes within 1s; pending input lost. |
| Server-side B2B wage payment fires mid-player-panel-open | Player sees safe balance drop via `OnBalanceChanged`; no UI desync. |
| Save + load with non-zero safe balance | `BuildingSaveData.TreasurySeeded` already idempotent; balance persists; `_treasurySeeded` not re-seeded. |
| NPC restock pulls from `safe.TryDebit` (existing B2B path) | Unaffected; no regression. |
| Player spams Deposit-button before server response arrives | RPC queue handles; balance progresses correctly; no client desync (each RPC re-validates). |
| Player submits with safe destroyed (race: server tick destroys safe while client RPC in flight) | RPC's `characterRef.TryGet` / `_safe == null` rejects; client sees panel close on `OnNetworkDespawn`. |
| Client clicks Withdraw while another player just emptied the safe | RPC's `TryDebit` returns false; client sees `insufficient-safe`. |

## 12. Documentation updates (rule #28 / #29 / #29b)

After implementation:

- **`.agent/skills/building_system/SKILL.md`** — append section on `UI_SafePanel` + `CharacterAction_DepositToSafe/WithdrawFromSafe`. Document the new RPCs on `SafeFurnitureNetworkSync`.
- **`.agent/skills/character_system/SKILL.md`** (or wherever `CharacterAction` is documented) — register the two new actions.
- **`wiki/systems/commercial-treasury.md`** — bump `updated:`, append change log line, refresh `depended_on_by` to include the new UI script, add a "Player UI" subsection under `Public API`.
- **`wiki/systems/character-actions.md`** (if it exists) — list the two new actions.
- **`wiki/INDEX.md`** — no change unless we land a new gotcha during implementation.
- **`.claude/agents/building-furniture-specialist.md`** — extend the description to mention SafeFurniture player UI + the two new CharacterActions.

## 13. References

- `Assets/Scripts/World/Furniture/SafeFurniture.cs`
- `Assets/Scripts/World/Furniture/SafeFurnitureNetworkSync.cs`
- `Assets/Scripts/World/Furniture/SafeRoleType.cs`
- `Assets/Scripts/World/Furniture/StorageFurniture.cs` (precedent for client-local open)
- `Assets/Scripts/World/Furniture/StorageFurnitureNetworkSync.cs`
- `Assets/Scripts/UI/WorldUI/UI_StorageFurniturePanel.cs` (closest UI precedent)
- `Assets/Scripts/UI/Shop/UI_ShopBuyPanel.cs` (cashier-pattern reference)
- `Assets/Scripts/UI/PlayerUI.cs`
- `Assets/Scripts/Character/CharacterWallet/CharacterWallet.cs`
- `Assets/Scripts/Economy/CurrencyId.cs`
- `Assets/Scripts/World/Buildings/BuildingTreasuryEntry.cs`
- `Assets/Scripts/Interactable/InteractableObject.cs`
- `Assets/Scripts/Interactable/FurnitureInteractable.cs`
- `CLAUDE.md` rules #18 (NGO authority), #19 / #19b (late-joiner audit), #22 (player↔NPC parity), #33 (input ownership), #34 (perf), #36 (interaction zone)
- `wiki/gotchas/host-only-state-blindspot.md`
- `docs/superpowers/plans/2026-05-16-building-so-and-treasury.md`
