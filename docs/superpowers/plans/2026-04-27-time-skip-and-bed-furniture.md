# Time Skip & Bed Furniture — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Spec:** [docs/superpowers/specs/2026-04-27-time-skip-and-bed-furniture-design.md](../specs/2026-04-27-time-skip-and-bed-furniture-design.md)

**Goal:** Add a player-initiated *time-skip* path that coexists with `GameSpeedController`. The skip hibernates the active map, advances `TimeManager` one hour at a time, and runs `MacroSimulator.SimulateOneHour` per iteration before waking the map. In the same change, introduce `BedFurniture` with a modular per-prefab slot list and a sleep-state lifecycle on `Character`.

**Architecture:** New `TimeSkipController` (server-authoritative singleton, mirrors `GameSpeedController`) owns the per-hour loop. `TimeManager.AdvanceOneHour` adds a new clock-advance entry point with the same event semantics as live `ProgressTime`. `MacroSimulator.SimulateOneHour` extracts the existing single-pass catch-up math into per-hour and per-day blocks, gating cumulative-delta steps (resource regen, inventory yields, city growth, zone motion) to day-boundary crossings. `BedFurniture : Furniture` adds a serialized `List<BedSlot>` (anchor + occupant per slot); the slot lifecycle drives `Character.EnterSleep` / `ExitSleep`, which snap position, toggle the `NavMeshAgent`, and flip a `NetworkVariable<bool> IsSleeping` that gates `PlayerController.Update`. v1 is single-player only; multiplayer auto-trigger ("all players asleep ⇒ skip") is deferred to v2.

**Tech Stack:** Unity 2D-in-3D, C#, NGO (Netcode for GameObjects), NUnit EditMode tests, NavMeshAgent, existing `MacroSimulator` math.

> **MCP availability note:** Task 8 ("Run EditMode tests"), Task 12 (DevModePanel prefab edit), Task 13 (UI prefab edits) and all "Smoke-test in Play mode" steps assume Unity MCP is connected. If MCP is offline, the executing worker must pause and hand control back to the user to perform those steps in the Unity Editor manually.

---

## Pre-flight

- [ ] **P1: Read the spec end-to-end** before touching any code.
  Read: [docs/superpowers/specs/2026-04-27-time-skip-and-bed-furniture-design.md](../specs/2026-04-27-time-skip-and-bed-furniture-design.md).
- [ ] **P2: Confirm clean working tree.**
  Run: `git status`
  Expected: only the working-set files reported in the brief. No new staged changes.
- [ ] **P3: Verify the EditMode test runner works.**
  In Unity Editor → Window → General → Test Runner → EditMode → Run All. Expected: existing `WageCalculatorTests`, `HarvesterCreditCalculatorTests`, etc. pass. If they fail, stop and investigate before adding new tests on top of broken infra.
- [ ] **P4: Verify the existing wake-up macro-sim path works.**
  Open a scene with at least one map. Place an NPC, force-hibernate the map (drive away → return), verify `MacroSimulator.SimulateCatchUp` runs (look for the `[MacroSim] Fast-forwarding Map …` log). This is your baseline — Task 7 must not regress this path.

---

## Task 1: `BedSlot` + `BedFurniture` skeleton

**Files:**
- Create: `Assets/Scripts/World/Furniture/BedSlot.cs`
- Create: `Assets/Scripts/World/Furniture/BedFurniture.cs`

This task creates the data classes only. No `Character` integration, no anchor snap — just slot tracking.

- [ ] **Step 1.1: Create the `BedSlot` data class.**

```csharp
// Assets/Scripts/World/Furniture/BedSlot.cs
using UnityEngine;

/// <summary>
/// One slot on a <see cref="BedFurniture"/>. The Anchor transform defines where the
/// occupying Character is snapped (position + rotation) when sleeping. Occupant /
/// ReservedBy are runtime-only (not serialized) — set internally by <see cref="BedFurniture"/>.
/// </summary>
[System.Serializable]
public class BedSlot
{
    [Tooltip("Authored child transform. Sets the position + rotation the sleeping character is snapped to.")]
    [SerializeField] private Transform _anchor;

    public Transform Anchor => _anchor;
    public Character Occupant { get; internal set; }
    public Character ReservedBy { get; internal set; }
    public bool IsFree => Occupant == null && ReservedBy == null;
}
```

- [ ] **Step 1.2: Create the `BedFurniture` skeleton.**

```csharp
// Assets/Scripts/World/Furniture/BedFurniture.cs
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Bed furniture with a modular per-prefab slot list. Single-bed prefab = 1 slot,
/// double-bed = 2, family-bed = 4, etc. Slot count is baked into the prefab via
/// the serialized <c>_slots</c> list — no per-prefab code.
///
/// Slot-aware lifecycle is preferred (<see cref="ReserveSlot"/> / <see cref="UseSlot"/>
/// / <see cref="ReleaseSlot"/>). Base <see cref="Furniture.Reserve"/> / <see cref="Furniture.Use"/>
/// / <see cref="Furniture.Release"/> are overridden to pick the first free slot for
/// backward-compat with legacy single-slot callers (e.g. existing SleepBehaviour fallback).
/// </summary>
public class BedFurniture : Furniture
{
    [Header("Bed")]
    [SerializeField] private List<BedSlot> _slots = new List<BedSlot>();

    public IReadOnlyList<BedSlot> Slots => _slots;
    public int SlotCount => _slots.Count;

    public int FreeSlotCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < _slots.Count; i++) if (_slots[i].IsFree) n++;
            return n;
        }
    }

    public bool HasFreeSlot => FreeSlotCount > 0;

    public int FindFreeSlotIndex()
    {
        for (int i = 0; i < _slots.Count; i++) if (_slots[i].IsFree) return i;
        return -1;
    }

    public int GetSlotIndexFor(Character c)
    {
        if (c == null) return -1;
        for (int i = 0; i < _slots.Count; i++)
            if (_slots[i].Occupant == c || _slots[i].ReservedBy == c) return i;
        return -1;
    }

    public bool ReserveSlot(int slotIndex, Character c)
    {
        if (c == null) return false;
        if (slotIndex < 0 || slotIndex >= _slots.Count) return false;
        var slot = _slots[slotIndex];
        if (!slot.IsFree) return false;
        slot.ReservedBy = c;
        return true;
    }

    public bool UseSlot(int slotIndex, Character c)
    {
        // Wired to Character.EnterSleep in Task 3.
        if (c == null) return false;
        if (slotIndex < 0 || slotIndex >= _slots.Count) return false;
        var slot = _slots[slotIndex];
        if (slot.Occupant != null) return false;
        slot.Occupant = c;
        slot.ReservedBy = null;
        c.SetOccupyingFurniture(this);
        return true;
    }

    public void ReleaseSlot(int slotIndex)
    {
        // Wired to Character.ExitSleep in Task 3.
        if (slotIndex < 0 || slotIndex >= _slots.Count) return;
        var slot = _slots[slotIndex];
        if (slot.Occupant != null) slot.Occupant.SetOccupyingFurniture(null);
        slot.Occupant = null;
        slot.ReservedBy = null;
    }

    // ── Override base Furniture single-slot API for backward-compat ──

    public override bool IsFree() => HasFreeSlot;

    public new bool Reserve(Character c)
    {
        int idx = FindFreeSlotIndex();
        if (idx < 0)
        {
            Debug.LogWarning($"<color=orange>[BedFurniture]</color> Reserve fallback: no free slot on {FurnitureName}.");
            return false;
        }
        return ReserveSlot(idx, c);
    }

    public new bool Use(Character c)
    {
        int idx = GetSlotIndexFor(c);
        if (idx < 0) idx = FindFreeSlotIndex();
        if (idx < 0)
        {
            Debug.LogWarning($"<color=orange>[BedFurniture]</color> Use fallback: no free slot on {FurnitureName}.");
            return false;
        }
        return UseSlot(idx, c);
    }

    public new void Release()
    {
        // Release every slot that has an occupant or reservation.
        for (int i = 0; i < _slots.Count; i++)
        {
            if (!_slots[i].IsFree) ReleaseSlot(i);
        }
    }
}
```

> Note: `Furniture.Reserve` is `virtual`, so we keep the `public override` form for it. `Use` and `Release` are non-virtual on `Furniture`, so we use `new` to shadow them. Callers that hold a `BedFurniture` reference get the slot-aware fallback; callers that hold a `Furniture` reference fall through to the base methods, which still work (occupy the inherited `_occupant`) but bypass the slot list. Task 4 updates the one in-tree caller (`SleepBehaviour`) to use the slot-aware API directly when the bed is a `BedFurniture`.

- [ ] **Step 1.3: Compile.** Switch to Unity, wait for recompile. Console expected: zero errors.

- [ ] **Step 1.4: Commit.**

```bash
git add "Assets/Scripts/World/Furniture/BedSlot.cs" "Assets/Scripts/World/Furniture/BedSlot.cs.meta" "Assets/Scripts/World/Furniture/BedFurniture.cs" "Assets/Scripts/World/Furniture/BedFurniture.cs.meta"
git commit -m "feat(furniture): add BedFurniture with modular per-prefab slot list"
```

