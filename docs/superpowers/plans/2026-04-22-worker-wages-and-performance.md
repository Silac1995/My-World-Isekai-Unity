# Worker Wages & Performance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the worker-wages-and-performance feature: a multi-currency `CharacterWallet`, a `CharacterWorkLog` with per-JobType / per-workplace lifetime history, owner-editable wage fields on `JobAssignment`, and a `WageSystemService` that pays workers at punch-out using `wage = (shiftRatio × min) + (pieceRate × shiftUnits)` for piece-work and `shiftRatio × fixed` for fixed-wage jobs. No building treasury (minted for now); `CurrencyId` is a placeholder for the future Kingdom system.

**Architecture:** Two new `CharacterSystem` subsystems (`CharacterWallet`, `CharacterWorkLog`) on child GOs of the Character facade, both `ICharacterSaveData<T>` participants auto-discovered by `CharacterDataCoordinator`. Pure-logic helpers (`WageCalculator`, `HarvesterCreditCalculator`) kept static and unit-testable. Hooks into existing `CommercialBuilding.OnWorkerPunchIn/Out`, `GoapAction_DepositResources`, `JobCrafter` subclasses, `JobTransporter.NotifyDeliveryProgress`, and `MacroSimulator` yield loop.

**Tech Stack:** Unity 6, C#, Unity Netcode for GameObjects (NGO), Newtonsoft.Json (existing for save JSON), Unity Test Framework (bootstrapped in Task 2).

**Spec reference:** [docs/superpowers/specs/2026-04-22-worker-wages-and-performance-design.md](../specs/2026-04-22-worker-wages-and-performance-design.md)

**Deliberate deviation from full TDD:** the project has no test assembly today. Task 2 bootstraps one (Unity Test Framework / NUnit) *for the pure-logic helpers only*. MonoBehaviour / NetworkBehaviour glue, scene-dependent code, and integration hooks are verified via compile + manual smoke-test steps (explicit expected outputs given). Writing Play-mode tests for every subsystem is out of scope for this feature.

---

## File Structure

### New files (22)

| Path | Responsibility |
|---|---|
| `Assets/Scripts/Economy/CurrencyId.cs` | Thin struct wrapping int currency id. One `Default` currency today. |
| `Assets/Scripts/Character/CharacterWallet/CharacterWallet.cs` | Subsystem: per-currency `NetworkVariable<int>`-backed balances, save contract, server RPCs for mutation. |
| `Assets/Scripts/Character/CharacterWallet/WalletSaveData.cs` | `[Serializable]` DTO for save. |
| `Assets/Scripts/Character/CharacterWorkLog/CharacterWorkLog.cs` | Subsystem: per-shift / per-career counters, per-workplace records, save contract. |
| `Assets/Scripts/Character/CharacterWorkLog/WorkLogSaveData.cs` | `[Serializable]` DTO. |
| `Assets/Scripts/Character/CharacterWorkLog/WorkPlaceRecord.cs` | Per-(JobType, BuildingId) runtime record. |
| `Assets/Scripts/Character/CharacterWorkLog/ShiftSummary.cs` | Return value of `FinalizeShift`. |
| `Assets/Scripts/World/Jobs/Wages/WageRatesSO.cs` | Designer-tunable defaults per `JobType`. |
| `Assets/Scripts/World/Jobs/Wages/WageRateEntry.cs` | `[Serializable]` row of the SO. |
| `Assets/Scripts/World/Jobs/Wages/IWagePayer.cs` | Payer interface (mint today / treasury later). |
| `Assets/Scripts/World/Jobs/Wages/MintedWagePayer.cs` | Default impl — credits wallet unconditionally. |
| `Assets/Scripts/World/Jobs/Wages/WageSystemService.cs` | Singleton `MonoBehaviour` on world controller; holds `WageRatesSO` + active `IWagePayer`; computes and pays. |
| `Assets/Scripts/World/Jobs/Wages/WageCalculator.cs` | Static pure-logic wage math — unit-testable. |
| `Assets/Scripts/World/Jobs/Wages/HarvesterCreditCalculator.cs` | Static pure-logic deficit math — unit-testable. |
| `Assets/Tests/EditMode/WagesAndPerformance.Tests.asmdef` | Test assembly definition. |
| `Assets/Tests/EditMode/WageCalculatorTests.cs` | Unit tests for `WageCalculator`. |
| `Assets/Tests/EditMode/HarvesterCreditCalculatorTests.cs` | Unit tests for `HarvesterCreditCalculator`. |
| `Assets/ScriptableObjects/Jobs/WageRates.asset` | Concrete `WageRatesSO` asset (placeholder values). |
| `.agent/skills/character-wallet/SKILL.md` | Procedures: use wallet, currency model, save/load, networking surface. |
| `.agent/skills/character-worklog/SKILL.md` | Procedures: counter taxonomy, workplace records, integration hooks. |
| `.agent/skills/wage-system/SKILL.md` | Procedures: formulas, payer swap, hire-time seed, macro-sim bridge. |
| `wiki/systems/worker-wages-and-performance.md` | Architecture page. |

### Modified files (15)

| Path | Change |
|---|---|
| `Assets/Scripts/Character/Character.cs` | Add `_characterWallet` / `_characterWorkLog` slots + properties with registry-first fallback. |
| `Assets/Scripts/Character/CharacterJob/CharacterJob.cs` | Extend `JobAssignment` with wage fields; add `SetWage` method; hire-time defaults seeding. |
| `Assets/Scripts/Character/SaveLoad/ProfileSaveData/JobSaveData.cs` | Extend `JobAssignmentSaveEntry` with wage fields + currency id. |
| `Assets/Scripts/World/Buildings/CommercialBuilding.cs` | Wage hooks at punch-in/out; `PaymentCurrency` placeholder property. |
| `Assets/Scripts/AI/GOAP/Actions/GoapAction_DepositResources.cs` | Harvester credit on deposit (deficit-bounded). |
| `Assets/Scripts/World/Jobs/TransportJobs/JobTransporter.cs` | Transporter credit in `NotifyDeliveryProgress`. |
| `Assets/Scripts/World/Jobs/CraftingJobs/JobCrafter.cs` (and subclasses that override completion) | Crafter credit on item completion. |
| `Assets/Scripts/World/MapSystem/MacroSimulator.cs` | WorkLog accrual in Inventory Yields pass. |
| `.agent/skills/job_system/SKILL.md` | Cross-link wage hooks. |
| `.agent/skills/logistics_cycle/SKILL.md` | Cross-link deficit-bounded harvest credit. |
| `.agent/skills/save-load-system/SKILL.md` | Note wallet + worklog save contracts. |
| `wiki/systems/jobs-and-logistics.md` | Change-log entry + cross-link. |
| `wiki/systems/commercial-building.md` | Change-log entry + cross-link. |
| `wiki/systems/character-job.md` | Change-log entry + cross-link. |
| `wiki/INDEX.md` | One-line entry for new systems page. |
| `.claude/agents/npc-ai-specialist.md` | Add wage/worklog awareness. |
| `.claude/agents/building-furniture-specialist.md` | Add wage hooks awareness. |

---

## Pre-flight — References You Will Need

Keep these open:

- [Assets/Scripts/Character/CharacterNeeds/CharacterNeeds.cs](../../../Assets/Scripts/Character/CharacterNeeds/CharacterNeeds.cs) — reference `ICharacterSaveData<T>` shape + non-generic bridge (`SerializeToJson`/`DeserializeFromJson` via `CharacterSaveDataHelper`).
- [Assets/Scripts/Character/CharacterParty/CharacterParty.cs](../../../Assets/Scripts/Character/CharacterParty/CharacterParty.cs) — reference `NetworkVariable` + `OnNetworkSpawn` subscription pattern.
- [Assets/Scripts/Character/SaveLoad/CharacterSaveDataBase.cs](../../../Assets/Scripts/Character/SaveLoad/CharacterSaveDataBase.cs) — `CharacterSaveDataHelper` for JSON bridge.
- [Assets/Scripts/Character/CharacterJob/CharacterJob.cs](../../../Assets/Scripts/Character/CharacterJob/CharacterJob.cs) — existing `JobAssignment` class (lines 5-11), `TakeJob` (line 76), save priority (60).
- [Assets/Scripts/Character/SaveLoad/ProfileSaveData/JobSaveData.cs](../../../Assets/Scripts/Character/SaveLoad/ProfileSaveData/JobSaveData.cs) — 14-line file, trivial to extend.
- [Assets/Scripts/World/Buildings/CommercialBuilding.cs](../../../Assets/Scripts/World/Buildings/CommercialBuilding.cs) — `OnWorkerPunchIn` ~line 444, `OnWorkerPunchOut` ~line 493, `WorkerEndingShift` ~line 481.
- [Assets/Scripts/World/Jobs/TransportJobs/JobTransporter.cs](../../../Assets/Scripts/World/Jobs/TransportJobs/JobTransporter.cs) — `NotifyDeliveryProgress` at line 340-364.
- [Assets/Scripts/World/MapSystem/MacroSimulator.cs](../../../Assets/Scripts/World/MapSystem/MacroSimulator.cs) — Inventory Yields pass lines 142-167.
- [Assets/Scripts/DayNightCycle/TimeManager.cs](../../../Assets/Scripts/DayNightCycle/TimeManager.cs) — `CurrentDay` / `CurrentTime01` (lines 27-35).
- [Assets/Scripts/World/Jobs/JobYieldRegistry.cs](../../../Assets/Scripts/World/Jobs/JobYieldRegistry.cs) — `JobYieldRecipe` / `YieldOutput` shape.

**LoadPriority convention chosen:**

| New subsystem | Priority | Rationale |
|---|---|---|
| `CharacterWallet` | 35 | After equipment (30), before needs (40). Self-contained. |
| `CharacterWorkLog` | 65 | After `CharacterJob` (60) — worklog references building ids that jobs also track, harmless if coincidental. |

---

## Task 1: Add `CurrencyId` Primitive

**Files:**
- Create: `Assets/Scripts/Economy/CurrencyId.cs`

- [ ] **Step 1: Create the file**

```csharp
using System;

namespace MWI.Economy
{
    /// <summary>
    /// Thin handle for a currency. Placeholder for the future Kingdom system —
    /// today only <see cref="Default"/> exists; later, kingdoms mint additional ids.
    /// </summary>
    [Serializable]
    public struct CurrencyId : IEquatable<CurrencyId>
    {
        public int Id;

        public CurrencyId(int id) { Id = id; }

        public static readonly CurrencyId Default = new CurrencyId(0);

        public bool Equals(CurrencyId other) => Id == other.Id;
        public override bool Equals(object obj) => obj is CurrencyId c && Equals(c);
        public override int GetHashCode() => Id.GetHashCode();
        public static bool operator ==(CurrencyId a, CurrencyId b) => a.Id == b.Id;
        public static bool operator !=(CurrencyId a, CurrencyId b) => a.Id != b.Id;
        public override string ToString() => $"Currency#{Id}";
    }
}
```

