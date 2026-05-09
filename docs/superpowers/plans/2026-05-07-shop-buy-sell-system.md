# Shop sell/buy system — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the shop sell/buy system (Phase 2b of the Living World autonomy track) as specified in [`docs/superpowers/specs/2026-05-07-shop-buy-sell-system-design.md`](../specs/2026-05-07-shop-buy-sell-system-design.md).

**Architecture:** Cashier furniture is the customer entry point + per-instance till + transaction-lock holder, decoupled from a separately-occupying vendor. `CharacterAction_BuyFromShop : CharacterAction_Continuous` runs server-side for both NPC (2s timer) and Player (UI-driven) flows; commit is atomic with rollback on partial failure. Items move via `CharacterEquipment.PickUpItem` cascade (bag → hands → ground). Catalog + sell-shelves runtime-editable through the parallel-session management panel architecture.

**Tech Stack:** Unity 6, NGO (Netcode for GameObjects), C#, NUnit (EditMode tests via per-feature `MWI.<Feature>.Pure` assemblies).

---

## Reading order before starting

1. `docs/superpowers/specs/2026-05-07-shop-buy-sell-system-design.md` — full design (canonical code lives here).
2. `CLAUDE.md` at project root — 35 mandatory rules. Critical for this plan: #18 (server authority), #19 (multiplayer matrix), #22 (player↔NPC parity through `CharacterAction`), #28 (SKILL.md updates), #29b (wiki updates), #34 (no per-frame allocs).
3. `docs/superpowers/specs/2026-05-06-building-construction-loop-design.md` — reference for the canonical `CharacterAction_Continuous` + `[ServerRpc(RequireOwnership=false)]` pattern this plan mirrors.

## Hard prerequisite

**Wave 10 (Management Tabs) consumes the parallel session at**
`docs/superpowers/briefs/2026-05-07-commercial-building-management-panel-architecture-brief.md`.
**Stop after Wave 9 if that session has not merged.** Resume with Wave 10 once
`IManagementTab` + `CommercialBuilding.GetManagementTabs()` + `UI_OwnerManagementPanel` exist on the branch.

## File map

| File | Action | Wave |
|---|---|---|
| `Assets/Resources/Data/Item/ItemSO.cs` | Modify (add `_basePrice`) | 1 |
| `Assets/Scripts/World/Furniture/FurnitureTag.cs` | Modify (add `Cashier`) | 1 |
| `Assets/Scripts/World/Buildings/CommercialBuildings/Pure/MWI.Shop.Pure.asmdef` | Create | 1 |
| `Assets/Scripts/World/Buildings/CommercialBuildings/Pure/PriceResolver.cs` | Create | 1 |
| `Assets/Tests/EditMode/Shop/Shop.Tests.asmdef` | Create | 1 |
| `Assets/Tests/EditMode/Shop/PriceResolverTests.cs` | Create | 1 |
| `Assets/Scripts/World/Buildings/CommercialBuildings/ShopBuilding.cs` | Modify (extend `ShopItemEntry`, add catalog/shelves/cashiers, ServerRpcs, delete queue/vendor-point) | 1, 4 |
| `Assets/Scripts/World/Furniture/Cashier.cs` | Create | 2, 3 |
| `Assets/Scripts/World/Furniture/CashierInteractable.cs` | Create | 2 |
| `Assets/Scripts/World/Furniture/CashierNetSync.cs` | Create | 2, 3 |
| `Assets/Scripts/World/Furniture/CashierSaveData.cs` | Create | 9 |
| `Assets/Scripts/World/Buildings/CommercialBuildings/ShopBuildingNetSync.cs` | Create | 4 |
| `Assets/Scripts/World/Buildings/CommercialBuildings/ShopBuildingSaveData.cs` | Create | 9 |
| `Assets/Scripts/World/Jobs/ServiceJobs/JobVendor.cs` | Modify (pool model) | 5 |
| `Assets/Scripts/Character/CharacterActions/CharacterAction_BuyFromShop.cs` | Create | 6 |
| `Assets/Scripts/AI/GOAP/Actions/GoapAction_GoShopping.cs` | Modify | 7 |
| `Assets/Scripts/UI/Shop/UI_ShopBuyPanel.cs` (+ prefab) | Create | 8 |
| `Assets/Scripts/UI/Management/Tabs/ShopCatalogTab.cs` (+ view + prefab) | Create | 10 |
| `Assets/Scripts/UI/Management/Tabs/ShopShelvesTab.cs` (+ view + prefab) | Create | 10 |
| `Assets/Scripts/UI/Management/Tabs/ShopCashiersTab.cs` (+ view + prefab) | Create | 10 |
| `Assets/Scripts/UI/Management/Tabs/CatalogItemPickerDialog.cs` (+ prefab) | Create | 10 |
| `.agent/skills/shop-system/SKILL.md` | Create | 11 |
| `.agent/skills/jobs/SKILL.md` | Modify (pool-model JobVendor) | 11 |
| `.agent/skills/character-actions/SKILL.md` | Modify (new continuous action) | 11 |
| `.agent/skills/goap-actions/SKILL.md` | Modify (refactored GoapAction_GoShopping) | 11 |
| `wiki/systems/shop-system.md` | Create | 11 |
| `wiki/systems/cashier-furniture.md` | Create | 11 |
| `wiki/systems/character-actions.md` | Modify (change log) | 11 |

## Wave overview

- **Wave 1** — Data foundations + Pure-assembly extraction + price-resolver tests (TDD).
- **Wave 2** — Cashier furniture skeleton (Cashier + CashierInteractable + CashierNetSync stubs).
- **Wave 3** — Cashier server-only logic (lock, till, auto-occupy, register/unregister).
- **Wave 4** — ShopBuilding refactor (catalog, shelves, cashiers, NetSync, ServerRpcs, deletions).
- **Wave 5** — JobVendor pool-model refactor.
- **Wave 6** — `CharacterAction_BuyFromShop` (NPC + Player modes, atomic commit).
- **Wave 7** — `GoapAction_GoShopping` refactor.
- **Wave 8** — Player UI (`UI_ShopBuyPanel`) + ServerRpc wiring.
- **Wave 9** — Save/load (CashierSaveData + ShopBuildingSaveData + deferred-resolution wiring).
- **Wave 10** — Management tabs (depends on parallel session).
- **Wave 11** — Documentation + integration verification (15 testing scenarios).

---

## Wave 1 — Data foundations

### Task 1: Add `ItemSO._basePrice`

**Files:**
- Modify: `Assets/Resources/Data/Item/ItemSO.cs`

- [ ] **Step 1 — Add `_basePrice` field + getter to `ItemSO`**

After the existing `[Header("Tier")]` block (around line 19-21 of `ItemSO.cs`), insert:

```csharp
[Header("Economy")]
[Tooltip("Default shop sell price. ShopItemEntry.PriceOverride wins when > 0.")]
[SerializeField] private int _basePrice = 0;
public int BasePrice => _basePrice;
```

- [ ] **Step 2 — Compile in Unity Editor**

Use `mcp__ai-game-developer__assets-refresh` to refresh AssetDatabase. Then `mcp__ai-game-developer__console-get-logs` to verify zero compile errors.
Expected: no errors. The new field is inspector-visible on every `ItemSO` asset under Header "Economy".

- [ ] **Step 3 — Commit**

```bash
git add Assets/Resources/Data/Item/ItemSO.cs
git commit -m "feat(items): add ItemSO.BasePrice for shop pricing"
```

---

### Task 2: Add `FurnitureTag.Cashier`

**Files:**
- Modify: `Assets/Scripts/World/Furniture/FurnitureTag.cs`

- [ ] **Step 1 — Append `Cashier` enum value**

Replace the entire enum content with:

```csharp
public enum FurnitureTag
{
    None,
    Bed,        // Bedroom
    Cooking,    // Kitchen
    Crafting,   // Workshop
    Storage,    // Warehouse
    Seating,    // Living room / Tavern
    TimeClock,  // Punch-in / punch-out station for a CommercialBuilding
    Cashier     // Customer-facing transaction counter for a ShopBuilding (or future BankBuilding etc.)
}
```

- [ ] **Step 2 — Refresh + compile**

Run `mcp__ai-game-developer__assets-refresh`. Verify zero compile errors via `mcp__ai-game-developer__console-get-logs`.
Expected: no errors. Adding an enum value at the end is non-breaking.

- [ ] **Step 3 — Commit**

```bash
git add Assets/Scripts/World/Furniture/FurnitureTag.cs
git commit -m "feat(furniture): add FurnitureTag.Cashier"
```

---

### Task 3: Extract Pure assembly + `PriceResolver`

**Files:**
- Create: `Assets/Scripts/World/Buildings/CommercialBuildings/Pure/MWI.Shop.Pure.asmdef`
- Create: `Assets/Scripts/World/Buildings/CommercialBuildings/Pure/PriceResolver.cs`

- [ ] **Step 1 — Create the asmdef**

Path: `Assets/Scripts/World/Buildings/CommercialBuildings/Pure/MWI.Shop.Pure.asmdef`

```json
{
    "name": "MWI.Shop.Pure",
    "rootNamespace": "MWI.Shop",
    "references": [],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": true
}
```

`noEngineReferences: true` means this assembly cannot use UnityEngine — pure C#.

- [ ] **Step 2 — Create `PriceResolver`**

Path: `Assets/Scripts/World/Buildings/CommercialBuildings/Pure/PriceResolver.cs`

```csharp
namespace MWI.Shop
{
    /// <summary>
    /// Pure helper resolving the effective shop price for a catalog entry.
    /// Override > 0 wins; otherwise fall back to the item's base price.
    /// Negative values clamp to zero (defensive against bad authoring).
    /// </summary>
    public static class PriceResolver
    {
        public static int Resolve(int basePrice, int priceOverride)
        {
            if (priceOverride > 0) return priceOverride;
            return basePrice > 0 ? basePrice : 0;
        }
    }
}
```

- [ ] **Step 3 — Refresh + compile**

Run `mcp__ai-game-developer__assets-refresh`. Verify zero compile errors.
Expected: new assembly `MWI.Shop.Pure` registers; `PriceResolver` class compiles.

- [ ] **Step 4 — Commit**

```bash
git add Assets/Scripts/World/Buildings/CommercialBuildings/Pure/
git commit -m "feat(shop): extract MWI.Shop.Pure assembly + PriceResolver"
```

---

### Task 4: Test asmdef + `PriceResolverTests`

**Files:**
- Create: `Assets/Tests/EditMode/Shop/Shop.Tests.asmdef`
- Create: `Assets/Tests/EditMode/Shop/PriceResolverTests.cs`

- [ ] **Step 1 — Create the test asmdef**

Path: `Assets/Tests/EditMode/Shop/Shop.Tests.asmdef`

```json
{
    "name": "Shop.Tests",
    "rootNamespace": "MWI.Tests.Shop",
    "references": [
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner",
        "MWI.Shop.Pure"
    ],
    "includePlatforms": [
        "Editor"
    ],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": [
        "nunit.framework.dll"
    ],
    "autoReferenced": false,
    "defineConstraints": [
        "UNITY_INCLUDE_TESTS"
    ],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 2 — Write the failing tests**

Path: `Assets/Tests/EditMode/Shop/PriceResolverTests.cs`

```csharp
using NUnit.Framework;
using MWI.Shop;

namespace MWI.Tests.Shop
{
    public class PriceResolverTests
    {
        [Test]
        public void Override_WinsOverBase_WhenOverrideIsPositive()
        {
            Assert.AreEqual(50, PriceResolver.Resolve(basePrice: 10, priceOverride: 50));
        }

        [Test]
        public void OverrideOfOne_IsRespected()
        {
            // Owner explicitly sets price = 1 (cheapest possible non-fallback)
            Assert.AreEqual(1, PriceResolver.Resolve(basePrice: 100, priceOverride: 1));
        }

        [Test]
        public void OverrideOfZero_FallsBackToBase()
        {
            Assert.AreEqual(10, PriceResolver.Resolve(basePrice: 10, priceOverride: 0));
        }

        [Test]
        public void NegativeOverride_FallsBackToBase()
        {
            Assert.AreEqual(10, PriceResolver.Resolve(basePrice: 10, priceOverride: -5));
        }

        [Test]
        public void ZeroBase_ZeroOverride_ReturnsZero()
        {
            Assert.AreEqual(0, PriceResolver.Resolve(basePrice: 0, priceOverride: 0));
        }

        [Test]
        public void NegativeBase_ClampsToZero_OnFallback()
        {
            Assert.AreEqual(0, PriceResolver.Resolve(basePrice: -10, priceOverride: 0));
        }
    }
}
```

- [ ] **Step 3 — Run tests**

Run via `mcp__ai-game-developer__tests-run` with mode=EditMode + filter `Shop.Tests`.
Expected: all 6 tests PASS. (The implementation in Task 3 already satisfies them — the test acts as an executable specification of the contract.)

- [ ] **Step 4 — Commit**

```bash
git add Assets/Tests/EditMode/Shop/
git commit -m "test(shop): PriceResolver coverage (override-vs-base, sentinels, clamp)"
```

---

### Task 5: Extend `ShopItemEntry` with `PriceOverride`

**Files:**
- Modify: `Assets/Scripts/World/Buildings/CommercialBuildings/ShopBuilding.cs`

- [ ] **Step 1 — Extend `ShopItemEntry`**

In `ShopBuilding.cs`, replace the existing `ShopItemEntry` struct (lines 7-12) with:

```csharp
[System.Serializable]
public struct ShopItemEntry
{
    public ItemSO Item;
    public int MaxStock;

