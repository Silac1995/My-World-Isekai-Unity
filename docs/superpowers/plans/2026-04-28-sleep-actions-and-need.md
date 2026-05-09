# Sleep Actions & NeedSleep Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `NeedSleep` (passive networked need) + `CharacterAction_Sleep` / `CharacterAction_SleepOnFurniture` (rule #22 wrappers around the existing EnterSleep/ExitSleep + BedFurniture.UseSlot lifecycle) + `BedFurnitureInteractable` (player UI entry) + per-hour macro-sim restoration + post-skip save fan-out + wake-on-attack + player ground-sleep key.

**Architecture:** All gameplay state mutations route through `CharacterAction` per rule #22. Sleep is a passive `CharacterNeed` (no GOAP). Live restoration ticks every 5s; offline restoration runs per-hour inside `MacroSimulator.SimulateOneHour`. The save trigger lives only at time-skip end (no churn on accidental wake/cancel). Default sleep duration is 7h with a brief pre-skip fade.

**Tech Stack:** Unity 2022.3+ / Unity Netcode for GameObjects (NGO) / C#. Server-authoritative pattern throughout. No unit-test framework — verification is editor-play + Console log inspection.

**Spec:** [`docs/superpowers/specs/2026-04-28-sleep-actions-and-need-design.md`](../specs/2026-04-28-sleep-actions-and-need-design.md)

---

## Pre-flight (do once before starting)

- [ ] **Verify Unity Editor opens cleanly with no compile errors.**

Run: open the project in Unity, check `Console` for `Compile Errors: 0`.

If compile errors exist, stop and resolve before starting.

- [ ] **Confirm working branch.**

Run: `git status` — must be on `multiplayyer` (or your active feature branch). Working tree should be clean except for the design spec already committed.

---

## Task 1: NeedSleep math constants

**Files:**
- Create: `Assets/Scripts/Character/CharacterNeeds/NeedSleepMath.cs`

- [ ] **Step 1: Create the constants file**

Create `Assets/Scripts/Character/CharacterNeeds/NeedSleepMath.cs`:

```csharp
namespace MWI.Needs
{
    /// <summary>
    /// Pure-math constants and helpers for NeedSleep. Mirrors NeedHungerMath.
    /// Lives in MWI.Needs so it can be referenced from MacroSimulator (offline
    /// restoration) without dragging the full NeedSleep MonoBehaviour graph.
    /// </summary>
    public static class NeedSleepMath
    {
        public const float DEFAULT_MAX = 100f;
        public const float DEFAULT_START = 80f;
        public const float DEFAULT_LOW_THRESHOLD = 25f;

        // Decay per TimeManager phase (4 phases/day → fully drained in ~1 day awake).
        public const float DEFAULT_DECAY_PER_PHASE = 25f;

        // Live action restoration chunks (per 5s tick).
        public const float LIVE_GROUND_RESTORE_PER_TICK = 10f;
        public const float LIVE_BED_RESTORE_PER_TICK = 25f;

        // Offline (macro-sim) restoration chunks (per hour during a time skip).
        // Bed = full restore in ~2h. Ground = ~5h.
        public const float OFFLINE_BED_RESTORE_PER_HOUR = 50f;
        public const float OFFLINE_GROUND_RESTORE_PER_HOUR = 20f;
    }
}
```

- [ ] **Step 2: Verify compilation**

Switch to Unity Editor. Wait for the script reload. Confirm `Console` shows `Compile Errors: 0`.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Character/CharacterNeeds/NeedSleepMath.cs Assets/Scripts/Character/CharacterNeeds/NeedSleepMath.cs.meta
git commit -m "feat(needs): add NeedSleepMath constants

Pure-math constants for NeedSleep mirroring NeedHungerMath.
Defines max/start/decay-per-phase plus live tick + offline per-hour
restore rates for ground and bed sleep."
```

---

## Task 2: NeedSleep class (mirrors NeedHunger)

**Files:**
- Create: `Assets/Scripts/Character/CharacterNeeds/NeedSleep.cs`

- [ ] **Step 1: Create NeedSleep.cs**

Create `Assets/Scripts/Character/CharacterNeeds/NeedSleep.cs`:

```csharp
using System;
using System.Collections.Generic;
using MWI.Needs;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative sleep need. Passive (Option A from the design):
/// decays/restores but does NOT drive GOAP. The actual current value lives in
/// <c>CharacterNeeds._networkedSleep</c> (a <see cref="NetworkVariable{T}"/> of
/// type float, server-write / everyone-read). NeedSleep is a thin wrapper that:
/// <list type="bullet">
/// <item>Reads the network value through <c>CharacterNeeds.NetworkedSleepValue</c>.</item>
/// <item>Routes writes through the server (direct NV write if server, else ServerRpc).</item>
/// <item>Bridges <c>NetworkVariable.OnValueChanged</c> to public events
///       (<see cref="OnValueChanged"/>, <see cref="OnExhaustedChanged"/>).</item>
/// <item>Decays once per TimeManager phase on the server.</item>
/// </list>
/// </summary>
public class NeedSleep : CharacterNeed
{
    public const float DEFAULT_START = NeedSleepMath.DEFAULT_START;

    private readonly CharacterNeeds _owner;

    private bool _phaseSubscribed;
    private bool _bridgeBound;
    private bool _isExhausted;

    public event Action<float> OnValueChanged;
    public event Action<bool> OnExhaustedChanged;

    public float MaxValue => NeedSleepMath.DEFAULT_MAX;
    public bool IsExhausted => _isExhausted;

    public override float CurrentValue
    {
        get
        {
            if (_owner == null) return 0f;
            return _owner.NetworkedSleepValue;
        }
        set
        {
            if (_owner == null) return;

            float current = _owner.NetworkedSleepValue;
            float clampedTarget = Mathf.Clamp(value, 0f, MaxValue);

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
            {
                _owner.ServerSetSleep(clampedTarget);
            }
            else
            {
                float delta = clampedTarget - current;
                if (Mathf.Approximately(delta, 0f)) return;
                _owner.RequestAdjustSleepRpc(delta);
            }
        }
    }

    public NeedSleep(Character character, CharacterNeeds owner) : base(character)
    {
        _owner = owner;
    }

    public void IncreaseValue(float amount)
    {
        if (_owner == null || Mathf.Approximately(amount, 0f)) return;

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
            _owner.ServerSetSleep(_owner.NetworkedSleepValue + amount);
        else
            _owner.RequestAdjustSleepRpc(amount);
    }

    public void DecreaseValue(float amount)
    {
        if (_owner == null || Mathf.Approximately(amount, 0f)) return;

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
            _owner.ServerSetSleep(_owner.NetworkedSleepValue - amount);
        else
            _owner.RequestAdjustSleepRpc(-amount);
    }

    public bool IsLow() => CurrentValue <= NeedSleepMath.DEFAULT_LOW_THRESHOLD;

    public void TrySubscribeToPhase()
    {
        if (_phaseSubscribed) return;
        if (MWI.Time.TimeManager.Instance == null) return;
        MWI.Time.TimeManager.Instance.OnPhaseChanged += HandlePhaseChanged;
        _phaseSubscribed = true;
    }

    public void UnsubscribeFromPhase()
    {
        if (!_phaseSubscribed) return;
        if (MWI.Time.TimeManager.Instance != null)
            MWI.Time.TimeManager.Instance.OnPhaseChanged -= HandlePhaseChanged;
        _phaseSubscribed = false;
    }

