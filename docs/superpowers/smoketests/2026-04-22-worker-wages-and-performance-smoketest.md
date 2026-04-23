# Worker Wages & Performance — Manual Smoke Test

**Branch:** `multiplayyer`
**Date:** 2026-04-22
**Companion to:** `docs/superpowers/specs/2026-04-22-worker-wages-and-performance-design.md`

This is a manual Play Mode test you (the user) run in Unity. The automated test pass is the 14 EditMode unit tests at `Assets/Tests/EditMode/` — they verify pure logic (wage formula, deficit math). This file verifies end-to-end behavior in a live scene.

---

## Prerequisites

- `GameScene` open in the Hierarchy.
- `WageSystemService` GameObject exists at scene root with `_defaultRates` pointing at `Assets/ScriptableObjects/Jobs/WageRates.asset` (Task 19).
- At least one `Character_Default*` prefab spawnable via the existing dev-mode spawn flow.
- A `CommercialBuilding` placed (e.g., a Sawmill, Smithy, or Shop) with:
  - At least one job slot
  - At least one outstanding `BuyOrder` / `CraftingOrder` / harvest target so workers have something to do.

If your dev-mode panel already exposes wallet balance and per-jobtype workplace history, use that. Otherwise add a temporary `Debug.Log` in `WageSystemService.ComputeAndPayShiftWage` to print the wage value during testing.

---

## Test 1 — Solo session, harvester full shift