    [Tooltip("0 = use ItemSO.BasePrice")]
    public int PriceOverride;
}
```

- [ ] **Step 2 — Add `ResolvePrice` helper**

Inside `ShopBuilding` class, near the top (after `BuildingType` getter), add:

```csharp
/// <summary>
/// Resolves the effective sell price for a catalog entry: override wins when
/// positive, otherwise the item's base price. Routes through the pure helper
/// in MWI.Shop.Pure so the same logic is unit-testable.
/// </summary>
public static int ResolvePrice(ShopItemEntry entry)
{
    int basePrice = entry.Item != null ? entry.Item.BasePrice : 0;
    return MWI.Shop.PriceResolver.Resolve(basePrice, entry.PriceOverride);
}
```

- [ ] **Step 3 — Refresh + compile**

Run `mcp__ai-game-developer__assets-refresh`. Verify zero compile errors.
Expected: existing `ShopItemEntry` references still compile (struct field additions are non-breaking).

- [ ] **Step 4 — Commit**

```bash
git add Assets/Scripts/World/Buildings/CommercialBuildings/ShopBuilding.cs
git commit -m "feat(shop): extend ShopItemEntry with PriceOverride + ResolvePrice helper"
```

---

## Wave 2 — Cashier furniture skeleton

### Task 6: Create `Cashier.cs` (state-only, no networking)

**Files:**
- Create: `Assets/Scripts/World/Furniture/Cashier.cs`

- [ ] **Step 1 — Write the class**

Path: `Assets/Scripts/World/Furniture/Cashier.cs`

```csharp
using System.Collections.Generic;
using MWI.Economy;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Customer-facing transaction counter inside a CommercialBuilding (today
/// only ShopBuilding uses this; future BankBuilding etc. may too).
///
/// Three orthogonal state slots:
/// • Occupant (inherited from Furniture) — vendor currently driving the cashier.
/// • CurrentCustomer — customer mid-transaction (the lock).
/// • Till — money held by this cashier (per-currency).
///
/// Server-authoritative — all mutations gated on IsServer and replicated via
/// the sibling CashierNetSync component.
/// </summary>
[RequireComponent(typeof(CashierInteractable))]
[RequireComponent(typeof(CashierNetSync))]
public class Cashier : Furniture
{
    [Header("Cashier")]
    [Tooltip("If false, this is an automatic distributor — no vendor required to serve customers.")]
    [SerializeField] private bool _requiresVendor = true;
    public bool RequiresVendor => _requiresVendor;

    [Tooltip("Radius around InteractionPoint within which an on-shift vendor auto-seats.")]
    [SerializeField] private float _autoSeatRadius = 1.5f;

    private CommercialBuilding _linkedBuilding;
    public CommercialBuilding LinkedBuilding => _linkedBuilding;
    public ShopBuilding LinkedShop => _linkedBuilding as ShopBuilding;

    private Character _currentCustomer;
    public Character CurrentCustomer => _currentCustomer;

    public bool IsAvailableForCustomer =>
        _currentCustomer == null
        && (!_requiresVendor || Occupant != null);

    private readonly Dictionary<CurrencyId, int> _till = new();
    public int GetTillBalance(CurrencyId c) => _till.TryGetValue(c, out var v) ? v : 0;
    public IReadOnlyDictionary<CurrencyId, int> GetAllTillBalances() => _till;

    private CashierNetSync _netSync;
    public CashierNetSync NetSync => _netSync;

    protected void Awake()
    {
        _linkedBuilding = GetComponentInParent<CommercialBuilding>();
        _netSync = GetComponent<CashierNetSync>();
    }

    // Lifecycle hooks for register/unregister filled in Wave 3 (Task 11).
    // Server-only logic (lock, till mutations, auto-occupy) filled in Wave 3.
}
```

- [ ] **Step 2 — Refresh + compile**

Run `mcp__ai-game-developer__assets-refresh`. Verify the only compile errors are about the not-yet-created `CashierInteractable` and `CashierNetSync` (handled in Tasks 7 + 8). No semantic errors in `Cashier.cs` itself.

- [ ] **Step 3 — Commit**

```bash
git add Assets/Scripts/World/Furniture/Cashier.cs
git commit -m "feat(cashier): scaffold Cashier furniture (state slots, getters, no logic yet)"
```

---

### Task 7: Create `CashierInteractable.cs`

**Files:**
- Create: `Assets/Scripts/World/Furniture/CashierInteractable.cs`

- [ ] **Step 1 — Write the class**

Path: `Assets/Scripts/World/Furniture/CashierInteractable.cs`

```csharp
using MWI.UI.Notifications;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Player-facing interactable surface on a Cashier. Tap-E entry routes the
/// customer through Cashier.RequestStartBuyServerRpc (server-relay).
///
/// Pre-gates on the local client (vendor present? cashier free?) so the
/// player gets an immediate toast without an RPC roundtrip — server still
/// re-validates authoritatively in the RPC.
///
/// Mirrors the Phase 1 BuildingInteractable pattern (2026-05-06
/// construction-loop spec): InteractableObject base + ServerRpc relay +
/// no client-side authoritative state.
/// </summary>
[RequireComponent(typeof(Cashier))]
public class CashierInteractable : InteractableObject
{
    private const string PromptShop = "Press E to shop";
    private const string PromptNoVendor = "No vendor on duty";
    private const string PromptBusy = "Vendor is busy";

    private Cashier _cashier;

    protected void Awake()
    {
        _cashier = GetComponent<Cashier>();

        if (string.IsNullOrEmpty(interactionPrompt) || interactionPrompt == "Press E to interact")
        {
            interactionPrompt = PromptShop;
        }
    }

    public override void Interact(Character interactor)
    {
        if (interactor == null || _cashier == null) return;
        if (!IsCharacterInInteractionZone(interactor)) return;

        // Local pre-gate (immediate toast on the offending client).
        if (_cashier.RequiresVendor && _cashier.Occupant == null)
        {
            UI_Toast.Show("No vendor on duty.", ToastType.Warning);
            return;
        }
        if (_cashier.CurrentCustomer != null && _cashier.CurrentCustomer != interactor)
        {
            UI_Toast.Show("Shop vendor is busy with another customer.", ToastType.Warning);
            return;
        }

        // Server-relay. The server re-validates and may still send a busy toast back if a race occurred.
        _cashier.NetSync.RequestStartBuyServerRpc(new NetworkBehaviourReference(interactor));
    }
}
```

- [ ] **Step 2 — Refresh + compile**

Run `mcp__ai-game-developer__assets-refresh`. Compile errors expected on `RequestStartBuyServerRpc` (lives on `CashierNetSync`, created in Task 8). Other errors should be limited to that.

- [ ] **Step 3 — Commit**

```bash
git add Assets/Scripts/World/Furniture/CashierInteractable.cs
git commit -m "feat(cashier): CashierInteractable tap-E entry + pre-gate toasts"
```

---

### Task 8: Create `CashierNetSync.cs` (skeleton)

**Files:**
- Create: `Assets/Scripts/World/Furniture/CashierNetSync.cs`

- [ ] **Step 1 — Write the network sibling**

Path: `Assets/Scripts/World/Furniture/CashierNetSync.cs`

```csharp
using MWI.Economy;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Network sibling for Cashier. Owns:
/// • _currentCustomerNetworkObjectId: NetworkVariable&lt;ulong&gt; — the lock state.
/// • _tillBalances: NetworkList&lt;CurrencyBalanceEntry&gt; — replicated till.
/// • _linkedBuildingRef: NetworkVariable&lt;NetworkObjectReference&gt; — for late-joiners.
///
/// Also surfaces the customer-facing ServerRpcs (RequestStartBuy / SubmitPlayerSelection /
/// CancelPlayerTransaction) and the vendor-occupied notification ClientRpcs.
///
/// Sealed bridge between Cashier (plain Furniture, owns the in-memory state) and
/// the network mirror — Cashier never derives from NetworkBehaviour directly,
/// preserving the existing Furniture base contract.
/// </summary>
[RequireComponent(typeof(Cashier))]
public class CashierNetSync : NetworkBehaviour
{
    private Cashier _cashier;

    public NetworkVariable<ulong> CurrentCustomerNetworkObjectId = new(
        0,
        readPermission: NetworkVariableReadPermission.Everyone,
        writePermission: NetworkVariableWritePermission.Server);

    public NetworkList<CurrencyBalanceEntry> TillBalances;

    public NetworkVariable<NetworkObjectReference> LinkedBuildingRef = new(
        default,
        readPermission: NetworkVariableReadPermission.Everyone,
        writePermission: NetworkVariableWritePermission.Server);

    protected void Awake()
    {
        _cashier = GetComponent<Cashier>();
        TillBalances = new NetworkList<CurrencyBalanceEntry>(
            null,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
    }

    // ----- Server-side helpers -----

    public void SetCurrentCustomerServer(ulong networkObjectId)
    {
        if (!IsServer) return;
        CurrentCustomerNetworkObjectId.Value = networkObjectId;
    }

    public void SetLinkedBuildingServer(NetworkObjectReference reference)
    {
        if (!IsServer) return;
        LinkedBuildingRef.Value = reference;
    }

    public void SetTillBalanceServer(CurrencyId currency, int balance)
    {
        if (!IsServer) return;
        for (int i = 0; i < TillBalances.Count; i++)
        {
            if (TillBalances[i].currencyId == currency.Id)
            {
                if (balance == 0) { TillBalances.RemoveAt(i); return; }
                TillBalances[i] = new CurrencyBalanceEntry { currencyId = currency.Id, amount = balance };
                return;
            }
        }
        if (balance != 0)
            TillBalances.Add(new CurrencyBalanceEntry { currencyId = currency.Id, amount = balance });
    }

    // ----- ClientRpcs -----

    [ClientRpc]
    public void NotifyOccupiedClientRpc(ulong vendorNetworkObjectId)
    {
        // Hook for future visual effects / prompt refresh on every peer.
        // Phase 2b intentionally has no visual yet — gate adding any here.
    }

    [ClientRpc]
    public void NotifyReleasedClientRpc()
    {
        // Same — visual hook reserved.
    }

    [ClientRpc]
    public void OpenBuyPanelClientRpc(ulong customerNetworkObjectId, ulong cashierNetworkObjectId, ClientRpcParams p = default)
    {
        // Wire to UI_ShopBuyPanel in Wave 8 (Task 24). For now, no-op so the
        // pre-Wave-8 manual smoke test doesn't NRE.
    }

    [ClientRpc]
    public void CloseBuyPanelClientRpc(ulong customerNetworkObjectId, ClientRpcParams p = default)
    {
        // Wire to UI_ShopBuyPanel in Wave 8.
    }

    [ClientRpc]
    public void SendBusyToastClientRpc(ClientRpcParams p = default)
    {
        // Targeted toast to a single client (rule #19 — never broadcast personal-context toasts).
        MWI.UI.Notifications.UI_Toast.Show(
            "Shop vendor is busy with another customer.",
            MWI.UI.Notifications.ToastType.Warning);
    }

    [ClientRpc]
    public void ToastClientRpc(string message, MWI.UI.Notifications.ToastType type, ClientRpcParams p = default)
    {
        MWI.UI.Notifications.UI_Toast.Show(message, type);
    }

    // ----- ServerRpcs (filled in later waves) -----

    [ServerRpc(RequireOwnership = false)]
    public void RequestStartBuyServerRpc(NetworkBehaviourReference customerRef, ServerRpcParams p = default)
    {
        // Implemented in Task 22 (after CharacterAction_BuyFromShop exists).
        Debug.LogWarning($"[Cashier] RequestStartBuyServerRpc: not yet implemented (filled in Wave 6).");
    }

    [ServerRpc(RequireOwnership = false)]
    public void SubmitPlayerSelectionServerRpc(BuySelectionPayload payload, ServerRpcParams p = default)
    {
        // Implemented in Task 22.
        Debug.LogWarning($"[Cashier] SubmitPlayerSelectionServerRpc: not yet implemented (filled in Wave 6).");
    }

    [ServerRpc(RequireOwnership = false)]
    public void CancelPlayerTransactionServerRpc(ServerRpcParams p = default)
    {
        // Implemented in Task 22.
        Debug.LogWarning($"[Cashier] CancelPlayerTransactionServerRpc: not yet implemented (filled in Wave 6).");
    }
}

/// <summary>
/// Network-serializable payload for player buy submissions. Contains an
/// array of (itemId, quantity) pairs.
/// </summary>
public struct BuySelectionPayload : INetworkSerializable
{
    public Unity.Collections.FixedString64Bytes[] ItemIds;
    public int[] Quantities;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        // NGO requires explicit array length serialisation for non-fixed arrays.
        int len = ItemIds?.Length ?? 0;
        serializer.SerializeValue(ref len);
        if (serializer.IsReader)
        {
            ItemIds = new Unity.Collections.FixedString64Bytes[len];
            Quantities = new int[len];
        }
        for (int i = 0; i < len; i++)
        {
            serializer.SerializeValue(ref ItemIds[i]);
            serializer.SerializeValue(ref Quantities[i]);
        }
    }
}
```

- [ ] **Step 2 — Refresh + compile**

Run `mcp__ai-game-developer__assets-refresh`. Expected: clean compile (the `Debug.LogWarning` placeholders prevent semantic errors; subsequent waves replace them with real bodies).

- [ ] **Step 3 — Commit**

```bash
git add Assets/Scripts/World/Furniture/CashierNetSync.cs
git commit -m "feat(cashier): CashierNetSync skeleton (NetVars, NetworkList, RPC stubs)"
```

---

## Wave 3 — Cashier server-only logic

### Task 9: Implement `Cashier.TryAcquireCustomerLock` / `ReleaseCustomerLock`

**Files:**
- Modify: `Assets/Scripts/World/Furniture/Cashier.cs`

- [ ] **Step 1 — Add lock methods**

Inside `Cashier`, append before the closing brace:

```csharp
/// <summary>
/// Server-only — acquires the transaction lock for this customer. Returns false
/// if the cashier is already busy or has no vendor (when one is required).
/// </summary>
public bool TryAcquireCustomerLock(Character customer)
{
    if (!_netSync.IsServer) return false;
    if (customer == null) return false;
    if (!IsAvailableForCustomer) return false;

    _currentCustomer = customer;
    _netSync.SetCurrentCustomerServer(customer.NetworkObjectId);
    return true;
}

/// <summary>
/// Server-only — releases the transaction lock. Logs a warning if the caller
/// is not the holder (defensive — should never happen).
/// </summary>
public void ReleaseCustomerLock(Character customer)
{
    if (!_netSync.IsServer) return;
    if (_currentCustomer != customer)
    {
        Debug.LogWarning($"[Cashier] ReleaseCustomerLock: caller {customer?.CharacterName ?? "null"} is not the holder ({_currentCustomer?.CharacterName ?? "null"}). Ignored.");
        return;
    }
    _currentCustomer = null;
    _netSync.SetCurrentCustomerServer(0);
}
```

- [ ] **Step 2 — Refresh + compile**

Run `mcp__ai-game-developer__assets-refresh`. Expected: clean compile.

- [ ] **Step 3 — Commit**

```bash
git add Assets/Scripts/World/Furniture/Cashier.cs
git commit -m "feat(cashier): customer-lock acquire/release (server-only)"
```

---

### Task 10: Implement `Cashier.CreditTill` / `DebitTill`

**Files:**
- Modify: `Assets/Scripts/World/Furniture/Cashier.cs`

- [ ] **Step 1 — Add till methods**

Inside `Cashier`, append:

```csharp
/// <summary>
/// Server-only — adds coins to the till. Logs and noops on non-positive amounts.
/// Mirrors the new balance into the replicated NetworkList.
/// </summary>
public void CreditTill(CurrencyId currency, int amount, string source)
{
    if (!_netSync.IsServer) return;
    if (amount <= 0)
    {
        Debug.LogError($"[Cashier] CreditTill rejected: amount={amount} source={source} on {FurnitureName}");
        return;
    }
    int next = GetTillBalance(currency) + amount;
    _till[currency] = next;
    _netSync.SetTillBalanceServer(currency, next);
}

/// <summary>
/// Server-only — removes coins from the till. Returns false if the till is short.
/// </summary>
public bool DebitTill(CurrencyId currency, int amount, string reason)
{
    if (!_netSync.IsServer) return false;
    if (amount <= 0)
    {
        Debug.LogError($"[Cashier] DebitTill rejected: amount={amount} reason={reason} on {FurnitureName}");
        return false;
    }
    int current = GetTillBalance(currency);
    if (current < amount) return false;
    int next = current - amount;
    if (next == 0) _till.Remove(currency);
    else _till[currency] = next;
    _netSync.SetTillBalanceServer(currency, next);
    return true;
}
```

- [ ] **Step 2 — Refresh + compile**

Run `mcp__ai-game-developer__assets-refresh`. Expected: clean compile.

- [ ] **Step 3 — Commit**

```bash
git add Assets/Scripts/World/Furniture/Cashier.cs
git commit -m "feat(cashier): credit/debit till (server-only) with NetworkList mirror"
```

---

### Task 11: Implement Cashier auto-occupy server tick + register/unregister hooks

**Files:**
- Modify: `Assets/Scripts/World/Furniture/Cashier.cs`

- [ ] **Step 1 — Add lifecycle + auto-occupy logic**

Inside `Cashier`, replace the `protected void Awake()` block and append below it:

```csharp
private float _autoSeatTimer;
private const float AUTO_SEAT_TICK_INTERVAL = 1f;
private static readonly Collider[] _scratchColliders = new Collider[16]; // shared per-cashier scratch — server-only writes
private bool _registered;

protected void Awake()
{
    _linkedBuilding = GetComponentInParent<CommercialBuilding>();
    _netSync = GetComponent<CashierNetSync>();
}

protected void OnEnable()
{
    // Server-only registration; the NetworkBehaviour sibling's IsServer check is
    // inside RegisterCashier so this is safe to call on every peer.
    if (LinkedShop != null) { LinkedShop.RegisterCashier(this); _registered = true; }
}

protected void OnDisable()
{
    if (_registered && LinkedShop != null) LinkedShop.UnregisterCashier(this);
    _registered = false;

    // Mid-transaction safety — abort any active customer action.
    if (_currentCustomer != null && _netSync != null && _netSync.IsServer)
    {
        AbortActiveTransactionServerOnly("cashier removed");
    }

    // Drop till coins as WorldItems on the ground.
    if (_netSync != null && _netSync.IsServer && _till.Count > 0)
    {
        DropTillCoinsAsWorldItems();
    }
}

private void Update()
{
    if (_netSync == null || !_netSync.IsServer) return;
    _autoSeatTimer += Time.unscaledDeltaTime;
    if (_autoSeatTimer >= AUTO_SEAT_TICK_INTERVAL)
    {
        _autoSeatTimer = 0f;
        ServerTickAutoOccupy();
    }
}

private void ServerTickAutoOccupy()
{
    if (Occupant != null) return;
    if (_currentCustomer != null) return;

    // Find any character within InteractionPoint range whose CharacterJob is a
    // JobVendor of this shop and who is currently on shift.
    int n = Physics.OverlapSphereNonAlloc(GetInteractionPosition(), _autoSeatRadius, _scratchColliders);
    for (int i = 0; i < n; i++)
    {
        var collider = _scratchColliders[i];
        if (collider == null) continue;
        var character = collider.GetComponentInParent<Character>();
        if (character == null) continue;
        if (character.CharacterJob == null) continue;
        if (character.CharacterJob.CurrentJob is not JobVendor jv) continue;
        if (jv.Workplace != _linkedBuilding) continue;
        if (character.CharacterSchedule == null || !character.CharacterSchedule.IsOnWorkShiftNow) continue;
        Use(character);
        break;
    }
}

/// <summary>
/// Internal — server-only — called when the cashier is being removed mid-transaction.
/// Cancels the active CharacterAction_BuyFromShop on the customer. The action's
/// OnCancel path releases the lock and closes any open Player UI.
/// </summary>
internal void AbortActiveTransactionServerOnly(string reason)
{
    if (_currentCustomer == null) return;
    _currentCustomer.CharacterActions?.CancelCurrentAction();
    Debug.Log($"[Cashier] Aborted active transaction: {reason}");
}

private void DropTillCoinsAsWorldItems()
{
    // Phase 2b ships till-as-int — coin item drop is deferred until the future
    // currency-as-item refactor (or treasury session). For now, log the lost
    // coins so playtesters know.
    foreach (var kv in _till)
    {
        Debug.LogWarning($"[Cashier] {FurnitureName} removed with {kv.Value} of currency {kv.Key.Id} in till. Coins are lost (TODO: drop as WorldItems once coin items exist).");
    }
    _till.Clear();
}
```

- [ ] **Step 2 — Override `Use` and `Release` for client-rpc broadcast**

Inside `Cashier`, append:

```csharp
public override bool Use(Character vendor)
{
    if (!base.Use(vendor)) return false;
    if (_netSync != null && _netSync.IsServer)
        _netSync.NotifyOccupiedClientRpc(vendor.NetworkObjectId);
    return true;
}

public override void Release()
{
    bool wasOccupied = IsOccupied;
    base.Release();
    if (wasOccupied && _netSync != null && _netSync.IsServer)
    {
        _netSync.NotifyReleasedClientRpc();
        if (_currentCustomer != null)
            AbortActiveTransactionServerOnly("vendor walked off");
    }
}
```

- [ ] **Step 3 — Refresh + compile**

Run `mcp__ai-game-developer__assets-refresh`. Expected: errors on `LinkedShop.RegisterCashier / UnregisterCashier` (filled in Wave 4 Task 13). All other code clean.

- [ ] **Step 4 — Commit**

```bash
git add Assets/Scripts/World/Furniture/Cashier.cs
git commit -m "feat(cashier): auto-occupy server tick + lifecycle hooks (register/abort/till-drop)"
```

---

## Wave 4 — ShopBuilding refactor

### Task 12: Add `ShopBuilding._catalog` / `_sellShelves` / `_cashiers` + getters

**Files:**
- Modify: `Assets/Scripts/World/Buildings/CommercialBuildings/ShopBuilding.cs`

- [ ] **Step 1 — Convert `_itemsToSell` to a runtime catalog**

In `ShopBuilding`, replace the fields under `[Header("Shop Settings")]` (lines 27-28) with:

```csharp
[Header("Shop Settings")]
[Tooltip("Inspector-authored seed catalog. At runtime this is copied into the mutable _catalog list (which the management UI edits).")]
[SerializeField] private List<ShopItemEntry> _seedCatalog = new List<ShopItemEntry>();

private List<ShopItemEntry> _catalog;
public IReadOnlyList<ShopItemEntry> Catalog => _catalog;

private List<StorageFurniture> _sellShelves = new List<StorageFurniture>();
public IReadOnlyList<StorageFurniture> SellShelves => _sellShelves;

private List<Cashier> _cashiers = new List<Cashier>();
public IReadOnlyList<Cashier> Cashiers => _cashiers;

public event System.Action OnCatalogChanged;
public event System.Action OnSellShelvesChanged;
public event System.Action OnCashiersChanged;
```

- [ ] **Step 2 — Adjust `_itemsToSell` references throughout `ShopBuilding`**

Search-replace within `ShopBuilding.cs`:
- `_itemsToSell` → `_catalog`
- After the change, find the `Awake` override (or add one if missing) and seed the catalog from the inspector list:

```csharp
protected override void Awake()
{
    base.Awake();
    if (_catalog == null)
    {
        _catalog = new List<ShopItemEntry>(_seedCatalog?.Count ?? 0);
        if (_seedCatalog != null) _catalog.AddRange(_seedCatalog);
    }
}
```

(Note: if `CommercialBuilding.Awake` is not virtual today, make it virtual or use a different lifecycle hook — `OnNetworkSpawn` is the safer choice. Use `OnNetworkSpawn` instead and call `base.OnNetworkSpawn()` first.)

- [ ] **Step 3 — Update `IStockProvider.GetStockTargets()` to iterate `_catalog`**

Already does — the rename in Step 2 covers it.

- [ ] **Step 4 — Refresh + compile**

Run `mcp__ai-game-developer__assets-refresh`. Expected: zero compile errors.
**Run all existing EditMode tests** via `mcp__ai-game-developer__tests-run` to verify no regression. Expected: all pre-existing tests still pass.

- [ ] **Step 5 — Commit**

```bash
git add Assets/Scripts/World/Buildings/CommercialBuildings/ShopBuilding.cs
git commit -m "refactor(shop): mutable runtime catalog + sell-shelves + cashiers lists"
```

---

### Task 13: Add `RegisterCashier` / `UnregisterCashier` (pool model)

**Files:**
- Modify: `Assets/Scripts/World/Buildings/CommercialBuildings/ShopBuilding.cs`

- [ ] **Step 1 — Add the methods**

Inside `ShopBuilding`, append after the `OnCashiersChanged` event:

```csharp
/// <summary>
/// Server-only — called by Cashier.OnEnable when a cashier becomes a child of this shop.
/// Adds a generic JobVendor slot to the pool if the cashier requires a vendor.
/// </summary>
public void RegisterCashier(Cashier cashier)
{
    if (!IsServer) return;
    if (cashier == null || _cashiers.Contains(cashier)) return;

    _cashiers.Add(cashier);
    OnCashiersChanged?.Invoke();

    if (cashier.RequiresVendor)
    {
        _jobs.Add(new JobVendor());   // Pool model — slot is generic, not bound.
        OnJobsChanged?.Invoke();
    }
}

/// <summary>
/// Server-only — called by Cashier.OnDisable. Removes the cashier from the list,
/// and if it was vendor-requiring, removes one generic JobVendor slot from the pool.
/// Existing fire flow handles any worker assigned to the removed slot.
/// </summary>
public void UnregisterCashier(Cashier cashier)
{
    if (!IsServer) return;
    int idx = _cashiers.IndexOf(cashier);
    if (idx < 0) return;

    _cashiers.RemoveAt(idx);
    OnCashiersChanged?.Invoke();

    if (cashier.RequiresVendor)
    {
        for (int i = _jobs.Count - 1; i >= 0; i--)
        {
            if (_jobs[i] is JobVendor jv)
            {
                jv.Unassign();
                _jobs.RemoveAt(i);
                break;
            }
        }
        OnJobsChanged?.Invoke();
    }
}
```

- [ ] **Step 2 — Verify `OnJobsChanged` exists on `CommercialBuilding`**

Open `Assets/Scripts/World/Buildings/CommercialBuilding.cs` and confirm the event is declared (or compatible with `_jobs` mutations). If not, add an `event System.Action OnJobsChanged;` field next to the `_jobs` declaration. Fire it from any existing `_jobs.Add` / `Remove` site (e.g., `InitializeJobs`).

- [ ] **Step 3 — Refresh + compile**

Run `mcp__ai-game-developer__assets-refresh`. Expected: clean compile.

- [ ] **Step 4 — Commit**

```bash
git add Assets/Scripts/World/Buildings/CommercialBuildings/ShopBuilding.cs Assets/Scripts/World/Buildings/CommercialBuilding.cs
git commit -m "feat(shop): RegisterCashier/UnregisterCashier with pool-model JobVendor slots"
```

---

### Task 14: Add `GetCatalogEntry` + `GetFirstAvailableCashier` helpers

**Files:**
- Modify: `Assets/Scripts/World/Buildings/CommercialBuildings/ShopBuilding.cs`

- [ ] **Step 1 — Add helpers**

Inside `ShopBuilding`, append:

```csharp
/// <summary>
/// O(N) scan for the catalog entry matching the given ItemSO. Returns null if
/// not in the catalog. N is small (≤ ~50 in practice).
/// </summary>
public ShopItemEntry? GetCatalogEntry(ItemSO item)
{
    if (item == null || _catalog == null) return null;
    for (int i = 0; i < _catalog.Count; i++)
        if (_catalog[i].Item == item) return _catalog[i];
    return null;
}

/// <summary>
/// Returns the first cashier in this shop that is currently
/// IsAvailableForCustomer (free + has a vendor if required). Order follows
/// _cashiers list (registration order). No tie-breaking rule beyond first match.
/// </summary>
public Cashier GetFirstAvailableCashier()
{
    if (_cashiers == null) return null;
    for (int i = 0; i < _cashiers.Count; i++)
    {
        var c = _cashiers[i];
        if (c != null && c.IsAvailableForCustomer) return c;
    }
    return null;
}
```

- [ ] **Step 2 — Refresh + compile**

Run `mcp__ai-game-developer__assets-refresh`. Expected: clean compile.

- [ ] **Step 3 — Commit**

```bash
git add Assets/Scripts/World/Buildings/CommercialBuildings/ShopBuilding.cs
git commit -m "feat(shop): GetCatalogEntry + GetFirstAvailableCashier helpers"
```

---

### Task 15: Delete deprecated queue + vendor-point surfaces

**Files:**
- Modify: `Assets/Scripts/World/Buildings/CommercialBuildings/ShopBuilding.cs`

- [ ] **Step 1 — Delete fields and methods**

Delete the following from `ShopBuilding`:

```csharp
// DELETE these:
[Header("Work Positions")]
[SerializeField] private Transform _vendorPoint;
public Transform VendorPoint => _vendorPoint;

private Queue<Character> _customerQueue = new Queue<Character>();
public int CustomersInQueue => _customerQueue.Count;
public void JoinQueue(Character customer) { ... }
public Character GetNextCustomer() { ... }
public void ClearQueue() { ... }

public override Vector3 GetWorkPosition(Character worker) { ... } // remove the JobVendor branch only; keep the base.GetWorkPosition fallback if other jobs use this method

public JobVendor GetVendor() { ... } // singular — replaced by Vendors getter below
```

Replace `GetVendor` with a multi-vendor projection:

```csharp
/// <summary>Snapshot of all currently-active JobVendor slots in this shop. NOT cached — used only by debug UI / management panel; if it becomes hot, cache via dirty flag (rule #34).</summary>
public IEnumerable<JobVendor> Vendors
{
    get
    {
        for (int i = 0; i < _jobs.Count; i++)
            if (_jobs[i] is JobVendor jv) yield return jv;
    }
}
```

- [ ] **Step 2 — Refresh + compile**

Run `mcp__ai-game-developer__assets-refresh`. Expect compile errors at every former call site:
- `JobVendor.cs` — references to `shop.JoinQueue` / `GetNextCustomer` / `_currentClient` etc. (refactored in Wave 5)
- `GoapAction_GoShopping.cs` — references to `_shop.JoinQueue` (refactored in Wave 7)
- Any debug UI referencing `CustomersInQueue` / `VendorPoint` / `GetVendor` — leave broken until refactor; these will be fixed in their owning waves or in Wave 11 cleanup.

These breaks are EXPECTED. **Do not fix the call sites here** — they're wave-specific.

- [ ] **Step 3 — Commit (with intentional breakage)**

```bash
git add Assets/Scripts/World/Buildings/CommercialBuildings/ShopBuilding.cs
git commit -m "refactor(shop): delete legacy queue + vendor-point surfaces (call sites broken; fixed in W5+W7)"
```

---

### Task 16: Create `ShopBuildingNetSync.cs`

**Files:**
- Create: `Assets/Scripts/World/Buildings/CommercialBuildings/ShopBuildingNetSync.cs`

- [ ] **Step 1 — Write the network sibling**

Path: `Assets/Scripts/World/Buildings/CommercialBuildings/ShopBuildingNetSync.cs`

```csharp
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Collections;
using UnityEngine;

/// <summary>
/// Network sibling for ShopBuilding. Replicates the runtime catalog,
/// sell-shelves, and cashiers lists so clients can render the buy UI / management UI
/// from authoritative server state without RPC pingpong per query.
/// </summary>
[RequireComponent(typeof(ShopBuilding))]
public class ShopBuildingNetSync : NetworkBehaviour
{
    private ShopBuilding _shop;

    public NetworkList<ShopItemEntryNet> Catalog;
    public NetworkList<NetworkObjectReference> SellShelves;
    public NetworkList<NetworkObjectReference> Cashiers;

    protected void Awake()
    {
        _shop = GetComponent<ShopBuilding>();
        Catalog = new NetworkList<ShopItemEntryNet>(null,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        SellShelves = new NetworkList<NetworkObjectReference>(null,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        Cashiers = new NetworkList<NetworkObjectReference>(null,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    }

    // ----- Server-only push helpers -----

    public void PushCatalogEntryAddedServer(ShopItemEntry entry)
    {
        if (!IsServer || entry.Item == null) return;
        Catalog.Add(ToNet(entry));
    }

    public void PushCatalogEntryRemovedServer(string itemId)
    {
        if (!IsServer) return;
        for (int i = Catalog.Count - 1; i >= 0; i--)
        {
            if (Catalog[i].itemId == itemId) { Catalog.RemoveAt(i); return; }
        }
    }

    public void PushCatalogEntryEditedServer(ShopItemEntry entry)
    {
        if (!IsServer || entry.Item == null) return;
        for (int i = 0; i < Catalog.Count; i++)
        {
            if (Catalog[i].itemId == entry.Item.ItemId) { Catalog[i] = ToNet(entry); return; }
        }
    }

    public void PushSellShelfAddedServer(NetworkObjectReference shelfRef)
    {
        if (!IsServer) return;
        SellShelves.Add(shelfRef);
    }

    public void PushSellShelfRemovedServer(NetworkObjectReference shelfRef)
    {
        if (!IsServer) return;
        for (int i = SellShelves.Count - 1; i >= 0; i--)
        {
            if (SellShelves[i].NetworkObjectId == shelfRef.NetworkObjectId) { SellShelves.RemoveAt(i); return; }
        }
    }

    public void PushCashierAddedServer(ulong cashierNetworkObjectId)
    {
        if (!IsServer) return;
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(cashierNetworkObjectId, out var obj))
            Cashiers.Add(new NetworkObjectReference(obj));
    }

    public void PushCashierRemovedServer(ulong cashierNetworkObjectId)
    {
        if (!IsServer) return;
        for (int i = Cashiers.Count - 1; i >= 0; i--)
        {
            if (Cashiers[i].NetworkObjectId == cashierNetworkObjectId) { Cashiers.RemoveAt(i); return; }
        }
    }

    [ClientRpc]
    public void SendUnauthorizedToastClientRpc(ClientRpcParams p = default)
    {
        MWI.UI.Notifications.UI_Toast.Show(
            "Only the shop owner can do that.",
            MWI.UI.Notifications.ToastType.Warning);
    }

    private static ShopItemEntryNet ToNet(ShopItemEntry e) => new()
    {
        itemId = new FixedString64Bytes(e.Item.ItemId),
        maxStock = e.MaxStock,
        priceOverride = e.PriceOverride
    };
}

public struct ShopItemEntryNet : INetworkSerializable, System.IEquatable<ShopItemEntryNet>
{
    public FixedString64Bytes itemId;
    public int maxStock;
    public int priceOverride;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref itemId);
        serializer.SerializeValue(ref maxStock);
        serializer.SerializeValue(ref priceOverride);
    }

    public bool Equals(ShopItemEntryNet other) =>
        itemId == other.itemId && maxStock == other.maxStock && priceOverride == other.priceOverride;
}
```

- [ ] **Step 2 — Refresh + compile**

Run `mcp__ai-game-developer__assets-refresh`. Expected: clean compile.

- [ ] **Step 3 — Commit**

```bash
git add Assets/Scripts/World/Buildings/CommercialBuildings/ShopBuildingNetSync.cs
git commit -m "feat(shop): ShopBuildingNetSync (catalog/shelves/cashiers replication)"
```

---

### Task 17: Owner-only ServerRpcs on `ShopBuilding`

**Files:**
- Modify: `Assets/Scripts/World/Buildings/CommercialBuildings/ShopBuilding.cs`

- [ ] **Step 1 — Wire NetSync reference**

Inside `ShopBuilding`, near the other private fields, add:

```csharp
private ShopBuildingNetSync _netSync;
public ShopBuildingNetSync NetSync => _netSync;
```

In `Awake` / `OnNetworkSpawn`, fetch it:

```csharp
_netSync = GetComponent<ShopBuildingNetSync>();
```

- [ ] **Step 2 — Add catalog ServerRpcs**

Append inside `ShopBuilding`:

```csharp
[ServerRpc(RequireOwnership = false)]
public void AddCatalogEntryServerRpc(string itemId, int maxStock, int priceOverride, ServerRpcParams p = default)
{
    if (!ValidateOwnerCaller(p)) { _netSync.SendUnauthorizedToastClientRpc(SingleClientRpcParams(p)); return; }
    var so = ResolveItemSO(itemId);
    if (so == null) { Debug.LogWarning($"[Shop] AddCatalogEntry: unknown itemId '{itemId}'"); return; }
    if (maxStock < 0) maxStock = 0;
    if (priceOverride < 0) priceOverride = 0;

    if (GetCatalogEntry(so) != null) return; // duplicate — silently ignore
    var entry = new ShopItemEntry { Item = so, MaxStock = maxStock, PriceOverride = priceOverride };
    _catalog.Add(entry);
    _netSync.PushCatalogEntryAddedServer(entry);
    OnCatalogChanged?.Invoke();
}

[ServerRpc(RequireOwnership = false)]
public void RemoveCatalogEntryServerRpc(string itemId, ServerRpcParams p = default)
{
    if (!ValidateOwnerCaller(p)) { _netSync.SendUnauthorizedToastClientRpc(SingleClientRpcParams(p)); return; }
    for (int i = _catalog.Count - 1; i >= 0; i--)
    {
        if (_catalog[i].Item != null && _catalog[i].Item.ItemId == itemId)
        {
            _catalog.RemoveAt(i);
            _netSync.PushCatalogEntryRemovedServer(itemId);
            OnCatalogChanged?.Invoke();
            return;
        }
    }
}

[ServerRpc(RequireOwnership = false)]
public void EditCatalogEntryServerRpc(string itemId, int newMaxStock, int newPriceOverride, ServerRpcParams p = default)
{
    if (!ValidateOwnerCaller(p)) { _netSync.SendUnauthorizedToastClientRpc(SingleClientRpcParams(p)); return; }
    if (newMaxStock < 0) newMaxStock = 0;
    if (newPriceOverride < 0) newPriceOverride = 0;
    for (int i = 0; i < _catalog.Count; i++)
    {
        if (_catalog[i].Item != null && _catalog[i].Item.ItemId == itemId)
        {
            var e = _catalog[i];
            e.MaxStock = newMaxStock;
            e.PriceOverride = newPriceOverride;
            _catalog[i] = e;
            _netSync.PushCatalogEntryEditedServer(e);
            OnCatalogChanged?.Invoke();
            return;
        }
    }
}

[ServerRpc(RequireOwnership = false)]
public void SetSellShelfFlagServerRpc(NetworkObjectReference shelfRef, bool isSellShelf, ServerRpcParams p = default)
{
    if (!ValidateOwnerCaller(p)) { _netSync.SendUnauthorizedToastClientRpc(SingleClientRpcParams(p)); return; }
    if (!shelfRef.TryGet(out NetworkObject shelfObj)) return;
    var storage = shelfObj.GetComponent<StorageFurniture>();
    if (storage == null) return;

    bool currentlyListed = _sellShelves.Contains(storage);
    if (isSellShelf && !currentlyListed)
    {
        _sellShelves.Add(storage);
        _netSync.PushSellShelfAddedServer(shelfRef);
        OnSellShelvesChanged?.Invoke();
    }
    else if (!isSellShelf && currentlyListed)
    {
        _sellShelves.Remove(storage);
        _netSync.PushSellShelfRemovedServer(shelfRef);
        OnSellShelvesChanged?.Invoke();
    }
}

[ServerRpc(RequireOwnership = false)]
public void WithdrawCashierTillServerRpc(NetworkObjectReference cashierRef, ServerRpcParams p = default)
{
    if (!ValidateOwnerCaller(p)) { _netSync.SendUnauthorizedToastClientRpc(SingleClientRpcParams(p)); return; }
    if (!cashierRef.TryGet(out NetworkObject cashierObj)) return;
    var cashier = cashierObj.GetComponent<Cashier>();
    if (cashier == null) return;

    var currency = MWI.Economy.CurrencyId.Default;
    int balance = cashier.GetTillBalance(currency);
    if (balance <= 0) return;
    if (!cashier.DebitTill(currency, balance, "OwnerWithdraw")) return;

    // For Phase 2b: deposit into owner wallet directly. Treasury redirect lands later.
    var owner = ResolveCharacterFromClientId(p.Receive.SenderClientId);
    owner?.CharacterWallet?.AddCoins(currency, balance, $"FromCashier_{cashier.FurnitureName}");
}

// ----- Helpers -----

private bool ValidateOwnerCaller(ServerRpcParams p)
{
    var caller = ResolveCharacterFromClientId(p.Receive.SenderClientId);
    if (caller == null) return false;
    if (Owner == null || caller != Owner)
    {
        Debug.LogWarning($"[Shop] {buildingName}: rejected ServerRpc — caller {caller.CharacterName} is not owner ({Owner?.CharacterName ?? "null"}).");
        return false;
    }
    return true;
}

private static ClientRpcParams SingleClientRpcParams(ServerRpcParams source) => new()
{
    Send = new ClientRpcSendParams { TargetClientIds = new[] { source.Receive.SenderClientId } }
};

private Character ResolveCharacterFromClientId(ulong clientId)
{
    if (NetworkManager.Singleton == null) return null;
    if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var nc)) return null;
    return nc?.PlayerObject?.GetComponent<Character>();
}