    public void BindNetworkBridge()
    {
        if (_bridgeBound || _owner == null) return;
        _owner.SubscribeNetworkedSleepChanged(HandleNetworkedSleepChanged);
        _bridgeBound = true;

        float current = _owner.NetworkedSleepValue;
        _isExhausted = current <= 0f;

        try { OnValueChanged?.Invoke(current); }
        catch (Exception e) { Debug.LogException(e); }

        if (_isExhausted)
        {
            try { OnExhaustedChanged?.Invoke(true); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }

    public void UnbindNetworkBridge()
    {
        if (!_bridgeBound || _owner == null) return;
        _owner.UnsubscribeNetworkedSleepChanged(HandleNetworkedSleepChanged);
        _bridgeBound = false;
    }

    private void HandleNetworkedSleepChanged(float previous, float current)
    {
        try { OnValueChanged?.Invoke(current); }
        catch (Exception e) { Debug.LogException(e); }

        bool nowExhausted = current <= 0f;
        if (nowExhausted != _isExhausted)
        {
            _isExhausted = nowExhausted;
            try { OnExhaustedChanged?.Invoke(_isExhausted); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }

    private void HandlePhaseChanged(MWI.Time.DayPhase _)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
        if (_character == null) return;

        // Don't decay while the character is sleeping — they're literally restoring,
        // not depleting. This avoids "wake up, see sleep meter dropped during the
        // phase tick that fired mid-skip" weirdness.
        if (_character.IsSleeping) return;

        try
        {
            DecreaseValue(NeedSleepMath.DEFAULT_DECAY_PER_PHASE);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    // Passive — no GOAP integration.
    public override bool IsActive() => false;
    public override float GetUrgency() => 0f;
    public override GoapGoal GetGoapGoal() => null;
    public override List<GoapAction> GetGoapActions() => new List<GoapAction>();
}
```

- [ ] **Step 2: Verify compilation**

Wait for Unity reload. **Expected: compile errors** about `CharacterNeeds.NetworkedSleepValue`, `ServerSetSleep`, `RequestAdjustSleepRpc`, `SubscribeNetworkedSleepChanged`, `UnsubscribeNetworkedSleepChanged` — those will be added in Task 3.

- [ ] **Step 3: Do not commit yet** — Task 3 finishes the dependency.

---

## Task 3: Wire NeedSleep into CharacterNeeds

**Files:**
- Modify: `Assets/Scripts/Character/CharacterNeeds/CharacterNeeds.cs`

- [ ] **Step 1: Add the `_networkedSleep` NetworkVariable + accessors**

In `CharacterNeeds.cs`, find the existing `_networkedHunger` NetworkVariable block (around line 32) and add a sibling block right below it:

```csharp
    // ── Server-authoritative sleep ──────────────────────────────────────────
    // The single source of truth for NeedSleep.CurrentValue across all peers.
    // Server writes (phase decay, sleep restore); clients read via OnValueChanged bridge.
    private NetworkVariable<float> _networkedSleep = new NetworkVariable<float>(
        NeedSleepMath.DEFAULT_MAX,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public float NetworkedSleepValue => _networkedSleep.Value;

    public void SubscribeNetworkedSleepChanged(NetworkVariable<float>.OnValueChangedDelegate handler)
    {
        _networkedSleep.OnValueChanged += handler;
    }

    public void UnsubscribeNetworkedSleepChanged(NetworkVariable<float>.OnValueChangedDelegate handler)
    {
        _networkedSleep.OnValueChanged -= handler;
    }

    public void ServerSetSleep(float value)
    {
        if (!IsSpawned)
        {
            if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsServer) return;
        }
        else if (!IsServer)
        {
            Debug.LogWarning($"<color=orange>[CharacterNeeds]</color> ServerSetSleep called on non-server peer for {gameObject.name}. Ignored.");
            return;
        }

        float clamped = Mathf.Clamp(value, 0f, NeedSleepMath.DEFAULT_MAX);
        _networkedSleep.Value = clamped;
    }

    [Rpc(SendTo.Server)]
    public void RequestAdjustSleepRpc(float amount)
    {
        if (!IsServer) return;
        ServerSetSleep(_networkedSleep.Value + amount);
    }
```

- [ ] **Step 2: Register NeedSleep in `Awake`**

Find the `Awake` method's `_allNeeds` initialization block (around line 109-120). Add registration right after `NeedHunger`:

```csharp
            // NeedHunger needs a back-reference to CharacterNeeds so it can read/write the NetworkVariable.
            var hunger = new NeedHunger(_character, this);
            _allNeeds.Add(hunger);

            var sleep = new NeedSleep(_character, this);
            _allNeeds.Add(sleep);
```

- [ ] **Step 3: Wire `OnNetworkPreSpawn` seed**

Find `OnNetworkPreSpawn` (around line 123). Add a sibling line after the `_networkedHunger.Value = NeedHunger.DEFAULT_START;` assignment:

```csharp
        if (networkManager != null && networkManager.IsServer)
        {
            _networkedHunger.Value = NeedHunger.DEFAULT_START;
            _networkedSleep.Value = NeedSleep.DEFAULT_START;
        }
```

- [ ] **Step 4: Wire `OnNetworkSpawn` (subscribe to phase + bind bridge)**

Find `OnNetworkSpawn` (around line 138). Add a sibling block after the hunger setup:

```csharp
        var hunger = GetNeed<NeedHunger>();
        if (hunger != null)
        {
            hunger.TrySubscribeToPhase();
            hunger.BindNetworkBridge();
        }

        var sleep = GetNeed<NeedSleep>();
        if (sleep != null)
        {
            sleep.TrySubscribeToPhase();
            sleep.BindNetworkBridge();
        }
```

- [ ] **Step 5: Wire `OnNetworkDespawn` (unsubscribe + unbind)**

Find `OnNetworkDespawn` (around line 158). Add a sibling block after the hunger cleanup:

```csharp
        var hunger = GetNeed<NeedHunger>();
        if (hunger != null)
        {
            hunger.UnsubscribeFromPhase();
            hunger.UnbindNetworkBridge();
        }

        var sleep = GetNeed<NeedSleep>();
        if (sleep != null)
        {
            sleep.UnsubscribeFromPhase();
            sleep.UnbindNetworkBridge();
        }
```

- [ ] **Step 6: Wire `OnDestroy` defensive cleanup**

Find `OnDestroy` (around line 188). Add the sleep cleanup mirroring hunger:

```csharp
        var hungerNeed = GetNeed<NeedHunger>();
        if (hungerNeed != null)
        {
            hungerNeed.UnsubscribeFromPhase();
            hungerNeed.UnbindNetworkBridge();
        }

        var sleepNeed = GetNeed<NeedSleep>();
        if (sleepNeed != null)
        {
            sleepNeed.UnsubscribeFromPhase();
            sleepNeed.UnbindNetworkBridge();
        }
```

- [ ] **Step 7: Verify compilation**

Wait for Unity reload. **Expected: 0 compile errors.**

- [ ] **Step 8: Editor smoke check**

Press Play in Unity. Spawn (or use the existing scene's) host player. Open the dev panel (default `\``). Inspect the player Character → CharacterNeeds. **Expected**: `NeedSleep` is in the `_allNeeds` list with `CurrentValue == 80`. Wait for one phase tick (or use `/timewarp` if available). **Expected**: NeedSleep.CurrentValue drops to 55.

- [ ] **Step 9: Commit Tasks 2 + 3 together**

```bash
git add Assets/Scripts/Character/CharacterNeeds/NeedSleep.cs Assets/Scripts/Character/CharacterNeeds/NeedSleep.cs.meta Assets/Scripts/Character/CharacterNeeds/CharacterNeeds.cs
git commit -m "feat(needs): add passive NeedSleep, server-auth NetworkVariable

Mirrors NeedHunger plumbing: NetworkVariable on CharacterNeeds with
server-write/everyone-read, ServerSetSleep + RequestAdjustSleepRpc,
SubscribeNetworkedSleepChanged bridge for HUD wiring. Decays per
TimeManager phase but skips decay while IsSleeping. No GOAP
(IsActive=false, GetGoapActions=empty) — passive only."
```

---

## Task 4: CharacterAction_Sleep (ground sleep)

**Files:**
- Create: `Assets/Scripts/Character/CharacterActions/CharacterAction_Sleep.cs`

- [ ] **Step 1: Create the file**

Create `Assets/Scripts/Character/CharacterActions/CharacterAction_Sleep.cs`:

```csharp
using MWI.Needs;
using MWI.Time;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Ground sleep — character lies down where they stand. Short repeating action
/// (5s real-time per tick). On each tick, applies a small live restoration chunk
/// to stamina + NeedSleep. Cancels when a TimeSkip starts (offline restoration
/// takes over) and on combat / damage / movement.
///
/// Save-on-wake is owned by TimeSkipController (not this action) — only legitimate
/// time-skipped sleep saves the player profile.
/// </summary>
public class CharacterAction_Sleep : CharacterAction
{
    private const float TICK_DURATION = 5f;

    public CharacterAction_Sleep(Character character) : base(character, TICK_DURATION) { }

    public override string ActionName => "Sleep";

    public override bool CanExecute()
    {
        if (character == null) return false;
        if (TimeSkipController.Instance != null && TimeSkipController.Instance.IsSkipping) return false;
        return true;
    }

    public override void OnStart()
    {
        // Server-only state mutation. Idempotent — Character.EnterSleep guards re-entry.
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            character.EnterSleep(character.transform);
        }

        // Cancel ourselves the moment a time-skip starts — offline restoration takes over.
        if (TimeSkipController.Instance != null)
        {
            TimeSkipController.Instance.OnSkipStarted += HandleSkipStarted;
        }
    }

    public override void OnApplyEffect()
    {
        // Server applies the restoration chunk. Live action ticks complement
        // (don't replace) the per-hour macro-sim restoration.
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

        ApplyRestore();
    }

    public override void OnCancel()
    {
        Unsubscribe();

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            character.ExitSleep();  // idempotent
        }
    }

    private void HandleSkipStarted(int hours)
    {
        Unsubscribe();
        // Force-cancel via the action layer so OnCancel fires once.
        character.CharacterActions?.ClearCurrentAction();
    }

    private void Unsubscribe()
    {
        if (TimeSkipController.Instance != null)
            TimeSkipController.Instance.OnSkipStarted -= HandleSkipStarted;
    }

    private void ApplyRestore()
    {
        var sleep = character.CharacterNeeds?.GetNeed<NeedSleep>();
        sleep?.IncreaseValue(NeedSleepMath.LIVE_GROUND_RESTORE_PER_TICK);

        var stamina = character.CharacterStats?.Stamina;
        if (stamina != null)
        {
            float target = stamina.CurrentValue + (NeedSleepMath.LIVE_GROUND_RESTORE_PER_TICK * 0.01f * stamina.MaxValue);
            if (target > stamina.MaxValue) target = stamina.MaxValue;
            stamina.CurrentValue = target;
        }
    }
}
```

- [ ] **Step 2: Verify compilation**

Wait for Unity reload. If `character.CharacterStats?.Stamina` does not compile (CharacterStats may use a different accessor, e.g. `GetStat<CharacterStamina>()`), open `Assets/Scripts/Character/CharacterStats/CharacterStats.cs` and inspect the public API. Replace the `ApplyRestore` body's stamina lines with the matching accessor pattern. Same for `CurrentValue`/`MaxValue` if those names differ.

If the field names don't match, the implementer should adapt — this is a documented "read first" point. **Do not invent a missing API**; use what exists.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Character/CharacterActions/CharacterAction_Sleep.cs Assets/Scripts/Character/CharacterActions/CharacterAction_Sleep.cs.meta
git commit -m "feat(actions): add CharacterAction_Sleep (ground sleep)

5s repeating action wrapping Character.EnterSleep/ExitSleep. On tick
apply a live ground-rate restore chunk to stamina + NeedSleep.
Self-cancels when TimeSkipController.OnSkipStarted fires so the
offline macro-sim restoration becomes the single restore source for
the duration of the skip."
```

---

## Task 5: CharacterAction_SleepOnFurniture (bed sleep)

**Files:**
- Create: `Assets/Scripts/Character/CharacterActions/CharacterAction_SleepOnFurniture.cs`

- [ ] **Step 1: Create the file**

Create `Assets/Scripts/Character/CharacterActions/CharacterAction_SleepOnFurniture.cs`:

```csharp
using MWI.Needs;
using MWI.Time;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Bed sleep — character occupies a slot on a <see cref="BedFurniture"/>.
/// On start, calls <c>bed.UseSlot(slotIndex, character)</c> which chains to
/// <c>Character.EnterSleep(slot.Anchor)</c>. On cancel, releases the slot
/// (which chains to <c>ExitSleep</c>).
///
/// 5s repeating action. On each tick, applies a bed-rate restore chunk to
/// stamina + NeedSleep (~2.5× ground rate). Cancels on TimeSkip start so
/// the offline macro-sim restoration takes over for the skip duration.
///
/// Save-on-wake is owned by TimeSkipController (not this action).
/// </summary>
public class CharacterAction_SleepOnFurniture : CharacterAction
{
    private const float TICK_DURATION = 5f;

    private readonly BedFurniture _bed;
    private readonly int _slotIndex;
    private bool _slotAcquired;

    public CharacterAction_SleepOnFurniture(Character character, BedFurniture bed, int slotIndex)
        : base(character, TICK_DURATION)
    {
        _bed = bed;
        _slotIndex = slotIndex;
    }

    public override string ActionName => "Sleep (bed)";

    public override bool CanExecute()
    {
        if (character == null || _bed == null) return false;
        if (_slotIndex < 0 || _slotIndex >= _bed.SlotCount) return false;
        if (TimeSkipController.Instance != null && TimeSkipController.Instance.IsSkipping) return false;

        // Slot must be free OR already held by this character (re-enqueue case).
        var slot = _bed.Slots[_slotIndex];
        if (slot.Occupant != null && slot.Occupant != character) return false;
        if (slot.ReservedBy != null && slot.ReservedBy != character) return false;
        return true;
    }

    public override void OnStart()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            // If we're already the slot's occupant (re-enqueue case during long sleep),
            // skip the UseSlot call — it would fail the "already occupied" guard.
            if (_bed.Slots[_slotIndex].Occupant != character)
            {
                _slotAcquired = _bed.UseSlot(_slotIndex, character);
                if (!_slotAcquired)
                {
                    Debug.LogWarning($"<color=orange>[CharacterAction_SleepOnFurniture]</color> {character.CharacterName} failed to acquire slot {_slotIndex} on {_bed.FurnitureName}.");
                    Finish();  // bail — coroutine will treat this as a no-op tick
                    return;
                }
            }
            else
            {
                _slotAcquired = true;  // we already had it from a prior tick
            }
        }

        if (TimeSkipController.Instance != null)
        {
            TimeSkipController.Instance.OnSkipStarted += HandleSkipStarted;
        }
    }

    public override void OnApplyEffect()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
        if (!_slotAcquired) return;

        ApplyRestore();
    }

    public override void OnCancel()
    {
        Unsubscribe();

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer && _slotAcquired)
        {
            // ReleaseSlot internally calls Character.ExitSleep (idempotent).
            _bed.ReleaseSlot(_slotIndex);
        }
    }

