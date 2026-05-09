---
name: character-wallet
description: Per-character multi-currency wallet (Dictionary<CurrencyId,int>), server-authoritative, with ClientRpc-on-change sync and ICharacterSaveData persistence.
---

# Character Wallet

`CharacterWallet` is a `CharacterSystem` (NetworkBehaviour) child of every `Character` (player + NPC + animal — attached on `Character_Default` prefab). It holds per-currency `int` balances and is the single point all coin movement flows through.

## When to use this skill

- Crediting wages, sale proceeds, quest rewards, found loot to a character.
- Charging a character for a purchase, tax, or fine.
- Reading another character's balance from a UI or AI predicate (e.g., "can this NPC afford the door fee?").
- Adding a new currency type (will become real once the Kingdom system lands).

## Public API

```csharp
// Read
int GetBalance(CurrencyId currency);
IReadOnlyDictionary<CurrencyId, int> GetAllBalances();
bool CanAfford(CurrencyId currency, int amount);

// Mutate (server-authoritative)
void AddCoins(CurrencyId currency, int amount, string source);
bool RemoveCoins(CurrencyId currency, int amount, string reason);

// Events (fire on every machine — server fires directly, client fires after RPC)
event Action<CurrencyId, int, int> OnBalanceChanged; // (currency, oldValue, newValue)
event Action<CurrencyId, int, string> OnCoinsReceived; // (currency, amount, source) — server-only
```

`CurrencyId` (`Assets/Scripts/Economy/CurrencyId.cs`) is a thin int-wrapping struct. Today only `CurrencyId.Default` exists; the future Kingdom system will mint additional ids.

## Server authority

Both `AddCoins` and `RemoveCoins` enforce:

```csharp
if (!IsServer && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
{
    Debug.LogError(...);
    return;
}
```

This means:
- In a networked session, only the server can mutate. Clients must route via a `[ServerRpc]`.
- In Solo / EditMode tests (no NetworkManager listening), the call proceeds — useful for tests and offline play.

The `SafeOwnerName()` helper is used internally for log diagnostics (handles `_character` null + missing CharacterName).

## Network sync

`BroadcastBalanceChangeClientRpc(int currencyRawId, int newValue)` fires on every mutation. The host short-circuits with `if (IsServer) return;` to avoid double-applying.

**v1 limitation — late-joiners see balance 0** until the next mutation. There is no initial-state sync. This is acceptable today because:
- Player wallets travel with the portable character profile (rule #20) — saved/restored via JSON.
- NPC wallets observed by player UI are mostly informational; the wallet eventually re-syncs on the next wage payment.

When the Kingdom system adds multiple currencies per character (and the NPC-wallet HUD becomes player-visible), upgrade the sync path to `NetworkList<CurrencyBalanceEntry>` with full initial-state replication. **Do not change callers.**

## Save / load

```csharp
public string SaveKey => "CharacterWallet";
public int LoadPriority => 35;   // after equipment (30), before needs (40)
```

`Serialize()` flattens the dictionary into `WalletSaveData.balances` (`List<CurrencyBalanceEntry>`). `Deserialize` clears and rebuilds. The save is a portable per-character record (rule #20) — round-trips through `CharacterDataCoordinator` without any explicit registration (auto-discovered via `GetComponentsInChildren<ICharacterSaveData>`).

## Integration points

| Caller | What it does |
|---|---|
| `MintedWagePayer.PayWages` | Calls `worker.CharacterWallet.AddCoins(currency, amount, source)` after a shift. |
| Future shop/vendor purchase | Will call `RemoveCoins` on the buyer + `AddCoins` on the seller. |
| Future quest reward system | Will call `AddCoins`. |

Always pass a meaningful `source` / `reason` string — it shows up in logs and in the future audit/telemetry layer.

## Gotchas

- **Negative or zero amount** → `LogError` + no-op. Don't pass `0` to "trigger an event"; nothing fires.
- **`GetAllBalances()` returns the live dictionary by reference** as `IReadOnlyDictionary`. Don't iterate it during a mutation, and don't cache it across frames.
- **`OnCoinsReceived` is server-only** (intentional — used for toast/floating-text "you got paid"). Don't subscribe expecting it to fire on clients.
- **`OnBalanceChanged` fires on every machine.** Use this for HUD updates, not `OnCoinsReceived`.
- **The wallet exists on every `Character_Default` variant** including animals (Task 11 attaches to the base prefab; Humanoid/Quadruped/Animal nest it). A future "tame animal earns wages" system would Just Work.

## Related

- `.agent/skills/character-worklog/SKILL.md` — sibling subsystem tracking work units.
- `.agent/skills/wage-system/SKILL.md` — the orchestrator that pays wallets at punch-out.
- `.agent/skills/save-load-system/SKILL.md` — how SaveKey/LoadPriority dispatch works.
- `wiki/systems/worker-wages-and-performance.md` — architecture overview.

## Source files

- `Assets/Scripts/Character/CharacterWallet/CharacterWallet.cs`
- `Assets/Scripts/Character/CharacterWallet/WalletSaveData.cs`
- `Assets/Scripts/Economy/CurrencyId.cs`