private static ItemSO ResolveItemSO(string itemId)
{
    if (string.IsNullOrEmpty(itemId)) return null;
    var all = Resources.LoadAll<ItemSO>("Data/Item");
    return System.Array.Find(all, x => x.ItemId == itemId);
}
```

- [ ] **Step 3 — Refresh + compile**

Run `mcp__ai-game-developer__assets-refresh`. Expected: clean compile (apart from leftover Wave 5 / 7 breaks).

- [ ] **Step 4 — Commit**

```bash
git add Assets/Scripts/World/Buildings/CommercialBuildings/ShopBuilding.cs
git commit -m "feat(shop): owner-only ServerRpcs (catalog edit, shelf flag, withdraw till)"
```

---

## Wave 5 — JobVendor pool model

### Task 18: Refactor `JobVendor.Execute` (pool-pick + race-friendly)

**Files:**
- Modify: `Assets/Scripts/World/Jobs/ServiceJobs/JobVendor.cs`

- [ ] **Step 1 — Replace the entire class body**

Path: `Assets/Scripts/World/Jobs/ServiceJobs/JobVendor.cs`

```csharp
using UnityEngine;

/// <summary>
/// Vendor job — pool model. The shop has N vendor slots (one per cashier
/// with RequiresVendor=true). Each shift, every assigned worker
/// independently picks any free cashier in the pool, races to claim it
/// via Reserve+Use (loser falls through to the next free one), and idles
/// while occupying. Customer interactions are customer-initiated through
/// CashierInteractable / RequestStartBuyServerRpc — there is no "call next
/// customer" loop here.
///
/// Player-vendor parity (rule #22): players aren't driven by Execute —
/// they walk where they want, and Cashier.ServerTickAutoOccupy seats them
/// when they happen to stand on the InteractionPoint during their work
/// shift. Symmetrical race semantics apply.
/// </summary>
public class JobVendor : Job
{
    public override string JobTitle => "Vendor";
    public override JobCategory Category => JobCategory.Service;