    private void HandleSkipStarted(int hours)
    {
        Unsubscribe();
        character.CharacterActions?.ClearCurrentAction();
    }

    private void Unsubscribe()
    {
        if (TimeSkipController.Instance != null)
            TimeSkipController.Instance.OnSkipStarted -= HandleSkipStarted;
    }

    private void ApplyRestore()
    {
        var sleep = character.CharacterNeeds?.GetNeed<NeedSleep>();
        sleep?.IncreaseValue(NeedSleepMath.LIVE_BED_RESTORE_PER_TICK);

        var stamina = character.CharacterStats?.Stamina;
        if (stamina != null)
        {
            float target = stamina.CurrentValue + (NeedSleepMath.LIVE_BED_RESTORE_PER_TICK * 0.01f * stamina.MaxValue);
            if (target > stamina.MaxValue) target = stamina.MaxValue;
            stamina.CurrentValue = target;
        }
    }
}
```

- [ ] **Step 2: Verify compilation**

Wait for Unity reload. Expect 0 errors. If stamina API differs, mirror the Task-4 fix.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Character/CharacterActions/CharacterAction_SleepOnFurniture.cs Assets/Scripts/Character/CharacterActions/CharacterAction_SleepOnFurniture.cs.meta
git commit -m "feat(actions): add CharacterAction_SleepOnFurniture (bed sleep)

Wraps BedFurniture.UseSlot/ReleaseSlot lifecycle (which chain to
EnterSleep/ExitSleep) per rule #22. 5s ticking action with bed-rate
restoration (2.5x ground). Cancels on TimeSkip start. Idempotent
re-enqueue when the same character already holds the slot."
```