---

## Task 2: `Character.IsSleeping` + `EnterSleep` / `ExitSleep`

**Files:**
- Modify: `Assets/Scripts/Character/Character.cs`

- [ ] **Step 2.1: Add the `_isSleeping` NetworkVariable.**

In `Character.cs`, find the existing public NetworkVariables block (around line 155 — `NetworkRaceId`, `NetworkCharacterName`, `NetworkVisualSeed`, `NetworkCharacterId`). Add immediately after `NetworkCharacterId`:

```csharp
/// <summary>
/// Server-authoritative flag. True while the character is occupying a bed slot.
/// Replicates to all peers so client visuals can switch to sleep pose; gates
/// PlayerController.Update on the owning player. Not in ICharacterSaveData —
/// sleeping characters wake up out-of-bed on save/load.
/// </summary>
public NetworkVariable<bool> NetworkIsSleeping = new NetworkVariable<bool>(
    false,
    NetworkVariableReadPermission.Everyone,
    NetworkVariableWritePermission.Server
);
```

- [ ] **Step 2.2: Add the public getter and the event.**

In the same `#region Properties` block as `IsUnconscious` (around line 271), add:

```csharp
public bool IsSleeping => NetworkIsSleeping.Value;
```

In the `#region Events` block (around line 198), add the event next to `OnUnconsciousChanged`:

```csharp
public event System.Action<bool> OnSleepStateChanged;
```

- [ ] **Step 2.3: Wire the NetworkVariable change callback.**

Find `OnNetworkSpawn` in `Character.cs`. After existing event subscriptions for other NetworkVariables, add:

```csharp
NetworkIsSleeping.OnValueChanged += HandleSleepStateChanged;
```

Find `OnNetworkDespawn` (or `OnDestroy` if no `OnNetworkDespawn` exists). Add:

```csharp
NetworkIsSleeping.OnValueChanged -= HandleSleepStateChanged;
```

Add the handler near other NetworkVariable handlers:

```csharp
private void HandleSleepStateChanged(bool previous, bool current)
{
    OnSleepStateChanged?.Invoke(current);
}
```

- [ ] **Step 2.4: Add `EnterSleep` and `ExitSleep`.**

In `Character.cs`, near the existing `SetOccupyingFurniture` method (around line 1050), add:

```csharp
/// <summary>
/// Server-only. Snap the character to the given anchor, disable navigation,
/// and flip <see cref="NetworkIsSleeping"/> to true. Called by
/// <see cref="BedFurniture.UseSlot"/>. Position is server-driven — clients
/// receive the snap via NetworkTransform, not via this method.
/// </summary>
public void EnterSleep(Transform anchor)
{
    if (!IsServer)
    {
        Debug.LogWarning($"<color=orange>[Character]</color> EnterSleep called on non-server peer for {CharacterName}. Ignored.");
        return;
    }
    if (anchor == null)
    {
        Debug.LogError($"<color=red>[Character]</color> EnterSleep called with null anchor for {CharacterName}.");
        return;
    }
    if (NetworkIsSleeping.Value)
    {
        Debug.LogWarning($"<color=orange>[Character]</color> EnterSleep on {CharacterName} but already sleeping. Ignored.");
        return;
    }

    transform.SetPositionAndRotation(anchor.position, anchor.rotation);
    if (_cachedNavMeshAgent != null) _cachedNavMeshAgent.enabled = false;
    CharacterMovement?.ResetPath();
    NetworkIsSleeping.Value = true;

    Debug.Log($"<color=cyan>[Character]</color> {CharacterName} EnterSleep at {anchor.name} ({anchor.position}).");
}

/// <summary>
/// Server-only. Re-enable navigation and flip <see cref="NetworkIsSleeping"/> to false.
/// The character stays at the anchor's last position; the next AI tick or player input
/// drives them away. Called by <see cref="BedFurniture.ReleaseSlot"/>.
/// </summary>
public void ExitSleep()
{
    if (!IsServer)
    {
        Debug.LogWarning($"<color=orange>[Character]</color> ExitSleep called on non-server peer for {CharacterName}. Ignored.");
        return;
    }
    if (!NetworkIsSleeping.Value)
    {
        // No-op on idempotent call; common during shutdown.
        return;
    }

    if (_cachedNavMeshAgent != null) _cachedNavMeshAgent.enabled = true;
    NetworkIsSleeping.Value = false;

    Debug.Log($"<color=cyan>[Character]</color> {CharacterName} ExitSleep.");
}
```

- [ ] **Step 2.5: Compile.** Switch to Unity, wait for recompile. Console expected: zero errors.

- [ ] **Step 2.6: Commit.**

```bash
git add "Assets/Scripts/Character/Character.cs"
git commit -m "feat(character): add NetworkIsSleeping + EnterSleep/ExitSleep"
```

---

## Task 3: Wire `BedFurniture.UseSlot` / `ReleaseSlot` to `Character.EnterSleep` / `ExitSleep`

**Files:**
- Modify: `Assets/Scripts/World/Furniture/BedFurniture.cs`

Task 1 left the wiring as a `// Wired in Task 3` comment. Replace those bodies now that `Character.EnterSleep` / `ExitSleep` exist.

- [ ] **Step 3.1: Update `UseSlot`.**

Replace the `UseSlot` body in `BedFurniture.cs` with:

```csharp
public bool UseSlot(int slotIndex, Character c)
{
    if (c == null) return false;
    if (slotIndex < 0 || slotIndex >= _slots.Count) return false;
    var slot = _slots[slotIndex];
    if (slot.Occupant != null)
    {
        Debug.LogWarning($"<color=orange>[BedFurniture]</color> Slot {slotIndex} on {FurnitureName} already occupied by {slot.Occupant.CharacterName}.");
        return false;
    }
    if (slot.Anchor == null)
    {
        Debug.LogError($"<color=red>[BedFurniture]</color> Slot {slotIndex} on {FurnitureName} has no Anchor authored. Cannot UseSlot.");
        return false;
    }

    slot.Occupant = c;
    slot.ReservedBy = null;
    c.SetOccupyingFurniture(this);
    c.EnterSleep(slot.Anchor);
    Debug.Log($"<color=cyan>[BedFurniture]</color> {c.CharacterName} occupied slot {slotIndex} on {FurnitureName}.");
    return true;
}
```

- [ ] **Step 3.2: Update `ReleaseSlot`.**

Replace `ReleaseSlot` with:

```csharp
public void ReleaseSlot(int slotIndex)
{
    if (slotIndex < 0 || slotIndex >= _slots.Count) return;
    var slot = _slots[slotIndex];
    if (slot.Occupant != null)
    {
        Debug.Log($"<color=cyan>[BedFurniture]</color> {slot.Occupant.CharacterName} released slot {slotIndex} on {FurnitureName}.");
        slot.Occupant.ExitSleep();
        slot.Occupant.SetOccupyingFurniture(null);
    }
    slot.Occupant = null;
    slot.ReservedBy = null;
}
```

- [ ] **Step 3.3: Compile.** Console expected: zero errors.

- [ ] **Step 3.4: Commit.**

```bash
git add "Assets/Scripts/World/Furniture/BedFurniture.cs"
git commit -m "feat(furniture): wire BedFurniture slot lifecycle to Character.EnterSleep/ExitSleep"
```

---

## Task 4: `SleepBehaviour` routing through `BedFurniture.UseSlot`

**Files:**
- Modify: `Assets/Scripts/Character/AIBehaviour/SleepBehaviour.cs`

Today `SleepBehaviour.HandleGoingToBed` calls `_bed.Use(character)`. If the bed is a `BedFurniture`, route through `UseSlot` so the NPC gets the anchor snap and `IsSleeping` flag. Plain-`Furniture` beds keep the legacy path.

- [ ] **Step 4.1: Update `HandleGoingToBed`.**

In `SleepBehaviour.cs`, replace the body of `HandleGoingToBed` (around line 113) with:

```csharp
private void HandleGoingToBed(Character character, CharacterMovement movement)
{
    if (!_destinationSet)
    {
        movement.SetDestination(_bed.GetInteractionPosition());
        _destinationSet = true;
        return;
    }

    if (!movement.PathPending && (!movement.HasPath || movement.RemainingDistance <= movement.StoppingDistance + 0.5f))
    {
        bool ok;
        if (_bed is BedFurniture bedFurniture)
        {
            int slotIdx = bedFurniture.GetSlotIndexFor(character);
            if (slotIdx < 0) slotIdx = bedFurniture.FindFreeSlotIndex();
            ok = slotIdx >= 0 && bedFurniture.UseSlot(slotIdx, character);
        }
        else
        {
            ok = _bed.Use(character);  // legacy plain-Furniture fallback
        }

        if (ok)
        {
            Debug.Log($"<color=cyan>[Sleep]</color> {character.CharacterName} is now sleeping in {_bed.FurnitureName}.");
        }
        else
        {
            Debug.LogWarning($"<color=orange>[Sleep]</color> {character.CharacterName} failed to occupy {_bed.FurnitureName}. Sleeping standing.");
        }
        movement.ResetPath();
        _phase = SleepPhase.Sleeping;
    }
}
```