    private Cashier _heldCashier;
    private bool _hasReserved;
    private bool _isMovingToCashier;

    public Cashier HeldCashier => _heldCashier;

    public override string CurrentActionName
    {
        get
        {
            if (_heldCashier == null) return "Idle (no free cashier)";
            if (_heldCashier.Occupant == _worker) return $"Manning {_heldCashier.FurnitureName}";
            return $"Walking to {_heldCashier.FurnitureName}";
        }
    }

    public override bool CanExecute() =>
        base.CanExecute() && _workplace is ShopBuilding;

    public override void Execute()
    {
        if (_worker == null) return;

        // 1) Already occupying — idle.
        if (_heldCashier != null && _heldCashier.Occupant == _worker)
        {
            _isMovingToCashier = false;
            return;
        }

        // 2) Lost the seat (race / shift change / cashier removed) — drop and re-pick.
        if (_heldCashier != null && _heldCashier.Occupant != _worker)
        {
            if (_heldCashier.ReservedBy == _worker) _heldCashier.Release();
            _heldCashier = null;
            _hasReserved = false;
            _isMovingToCashier = false;
        }

        // 3) Pick a free cashier from the shop and walk to it.
        var shop = _workplace as ShopBuilding;
        if (shop == null) return;

        for (int i = 0; i < shop.Cashiers.Count; i++)
        {
            var c = shop.Cashiers[i];
            if (c == null) continue;
            if (!c.RequiresVendor) continue;
            if (c.Occupant != null) continue;
            if (c.ReservedBy != null && c.ReservedBy != _worker) continue;
            if (!c.Reserve(_worker)) continue;

            _heldCashier = c;
            _hasReserved = true;
            var movement = _worker.CharacterMovement;
            if (movement != null)
            {
                movement.SetDestination(c.GetInteractionPosition(_worker.transform.position));
                _isMovingToCashier = true;
            }
            return;
        }

        // No free cashier — vendor stays idle in the shop zone (existing fallback behavior).
    }

    public override void Unassign()
    {
        if (_heldCashier != null)
        {
            if (_heldCashier.Occupant == _worker) _heldCashier.Release();
            else if (_heldCashier.ReservedBy == _worker) _heldCashier.Release();
        }
        _heldCashier = null;
        _hasReserved = false;
        _isMovingToCashier = false;
        base.Unassign();
    }
}
```

- [ ] **Step 2 — Refresh + compile**

Run `mcp__ai-game-developer__assets-refresh`. Expected: clean compile (this also unblocks the breaks from Task 15).

- [ ] **Step 3 — Manual smoke check (Unity Editor)**

Open the test scene with one ShopBuilding placed. Use `mcp__ai-game-developer__editor-application-set-state` to enter PlayMode.
Expected behavior with one Cashier in the shop: vendor walks to the cashier's InteractionPoint and sits (Occupant becomes the vendor). With two cashiers, two vendors hired → each picks a different one within ~2 ticks.

- [ ] **Step 4 — Commit**

```bash
git add Assets/Scripts/World/Jobs/ServiceJobs/JobVendor.cs
git commit -m "refactor(jobvendor): pool model + race-friendly Reserve/Use loop"
```

---

## Wave 6 — `CharacterAction_BuyFromShop`

### Task 19: Create the action class skeleton

**Files:**
- Create: `Assets/Scripts/Character/CharacterActions/CharacterAction_BuyFromShop.cs`

- [ ] **Step 1 — Write the skeleton**

Path: `Assets/Scripts/Character/CharacterActions/CharacterAction_BuyFromShop.cs`

```csharp
using System.Collections.Generic;
using MWI.Economy;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative shop-purchase action. Single class, two completion
/// gates (NPC: 2s timer; Player: UI Confirm via ServerRpc).
///
/// Lifecycle:
/// 1. CanExecute — cashier free + has vendor (when required) + customer not null.
/// 2. OnStart   — TryAcquireCustomerLock; opens UI on customer client (Player mode).
/// 3. OnTick    — NPC: timer; Player: waits for _hasPlayerSelection.
/// 4. Commit    — atomic: validate funds, pull items from sell-shelves with
///                rollback on partial failure, deliver via PickUpItem cascade,
///                debit wallet, credit till, release lock.
/// 5. OnCancel  — refund/release if not yet committed; close UI on customer client.
///
/// Mirrors CharacterAction_FinishConstruction (2026-05-06 spec) for pattern.
/// </summary>
public class CharacterAction_BuyFromShop : CharacterAction_Continuous
{
    public enum BuyMode { NPC, Player }

    private readonly Cashier _cashier;
    private readonly List<ItemSO> _itemsToBuy;
    private readonly Dictionary<ItemSO, int> _quantities;
    private readonly BuyMode _mode;

    private float _elapsed;
    private bool _hasPlayerSelection;
    private bool _commitDone;

    private const float NPC_DURATION = 2f;
    private const float SENTINEL_TIMEOUT = 600f;

    public Cashier Cashier => _cashier;
    public BuyMode Mode => _mode;

    public CharacterAction_BuyFromShop(Character customer, Cashier cashier, List<ItemSO> itemsToBuy, BuyMode mode)
        : base(customer)
    {
        _cashier = cashier;
        _itemsToBuy = itemsToBuy ?? new List<ItemSO>();
        _quantities = new Dictionary<ItemSO, int>();
        _mode = mode;
        TickIntervalSeconds = 0.2f;

        if (mode == BuyMode.NPC)
        {
            for (int i = 0; i < _itemsToBuy.Count; i++)
            {
                var so = _itemsToBuy[i];
                if (so == null) continue;
                _quantities[so] = (_quantities.TryGetValue(so, out var v) ? v : 0) + 1;
            }
        }
    }

    public override bool CanExecute()
    {
        if (_cashier == null || _cashier.LinkedShop == null) return false;
        if (!_cashier.IsAvailableForCustomer) return false;
        return true;
    }

    public override void OnStart()
    {
        if (!_cashier.TryAcquireCustomerLock(character))
        {
            Debug.LogWarning($"[BuyFromShop] {character?.CharacterName} failed to acquire lock at OnStart — aborting.");
            Finish();
            return;
        }
        if (_mode == BuyMode.Player && _cashier.NetSync != null)
        {
            ulong customerId = character != null ? character.NetworkObjectId : 0UL;
            ulong cashierId = _cashier.NetSync.NetworkObjectId;
            ClientRpcParams p = new() { Send = new ClientRpcSendParams { TargetClientIds = new[] { character.OwnerClientId } } };
            _cashier.NetSync.OpenBuyPanelClientRpc(customerId, cashierId, p);
        }
    }