---

## Task 6: CharacterActions ServerRpc for client→server bed sleep

**Files:**
- Modify: `Assets/Scripts/Character/CharacterActions/CharacterActions.cs`

- [ ] **Step 1: Add the ServerRpc**

In `CharacterActions.cs`, find the existing `Request*ServerRpc` family (around the area with `RequestCraftServerRpc`, `RequestHarvestServerRpc`, etc.). Add a new sibling RPC:

```csharp
    /// <summary>
    /// Client → Server: enqueue CharacterAction_SleepOnFurniture for the local
    /// player Character. Server resolves the bed by NetworkObjectReference,
    /// validates the slot, sets PendingSkipHours, and queues the action.
    /// The auto-trigger watcher in TimeSkipController will then fire RequestSkip
    /// once all connected players are sleeping with PendingSkipHours > 0.
    /// </summary>
    [Rpc(SendTo.Server)]
    public void RequestSleepOnFurnitureServerRpc(NetworkObjectReference bedRef, int slotIndex, int desiredHours)
    {
        if (!bedRef.TryGet(out NetworkObject bedNetObj))
        {
            // Beds DO NOT have NetworkObjects (memory rule), so we expect this RPC
            // to receive a parent-building NetworkObjectReference and resolve the
            // bed by component lookup on that GameObject hierarchy. Adapter:
            Debug.LogWarning("[CharacterActions] RequestSleepOnFurnitureServerRpc: bedRef did not resolve to a NetworkObject.");
            return;
        }

        BedFurniture bed = bedNetObj.GetComponentInChildren<BedFurniture>();
        if (bed == null)
        {
            Debug.LogWarning("[CharacterActions] RequestSleepOnFurnitureServerRpc: no BedFurniture found under the resolved NetworkObject.");
            return;
        }

        if (slotIndex < 0 || slotIndex >= bed.SlotCount)
        {
            Debug.LogWarning($"[CharacterActions] RequestSleepOnFurnitureServerRpc: slotIndex {slotIndex} out of range for {bed.FurnitureName}.");
            return;
        }

        // Set the per-skip target so the auto-trigger watcher can fire.
        if (desiredHours > 0)
        {
            _character.SetPendingSkipHours(desiredHours);
        }

        var action = new CharacterAction_SleepOnFurniture(_character, bed, slotIndex);
        if (!ExecuteAction(action))
        {
            Debug.LogWarning($"[CharacterActions] RequestSleepOnFurnitureServerRpc: ExecuteAction rejected for {_character.CharacterName} on {bed.FurnitureName}.");
        }
    }
```

> **Important:** beds do NOT have their own NetworkObject (memory rule about no nested NetworkObjects in runtime-spawned building prefabs). The RPC takes the NetworkObject of the **parent building** (or whichever ancestor is networked). The interactable-side caller (Task 7) is responsible for resolving the parent NetworkObject reference.

- [ ] **Step 2: Verify compilation**

Wait for Unity reload. Expect 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Character/CharacterActions/CharacterActions.cs
git commit -m "feat(actions): add RequestSleepOnFurnitureServerRpc

Client→Server entry point for player bed sleep. Resolves the bed
via parent NetworkObject (since beds have no NO of their own per
the no-nested-NO rule), sets PendingSkipHours, and enqueues
CharacterAction_SleepOnFurniture. Mirrors the existing Request*
ServerRpc family pattern."
```

---

## Task 7: BedFurnitureInteractable

**Files:**
- Create: `Assets/Scripts/Interactable/BedFurnitureInteractable.cs`

- [ ] **Step 1: Create the file**

Create `Assets/Scripts/Interactable/BedFurnitureInteractable.cs`:

```csharp
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Interactable attached to a <see cref="BedFurniture"/>. When the local player
/// uses the bed, opens <see cref="UI_BedSleepPrompt"/> for hour selection.
/// On confirm, routes through <see cref="CharacterActions.RequestSleepOnFurnitureServerRpc"/>
/// so the server runs <c>bed.UseSlot</c> + sets PendingSkipHours + enqueues
/// <see cref="CharacterAction_SleepOnFurniture"/>. The TimeSkipController auto-trigger
/// watcher then fires the skip.
///
/// For NPCs (server direct path), the existing <see cref="SleepBehaviour"/> still
/// drives sleep — this interactable is the player surface only.
/// </summary>
[RequireComponent(typeof(BedFurniture))]
public class BedFurnitureInteractable : FurnitureInteractable
{
    [Header("Bed UI")]
    [Tooltip("Bed sleep prompt UI singleton. If null, resolved via Object.FindFirstObjectByType at first interact.")]
    [SerializeField] private UI_BedSleepPrompt _sleepPrompt;

    [Tooltip("Override if this interactable lives nested under a non-default networked ancestor.")]
    [SerializeField] private NetworkObject _parentNetworkObject;

    private BedFurniture _bed;

    private const string PROMPT_DEFAULT = "Press E to Sleep";
    private const string PROMPT_OCCUPIED = "Bed is full";

    protected override void Awake()
    {
        base.Awake();

        _bed = GetComponent<BedFurniture>();

        if (_parentNetworkObject == null)
        {
            // Beds live nested under a CommercialBuilding / ResidentialBuilding /
            // similar — walk up to the nearest NetworkObject ancestor.
            _parentNetworkObject = GetComponentInParent<NetworkObject>();
        }
    }

    private void Update()
    {
        // Reactive prompt — only runs cheaply (string assignment guarded by equality).
        string desired = _bed != null && _bed.HasFreeSlot ? PROMPT_DEFAULT : PROMPT_OCCUPIED;
        if (interactionPrompt != desired) interactionPrompt = desired;
    }

    public override void Interact(Character interactor)
    {
        if (interactor == null || _bed == null) return;

        // Resolve a free slot up-front (needed by both player UI and direct server path).
        int slotIndex = _bed.GetSlotIndexFor(interactor);
        if (slotIndex < 0) slotIndex = _bed.FindFreeSlotIndex();
        if (slotIndex < 0)
        {
            Debug.Log($"<color=orange>[Bed]</color> No free slot on {_bed.FurnitureName} for {interactor.CharacterName}.");
            return;
        }

        // Branch: local-player → UI prompt; everyone else (NPC / direct server) → enqueue immediately.
        bool isLocalPlayer = interactor.IsOwner && interactor.IsPlayer();

        if (isLocalPlayer)
        {
            ShowPromptForLocalPlayer(interactor, slotIndex);
        }
        else
        {
            EnqueueSleepServerSide(interactor, slotIndex, desiredHours: 0);
        }
    }

    private void ShowPromptForLocalPlayer(Character localPlayer, int slotIndex)
    {
        if (_sleepPrompt == null)
        {
            _sleepPrompt = Object.FindFirstObjectByType<UI_BedSleepPrompt>(FindObjectsInactive.Include);
        }

        if (_sleepPrompt == null)
        {
            Debug.LogWarning("<color=orange>[Bed]</color> No UI_BedSleepPrompt found in scene. Skipping prompt.");
            return;
        }

        // The prompt's Confirm callback drives the actual server enqueue.
        _sleepPrompt.Show(hours =>
        {
            if (localPlayer == null || _bed == null) return;
            EnqueueSleep(localPlayer, slotIndex, hours);
        });
    }