**Setup:**
1. Enter Play Mode.
2. Spawn an NPC and assign them as a Harvester (Woodcutter / Forager / Farmer / Miner) via dev-mode `Hire` action at a HarvestingBuilding.
3. Note the time of day (let's say 08:00). Their schedule should run, e.g., 08:00 → 16:00.

**Run:**
1. Use `GameSpeedController` to advance time to ~17:00 (after their shift end).
2. Inspect the NPC's `CharacterWallet` component in the Inspector.

**Expected:**
- `_balances` dictionary has one entry: `(CurrencyId.Default, N)` where N ≥ 10 (their MinimumShiftWage).
- If they harvested anything that fit the building's needs, N > 10 (piece bonus added).
- `CharacterWorkLog._careerByJob` has an entry for their JobType, with one `WorkPlaceRecord` whose `BuildingDisplayName` matches the building's display name and `ShiftsCompleted == 1`.

**Failure modes to watch:**
- Wallet entry empty → `WageSystemService.Instance` was null at punch-out, or `JobAssignment.MinimumShiftWage` was 0 (asset not seeded — check `Assets/ScriptableObjects/Jobs/WageRates.asset` was set on `WageSystemService._defaultRates` slot in `GameScene`).
- `BuildingDisplayName` is wrong → check `Building.BuildingDisplayName` getter (Task 25) and the authored `buildingName` field on the building.

---

## Test 2 — Early punch-out, partial wage

**Setup:** same as Test 1.

**Run:**
1. After NPC has been working for ~half their scheduled shift (e.g., 4h of 8h), use dev-mode `Fire` (or force punch-out) to end the shift early.

**Expected:**
- Wallet balance ≈ 5 coins (half of 10 minimum) plus any piece bonus from items they actually deposited.
- `WorkPlaceRecord.ShiftsCompleted == 1` (counts as a shift even if partial).
- No log spam — clean punch-out.

---

## Test 3 — Overtime cap (no bonus)

**Setup:** spawn an NPC, assign as Harvester. Disable the auto-punch-out (set their schedule end = 23:59 OR keep them past their scheduled end via dev-mode override).

**Run:**
1. Let them work past their scheduled `endHour`.
2. Confirm they keep depositing items past the scheduled end.
3. Force punch-out.

**Expected:**
- Wallet shows the full minimum wage (10) — NOT more, even though they worked overtime.
- Items deposited past the scheduled end DO appear in `WorkPlaceRecord.UnitsWorked` (lifetime always increments) but they did NOT earn piece-rate pay for those overtime units.

This validates the "no overtime bonus" rule in `CharacterWorkLog.LogShiftUnit` (the shift-window check) and `WageCalculator.ComputeShiftRatio` (the clamp01).

---

## Test 4 — Vendor / Server / Barman / LogisticsManager (fixed wage)

**Setup:** spawn an NPC, assign them to a fixed-wage job (Vendor at a ShopBuilding, Server at a Bar, etc.).

**Run:**
1. Let them complete a full shift.

**Expected:**
- Wallet receives exactly the `FixedShiftWage` from `WageRates.asset` (Vendor=18, Server=14, Barman=16, LogisticsManager=22).
- `WorkPlaceRecord.UnitsWorked == 0` (fixed-wage jobs don't accumulate units).
- `WorkPlaceRecord.ShiftsCompleted == 1`.

---

## Test 5 — Save / Load round-trip

**Setup:** complete Test 1 so the NPC has a wallet balance and a WorkPlaceRecord.

**Run:**
1. Save the world via the bed checkpoint or portal gate (whatever your save trigger is).
2. Quit Play Mode and re-enter.
3. Load the save.

**Expected:**
- NPC's wallet balance is identical to pre-save.
- `CharacterWorkLog._careerByJob` is identical (UnitsWorked, ShiftsCompleted, FirstWorkedDay, LastWorkedDay, BuildingDisplayName).
- `JobAssignment.PieceRate / MinimumShiftWage / FixedShiftWage / Currency` are preserved (Task 17 round-trip).

---

## Test 6 — Owner-edited wage

**Setup:** spawn an Owner character + an NPC employee. Owner hires the NPC at the building.

**Run (script via dev-mode console or temporary button):**
```csharp
var building = /* the CommercialBuilding */;
var owner = /* the Owner Character */;
var npc = /* the employee Character */;
bool ok = building.TrySetAssignmentWage(owner, npc, pieceRate: 5, minimumShift: 20);
Debug.Log($"SetWage authorized: {ok}");
```

**Expected:**
- `ok == true`.
- `npc.CharacterJob.ActiveJobs[0].PieceRate == 5`, `MinimumShiftWage == 20`.
- Try the same call with `requester = npc` (a non-owner): logs warning, returns false, no change.

---

## Test 7 — Multiplayer matrix (Host ↔ Client wallet sync)

**Setup:** Host launches GameScene; Client connects.

**Run:**
1. On Host: spawn an NPC, hire as Harvester, advance time through one full shift.
2. On Client: inspect the same NPC's `CharacterWallet`.

**Expected:**
- Client sees the same wallet balance as Host (BroadcastBalanceChangeClientRpc replication).
- Late-joining client (joins AFTER the wage is paid, before another shift): sees balance 0 — this is the documented v1 limitation. Will be fixed when the wallet upgrades to NetworkList sync.

---

## Test 8 — Hibernation transition (zero crash)

**Setup:** spawn an NPC, let them work a partial shift on Map A.

**Run:**
1. Travel away to Map B (Map A hibernates, NPC's WorkLog state should serialize correctly).
2. Travel back to Map A.

**Expected:**
- NPC's `WorkPlaceRecord` data is intact (lifetime career counter survived).
- No new shift entries get fabricated by macro-sim (the Task 26 TODO documents that hibernated work doesn't accrue).

---

## Known v1 Limitations (do NOT report as bugs)

1. **Late-joining clients see wallet 0** until next mutation (ClientRpc-on-change, no initial-state sync). Tracked: spec section 6 + character-wallet SKILL.md.
2. **Hibernated NPCs don't accrue WorkLog units offline.** The yield goes into community.ResourcePools but the NPC's career counter doesn't grow during hibernation. Tracked: TODO in `MacroSimulator.cs` next to the inventory-yields pass.
3. **Harvester deficit cap is dormant.** `HarvestingBuilding` does not implement `IStockProvider` today, so the deficit-bounded credit code path is unreachable. Each deposit credits one unit fully. The user-visible exploit ceiling is bounded by `HarvestingBuilding.IsResourceAtLimit` (which stops a worker from depositing once at-limit), so this isn't a critical leak — but ideally `HarvestingBuilding : IStockProvider` lands as a small follow-up.
4. **Multi-role workers at the same building** (a worker holding two `JobAssignment`s at one building): wage payment and unit credit pick the FIRST matching assignment. v1 limitation.

---

## Pass / Fail Criteria

This smoke test passes if:
- All 8 tests pass with the noted known limitations.
- No new exceptions in the Unity console during any test.
- No save corruption or load failures after Test 5.

Report results in the next conversation turn for documentation.
