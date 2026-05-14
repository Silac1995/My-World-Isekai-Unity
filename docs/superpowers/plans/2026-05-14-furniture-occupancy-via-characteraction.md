# Furniture Occupancy via CharacterAction — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans for inline execution. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `Cashier.ServerTickAutoOccupy` (proximity-driven server-side auto-seat) with a single shared `CharacterAction_OccupyFurniture` continuous action driven identically by player E-press and NPC `JobVendor.Execute`. Player↔NPC controller swaps become no-ops for seating state.

**Architecture:** Continuous server-only action; client-side gates read a new `NetworkVariable<ulong>` on Character that mirrors `OccupyingFurniture`. Default `OccupiableFurniture.OnInteract` (tap-E) and `CashierInteractable.Interact` route through the action. `Bed`/`CraftingStation` are unchanged (they have their own domain actions wrapping `Use`).

**Tech Stack:** Unity 2022/Netcode for GameObjects, C# server-authoritative architecture, existing `CharacterAction_Continuous` base, existing `CashierNetSync.OccupantNetworkObjectId` channel.

**Reference spec:** `docs/superpowers/specs/2026-05-14-furniture-occupancy-via-characteraction-design.md`.

**Validation pattern (this codebase has no unit-test suite):**
- Compile check via Unity asset refresh (`mcp__ai-game-developer__assets-refresh`) or implicit through Edit-then-domain-reload.
- Manual late-joiner repro at the end (rule #19b — mandatory).

---

## File Structure

**New:**
- `Assets/Scripts/Character/CharacterActions/CharacterAction_OccupyFurniture.cs` — continuous action, server-side OnTick, OnCancel releases seat.

**Modified:**
- `Assets/Scripts/Character/Character.cs` — NetworkVariable replication of OccupyingFurniture + getter override + OnValueChanged hook.
- `Assets/Scripts/Character/CharacterActions/CharacterActions.cs` — `RequestOccupyFurnitureServerRpc` + `RequestLeaveOccupiedFurnitureServerRpc`.
- `Assets/Scripts/Character/CharacterControllers/PlayerController.cs` — early-return at top of `Move()` for `OccupyingFurniture != null`.
- `Assets/Scripts/Character/CharacterMovement/CharacterMovement.cs` — early-return at top of `SetDestination` (need to read it first to find exact signature).
- `Assets/Scripts/World/Furniture/OccupiableFurniture.cs` — `OnInteract` routes through action (NPC = direct ExecuteAction, player = ServerRpc).
- `Assets/Scripts/World/Furniture/Cashier.cs` — delete `Update`, `ServerTickAutoOccupy`, `_autoSeatTimer`, `AUTO_SEAT_TICK_INTERVAL`, `_scratchColliders`, `_autoSeatRadius` field.
- `Assets/Scripts/World/Furniture/CashierInteractable.cs` — three-branch routing.
- `Assets/Scripts/World/Jobs/ServiceJobs/JobVendor.cs` — queue occupy action when in zone; clear on `Unassign`; schedule-out-of-Work hook.

**Docs:**
- `.agent/skills/character_core/SKILL.md` (occupancy section + Character NetVar)
- `.agent/skills/shop_system/SKILL.md` (delete deferred-refactor callout, document the new flow)
- `.agent/skills/job_system/SKILL.md` (vendor flow)
- `wiki/systems/character.md` (Change log + occupancy section)
- `wiki/systems/shop-vendor.md` (Change log + occupancy section)
- `.claude/agents/character-system-specialist.md` (continuous action surface + new NetVar)

---

## Task 1: Character.OccupyingFurniture NetVar replication

**Files:**
- Modify: `Assets/Scripts/Character/Character.cs:335` (auto-property), `~1164` (`SetOccupyingFurniture`).

- [ ] **Step 1.1: Read Character.cs around the existing NetVar declarations to find the right insertion point and `using` directives.**

Look at `NetworkIsSleeping` (line ~348) — copy its declaration pattern.

- [ ] **Step 1.2: Add the NetworkVariable<ulong> declaration next to `NetworkIsSleeping`.**

```csharp
/// <summary>
/// Server-replicated NetworkObjectId of the furniture this character currently occupies
/// (0 = none). Mirrors the in-memory <see cref="OccupyingFurniture"/> reference so client
/// peers can read it for movement gates and interaction routing (rule #19b).
/// Server-write only; clients resolve the Furniture via SpawnManager in the getter.
/// </summary>
public NetworkVariable<ulong> NetworkOccupyingFurnitureNetId = new NetworkVariable<ulong>(
    0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
```

- [ ] **Step 1.3: Convert the auto-property `public Furniture OccupyingFurniture { get; private set; }` into a backing field + getter override.**

Replace the auto-property with:
```csharp
private Furniture _occupyingFurniture;
/// <summary>
/// Furniture this character is currently occupying (Bed / Chair / Cashier / …),
/// or null. Server-authoritative — set inside <see cref="OccupiableFurniture.Use"/>
/// and mirrored to clients via <see cref="NetworkOccupyingFurnitureNetId"/>. On
/// non-server peers the field would otherwise stay null because <c>Use</c> only
/// runs on the seating peer; the getter resolves the NetVar so every peer can
/// gate on this property (rule #19b client-side audit).
/// </summary>
public Furniture OccupyingFurniture
{
    get
    {
        if (IsServer) return _occupyingFurniture;
        return ResolveFurnitureByNetworkObjectId(NetworkOccupyingFurnitureNetId.Value);
    }
}

private static Furniture ResolveFurnitureByNetworkObjectId(ulong networkObjectId)
{
    if (networkObjectId == 0) return null;
    var nm = NetworkManager.Singleton;
    if (nm == null || nm.SpawnManager == null) return null;
    if (!nm.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out var obj)) return null;
    return obj != null ? obj.GetComponentInChildren<Furniture>() : null;
}
```

Note: use `GetComponentInChildren<Furniture>()` because some furnitures (e.g. Bed slots, ChairFurniture under a parent prefab) may not sit directly on the spawned NetworkObject — the NetworkObject is typically on the *parent* prefab and the Furniture script lives on a child. Cashier is an exception (NetworkObject lives on the same GameObject as the Cashier) — `GetComponentInChildren` still resolves the right component when it's on the root.

- [ ] **Step 1.4: Rewrite `SetOccupyingFurniture` to update both the backing field and the NetVar (server-side).**

Replace the existing method (`Character.cs:~1164`):
```csharp
public void SetOccupyingFurniture(Furniture furniture)
{
    var prev = OccupyingFurniture;
    if (prev == furniture) return;

    _occupyingFurniture = furniture;

    if (IsServer)
    {
        // Mirror to the replicated NetVar so client peers (including the seated
        // player's own owner client) see the change.
        ulong netId = 0;
        if (furniture != null)
        {
            var furnNetObj = furniture.GetComponentInParent<NetworkObject>();
            netId = furnNetObj != null ? furnNetObj.NetworkObjectId : 0;
        }
        NetworkOccupyingFurnitureNetId.Value = netId;
    }

    OnOccupyingFurnitureChanged?.Invoke(prev, furniture);
}
```

- [ ] **Step 1.5: Subscribe a client-side OnValueChanged handler so the event fires on remote peers too.**

Find `OnNetworkSpawn` in Character.cs (it exists — used for `NetworkIsSleeping`). Add:
```csharp
NetworkOccupyingFurnitureNetId.OnValueChanged += OnNetworkOccupyingFurnitureNetIdChanged;
```

And in `OnNetworkDespawn`:
```csharp
NetworkOccupyingFurnitureNetId.OnValueChanged -= OnNetworkOccupyingFurnitureNetIdChanged;
```

Add the handler (near `SetOccupyingFurniture`):
```csharp
private void OnNetworkOccupyingFurnitureNetIdChanged(ulong previousId, ulong currentId)
{
    if (IsServer) return; // Server already fired the event in SetOccupyingFurniture.
    var prev = ResolveFurnitureByNetworkObjectId(previousId);
    var curr = ResolveFurnitureByNetworkObjectId(currentId);
    OnOccupyingFurnitureChanged?.Invoke(prev, curr);
}
```

- [ ] **Step 1.6: Compile-check.**

Open Unity Editor (already running) — domain reload triggered by save. If errors, address before commit.

- [ ] **Step 1.7: Commit.**

```bash
git add Assets/Scripts/Character/Character.cs
git commit -m "feat(char): replicate Character.OccupyingFurniture via NetworkVariable

Adds NetworkOccupyingFurnitureNetId mirrored from SetOccupyingFurniture
server-side; getter resolves through SpawnManager on clients. Mirrors
the Cashier.Occupant override pattern (Cashier.cs:60-67) and the
Character.IsSleeping NetVar precedent.

Required for the upcoming CharacterAction_OccupyFurniture refactor —
the spec's literal 'OccupyingFurniture != null ⇒ no movement' gate
needs the property to be readable on every peer (rule #19b)."
```

---

## Task 2: CharacterAction_OccupyFurniture

**Files:**
- Create: `Assets/Scripts/Character/CharacterActions/CharacterAction_OccupyFurniture.cs`

- [ ] **Step 2.1: Create the file with the full action implementation.**

```csharp
using UnityEngine;

/// <summary>
/// Continuous action that seats a character on an <see cref="OccupiableFurniture"/> and
/// holds the seat until cancelled. Single source of truth for occupy/leave across both
/// player (E-press → ServerRpc) and NPC (server-side JobVendor.Execute) paths —
/// controller swaps become no-ops for seating state (rule #22 player↔NPC parity).
///
/// Server-only: continuous actions are rejected on clients at <c>CharacterActions.cs:73</c>
/// because <c>OnTick</c> mutates server-authoritative state (calls <c>Use</c>/<c>Leave</c>).
///
/// Lifecycle:
/// • OnStart    → Use(character). If Use returns false (lost the race), <see cref="_seatingFailed"/>
///                  is set and OnTick will end the action immediately.
/// • OnTick     → Validate (target still valid, occupant still == character). On invalidation,
///                  call Leave defensively and return true (Finish).
/// • OnCancel   → Leave(character). Idempotent — <c>OccupiableFurniture.Leave</c> returns
///                  false if not currently the occupant.
///
/// Replication: <see cref="IsReplicatedInternally"/> = false → the standard visual-proxy
/// pipeline (CharacterActions.ExecuteAction line 50, 600s sentinel duration) sets
/// _currentAction on each peer. <see cref="ShouldPlayGenericActionAnimation"/> = false
/// because the character should look like they are idling at the StandingPoint, not
/// "performing an action".
///
/// Authored 2026-05-14 — replaces <c>Cashier.ServerTickAutoOccupy</c>.
/// </summary>
public sealed class CharacterAction_OccupyFurniture : CharacterAction_Continuous
{
    private readonly OccupiableFurniture _target;
    private bool _seatingFailed;

    public override string ActionName => "OccupyFurniture";
    public override bool ShouldPlayGenericActionAnimation => false;

    public CharacterAction_OccupyFurniture(Character character, OccupiableFurniture target)
        : base(character)
    {
        _target = target;
        TickIntervalSeconds = 1f;
    }

    public override bool CanExecute()
    {
        if (character == null || _target == null) return false;
        var interactable = _target.GetComponent<InteractableObject>();
        if (interactable != null && !interactable.IsCharacterInInteractionZone(character)) return false;
        if (_target.IsOccupied && _target.Occupant != character) return false;
        return true;
    }

    public override void OnStart()
    {
        // CharacterActions only ticks continuous actions on the server (line 596 short-circuit),
        // so OnStart is also effectively server-side for this action. Defensive IsServer check.
        var co = character != null ? character.GetComponent<Unity.Netcode.NetworkObject>() : null;
        bool isServer = co != null && co.NetworkManager != null && co.NetworkManager.IsServer;
        if (!isServer) { _seatingFailed = true; return; }

        if (!_target.Use(character))
        {
            _seatingFailed = true;
            Debug.LogWarning($"<color=orange>[OccupyFurniture]</color> {character?.CharacterName} failed to seat on {_target?.FurnitureName} (lost the race).");
        }
    }

    public override bool OnTick()
    {
        if (_seatingFailed) return true; // finish immediately
        if (_target == null) return true;
        if (_target.Occupant != character)
        {
            // Lost the seat (forced eviction, despawn, …). Leave defensively (no-op if
            // already released by AutoLeaveOccupiedFurniture).
            _target.Leave(character);
            return true;
        }
        return false;
    }

    public override void OnCancel()
    {
        if (_seatingFailed) return;
        if (_target == null) return;
        // Server-only path: continuous actions only run on the server; OnCancel triggered
        // from ClearCurrentActionLocally (combat, incapacitation, voluntary leave).
        // Idempotent — Leave returns false if not the current occupant.
        _target.Leave(character);
    }
}
```

- [ ] **Step 2.2: Compile-check (Unity asset refresh).**

- [ ] **Step 2.3: Commit.**

```bash
git add Assets/Scripts/Character/CharacterActions/CharacterAction_OccupyFurniture.cs
git commit -m "feat(actions): add CharacterAction_OccupyFurniture

Continuous server-only action that seats a character on an
OccupiableFurniture and holds the seat until cancelled. OnStart calls
Use; OnTick validates; OnCancel calls Leave (idempotent). Powers the
upcoming Cashier.ServerTickAutoOccupy replacement and the generic
tap-E occupy path on OccupiableFurniture.OnInteract."
```

---

## Task 3: CharacterActions ServerRpcs

**Files:**
- Modify: `Assets/Scripts/Character/CharacterActions/CharacterActions.cs` — add two new ServerRpcs near `RequestSleepOnFurnitureServerRpc` (line ~480).

- [ ] **Step 3.1: Add `RequestOccupyFurnitureServerRpc`.**

Insert after `RequestSleepOnFurnitureServerRpc` and its helper:

```csharp
/// <summary>
/// Client → Server: enqueue <see cref="CharacterAction_OccupyFurniture"/> for the local
/// player Character on the resolved OccupiableFurniture. Validates proximity server-side
/// (anti-cheat / race) before queuing.
///
/// <paramref name="furnitureRef"/> is a <see cref="NetworkBehaviourReference"/> rather than
/// a position because OccupiableFurniture subclasses (Cashier, ChairFurniture) are
/// NetworkBehaviour components whose NetworkObject lives on the same GameObject —
/// position-resolution would have to disambiguate co-located furniture, but reference-
/// resolution is exact.
/// </summary>
[Rpc(SendTo.Server)]
public void RequestOccupyFurnitureServerRpc(NetworkBehaviourReference furnitureRef)
{
    if (!furnitureRef.TryGet(out NetworkBehaviour nb))
    {
        Debug.LogWarning("[CharacterActions] Server: RequestOccupyFurniture: furnitureRef did not resolve.");
        return;
    }

    var target = nb as OccupiableFurniture;
    if (target == null)
    {
        // Fallback for callers that pass any NetworkBehaviour on a furniture GameObject.
        target = nb.GetComponent<OccupiableFurniture>();
    }
    if (target == null)
    {
        Debug.LogWarning("[CharacterActions] Server: RequestOccupyFurniture: resolved NetworkBehaviour is not OccupiableFurniture.");
        return;
    }

    var interactable = target.GetComponent<InteractableObject>();
    if (interactable != null && !interactable.IsCharacterInInteractionZone(_character))
    {
        Debug.LogWarning($"[CharacterActions] Server: RequestOccupyFurniture: {_character.CharacterName} not in interaction zone of {target.FurnitureName}.");
        return;
    }

    if (_character.OccupyingFurniture != null && _character.OccupyingFurniture != target)
    {
        Debug.LogWarning($"[CharacterActions] Server: RequestOccupyFurniture: {_character.CharacterName} already occupying {_character.OccupyingFurniture.name}; rejected.");
        return;
    }

    var action = new CharacterAction_OccupyFurniture(_character, target);
    if (!ExecuteAction(action))
    {
        Debug.LogWarning($"[CharacterActions] Server: RequestOccupyFurniture: ExecuteAction rejected for {_character.CharacterName} on {target.FurnitureName}.");
    }
}
```

- [ ] **Step 3.2: Add `RequestLeaveOccupiedFurnitureServerRpc`.**

```csharp
/// <summary>
/// Client → Server: leave whatever furniture this character is currently occupying.
/// Server-side: if OccupyingFurniture != null, clear the current action — the
/// <see cref="CharacterAction_OccupyFurniture.OnCancel"/> handler calls Leave on the
/// target and releases the seat. Idempotent — no-op if nothing to leave.
/// </summary>
[Rpc(SendTo.Server)]
public void RequestLeaveOccupiedFurnitureServerRpc()
{
    if (_character == null) return;
    if (_character.OccupyingFurniture == null) return;
    ClearCurrentAction();
}
```

- [ ] **Step 3.3: Compile-check.**

- [ ] **Step 3.4: Commit.**

```bash
git add Assets/Scripts/Character/CharacterActions/CharacterActions.cs
git commit -m "feat(actions): add RequestOccupy/LeaveFurniture ServerRpcs

Player E-press paths route through these. Validates proximity via
InteractableObject.IsCharacterInInteractionZone (rule canonical 2D X-Z)
and rejects if already occupying another furniture. Mirrors
RequestSleepOnFurnitureServerRpc validation shape."
```

---

## Task 4: OccupiableFurniture.OnInteract → CharacterAction routing

**Files:**
- Modify: `Assets/Scripts/World/Furniture/OccupiableFurniture.cs` lines 132-141.

- [ ] **Step 4.1: Replace the body of `OnInteract`.**

Replace:
```csharp
public override bool OnInteract(Character interactor)
{
    if (interactor == null) return false;
    if (IsOccupied)
    {
        return false;
    }
    return Use(interactor);
}
```

With:
```csharp
/// <summary>
/// Universal interactable dispatch — tap-E binds the interactor as the occupant by
/// queueing <see cref="CharacterAction_OccupyFurniture"/>. NPCs run server-side and
/// queue directly; player owners send a ServerRpc through CharacterActions which
/// re-validates proximity and queues authoritatively.
///
/// Furniture with a bespoke E-press handler (e.g. <see cref="Cashier"/> via
/// <c>CashierInteractable</c>, beds via the sleep flow) override this entirely
/// and route through their own action.
/// </summary>
public override bool OnInteract(Character interactor)
{
    if (interactor == null) return false;
    if (IsOccupied && Occupant != interactor) return false;
    if (interactor.CharacterActions == null) return false;

    var netObj = GetComponent<Unity.Netcode.NetworkObject>();
    bool isServer = netObj != null && netObj.NetworkManager != null && netObj.NetworkManager.IsServer;

    if (isServer)
    {
        // NPC / host path — queue the action directly on the server-authoritative actor.
        return interactor.CharacterActions.ExecuteAction(new CharacterAction_OccupyFurniture(interactor, this));
    }

    // Client owner path — relay through the canonical ServerRpc which re-validates
    // proximity and authoritatively queues the action.
    var selfNb = this as Unity.Netcode.NetworkBehaviour;
    if (selfNb == null) return false;
    interactor.CharacterActions.RequestOccupyFurnitureServerRpc(new Unity.Netcode.NetworkBehaviourReference(selfNb));
    return true;
}
```

- [ ] **Step 4.2: Compile-check.**

- [ ] **Step 4.3: Commit.**

```bash
git add Assets/Scripts/World/Furniture/OccupiableFurniture.cs
git commit -m "refactor(furniture): route OnInteract through CharacterAction_OccupyFurniture

Default tap-E path (used by ChairFurniture and any future Occupiable
that doesn't override OnInteract) now queues the new continuous action
instead of calling Use directly. Bed (sleep flow) and CraftingStation
(craft flow) keep their domain-specific actions unchanged.

NPC/host calls ExecuteAction directly; client owner sends ServerRpc."
```

---

## Task 5: CashierInteractable three-branch routing

**Files:**
- Modify: `Assets/Scripts/World/Furniture/CashierInteractable.cs:36-57`.

- [ ] **Step 5.1: Replace `Interact` body with three-branch routing.**

```csharp
public override void Interact(Character interactor)
{
    if (interactor == null || _cashier == null) return;
    if (!IsCharacterInInteractionZone(interactor)) return;

    // Branch 1: already seated on THIS cashier → leave (vendor stepping away from counter).
    // Uses Cashier.Occupant which resolves via CashierNetSync on clients, so this branch
    // fires correctly on every peer.
    if (_cashier.Occupant == interactor)
    {
        if (interactor.CharacterActions != null)
            interactor.CharacterActions.RequestLeaveOccupiedFurnitureServerRpc();
        return;
    }

    // Branch 2: this character is the assigned vendor for this shop and is not yet
    // occupying anyone — take the cashier.
    if (interactor.CharacterJob != null
        && interactor.CharacterJob.CurrentJob is JobVendor jv
        && jv.Workplace == _cashier.LinkedBuilding
        && _cashier.RequiresVendor
        && _cashier.Occupant == null)
    {
        if (interactor.CharacterActions != null)
        {
            interactor.CharacterActions.RequestOccupyFurnitureServerRpc(
                new NetworkBehaviourReference(_cashier));
        }
        return;
    }

    // Branch 3: customer flow — existing local pre-gate + buy ServerRpc.
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

    _cashier.NetSync.RequestStartBuyServerRpc(new NetworkBehaviourReference(interactor));
}
```

- [ ] **Step 5.2: Compile-check.**

- [ ] **Step 5.3: Commit.**

```bash
git add Assets/Scripts/World/Furniture/CashierInteractable.cs
git commit -m "refactor(cashier): three-branch E-press routing (leave / occupy / buy)

CashierInteractable.Interact now distinguishes:
  1. seated occupant → leave
  2. assigned vendor not yet seated → occupy
  3. customer → existing buy flow

Reads Cashier.Occupant which is client-resolved via CashierNetSync,
so each branch fires identically on every peer."
```

---

## Task 6: Cashier.cs deletion of ServerTickAutoOccupy

**Files:**
- Modify: `Assets/Scripts/World/Furniture/Cashier.cs:88-190` (delete `Update`, `ServerTickAutoOccupy`, related fields).

- [ ] **Step 6.1: Delete the deletion-targets in Cashier.cs.**

Remove these lines (line numbers approximate — based on the read):
- Line 27-28: the `[Tooltip]` + `_autoSeatRadius` field.
- Line 89-91: `_autoSeatTimer`, `AUTO_SEAT_TICK_INTERVAL`, `_scratchColliders`.
- Lines 155-164: the `Update` method.
- Lines 166-190: the `ServerTickAutoOccupy` method.

Final state: Cashier owns Awake/OnEnable/OnDisable/Use/Release/customer-lock/till/save APIs only — no Update loop.

- [ ] **Step 6.2: Update Cashier.cs class-level summary to reflect the new contract.**

Find the existing summary at the top (`/// <summary>`) and replace the paragraph mentioning vendor seating with:

```csharp
/// Customer-facing transaction counter inside a CommercialBuilding (today
/// only ShopBuilding uses this; future BankBuilding etc. may too).
///
/// Three orthogonal state slots:
/// • Occupant (inherited from OccupiableFurniture) — vendor currently driving the cashier.
/// • CurrentCustomer — customer mid-transaction (the lock).
/// • Till — money held by this cashier (per-currency).
///
/// Vendor seating is driven by CharacterAction_OccupyFurniture — players and
/// NPCs queue the same action through their CharacterActions facade (JobVendor.Execute
/// for NPCs, CashierInteractable's E-press routing for players). The Cashier itself
/// is a pure react-to-actions component — Use/Release are the only seat mutators
/// and replicate via CashierNetSync.OccupantNetworkObjectId.
///
/// Server-authoritative — all mutations gated on IsServer and replicated via
/// the sibling CashierNetSync component.
```

- [ ] **Step 6.3: Compile-check.**

Look for any reference to the deleted fields. If a saved value still mentions `_autoSeatRadius`, leave the serialised name out of `CashierSaveData` (it never persisted anyway — verify).

- [ ] **Step 6.4: Commit.**

```bash
git add Assets/Scripts/World/Furniture/Cashier.cs
git commit -m "refactor(cashier): delete ServerTickAutoOccupy proximity auto-seat

Vendor seating now routes through CharacterAction_OccupyFurniture
(JobVendor for NPCs, CashierInteractable.Interact for players). Cashier
becomes a pure react-to-actions component — Update + auto-seat timer
+ overlap-sphere scratch buffer + _autoSeatRadius field all gone.

Class summary refreshed to reflect the new action-driven contract."
```

---

## Task 7: JobVendor wiring

**Files:**
- Modify: `Assets/Scripts/World/Jobs/ServiceJobs/JobVendor.cs`.

- [ ] **Step 7.1: Update `Execute()` to queue the occupy action on arrival.**

Replace `Execute()`:
```csharp
public override void Execute()
{
    if (_worker == null) return;

    // 1) Already manning a cashier — idle.
    if (_heldCashier != null && _heldCashier.Occupant == _worker)
    {
        _isMovingToCashier = false;
        return;
    }

    // 2) Lost the seat (race / shift change / cashier removed) — drop reservation and re-pick next tick.
    if (_heldCashier != null && _heldCashier.Occupant != null && _heldCashier.Occupant != _worker)
    {
        if (_heldCashier.ReservedBy == _worker) _heldCashier.Release();
        _heldCashier = null;
        _hasReserved = false;
        _isMovingToCashier = false;
        return;
    }

    // 3) Have reservation + reached the cashier → queue the occupy action.
    //    JobVendor runs server-side, so we call ExecuteAction directly (no ServerRpc roundtrip).
    if (_heldCashier != null && _isMovingToCashier)
    {
        var interactable = _heldCashier.GetComponent<InteractableObject>();
        bool inZone = interactable != null && interactable.IsCharacterInInteractionZone(_worker);
        if (inZone)
        {
            if (_worker.CharacterActions != null && _worker.CharacterActions.CurrentAction == null)
            {
                bool started = _worker.CharacterActions.ExecuteAction(
                    new CharacterAction_OccupyFurniture(_worker, _heldCashier));
                if (started)
                {
                    _isMovingToCashier = false;
                }
            }
        }
        return; // still arriving (or just queued the action — next tick will see Occupant == _worker)
    }

    // 4) No reservation → pick a free cashier and walk to it.
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
```

- [ ] **Step 7.2: Update `Unassign()` to clear the occupy action.**

Replace `Unassign()`:
```csharp
public override void Unassign()
{
    if (_heldCashier != null)
    {
        // If actually seated, route through the action's OnCancel path so future systems
        // listening to the cancel event (animations, audio, …) fire correctly. Otherwise
        // release the bare reservation directly.
        if (_heldCashier.Occupant == _worker)
        {
            if (_worker != null && _worker.CharacterActions != null)
                _worker.CharacterActions.ClearCurrentAction();
            // Defensive: if no current action was the occupy action, also release directly.
            if (_heldCashier.Occupant == _worker) _heldCashier.Leave(_worker);
        }
        else if (_heldCashier.ReservedBy == _worker)
        {
            _heldCashier.Release();
        }
    }
    _heldCashier = null;
    _hasReserved = false;
    _isMovingToCashier = false;
    base.Unassign();
}
```

- [ ] **Step 7.3: Update the class summary.**

Replace:
```csharp
/// Player-vendor parity (rule #22): players aren't driven by Execute —
/// they walk where they want, and Cashier.ServerTickAutoOccupy seats them
/// when they happen to stand on the InteractionPoint during their work
/// shift. Symmetrical race semantics apply.
```

With:
```csharp
/// Player-vendor parity (rule #22): players queue CharacterAction_OccupyFurniture
/// via CashierInteractable.Interact (E-press, three-branch routing). NPCs queue
/// the same action here in step 3. Switching controller between PlayerController
/// and NPCController is a no-op for seating state — the action runs on
/// CharacterActions, which lives on the Character regardless of who drives it.
```

- [ ] **Step 7.4: Compile-check.**

- [ ] **Step 7.5: Commit.**

```bash
git add Assets/Scripts/World/Jobs/ServiceJobs/JobVendor.cs
git commit -m "refactor(jobvendor): queue CharacterAction_OccupyFurniture on arrival

Execute() new step 3: reserved + in interaction zone + no current action →
ExecuteAction(CharacterAction_OccupyFurniture). Unassign() routes through
ClearCurrentAction for the seated case so the OnCancel chain fires.

Removes the implicit dependency on Cashier.ServerTickAutoOccupy that
shipped today's bug — players and NPCs are now driven by the same action."
```

---

## Task 8: Movement gates

**Files:**
- Modify: `Assets/Scripts/Character/CharacterControllers/PlayerController.cs:511` (start of `Move`).
- Modify: `Assets/Scripts/Character/CharacterMovement/CharacterMovement.cs` — `SetDestination`.

- [ ] **Step 8.1: Read CharacterMovement.cs to find SetDestination signature + the right early-return spot.**

Use Read on `Assets/Scripts/Character/CharacterMovement/CharacterMovement.cs`. Look for `public void SetDestination` or similar. Note exact signature for Step 8.3.

- [ ] **Step 8.2: Add the gate at the top of `PlayerController.Move()`.**

Inside `Move()` (currently begins at line 511), insert IMMEDIATELY after the method opening brace:
```csharp
public void Move()
{
    // Occupied-furniture lockout: while seated (cashier, chair, …) the player cannot
    // self-propel. Leave path = E-press through CashierInteractable / OccupiableFurniture.OnInteract
    // or any explicit ClearCurrentAction (combat / Z key for sleep).
    if (_character != null && _character.OccupyingFurniture != null)
    {
        // Drain NavMesh state if we transitioned into a seat with an active order pending.
        if (_wasNavMeshActiveLastFrame)
        {
            _character.ConfigureNavMesh(false);
            _characterMovement.Stop();
            _wasNavMeshActiveLastFrame = false;
        }
        return;
    }

    bool needsNavMesh = _currentOrder != null;
    // ... existing body unchanged ...
```

- [ ] **Step 8.3: Add the gate at the top of `CharacterMovement.SetDestination`.**

Match the exact signature found in Step 8.1. The gate is:
```csharp
public void SetDestination(Vector3 destination /* ...existing params... */)
{
    // Occupied-furniture lockout: a seated character (vendor at cashier, NPC asleep
    // in bed, …) cannot self-propel. Forced leaves (combat / Die / Unconscious) call
    // AutoLeaveOccupiedFurniture before they trigger downstream movement, so this gate
    // is open by the time combat AI tries to start moving.
    if (_character != null && _character.OccupyingFurniture != null) return;

    // ... existing body unchanged ...
```

If `SetDestination` returns bool, change `return;` to `return false;`.

- [ ] **Step 8.4: Compile-check.**

- [ ] **Step 8.5: Commit.**

```bash
git add Assets/Scripts/Character/CharacterControllers/PlayerController.cs Assets/Scripts/Character/CharacterMovement/CharacterMovement.cs
git commit -m "feat(movement): lock movement while OccupyingFurniture != null

PlayerController.Move and CharacterMovement.SetDestination both early-
return when the character is seated. Works on every peer thanks to the
Character.NetworkOccupyingFurnitureNetId replication.

Forced leaves (combat, unconscious, death) clear OccupyingFurniture via
AutoLeaveOccupiedFurniture before any downstream movement intent fires,
so this gate is open by the time combat AI tries to chase a target."
```

---

## Task 9: Rule #19b client-side audit (mandatory)

This task is the **gate that turns implementation into 'done'**. No commit-as-done before all six checks pass.

- [ ] **Step 9.1: Manual late-joiner repro (mandatory per rule #19b).**

1. Start the project in Unity Editor as host.
2. Spawn a shop with at least one cashier requiring a vendor.
3. Assign a vendor NPC to the shop and let them seat themselves (verify the new action queues by watching the console for the `[OccupyFurniture]` warning *absence* and the existing Cashier `Use server: …` log).
4. Launch a fresh Standalone build as a client and join the host.
5. Verify on the joining client:
   - The vendor visually stands at the cashier StandingPoint.
   - `cashier.Occupant` resolves to the vendor (read via DevMode Inspect → Cashier inspector, or via console probe).
   - Press E as the joining client on a different cashier → should buy correctly (route through Branch 3).
   - Have the host log that the vendor's `OccupyingFurniture` is non-null. On the joining client, confirm same via DevMode Inspect → Character inspector → check `Character.OccupyingFurniture` returns the expected Cashier (the new NetVar resolution path).

- [ ] **Step 9.2: Six-question audit checklist.**

Write the audit to `docs/superpowers/audits/2026-05-14-furniture-occupancy-rule-19b.md`. Each item answered with the actual code reference / log line / repro screenshot path.

1. Who writes / reads `Character.NetworkOccupyingFurnitureNetId`?
2. Replication channel for OccupyingFurniture?
3. What does the late-joiner see on connect? (Concrete repro outcome.)
4. Client pre-gate vs server authority — what does `CashierInteractable.Interact` read on the joining client and how is it kept in sync with the server?
5. `GetComponentInParent` spawn-race — any new call sites? (Answer: no; the new NetVar uses the existing NetworkBehaviour lifecycle.)
6. Proximity gate — every player path routes through `IsCharacterInInteractionZone`? (Answer: yes — Cashier.Interactable line 39, OccupiableFurniture.OnInteract not directly gated but RequestOccupyFurnitureServerRpc re-validates server-side, JobVendor checks before queuing.)

- [ ] **Step 9.3: Commit the audit.**

```bash
git add docs/superpowers/audits/2026-05-14-furniture-occupancy-rule-19b.md
git commit -m "audit(occupancy): rule #19b late-joiner repro + six-question checklist"
```

---

## Task 10: Docs (skills, wiki, agent) — rules #28, #29, #29b

- [ ] **Step 10.1: Update `.agent/skills/character_core/SKILL.md`.**

In the "Occupying-Furniture State" section (or create one if absent), document:
- `Character.OccupyingFurniture` is now server-replicated via `NetworkOccupyingFurnitureNetId`.
- `CharacterAction_OccupyFurniture` is the new occupy entry point.
- Voluntary leave = `ClearCurrentAction` (the action's OnCancel calls `Leave`).
- Forced leave = `AutoLeaveOccupiedFurniture` (already shipped; runs before the action's OnCancel via SetCombatState/Unconscious/Die).

- [ ] **Step 10.2: Update `.agent/skills/shop_system/SKILL.md`.**

- Delete the "Deferred refactor" callout (the refactor is done).
- Document `CharacterAction_OccupyFurniture` + `CashierInteractable` three-branch routing.

- [ ] **Step 10.3: Update `.agent/skills/job_system/SKILL.md`.**

- Document the `JobVendor.Execute` step-3 change.
- Reaffirm player↔NPC parity: same action for both.

- [ ] **Step 10.4: Update `wiki/systems/character.md` (frontmatter date + Change log + Occupancy section).**

Add Change log entry: `- 2026-05-14 — OccupyingFurniture replicated; CharacterAction_OccupyFurniture replaces Cashier.ServerTickAutoOccupy — claude`. Update body sections that reference seating.

- [ ] **Step 10.5: Update `wiki/systems/shop-vendor.md`.**

Same pattern: Change log entry, delete deferred-refactor callout, document new flow.

- [ ] **Step 10.6: Update `.claude/agents/character-system-specialist.md`.**

Add the new continuous action and Character NetVar to the agent's knowledge surface (per rule #29a/b).

- [ ] **Step 10.7: Commit docs.**

```bash
git add .agent/skills/character_core/SKILL.md .agent/skills/shop_system/SKILL.md .agent/skills/job_system/SKILL.md wiki/systems/character.md wiki/systems/shop-vendor.md .claude/agents/character-system-specialist.md
git commit -m "docs: CharacterAction_OccupyFurniture refactor — skills + wiki + agent

- character_core SKILL: OccupyingFurniture replication + occupy action.
- shop_system SKILL: delete deferred-refactor callout; document three-
  branch routing in CashierInteractable.
- job_system SKILL: JobVendor.Execute step-3 + player↔NPC parity.
- wiki/character + wiki/shop-vendor: Change log + occupancy sections.
- character-system-specialist agent: continuous action + Character NetVar.

Per rules #28 (SKILL.md), #29 (agents), #29b (wiki/systems)."
```

---

## Self-review checklist

Run after writing all task code, before the audit task:

1. **Spec coverage** — every section of the design spec has at least one task. Mapping:
   - §1 Character NetVar → Task 1.
   - §2 CharacterAction_OccupyFurniture → Task 2.
   - §3 ServerRpcs → Task 3.
   - §4 Interaction routing → Tasks 4 + 5.
   - §5 JobVendor → Task 7.
   - §6 Movement gates → Task 8.
   - §7 Cashier deletions → Task 6.
   - §Audit → Task 9.
   - §Docs → Task 10.

2. **Placeholder scan** — no TBD/TODO in code blocks above. Each step has the actual code or the actual command.

3. **Type consistency**:
   - Action class name: `CharacterAction_OccupyFurniture` (Tasks 2, 3, 4, 5, 7).
   - NetVar name: `NetworkOccupyingFurnitureNetId` (Task 1 declares, Task 9 audits).
   - ServerRpc names: `RequestOccupyFurnitureServerRpc` / `RequestLeaveOccupiedFurnitureServerRpc` (Tasks 3, 5; Task 4 also uses them).
   - Method names: `OnStart` / `OnTick` / `OnCancel` / `CanExecute` match `CharacterAction` / `CharacterAction_Continuous` base.

4. **Player↔NPC parity invariant** — same action used in Tasks 4 (NPC default OnInteract), 5 (player Cashier), 7 (NPC JobVendor). Confirmed.

5. **Forced-leave order preserved** — `AutoLeaveOccupiedFurniture` runs before `OnCombatStateChanged` event in `SetCombatState` (Character.cs:865-869), so by the time CharacterActions.HandleCombatStateChanged clears the action, the seat is already released. Action's OnCancel `Leave` call is idempotent.

6. **No furniture-side replication channel changes** — only Character side. Spec's "out of scope" honored.