    private void EnqueueSleep(Character character, int slotIndex, int desiredHours)
    {
        // Local-player path: route through CharacterActions ServerRpc so the server
        // is the one running UseSlot + SetPendingSkipHours + ExecuteAction.
        var nm = NetworkManager.Singleton;
        bool offline = nm == null || !nm.IsListening;
        if (offline || nm.IsServer)
        {
            EnqueueSleepServerSide(character, slotIndex, desiredHours);
            return;
        }

        if (_parentNetworkObject == null)
        {
            Debug.LogError("<color=red>[Bed]</color> Cannot route ServerRpc — no parent NetworkObject resolved.");
            return;
        }

        character.CharacterActions?.RequestSleepOnFurnitureServerRpc(
            new NetworkObjectReference(_parentNetworkObject),
            slotIndex,
            desiredHours);
    }

    private void EnqueueSleepServerSide(Character character, int slotIndex, int desiredHours)
    {
        // Already on the server (host player, NPC, or offline single-player).
        if (desiredHours > 0)
        {
            character.SetPendingSkipHours(desiredHours);
        }

        var action = new CharacterAction_SleepOnFurniture(character, _bed, slotIndex);
        if (character.CharacterActions == null || !character.CharacterActions.ExecuteAction(action))
        {
            Debug.LogWarning($"<color=orange>[Bed]</color> ExecuteAction rejected for {character.CharacterName} on {_bed.FurnitureName}.");
        }
    }

    public override List<InteractionOption> GetHoldInteractionOptions(Character interactor)
    {
        var baseOptions = base.GetHoldInteractionOptions(interactor) ?? new List<InteractionOption>();

        if (interactor == null || _bed == null) return baseOptions.Count > 0 ? baseOptions : null;

        bool hasFree = _bed.HasFreeSlot;
        bool isOccupant = _bed.GetSlotIndexFor(interactor) >= 0;

        baseOptions.Insert(0, new InteractionOption
        {
            Name = isOccupant ? "Wake up" : "Sleep",
            IsDisabled = !isOccupant && !hasFree,
            Action = () => Interact(interactor)
        });

        return baseOptions;
    }
}
```

- [ ] **Step 2: Verify compilation**

Wait for Unity reload. **Expected error**: `UI_BedSleepPrompt.Show(System.Action<int>)` does not exist — the current `Show()` takes no parameters. Task 8 fixes this. Leave the file uncompiled for now.

- [ ] **Step 3: Do not commit yet** — Task 8 finishes the dependency.

---

## Task 8: Rewire UI_BedSleepPrompt

**Files:**
- Modify: `Assets/Scripts/UI/UI_BedSleepPrompt.cs`

The current prompt directly calls `TimeSkipController.RequestSkip` on confirm — this bypasses `EnterSleep` entirely. We need to replace that with a callback so the BedFurnitureInteractable owns the routing.

- [ ] **Step 1: Replace the file contents**

Replace the entire contents of `Assets/Scripts/UI/UI_BedSleepPrompt.cs` with:

```csharp
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// In-world modal that appears when the local player uses a <see cref="BedFurniture"/>.
/// Shows a slider 1–168 with a default of 7 hours (Minecraft-style "until morning")
/// and invokes a caller-provided callback with the chosen hour count on confirm.
///
/// The actual sleep enqueue + EnterSleep + PendingSkipHours wiring is the caller's
/// responsibility (see <see cref="BedFurnitureInteractable"/>). This prompt is
/// pure UI — it does NOT call <c>TimeSkipController.RequestSkip</c> directly any
/// more (that path bypassed EnterSleep, which the auto-trigger watcher needs).
/// Hidden by default.
/// </summary>
public class UI_BedSleepPrompt : MonoBehaviour
{
    [SerializeField] private Slider _hoursSlider;
    [SerializeField] private TMP_Text _hoursLabel;
    [SerializeField] private Button _confirmButton;
    [SerializeField] private Button _cancelButton;

    [Header("Defaults")]
    [Tooltip("Default value of the hours slider when Show() is called. Minecraft default is 7h.")]
    [SerializeField] private int _defaultHours = 7;

    private Action<int> _onConfirm;

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

    /// <summary>
    /// Open the prompt. <paramref name="onConfirm"/> is invoked with the chosen
    /// hour count if the user clicks Confirm; not invoked on Cancel.
    /// </summary>
    public void Show(Action<int> onConfirm)
    {
        _onConfirm = onConfirm;
        gameObject.SetActive(true);
        if (_hoursSlider != null)
        {
            _hoursSlider.minValue = 1;
            _hoursSlider.maxValue = MWI.Time.TimeSkipController.MaxHours;
            _hoursSlider.wholeNumbers = true;
            _hoursSlider.value = Mathf.Clamp(_defaultHours, 1, MWI.Time.TimeSkipController.MaxHours);
            OnSliderChanged(_hoursSlider.value);
        }
    }

    public void Hide()
    {
        _onConfirm = null;
        gameObject.SetActive(false);
    }

    private void OnSliderChanged(float value)
    {
        if (_hoursLabel != null) _hoursLabel.text = $"Skip {(int)value} h";
    }

    private void OnConfirmClicked()
    {
        int hours = _hoursSlider != null ? (int)_hoursSlider.value : _defaultHours;
        var cb = _onConfirm;
        Hide();
        try { cb?.Invoke(hours); }
        catch (Exception e) { Debug.LogException(e); }
    }

    private void OnCancelClicked() => Hide();
}
```

- [ ] **Step 2: Verify compilation**

Wait for Unity reload. **Expected: 0 errors** (Task 7's BedFurnitureInteractable file now compiles too).

- [ ] **Step 3: Editor smoke check**

Press Play. Find a `BedFurniture` in the scene (or place one via dev mode). Add the `BedFurnitureInteractable` component to the bed GameObject. Walk player to the bed, click → prompt opens with **slider value defaulted to 7**. Move slider to 4, click Confirm. Confirm log: `Character.SetPendingSkipHours(4)` is called server-side; `bed.UseSlot` is called; `Character.EnterSleep` chains. The TimeSkipController auto-trigger fires `RequestSkip(4, force:false)`.

If the scene has no `UI_BedSleepPrompt` prefab instance, the warning log fires. Add the prefab per the manual setup checklist in `wiki/systems/world-time-skip.md`.

- [ ] **Step 4: Commit Tasks 7 + 8 together**

```bash
git add Assets/Scripts/Interactable/BedFurnitureInteractable.cs Assets/Scripts/Interactable/BedFurnitureInteractable.cs.meta Assets/Scripts/UI/UI_BedSleepPrompt.cs
git commit -m "feat(bed): add BedFurnitureInteractable + rewire prompt callback

BedFurnitureInteractable: player UI surface for the bed. Local
player path opens UI_BedSleepPrompt; on confirm, routes through
CharacterActions.RequestSleepOnFurnitureServerRpc. NPCs/server
direct path enqueue immediately.

UI_BedSleepPrompt rewired: Show takes an Action<int> callback
instead of directly calling TimeSkipController.RequestSkip. Default
slider value = 7h. The previous direct-RequestSkip path bypassed
EnterSleep, breaking the multiplayer auto-trigger gate."
```

---

## Task 9: SleepBehaviour refactor — route through actions, drop save call

**Files:**
- Modify: `Assets/Scripts/Character/AIBehaviour/SleepBehaviour.cs`

- [ ] **Step 1: Replace HandleGoingToBed to use CharacterAction_SleepOnFurniture**

In `SleepBehaviour.cs`, replace the `HandleGoingToBed` method with:

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
            // Per rule #22: enqueue the CharacterAction wrapper instead of mutating
            // bed/character state directly. The action calls bed.UseSlot internally,
            // which chains to Character.EnterSleep.
            CharacterAction action = null;
            if (_bed is BedFurniture bedFurniture)
            {
                int slotIdx = bedFurniture.GetSlotIndexFor(character);
                if (slotIdx < 0) slotIdx = bedFurniture.FindFreeSlotIndex();
                if (slotIdx >= 0)
                {
                    action = new CharacterAction_SleepOnFurniture(character, bedFurniture, slotIdx);
                }
            }
            else
            {
                // Legacy plain-Furniture fallback: direct Use as before. No CharacterAction
                // wrapper exists for non-BedFurniture beds; existing scenes that haven't
                // been migrated keep working.
                if (_bed.Use(character))
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    Debug.Log($"<color=cyan>[Sleep]</color> {character.CharacterName} legacy-occupied {_bed.FurnitureName}.");
#endif
                }
            }

            if (action != null)
            {
                if (character.CharacterActions != null && character.CharacterActions.ExecuteAction(action))
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    Debug.Log($"<color=cyan>[Sleep]</color> {character.CharacterName} enqueued sleep on {_bed.FurnitureName}.");
#endif
                }
                else
                {
                    Debug.LogWarning($"<color=orange>[Sleep]</color> {character.CharacterName} failed to enqueue CharacterAction_SleepOnFurniture on {_bed.FurnitureName}. Falling back to standing.");
                }
            }

            movement.ResetPath();
            _phase = SleepPhase.Sleeping;
        }
    }
```