- [ ] **Step 4.2: Update `Exit` to release the bed slot when applicable.**

Replace the `Exit` body with:

```csharp
public void Exit(Character character)
{
    if (_bed != null)
    {
        if (_bed is BedFurniture bedFurniture)
        {
            int slotIdx = bedFurniture.GetSlotIndexFor(character);
            if (slotIdx >= 0) bedFurniture.ReleaseSlot(slotIdx);
        }
        else if (_bed.Occupant == character || _bed.ReservedBy == character)
        {
            _bed.Release();
        }
        Debug.Log($"<color=cyan>[Sleep]</color> {character.CharacterName} woke up and left {_bed.FurnitureName}.");
    }

    character.CharacterMovement?.ResetPath();

    // Save player profile and world state to disk after sleeping
    if (character.IsServer && character.IsPlayer())
    {
        if (SaveManager.Instance != null)
            SaveManager.Instance.RequestSave(character);
    }
}
```

- [ ] **Step 4.3: Compile.** Console expected: zero errors.

- [ ] **Step 4.4: Commit.**

```bash
git add "Assets/Scripts/Character/AIBehaviour/SleepBehaviour.cs"
git commit -m "feat(ai): route SleepBehaviour through BedFurniture.UseSlot when bed is BedFurniture"
```

---

## Task 5: `PlayerController` early-out on `IsSleeping`

**Files:**
- Modify: `Assets/Scripts/Character/CharacterControllers/PlayerController.cs`

`PlayerController.Update()` (around line 50) gates input on `IsOwner`. Add an early-out for `IsSleeping` so a sleeping player can't move or trigger actions. Per project rule #33, all player input lives in `PlayerController` — so this is the only file that needs to change for player-input lockout.

- [ ] **Step 5.1: Add the early-out.**

In `PlayerController.Update()`, immediately after the opening `if (IsOwner)` (line 52), and **before** the existing UI text-field gate, insert:

```csharp
        if (IsOwner)
        {
            // Sleeping players accept no input — bed/skip lifecycle owns position+rotation,
            // animator switches to sleep pose via Character.OnSleepStateChanged.
            if (Character != null && Character.IsSleeping) return;

            // Block player movement/action input if typing in any UI text field
            // … (existing code unchanged below) …
```

- [ ] **Step 5.2: Compile.** Console expected: zero errors.

- [ ] **Step 5.3: Commit.**

```bash
git add "Assets/Scripts/Character/CharacterControllers/PlayerController.cs"
git commit -m "feat(player): early-out PlayerController.Update when Character.IsSleeping"
```

---

## Task 6: `TimeManager.AdvanceOneHour`

**Files:**
- Modify: `Assets/Scripts/DayNightCycle/TimeManager.cs`

Add a clock-advance entry point distinct from `SkipToHour` (which jumps directly without honoring intermediate hour transitions).

- [ ] **Step 6.1: Add `AdvanceOneHour`.**

In `TimeManager.cs`, after the existing `SkipToHour` method (around line 169), add:

```csharp
/// <summary>
/// Advance the in-game clock by exactly one hour. Fires <see cref="OnHourChanged"/>,
/// <see cref="OnNewDay"/> on rollover, and <see cref="OnPhaseChanged"/> on phase
/// boundary — same event semantics as the live <c>ProgressTime()</c> path. Called
/// by <see cref="MWI.WorldSystem.TimeSkipController"/> per loop iteration.
/// Subscribers cannot tell the difference between a real hour and a skip hour.
/// </summary>
public void AdvanceOneHour()
{
    _currentTime += 1f / 24f;
    if (_currentTime >= 1f)
    {
        _currentTime -= 1f;
        CurrentDay++;
        OnNewDay?.Invoke();
    }

    int newHour = CurrentHour;
    if (newHour != _lastHour)
    {
        _lastHour = newHour;
        OnHourChanged?.Invoke(newHour);
    }

    UpdatePhase(false);  // fires OnPhaseChanged if morning/afternoon/evening/night boundary crossed
}
```

- [ ] **Step 6.2: Compile.** Console expected: zero errors.

- [ ] **Step 6.3: Commit.**

```bash
git add "Assets/Scripts/DayNightCycle/TimeManager.cs"
git commit -m "feat(time): TimeManager.AdvanceOneHour for per-hour skip iteration"
```

---

## Task 7: `MacroSimulator.SimulateOneHour` with day-boundary gating

**Files:**
- Modify: `Assets/Scripts/World/MapSystem/MacroSimulator.cs`

Add a new entry point. Existing `SimulateCatchUp` is **untouched** — the two paths coexist and operate on disjoint cases (active-map per-hour during a skip vs. visited-map single-pass on wake-up).

The per-hour pass must:
- Run hour-grained steps every call (needs decay, schedule snap, terrain, vegetation).
- Run day-grained steps only when `prevHour == 23 && currentHour == 0` (resource regen, inventory yields, city growth, zone motion).
- Update `LastHibernationTime` so a subsequent `WakeUp()` doesn't double-process.

- [ ] **Step 7.1: Add `SimulateOneHour` next to `SimulateCatchUp`.**

In `MacroSimulator.cs`, after the existing `SimulateCatchUp` method (around line 192), add:

```csharp
/// <summary>
/// Per-hour catch-up entry point used by <see cref="MWI.WorldSystem.TimeSkipController"/>.
/// Runs hour-grained steps every call and day-grained steps only on hour-23→hour-0
/// rollover. Updates <c>data.LastHibernationTime</c> so a subsequent <c>WakeUp()</c>
/// does not double-process. Existing <see cref="SimulateCatchUp"/> remains the
/// single-pass wake-up path for hibernated maps.
/// </summary>
/// <param name="data">The active map's hibernation snapshot — typically <c>MapController.HibernationData</c>.</param>
/// <param name="currentDay">Post-advance day (after <c>TimeManager.AdvanceOneHour</c>).</param>
/// <param name="currentTime01">Post-advance time01.</param>
/// <param name="jobYields">Job yield registry passed through to inventory-yield helpers.</param>
/// <param name="previousHour">The hour value BEFORE this hour-advance (used for day-rollover detection).</param>
public static void SimulateOneHour(MapSaveData data, int currentDay, float currentTime01, JobYieldRegistry jobYields, int previousHour)
{
    if (data == null || data.HibernatedNPCs == null) return;

    int currentHour = Mathf.FloorToInt(currentTime01 * 24f);
    bool crossedDayBoundary = (previousHour == 23 && currentHour == 0);

    MapController map = null;
    var activeMaps = UnityEngine.Object.FindObjectsByType<MapController>(FindObjectsSortMode.None);
    foreach (var m in activeMaps)
    {
        if (m.MapId == data.MapId) { map = m; break; }
    }

    CommunityData community = null;
    if (MapRegistry.Instance != null) community = MapRegistry.Instance.GetCommunity(data.MapId);

    // ── Hour-grained: always run ──

    // Needs decay + schedule snap (per-NPC)
    foreach (var npc in data.HibernatedNPCs)
    {
        ApplyNeedsDecayHours(npc, hoursPassed: 1f);
        SnapPositionFromSchedule(npc, data.MapId, currentHour);
    }

    // Terrain + vegetation
    if (data.TerrainCells != null)
    {
        var climateProfile = map?.Biome?.ClimateProfile;
        if (climateProfile != null)
        {
            var transitionRules = Resources.LoadAll<MWI.Terrain.TerrainTransitionRule>("Data/Terrain/TransitionRules");
            SimulateTerrainCatchUp(data.TerrainCells, climateProfile, 1f, new List<MWI.Terrain.TerrainTransitionRule>(transitionRules));
            SimulateVegetationCatchUp(data.TerrainCells, climateProfile, 1f);
        }
    }

    // ── Day-grained: only on day rollover ──
    if (crossedDayBoundary)
    {
        // 1. Resource pool regen — port from existing SimulateCatchUp's "1. Resource Regeneration" block, with fullDays=1
        if (map != null && map.Biome != null && community != null)
        {
            foreach (var pool in community.ResourcePools)
            {
                var entry = map.Biome.Harvestables.Find(h => h.ResourceId == pool.ResourceId);
                if (entry == null) continue;
                pool.CurrentAmount = Mathf.Min(pool.CurrentAmount + Mathf.CeilToInt(entry.BaseYieldQuantity), pool.MaxAmount);
            }
        }

        // 2. Inventory yields per NPC — port from existing block with daysPassed=1
        if (jobYields != null && community != null)
        {
            foreach (var npc in data.HibernatedNPCs)
            {
                if (npc.SavedJobType == JobType.None) continue;
                var recipe = jobYields.GetYieldFor(npc.SavedJobType);
                if (recipe == null) continue;

                float fraction = ((npc.FreeTimeStarts - npc.WorkHourStarts + 24) % 24) / 24f;
                float workFraction = Mathf.Clamp(fraction == 0f ? 1f : fraction, 0.1f, 1f);

                foreach (var output in recipe.Outputs)
                {
                    int yieldAmount = Mathf.FloorToInt(output.BaseAmountPerDay * workFraction * 1f);
                    if (yieldAmount <= 0) continue;
                    var pool = community.ResourcePools.Find(p => p.ResourceId == output.ResourceId);
                    if (pool == null)
                    {
                        pool = new ResourcePoolEntry { ResourceId = output.ResourceId, CurrentAmount = 0, MaxAmount = 9999f, LastHarvestedDay = currentDay };
                        community.ResourcePools.Add(pool);
                    }
                    pool.CurrentAmount += yieldAmount;
                }
            }
        }

        // 3. City growth — call existing SimulateCityGrowth helper with daysPassed=1.0
        if (community != null) SimulateCityGrowth(community, daysPassed: 1.0, data);

        // 4. Zone motion — daysSinceLastTick=1
        TickZoneMotion(daysSinceLastTick: 1);
    }

    // Stamp the new hibernation time so a future WakeUp single-pass does not re-process this delta.
    data.LastHibernationTime = (double)currentDay + currentTime01;
}

/// <summary>
/// Extracted from <see cref="SimulateCatchUp"/> step 3 (Needs Decay) so both per-hour and
/// per-day paths share one implementation.
/// </summary>
private static void ApplyNeedsDecayHours(HibernatedNPCData npcData, float hoursPassed)
{
    foreach (var need in npcData.SavedNeeds)
    {
        if (need.NeedType == "NeedSocial")
        {
            float drainRate = 45f / 24f;
            need.Value -= (hoursPassed * drainRate);
            if (need.Value < 0) need.Value = 0;
        }
        else if (need.NeedType == "NeedHunger")
        {
            const float drainRatePerHour = 100f / 24f;
            need.Value = MWI.Needs.HungerCatchUpMath.ApplyDecay(need.Value, drainRatePerHour, hoursPassed);
        }
    }
}

/// <summary>
/// Extracted from <see cref="SimulateCatchUp"/> step 4 (Schedule Snap).
/// </summary>
private static void SnapPositionFromSchedule(HibernatedNPCData npcData, string currentMapId, int currentHour)
{
    if (!npcData.HasSchedule) return;

    string targetMapId;
    Vector3 targetPosition = npcData.Position;

    if (IsHourInRange(currentHour, npcData.SleepHourStarts, npcData.WorkHourStarts))
    {
        targetMapId = npcData.HomeMapId;
        targetPosition = npcData.HomePosition;
    }
    else if (IsHourInRange(currentHour, npcData.WorkHourStarts, npcData.FreeTimeStarts))
    {
        targetMapId = npcData.WorkMapId;
        targetPosition = npcData.WorkPosition;
    }
    else
    {
        targetMapId = npcData.FreeTimeMapId;
        targetPosition = npcData.FreeTimePosition;
    }

    if (string.IsNullOrEmpty(targetMapId) || targetMapId == currentMapId)
    {
        if (targetPosition != Vector3.zero) npcData.Position = targetPosition;
    }
}
```