    public override bool OnTick()
    {
        _elapsed += TickIntervalSeconds;
        if (_elapsed > SENTINEL_TIMEOUT)
        {
            Debug.LogWarning("[BuyFromShop] sentinel timeout — aborting.");
            return true; // OnCancel will run via the Finish path; lock cleanup happens there.
        }

        if (_mode == BuyMode.NPC)
            return _elapsed >= NPC_DURATION && Commit();

        if (_mode == BuyMode.Player)
            return _hasPlayerSelection && Commit();

        return false;
    }

    /// <summary>
    /// Server-only — called by CashierNetSync.SubmitPlayerSelectionServerRpc when
    /// the player presses Confirm in the buy UI.
    /// </summary>
    internal void ApplyPlayerSelection(IReadOnlyList<(ItemSO item, int qty)> selections)
    {
        _itemsToBuy.Clear();
        _quantities.Clear();
        if (selections == null) { _hasPlayerSelection = true; return; }
        for (int i = 0; i < selections.Count; i++)
        {
            var (so, qty) = selections[i];
            if (so == null || qty <= 0) continue;
            _itemsToBuy.Add(so);
            _quantities[so] = qty;
        }
        _hasPlayerSelection = true;
    }

    public override void OnCancel()
    {
        if (!_commitDone) ReleaseLockOnly();
        if (_mode == BuyMode.Player && _cashier?.NetSync != null && character != null)
        {
            ClientRpcParams p = new() { Send = new ClientRpcSendParams { TargetClientIds = new[] { character.OwnerClientId } } };
            _cashier.NetSync.CloseBuyPanelClientRpc(character.NetworkObjectId, p);
        }
    }

    private void ReleaseLockOnly()
    {
        if (_cashier != null && character != null) _cashier.ReleaseCustomerLock(character);
    }

    // Commit + helpers filled in Task 20.
    private bool Commit() { return false; }
}
```

- [ ] **Step 2 — Refresh + compile**

Run `mcp__ai-game-developer__assets-refresh`. Expected: clean compile.

- [ ] **Step 3 — Commit**

```bash
git add Assets/Scripts/Character/CharacterActions/CharacterAction_BuyFromShop.cs
git commit -m "feat(shop-action): CharacterAction_BuyFromShop skeleton (NPC/Player modes, lifecycle)"
```

---

### Task 20: Implement `Commit` (atomic transaction with rollback)

**Files:**
- Modify: `Assets/Scripts/Character/CharacterActions/CharacterAction_BuyFromShop.cs`

- [ ] **Step 1 — Replace the empty `Commit()` stub**

Inside `CharacterAction_BuyFromShop`, replace `private bool Commit() { return false; }` with:

```csharp
private bool Commit()
{
    if (_commitDone) return true;
    var shop = _cashier.LinkedShop;
    if (shop == null) { Abort("shop missing"); return true; }

    // 1) Resolve total cost from the authoritative catalog.
    int totalCost = 0;
    foreach (var so in _quantities.Keys)
    {
        var entry = shop.GetCatalogEntry(so);
        if (entry == null) { Abort($"item {so?.ItemName ?? "?"} not in catalog"); return true; }
        totalCost += ShopBuilding.ResolvePrice(entry.Value) * _quantities[so];
    }

    // 2) Affordability gate (final server-side check; player UI also pre-gates).
    if (totalCost > 0 && !character.CharacterWallet.CanAfford(CurrencyId.Default, totalCost))
    {
        Abort("insufficient funds");
        return true;
    }

    // 3) Pull each ItemInstance from the sell-shelves; rollback on partial failure.
    var pulled = new List<(StorageFurniture shelf, ItemInstance instance)>();
    foreach (var pair in _quantities)
    {
        var so = pair.Key;
        int qty = pair.Value;
        for (int i = 0; i < qty; i++)
        {
            if (!TryPullFromAnyShelf(shop.SellShelves, so, out var shelf, out var instance))
            {
                RollbackPulls(pulled);
                AbortWithToast($"{so.ItemName} is no longer available — purchase cancelled.");
                return true;
            }
            pulled.Add((shelf, instance));
        }
    }

    // 4) Deliver each pulled ItemInstance to the customer.
    for (int i = 0; i < pulled.Count; i++)
        DeliverToCustomer(pulled[i].instance);

    // 5) Money: customer wallet → cashier till.
    if (totalCost > 0)
    {
        if (!character.CharacterWallet.RemoveCoins(CurrencyId.Default, totalCost, $"ShopPurchase_{shop.buildingName}"))
        {
            // Should be impossible after the affordability gate; defensive rollback.
            RollbackPulls(pulled);
            Abort("wallet debit failed");
            return true;
        }
        _cashier.CreditTill(CurrencyId.Default, totalCost, $"PurchaseBy_{character.CharacterName}");
    }

    _cashier.ReleaseCustomerLock(character);
    _commitDone = true;
    return true;
}

private static bool TryPullFromAnyShelf(IReadOnlyList<StorageFurniture> shelves, ItemSO target, out StorageFurniture pickedShelf, out ItemInstance pickedInstance)
{
    pickedShelf = null;
    pickedInstance = null;
    if (shelves == null) return false;

    for (int i = 0; i < shelves.Count; i++)
    {
        var shelf = shelves[i];
        if (shelf == null) continue;
        for (int s = 0; s < shelf.Capacity; s++)
        {
            var slot = shelf.GetItemSlot(s);
            if (slot == null || slot.IsEmpty()) continue;
            if (slot.ItemInstance.ItemSO == target)
            {
                pickedInstance = slot.ItemInstance;
                if (!shelf.RemoveItem(pickedInstance)) continue;
                pickedShelf = shelf;
                return true;
            }
        }
    }
    return false;
}

private static void RollbackPulls(List<(StorageFurniture shelf, ItemInstance instance)> pulled)
{
    for (int i = 0; i < pulled.Count; i++)
    {
        var (shelf, instance) = pulled[i];
        if (shelf == null || instance == null) continue;
        if (!shelf.AddItem(instance))
            Debug.LogError($"[BuyFromShop] Rollback failed: {instance.ItemSO.ItemName} could not be returned to {shelf.name}. Item lost.");
    }
}

private void DeliverToCustomer(ItemInstance instance)
{
    if (character.CharacterEquipment.PickUpItem(instance)) return;

    SpawnAsWorldItemNextToCharacter(instance);
    if (_mode == BuyMode.Player && _cashier?.NetSync != null)
    {
        ClientRpcParams p = new() { Send = new ClientRpcSendParams { TargetClientIds = new[] { character.OwnerClientId } } };
        _cashier.NetSync.ToastClientRpc(
            $"{instance.ItemSO.ItemName} dropped on the ground",
            MWI.UI.Notifications.ToastType.Info,
            p);
    }
}

private void SpawnAsWorldItemNextToCharacter(ItemInstance instance)
{
    // Reuse the existing physical-drop helper used by CharacterDropItem / DropItemFromHand.
    CharacterDropItem.ExecutePhysicalDrop(character, instance);
}

private void Abort(string reason)
{
    Debug.Log($"[BuyFromShop] Abort: {reason}");
    _cashier?.ReleaseCustomerLock(character);
    _commitDone = true; // suppress duplicate cleanup in OnCancel
}

private void AbortWithToast(string message)
{
    Debug.Log($"[BuyFromShop] Abort: {message}");
    if (_mode == BuyMode.Player && _cashier?.NetSync != null && character != null)
    {
        ClientRpcParams p = new() { Send = new ClientRpcSendParams { TargetClientIds = new[] { character.OwnerClientId } } };
        _cashier.NetSync.ToastClientRpc(message, MWI.UI.Notifications.ToastType.Warning, p);
    }
    _cashier?.ReleaseCustomerLock(character);
    _commitDone = true;
}
```

- [ ] **Step 2 — Refresh + compile**

Run `mcp__ai-game-developer__assets-refresh`. Expected: clean compile. If `CharacterDropItem.ExecutePhysicalDrop` is missing or has a different signature, adapt by calling whatever the project's existing item-drop API is — search `Assets/Scripts/Character/CharacterActions/CharacterDropItem.cs` for the correct entry point.

- [ ] **Step 3 — Commit**

```bash
git add Assets/Scripts/Character/CharacterActions/CharacterAction_BuyFromShop.cs
git commit -m "feat(shop-action): atomic Commit with rollback + delivery cascade"
```

---

### Task 21: Wire ServerRpcs on `CashierNetSync`

**Files:**
- Modify: `Assets/Scripts/World/Furniture/CashierNetSync.cs`

- [ ] **Step 1 — Track active action server-side**

Inside `CashierNetSync`, near other private fields, add:

```csharp
private CharacterAction_BuyFromShop _activeAction;
public CharacterAction_BuyFromShop ActiveAction => _activeAction;
public void SetActiveActionServer(CharacterAction_BuyFromShop action) { if (IsServer) _activeAction = action; }
```

- [ ] **Step 2 — Replace the three RPC stubs with real bodies**

Replace the existing stubs (the three `Debug.LogWarning` placeholders) with:

```csharp
[ServerRpc(RequireOwnership = false)]
public void RequestStartBuyServerRpc(NetworkBehaviourReference customerRef, ServerRpcParams p = default)
{
    if (!customerRef.TryGet(out Character customer)) return;
    if (customer.OwnerClientId != p.Receive.SenderClientId)
    {
        Debug.LogWarning($"[Cashier] RequestStartBuy: sender {p.Receive.SenderClientId} does not own customer {customer.NetworkObjectId}.");
        return;
    }
    if (!_cashier.IsAvailableForCustomer)
    {
        ClientRpcParams toCaller = new() { Send = new ClientRpcSendParams { TargetClientIds = new[] { p.Receive.SenderClientId } } };
        SendBusyToastClientRpc(toCaller);
        return;
    }

    var action = new CharacterAction_BuyFromShop(
        customer, _cashier, new System.Collections.Generic.List<ItemSO>(), CharacterAction_BuyFromShop.BuyMode.Player);
    SetActiveActionServer(action);
    customer.CharacterActions.EnqueueAction(action);
}

[ServerRpc(RequireOwnership = false)]
public void SubmitPlayerSelectionServerRpc(BuySelectionPayload payload, ServerRpcParams p = default)
{
    if (_activeAction == null) return;
    if (_activeAction.Mode != CharacterAction_BuyFromShop.BuyMode.Player) return;
    if (_cashier.CurrentCustomer == null || _cashier.CurrentCustomer.OwnerClientId != p.Receive.SenderClientId) return;

    var selections = new System.Collections.Generic.List<(ItemSO, int)>();
    int len = payload.ItemIds?.Length ?? 0;
    for (int i = 0; i < len; i++)
    {
        var so = ResolveItemSO(payload.ItemIds[i].ToString());
        if (so == null) continue;
        int qty = payload.Quantities[i];
        if (qty <= 0) continue;
        selections.Add((so, qty));
    }
    _activeAction.ApplyPlayerSelection(selections);
}

[ServerRpc(RequireOwnership = false)]
public void CancelPlayerTransactionServerRpc(ServerRpcParams p = default)
{
    if (_cashier.CurrentCustomer == null || _cashier.CurrentCustomer.OwnerClientId != p.Receive.SenderClientId) return;
    _cashier.CurrentCustomer.CharacterActions?.CancelCurrentAction();
    _activeAction = null;
}

private static ItemSO ResolveItemSO(string itemId)
{
    if (string.IsNullOrEmpty(itemId)) return null;
    var all = Resources.LoadAll<ItemSO>("Data/Item");
    return System.Array.Find(all, x => x.ItemId == itemId);
}
```

- [ ] **Step 3 — Refresh + compile**

Run `mcp__ai-game-developer__assets-refresh`. Expected: clean compile.

- [ ] **Step 4 — Commit**

```bash
git add Assets/Scripts/World/Furniture/CashierNetSync.cs
git commit -m "feat(cashier): customer-flow ServerRpcs (start-buy / submit-selection / cancel)"
```

---

## Wave 7 — `GoapAction_GoShopping` refactor

### Task 22: Refactor `GoapAction_GoShopping`

**Files:**
- Modify: `Assets/Scripts/AI/GOAP/Actions/GoapAction_GoShopping.cs`

- [ ] **Step 1 — Replace class body with the refactored version**

Path: `Assets/Scripts/AI/GOAP/Actions/GoapAction_GoShopping.cs`

```csharp
using System.Collections.Generic;
using System.Linq;
using MWI.Economy;
using UnityEngine;

public class GoapAction_GoShopping : GoapAction
{
    public override string ActionName => "GoShopping";

    public override Dictionary<string, bool> Preconditions => new();
    public override Dictionary<string, bool> Effects => new() { { "shoppingDone", true } };
    public override float Cost => 2f;

    private readonly ItemSO _desiredItem;
    private bool _isComplete;
    private bool _isMoving;
    private bool _actionEnqueued;
    private bool _actionFinished;
    private ShopBuilding _chosenShop;
    private Cashier _chosenCashier;
    private CharacterAction_BuyFromShop _enqueuedAction;

    public override bool IsComplete => _isComplete;

    public GoapAction_GoShopping(ItemSO desiredItem) { _desiredItem = desiredItem; }

    public override bool IsValid(Character worker)
    {
        if (_isComplete) return false;
        if (_chosenShop != null && _chosenCashier != null)
            return _chosenCashier.IsAvailableForCustomer || _actionEnqueued;

        var shop = FindShopWithItem(_desiredItem);
        if (shop == null) return false;

        var entry = shop.GetCatalogEntry(_desiredItem);
        if (!entry.HasValue) return false;

        int price = ShopBuilding.ResolvePrice(entry.Value);
        if (price > 0 && !worker.CharacterWallet.CanAfford(CurrencyId.Default, price)) return false;

        bool hasBagSpace = worker.CharacterEquipment != null && worker.CharacterEquipment.HasFreeSpaceForItemSO(_desiredItem);
        bool handsFree = worker.CharacterVisual?.BodyPartsController?.HandsController?.AreHandsFree() == true;
        if (!hasBagSpace && !handsFree) return false;

        var cashier = shop.GetFirstAvailableCashier();
        if (cashier == null) return false;

        _chosenShop = shop;
        _chosenCashier = cashier;
        return true;
    }

    public override void Execute(Character worker)
    {
        if (_isComplete) return;
        if (_chosenShop == null || _chosenCashier == null) { _isComplete = true; return; }

        var movement = worker.CharacterMovement;
        if (movement == null) { _isComplete = true; return; }

        var dest = _chosenCashier.GetInteractionPosition(worker.transform.position);
        if (Vector3.Distance(worker.transform.position, dest) > 1.5f)
        {
            if (!_isMoving) { movement.SetDestination(dest); _isMoving = true; }
            return;
        }
        if (_isMoving) { movement.Stop(); _isMoving = false; }

        if (!_actionEnqueued)
        {
            if (!_chosenCashier.IsAvailableForCustomer) { _isComplete = true; return; }

            _enqueuedAction = new CharacterAction_BuyFromShop(
                worker, _chosenCashier, new List<ItemSO> { _desiredItem }, CharacterAction_BuyFromShop.BuyMode.NPC);
            _enqueuedAction.OnActionFinished += () => _actionFinished = true;
            worker.CharacterActions.EnqueueAction(_enqueuedAction);
            _actionEnqueued = true;
        }

        if (_actionFinished) _isComplete = true;
    }

    public override void Exit(Character worker)
    {
        _isComplete = false;
        _isMoving = false;
        _actionEnqueued = false;
        _actionFinished = false;
        _chosenShop = null;
        _chosenCashier = null;
        _enqueuedAction = null;
        worker.CharacterMovement?.Stop();
    }