- [ ] **Step 2: Update HandleFindingBed fallback to use CharacterAction_Sleep for ground sleep**

Replace `HandleFindingBed` with:

```csharp
    private void HandleFindingBed(Character character, CharacterMovement movement)
    {
        _bed = character.CharacterLocations.GetAssignedBed();

        if (_bed != null && _bed.Reserve(character))
        {
            _phase = SleepPhase.GoingToBed;
            _destinationSet = false;
        }
        else
        {
            // No bed available — sleep standing at home. Route through CharacterAction
            // so player parity holds (rule #22).
            Debug.Log($"<color=orange>[Sleep]</color> {character.CharacterName} found no available bed. Sleeping at home anyway.");
            movement.ResetPath();

            var action = new CharacterAction_Sleep(character);
            if (character.CharacterActions != null)
            {
                character.CharacterActions.ExecuteAction(action);
            }

            _phase = SleepPhase.Sleeping;
        }
    }
```

- [ ] **Step 3: Remove the SaveManager call from Exit**

Replace the entire `Exit` method with:

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
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"<color=cyan>[Sleep]</color> {character.CharacterName} woke up and left {_bed.FurnitureName}.");
#endif
        }

        character.CharacterMovement?.ResetPath();

        // No SaveManager call here. Per the new design, only the time-skip end
        // path triggers a save (avoids churn on accidental wakes). Player sleep
        // saves are owned by TimeSkipController.RunSkip's post-skip loop.
        // NPCs don't trigger save anyway (player-only gate).
    }
```

- [ ] **Step 4: Verify compilation**

Wait for Unity reload. Expect 0 errors.

- [ ] **Step 5: Editor smoke check (NPC)**

Spawn or find an NPC with a sleep schedule + an assigned bed (use the dev panel to advance time to their sleep phase if needed). **Expected**: NPC walks home, finds bed, action `Sleep (bed)` appears in their CurrentAction inspector, NPC stays in sleep pose. Wait until their sleep phase ends. **Expected**: action finishes, slot released, NPC moves on. **No `RequestSave` log** during NPC wake (NPCs don't save).

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Character/AIBehaviour/SleepBehaviour.cs
git commit -m "refactor(npc-ai): SleepBehaviour routes through CharacterAction_Sleep*

Per rule #22, sleep effect mutations now flow through CharacterAction.
HandleGoingToBed enqueues CharacterAction_SleepOnFurniture for
BedFurniture (legacy Furniture fallback unchanged).
HandleFindingBed enqueues CharacterAction_Sleep for the no-bed case.
Removed the SleepBehaviour.Exit SaveManager call — post-skip save is
now centralised in TimeSkipController.RunSkip."
```

---

## Task 10: MacroSimulator — per-hour sleep restoration step

**Files:**
- Modify: `Assets/Scripts/World/MapSystem/MacroSimulator.cs`

- [ ] **Step 1: Read the current SimulateOneHour entry to understand the structure**

Open `Assets/Scripts/World/MapSystem/MacroSimulator.cs`. Find the `SimulateOneHour` method and the `ApplyNeedsDecayHours` helper. Note the signature, where in the per-hour block decay runs, and the iteration over `HibernatedNPCData`.

- [ ] **Step 2: Add the restoration helper**

Add this method near `ApplyNeedsDecayHours`:

```csharp
    /// <summary>
    /// Per-hour sleep restoration for IsSleeping characters during a TimeSkip.
    /// Runs alongside ApplyNeedsDecayHours but in the opposite direction —
    /// while the character is in a sleep pose, NeedSleep + Stamina restore
    /// at the offline rate from <see cref="MWI.Needs.NeedSleepMath"/>.
    ///
    /// Whether the rate is bed-rate or ground-rate is determined per-character
    /// by whether their hibernated bed slot reference is set.
    /// </summary>
    private static void ApplySleepRestoreHours(HibernatedNPCData npc, int hours)
    {
        if (npc == null || hours <= 0) return;
        if (!npc.IsSleeping) return;

        // Bed-rate when the NPC has a bed reference; ground-rate otherwise.
        bool onBed = npc.SleepingOnBedFurniture;
        float perHour = onBed
            ? MWI.Needs.NeedSleepMath.OFFLINE_BED_RESTORE_PER_HOUR
            : MWI.Needs.NeedSleepMath.OFFLINE_GROUND_RESTORE_PER_HOUR;

        float total = perHour * hours;

        // NeedSleep is stored alongside other needs in HibernatedNPCData.NeedValues
        // (or equivalent). Apply the restore.
        ApplyNeedRestore(npc, "NeedSleep", total, MWI.Needs.NeedSleepMath.DEFAULT_MAX);

        // Stamina restoration. Stamina is a CharacterPrimaryStat — its hibernated value
        // lives in HibernatedNPCData.PrimaryStatValues (or equivalent).
        ApplyStatRestore(npc, "Stamina", total);
    }

    // Helper signatures — implementation MUST match the actual HibernatedNPCData fields.
    // Read HibernatedNPCData.cs first to find the actual collection names.
    private static void ApplyNeedRestore(HibernatedNPCData npc, string needTypeName, float amount, float maxValue)
    {
        // Pseudocode — adapt to the real collection on HibernatedNPCData:
        // for each entry in npc.NeedValues where entry.needType == needTypeName:
        //     entry.value = Mathf.Clamp(entry.value + amount, 0f, maxValue);
    }

    private static void ApplyStatRestore(HibernatedNPCData npc, string statName, float amount)
    {
        // Pseudocode — adapt to the real collection on HibernatedNPCData:
        // for each entry in npc.PrimaryStatValues where entry.statName == statName:
        //     entry.currentValue = Mathf.Clamp(entry.currentValue + amount, 0f, entry.maxValue);
    }
```

- [ ] **Step 3: Implementer must inspect HibernatedNPCData and finish the helpers**

Open `Assets/Scripts/World/MapSystem/MapSaveData.cs` (HibernatedNPCData lives there or in a sibling file). Find:
- The collection that stores per-need values (likely a `List<NeedSaveEntry>` or similar — look for where `ApplyNeedsDecayHours` writes).
- Whether `IsSleeping` is on `HibernatedNPCData`. **If not, add it** (`public bool IsSleeping;`) and ensure `MapController.HibernateForSkip`'s NPC-flush path captures it from `Character.IsSleeping`.
- Whether there's a per-NPC bed reference (or a flag for `SleepingOnBedFurniture`). **If not, add it** (`public bool SleepingOnBedFurniture;`) and capture it during hibernation flush by checking whether the character's current action is `CharacterAction_SleepOnFurniture`.
- The collection that stores per-stat values (look for where Stamina is read/written during hibernation).

Replace the `ApplyNeedRestore` and `ApplyStatRestore` pseudocode with the real loops.

- [ ] **Step 4: Wire ApplySleepRestoreHours into SimulateOneHour**

In `SimulateOneHour`, find where `ApplyNeedsDecayHours` is called per NPC. Add a sibling call:

```csharp
            // Per-hour decay (existing).
            ApplyNeedsDecayHours(npc, hours: 1);

            // Per-hour sleep restore for IsSleeping characters (new).
            ApplySleepRestoreHours(npc, hours: 1);
```

This is **hour-grained** — runs every hour, not gated on day-boundary.