- [ ] **Step 7.2: Refactor `SimulateNPCCatchUp` to share the new helpers (no behavior change).**

The existing `SimulateNPCCatchUp` private method (around line 272) hand-rolls the same needs-decay and schedule-snap logic. To prevent the two paths from drifting, replace its body with calls to the new helpers:

```csharp
private static void SimulateNPCCatchUp(HibernatedNPCData npcData, string currentMapId, int currentHour, float hoursPassed)
{
    ApplyNeedsDecayHours(npcData, hoursPassed);
    SnapPositionFromSchedule(npcData, currentMapId, currentHour);
}
```

- [ ] **Step 7.3: Compile.** Switch to Unity, wait for recompile. Console expected: zero errors.

- [ ] **Step 7.4: Smoke-test the wake-up path is unchanged.**

Repeat the baseline check from P4: hibernate a map, wake it, look for the `[MacroSim] Fast-forwarding Map …` log. Verify NPCs respawn in their schedule-correct positions and resource pools regen as before. If anything regresses, roll back this task and investigate before proceeding.

- [ ] **Step 7.5: Commit.**

```bash
git add "Assets/Scripts/World/MapSystem/MacroSimulator.cs"
git commit -m "feat(macro-sim): add SimulateOneHour with day-boundary gating, share helpers with SimulateCatchUp"
```

---

## Task 8: EditMode test — `24×SimulateOneHour ≡ 1×SimulateCatchUp`

**Files:**
- Create: `Assets/Tests/EditMode/MacroSimulator_SimulateOneHour_Tests.cs`

This is the correctness invariant guarding the per-hour split. For a 24-hour delta from hour 0 to hour 0 next day, calling `SimulateOneHour` 24 times must produce the same `MapSaveData` mutations as one `SimulateCatchUp(daysPassed=1.0)` on a clone.

Pure-math test: no Unity scene, no NetworkManager, no live characters. Build a minimal `MapSaveData` fixture by hand.

- [ ] **Step 8.1: Locate the existing EditMode test folder.**

```bash
ls Assets/Tests/EditMode/
```

Expected: an existing test asmdef. If the folder doesn't exist, create it and an `EditMode.asmdef` referencing the runtime asmdef before adding tests — but in this project EditMode tests already exist (`WageCalculatorTests`, etc.), so this should be a no-op.

- [ ] **Step 8.2: Write the failing test.**

```csharp
// Assets/Tests/EditMode/MacroSimulator_SimulateOneHour_Tests.cs
using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;
using MWI.Time;

public class MacroSimulator_SimulateOneHour_Tests
{
    [Test]
    public void NeedsDecay_24HoursOfOneHour_EqualsOneCallWith24Hours()
    {
        // Two identical NPCs with NeedSocial = 100, NeedHunger = 100.
        var npcA = MakeNpc(socialValue: 100f, hungerValue: 100f);
        var npcB = MakeNpc(socialValue: 100f, hungerValue: 100f);

        var dataA = MakeMapData(npcA, lastHibernationTimeAbs: 1.0);  // day=1, time01=0
        var dataB = MakeMapData(npcB, lastHibernationTimeAbs: 1.0);

        // Path A: 24 calls of SimulateOneHour, each advancing 1 hour.
        for (int h = 0; h < 24; h++)
        {
            int prevHour = h;       // hour values 0..23
            int currentDay = (h == 23) ? 2 : 1;
            float currentTime01 = ((h + 1) % 24) / 24f;
            MacroSimulator.SimulateOneHour(dataA, currentDay, currentTime01, jobYields: null, previousHour: prevHour);
        }

        // Path B: one SimulateCatchUp covering 24 hours (daysPassed=1.0).
        MacroSimulator.SimulateCatchUp(dataB, currentDay: 2, currentTime01: 0f, jobYields: null);

        // Compare each need's final value with a 0.01 tolerance for FP accumulation.
        for (int i = 0; i < npcA.SavedNeeds.Count; i++)
        {
            Assert.AreEqual(
                npcB.SavedNeeds[i].Value,
                npcA.SavedNeeds[i].Value,
                delta: 0.01f,
                $"Need '{npcA.SavedNeeds[i].NeedType}' diverged between per-hour and single-pass paths.");
        }
    }

    [Test]
    public void LastHibernationTime_AdvancesOnEachHour()
    {
        var npc = MakeNpc(socialValue: 100f, hungerValue: 100f);
        var data = MakeMapData(npc, lastHibernationTimeAbs: 1.0);

        MacroSimulator.SimulateOneHour(data, currentDay: 1, currentTime01: 1f / 24f, jobYields: null, previousHour: 0);
        Assert.AreEqual(1.0 + (1.0 / 24.0), data.LastHibernationTime, 1e-5);

        MacroSimulator.SimulateOneHour(data, currentDay: 1, currentTime01: 2f / 24f, jobYields: null, previousHour: 1);
        Assert.AreEqual(1.0 + (2.0 / 24.0), data.LastHibernationTime, 1e-5);
    }

    // ── helpers ──
    private static HibernatedNPCData MakeNpc(float socialValue, float hungerValue)
    {
        var npc = new HibernatedNPCData
        {
            CharacterId = System.Guid.NewGuid().ToString(),
            HasSchedule = false,  // disable schedule snap to keep this test focused on needs decay
            SavedNeeds = new List<HibernatedNeedData>
            {
                new HibernatedNeedData { NeedType = "NeedSocial", Value = socialValue },
                new HibernatedNeedData { NeedType = "NeedHunger", Value = hungerValue }
            },
            SavedJobType = JobType.None
        };
        return npc;
    }

    private static MapSaveData MakeMapData(HibernatedNPCData npc, double lastHibernationTimeAbs)
    {
        return new MapSaveData
        {
            MapId = "TestMap",
            LastHibernationTime = lastHibernationTimeAbs,
            HibernatedNPCs = new List<HibernatedNPCData> { npc }
        };
    }
}
```

> Note: this test deliberately scopes to needs decay only (not the day-grained yields/regen/growth) to keep the fixture small. The day-boundary gating logic is exercised indirectly by the integration smoke-test in Task 14.