    private static ShopBuilding FindShopWithItem(ItemSO item)
    {
        if (BuildingManager.Instance == null) return null;
        return BuildingManager.Instance.allBuildings
            .OfType<ShopBuilding>()
            .FirstOrDefault(s =>
            {
                var entry = s.GetCatalogEntry(item);
                if (!entry.HasValue) return false;
                if (s.GetFirstAvailableCashier() == null) return false;

                // At least one matching instance must be on a sell-shelf.
                for (int i = 0; i < s.SellShelves.Count; i++)
                {
                    var shelf = s.SellShelves[i];
                    if (shelf == null) continue;
                    for (int sl = 0; sl < shelf.Capacity; sl++)
                    {
                        var slot = shelf.GetItemSlot(sl);
                        if (slot != null && !slot.IsEmpty() && slot.ItemInstance.ItemSO == item) return true;
                    }
                }
                return false;
            });
    }
}
```

- [ ] **Step 2 — Refresh + compile**

Run `mcp__ai-game-developer__assets-refresh`. Expected: clean compile. The legacy break from Task 15 (`_shop.JoinQueue` reference) is now gone.

- [ ] **Step 3 — Commit**

```bash
git add Assets/Scripts/AI/GOAP/Actions/GoapAction_GoShopping.cs
git commit -m "refactor(goap): GoShopping uses CharacterAction_BuyFromShop (NPC mode)"
```

---

## Wave 8 — Player UI

### Task 23: Create `UI_ShopBuyPanel`

**Files:**
- Create: `Assets/Scripts/UI/Shop/UI_ShopBuyPanel.cs`
- Create: `Assets/Resources/UI/UI_ShopBuyPanel.prefab` (designer task — placeholder vertical layout with header + item rows + footer for first iteration)

- [ ] **Step 1 — Create the panel script**

Path: `Assets/Scripts/UI/Shop/UI_ShopBuyPanel.cs`

```csharp
using System.Collections.Generic;
using MWI.Economy;
using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace MWI.UI.Shop
{
    /// <summary>
    /// Player-facing shop buy panel. Opens via OpenBuyPanelClientRpc; reads
    /// authoritative state from ShopBuildingNetSync (catalog) + per-shelf
    /// StorageFurnitureNetworkSync (stock) + CharacterWallet. Reactive — refreshes
    /// on any of those events.
    ///
    /// All mutations server-authoritative through Cashier.NetSync ServerRpcs.
    /// </summary>
    public class UI_ShopBuyPanel : MonoBehaviour
    {
        [Header("Wiring")]
        [SerializeField] private TMP_Text _titleText;
        [SerializeField] private TMP_Text _walletText;
        [SerializeField] private TMP_Text _totalText;
        [SerializeField] private Button _confirmButton;
        [SerializeField] private Button _cancelButton;
        [SerializeField] private Transform _rowsParent;
        [SerializeField] private GameObject _rowPrefab;   // prefab carrying a UI_ShopBuyRow component

        private static UI_ShopBuyPanel _instance;
        private Cashier _cashier;
        private Character _customer;
        private ShopBuilding _shop;
        private readonly Dictionary<ItemSO, int> _quantities = new();
        private readonly List<UI_ShopBuyRow> _rows = new();

        public static void Open(Cashier cashier, Character customer)
        {
            if (_instance == null)
            {
                var prefab = Resources.Load<GameObject>("UI/UI_ShopBuyPanel");
                if (prefab == null) { Debug.LogError("[UI_ShopBuyPanel] prefab not found at Resources/UI/UI_ShopBuyPanel"); return; }
                var go = Instantiate(prefab);
                _instance = go.GetComponent<UI_ShopBuyPanel>();
            }
            _instance.Bind(cashier, customer);
            _instance.gameObject.SetActive(true);
        }

        public static void Close()
        {
            if (_instance == null) return;
            _instance.Unbind();
            _instance.gameObject.SetActive(false);
        }

        private void Bind(Cashier cashier, Character customer)
        {
            _cashier = cashier;
            _customer = customer;
            _shop = cashier.LinkedShop;
            if (_shop == null) { Debug.LogError("[UI_ShopBuyPanel] cashier has no LinkedShop"); Close(); return; }

            _titleText.text = $"Shop: {_shop.buildingName}";
            _confirmButton.onClick.AddListener(OnConfirmClicked);
            _cancelButton.onClick.AddListener(OnCancelClicked);

            SubscribeAll();
            RebuildRows();
            Refresh();
        }

        private void Unbind()
        {
            _confirmButton.onClick.RemoveListener(OnConfirmClicked);
            _cancelButton.onClick.RemoveListener(OnCancelClicked);
            UnsubscribeAll();

            for (int i = 0; i < _rows.Count; i++) Destroy(_rows[i].gameObject);
            _rows.Clear();
            _quantities.Clear();
            _cashier = null; _customer = null; _shop = null;
        }

        private void SubscribeAll()
        {
            if (_shop != null) _shop.OnCatalogChanged += Refresh;
            for (int i = 0; i < _shop.SellShelves.Count; i++)
            {
                var shelf = _shop.SellShelves[i];
                if (shelf != null) shelf.OnInventoryChanged += Refresh;
            }
            if (_cashier?.NetSync != null)
                _cashier.NetSync.CurrentCustomerNetworkObjectId.OnValueChanged += OnLockChanged;
            if (_customer?.CharacterWallet != null)
                _customer.CharacterWallet.OnBalanceChanged += OnWalletChanged;
        }

        private void UnsubscribeAll()
        {
            if (_shop != null) _shop.OnCatalogChanged -= Refresh;
            if (_shop != null)
                for (int i = 0; i < _shop.SellShelves.Count; i++)
                {
                    var shelf = _shop.SellShelves[i];
                    if (shelf != null) shelf.OnInventoryChanged -= Refresh;
                }
            if (_cashier?.NetSync != null)
                _cashier.NetSync.CurrentCustomerNetworkObjectId.OnValueChanged -= OnLockChanged;
            if (_customer?.CharacterWallet != null)
                _customer.CharacterWallet.OnBalanceChanged -= OnWalletChanged;
        }

        private void RebuildRows()
        {
            for (int i = 0; i < _rows.Count; i++) Destroy(_rows[i].gameObject);
            _rows.Clear();
            for (int i = 0; i < _shop.Catalog.Count; i++)
            {
                var entry = _shop.Catalog[i];
                if (entry.Item == null) continue;
                var rowGo = Instantiate(_rowPrefab, _rowsParent);
                var row = rowGo.GetComponent<UI_ShopBuyRow>();
                row.Bind(entry, OnRowQuantityChanged);
                _rows.Add(row);
            }
        }

        private void OnRowQuantityChanged(ItemSO item, int qty)
        {
            if (qty <= 0) _quantities.Remove(item);
            else _quantities[item] = qty;
            Refresh();
        }

        private void OnWalletChanged(CurrencyId currency, int oldValue, int newValue) => Refresh();
        private void OnLockChanged(ulong previous, ulong current)
        {
            if (current == 0 && _customer != null)
            {
                MWI.UI.Notifications.UI_Toast.Show("Vendor left — purchase cancelled.", MWI.UI.Notifications.ToastType.Warning);
                Close();
            }
        }

        private void Refresh()
        {
            if (_shop == null || _customer == null) return;

            int total = 0;
            for (int i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                int stock = AggregateStockAcrossShelves(row.Item);
                row.SetStock(stock);
                row.ClampQuantity(0, stock);
                int qty = row.CurrentQuantity;
                if (qty > 0) _quantities[row.Item] = qty; else _quantities.Remove(row.Item);

                var entry = _shop.GetCatalogEntry(row.Item);
                if (entry.HasValue) total += ShopBuilding.ResolvePrice(entry.Value) * qty;
            }

            int wallet = _customer.CharacterWallet.GetBalance(CurrencyId.Default);
            _walletText.text = $"Wallet: {wallet} g";
            _totalText.text = $"Total: {total} g";
            _confirmButton.interactable = total <= wallet && _quantities.Count > 0;
        }

        private int AggregateStockAcrossShelves(ItemSO item)
        {
            int count = 0;
            for (int s = 0; s < _shop.SellShelves.Count; s++)
            {
                var shelf = _shop.SellShelves[s];
                if (shelf == null) continue;
                for (int sl = 0; sl < shelf.Capacity; sl++)
                {
                    var slot = shelf.GetItemSlot(sl);
                    if (slot != null && !slot.IsEmpty() && slot.ItemInstance.ItemSO == item) count++;
                }
            }
            return count;
        }

        private void OnConfirmClicked()
        {
            if (_cashier?.NetSync == null) return;
            var ids = new FixedString64Bytes[_quantities.Count];
            var qtys = new int[_quantities.Count];
            int i = 0;
            foreach (var kv in _quantities)
            {
                ids[i] = new FixedString64Bytes(kv.Key.ItemId);
                qtys[i] = kv.Value;
                i++;
            }
            var payload = new BuySelectionPayload { ItemIds = ids, Quantities = qtys };
            _cashier.NetSync.SubmitPlayerSelectionServerRpc(payload);
        }

        private void OnCancelClicked()
        {
            if (_cashier?.NetSync == null) { Close(); return; }
            _cashier.NetSync.CancelPlayerTransactionServerRpc();
        }

        private void OnDestroy()
        {
            UnsubscribeAll();
            if (_instance == this) _instance = null;
        }
    }
}
```

- [ ] **Step 2 — Create the row component**

Path: `Assets/Scripts/UI/Shop/UI_ShopBuyRow.cs`

```csharp
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MWI.UI.Shop
{
    public class UI_ShopBuyRow : MonoBehaviour
    {
        [SerializeField] private Image _icon;
        [SerializeField] private TMP_Text _nameText;
        [SerializeField] private TMP_Text _priceText;
        [SerializeField] private TMP_Text _stockText;
        [SerializeField] private TMP_Text _subtotalText;
        [SerializeField] private TMP_InputField _quantityInput;
        [SerializeField] private Button _plusButton;
        [SerializeField] private Button _minusButton;

        public ItemSO Item { get; private set; }
        public int CurrentQuantity { get; private set; }
        private int _stock;
        private int _price;
        private Action<ItemSO, int> _onQuantityChanged;

        public void Bind(ShopItemEntry entry, Action<ItemSO, int> onQuantityChanged)
        {
            Item = entry.Item;
            _price = ShopBuilding.ResolvePrice(entry);
            _onQuantityChanged = onQuantityChanged;

            _icon.sprite = Item.Icon;
            _nameText.text = Item.ItemName;
            _priceText.text = $"{_price} g";

            _quantityInput.text = "0";
            _quantityInput.onEndEdit.AddListener(OnQuantityInputChanged);
            _plusButton.onClick.AddListener(OnPlus);
            _minusButton.onClick.AddListener(OnMinus);
            UpdateSubtotal();
        }

        public void SetStock(int stock)
        {
            _stock = stock;
            _stockText.text = $"{stock} in stock";
        }

        public void ClampQuantity(int min, int max)
        {
            int q = CurrentQuantity;
            if (q < min) q = min;
            if (q > max) q = max;
            if (q != CurrentQuantity) SetQuantity(q, fireCallback: false);
        }

        private void SetQuantity(int q, bool fireCallback)
        {
            CurrentQuantity = q;
            _quantityInput.SetTextWithoutNotify(q.ToString());
            UpdateSubtotal();
            if (fireCallback) _onQuantityChanged?.Invoke(Item, q);
        }

        private void UpdateSubtotal() => _subtotalText.text = $"= {_price * CurrentQuantity} g";

        private void OnQuantityInputChanged(string raw)
        {
            if (!int.TryParse(raw, out int q)) q = 0;
            if (q < 0) q = 0;
            if (q > _stock) q = _stock;
            SetQuantity(q, fireCallback: true);
        }

        private void OnPlus()
        {
            if (CurrentQuantity < _stock) SetQuantity(CurrentQuantity + 1, fireCallback: true);
        }

        private void OnMinus()
        {
            if (CurrentQuantity > 0) SetQuantity(CurrentQuantity - 1, fireCallback: true);
        }

        private void OnDestroy()
        {
            _quantityInput.onEndEdit.RemoveListener(OnQuantityInputChanged);
            _plusButton.onClick.RemoveListener(OnPlus);
            _minusButton.onClick.RemoveListener(OnMinus);
        }
    }
}
```

- [ ] **Step 3 — Wire `OpenBuyPanelClientRpc` / `CloseBuyPanelClientRpc`**

In `Assets/Scripts/World/Furniture/CashierNetSync.cs`, replace the empty bodies of `OpenBuyPanelClientRpc` and `CloseBuyPanelClientRpc` with:

```csharp
[ClientRpc]
public void OpenBuyPanelClientRpc(ulong customerNetworkObjectId, ulong cashierNetworkObjectId, ClientRpcParams p = default)
{
    if (NetworkManager.Singleton == null) return;
    if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(customerNetworkObjectId, out var customerObj)) return;
    if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(cashierNetworkObjectId, out var cashierObj)) return;
    var customer = customerObj.GetComponent<Character>();
    var cashier = cashierObj.GetComponent<Cashier>();
    if (customer == null || cashier == null) return;
    if (!customer.IsOwner) return;     // only the owning client opens the UI
    MWI.UI.Shop.UI_ShopBuyPanel.Open(cashier, customer);
}

[ClientRpc]
public void CloseBuyPanelClientRpc(ulong customerNetworkObjectId, ClientRpcParams p = default)
{
    MWI.UI.Shop.UI_ShopBuyPanel.Close();
}
```

- [ ] **Step 4 — Designer task: build the prefab**

Use Unity Editor to create `UI_ShopBuyPanel` prefab at `Assets/Resources/UI/UI_ShopBuyPanel.prefab`. Hierarchy:

```
UI_ShopBuyPanel (Canvas + UI_ShopBuyPanel script)
  Header (TMP "Shop: ...") + close button
  RowsScrollView (ScrollRect + Vertical Layout Group)
    Content (rowsParent)
  Footer
    Wallet label + Total label + Confirm button + Cancel button
```

Row prefab at `Assets/Resources/UI/UI_ShopBuyRow.prefab` carries the `UI_ShopBuyRow` script with serialized fields wired.

- [ ] **Step 5 — Refresh + compile + smoke test**

Run `mcp__ai-game-developer__assets-refresh`. Verify zero compile errors.
PlayMode smoke test: spawn a shop, place a cashier with a hired vendor, hand a player some catalog items and gold, walk to the cashier, tap E. Expected: panel opens, items list, Confirm fires the ServerRpc, items + money transfer.

- [ ] **Step 6 — Commit**

```bash
git add Assets/Scripts/UI/Shop/ Assets/Scripts/World/Furniture/CashierNetSync.cs Assets/Resources/UI/
git commit -m "feat(shop-ui): UI_ShopBuyPanel + UI_ShopBuyRow + OpenBuyPanelClientRpc wiring"
```

---

## Wave 9 — Save / load

### Task 24: Create `CashierSaveData` + load wiring

**Files:**
- Create: `Assets/Scripts/World/Furniture/CashierSaveData.cs`
- Modify: `Assets/Scripts/World/Furniture/Cashier.cs`

- [ ] **Step 1 — Create the save shape**

Path: `Assets/Scripts/World/Furniture/CashierSaveData.cs`

```csharp
using System.Collections.Generic;

[System.Serializable]
public class CashierSaveData
{
    public List<CurrencyBalanceEntry> till = new();
    public string linkedBuildingId;
    public bool requiresVendor;
}
```

- [ ] **Step 2 — Add Serialize / Deserialize on `Cashier`**

Inside `Cashier`, append:

```csharp
public CashierSaveData Serialize()
{
    var data = new CashierSaveData { requiresVendor = _requiresVendor };
    if (_linkedBuilding is Building b) data.linkedBuildingId = b.BuildingId.Value.ToString();
    foreach (var kv in _till)
        data.till.Add(new CurrencyBalanceEntry { currencyId = kv.Key.Id, amount = kv.Value });
    return data;
}