- [ ] **Step 5: Verify compilation**

Wait for Unity reload. Expect 0 errors.

- [ ] **Step 6: Editor smoke check**

Drain a player's NeedSleep to ~10 (use the dev panel or the `/timewarp` chat command). Walk to a bed, sleep for 4 hours via the prompt. After the skip completes, **expected**: NeedSleep close to MaxValue (4h × 50 = 200, clamped to 100), stamina near max. Test ground sleep equivalent: NeedSleep up by ~80 (4h × 20).

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/World/MapSystem/MacroSimulator.cs Assets/Scripts/World/MapSystem/MapSaveData.cs
git commit -m "feat(macro-sim): per-hour sleep restoration during time skip

ApplySleepRestoreHours runs alongside ApplyNeedsDecayHours inside
SimulateOneHour. For IsSleeping characters, restores NeedSleep +
Stamina at the offline per-hour rate (50/h on a bed, 20/h on the
ground). Hour-grained (every hour, not day-boundary). Captures
IsSleeping + SleepingOnBedFurniture into HibernatedNPCData on the
hibernate-for-skip flush."
```

---

## Task 11: TimeSkipController — pre-skip fade + post-skip save fan-out

**Files:**
- Modify: `Assets/Scripts/DayNightCycle/TimeSkipController.cs`

- [ ] **Step 1: Add the serialized fade duration field**

Near the top of `TimeSkipController` (after the existing `MaxHours` constant), add:

```csharp
        [Header("Pacing")]
        [Tooltip("Real-time seconds to wait after OnSkipStarted before the per-hour loop begins. Lets UI_TimeSkipOverlay fade to black.")]
        [SerializeField] private float _preSkipFadeSeconds = 1.5f;
```

- [ ] **Step 2: Add the pre-skip fade yield in RunSkip**

In `RunSkip`, find the line `OnSkipStarted?.Invoke(hours);` and immediately after it (and after the existing pre-skip checkpoint save block, before `MapController.HibernateForSkip()`), add:

```csharp
            // Pre-skip fade window — UI_TimeSkipOverlay subscribes to OnSkipStarted
            // and fades to black during this real-time window before time math begins.
            // Use Realtime since we just set Time.timeScale = 0.
            if (_preSkipFadeSeconds > 0f)
            {
                yield return new WaitForSecondsRealtime(_preSkipFadeSeconds);
            }
```

> **Important:** the existing pre-skip checkpoint save block already waits for `SaveManager.CurrentState == Idle`. The fade goes **after** that wait so we don't start fading before the snapshot is captured.

- [ ] **Step 3: Add the post-skip save fan-out**

After the existing `try { ... ExitSleep } catch ...` block (around line 248), and after `Time.timeScale = savedTimeScale;`, but **before** `IsSkipping = false; OnSkipEnded?.Invoke();`, add:

```csharp
            // Post-skip save fan-out — single source of truth for save-on-wake.
            // Only fires when a real skip completed (not on cancel/abort would also be
            // saved, since aborts still went through hibernate/wake — see open question
            // in the spec; v1 saves on every completed RunSkip pass regardless of abort).
            try
            {
                foreach (var player in players)
                {
                    if (player == null || !player.IsPlayer()) continue;
                    if (SaveManager.Instance == null) continue;
                    SaveManager.Instance.RequestSave(player);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError("<color=red>[TimeSkip]</color> Exception during post-skip save fan-out — falling through.");
                Debug.LogException(e);
            }
```

- [ ] **Step 4: Verify compilation**

Wait for Unity reload. Expect 0 errors.

- [ ] **Step 5: Editor smoke check (host alone)**

Press Play in single-host mode. Walk to a bed, click → prompt opens with default 7. Confirm. **Expected**: `OnSkipStarted` fires, screen fades to black via UI_TimeSkipOverlay, ~1.5s real-time pause, then per-hour loop runs (clock visibly advances 7h). On wake, **exactly one** `[SaveManager] RequestSave` log entry per player character. Compare against pre-skip: there should be one save before AND one save after (two total per skip).

- [ ] **Step 6: Editor smoke check (combat-interrupted)**

Walk to bed, click → prompt opens, click Cancel. **Expected**: NO save log. Walk to bed again, confirm 7h. **As soon as fade starts**, spawn a hostile NPC adjacent to the bed (use dev panel). Wait — does the skip continue or abort? Per the existing design, only **player-death** auto-aborts a skip. So the player remains asleep through the skip and wakes at the end. Damage during the skip doesn't currently abort. (Documented as a TODO in the time-skip wiki.) For our concern: confirm save still fires once at skip end.

For the **non-skip** wake-on-attack path (player asleep but skip hasn't started yet, attacked), see Task 12.

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/DayNightCycle/TimeSkipController.cs
git commit -m "feat(time-skip): pre-skip fade window + post-skip save fan-out

Adds a configurable real-time pause (default 1.5s) right after
OnSkipStarted so UI_TimeSkipOverlay can fade to black before the
per-hour math begins. After the per-hour loop + ExitSleep cleanup,
fans out SaveManager.RequestSave for each connected player Character
as the single source of truth for save-on-wake (replacing the
removed SleepBehaviour.Exit save call)."
```

---

## Task 12: Wake-on-attack hook

**Files:**
- Modify: one of `Assets/Scripts/Character/CharacterStatusManager.cs` or `Assets/Scripts/Character/CharacterCombat.cs` (verify which owns the damage event)

- [ ] **Step 1: Locate the damage event**

Open `Assets/Scripts/Character/CharacterStatusManager.cs`. Search for `OnDamage`, `TakeDamage`, `OnHit`, or `ReceiveDamage`. If no damage event exists there, open `Assets/Scripts/Character/CharacterCombat.cs` and search the same. Identify the canonical event/method that fires when a character takes damage.

- [ ] **Step 2: Add the wake hook**

Inside the damage handler (server-side path only, so this runs once per damage event), add:

```csharp
        // Wake-on-attack: if asleep, force a wake. The current sleep CharacterAction
        // (if any) will then OnCancel via the standard cancel chain (HandleCombatStateChanged
        // also fires shortly after, which calls ClearCurrentActionLocally — but ExitSleep
        // here ensures NetworkIsSleeping flips immediately so the player isn't visibly
        // still asleep during the combat-state transition).
        if (_character != null && _character.IsSleeping)
        {
            _character.ExitSleep();
        }
```

> **Note**: `ExitSleep` is idempotent (line 1146 of `Character.cs` returns no-op if not sleeping), so the additional cancel from `HandleCombatStateChanged` doesn't double-fire ExitSleep — but `BedFurniture.ReleaseSlot` is **not** idempotent. If the action's OnCancel runs after our explicit `ExitSleep` here, it will call `ReleaseSlot` once (correct). If the player wasn't on a bed (ground sleep), only ExitSleep runs (correct).

> **Defensive**: wrap in `try/catch` per rule #31 since this runs in the damage hot path:

```csharp
        try
        {
            if (_character != null && _character.IsSleeping)
            {
                _character.ExitSleep();
            }
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
        }
```

- [ ] **Step 3: Verify compilation**

Wait for Unity reload. Expect 0 errors.

- [ ] **Step 4: Editor smoke check**

Place a player on a bed (use the dev panel or just walk + click). Confirm 7h prompt, but **before** the auto-trigger fires** (~1 second), spawn a hostile NPC adjacent and have it attack the sleeping player. **Expected**: player's `IsSleeping` flips false immediately on first damage; sleep action cancels; bed slot released; player engages combat. **No `SaveManager.RequestSave` log fires** (this was a non-skip wake).

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Character/CharacterStatusManager.cs
# (or CharacterCombat.cs depending on Step 1)
git commit -m "feat(combat): wake-on-attack — ExitSleep on damage

Damage to a sleeping character forces ExitSleep server-side. The
sleep CharacterAction's OnCancel chain handles bed-slot release.
ExitSleep is idempotent, so the cascade through
HandleCombatStateChanged → ClearCurrentActionLocally → action.OnCancel
→ BedFurniture.ReleaseSlot → ExitSleep (no-op the second time) is
safe."
```

---

## Task 13: PlayerController — Z key for ground sleep + wake-on-movement

**Files:**
- Modify: `Assets/Scripts/Character/CharacterControllers/PlayerController.cs`

- [ ] **Step 1: Read the existing Update structure**

Open `PlayerController.cs`. Find:
- The existing `Update()` method (gated by `IsOwner`).
- The existing `IsSleeping` early-out check (already shipped per the time-skip work).
- The existing input dispatch for movement (`PlayerMoveCommand` enqueueing).

Plan: add the Z-toggle **before** the `IsSleeping` early-out (Z is the wake input when asleep), and add a wake-on-movement check **before** the early-out too.

- [ ] **Step 2: Add the Z-toggle and wake-on-movement logic**

Inside `Update()`, just **before** the existing `if (_character.IsSleeping) return;` (or equivalent) early-out, insert:

```csharp
            // Sleep toggle: Z is "lay down" when awake, "wake up" when asleep.
            if (Input.GetKeyDown(KeyCode.Z))
            {
                if (_character.IsSleeping)
                {
                    _character.CharacterActions?.ClearCurrentAction();
                }
                else if (_character.CharacterActions != null
                         && _character.CharacterActions.CurrentAction == null
                         && !_character.IsIncapacitated())
                {
                    var action = new CharacterAction_Sleep(_character);
                    _character.CharacterActions.ExecuteAction(action);
                }
                return;  // consume the input; don't fall through to other handlers
            }

            // Wake-on-movement: any movement input attempt while asleep wakes the
            // character. We only check the keys here; the actual movement command
            // will route through the action layer naturally next frame.
            if (_character.IsSleeping
                && (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.A)
                    || Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.D)
                    || Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)))
            {
                _character.CharacterActions?.ClearCurrentAction();
                return;  // skip the IsSleeping early-out so the movement registers next frame
            }