- [ ] **Step 8.3: Run the test, confirm it passes.**

Unity Editor → Window → General → Test Runner → EditMode → run `MacroSimulator_SimulateOneHour_Tests`. Expected: both tests green. If `LastHibernationTime_AdvancesOnEachHour` fails, the stamping logic in `SimulateOneHour` is wrong. If `NeedsDecay_24HoursOfOneHour_EqualsOneCallWith24Hours` fails, the hour math diverges from the day math — investigate `ApplyNeedsDecayHours` and the existing single-pass needs branch.

- [ ] **Step 8.4: Commit.**

```bash
git add "Assets/Tests/EditMode/MacroSimulator_SimulateOneHour_Tests.cs" "Assets/Tests/EditMode/MacroSimulator_SimulateOneHour_Tests.cs.meta"
git commit -m "test(macro-sim): assert 24xSimulateOneHour == 1xSimulateCatchUp invariant"
```

---

## Task 9: `MapController.HibernateForSkip` + `_pendingSkipWake` flag

**Files:**
- Modify: `Assets/Scripts/World/MapSystem/MapController.cs`

The skip needs to force-hibernate the active map even though a player is present. Add a public entry point that wraps `Hibernate()` with `forceHibernate: true` semantics, and a flag that tells `WakeUp()` to skip the redundant single-pass `SimulateCatchUp` (because the per-hour loop already advanced the data).

- [ ] **Step 9.1: Add the `_pendingSkipWake` field.**

In `MapController.cs`, near the other `[Header("Runtime State")]` fields (around line 47), add:

```csharp
[Tooltip("Set by HibernateForSkip; tells WakeUp() to skip SimulateCatchUp (the per-hour loop already advanced the data).")]
[SerializeField] private bool _pendingSkipWake = false;
public bool PendingSkipWake => _pendingSkipWake;
```

- [ ] **Step 9.2: Add `HibernateForSkip()`.**

After the existing private `Hibernate()` method (the one starting at line 1182), add a public sibling:

```csharp
/// <summary>
/// Force-hibernate this map for a time-skip. Identical to <see cref="Hibernate"/>
/// except it bypasses the "no players nearby" guard and sets <see cref="PendingSkipWake"/>
/// so the next <c>WakeUp()</c> skips the single-pass <c>SimulateCatchUp</c>
/// (the time-skip loop already ran <c>SimulateOneHour</c> per hour).
///
/// Server-only. Caller is <see cref="MWI.WorldSystem.TimeSkipController"/>.
/// </summary>
public void HibernateForSkip()
{
    if (IsHibernating)
    {
        Debug.LogWarning($"<color=orange>[MapController:HibernateForSkip]</color> Map '{MapId}' is already hibernating. Setting PendingSkipWake anyway.");
        _pendingSkipWake = true;
        return;
    }
    _pendingSkipWake = true;
    Hibernate();
}
```

- [ ] **Step 9.3: Gate `SimulateCatchUp` in `WakeUp()` on `_pendingSkipWake`.**

Find the `WakeUp` block where `MacroSimulator.SimulateCatchUp` is called (around line 1528). Wrap the call with the flag check, and clear the flag after consuming it:

```csharp
            if (_hibernationData != null)
            {
                if (_pendingSkipWake)
                {
                    Debug.Log($"<color=cyan>[MapController:WakeUp]</color> Map '{MapId}' has PendingSkipWake — skipping SimulateCatchUp (per-hour loop already ran).");
                    _pendingSkipWake = false;  // consume the flag
                }
                else
                {
                    // 4. Run MacroSimulator catch-up on NPCs
                    MacroSimulator.SimulateCatchUp(_hibernationData, _timeManager.CurrentDay, _timeManager.CurrentTime01, JobYields);
                }

                // 4b. Restore terrain cells after macro-simulation has updated them
                // (existing code unchanged below)
```

- [ ] **Step 9.4: Compile.** Console expected: zero errors.

- [ ] **Step 9.5: Commit.**

```bash
git add "Assets/Scripts/World/MapSystem/MapController.cs"
git commit -m "feat(map): MapController.HibernateForSkip + PendingSkipWake gating in WakeUp"
```

---

## Task 10: `TimeSkipController` — server-authoritative skip lifecycle

**Files:**
- Create: `Assets/Scripts/DayNightCycle/TimeSkipController.cs`

This is the single entry point for all triggers (dev panel, chat command, bed UI). Mirrors `GameSpeedController`'s singleton + NetworkBehaviour shape.

- [ ] **Step 10.1: Create the file.**

```csharp
// Assets/Scripts/DayNightCycle/TimeSkipController.cs
using System.Collections;
using Unity.Netcode;
using UnityEngine;
using MWI.WorldSystem;

namespace MWI.Time
{
    /// <summary>
    /// Server-authoritative singleton that owns the time-skip lifecycle:
    /// hibernate-skip-wake. Coexists with <see cref="GameSpeedController"/> —
    /// that one runs live simulation faster; this one freezes the active map
    /// and runs <see cref="MacroSimulator.SimulateOneHour"/> per hour.
    ///
    /// v1: single-player only (gated on ConnectedClients.Count == 1).
    /// v2: replace gate with "all connected players are simultaneously in a bed slot."
    /// </summary>
    public class TimeSkipController : NetworkBehaviour
    {
        public static TimeSkipController Instance { get; private set; }

        public const int MaxHours = 168;

        public bool IsSkipping { get; private set; }

        /// <summary>Fires on the server at the start of each skipped hour. Argument is the elapsed hours so far (1..hoursToSkip).</summary>
        public event System.Action<int, int> OnSkipHourTick;
        /// <summary>Fires on the server when the skip ends (completed or aborted).</summary>
        public event System.Action OnSkipEnded;
        /// <summary>Fires on the server when a skip starts.</summary>
        public event System.Action<int> OnSkipStarted;

        private bool _aborted;
        private Coroutine _runCoroutine;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(gameObject); return; }
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Main entry point. Server-only. Returns true if the skip was successfully started.
        /// </summary>
        public bool RequestSkip(int hours)
        {
            if (!IsServer)
            {
                Debug.LogWarning("<color=orange>[TimeSkip]</color> RequestSkip called on non-server peer. Ignored.");
                return false;
            }
            if (IsSkipping)
            {
                Debug.LogWarning("<color=orange>[TimeSkip]</color> RequestSkip rejected — already skipping.");
                return false;
            }
            if (hours < 1 || hours > MaxHours)
            {
                Debug.LogWarning($"<color=orange>[TimeSkip]</color> RequestSkip rejected — hours={hours} outside [1, {MaxHours}].");
                return false;
            }
            int connectedCount = NetworkManager.Singleton != null ? NetworkManager.Singleton.ConnectedClients.Count : 1;
            if (connectedCount > 1)
            {
                Debug.LogWarning($"<color=orange>[TimeSkip]</color> RequestSkip rejected — multiplayer not supported in v1 (connected={connectedCount}).");
                return false;
            }
            if (TimeManager.Instance == null)
            {
                Debug.LogError("<color=red>[TimeSkip]</color> RequestSkip rejected — TimeManager.Instance is null.");
                return false;
            }

            _runCoroutine = StartCoroutine(RunSkip(hours));
            return true;
        }

        public void RequestAbort()
        {
            if (!IsServer || !IsSkipping) return;
            _aborted = true;
            Debug.Log("<color=cyan>[TimeSkip]</color> RequestAbort received — loop will exit at next iteration.");
        }

        private IEnumerator RunSkip(int hours)
        {
            IsSkipping = true;
            _aborted = false;
            OnSkipStarted?.Invoke(hours);
            Debug.Log($"<color=cyan>[TimeSkip]</color> Skip starting: {hours} in-game hours.");

            // 1. Snapshot the active map(s) the player(s) are on.
            //    v1 single-player: there is exactly one player and one active map.
            MapController activeMap = ResolveActivePlayerMap();
            if (activeMap == null)
            {
                Debug.LogError("<color=red>[TimeSkip]</color> Could not resolve active player map. Aborting.");
                IsSkipping = false;
                OnSkipEnded?.Invoke();
                yield break;
            }

            // 2. EnterSkipMode — hibernate the active map and freeze player(s).
            //    Player.EnterSleep is called by the bed BEFORE RequestSkip in the bed flow.
            //    For dev / chat commands the player is NOT in a bed; we still freeze them
            //    in place by calling EnterSleep with their own current transform.
            Character[] players = ResolveLocalPlayers();
            foreach (var player in players)
            {
                if (player != null && !player.IsSleeping)
                    player.EnterSleep(player.transform);  // freeze in place; no anchor snap
            }
            activeMap.HibernateForSkip();

            // 3. Per-hour loop.
            int hoursElapsed = 0;
            while (hoursElapsed < hours)
            {
                if (_aborted) break;

                // Player-death auto-abort
                bool anyDead = false;
                foreach (var p in players) if (p == null || !p.IsAlive()) { anyDead = true; break; }
                if (anyDead) { Debug.Log("<color=cyan>[TimeSkip]</color> Aborting — player dead."); break; }

                int prevHour = TimeManager.Instance.CurrentHour;
                TimeManager.Instance.AdvanceOneHour();

                MacroSimulator.SimulateOneHour(
                    activeMap.HibernationData,
                    TimeManager.Instance.CurrentDay,
                    TimeManager.Instance.CurrentTime01,
                    activeMap.JobYields,
                    prevHour);

                hoursElapsed++;
                OnSkipHourTick?.Invoke(hoursElapsed, hours);

                yield return null;  // one frame per hour — lets cancel UI tick + abort flag flip
            }

            // 4. ExitSkipMode — wake the map and unfreeze the player(s).
            //    The map's PendingSkipWake flag suppresses the redundant SimulateCatchUp.
            activeMap.WakeUp();
            foreach (var player in players)
            {
                if (player != null && player.IsSleeping) player.ExitSleep();
            }

            // 5. Save world + player profile (matches existing bed-sleep save).
            if (SaveManager.Instance != null && players.Length > 0 && players[0] != null)
            {
                SaveManager.Instance.RequestSave(players[0]);
            }

            IsSkipping = false;
            OnSkipEnded?.Invoke();
            Debug.Log($"<color=cyan>[TimeSkip]</color> Skip ended. Hours actually skipped: {hoursElapsed}/{hours}.");
        }

        private MapController ResolveActivePlayerMap()
        {
            // v1: pick the first MapController whose ActivePlayers list is non-empty.
            var maps = UnityEngine.Object.FindObjectsByType<MapController>(FindObjectsSortMode.None);
            foreach (var m in maps)
            {
                if (m != null && !m.IsHibernating) return m;
            }
            return null;
        }

        private Character[] ResolveLocalPlayers()
        {
            if (NetworkManager.Singleton == null) return new Character[0];
            var list = new System.Collections.Generic.List<Character>();
            foreach (var kvp in NetworkManager.Singleton.ConnectedClients)
            {
                var po = kvp.Value.PlayerObject;
                if (po != null && po.TryGetComponent(out Character c)) list.Add(c);
            }
            return list.ToArray();
        }
    }
}
```