public void RestoreFromSaveData(CashierSaveData data)
{
    if (data == null) return;
    _till.Clear();
    foreach (var e in data.till)
        if (e.amount > 0) _till[new CurrencyId(e.currencyId)] = e.amount;
    if (_netSync != null && _netSync.IsServer)
        foreach (var kv in _till) _netSync.SetTillBalanceServer(kv.Key, kv.Value);
}
```

- [ ] **Step 3 — Hook into the existing furniture-save mechanism**

In whichever class drives furniture-side save/restore (search for `StorageFurniture.RestoreFromSaveData` callers — likely `MapController.SpawnSavedBuildings` or similar), add a parallel path that finds `Cashier` components and calls `Serialize` / `RestoreFromSaveData`. Document the exact entry point for the executor by searching for `.RestoreFromSaveData(` calls.

If no existing furniture-save dispatcher exists for non-StorageFurniture types, add a minimal `IFurnitureSaveable` interface that both can implement, and dispatch through it.

- [ ] **Step 4 — Refresh + compile**

Run `mcp__ai-game-developer__assets-refresh`. Expected: clean compile.

- [ ] **Step 5 — Commit**

```bash
git add Assets/Scripts/World/Furniture/CashierSaveData.cs Assets/Scripts/World/Furniture/Cashier.cs
git commit -m "feat(cashier): CashierSaveData + serialize/restore (till + linkage)"
```

---

### Task 25: Create `ShopBuildingSaveData` + load wiring

**Files:**
- Create: `Assets/Scripts/World/Buildings/CommercialBuildings/ShopBuildingSaveData.cs`
- Modify: `Assets/Scripts/World/Buildings/CommercialBuildings/ShopBuilding.cs`

- [ ] **Step 1 — Create the save shape**

Path: `Assets/Scripts/World/Buildings/CommercialBuildings/ShopBuildingSaveData.cs`

```csharp
using System.Collections.Generic;

[System.Serializable]
public class ShopBuildingSaveData : BuildingSaveData
{
    public List<CatalogEntrySaveEntry> catalog = new();
    public List<string> sellShelfFurnitureIds = new();
}

[System.Serializable]
public struct CatalogEntrySaveEntry
{
    public string itemId;
    public int maxStock;
    public int priceOverride;
}
```

(If `BuildingSaveData` is not the correct base class for the project's existing building-save pattern, replace with the actual base. Search `BuildingSaveData.cs` to confirm.)

- [ ] **Step 2 — Override save on `ShopBuilding`**

Inside `ShopBuilding`, override the appropriate save methods (find by searching `ShopBuilding`'s base class for `Serialize` / `ToSaveData`):

```csharp
public override BuildingSaveData ToSaveData()
{
    var data = new ShopBuildingSaveData();
    base.PopulateBaseSaveData(data); // or whatever the base helper is — adapt to project convention

    foreach (var entry in _catalog)
    {
        if (entry.Item == null) continue;
        data.catalog.Add(new CatalogEntrySaveEntry
        {
            itemId = entry.Item.ItemId,
            maxStock = entry.MaxStock,
            priceOverride = entry.PriceOverride
        });
    }
    foreach (var shelf in _sellShelves)
    {
        if (shelf == null) continue;
        var furn = shelf.GetComponent<UnityEngine.MonoBehaviour>(); // find a furniture-id source consistent with project
        // Persist shelf ID — adjust to match how the project IDs furniture instances.
        data.sellShelfFurnitureIds.Add(shelf.GetInstanceID().ToString()); // PLACEHOLDER — replace with project's furniture-instance ID
    }
    return data;
}

public override void RestoreFromSaveData(BuildingSaveData rawData)
{
    base.RestoreFromSaveData(rawData);
    if (rawData is not ShopBuildingSaveData data) return;

    _catalog.Clear();
    foreach (var e in data.catalog)
    {
        var so = ResolveItemSO(e.itemId);
        if (so == null) continue;
        _catalog.Add(new ShopItemEntry { Item = so, MaxStock = e.maxStock, PriceOverride = e.priceOverride });
    }
    OnCatalogChanged?.Invoke();

    // Sell-shelves are resolved AFTER cashiers + storages register — defer.
    _pendingSellShelfIds = new List<string>(data.sellShelfFurnitureIds);
}

private List<string> _pendingSellShelfIds;

/// <summary>
/// Hook called by MapController after default-furniture spawn finishes.
/// Resolves the saved sell-shelf IDs against the now-spawned StorageFurniture instances.
/// </summary>
public void OnFurnituresLoaded()
{
    if (_pendingSellShelfIds == null) return;
    var allStorages = GetComponentsInChildren<StorageFurniture>();
    foreach (var savedId in _pendingSellShelfIds)
    {
        foreach (var storage in allStorages)
        {
            // Match against project's furniture-instance ID convention.
            if (storage.GetInstanceID().ToString() == savedId)  // PLACEHOLDER — replace
            {
                if (!_sellShelves.Contains(storage)) _sellShelves.Add(storage);
                break;
            }
        }
    }
    _pendingSellShelfIds = null;
    OnSellShelvesChanged?.Invoke();
}
```

(Note: the `GetInstanceID().ToString()` is a PLACEHOLDER. Search for how `StorageFurniture` instances are uniquely identified in saves — likely a `furnitureId : string` on `Furniture` or via the parent `Building.BuildingId` + slot index. Update the two PLACEHOLDER lines accordingly.)

- [ ] **Step 3 — Refresh + compile**

Run `mcp__ai-game-developer__assets-refresh`. Expected: clean compile after the placeholder substitution.

- [ ] **Step 4 — Manual save/load smoke**

In Unity Editor: place a shop with a cashier and a sell-shelf, set a catalog entry, save the world, exit PlayMode, reload the save. Expected: catalog persists, sell-shelf flag persists, till (if non-zero) persists.

- [ ] **Step 5 — Commit**

```bash
git add Assets/Scripts/World/Buildings/CommercialBuildings/ShopBuildingSaveData.cs Assets/Scripts/World/Buildings/CommercialBuildings/ShopBuilding.cs
git commit -m "feat(shop): ShopBuildingSaveData + deferred sell-shelf resolution on load"
```

---

## Wave 10 — Management tabs (depends on parallel session)

> **Stop point.** This wave depends on
> `docs/superpowers/briefs/2026-05-07-commercial-building-management-panel-architecture-brief.md`
> having merged. If `IManagementTab` / `CommercialBuilding.GetManagementTabs()` /
> `UI_OwnerManagementPanel` do not exist on the branch, halt here. Resume with
> Task 26 once the parallel session has merged.

### Task 26: `ShopCatalogTab`

**Files:**
- Create: `Assets/Scripts/UI/Management/Tabs/ShopCatalogTab.cs`
- Create: `Assets/Scripts/UI/Management/Tabs/ShopCatalogTabView.cs`
- Designer: `Assets/Resources/UI/Management/Tabs/ShopCatalogTabView.prefab`

- [ ] **Step 1 — Create `ShopCatalogTab`**

```csharp
namespace MWI.UI.Management.Tabs
{
    public class ShopCatalogTab : IManagementTab
    {
        private readonly ShopBuilding _shop;
        public ShopCatalogTab(ShopBuilding shop) { _shop = shop; }
        public string Name => "Catalog";
        public IManagementTabView CreateView() => ShopCatalogTabView.Spawn(_shop);
    }
}
```

- [ ] **Step 2 — Create `ShopCatalogTabView`**

```csharp
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MWI.UI.Management.Tabs
{
    public class ShopCatalogTabView : MonoBehaviour, IManagementTabView
    {
        [SerializeField] private Transform _rowsParent;
        [SerializeField] private GameObject _rowPrefab;
        [SerializeField] private Button _addButton;

        private ShopBuilding _shop;
        private readonly List<ShopCatalogTabRow> _rows = new();

        public GameObject Root => gameObject;

        public static ShopCatalogTabView Spawn(ShopBuilding shop)
        {
            var prefab = Resources.Load<GameObject>("UI/Management/Tabs/ShopCatalogTabView");
            var go = Instantiate(prefab);
            var view = go.GetComponent<ShopCatalogTabView>();
            view._shop = shop;
            return view;
        }

        public void OnTabActivated()
        {
            _shop.OnCatalogChanged += Refresh;
            _addButton.onClick.AddListener(OnAddClicked);
            Refresh();
        }

        public void OnTabDeactivated()
        {
            _shop.OnCatalogChanged -= Refresh;
            _addButton.onClick.RemoveListener(OnAddClicked);
        }

        public void Dispose() { OnTabDeactivated(); Destroy(gameObject); }

        private void Refresh()
        {
            for (int i = 0; i < _rows.Count; i++) Destroy(_rows[i].gameObject);
            _rows.Clear();
            foreach (var entry in _shop.Catalog)
            {
                if (entry.Item == null) continue;
                var rowGo = Instantiate(_rowPrefab, _rowsParent);
                var row = rowGo.GetComponent<ShopCatalogTabRow>();
                row.Bind(_shop, entry);
                _rows.Add(row);
            }
        }

        private void OnAddClicked() => CatalogItemPickerDialog.Show(_shop, OnItemPicked);
        private void OnItemPicked(ItemSO item, int maxStock, int priceOverride)
        {
            _shop.AddCatalogEntryServerRpc(item.ItemId, maxStock, priceOverride);
        }
    }

    public class ShopCatalogTabRow : MonoBehaviour
    {
        [SerializeField] private Image _icon;
        [SerializeField] private TMP_Text _nameText;
        [SerializeField] private TMP_InputField _maxStockInput;
        [SerializeField] private TMP_InputField _priceInput;
        [SerializeField] private TMP_Text _priceHelperText;
        [SerializeField] private Button _editButton;
        [SerializeField] private Button _removeButton;

        private ShopBuilding _shop;
        private ShopItemEntry _entry;

        public void Bind(ShopBuilding shop, ShopItemEntry entry)
        {
            _shop = shop; _entry = entry;
            _icon.sprite = entry.Item.Icon;
            _nameText.text = entry.Item.ItemName;
            _maxStockInput.SetTextWithoutNotify(entry.MaxStock.ToString());
            _priceInput.SetTextWithoutNotify(entry.PriceOverride.ToString());
            _priceHelperText.text = entry.Item.BasePrice > 0
                ? $"0 = use base price ({entry.Item.BasePrice} g)"
                : "0 = item has no base price";

            _editButton.onClick.AddListener(OnEditClicked);
            _removeButton.onClick.AddListener(OnRemoveClicked);
        }

        private void OnEditClicked()
        {
            int.TryParse(_maxStockInput.text, out int maxStock);
            int.TryParse(_priceInput.text, out int price);
            _shop.EditCatalogEntryServerRpc(_entry.Item.ItemId, maxStock, price);
        }

        private void OnRemoveClicked() => _shop.RemoveCatalogEntryServerRpc(_entry.Item.ItemId);

        private void OnDestroy()
        {
            if (_editButton != null) _editButton.onClick.RemoveListener(OnEditClicked);
            if (_removeButton != null) _removeButton.onClick.RemoveListener(OnRemoveClicked);
        }
    }
}
```

- [ ] **Step 3 — Designer creates the prefab**

`Assets/Resources/UI/Management/Tabs/ShopCatalogTabView.prefab` — vertical layout container with an "+ Add" button and a Content transform for rows. Row prefab at `Assets/Resources/UI/Management/Tabs/ShopCatalogTabRow.prefab`.

- [ ] **Step 4 — Refresh + compile + commit**

```bash
git add Assets/Scripts/UI/Management/Tabs/ShopCatalogTab.cs Assets/Scripts/UI/Management/Tabs/ShopCatalogTabView.cs Assets/Resources/UI/Management/Tabs/
git commit -m "feat(mgmt-ui): ShopCatalogTab + view + row component"
```

---

### Task 27: `ShopShelvesTab`

**Files:**
- Create: `Assets/Scripts/UI/Management/Tabs/ShopShelvesTab.cs`
- Create: `Assets/Scripts/UI/Management/Tabs/ShopShelvesTabView.cs`
- Designer: prefab + row prefab

- [ ] **Step 1 — Create the tab + view**

```csharp
namespace MWI.UI.Management.Tabs
{
    public class ShopShelvesTab : IManagementTab
    {
        private readonly ShopBuilding _shop;
        public ShopShelvesTab(ShopBuilding shop) { _shop = shop; }
        public string Name => "Shelves";
        public IManagementTabView CreateView() => ShopShelvesTabView.Spawn(_shop);
    }

    public class ShopShelvesTabView : MonoBehaviour, IManagementTabView
    {
        [SerializeField] private Transform _rowsParent;
        [SerializeField] private GameObject _rowPrefab;

        private ShopBuilding _shop;
        private readonly List<ShopShelvesTabRow> _rows = new();
        public GameObject Root => gameObject;

        public static ShopShelvesTabView Spawn(ShopBuilding shop)
        {
            var prefab = Resources.Load<GameObject>("UI/Management/Tabs/ShopShelvesTabView");
            var go = Instantiate(prefab);
            var view = go.GetComponent<ShopShelvesTabView>();
            view._shop = shop;
            return view;
        }

        public void OnTabActivated()
        {
            _shop.OnSellShelvesChanged += Refresh;
            // Also refresh when shop's furniture set changes (storages added/removed).
            // Hook into FurnitureManager.OnFurnitureChanged or Building.OnFurnitureChanged — adapt to project.
            Refresh();
        }

        public void OnTabDeactivated() => _shop.OnSellShelvesChanged -= Refresh;
        public void Dispose() { OnTabDeactivated(); Destroy(gameObject); }

        private void Refresh()
        {
            for (int i = 0; i < _rows.Count; i++) Destroy(_rows[i].gameObject);
            _rows.Clear();

            var allStorages = _shop.GetComponentsInChildren<StorageFurniture>();
            foreach (var storage in allStorages)
            {
                bool isSellShelf = false;
                for (int i = 0; i < _shop.SellShelves.Count; i++)
                    if (_shop.SellShelves[i] == storage) { isSellShelf = true; break; }
                int used = 0;
                for (int s = 0; s < storage.Capacity; s++)
                    if (!storage.GetItemSlot(s).IsEmpty()) used++;

                var rowGo = Instantiate(_rowPrefab, _rowsParent);
                var row = rowGo.GetComponent<ShopShelvesTabRow>();
                row.Bind(_shop, storage, isSellShelf, used);
                _rows.Add(row);
            }
        }
    }

    public class ShopShelvesTabRow : MonoBehaviour
    {
        [SerializeField] private Toggle _toggle;
        [SerializeField] private TMP_Text _label;
        private ShopBuilding _shop;
        private StorageFurniture _storage;

        public void Bind(ShopBuilding shop, StorageFurniture storage, bool isSellShelf, int used)
        {
            _shop = shop; _storage = storage;
            _label.text = $"{storage.FurnitureName}    {used}/{storage.Capacity} slots used";
            _toggle.SetIsOnWithoutNotify(isSellShelf);
            _toggle.onValueChanged.AddListener(OnToggle);
        }
        private void OnToggle(bool isOn)
        {
            var net = _storage.GetComponent<Unity.Netcode.NetworkObject>();
            if (net == null) return;
            _shop.SetSellShelfFlagServerRpc(new Unity.Netcode.NetworkObjectReference(net), isOn);
        }
        private void OnDestroy() { if (_toggle != null) _toggle.onValueChanged.RemoveListener(OnToggle); }
    }
}
```

- [ ] **Step 2 — Designer prefab + commit**

Build prefabs. Commit:

```bash
git add Assets/Scripts/UI/Management/Tabs/ShopShelvesTab*.cs Assets/Resources/UI/Management/Tabs/ShopShelvesTab*
git commit -m "feat(mgmt-ui): ShopShelvesTab (toggle storage as sell-shelf)"
```

---

### Task 28: `ShopCashiersTab`

**Files:**
- Create: `Assets/Scripts/UI/Management/Tabs/ShopCashiersTab.cs`
- Create: `Assets/Scripts/UI/Management/Tabs/ShopCashiersTabView.cs`
- Designer: prefab + row prefab

- [ ] **Step 1 — Create tab + view**

```csharp
namespace MWI.UI.Management.Tabs
{
    public class ShopCashiersTab : IManagementTab
    {
        private readonly ShopBuilding _shop;
        public ShopCashiersTab(ShopBuilding shop) { _shop = shop; }
        public string Name => "Cashiers";
        public IManagementTabView CreateView() => ShopCashiersTabView.Spawn(_shop);
    }

    public class ShopCashiersTabView : MonoBehaviour, IManagementTabView
    {
        [SerializeField] private Transform _rowsParent;
        [SerializeField] private GameObject _rowPrefab;
        [SerializeField] private TMP_Text _staffingLabel;

        private ShopBuilding _shop;
        private readonly List<ShopCashiersTabRow> _rows = new();
        public GameObject Root => gameObject;

        public static ShopCashiersTabView Spawn(ShopBuilding shop)
        {
            var prefab = Resources.Load<GameObject>("UI/Management/Tabs/ShopCashiersTabView");
            var go = Instantiate(prefab);
            var view = go.GetComponent<ShopCashiersTabView>();
            view._shop = shop;
            return view;
        }

        public void OnTabActivated()
        {
            _shop.OnCashiersChanged += Refresh;
            Refresh();
        }
        public void OnTabDeactivated() => _shop.OnCashiersChanged -= Refresh;
        public void Dispose() { OnTabDeactivated(); Destroy(gameObject); }

        private void Refresh()
        {
            for (int i = 0; i < _rows.Count; i++) Destroy(_rows[i].gameObject);
            _rows.Clear();

            int requiringVendor = 0, vendorsHired = 0;
            foreach (var c in _shop.Cashiers)
            {
                if (c == null) continue;
                if (c.RequiresVendor) requiringVendor++;
                var rowGo = Instantiate(_rowPrefab, _rowsParent);
                var row = rowGo.GetComponent<ShopCashiersTabRow>();
                row.Bind(_shop, c);
                _rows.Add(row);
            }
            foreach (var jv in _shop.Vendors) if (jv.IsAssigned) vendorsHired++; // adapt IsAssigned to project's actual job-state API

            _staffingLabel.text = $"Cashiers requiring a vendor: {requiringVendor}\nVendors hired: {vendorsHired} / {requiringVendor}";
        }
    }

    public class ShopCashiersTabRow : MonoBehaviour
    {
        [SerializeField] private TMP_Text _cashierLabel;
        [SerializeField] private TMP_Text _vendorLabel;
        [SerializeField] private TMP_Text _tillLabel;
        [SerializeField] private Button _withdrawButton;

        private ShopBuilding _shop;
        private Cashier _cashier;

        public void Bind(ShopBuilding shop, Cashier cashier)
        {
            _shop = shop; _cashier = cashier;
            _cashierLabel.text = cashier.FurnitureName;
            _vendorLabel.text = cashier.Occupant != null ? cashier.Occupant.CharacterName : "(vacant)";
            _tillLabel.text = $"{cashier.GetTillBalance(MWI.Economy.CurrencyId.Default)} g";

            // Subscribe to till changes so the label refreshes live.
            if (cashier.NetSync != null)
                cashier.NetSync.TillBalances.OnListChanged += OnTillChanged;
            _withdrawButton.onClick.AddListener(OnWithdrawClicked);
        }

        private void OnTillChanged(Unity.Netcode.NetworkListEvent<MWI.Economy.CurrencyBalanceEntry> _)
        {
            _tillLabel.text = $"{_cashier.GetTillBalance(MWI.Economy.CurrencyId.Default)} g";
        }

        private void OnWithdrawClicked()
        {
            var net = _cashier.GetComponent<Unity.Netcode.NetworkObject>();
            if (net == null) return;
            _shop.WithdrawCashierTillServerRpc(new Unity.Netcode.NetworkObjectReference(net));
        }

        private void OnDestroy()
        {
            if (_cashier?.NetSync != null) _cashier.NetSync.TillBalances.OnListChanged -= OnTillChanged;
            if (_withdrawButton != null) _withdrawButton.onClick.RemoveListener(OnWithdrawClicked);
        }
    }
}
```

- [ ] **Step 2 — Designer prefab + commit**

```bash
git add Assets/Scripts/UI/Management/Tabs/ShopCashiersTab*.cs Assets/Resources/UI/Management/Tabs/ShopCashiersTab*
git commit -m "feat(mgmt-ui): ShopCashiersTab (per-cashier till + Withdraw)"
```

---

### Task 29: `CatalogItemPickerDialog`

**Files:**
- Create: `Assets/Scripts/UI/Management/Tabs/CatalogItemPickerDialog.cs`
- Designer: `Assets/Resources/UI/Management/Tabs/CatalogItemPickerDialog.prefab`

- [ ] **Step 1 — Create the dialog**

```csharp
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MWI.UI.Management.Tabs
{
    public class CatalogItemPickerDialog : MonoBehaviour
    {
        [SerializeField] private TMP_Dropdown _itemDropdown;
        [SerializeField] private TMP_InputField _maxStockInput;
        [SerializeField] private TMP_InputField _priceOverrideInput;
        [SerializeField] private TMP_Text _priceHelper;
        [SerializeField] private Button _confirmButton;
        [SerializeField] private Button _cancelButton;

        private static CatalogItemPickerDialog _instance;
        private ShopBuilding _shop;
        private List<ItemSO> _availableItems;
        private Action<ItemSO, int, int> _onPicked;

        public static void Show(ShopBuilding shop, Action<ItemSO, int, int> onPicked)
        {
            if (_instance == null)
            {
                var prefab = Resources.Load<GameObject>("UI/Management/Tabs/CatalogItemPickerDialog");
                _instance = Instantiate(prefab).GetComponent<CatalogItemPickerDialog>();
            }
            _instance._shop = shop;
            _instance._onPicked = onPicked;
            _instance.PopulateDropdown();
            _instance.gameObject.SetActive(true);
        }

        private void PopulateDropdown()
        {
            // Phase 2b: list every ItemSO. Future swap to known-entities registry is one line.
            _availableItems = new List<ItemSO>(Resources.LoadAll<ItemSO>("Data/Item"));
            // Filter out items already in the catalog (no duplicate entries).
            _availableItems.RemoveAll(item => _shop.GetCatalogEntry(item).HasValue);
            _availableItems.Sort((a, b) => string.Compare(a.ItemName, b.ItemName, StringComparison.Ordinal));

            _itemDropdown.ClearOptions();
            var options = new List<TMP_Dropdown.OptionData>();
            foreach (var item in _availableItems) options.Add(new TMP_Dropdown.OptionData(item.ItemName, item.Icon));
            _itemDropdown.AddOptions(options);
            _itemDropdown.value = 0;
            _itemDropdown.onValueChanged.AddListener(OnDropdownChanged);
            _confirmButton.onClick.AddListener(OnConfirm);
            _cancelButton.onClick.AddListener(OnCancel);

            _maxStockInput.SetTextWithoutNotify("10");
            _priceOverrideInput.SetTextWithoutNotify("0");
            UpdatePriceHelper();
        }

        private void OnDropdownChanged(int idx) => UpdatePriceHelper();
        private void UpdatePriceHelper()
        {
            int idx = _itemDropdown.value;
            if (idx < 0 || idx >= _availableItems.Count) { _priceHelper.text = ""; return; }
            var item = _availableItems[idx];
            _priceHelper.text = item.BasePrice > 0
                ? $"0 = use base price ({item.BasePrice} g)"
                : "0 = item has no base price";
        }

        private void OnConfirm()
        {
            int idx = _itemDropdown.value;
            if (idx < 0 || idx >= _availableItems.Count) return;
            int.TryParse(_maxStockInput.text, out int maxStock);
            int.TryParse(_priceOverrideInput.text, out int price);
            _onPicked?.Invoke(_availableItems[idx], maxStock, price);
            Hide();
        }
        private void OnCancel() => Hide();

        private void Hide()
        {
            _itemDropdown.onValueChanged.RemoveListener(OnDropdownChanged);
            _confirmButton.onClick.RemoveListener(OnConfirm);
            _cancelButton.onClick.RemoveListener(OnCancel);
            gameObject.SetActive(false);
            _onPicked = null;
        }
    }
}
```

- [ ] **Step 2 — Designer prefab + commit**

```bash
git add Assets/Scripts/UI/Management/Tabs/CatalogItemPickerDialog.cs Assets/Resources/UI/Management/Tabs/CatalogItemPickerDialog*
git commit -m "feat(mgmt-ui): CatalogItemPickerDialog for owner add-item flow"
```

---

### Task 30: Override `ShopBuilding.GetManagementTabs()`

**Files:**
- Modify: `Assets/Scripts/World/Buildings/CommercialBuildings/ShopBuilding.cs`

- [ ] **Step 1 — Add the override**

Inside `ShopBuilding`, append:

```csharp
public override System.Collections.Generic.IReadOnlyList<MWI.UI.Management.IManagementTab> GetManagementTabs()
{
    var tabs = new System.Collections.Generic.List<MWI.UI.Management.IManagementTab>(base.GetManagementTabs());
    tabs.Add(new MWI.UI.Management.Tabs.ShopCatalogTab(this));
    tabs.Add(new MWI.UI.Management.Tabs.ShopShelvesTab(this));
    tabs.Add(new MWI.UI.Management.Tabs.ShopCashiersTab(this));
    return tabs;
}
```

- [ ] **Step 2 — Refresh + compile + manual UI smoke**

In Unity Editor: open the management desk on a ShopBuilding as the owner. Expected: four tabs (Hiring + Catalog + Shelves + Cashiers), all functional.

- [ ] **Step 3 — Commit**

```bash
git add Assets/Scripts/World/Buildings/CommercialBuildings/ShopBuilding.cs
git commit -m "feat(shop): override GetManagementTabs to surface Catalog/Shelves/Cashiers"
```

---

## Wave 11 — Documentation + integration verification

### Task 31: SKILL.md updates

**Files:**
- Create: `.agent/skills/shop-system/SKILL.md`
- Modify: `.agent/skills/jobs/SKILL.md` (or wherever JobVendor is documented)
- Modify: `.agent/skills/character-actions/SKILL.md`
- Modify: `.agent/skills/goap-actions/SKILL.md`

- [ ] **Step 1 — Read the skill-creator template**

Open `.agent/skills/skill-creator/SKILL.md` to confirm the SKILL.md template before authoring `shop-system`.

- [ ] **Step 2 — Author `shop-system` SKILL.md**

Cover sections per the template: Purpose, Public API (Cashier / ShopBuilding / CharacterAction_BuyFromShop / CashierInteractable / CashierNetSync / ShopBuildingNetSync), Events, Dependencies, Integration points, How to add a new commercial building variant that uses Cashier, how to author an automatic-distributor cashier (`_requiresVendor=false`).

- [ ] **Step 3 — Update jobs / character-actions / goap-actions SKILL.md files**

- `jobs/SKILL.md`: replace JobVendor section with the pool model (no AssignedCashier).
- `character-actions/SKILL.md`: list `CharacterAction_BuyFromShop` under continuous-action subclasses.
- `goap-actions/SKILL.md`: refactored `GoapAction_GoShopping` (catalog + funds + capacity gates).

- [ ] **Step 4 — Commit**

```bash
git add .agent/skills/
git commit -m "docs(skills): SKILL.md updates for shop system + JobVendor + buy action"
```

---

### Task 32: Wiki updates

**Files:**
- Create: `wiki/systems/shop-system.md`
- Create: `wiki/systems/cashier-furniture.md`
- Modify: `wiki/systems/character-actions.md`
- Modify: `wiki/systems/jobs.md` (or equivalent)

- [ ] **Step 1 — Read the wiki schema**

Open `wiki/CLAUDE.md` for the wiki rules and `wiki/_templates/system.md` (or equivalent) for the system-page template. Follow the 10 required sections + frontmatter convention.

- [ ] **Step 2 — Author `wiki/systems/shop-system.md`**

Sections: Purpose, Responsibilities, Key classes/files (link to source), Public API, Data flow (customer + vendor), Dependencies (Building, Furniture, CharacterEquipment, CharacterWallet, NGO), State & persistence, Gotchas (race semantics, save-during-transaction, multi-cashier ordering), Open questions, Change log, Sources (link to spec + SKILL.md).

- [ ] **Step 3 — Author `wiki/systems/cashier-furniture.md`**

Sections same structure, focused on the Cashier furniture primitive.

- [ ] **Step 4 — Update existing pages**

`wiki/systems/character-actions.md`: append change-log line + add `CharacterAction_BuyFromShop` row to the actions table.
`wiki/systems/jobs.md`: update JobVendor entry to pool model.

- [ ] **Step 5 — Commit**

```bash
git add wiki/
git commit -m "docs(wiki): shop-system + cashier-furniture pages + JobVendor pool-model update"
```

---

### Task 33: Integration verification — 15 testing scenarios

**Verification approach:** open Unity Editor, build a test scene with the necessary furniture/characters, exercise each scenario via player input + MCP. Document any deviations against the spec; fix or file a follow-up.

For each scenario, follow:

1. Set up the scene as described.
2. Trigger the action.
3. Verify the expected outcome via Unity Editor inspector + MCP debug commands (`mcp__ai-game-developer__gameobject-component-get` for state inspection).
4. Repeat with Host alone, Host+Client (using Multiplayer Play Mode), and any NPC variants per rule #19.

The 15 scenarios from the spec (Section 9):

- [ ] **1. Catalog mutation** — owner adds/edits/removes; all peers see changes via NetworkList.
- [ ] **2. Shelf designation** — owner toggles a chest; player buy panel reflects live.
- [ ] **3. Vendor occupy** — NPC and player vendors auto-seat on shift; release on movement away.
- [ ] **4. Vendor race** — two vendors target same cashier; loser falls through.
- [ ] **5. Customer transaction (player)** — tap E, pick items, confirm; atomic commit.
- [ ] **6. Customer transaction (NPC)** — GOAP-driven; 2s timer; commit identical.
- [ ] **7. Out-of-stock during commit** — concurrent NPC purchase drains shelf; player Confirm aborts cleanly with toast.
- [ ] **8. Insufficient funds** — UI Confirm disabled; if bypassed, server-side commit aborts.
- [ ] **9. Overflow drop** — player + NPC: ground-spawn next to them.
- [ ] **10. Vendor leaves mid-transaction** — UI closes with toast; lock cleared.
- [ ] **11. Cashier picked up mid-transaction** — same abort; till coins drop (logged for now).
- [ ] **12. Withdraw from till** — owner clicks; coins move to wallet; balance updates.
- [ ] **13. Save during transaction** — load resets locks; vendor walks back.
- [ ] **14. Late joiner during active shop** — sees catalog, shelves, till, occupant.
- [ ] **15. Auto-distributor mode** (forward-compat smoke) — `_requiresVendor = false` cashier accepts customer without vendor.

- [ ] **Final commit (verification log)**

Any verification notes or follow-up TODOs go in `wiki/projects/optimisation-backlog.md` or the appropriate wiki location, NOT in source TODOs (rule #34).

```bash
git add wiki/
git commit -m "docs(verification): Phase 2b integration scenarios complete"
```

---

## Self-review

After all tasks complete, verify:

1. **Spec coverage** — every section of the spec has at least one task. Section 1 (architecture) → all of Wave 1-10. Section 2 (data) → Wave 1. Section 3 (cashier lifecycle) → Wave 2-3. Section 4 (vendor flow) → Wave 5. Section 5 (customer flow) → Waves 6-8. Section 6 (management tabs) → Wave 10. Section 7 (networking) → covered by NetSync tasks across 2-4. Section 8 (save/load) → Wave 9. Section 9 (migration + testing) → Waves 11.
2. **Placeholder scan** — three explicit `PLACEHOLDER` markers remain in Task 25 (furniture-instance ID resolution) — flagged for the executor to substitute against project convention. No silent TODOs.
3. **Type consistency** — `Cashier.NetSync` is the public getter; `_netSync` is the private field. `CharacterAction_BuyFromShop` uses `BuyMode { NPC, Player }` consistently. `Cashier.IsAvailableForCustomer` is the canonical predicate (used in `CanExecute`, `RequestStartBuyServerRpc`, `JobVendor.Execute`, `GoapAction_GoShopping`).

---

*End of plan.*