```

> **Note**: per rule #33, all player keyboard input lives in `PlayerController.cs`. The Z key + movement keys here comply.
>
> If `IsIncapacitated()` is named differently on `Character` (e.g., `IsDowned` or `IsDead`), use the actual API.

- [ ] **Step 3: Verify compilation**

Wait for Unity reload. If `Character.IsIncapacitated` doesn't exist, replace with `!_character.IsAlive()` (which is referenced from `TimeSkipController.cs` line 196 so we know it exists).

- [ ] **Step 4: Editor smoke check**

Press Play. Stand somewhere not near a bed. Press `Z`. **Expected**: ground-sleep action enqueues; player snaps to sleep pose; HUD/inspector shows `CurrentAction = Sleep`. Wait through one tick (5s). NeedSleep + stamina increment by ground-rate.

Press `Z` again → player wakes. **No save log**.

Lay down again, press `W` → player wakes, walks forward. **No save log**.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Character/CharacterControllers/PlayerController.cs
git commit -m "feat(player): Z-toggle ground sleep + wake-on-movement

Z key enqueues CharacterAction_Sleep when awake, clears current
action when asleep. WASD or click-to-move while asleep also clears
the action so the character wakes and the movement command runs the
next frame. All input lives in PlayerController per rule #33."
```

---

## Task 14: End-to-end manual verification

This task has no code — it's the final correctness gate before merge.

- [ ] **Step 1: Run §9.1 single-player smoke (from spec)**

Follow each numbered step in the spec's §9.1. Pass criteria match exactly.

- [ ] **Step 2: Run §9.3 wake-on-attack**

Follow §9.3.

- [ ] **Step 3: Run §9.4 save/load round-trip**

Follow §9.4. Confirm `NeedSleep` persists across save/load.

- [ ] **Step 4: Run §9.5 edge cases**

Each bullet under §9.5. Pay special attention to the cohabiting-bed and disconnect-mid-skip cases.

- [ ] **Step 5: Run §9.6 save-trigger correctness (regression-prevention)**

This is the highest-risk regression protection. Inspect the Console log to confirm:

| Scenario | Save call count |
|---|---|
| Wake-on-attack | 0 |
| Manual `Z` cancel | 0 |
| Bed prompt cancel | 0 |
| Successful time-skip | 1 per player |

If any non-skip wake produces a save, find the leaking code path and fix.

- [ ] **Step 6: Multiplayer smoke (host + 1 client)**

Run the spec's §9.2 in two Editor instances or via an external build.

- [ ] **Step 7: Final commit (SKILL.md / wiki updates per rule #28 + #29b)**

Per CLAUDE.md rule #28 (every system must update its SKILL.md) and rule #29b (every system must update wiki/systems/):

- Update `.agent/skills/character_needs/SKILL.md` to document NeedSleep.
- Update `.agent/skills/world-system/SKILL.md` Time Skip section to note the post-skip save fan-out and pre-skip fade.
- Update `wiki/systems/character-needs.md` Change log + add NeedSleep to the systems table.
- Update `wiki/systems/world-time-skip.md` — bump `updated:`, append change log line, mark the "future BedFurnitureInteractable" open question as done.
- Create `wiki/systems/sleep-actions.md` (new system page) following `wiki/_templates/system.md`.

```bash
git add .agent/skills/character_needs/SKILL.md .agent/skills/world-system/SKILL.md wiki/systems/character-needs.md wiki/systems/world-time-skip.md wiki/systems/sleep-actions.md
git commit -m "docs(wiki): NeedSleep + sleep-actions system pages, update time-skip

Per CLAUDE.md rules #28 and #29b — every system change updates its
SKILL.md and wiki/systems/ page. Adds wiki/systems/sleep-actions.md
covering CharacterAction_Sleep / CharacterAction_SleepOnFurniture /
BedFurnitureInteractable. Bumps NeedSleep into character-needs page.
Updates world-time-skip with post-skip save fan-out + pre-skip fade
notes; closes the BedFurnitureInteractable v1 open question."
```

---

## Self-review

- **Spec coverage:**
  - §2 Constraints (Option A passive need, save-only-on-skip, default 7h, pre-skip fade, both ground/bed first-class, parity) — covered by Tasks 2 (passive), 11 (save fan-out + fade), 8 (default 7h), 4+5 (ground/bed actions), 9+13 (NPC + player parity).
  - §4 Component inventory — every row mapped to a Task.
  - §5 Data flow — covered by Tasks 4–9 + 11.
  - §6 Networking — Tasks 3 (NV), 6 (RPC), 11 (server save).
  - §7 Restoration math — Task 1 constants, Tasks 4+5 live, Task 10 offline.
  - §8 Error handling — try/catch in NeedSleep, defensive checks in actions, Task 12 wake-on-attack guard.
  - §9 Testing plan — Task 14 runs every test scenario.

- **Placeholder scan:** no "TBD"/"TODO"/"implement later" remain in the plan. Task 10 explicitly flags the "implementer must inspect HibernatedNPCData and finish the helpers" step — this is intentional context for the implementer because the field shape varies and is unsafe to fabricate; the alternative would be the implementer copy/paste the wrong type.

- **Type consistency:** `BedFurniture` slot API used consistently — `UseSlot(int, Character)`, `ReleaseSlot(int)`, `FindFreeSlotIndex()`, `GetSlotIndexFor(Character)`. `Character.EnterSleep(Transform)` / `ExitSleep()` / `SetPendingSkipHours(int)` match what's actually in `Character.cs`. `TimeSkipController.OnSkipStarted` event signature `Action<int>` matches the source. `CharacterAction.OnStart/OnApplyEffect/OnCancel` match the base class.

- **Open knowns:**
  - Stamina mutation API in Tasks 4+5 may need adaptation (clearly flagged in Step 2 of each task).
  - `HibernatedNPCData` shape in Task 10 needs implementer inspection (clearly flagged).
  - `IsIncapacitated()` vs `IsAlive()` in Task 13 (clearly flagged).
  - Wake-on-attack hook location (`CharacterStatusManager` vs `CharacterCombat`) is "verify which class owns the damage event" in Task 12 Step 1 — implementer chooses based on actual code shape.