- [ ] **Step 2: Verify compile**

In Unity Editor (Console window), confirm zero compile errors after the file is saved. If the Editor is running, it will auto-refresh; otherwise use the `assets-refresh` MCP tool.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Economy/CurrencyId.cs
git commit -m "feat(economy): add CurrencyId placeholder handle for multi-currency wallet"
```

---

## Task 2: Bootstrap EditMode Test Assembly

**Files:**
- Create: `Assets/Tests/EditMode/WagesAndPerformance.Tests.asmdef`

- [ ] **Step 1: Create `Assets/Tests/` and `Assets/Tests/EditMode/` folders**

Use the `assets-create-folder` MCP tool (or create via Unity Project window). Folders are required before the asmdef will resolve.

- [ ] **Step 2: Create the asmdef**

```json
{
    "name": "WagesAndPerformance.Tests",
    "rootNamespace": "MWI.Tests",
    "references": [
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner"
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
    "noEngineReferences": false,
    "optionalUnityReferences": []
}
```

- [ ] **Step 3: Save the file, wait for Unity to import**

Verify in the Unity Editor: open `Window > General > Test Runner > EditMode` tab. The `WagesAndPerformance.Tests` assembly should appear (empty until we add tests).

- [ ] **Step 4: Commit**

```bash
git add Assets/Tests/EditMode/WagesAndPerformance.Tests.asmdef
git commit -m "test: bootstrap EditMode test assembly for wage/performance helpers"
```

---

## Task 3: Add `WageCalculator` Pure Helper

**Files:**
- Create: `Assets/Scripts/World/Jobs/Wages/WageCalculator.cs`

- [ ] **Step 1: Create the helper**

```csharp
using UnityEngine;

namespace MWI.Jobs.Wages
{
    /// <summary>
    /// Pure-logic wage math. No Unity dependencies outside Mathf.Clamp01.
    /// Kept deliberately small so it can be unit-tested in an EditMode assembly.
    /// </summary>
    public static class WageCalculator
    {
        /// <summary>
        /// Piece-work wage: (shiftRatio * minimumShiftWage) + (pieceRate * shiftUnits).
        /// Minimum component is attendance-prorated. Piece bonus is not.
        /// </summary>
        public static int ComputePieceWorkWage(
            float hoursWorked, float scheduledShiftHours,
            int minimumShiftWage, int pieceRate, int shiftUnits)
        {
            float ratio = ComputeShiftRatio(hoursWorked, scheduledShiftHours);
            return Mathf.RoundToInt(ratio * minimumShiftWage) + (pieceRate * shiftUnits);
        }

        /// <summary>
        /// Fixed-wage wage: shiftRatio * fixedShiftWage. Fully attendance-prorated.
        /// </summary>
        public static int ComputeFixedWage(
            float hoursWorked, float scheduledShiftHours, int fixedShiftWage)
        {
            float ratio = ComputeShiftRatio(hoursWorked, scheduledShiftHours);
            return Mathf.RoundToInt(ratio * fixedShiftWage);
        }

        /// <summary>
        /// Clamped attendance ratio. Caps at 1.0 — no overtime bonus.
        /// Safe if scheduledShiftHours &lt;= 0 (returns 0).
        /// </summary>
        public static float ComputeShiftRatio(float hoursWorked, float scheduledShiftHours)
        {
            if (scheduledShiftHours <= 0f) return 0f;
            return Mathf.Clamp01(hoursWorked / scheduledShiftHours);
        }
    }
}
```

- [ ] **Step 2: Verify compile (zero errors)**

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/World/Jobs/Wages/WageCalculator.cs
git commit -m "feat(wages): add pure-logic WageCalculator helper"
```

---

## Task 4: Unit Tests for `WageCalculator`

**Files:**
- Create: `Assets/Tests/EditMode/WageCalculatorTests.cs`

- [ ] **Step 1: Write the test file**

```csharp
using NUnit.Framework;
using MWI.Jobs.Wages;

namespace MWI.Tests
{
    public class WageCalculatorTests
    {
        [Test]
        public void FullShiftZeroUnits_PieceWork_PaysFullMinimum()
        {
            // 8h/8h, min=10, piece=2, units=0 -> ratio=1, wage=10 + 0 = 10
            int w = WageCalculator.ComputePieceWorkWage(8f, 8f, 10, 2, 0);
            Assert.AreEqual(10, w);
        }

        [Test]
        public void FullShiftWithUnits_PieceWork_AddsPieceBonus()
        {
            // 8h/8h, min=10, piece=2, units=5 -> 10 + 10 = 20
            int w = WageCalculator.ComputePieceWorkWage(8f, 8f, 10, 2, 5);
            Assert.AreEqual(20, w);
        }

        [Test]
        public void HalfShift_PieceWork_ProratesMinimumOnly()
        {
            // 4h/8h, min=10, piece=2, units=3 -> ratio=0.5, wage=5 + 6 = 11
            int w = WageCalculator.ComputePieceWorkWage(4f, 8f, 10, 2, 3);
            Assert.AreEqual(11, w);
        }

        [Test]
        public void OvertimeHours_PieceWork_CapsRatioAtOne()
        {
            // 12h/8h, min=10, piece=2, units=0 -> ratio=1.0 (not 1.5), wage=10
            int w = WageCalculator.ComputePieceWorkWage(12f, 8f, 10, 2, 0);
            Assert.AreEqual(10, w);
        }

        [Test]
        public void FullShift_FixedWage_PaysFull()
        {
            int w = WageCalculator.ComputeFixedWage(8f, 8f, 15);
            Assert.AreEqual(15, w);
        }

        [Test]
        public void HalfShift_FixedWage_ProratesHalf()
        {
            int w = WageCalculator.ComputeFixedWage(4f, 8f, 15);
            Assert.AreEqual(8, w); // 0.5 * 15 = 7.5 -> rounds to 8
        }

        [Test]
        public void ZeroScheduledHours_ReturnsZeroRatio()
        {
            int w = WageCalculator.ComputePieceWorkWage(1f, 0f, 10, 2, 5);
            // ratio clamps to 0, wage = 0 + 10 = 10 (piece still pays)
            Assert.AreEqual(10, w);
        }

        [Test]
        public void NegativeHours_ReturnsZeroRatio()
        {
            int w = WageCalculator.ComputeFixedWage(-2f, 8f, 15);
            Assert.AreEqual(0, w);
        }
    }
}
```

- [ ] **Step 2: Run tests**

In Unity Editor: `Window > General > Test Runner > EditMode > Run All`. Expect 8/8 pass.

- [ ] **Step 3: Commit**

```bash
git add Assets/Tests/EditMode/WageCalculatorTests.cs
git commit -m "test(wages): add unit tests for WageCalculator"
```

---

## Task 5: Add `HarvesterCreditCalculator` Pure Helper + Tests

**Files:**
- Create: `Assets/Scripts/World/Jobs/Wages/HarvesterCreditCalculator.cs`
- Create: `Assets/Tests/EditMode/HarvesterCreditCalculatorTests.cs`

- [ ] **Step 1: Create the helper**

```csharp
using System;

namespace MWI.Jobs.Wages
{
    /// <summary>
    /// Pure-logic deficit-bounded credit for harvester deposits.
    /// Credit = clamp(depositQty, 0, deficitBefore).
    /// Excess deposits do not earn bonus pay.
    /// </summary>
    public static class HarvesterCreditCalculator
    {
        public static int GetCreditedAmount(int depositQty, int deficitBefore)
        {
            if (depositQty <= 0) return 0;
            if (deficitBefore <= 0) return 0;
            return Math.Min(depositQty, deficitBefore);
        }
    }
}
```

- [ ] **Step 2: Create the tests**

```csharp
using NUnit.Framework;
using MWI.Jobs.Wages;

namespace MWI.Tests
{
    public class HarvesterCreditCalculatorTests
    {
        [Test]
        public void Deposit_WithinDeficit_CreditsFullDeposit()
        {
            Assert.AreEqual(3, HarvesterCreditCalculator.GetCreditedAmount(3, 5));
        }

        [Test]
        public void Deposit_ExceedsDeficit_CreditsOnlyDeficitAmount()
        {
            Assert.AreEqual(3, HarvesterCreditCalculator.GetCreditedAmount(10, 3));
        }

        [Test]
        public void Deposit_ToFullBuilding_CreditsZero()
        {
            Assert.AreEqual(0, HarvesterCreditCalculator.GetCreditedAmount(5, 0));
        }

        [Test]
        public void Deposit_NegativeDeficit_CreditsZero()
        {
            Assert.AreEqual(0, HarvesterCreditCalculator.GetCreditedAmount(5, -2));
        }

        [Test]
        public void Deposit_ZeroQty_CreditsZero()
        {
            Assert.AreEqual(0, HarvesterCreditCalculator.GetCreditedAmount(0, 5));
        }

        [Test]
        public void Deposit_NegativeQty_CreditsZero()
        {
            Assert.AreEqual(0, HarvesterCreditCalculator.GetCreditedAmount(-3, 5));
        }
    }
}
```

- [ ] **Step 3: Run EditMode tests → expect 6/6 pass**

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/World/Jobs/Wages/HarvesterCreditCalculator.cs Assets/Tests/EditMode/HarvesterCreditCalculatorTests.cs
git commit -m "feat(wages): add HarvesterCreditCalculator with deficit-bounded credit"
```

---

## Task 6: Create `WalletSaveData` DTO

**Files:**
- Create: `Assets/Scripts/Character/CharacterWallet/WalletSaveData.cs`

- [ ] **Step 1: Create the file**

```csharp
using System;
using System.Collections.Generic;

[Serializable]
public class WalletSaveData
{
    public List<CurrencyBalanceEntry> balances = new List<CurrencyBalanceEntry>();
}

[Serializable]
public class CurrencyBalanceEntry
{
    public int currencyId;
    public int amount;
}
```

- [ ] **Step 2: Verify compile**

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Character/CharacterWallet/WalletSaveData.cs
git commit -m "feat(wallet): add WalletSaveData DTO"
```

---

## Task 7: Implement `CharacterWallet` Subsystem

**Files:**
- Create: `Assets/Scripts/Character/CharacterWallet/CharacterWallet.cs`

Follow the `CharacterNeeds` + `CharacterParty` hybrid pattern: `CharacterSystem` subclass, `ICharacterSaveData<WalletSaveData>`, NetworkVariable for primary currency, dictionary for everything else.

- [ ] **Step 1: Create the file**

```csharp
using System;
using System.Collections.Generic;
using MWI.Economy;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Per-character wallet with multi-currency balances.
/// Server-authoritative — mutations must originate on the server (or route through a ServerRpc).
/// Persisted via ICharacterSaveData&lt;WalletSaveData&gt;.
/// Today uses a plain Dictionary synced via ClientRpc on change; sufficient while only
/// a single "Default" currency exists. When Kingdom lands and we have N currencies per
/// character, upgrade the sync path to NetworkList&lt;CurrencyBalanceEntry&gt; without
/// changing callers.
/// </summary>
public class CharacterWallet : CharacterSystem, ICharacterSaveData<WalletSaveData>
{
    private readonly Dictionary<CurrencyId, int> _balances = new Dictionary<CurrencyId, int>();

    public event Action<CurrencyId, int, int> OnBalanceChanged; // currency, oldValue, newValue
    public event Action<CurrencyId, int, string> OnCoinsReceived; // currency, amount, source

    // --- Public read API ---

    public int GetBalance(CurrencyId currency)
    {
        return _balances.TryGetValue(currency, out int v) ? v : 0;
    }

    public IReadOnlyDictionary<CurrencyId, int> GetAllBalances() => _balances;

    public bool CanAfford(CurrencyId currency, int amount)
    {
        if (amount <= 0) return true;
        return GetBalance(currency) >= amount;
    }

    // --- Public mutation API (server-authoritative) ---

    public void AddCoins(CurrencyId currency, int amount, string source)
    {
        if (amount <= 0)
        {
            Debug.LogError($"[CharacterWallet] AddCoins rejected: amount={amount} source={source} on {_character?.CharacterName}");
            return;
        }
        if (!IsServer && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            Debug.LogError($"[CharacterWallet] AddCoins called on non-server instance for {_character?.CharacterName}. Route through a ServerRpc.");
            return;
        }
        int old = GetBalance(currency);
        int next = old + amount;
        _balances[currency] = next;
        OnBalanceChanged?.Invoke(currency, old, next);
        OnCoinsReceived?.Invoke(currency, amount, source);
        BroadcastBalanceChangeClientRpc(currency.Id, next);
    }

    public bool RemoveCoins(CurrencyId currency, int amount, string reason)
    {
        if (amount <= 0) { Debug.LogError($"[CharacterWallet] RemoveCoins rejected: amount={amount} reason={reason}"); return false; }
        int old = GetBalance(currency);
        if (old < amount) return false;
        int next = old - amount;
        _balances[currency] = next;
        OnBalanceChanged?.Invoke(currency, old, next);
        BroadcastBalanceChangeClientRpc(currency.Id, next);
        return true;
    }

    [ClientRpc]
    private void BroadcastBalanceChangeClientRpc(int currencyRawId, int newValue)
    {
        if (IsServer) return; // server already applied it
        var currency = new CurrencyId(currencyRawId);
        int old = GetBalance(currency);
        _balances[currency] = newValue;
        OnBalanceChanged?.Invoke(currency, old, newValue);
    }

    // --- ICharacterSaveData ---

    public string SaveKey => "CharacterWallet";
    public int LoadPriority => 35;

    public WalletSaveData Serialize()
    {
        var data = new WalletSaveData();
        foreach (var kv in _balances)
        {
            data.balances.Add(new CurrencyBalanceEntry { currencyId = kv.Key.Id, amount = kv.Value });
        }
        return data;
    }

    public void Deserialize(WalletSaveData data)
    {
        _balances.Clear();
        if (data == null || data.balances == null) return;
        foreach (var entry in data.balances)
        {
            _balances[new CurrencyId(entry.currencyId)] = entry.amount;
        }
    }

    string ICharacterSaveData.SerializeToJson() => CharacterSaveDataHelper.SerializeToJson(this);
    void ICharacterSaveData.DeserializeFromJson(string json) => CharacterSaveDataHelper.DeserializeFromJson(this, json);
}
```

- [ ] **Step 2: Verify compile**

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Character/CharacterWallet/CharacterWallet.cs
git commit -m "feat(wallet): implement CharacterWallet subsystem with save + network sync"
```

---

## Task 8: Create `WorkPlaceRecord` + `ShiftSummary` + `WorkLogSaveData`

**Files:**
- Create: `Assets/Scripts/Character/CharacterWorkLog/WorkPlaceRecord.cs`
- Create: `Assets/Scripts/Character/CharacterWorkLog/ShiftSummary.cs`
- Create: `Assets/Scripts/Character/CharacterWorkLog/WorkLogSaveData.cs`

- [ ] **Step 1: Create `WorkPlaceRecord`**

```csharp
using System;

/// <summary>
/// Runtime record of a character's work history at a single building for a single JobType.
/// BuildingDisplayName is denormalized at first-work-time so the history survives
/// the building being destroyed/abandoned.
/// </summary>
[Serializable]
public class WorkPlaceRecord
{
    public string BuildingId;
    public string BuildingDisplayName;
    public int UnitsWorked;
    public int ShiftsCompleted;
    public int FirstWorkedDay;
    public int LastWorkedDay;
}
```

- [ ] **Step 2: Create `ShiftSummary`**

```csharp
/// <summary>
/// Return value of CharacterWorkLog.FinalizeShift — consumed by the wage pipeline.
/// </summary>
public class ShiftSummary
{
    public int ShiftUnits;
    public int NewCareerUnitsForJob;
    public int NewCareerUnitsForJobAndPlace;
    public int ShiftsCompletedAtPlace;
}
```

- [ ] **Step 3: Create `WorkLogSaveData`**

```csharp
using System;
using System.Collections.Generic;

[Serializable]
public class WorkLogSaveData
{
    public List<WorkLogJobEntry> jobs = new List<WorkLogJobEntry>();
}

[Serializable]
public class WorkLogJobEntry
{
    // JobType serialized as enum NAME (string) to match existing JobAssignmentSaveEntry pattern.
    public string jobType;
    public List<WorkPlaceSaveEntry> workplaces = new List<WorkPlaceSaveEntry>();
}

[Serializable]
public class WorkPlaceSaveEntry
{
    public string buildingId;
    public string buildingDisplayName;
    public int unitsWorked;
    public int shiftsCompleted;
    public int firstWorkedDay;
    public int lastWorkedDay;
}
```

- [ ] **Step 4: Verify compile, then commit**

```bash
git add Assets/Scripts/Character/CharacterWorkLog/
git commit -m "feat(worklog): add WorkPlaceRecord + ShiftSummary + WorkLogSaveData types"
```

---

## Task 9: Implement `CharacterWorkLog` Subsystem

**Files:**
- Create: `Assets/Scripts/Character/CharacterWorkLog/CharacterWorkLog.cs`

**Shift-window enforcement:** `LogShiftUnit` silently drops the shift-counter increment if the call arrives *after* `_currentShiftScheduledEndTime01` — the unit still accrues to the lifetime record (the work happened) but earns no bonus pay. This is the single point of truth for the "no overtime bonus" rule.

- [ ] **Step 1: Create the file**

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Per-character work-history log. Tracks:
///   - per-shift transient counters (not persisted),
///   - per-JobType lifetime counters (persisted),
///   - per-(JobType, BuildingId) lifetime WorkPlaceRecord entries with display-name snapshots (persisted).
///
/// The "no overtime bonus" rule is enforced inside LogShiftUnit: units logged past
/// the scheduled shift end are rolled into the lifetime record but not the shift counter.
/// </summary>
public class CharacterWorkLog : CharacterSystem, ICharacterSaveData<WorkLogSaveData>
{
    // JobType -> Dictionary<BuildingId, WorkPlaceRecord> — lifetime, persisted
    private readonly Dictionary<JobType, Dictionary<string, WorkPlaceRecord>> _careerByJob =
        new Dictionary<JobType, Dictionary<string, WorkPlaceRecord>>();

    // JobType -> shift unit count (transient, reset on punch-in)
    private readonly Dictionary<JobType, int> _shiftByJobType = new Dictionary<JobType, int>();

    // Scheduled shift end for the active shift (0..1 time-of-day). <0 = no active shift.
    private float _currentShiftScheduledEndTime01 = -1f;

    public event Action<JobType, string, int> OnShiftUnitLogged;
    public event Action<JobType, string, ShiftSummary> OnShiftFinalized;

    // --- Passthrough convenience (rule: CharacterJob is authoritative for current employment) ---
    // NOTE: replace ".Jobs" below with whatever public collection CharacterJob exposes
    //       (likely .Jobs, .Assignments, or .ActiveAssignments — verify before compile).
    public IReadOnlyList<JobAssignment> CurrentAssignments =>
        _character != null && _character.CharacterJob != null
            ? _character.CharacterJob.Jobs
            : (IReadOnlyList<JobAssignment>)Array.Empty<JobAssignment>();

    // --- Shift lifecycle ---

    /// <summary>
    /// Resets the shift counter for this JobType and records the scheduled end-of-shift
    /// (in 0..1 time-of-day, compared against TimeManager.CurrentTime01).
    /// Creates the WorkPlaceRecord if this is the character's first shift at this place.
    /// </summary>
    public void OnPunchIn(JobType jobType, string buildingId, string buildingDisplayName, float scheduledEndTime01)
    {
        _shiftByJobType[jobType] = 0;
        _currentShiftScheduledEndTime01 = scheduledEndTime01;

        var places = GetOrCreateJobMap(jobType);
        if (!places.TryGetValue(buildingId, out var rec))
        {
            rec = new WorkPlaceRecord
            {
                BuildingId = buildingId,
                BuildingDisplayName = buildingDisplayName,
                UnitsWorked = 0,
                ShiftsCompleted = 0,
                FirstWorkedDay = CurrentDay(),
                LastWorkedDay = CurrentDay()
            };
            places[buildingId] = rec;
        }
    }

    /// <summary>
    /// Increments shift and lifetime counters. If called AFTER the scheduled shift end,
    /// only the lifetime counter is touched — no shift credit (no overtime bonus).
    /// </summary>
    public void LogShiftUnit(JobType jobType, string buildingId, int amount = 1)
    {
        if (amount <= 0) return;

        // Lifetime always increments (it's history).
        var places = GetOrCreateJobMap(jobType);
        if (!places.TryGetValue(buildingId, out var rec))
        {
            // Defensive: log-without-punch-in. Create a record with best-effort display name.
            rec = new WorkPlaceRecord
            {
                BuildingId = buildingId,
                BuildingDisplayName = buildingId,
                UnitsWorked = 0,
                ShiftsCompleted = 0,
                FirstWorkedDay = CurrentDay(),
                LastWorkedDay = CurrentDay()
            };
            places[buildingId] = rec;
        }
        rec.UnitsWorked += amount;
        rec.LastWorkedDay = CurrentDay();

        // Shift counter only if we are still inside the scheduled window.
        if (IsWithinScheduledShift())
        {
            _shiftByJobType.TryGetValue(jobType, out int prev);
            int next = prev + amount;
            _shiftByJobType[jobType] = next;
            OnShiftUnitLogged?.Invoke(jobType, buildingId, next);
        }
    }

    /// <summary>
    /// Finalize the shift: roll shift counter into place record's ShiftsCompleted counter,
    /// clear transient state, return the summary for the wage pipeline.
    /// </summary>
    public ShiftSummary FinalizeShift(JobType jobType, string buildingId)
    {
        _shiftByJobType.TryGetValue(jobType, out int shiftUnits);

        var places = GetOrCreateJobMap(jobType);
        places.TryGetValue(buildingId, out var rec);
        if (rec != null)
        {
            rec.ShiftsCompleted += 1;
            rec.LastWorkedDay = CurrentDay();
        }

        var summary = new ShiftSummary
        {
            ShiftUnits = shiftUnits,
            NewCareerUnitsForJob = GetCareerUnits(jobType),
            NewCareerUnitsForJobAndPlace = rec != null ? rec.UnitsWorked : 0,
            ShiftsCompletedAtPlace = rec != null ? rec.ShiftsCompleted : 0
        };

        _shiftByJobType[jobType] = 0;
        _currentShiftScheduledEndTime01 = -1f;
        OnShiftFinalized?.Invoke(jobType, buildingId, summary);
        return summary;
    }

    // --- Queries ---

    public int GetShiftUnits(JobType jobType) =>
        _shiftByJobType.TryGetValue(jobType, out int v) ? v : 0;

    public int GetCareerUnits(JobType jobType)
    {
        if (!_careerByJob.TryGetValue(jobType, out var places)) return 0;
        int total = 0;
        foreach (var rec in places.Values) total += rec.UnitsWorked;
        return total;
    }

    public int GetCareerUnits(JobType jobType, string buildingId)
    {
        if (!_careerByJob.TryGetValue(jobType, out var places)) return 0;
        return places.TryGetValue(buildingId, out var rec) ? rec.UnitsWorked : 0;
    }

    public IReadOnlyList<WorkPlaceRecord> GetWorkplaces(JobType jobType)
    {
        if (!_careerByJob.TryGetValue(jobType, out var places)) return Array.Empty<WorkPlaceRecord>();
        return new List<WorkPlaceRecord>(places.Values);
    }

    public IReadOnlyDictionary<JobType, IReadOnlyList<WorkPlaceRecord>> GetAllHistory()
    {
        var result = new Dictionary<JobType, IReadOnlyList<WorkPlaceRecord>>();
        foreach (var kv in _careerByJob)
        {
            result[kv.Key] = new List<WorkPlaceRecord>(kv.Value.Values);
        }
        return result;
    }

    // --- Internals ---

    private Dictionary<string, WorkPlaceRecord> GetOrCreateJobMap(JobType jobType)
    {
        if (!_careerByJob.TryGetValue(jobType, out var map))
        {
            map = new Dictionary<string, WorkPlaceRecord>();
            _careerByJob[jobType] = map;
        }
        return map;
    }

    private bool IsWithinScheduledShift()
    {
        if (_currentShiftScheduledEndTime01 < 0f) return false;
        var tm = MWI.Time.TimeManager.Instance;
        if (tm == null) return true; // no time manager: accept (edit-mode / tests)
        return tm.CurrentTime01 <= _currentShiftScheduledEndTime01;
    }

    private int CurrentDay()
    {
        var tm = MWI.Time.TimeManager.Instance;
        return tm != null ? tm.CurrentDay : 1;
    }

    // --- ICharacterSaveData ---

    public string SaveKey => "CharacterWorkLog";
    public int LoadPriority => 65;

    public WorkLogSaveData Serialize()
    {
        var data = new WorkLogSaveData();
        foreach (var kv in _careerByJob)
        {
            var jobEntry = new WorkLogJobEntry { jobType = kv.Key.ToString() };
            foreach (var placeKv in kv.Value)
            {
                var rec = placeKv.Value;
                jobEntry.workplaces.Add(new WorkPlaceSaveEntry
                {
                    buildingId = rec.BuildingId,
                    buildingDisplayName = rec.BuildingDisplayName,
                    unitsWorked = rec.UnitsWorked,
                    shiftsCompleted = rec.ShiftsCompleted,
                    firstWorkedDay = rec.FirstWorkedDay,
                    lastWorkedDay = rec.LastWorkedDay
                });
            }
            data.jobs.Add(jobEntry);
        }
        return data;
    }

    public void Deserialize(WorkLogSaveData data)
    {
        _careerByJob.Clear();
        _shiftByJobType.Clear();
        _currentShiftScheduledEndTime01 = -1f;
        if (data == null || data.jobs == null) return;
        foreach (var jobEntry in data.jobs)
        {
            if (!Enum.TryParse<JobType>(jobEntry.jobType, out var parsedJobType))
            {
                Debug.LogWarning($"[CharacterWorkLog] Unknown JobType '{jobEntry.jobType}' on load — skipping entry.");
                continue;
            }
            var map = GetOrCreateJobMap(parsedJobType);
            foreach (var wp in jobEntry.workplaces)
            {
                map[wp.buildingId] = new WorkPlaceRecord
                {
                    BuildingId = wp.buildingId,
                    BuildingDisplayName = wp.buildingDisplayName,
                    UnitsWorked = wp.unitsWorked,
                    ShiftsCompleted = wp.shiftsCompleted,
                    FirstWorkedDay = wp.firstWorkedDay,
                    LastWorkedDay = wp.lastWorkedDay
                };
            }
        }
    }

    string ICharacterSaveData.SerializeToJson() => CharacterSaveDataHelper.SerializeToJson(this);
    void ICharacterSaveData.DeserializeFromJson(string json) => CharacterSaveDataHelper.DeserializeFromJson(this, json);
}
```

- [ ] **Step 2: Verify compile (may require `using` for JobType — add if errors appear)**

If `JobType` isn't found, add the correct `using` (check `JobType.cs` for its namespace).

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Character/CharacterWorkLog/CharacterWorkLog.cs
git commit -m "feat(worklog): implement CharacterWorkLog subsystem with shift-window enforcement"
```

---

## Task 10: Expose `Wallet` and `WorkLog` on `Character` Facade

**Files:**
- Modify: `Assets/Scripts/Character/Character.cs`

- [ ] **Step 1: Read the facade's existing field block around line 65 and property block around line 214-241**

You need the pattern used by existing subsystems (e.g., `_characterJob` + `CharacterJob` property using the `TryGet<T>()` capability registry).

- [ ] **Step 2: Add the two serialized fields**

Alongside `[SerializeField] private CharacterJob _characterJob;` (line 65), add:

```csharp
    [SerializeField] private CharacterWallet _characterWallet;
    [SerializeField] private CharacterWorkLog _characterWorkLog;
```

- [ ] **Step 3: Add the two properties**

Alongside the existing `CharacterJob` property (lines 214-241), add:

```csharp
    public CharacterWallet CharacterWallet => TryGet<CharacterWallet>(out var sw) ? sw : _characterWallet;
    public CharacterWorkLog CharacterWorkLog => TryGet<CharacterWorkLog>(out var swl) ? swl : _characterWorkLog;
```

(Name the local variables `sw` and `swl` only if they don't clash; pick unique names in the current file.)

- [ ] **Step 4: Verify compile**

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Character/Character.cs
git commit -m "feat(character): expose Wallet and WorkLog on Character facade"
```

---

## Task 11: Add Wallet + WorkLog Child GameObjects to Character Prefab

**Files:**
- Modify: Unity prefab `Assets/Prefabs/Character.prefab` (name may differ — find the main Character prefab)

This is a **Unity Editor action**, not a code edit. Use the MCP `assets-find` + `assets-prefab-open` tools.

- [ ] **Step 1: Find the Character prefab**

Use `assets-find` with filter `t:Prefab Character` to list candidate prefabs. Identify the one used as the player/NPC base (typically the largest, most subsystem-heavy).

- [ ] **Step 2: Open it for editing**

`assets-prefab-open` on the path.

- [ ] **Step 3: Add two empty child GameObjects**

Name them `CharacterWallet` and `CharacterWorkLog`. Under each, add the corresponding script component (`CharacterWallet` and `CharacterWorkLog`).

- [ ] **Step 4: Assign the Inspector slots**

On the root `Character` component, drag the new child GOs into the two serialized slots added in Task 10 (`_characterWallet`, `_characterWorkLog`).

- [ ] **Step 5: Save the prefab, close the edit mode**

`assets-prefab-save`, then `assets-prefab-close`.

- [ ] **Step 6: Commit**

```bash
git add Assets/Prefabs/Character.prefab  # replace with actual prefab path
git commit -m "feat(character): attach Wallet and WorkLog subsystems to Character prefab"
```

**Smoke test after commit:** enter Play mode, spawn a Character, inspect the hierarchy — `CharacterWallet` and `CharacterWorkLog` children should be present, both `enabled`, and `Character.CharacterWallet` / `Character.CharacterWorkLog` return non-null at runtime (drop a `Debug.Log` temporarily if needed, then remove).

---

## Task 12: Create `WageRateEntry` + `WageRatesSO`

**Files:**
- Create: `Assets/Scripts/World/Jobs/Wages/WageRateEntry.cs`
- Create: `Assets/Scripts/World/Jobs/Wages/WageRatesSO.cs`

- [ ] **Step 1: Create `WageRateEntry`**

```csharp
using System;

[Serializable]
public class WageRateEntry
{
    public JobType jobType;
    public int pieceRate;         // 0 for fixed-wage jobs
    public int minimumShiftWage;  // 0 for fixed-wage jobs
    public int fixedShiftWage;    // 0 for piece-work jobs
}
```

- [ ] **Step 2: Create `WageRatesSO`**

```csharp
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "MWI/Jobs/Wage Rates")]
public class WageRatesSO : ScriptableObject
{
    [SerializeField] private List<WageRateEntry> _entries = new List<WageRateEntry>();

    public WageRateEntry GetDefaults(JobType jobType)
    {
        return _entries.Find(e => e.jobType == jobType);
    }

    public IReadOnlyList<WageRateEntry> Entries => _entries;
}
```

- [ ] **Step 3: Verify compile, commit**

```bash
git add Assets/Scripts/World/Jobs/Wages/WageRateEntry.cs Assets/Scripts/World/Jobs/Wages/WageRatesSO.cs
git commit -m "feat(wages): add WageRatesSO designer asset + WageRateEntry"
```

---

## Task 13: Author `WageRates.asset` with Default Values

**Files:**
- Create: `Assets/ScriptableObjects/Jobs/WageRates.asset`

- [ ] **Step 1: Create the folder if missing**

Use `assets-create-folder` for `Assets/ScriptableObjects/Jobs/`.

- [ ] **Step 2: Create the asset**

In Unity Editor: `Project > Create > MWI > Jobs > Wage Rates`. Name it `WageRates`. Drop under `Assets/ScriptableObjects/Jobs/`.

- [ ] **Step 3: Populate entries**

Add one entry per JobType — reasonable placeholder values (designer will tune later):

| JobType | pieceRate | minimumShiftWage | fixedShiftWage |
|---|---|---|---|
| Harvester | 2 | 10 | 0 |
| Crafter | 3 | 10 | 0 |
| Blacksmith | 3 | 10 | 0 |
| Transporter | 2 | 10 | 0 |
| Shop | 0 | 0 | 15 |
| Vendor | 0 | 0 | 15 |
| Barman | 0 | 0 | 12 |
| Server | 0 | 0 | 10 |
| LogisticsManager | 0 | 0 | 20 |

(Add any additional JobType enum members not in this table with placeholder 0s — document in SKILL.md.)

- [ ] **Step 4: Commit**

```bash
git add Assets/ScriptableObjects/Jobs/WageRates.asset Assets/ScriptableObjects/Jobs/WageRates.asset.meta
git commit -m "feat(wages): author WageRates SO asset with placeholder values per JobType"
```

---

## Task 14: Add `IWagePayer` + `MintedWagePayer`

**Files:**
- Create: `Assets/Scripts/World/Jobs/Wages/IWagePayer.cs`
- Create: `Assets/Scripts/World/Jobs/Wages/MintedWagePayer.cs`

- [ ] **Step 1: Create `IWagePayer`**

```csharp
using MWI.Economy;

public interface IWagePayer
{
    /// <summary>
    /// Pay <paramref name="coins"/> of <paramref name="currency"/> to <paramref name="worker"/>.
    /// Server-authoritative — callers must be on the server.
    /// </summary>
    void PayWages(Character worker, CurrencyId currency, int coins, string source);
}
```

- [ ] **Step 2: Create `MintedWagePayer`**

```csharp
using MWI.Economy;
using UnityEngine;

/// <summary>
/// Default wage payer: mints coins from nothing. No treasury, no insolvency.
/// Drop-in replacement target: BuildingTreasuryWagePayer (future).
/// </summary>
public class MintedWagePayer : IWagePayer
{
    public void PayWages(Character worker, CurrencyId currency, int coins, string source)
    {
        if (worker == null) { Debug.LogError("[MintedWagePayer] null worker"); return; }
        if (worker.CharacterWallet == null)
        {
            Debug.LogError($"[MintedWagePayer] {worker.CharacterName} has no CharacterWallet — dropping wage.");
            return;
        }
        if (coins <= 0) return;
        worker.CharacterWallet.AddCoins(currency, coins, source);
    }
}
```

- [ ] **Step 3: Verify compile, commit**

```bash
git add Assets/Scripts/World/Jobs/Wages/IWagePayer.cs Assets/Scripts/World/Jobs/Wages/MintedWagePayer.cs
git commit -m "feat(wages): add IWagePayer contract + MintedWagePayer default impl"
```

---

## Task 15: Implement `WageSystemService` Singleton

**Files:**
- Create: `Assets/Scripts/World/Jobs/Wages/WageSystemService.cs`

- [ ] **Step 1: Create the service**

```csharp
using MWI.Economy;
using MWI.Jobs.Wages;
using UnityEngine;

/// <summary>
/// Scene-bound singleton. Holds the designer-authored WageRatesSO and the active IWagePayer.
/// Computes shift wages and dispatches to the payer. No per-frame or per-character state.
/// </summary>
public class WageSystemService : MonoBehaviour
{
    public static WageSystemService Instance { get; private set; }

    [SerializeField] private WageRatesSO _defaultRates;
    [Tooltip("If true, uses MintedWagePayer (free coins from nothing). Set false to swap in a future BuildingTreasuryWagePayer.")]
    [SerializeField] private bool _useMintedPayer = true;

    private IWagePayer _payer;

    public WageRatesSO DefaultRates => _defaultRates;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        _payer = _useMintedPayer ? (IWagePayer)new MintedWagePayer() : null;
        if (_payer == null)
            Debug.LogError("[WageSystemService] no IWagePayer configured — wages will not be paid.");
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Copy defaults from WageRatesSO into a fresh JobAssignment. Called at hire time.
    /// </summary>
    public void SeedAssignmentDefaults(JobAssignment assignment, JobType jobType, CurrencyId currency)
    {
        if (assignment == null) return;
        assignment.Currency = currency;
        if (_defaultRates == null) return;
        var entry = _defaultRates.GetDefaults(jobType);
        if (entry == null) return;
        assignment.PieceRate = entry.pieceRate;
        assignment.MinimumShiftWage = entry.minimumShiftWage;
        assignment.FixedShiftWage = entry.fixedShiftWage;
    }

    /// <summary>
    /// Compute and pay the shift wage. Called by CommercialBuilding at punch-out.
    /// Returns paid amount (0 if no payer or zero wage).
    /// </summary>
    public int ComputeAndPayShiftWage(
        Character worker, JobAssignment assignment, ShiftSummary summary,
        float scheduledShiftHours, float hoursWorked)
    {
        if (worker == null || assignment == null) return 0;
        if (_payer == null) return 0;

        int wage = IsPieceWork(assignment.JobType)
            ? WageCalculator.ComputePieceWorkWage(
                hoursWorked, scheduledShiftHours,
                assignment.MinimumShiftWage, assignment.PieceRate, summary.ShiftUnits)
            : WageCalculator.ComputeFixedWage(
                hoursWorked, scheduledShiftHours, assignment.FixedShiftWage);

        if (wage <= 0) return 0;

        string source = $"Wage@{assignment.Workplace?.BuildingId}";
        _payer.PayWages(worker, assignment.Currency, wage, source);
        return wage;
    }

    private static bool IsPieceWork(JobType jobType)
    {
        return jobType == JobType.Harvester
            || jobType == JobType.Crafter
            || jobType == JobType.Blacksmith
            || jobType == JobType.Transporter;
    }
}
```

**Note:** `assignment.JobType`, `assignment.PieceRate`, etc. are added in Task 16. Expect a compile error on this file until Task 16 commits. You can optionally skip verifying compile until after Task 16 — or comment out the body temporarily. Prefer the former: commit Task 16 immediately after Task 15.

- [ ] **Step 2: Commit (compile error will be fixed in Task 16)**

```bash
git add Assets/Scripts/World/Jobs/Wages/WageSystemService.cs
git commit -m "feat(wages): add WageSystemService singleton with compute-and-pay API"
```

---

## Task 16: Extend `JobAssignment` with Wage Fields + `SetWage`

**Files:**
- Modify: `Assets/Scripts/Character/CharacterJob/CharacterJob.cs`

- [ ] **Step 1: Read lines 5-11 of the file**

```csharp
[System.Serializable]
public class JobAssignment {
    [SerializeReference] public Job AssignedJob;
    public CommercialBuilding Workplace;
    public List<ScheduleEntry> WorkScheduleEntries = new List<ScheduleEntry>();
}
```

- [ ] **Step 2: Replace with wage-augmented version**

```csharp
[System.Serializable]
public class JobAssignment
{
    [SerializeReference] public Job AssignedJob;
    public CommercialBuilding Workplace;
    public List<ScheduleEntry> WorkScheduleEntries = new List<ScheduleEntry>();

    // --- Wage fields (NEW) ---
    // Seeded from WageRatesSO at hire time; owner may edit at runtime via SetWage.
    public MWI.Economy.CurrencyId Currency;
    public int PieceRate;
    public int MinimumShiftWage;
    public int FixedShiftWage;

    // Cached for convenience — derived from AssignedJob.Type, never serialized.
    public JobType JobType => AssignedJob != null ? AssignedJob.Type : JobType.None;

    public event System.Action<JobAssignment> OnWageChanged;

    /// <summary>
    /// Server-authoritative wage edit. Gated by the caller (owner / community leader).
    /// </summary>
    public void SetWage(int? pieceRate = null, int? minimumShift = null, int? fixedShift = null)
    {
        if (pieceRate.HasValue) PieceRate = System.Math.Max(0, pieceRate.Value);
        if (minimumShift.HasValue) MinimumShiftWage = System.Math.Max(0, minimumShift.Value);
        if (fixedShift.HasValue) FixedShiftWage = System.Math.Max(0, fixedShift.Value);
        OnWageChanged?.Invoke(this);
    }
}
```

- [ ] **Step 3: Verify compile — both this file and `WageSystemService` should now compile**

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Character/CharacterJob/CharacterJob.cs
git commit -m "feat(job): extend JobAssignment with wage fields + SetWage mutation"
```

---

## Task 17: Extend `JobAssignmentSaveEntry` with Wage Fields

**Files:**
- Modify: `Assets/Scripts/Character/SaveLoad/ProfileSaveData/JobSaveData.cs`

- [ ] **Step 1: Replace the file contents**

```csharp
using System.Collections.Generic;

[System.Serializable]
public class JobSaveData
{
    public List<JobAssignmentSaveEntry> jobs = new List<JobAssignmentSaveEntry>();
}

[System.Serializable]
public class JobAssignmentSaveEntry
{
    public string jobType;
    public string workplaceBuildingId;

    // Wage fields (persist owner edits across sessions).
    public int currencyId;
    public int pieceRate;
    public int minimumShiftWage;
    public int fixedShiftWage;
}
```

- [ ] **Step 2: Find `CharacterJob.Serialize()` and `Deserialize(JobSaveData)` (lines ~274 / ~304). Update them to round-trip the new fields.**

Search [CharacterJob.cs](../../../Assets/Scripts/Character/CharacterJob/CharacterJob.cs) for `JobAssignmentSaveEntry` construction. At the construction site inside `Serialize()`, add:

```csharp
    currencyId = assignment.Currency.Id,
    pieceRate = assignment.PieceRate,
    minimumShiftWage = assignment.MinimumShiftWage,
    fixedShiftWage = assignment.FixedShiftWage,
```

At the `JobAssignment` creation site inside `Deserialize`, after setting `AssignedJob` and `Workplace`, add:

```csharp
    newAssignment.Currency = new MWI.Economy.CurrencyId(entry.currencyId);
    newAssignment.PieceRate = entry.pieceRate;
    newAssignment.MinimumShiftWage = entry.minimumShiftWage;
    newAssignment.FixedShiftWage = entry.fixedShiftWage;
```

(Variable names may differ — use whatever the existing code uses. If the restored assignment ends up with all-zero wage fields, the runtime will re-seed defaults at the next job re-take / re-assignment; document this in the SKILL.md.)

- [ ] **Step 3: Verify compile**

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Character/SaveLoad/ProfileSaveData/JobSaveData.cs Assets/Scripts/Character/CharacterJob/CharacterJob.cs
git commit -m "feat(save): round-trip wage fields through JobAssignmentSaveEntry"
```

---

## Task 18: Seed Default Wages at Hire Time

**Files:**
- Modify: `Assets/Scripts/Character/CharacterJob/CharacterJob.cs` (method `TakeJob` around line 76)

- [ ] **Step 1: Inspect `TakeJob`**

Open [CharacterJob.cs](../../../Assets/Scripts/Character/CharacterJob/CharacterJob.cs) at the `TakeJob(Job job, CommercialBuilding building)` method. Find where a new `JobAssignment` is created.

- [ ] **Step 2: After assignment construction (and after setting `AssignedJob`, `Workplace`, `WorkScheduleEntries`), add seeding**

```csharp
    // Seed wage fields from designer-authored defaults.
    if (WageSystemService.Instance != null)
    {
        var currency = building != null ? building.PaymentCurrency : MWI.Economy.CurrencyId.Default;
        WageSystemService.Instance.SeedAssignmentDefaults(newAssignment, job.Type, currency);
    }
```

- [ ] **Step 3: Add `PaymentCurrency` placeholder to `CommercialBuilding`**

Open [CommercialBuilding.cs](../../../Assets/Scripts/World/Buildings/CommercialBuilding.cs). Near the top-level property block, add:

```csharp
    /// <summary>
    /// Currency this building pays wages in. Placeholder until the Kingdom system lands —
    /// currently always Default. When Kingdom arrives, resolve from kingdom ownership.
    /// </summary>
    public virtual MWI.Economy.CurrencyId PaymentCurrency => MWI.Economy.CurrencyId.Default;
```

- [ ] **Step 4: Verify compile, commit**

```bash
git add Assets/Scripts/Character/CharacterJob/CharacterJob.cs Assets/Scripts/World/Buildings/CommercialBuilding.cs
git commit -m "feat(job): seed JobAssignment wages from WageRatesSO at hire time"
```

---

## Task 19: Wire `WageSystemService` into a Scene Bootstrap GameObject

**Files:**
- Modify: the main world/bootstrap scene (most likely `Assets/Scenes/Main.unity` or equivalent — search for the scene that already hosts `MapController` / `GameManager`).

This is a **Unity Editor action**.

- [ ] **Step 1: Open the world bootstrap scene**

Use `scene-list-opened` + `scene-open` to bring it up.

- [ ] **Step 2: Create a new empty GameObject named `WageSystemService`**

Add the `WageSystemService` component to it.

- [ ] **Step 3: Assign the `WageRatesSO` asset to the `_defaultRates` slot**

Drag `Assets/ScriptableObjects/Jobs/WageRates.asset` onto the field.

- [ ] **Step 4: Save the scene, commit**

```bash
git add Assets/Scenes/<SceneFile>.unity
git commit -m "feat(wages): add WageSystemService singleton GameObject to bootstrap scene"
```

**Smoke test:** enter Play mode. Console should show no errors. A `Debug.Log(WageSystemService.Instance != null)` dropped temporarily should print `true`.

---

## Task 20: CommercialBuilding Punch-In Hook

**Files:**
- Modify: `Assets/Scripts/World/Buildings/CommercialBuilding.cs` (around line 444)

- [ ] **Step 1: Locate `OnWorkerPunchIn` (~line 444)**

- [ ] **Step 2: Add a punch-in dictionary to the class**

Near the top of the class (alongside other runtime fields):

```csharp
    // Maps worker -> (shift start time in hours, scheduled shift end time-of-day 0..1).
    private readonly Dictionary<Character, (float startHour, float scheduledEndTime01)> _punchInState
        = new Dictionary<Character, (float, float)>();
```

- [ ] **Step 3: Augment `OnWorkerPunchIn`**

At the start of the method body (before any existing logic):

```csharp
    float nowHours = MWI.Time.TimeManager.Instance != null
        ? MWI.Time.TimeManager.Instance.CurrentTime01 * 24f
        : 0f;

    // Resolve scheduled shift end (0..1) from the worker's active schedule entries for this assignment.
    float scheduledEndTime01 = ResolveScheduledEndTime01(worker, assignment);
    _punchInState[worker] = (nowHours, scheduledEndTime01);

    // Notify WorkLog — creates WorkPlaceRecord on first shift, resets shift counter.
    if (worker.CharacterWorkLog != null)
    {
        worker.CharacterWorkLog.OnPunchIn(
            assignment.JobType,
            BuildingId,
            BuildingDisplayName,
            scheduledEndTime01);
    }
```

- [ ] **Step 4: Add the helper `ResolveScheduledEndTime01`**

```csharp
    private float ResolveScheduledEndTime01(Character worker, JobAssignment assignment)
    {
        if (assignment == null || assignment.WorkScheduleEntries == null) return 1f;
        // Find the schedule entry that covers "now" — pick the latest endHour in the active entries.
        float latestEnd = 0f;
        foreach (var entry in assignment.WorkScheduleEntries)
        {
            float endH = entry.endHour; // 0..23
            float endAs01 = Mathf.Clamp01(endH / 24f);
            if (endAs01 > latestEnd) latestEnd = endAs01;
        }
        return latestEnd > 0f ? latestEnd : 1f;
    }
```

(If `ScheduleEntry` member names differ, adapt. Inspect `ScheduleEntry.cs` before writing; keep the logic shape.)

- [ ] **Step 5: Verify compile (may need `using System.Collections.Generic;` / `using UnityEngine;`)**

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/World/Buildings/CommercialBuilding.cs
git commit -m "feat(building): CommercialBuilding tracks punch-in state for wage calculation"
```

---

## Task 21: CommercialBuilding Punch-Out Hook (Wage Payment)

**Files:**
- Modify: `Assets/Scripts/World/Buildings/CommercialBuilding.cs` (around line 493)

- [ ] **Step 1: Locate `OnWorkerPunchOut` (~line 493) and the `WorkerEndingShift` caller (~line 481)**

- [ ] **Step 2: Augment `OnWorkerPunchOut`**

Inside the method, add at the start:

```csharp
    // Finalize WorkLog shift + compute/pay wage.
    if (!_punchInState.TryGetValue(worker, out var state))
    {
        Debug.LogWarning($"[CommercialBuilding] {worker?.CharacterName} punched out without recorded punch-in.");
    }
    else
    {
        float nowHours = MWI.Time.TimeManager.Instance != null
            ? MWI.Time.TimeManager.Instance.CurrentTime01 * 24f
            : state.startHour;

        float scheduledShiftHours = (state.scheduledEndTime01 * 24f) - state.startHour;
        if (scheduledShiftHours < 0f) scheduledShiftHours += 24f; // overnight shift safety

        float scheduledEndHours = state.startHour + scheduledShiftHours;
        float hoursWorked = Mathf.Max(0f, Mathf.Min(nowHours, scheduledEndHours) - state.startHour);

        if (worker.CharacterWorkLog != null)
        {
            var summary = worker.CharacterWorkLog.FinalizeShift(assignment.JobType, BuildingId);
            if (WageSystemService.Instance != null)
            {
                WageSystemService.Instance.ComputeAndPayShiftWage(
                    worker, assignment, summary, scheduledShiftHours, hoursWorked);
            }
        }

        _punchInState.Remove(worker);
    }
```

- [ ] **Step 3: Verify compile**

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/World/Buildings/CommercialBuilding.cs
git commit -m "feat(building): CommercialBuilding pays shift wage on worker punch-out"
```

**Smoke test after commit:**
1. Enter Play mode with an NPC assigned to a job.
2. Advance time to their shift, wait for punch-in, then advance to after shift.
3. Verify: Console shows no errors. Worker's `Character.CharacterWallet.GetBalance(CurrencyId.Default)` > 0. `Character.CharacterWorkLog.GetCareerUnits(JobType.Harvester)` matches number of deposits during shift.

---

## Task 22: Harvester Credit Hook — `GoapAction_DepositResources`

**Files:**
- Modify: `Assets/Scripts/AI/GOAP/Actions/GoapAction_DepositResources.cs`

- [ ] **Step 1: Open the file and find the deposit site**

Look for the line where an item quantity is added to the building's inventory / stock. This is the credit point.

- [ ] **Step 2: Before the actual deposit, compute the deficit; after the deposit, credit**

Replace the deposit section with something shaped like:

```csharp
    // BEFORE deposit: query the building's current deficit for this item.
    int deficitBefore = ComputeDeficitFor(targetBuilding, item);

    // ... existing deposit logic (adds `quantity` of `item` to targetBuilding) ...

    // AFTER deposit: credit the harvester with the deficit-bounded portion.
    int credited = MWI.Jobs.Wages.HarvesterCreditCalculator.GetCreditedAmount(quantity, deficitBefore);
    if (credited > 0 && character != null && character.CharacterWorkLog != null && targetBuilding != null)
    {
        character.CharacterWorkLog.LogShiftUnit(
            JobType.Harvester,
            targetBuilding.BuildingId,
            credited);
    }
```

- [ ] **Step 3: Add the `ComputeDeficitFor` helper (in this file or a static utility)**

```csharp
    private static int ComputeDeficitFor(CommercialBuilding building, ItemSO item)
    {
        if (building == null || item == null) return 0;
        var provider = building as IStockProvider;
        if (provider == null) return 0;
        int minStock = 0;
        foreach (var target in provider.GetStockTargets())
        {
            if (target.Item == item) { minStock = target.MinStock; break; }
        }
        int currentStock = building.InventoryCountOf(item); // name may differ — check existing building API
        int inFlight = building.InFlightIncomingCount(item); // name may differ — fallback to 0 if not present
        int deficit = minStock - currentStock - inFlight;
        return System.Math.Max(0, deficit);
    }
```

**Note:** `InventoryCountOf` / `InFlightIncomingCount` may have different names in the existing codebase. Inspect `CommercialBuilding` / `BuildingLogisticsManager` before wiring. If `inFlight` is unavailable, use 0 and note the gap in SKILL.md as a known-overcredit edge case.

- [ ] **Step 4: Verify compile**

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/AI/GOAP/Actions/GoapAction_DepositResources.cs
git commit -m "feat(job): harvester deposits credit WorkLog with deficit-bounded amount"
```

---

## Task 23: Transporter Credit Hook — `JobTransporter.NotifyDeliveryProgress`

**Files:**
- Modify: `Assets/Scripts/World/Jobs/TransportJobs/JobTransporter.cs` (line 340-364)

- [ ] **Step 1: Open and locate `NotifyDeliveryProgress`**

- [ ] **Step 2: Inside the method, after the existing `manager.UpdateTransportOrderProgress(...)` call**

Add:

```csharp
    // Credit the worker for the delivery. Credit goes to the worker's employer (this.Workplace),
    // not to the destination building — "where I worked" means "who employs me".
    if (amount > 0 && Worker != null && Worker.CharacterWorkLog != null && _workplace != null)
    {
        Worker.CharacterWorkLog.LogShiftUnit(
            JobType.Transporter,
            _workplace.BuildingId,
            amount);
    }
```

**Note:** variable names (`Worker`, `_workplace`, `amount`) — replace with whatever the actual file uses. Inspect lines 340-364 of the file before writing.

- [ ] **Step 3: Verify compile, commit**

```bash
git add Assets/Scripts/World/Jobs/TransportJobs/JobTransporter.cs
git commit -m "feat(job): transporter deliveries credit WorkLog at employer building"
```

---

## Task 24: Crafter Credit Hook — `JobCrafter` (and subclasses)

**Files:**
- Modify: `Assets/Scripts/World/Jobs/CraftingJobs/JobCrafter.cs` (and any subclass that overrides the "craft complete" path — inspect first)

- [ ] **Step 1: Find the craft-completion path**

Search the `JobCrafter` family for where a `CraftingOrder` is decremented / completed (likely in an `Execute()` override or a GOAP action result). Identify the moment a single crafted item is added to stock/output.

- [ ] **Step 2: Insert the credit call at every completion site**

For each site, add:

```csharp
    if (Worker != null && Worker.CharacterWorkLog != null && _workplace != null)
    {
        Worker.CharacterWorkLog.LogShiftUnit(
            JobType.Crafter,
            _workplace.BuildingId,
            1);
    }
```

For `JobBlacksmith` (if it has its own completion site), use `JobType.Blacksmith` instead of `JobType.Crafter`.

**Critical:** if you cannot identify a single authoritative completion hook, bundle discovery into this task and document the chosen site at the top of this file in a comment block. Do not guess — get the hook right.

- [ ] **Step 3: Verify compile, commit**

```bash
git add Assets/Scripts/World/Jobs/CraftingJobs/
git commit -m "feat(job): crafter/blacksmith order completions credit WorkLog"
```

---

## Task 25: Extend `CommercialBuilding` with `BuildingDisplayName`

**Files:**
- Modify: `Assets/Scripts/World/Buildings/CommercialBuilding.cs` (only if the property doesn't already exist)

The WorkLog denormalizes a `BuildingDisplayName`. Confirm `CommercialBuilding` (or its base `Building`) already exposes such a property. If not, add one.

- [ ] **Step 1: Grep for `BuildingDisplayName` / `DisplayName` in the buildings folder**

If present, skip this task (and the `OnPunchIn` call's third argument remains correct).

- [ ] **Step 2: If absent, add a virtual property**

```csharp
    [SerializeField] private string _buildingDisplayName;
    public virtual string BuildingDisplayName =>
        string.IsNullOrEmpty(_buildingDisplayName) ? gameObject.name : _buildingDisplayName;
```

Also add an Inspector slot so designers can set a nice name (e.g., "Bob's Smithy") instead of the GO name.

- [ ] **Step 3: Verify compile, commit if changed**

```bash
git add Assets/Scripts/World/Buildings/CommercialBuilding.cs
git commit -m "feat(building): add BuildingDisplayName for WorkLog history denormalization"
```

(Skip commit if the property already existed.)

---

## Task 26: Macro-Simulation Bridge — Accrue WorkLog for Hibernated NPCs

**Files:**
- Modify: `Assets/Scripts/World/MapSystem/MacroSimulator.cs` (Inventory Yields pass, lines 142-167)

- [ ] **Step 1: Locate the yield pass inside the per-NPC loop (lines 142-167)**

Below the `pool.CurrentAmount += yieldAmount;` line (~162), add work-log accrual.

- [ ] **Step 2: Insert accrual**

```csharp
    // Worker performance accrual — hibernated workers get lifetime credit for their offline yield.
    // Attendance is assumed 1.0 (no absence simulation in v1).
    if (npc.CharacterWorkLog != null && npc.SavedJobType != JobType.None && community != null)
    {
        // BuildingId resolution: the NPC is associated with a workplace via their saved JobAssignment.
        string workplaceBuildingId = npc.SavedWorkplaceBuildingId;
        string workplaceDisplayName = npc.SavedWorkplaceDisplayName ?? workplaceBuildingId;
        if (!string.IsNullOrEmpty(workplaceBuildingId))
        {
            // Note: OnPunchIn isn't called — we're offline. Use LogShiftUnit only, which creates
            // a WorkPlaceRecord defensively. Scheduled-shift-window check is bypassed because
            // hibernated time is fully inside the shift by definition (no overtime offline).
            npc.CharacterWorkLog.LogShiftUnit(
                npc.SavedJobType,
                workplaceBuildingId,
                yieldAmount);
        }
    }
```

**Note:** `npc.SavedWorkplaceBuildingId` / `npc.SavedWorkplaceDisplayName` are likely on `HibernatedNPCData` — inspect the type and use whatever name matches. If `SavedWorkplaceDisplayName` isn't tracked, plumb it through (add a field to `HibernatedNPCData` and populate at hibernate time).

- [ ] **Step 3: Verify compile**

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/World/MapSystem/MacroSimulator.cs
git commit -m "feat(macrosim): hibernated NPC work accrues into CharacterWorkLog on wake"
```

---

## Task 27: Owner-Gated Wage Edit — `CharacterJob.TrySetAssignmentWage`

**Files:**
- Modify: `Assets/Scripts/Character/CharacterJob/CharacterJob.cs`

- [ ] **Step 1: Add an owner-gated helper on `CharacterJob`**

```csharp
    /// <summary>
    /// Edit a worker's wage. Must be called on the server. The caller Character must be
    /// the worker's employer (building owner) or the community leader.
    /// Returns true if applied, false if caller isn't authorized or assignment not found.
    /// </summary>
    public bool TrySetAssignmentWage(
        Character caller,
        JobAssignment assignment,
        int? pieceRate = null,
        int? minimumShift = null,
        int? fixedShift = null)
    {
        if (caller == null || assignment == null || assignment.Workplace == null) return false;
        if (!assignment.Workplace.IsAuthorizedToManageWorkers(caller)) return false;
        assignment.SetWage(pieceRate, minimumShift, fixedShift);
        return true;
    }
```

- [ ] **Step 2: Ensure `CommercialBuilding.IsAuthorizedToManageWorkers(Character)` exists**

Grep existing code. The `AskForJob` path already gates by Owner or CommunityLeader — if that check is inlined there, extract it into a reusable method:

```csharp
    public virtual bool IsAuthorizedToManageWorkers(Character caller)
    {
        if (caller == null) return false;
        if (caller == Owner) return true;
        if (Community != null && Community.Leader == caller) return true;
        return false;
    }
```

(Names `Owner` / `Community.Leader` may differ — use existing properties.)

- [ ] **Step 3: Verify compile, commit**

```bash
git add Assets/Scripts/Character/CharacterJob/CharacterJob.cs Assets/Scripts/World/Buildings/CommercialBuilding.cs
git commit -m "feat(wages): add owner-gated TrySetAssignmentWage on CharacterJob"
```

---

## Task 28: Manual Smoke-Test Pass (Multiplayer Matrix)

No code. This task is a checklist for validating the network scenarios called out in the spec.

- [ ] **Step 1: Host↔Client — Host's wallet updates visible to Client**

1. Launch as Host. Spawn NPC worker. Launch second instance as Client, connect.
2. Trigger a shift-end on the NPC.
3. On Client: inspect the NPC's wallet via a debug UI or `Debug.Log`. Balance should match Host.

- [ ] **Step 2: Host↔Client — Wage edit by Host propagates**

1. As Host owner, call `CharacterJob.TrySetAssignmentWage(ownerChar, assignment, fixedShift: 30)`.
2. Client inspects the assignment — wage should read 30.

- [ ] **Step 3: Client-initiated wage edit routes through server**

(If you've not wired a `ServerRpc` yet, skip with a `TODO` in SKILL.md — manual owner edits via dev UI are Host-only for v1. Document this explicitly.)

- [ ] **Step 4: Host↔NPC — NPC earns wage, balance displays on Host**

Already covered by Step 1.

- [ ] **Step 5: Late-joiner — Client joins mid-shift**

1. Host starts a shift, Client joins.
2. Client's view of the NPC's wallet should sync to the current balance (not zero).

**Important:** if Step 5 fails, the wallet must upgrade to a `NetworkList<CurrencyBalanceEntry>` (or similar NetworkVariable-based mechanism). The current `Dictionary + ClientRpc on change` approach does not serve late-joiners. Document the follow-up task in SKILL.md as a known gap.

- [ ] **Step 6: Commit a notes file if needed**

No commit required if everything passes. If a gap is found, add a `LIMITATIONS.md` note or an entry in the SKILL.md and commit.

---

## Task 29: Write `character-wallet/SKILL.md`

**Files:**
- Create: `.agent/skills/character-wallet/SKILL.md`

- [ ] **Step 1: Copy the shape of an existing skill file**

Read any existing SKILL.md (e.g., `.agent/skills/job_system/SKILL.md`) for template structure.

- [ ] **Step 2: Write the content**

Include sections:
1. **Purpose** — one paragraph.
2. **Public API** — every method on `CharacterWallet`.
3. **Events** — `OnBalanceChanged`, `OnCoinsReceived`.
4. **Save/load** — SaveKey, LoadPriority, DTO shape.
5. **Networking** — server-authoritative; `AddCoins`/`RemoveCoins` must be called on server; `ClientRpc` broadcast mechanism. **Known v1 limitation:** late-joiner clients do not receive initial balance state — they only see updates that fire after they join. Document explicitly. Resolution path: upgrade to `NetworkList<CurrencyBalanceEntry>` for full late-join sync (out of scope for this spec).
6. **Multi-currency model** — `CurrencyId` placeholder; Kingdom evolution.
7. **Integration points** — `MintedWagePayer` → `CharacterWallet.AddCoins`.
8. **Gotchas** — no exchange, no insolvency (MintedWagePayer), late-joiner gap above.

- [ ] **Step 3: Commit**

```bash
git add .agent/skills/character-wallet/SKILL.md
git commit -m "docs(skill): add character-wallet SKILL.md"
```

---

## Task 30: Write `character-worklog/SKILL.md`

**Files:**
- Create: `.agent/skills/character-worklog/SKILL.md`

Cover: counter taxonomy (shift / career / career-per-place), `OnPunchIn` / `LogShiftUnit` / `FinalizeShift` call contract, shift-window enforcement rule, passthrough `CurrentAssignments`, DTO shape, integration points (each job type's hook), UI consumption (`GetAllHistory`, `GetWorkplaces`), BuildingDisplayName denormalization rationale, macro-sim accrual bridge.

**Required network section:** `CharacterWorkLog` is **server-only state in v1** — there is no `NetworkVariable` or `ClientRpc` sync. The data lives only on the host's instance. This is acceptable because no UI in v1 displays remote characters' work history. Resolution path when a multi-client UI is needed: serialize a periodic `WorkLogSyncPayload` snapshot via `ClientRpc` from `FinalizeShift`, or migrate to a `NetworkList`-backed structure. Document explicitly under **Known v1 limitations**.

- [ ] **Step 1: Write the file**

- [ ] **Step 2: Commit**

```bash
git add .agent/skills/character-worklog/SKILL.md
git commit -m "docs(skill): add character-worklog SKILL.md"
```

---

## Task 31: Write `wage-system/SKILL.md`

**Files:**
- Create: `.agent/skills/wage-system/SKILL.md`

Cover: wage formulas (piece-work + fixed), shift-ratio proration + overtime cap, `WageRatesSO` (how to tune), `IWagePayer` contract + `MintedWagePayer`, `WageSystemService.SeedAssignmentDefaults` / `ComputeAndPayShiftWage` call flow, owner-gated `TrySetAssignmentWage`, `PaymentCurrency` resolution placeholder, Kingdom evolution, deficit-bounded harvest credit delegation to `HarvesterCreditCalculator`.

- [ ] **Step 1: Write the file**

- [ ] **Step 2: Commit**

```bash
git add .agent/skills/wage-system/SKILL.md
git commit -m "docs(skill): add wage-system SKILL.md"
```

---

## Task 32: Update Existing SKILL.md Files

**Files:**
- Modify: `.agent/skills/job_system/SKILL.md` — add a "Wage hooks" section linking to `wage-system/SKILL.md` and noting the new `TakeJob` hire-time seed + punch-out payment.
- Modify: `.agent/skills/logistics_cycle/SKILL.md` — note harvester credit is now deficit-bounded at the deposit action; link to `wage-system/SKILL.md`.
- Modify: `.agent/skills/save-load-system/SKILL.md` — add `CharacterWallet` (priority 35, SaveKey `CharacterWallet`) and `CharacterWorkLog` (priority 65, SaveKey `CharacterWorkLog`) to the subsystem registry.

- [ ] **Step 1: Make each edit (three files)**

- [ ] **Step 2: Commit**

```bash
git add .agent/skills/job_system/SKILL.md .agent/skills/logistics_cycle/SKILL.md .agent/skills/save-load-system/SKILL.md
git commit -m "docs(skills): cross-link wage-system in existing SKILL.md files"
```

---

## Task 33: Write `wiki/systems/worker-wages-and-performance.md`

**Files:**
- Create: `wiki/systems/worker-wages-and-performance.md`

Follow [wiki/CLAUDE.md](../../wiki/CLAUDE.md) rules for frontmatter, sections, sources.

- [ ] **Step 1: Read wiki/CLAUDE.md for frontmatter and the 10 required sections**

- [ ] **Step 2: Read any recent wiki system page for shape**

- [ ] **Step 3: Write the page**

Required sections: Purpose, Responsibilities, Key classes / files, Public API, Data flow, Dependencies, State & persistence, Gotchas, Open questions, Change log (first entry: `- 2026-04-22 — initial creation — claude`).

Use `Sources` section to link into the SKILL.md files (procedures live there, not duplicated here).

- [ ] **Step 4: Commit**

```bash
git add wiki/systems/worker-wages-and-performance.md
git commit -m "docs(wiki): add worker-wages-and-performance system page"
```

---

## Task 34: Update Existing Wiki Pages

**Files:**
- Modify: `wiki/systems/jobs-and-logistics.md` — bump `updated:` date, append change-log entry, add `worker-wages-and-performance` to `related`.
- Modify: `wiki/systems/commercial-building.md` — same.
- Modify: `wiki/systems/character-job.md` — same, note new `SetWage` on JobAssignment.
- Modify: `wiki/INDEX.md` — add one line pointing to the new page.

- [ ] **Step 1: Make each edit**

Change-log entry format: `- 2026-04-22 — add wage + worklog integration — claude`.

- [ ] **Step 2: Commit**

```bash
git add wiki/
git commit -m "docs(wiki): cross-link worker-wages-and-performance from job/logistics/building pages"
```

---

## Task 35: Update Specialized Agents

**Files:**
- Modify: `.claude/agents/npc-ai-specialist.md`
- Modify: `.claude/agents/building-furniture-specialist.md`

Each agent gains awareness of the new systems per rule #29.

- [ ] **Step 1: `npc-ai-specialist.md` — add to the domain list**

> - Worker wages & performance: `CharacterWallet` (per-currency balances), `CharacterWorkLog` (per-JobType / per-workplace lifetime counters, shift-window enforcement). Credit hooks in `GoapAction_DepositResources` (harvester), `JobTransporter.NotifyDeliveryProgress`, and the crafter/blacksmith completion path.

- [ ] **Step 2: `building-furniture-specialist.md` — add to the domain list**

> - Worker wages & performance: `CommercialBuilding` punch-in/out pays shift wages via `WageSystemService.ComputeAndPayShiftWage`. Hire-time wage defaults are seeded from `WageRatesSO` at `TakeJob`. Owner-gated wage edit via `CharacterJob.TrySetAssignmentWage` → `JobAssignment.SetWage`.

- [ ] **Step 3: Commit**

```bash
git add .claude/agents/npc-ai-specialist.md .claude/agents/building-furniture-specialist.md
git commit -m "docs(agents): extend npc-ai-specialist and building-furniture-specialist with wage awareness"
```

---

## Task 36: Final Verification Pass

No code changes. Walk through the verification matrix one more time.

- [ ] **Step 1: Run EditMode tests**

`Test Runner > EditMode > Run All` — expect 14/14 pass (8 WageCalculator + 6 HarvesterCreditCalculator).

- [ ] **Step 2: Play-mode smoke test**

1. Spawn a Harvester NPC with an assigned workplace.
2. Let a full shift run.
3. Verify at punch-out:
   - Console: no errors, no warnings from wage code.
   - `worker.CharacterWallet.GetBalance(CurrencyId.Default) > 0`.
   - `worker.CharacterWorkLog.GetCareerUnits(JobType.Harvester) > 0`.
   - `worker.CharacterWorkLog.GetWorkplaces(JobType.Harvester)[0].ShiftsCompleted == 1`.
4. Advance to the next shift, punch in early, punch out halfway. Verify wage is roughly half the full-shift amount + piece bonus for items produced.
5. Keep NPC punched in past scheduled end. Verify no additional shift-counter increments — only career counter grows.

- [ ] **Step 3: Save/load round-trip**

1. With NPC above, save the world.
2. Reload. Verify wallet balance and work-log entries match exactly.
3. Inspect `JobAssignment.PieceRate` etc. — should be non-zero (seeded from WageRates or persisted edit).

- [ ] **Step 4: Multiplayer smoke test**

Repeat Step 2 with Host + Client. Verify Client sees wallet balance and work-log on the NPC match Host.

- [ ] **Step 5: Final commit — documentation touch-ups from discoveries**

If any smoke test revealed a gap, document it in the relevant SKILL.md under a "Known limitations" section and commit.

```bash
git add .agent/skills/ wiki/
git commit -m "docs: final smoke-test touch-ups for worker wages & performance"
```

---

## Spec Coverage Checklist (for self-review)

Cross-referenced against spec `2026-04-22-worker-wages-and-performance-design.md`:

| Spec section | Task(s) |
|---|---|
| 1. Data model — CurrencyId | Task 1 |
| 1. Data model — WalletSaveData | Task 6 |
| 1. Data model — WorkLogSaveData | Task 8 |
| 1. Data model — JobAssignment wage fields | Task 16 |
| 1. Data model — WageRatesSO | Task 12 |
| 2. Wage formulas (piece + fixed + ratio + overtime cap) | Tasks 3, 4, 15 |
| 2. Unit-credit rules (harvester deficit / crafter order / transporter delivery) | Tasks 5, 22, 23, 24 |
| 3. Components — CharacterWallet | Task 7 |
| 3. Components — CharacterWorkLog | Task 9 |
| 3. Components — IWagePayer + MintedWagePayer | Task 14 |
| 3. Components — WageSystemService | Task 15 |
| 3. Components — CommercialBuilding.PaymentCurrency | Task 18 |
| 3. Components — JobAssignment.SetWage | Task 16 |
| 4. Integration — punch-in / punch-out | Tasks 20, 21 |
| 4. Integration — Harvester deposit | Task 22 |
| 4. Integration — Crafter completion | Task 24 |
| 4. Integration — Transporter delivery | Task 23 |
| 4. Integration — hire-time seeding | Task 18 |
| 4. Integration — save profile chain | Task 17 (plus automatic discovery) |
| 5. Save / load | Tasks 6, 8, 17, and auto-discovery via Task 11 prefab attach |
| 6. Networking — NetworkVariable / RPC pattern | Task 7 (wallet); Task 28 matrix |
| 7. Hibernation / macro-sim | Task 26 |
| 8. Configuration — WageRatesSO asset + scene wiring | Tasks 12, 13, 19 |
| 9. UI surface — `GetAllHistory` | Task 9 |
| 10. Edge cases | Tasks 7, 9 (defensive coding), Task 22 (inFlight fallback) |
| 11. Docs — three new SKILL.md | Tasks 29, 30, 31 |
| 11. Docs — updated SKILL.md | Task 32 |
| 11. Docs — new wiki page | Task 33 |
| 11. Docs — updated wiki pages + INDEX | Task 34 |
| 11. Docs — agents | Task 35 |

---

## Commit-Count Summary

36 tasks → 36 commits (some tasks have multiple commits inside — the count above is tasks, which is the natural review unit).