- [ ] **Step 10.2: Add the `TimeSkipController` prefab.**

The controller needs a `NetworkObject` and must spawn at session start, alongside `GameSpeedController`. Two options:

**(a)** Add a `TimeSkipController` component on the same GameObject as `GameSpeedController` (it's already a `NetworkBehaviour` singleton in the scene). Confirm `GameSpeedController`'s GameObject has a `NetworkObject` and is registered in the NetworkPrefab list / scene-spawned. Add `TimeSkipController` next to it.
**(b)** Create a dedicated prefab and register it.

Pick (a) for v1. In Unity Editor:
1. Find the GameObject that hosts `GameSpeedController` (search the active scene's Hierarchy for "GameSpeed").
2. Add Component → `TimeSkipController`.
3. Save the scene (Ctrl+S).

If MCP is offline, pause and ask the user to do this manually.

- [ ] **Step 10.3: Compile + smoke-test.**

In a single-player Play-mode session, open the chat bar and (the chat command lands in Task 11, so for now) trigger the skip via the Editor: select the GameObject hosting `TimeSkipController`, in the Inspector right-click the component → invoke `RequestSkip(8)` via reflection or temporarily expose a `[ContextMenu]` button. Verify the Console shows the skip-start log, hour-tick logs, skip-end log. Verify the in-game clock advanced 8 hours and the map respawned.

If you can't easily trigger it without a UI, leave verification for Task 11/12; the controller is exercised end-to-end there.

- [ ] **Step 10.4: Commit.**

```bash
git add "Assets/Scripts/DayNightCycle/TimeSkipController.cs" "Assets/Scripts/DayNightCycle/TimeSkipController.cs.meta"
# Plus any scene file you saved that contains the new component
git commit -m "feat(time-skip): TimeSkipController hibernate-skip-wake server coroutine"
```

---

## Task 11: `/timeskip <hours>` chat command

**Files:**
- Modify: `Assets/Scripts/Debug/DevMode/DevChatCommands.cs`

- [ ] **Step 11.1: Register the command.**

In `DevChatCommands.cs`, in the `switch (cmd)` block (around line 24), add a new case:

```csharp
            case "timeskip":
                HandleTimeSkip(parts);
                break;
```

- [ ] **Step 11.2: Add the handler.**

Append to the same file, near `HandleDevmode`:

```csharp
private static void HandleTimeSkip(string[] parts)
{
    // Host check — same shape as devmode
    if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && !NetworkManager.Singleton.IsServer)
    {
        Debug.LogWarning("<color=orange>[DevChat]</color> /timeskip is host-only.");
        return;
    }
    if (parts.Length < 2 || !int.TryParse(parts[1], out int hours))
    {
        Debug.Log("<color=magenta>[DevChat]</color> Usage: /timeskip <hours>  (1-168)");
        return;
    }
    if (MWI.Time.TimeSkipController.Instance == null)
    {
        Debug.LogError("<color=red>[DevChat]</color> TimeSkipController is not present in the scene.");
        return;
    }
    bool ok = MWI.Time.TimeSkipController.Instance.RequestSkip(hours);
    Debug.Log($"<color=magenta>[DevChat]</color> /timeskip {hours} → {(ok ? "started" : "rejected (see prior log)")}.");
}
```

- [ ] **Step 11.3: Compile + smoke-test.**

Play a single-player scene. Open the chat bar. Type `/timeskip 8`. Console shows the start log, 8 hour-tick logs, end log. The in-game clock advanced 8 hours. The map's NPCs respawned in their schedule-correct positions.

Test the rejection paths: `/timeskip 0`, `/timeskip 200` (out of range), `/timeskip foo` (not parseable). Each should log a usage line; no skip should start.

- [ ] **Step 11.4: Commit.**

```bash
git add "Assets/Scripts/Debug/DevMode/DevChatCommands.cs"
git commit -m "feat(devmode): /timeskip <hours> chat command"
```

---

## Task 12: `DevTimeSkipModule` script + DevModePanel tab

**Files:**
- Create: `Assets/Scripts/Debug/DevMode/DevTimeSkipModule.cs`
- Modify: `Assets/Prefabs/UI/DevModePanel.prefab` (Inspector — Unity MCP)

- [ ] **Step 12.1: Create the module script.**

```csharp
// Assets/Scripts/Debug/DevMode/DevTimeSkipModule.cs
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Dev-mode panel module that exposes a single "Skip N hours" control. Lives on
/// a tab content GameObject inside DevModePanel; the tab entry is added in the
/// DevModePanel prefab via Inspector. Clicking the button delegates to
/// <see cref="MWI.Time.TimeSkipController"/> — same entry point as the chat command.
/// </summary>
public class DevTimeSkipModule : MonoBehaviour
{
    [SerializeField] private TMP_InputField _hoursInput;
    [SerializeField] private Button _skipButton;
    [SerializeField] private TMP_Text _statusLabel;

    private void Awake()
    {
        if (_skipButton != null) _skipButton.onClick.AddListener(OnSkipClicked);
    }

    private void OnDestroy()
    {
        if (_skipButton != null) _skipButton.onClick.RemoveListener(OnSkipClicked);
    }

    private void OnSkipClicked()
    {
        if (_hoursInput == null || string.IsNullOrWhiteSpace(_hoursInput.text))
        {
            SetStatus("Enter a number of hours (1–168).");
            return;
        }
        if (!int.TryParse(_hoursInput.text, out int hours))
        {
            SetStatus($"'{_hoursInput.text}' is not a number.");
            return;
        }
        if (MWI.Time.TimeSkipController.Instance == null)
        {
            SetStatus("TimeSkipController not in scene.");
            return;
        }
        bool ok = MWI.Time.TimeSkipController.Instance.RequestSkip(hours);
        SetStatus(ok ? $"Skipping {hours}h…" : "Skip rejected — see Console for reason.");
    }

    private void SetStatus(string msg)
    {
        if (_statusLabel != null) _statusLabel.text = msg;
        Debug.Log($"<color=magenta>[DevTimeSkip]</color> {msg}");
    }
}
```

- [ ] **Step 12.2: Add the tab to `DevModePanel.prefab`.**

Use Unity MCP (or the Editor manually if MCP is offline):
1. Open the `DevModePanel` prefab.
2. Under the panel's content root, duplicate one of the existing tab content GameObjects (e.g. `Tab_Spawn`) and rename it `Tab_TimeSkip`. Strip its inner content (delete child UI from the duplicate so you have a clean Image + RectTransform shell).
3. Add a child `Button` named `SkipButton` with a `TMP_Text` child labelled "Skip".
4. Add a child `TMP_InputField` named `HoursInput` (placeholder "Hours (1-168)").
5. Add a child `TMP_Text` named `StatusLabel` (empty initial text).
6. Add the `DevTimeSkipModule` component to `Tab_TimeSkip`. Wire `_hoursInput`, `_skipButton`, `_statusLabel` to the children created above.
7. In the prefab's Hierarchy, find an existing tab button (e.g. `TabButton_Spawn`) on the tab bar; duplicate it as `TabButton_TimeSkip` and label it "Time Skip".
8. On the prefab root's `DevModePanel` component, expand `_tabs` → click `+` to add a new entry. Set `TabButton` to `TabButton_TimeSkip`, `Content` to `Tab_TimeSkip`.
9. Save the prefab.

If MCP is offline: pause and hand off to the user with the steps above as a manual checklist.

- [ ] **Step 12.3: Smoke-test.**

In Play mode, enable Dev Mode (`/devmode on`). Click the new "Time Skip" tab. Enter `8`. Click Skip. Same expected behavior as the chat command in Task 11. Test rejection paths: empty field, non-numeric text, out-of-range (`0`, `200`).

- [ ] **Step 12.4: Commit.**

```bash
git add "Assets/Scripts/Debug/DevMode/DevTimeSkipModule.cs" "Assets/Scripts/Debug/DevMode/DevTimeSkipModule.cs.meta" "Assets/Prefabs/UI/DevModePanel.prefab"
git commit -m "feat(devmode): DevTimeSkipModule + Time Skip tab in DevModePanel"
```

---

## Task 13: `UI_TimeSkipOverlay` + `UI_BedSleepPrompt`

**Files:**
- Create: `Assets/Scripts/UI/UI_TimeSkipOverlay.cs`
- Create: `Assets/Scripts/UI/UI_BedSleepPrompt.cs`
- Plus: two prefabs in `Assets/UI/` (Inspector — Unity MCP)

- [ ] **Step 13.1: Create `UI_TimeSkipOverlay`.**

```csharp
// Assets/Scripts/UI/UI_TimeSkipOverlay.cs
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Fade-to-black overlay shown during a time-skip. Subscribes to
/// <see cref="MWI.Time.TimeSkipController"/> events; shows progress and a Cancel button.
/// One instance lives in the persistent UI canvas.
/// </summary>
public class UI_TimeSkipOverlay : MonoBehaviour
{
    [SerializeField] private CanvasGroup _canvasGroup;
    [SerializeField] private TMP_Text _hourLabel;
    [SerializeField] private Button _cancelButton;

    private void Awake()
    {
        if (_canvasGroup != null) _canvasGroup.alpha = 0f;
        gameObject.SetActive(false);
        if (_cancelButton != null) _cancelButton.onClick.AddListener(OnCancelClicked);
    }

    private void OnDestroy()
    {
        if (_cancelButton != null) _cancelButton.onClick.RemoveListener(OnCancelClicked);
        UnsubscribeFromController();
    }

    private void OnEnable()  => SubscribeToController();
    private void OnDisable() => UnsubscribeFromController();

    private void SubscribeToController()
    {
        if (MWI.Time.TimeSkipController.Instance == null) return;
        MWI.Time.TimeSkipController.Instance.OnSkipStarted += HandleStarted;
        MWI.Time.TimeSkipController.Instance.OnSkipHourTick += HandleHourTick;
        MWI.Time.TimeSkipController.Instance.OnSkipEnded += HandleEnded;
    }

    private void UnsubscribeFromController()
    {
        if (MWI.Time.TimeSkipController.Instance == null) return;
        MWI.Time.TimeSkipController.Instance.OnSkipStarted -= HandleStarted;
        MWI.Time.TimeSkipController.Instance.OnSkipHourTick -= HandleHourTick;
        MWI.Time.TimeSkipController.Instance.OnSkipEnded -= HandleEnded;
    }

    private void HandleStarted(int totalHours)
    {
        gameObject.SetActive(true);
        if (_canvasGroup != null) _canvasGroup.alpha = 1f;
        if (_hourLabel != null) _hourLabel.text = $"Skipping… 0 / {totalHours} h";
    }

    private void HandleHourTick(int elapsed, int total)
    {
        if (_hourLabel != null) _hourLabel.text = $"Skipping… {elapsed} / {total} h";
    }

    private void HandleEnded()
    {
        if (_canvasGroup != null) _canvasGroup.alpha = 0f;
        gameObject.SetActive(false);
    }

    private void OnCancelClicked()
    {
        if (MWI.Time.TimeSkipController.Instance != null)
            MWI.Time.TimeSkipController.Instance.RequestAbort();
    }
}
```

- [ ] **Step 13.2: Create `UI_BedSleepPrompt`.**

```csharp
// Assets/Scripts/UI/UI_BedSleepPrompt.cs
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// In-world modal that appears when the local player uses a <see cref="BedFurniture"/>.
/// Shows a slider 1–168 with a default of "until 06:00 next day" and routes the
/// confirmation through <see cref="MWI.Time.TimeSkipController"/> — same entry point
/// as the chat command and dev panel.
/// Hidden by default. Shown by external triggers (bed Use action — wired post-merge).
/// </summary>
public class UI_BedSleepPrompt : MonoBehaviour
{
    [SerializeField] private Slider _hoursSlider;
    [SerializeField] private TMP_Text _hoursLabel;
    [SerializeField] private Button _confirmButton;
    [SerializeField] private Button _cancelButton;

    private void Awake()
    {
        gameObject.SetActive(false);
        if (_hoursSlider != null) _hoursSlider.onValueChanged.AddListener(OnSliderChanged);
        if (_confirmButton != null) _confirmButton.onClick.AddListener(OnConfirmClicked);
        if (_cancelButton != null) _cancelButton.onClick.AddListener(OnCancelClicked);
    }

    private void OnDestroy()
    {
        if (_hoursSlider != null) _hoursSlider.onValueChanged.RemoveListener(OnSliderChanged);
        if (_confirmButton != null) _confirmButton.onClick.RemoveListener(OnConfirmClicked);
        if (_cancelButton != null) _cancelButton.onClick.RemoveListener(OnCancelClicked);
    }

    public void Show()
    {
        gameObject.SetActive(true);
        if (_hoursSlider != null)
        {
            _hoursSlider.minValue = 1;
            _hoursSlider.maxValue = MWI.Time.TimeSkipController.MaxHours;
            _hoursSlider.wholeNumbers = true;
            _hoursSlider.value = ComputeDefaultUntilSixAm();
            OnSliderChanged(_hoursSlider.value);
        }
    }

    public void Hide() => gameObject.SetActive(false);

    private float ComputeDefaultUntilSixAm()
    {
        if (MWI.Time.TimeManager.Instance == null) return 8f;
        int currentHour = MWI.Time.TimeManager.Instance.CurrentHour;
        int target = 6;  // 06:00 next morning
        int delta = (target - currentHour + 24) % 24;
        if (delta == 0) delta = 24;
        return delta;
    }

    private void OnSliderChanged(float value)
    {
        if (_hoursLabel != null) _hoursLabel.text = $"Skip {(int)value} h";
    }

    private void OnConfirmClicked()
    {
        int hours = _hoursSlider != null ? (int)_hoursSlider.value : 0;
        if (MWI.Time.TimeSkipController.Instance == null) { Hide(); return; }
        bool ok = MWI.Time.TimeSkipController.Instance.RequestSkip(hours);
        if (ok) Hide();
        else Debug.LogWarning($"<color=orange>[UI_BedSleepPrompt]</color> RequestSkip({hours}) rejected — see Console.");
    }

    private void OnCancelClicked() => Hide();
}
```

- [ ] **Step 13.3: Build the two prefabs.**

Two new prefabs under `Assets/UI/HUD/` (per project rule on UI prefab folder layout):
1. `UI_TimeSkipOverlay.prefab` — full-screen `Image` (black, alpha-controlled by CanvasGroup), centered `TMP_Text` (the hour label), bottom-right `Button` (Cancel). Add the `UI_TimeSkipOverlay` component on the root, wire the three serialized fields. Drop it into the persistent UI canvas (the one where `UI_ChatBar` lives).
2. `UI_BedSleepPrompt.prefab` — small modal panel: `Slider`, `TMP_Text` for "Skip N h" label, two `Button`s (Confirm, Cancel). Add `UI_BedSleepPrompt` component on the root, wire the four serialized fields. Drop it into the same persistent UI canvas, hidden by default.

If MCP is offline, pause and hand off these prefab steps to the user.

- [ ] **Step 13.4: Smoke-test the overlay.**

In Play mode, run `/timeskip 6`. Expected: the black overlay appears, the hour label counts up `0/6`, `1/6`, …, `6/6`, then the overlay fades out. Click Cancel mid-skip on a longer `/timeskip 12` — the loop exits at the next hour boundary (≤1 second).

The bed prompt is not yet wired to a real bed Use action in v1 — the existing `SleepBehaviour` is NPC-only, and a player-side bed interaction is a separate authoring task on a `BedFurnitureInteractable`. Document this gap in the wiki Task 14; for now the prompt is exercised in isolation by selecting the prefab in the scene and calling `Show()` from the Inspector / a temporary `[ContextMenu]`.

- [ ] **Step 13.5: Commit.**

```bash
git add "Assets/Scripts/UI/UI_TimeSkipOverlay.cs" "Assets/Scripts/UI/UI_TimeSkipOverlay.cs.meta" "Assets/Scripts/UI/UI_BedSleepPrompt.cs" "Assets/Scripts/UI/UI_BedSleepPrompt.cs.meta" "Assets/UI/HUD/UI_TimeSkipOverlay.prefab" "Assets/UI/HUD/UI_TimeSkipOverlay.prefab.meta" "Assets/UI/HUD/UI_BedSleepPrompt.prefab" "Assets/UI/HUD/UI_BedSleepPrompt.prefab.meta"
git commit -m "feat(ui): UI_TimeSkipOverlay (fade + cancel) and UI_BedSleepPrompt (slider, default until 06:00)"
```

---

## Task 14: Wiki + skill doc updates (rules #28, #29b)

**Files:**
- Create: `wiki/systems/world-time-skip.md`
- Modify: `wiki/systems/world-macro-simulation.md`
- Modify: `.agent/skills/world-system/SKILL.md`

Per project rules #28 (skill update) and #29b (wiki update), every system change ships with its docs current.

- [ ] **Step 14.1: Read the wiki schema.**

Re-read `wiki/CLAUDE.md` end-to-end. The frontmatter, sections, sources, and wikilink rules are mandatory and apply to every wiki edit.

- [ ] **Step 14.2: Create `wiki/systems/world-time-skip.md`.**

Use `wiki/_templates/system.md` as the starting point. Required frontmatter fields (per `wiki/CLAUDE.md` §3): `type: system`, `title`, `tags`, `created: 2026-04-27`, `updated: 2026-04-27`, `sources: []`, `related: []`, `status: wip`, `confidence: high`, `primary_agent: world-system-specialist`, `secondary_agents: [save-persistence-specialist]`, `owner_code_path: Assets/Scripts/DayNightCycle/`, `depends_on`, `depended_on_by`. Body must follow the 10-section system layout. Cover:
- Purpose: hibernate-skip-wake path coexisting with `GameSpeedController`.
- Responsibilities + non-responsibilities.
- Key classes: `TimeSkipController`, `TimeManager.AdvanceOneHour`, `MacroSimulator.SimulateOneHour`, `MapController.HibernateForSkip` / `PendingSkipWake`.
- Public API: `TimeSkipController.RequestSkip(int)`, `RequestAbort()`, events `OnSkipStarted` / `OnSkipHourTick` / `OnSkipEnded`.
- Data flow: trigger → `RequestSkip` → coroutine loop (per-hour `AdvanceOneHour` + `SimulateOneHour`) → `WakeUp()` → save.
- Dependencies: link `[[world-macro-simulation]]`, `[[world-map-hibernation]]`, `[[character-needs]]`, `[[time-manager]]` (if exists), `[[save-load]]`.
- State & persistence: in-memory `LastHibernationTime` advanced each hour; no per-hour disk write; final save on skip end.
- Known gotchas: per-hour day-boundary gating (resource regen / yields / city growth); single-player gate v1; `OnNewDay` subscriber audit; existing `_pendingSkipWake` flag must be cleared in `WakeUp` exactly once.
- Open questions: combat / danger detector; multiplayer auto-trigger watcher.
- Change log entry: `- 2026-04-27 — Initial pass (TimeSkipController, BedFurniture, EnterSleep/ExitSleep). — Claude / [[kevin]]`.
- Sources: link `Assets/Scripts/DayNightCycle/TimeSkipController.cs`, `Assets/Scripts/World/MapSystem/MacroSimulator.cs`, `.agent/skills/world-system/SKILL.md`, the design spec, the implementation plan.

- [ ] **Step 14.3: Update `wiki/systems/world-macro-simulation.md`.**

In the existing `wiki/systems/world-macro-simulation.md`:
- Bump `updated:` to `2026-04-27`.
- Add a "Per-hour entry point" section under `## Catch-up order (strict)` describing `SimulateOneHour` and the day-boundary gating rule. Cross-link to `[[world-time-skip]]`.
- Append to `## Change log`: `- 2026-04-27 — Added SimulateOneHour entry point with day-boundary gating. Used by [[world-time-skip]]. — Claude / [[kevin]]`.
- Add `[[world-time-skip]]` to `related:` and `depended_on_by:`.

- [ ] **Step 14.4: Update `.agent/skills/world-system/SKILL.md`.**

Add a new section "Time Skip (player-initiated macro-sim loop)" after the existing macro-sim section. Cover the procedural how-to:
- "When implementing a new system that needs hour-grained offline state, decide whether to slot into `SimulateOneHour` (hour-grained block) or the existing `SimulateCatchUp` (single-pass block). The two paths now share helpers (`ApplyNeedsDecayHours`, `SnapPositionFromSchedule`); add new shared helpers if your math fits both paths."
- "When triggering a skip programmatically (quest scripts, events, future world systems), call `TimeSkipController.Instance.RequestSkip(hours)`. Single-player only in v1."
- "Day-boundary gating: any new step that integrates over `daysPassed` must run in the `crossedDayBoundary` block of `SimulateOneHour`, not the hour-grained block, or it will floor-to-0 every hour."

- [ ] **Step 14.5: Run the wiki diff-preview rule.**

Per `wiki/CLAUDE.md` §8, since this touches >5 wiki/skill files? No — this touches 3. Below threshold; proceed without preview but show the diff in the commit message body.

- [ ] **Step 14.6: Commit.**

```bash
git add "wiki/systems/world-time-skip.md" "wiki/systems/world-macro-simulation.md" ".agent/skills/world-system/SKILL.md"
git commit -m "docs(wiki): add world-time-skip system page; update macro-sim + skill for SimulateOneHour"
```

---

## Post-merge follow-ups (NOT in this plan — track as separate tickets)

These are listed for traceability against the spec's "Out (deferred)" section. They are **not** part of this plan and must not be implemented under it.

- v2 multiplayer time-skip — replace `ConnectedClients.Count == 1` gate in `TimeSkipController.RequestSkip` with a watcher that fires when all connected players are simultaneously in a bed slot.
- Combat / danger-detector auto-abort.
- Scene-pass migration: replace existing plain-`Furniture` beds in built scenes with `BedFurniture` instances and author per-prefab `_slots` lists.
- Wire `BedFurnitureInteractable` so a player using a bed opens `UI_BedSleepPrompt` (today the prompt exists but has no in-world trigger).
- Day-grained correctness tests: extend the EditMode suite to cover resource regen / inventory yield / city growth equivalence on day rollover.

---

## Self-review (writing-plans skill checklist)

**Spec coverage** — every section of the spec maps to at least one task:

| Spec section | Task(s) |
|---|---|
| `BedFurniture` + `BedSlot` data model | 1 |
| `Character.IsSleeping` + `EnterSleep` / `ExitSleep` | 2 |
| BedFurniture wiring to Character | 3 |
| `SleepBehaviour` migration | 4 |
| `PlayerController` early-out | 5 |
| `TimeManager.AdvanceOneHour` | 6 |
| `MacroSimulator.SimulateOneHour` + day-boundary gating | 7 |
| 24×OneHour ≡ 1×CatchUp invariant test | 8 |
| `MapController.HibernateForSkip` + `_pendingSkipWake` | 9 |
| `TimeSkipController` lifecycle | 10 |
| `/timeskip` chat command | 11 |
| `DevTimeSkipModule` + DevModePanel tab | 12 |
| `UI_TimeSkipOverlay` + `UI_BedSleepPrompt` | 13 |
| Wiki + skill doc updates | 14 |
| Single-player v1 gate | 10 (Step 10.1) |
| Player-death auto-abort | 10 (Step 10.1) |
| Manual cancel | 10 (RequestAbort) + 13 (Cancel button) |
| `ConnectedClients.Count == 1` gate | 10 (Step 10.1) |
| Save on skip end | 10 (Step 10.1, end of `RunSkip`) |
| Migration note (legacy beds) | "Post-merge follow-ups" |

No gaps.

**Placeholder scan** — no TBD/TODO/"implement later"/"add appropriate error handling" in the plan body. Every code step shows the actual code.

**Type consistency:**
- `TimeSkipController.RequestSkip(int hours)` — used identically in Tasks 10, 11, 12, 13.
- `TimeSkipController.RequestAbort()` — Task 10 declares, Task 13 uses.
- `BedFurniture.UseSlot(int, Character)` / `ReleaseSlot(int)` / `FindFreeSlotIndex()` / `GetSlotIndexFor(Character)` — Task 1 declares, Tasks 3/4 use.
- `Character.EnterSleep(Transform)` / `ExitSleep()` / `IsSleeping` / `NetworkIsSleeping` — Task 2 declares, Tasks 3/5/10 use.
- `MapController.HibernateForSkip()` / `PendingSkipWake` / `HibernationData` / `JobYields` — Task 9 declares the first two; the latter two are pre-existing.
- `MacroSimulator.SimulateOneHour(MapSaveData, int, float, JobYieldRegistry, int)` — Task 7 declares, Task 10 uses.
- `TimeManager.AdvanceOneHour()` — Task 6 declares, Task 10 uses.

All consistent.
