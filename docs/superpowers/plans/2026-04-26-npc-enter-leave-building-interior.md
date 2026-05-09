# NPC Enter / Leave Building Interior Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add two reusable `CharacterAction` primitives — `CharacterEnterBuildingAction` and `CharacterLeaveInteriorAction` — so any caller (BT, GOAP, party, quests, future order system) can order an NPC to walk to a building's door and enter, or walk to the current interior's exit door and leave. Refactor the party's hand-rolled `DoorFollowRoutine` to delegate to the new Enter action.

**Architecture:** Internal abstract `CharacterDoorTraversalAction` base class owns the shared walk-loop (resolve door → freeze controller → walk with NavMesh repath → release self → call `door.Interact(actor)`). Two public concrete subclasses override `ResolveDoor()` and `IsActionRedundant()`. The base class never reimplements transition or lock logic — it leans entirely on the existing `BuildingInteriorDoor.Interact()` → `CharacterMapTransitionAction` chain. The party keeps a small portal-only coroutine for non-building map doors.

**Tech Stack:** Unity 6 (C#), Netcode for GameObjects (NGO), existing `CharacterAction` lifecycle, NavMesh-based `CharacterMovement`, `InteractableObject.IsCharacterInInteractionZone(Character)` proximity rule, NUnit EditMode tests where the logic permits.

**Spec:** [docs/superpowers/specs/2026-04-26-npc-enter-leave-building-interior-design.md](../specs/2026-04-26-npc-enter-leave-building-interior-design.md)

---

## File map

| File | Purpose | Action |
|---|---|---|
| `Assets/Scripts/Character/CharacterActions/CharacterDoorTraversalAction.cs` | Abstract base class — shared walk-loop, freeze/unfreeze, locked-key detection, timeout, two-step retry. | **CREATE** (Task 1) |
| `Assets/Scripts/Character/CharacterActions/CharacterEnterBuildingAction.cs` | Public concrete: enter a specific `Building`. Resolves the closest `BuildingInteriorDoor` child of the target. | **CREATE** (Task 2) |
| `Assets/Scripts/Character/CharacterActions/CharacterLeaveInteriorAction.cs` | Public concrete: leave the current interior. Resolves the closest `MapTransitionDoor` child of the actor's current interior `MapController`. | **CREATE** (Task 3) |
| `Assets/Scripts/Character/CharacterParty/CharacterParty.cs` | Delete `BuildingInteriorDoor` branch of door-follow coroutine; keep small portal-only coroutine; swap building call site to action enqueue. | **MODIFY** (Task 4) |
| `Assets/Scripts/Debug/DevMode/Modules/Actions/DevActionEnterBuilding.cs` | Dev-mode action button for manual testing. Select NPC → click "Enter Building" → click a target Building. | **CREATE** (Task 5) |
| `Assets/Scripts/Debug/DevMode/Modules/Actions/DevActionLeaveInterior.cs` | Dev-mode action button. Select NPC inside an interior → click "Leave Interior". | **CREATE** (Task 5) |
| `wiki/systems/character.md` | Append "Enter / Leave Building" subsection to CharacterActions catalogue. | **EDIT** (Task 6) |
| `wiki/systems/character-party.md` | Bump `updated:`, change-log, refresh `depends_on`. | **EDIT** (Task 6) |
| `wiki/systems/building-interior.md` | Add note: NPCs can now enter/leave programmatically. Refresh `depended_on_by`. | **EDIT** (Task 6) |
| `.agent/skills/party-system/SKILL.md` | Replace door-follow coroutine procedure with action-enqueue procedure. | **EDIT** (Task 6) |
| `.agent/skills/building_system/SKILL.md` | New section: "Programmatic NPC interior entry / exit". | **EDIT** (Task 6) |
| `.claude/agents/building-furniture-specialist.md` | Mention the new actions in the building-interior section. | **EDIT** (Task 6) |
| `.claude/agents/character-system-specialist.md` | Mention the new actions in the CharacterActions catalogue. | **EDIT** (Task 6) |

---

## Task 1: Create the abstract base — `CharacterDoorTraversalAction`

**Files:**
- Create: `Assets/Scripts/Character/CharacterActions/CharacterDoorTraversalAction.cs`

This is the shared walk-loop. Both Enter and Leave inherit from it. Two abstract methods: `ResolveDoor()` returns the door we should walk to (null = no door found, fail fast), and `IsActionRedundant()` returns true when the action would be a no-op (actor already at the destination).

**Lifecycle alignment with `CharacterAction`:**
- `Duration = 0` so `OnApplyEffect` fires immediately after `OnStart` (matches the queued-action contract). We do all real work in `OnStart` by launching a coroutine on `character` (Character is a `NetworkBehaviour`).
- The coroutine drives the walk loop; on completion or failure it stops itself. Cancellation is handled by `OnCancel`, which stops the coroutine and unfreezes the controller.
- We **release ourselves** (call `character.CharacterActions.ClearCurrentAction()`) just before calling `door.Interact(actor)`. This is required because `BuildingInteriorDoor.Interact` queues a new `CharacterMapTransitionAction`, but `CharacterActions.ExecuteAction` rejects new actions while one is active. Releasing first ensures the transition action takes the slot cleanly. (`ClearCurrentAction` triggers our `OnCancel`, which unfreezes and stops movement — exactly what we want before the door takes over.)

- [ ] **Step 1: Read prerequisite files for the constructor / API surface**

Run/inspect these files to confirm signatures used below:
- `Assets/Scripts/Character/CharacterActions/CharacterAction.cs` — abstract base, expects `(Character, float duration)` and overrides for `OnStart` / `OnApplyEffect` / `OnCancel`.
- `Assets/Scripts/Character/Character.cs:217-222` — `Controller` (CharacterGameController), `CharacterMovement`, `CharacterActions`, `CharacterEquipment` accessors.
- `Assets/Scripts/Character/CharacterControllers/CharacterGameController.cs:22-33` — `Freeze()` / `Unfreeze()` virtual methods.
- `Assets/Scripts/Character/CharacterMovement/CharacterMovement.cs:288, 325, 357` — `SetDestination(Vector3)`, `Stop()`, `Resume()`.
- `Assets/Scripts/Interactable/InteractableObject.cs:43` — `IsCharacterInInteractionZone(Character)` returns `bool`.
- `Assets/Scripts/World/MapSystem/MapTransitionDoor.cs` — `Interact(Character)` is the entry point we eventually call.
- `Assets/Scripts/World/Buildings/BuildingInteriorDoor.cs:60-82` — confirms the lock/key flow (locked + key → `RequestUnlockServerRpc` then return; locked + no key → `RequestJiggleServerRpc` then return).
- `Assets/Scripts/World/MapSystem/Doors/DoorLock.cs` (search if needed) — for `IsLocked.Value` / `LockId` / `RequiredTier`.

- [ ] **Step 2: Write the file**

```csharp
using System.Collections;
using UnityEngine;

/// <summary>
/// Abstract base for actions that walk an actor to a <see cref="MapTransitionDoor"/>
/// and trigger it. Subclasses override <see cref="ResolveDoor"/> (which door to use)
/// and <see cref="IsActionRedundant"/> (whether the action would be a no-op).
///
/// The action never reimplements transition or lock logic — it just navigates to the
/// door and calls <c>door.Interact(actor)</c>, then releases its slot in the action
/// queue so the door can queue <see cref="CharacterMapTransitionAction"/> normally.
///
/// Lifecycle:
///   OnStart → resolve door, freeze controller (NPC), launch walk coroutine.
///   Walk coroutine → repath every 2 s, time out at 15 s, on arrival release self
///   and call door.Interact.
///   OnCancel → stop coroutine, unfreeze controller, stop movement.
/// </summary>
public abstract class CharacterDoorTraversalAction : CharacterAction
{
    private const float WalkTimeoutSeconds = 15f;
    private const float RepathIntervalSeconds = 2f;
    private const float PostInteractWaitSeconds = 0.3f;

    private Coroutine _walkCoroutine;

    protected CharacterDoorTraversalAction(Character actor) : base(actor, duration: 0f) { }

    /// <summary>
    /// Returns the door this action should navigate to and interact with,
    /// or null if no valid door exists (action will cancel with a warning).
    /// </summary>
    protected abstract MapTransitionDoor ResolveDoor();

    /// <summary>
    /// Returns true when the action is already accomplished (e.g. actor is already
    /// on the destination map). The action cancels cleanly with no warning.
    /// </summary>
    protected abstract bool IsActionRedundant();

    public override void OnStart()
    {
        if (character == null || !character.IsAlive())
        {
            FailAndCancel("[DoorTraversal] Actor is null or dead at start.");
            return;
        }

        if (IsActionRedundant())
        {
            // No-op: silently cancel. Caller's intent is already satisfied.
            character.CharacterActions.ClearCurrentAction();
            return;
        }

        MapTransitionDoor door = ResolveDoor();
        if (door == null)
        {
            FailAndCancel($"[DoorTraversal] {character.CharacterName}: no door resolved.");
            return;
        }

        if (!character.IsPlayer() && character.Controller != null)
        {
            character.Controller.Freeze();
        }
        character.CharacterMovement?.Resume();

        _walkCoroutine = character.StartCoroutine(WalkRoutine(door));
    }

    public override void OnApplyEffect()
    {
        // Duration is 0; this fires immediately after OnStart. All real work runs
        // inside the walk coroutine launched in OnStart, so nothing to do here.
    }

    public override void OnCancel()
    {
        if (_walkCoroutine != null && character != null)
        {
            character.StopCoroutine(_walkCoroutine);
            _walkCoroutine = null;
        }

        if (character != null)
        {
            character.CharacterMovement?.Stop();
            if (!character.IsPlayer() && character.Controller != null)
            {
                character.Controller.Unfreeze();
            }
        }
    }

    private void FailAndCancel(string warning)
    {
        Debug.LogWarning($"<color=orange>{warning}</color>");
        character?.CharacterActions?.ClearCurrentAction();
    }

    private IEnumerator WalkRoutine(MapTransitionDoor door)
    {
        // Pre-check: locked-no-key fails fast (no point walking over).
        // The door itself would also reject us, but bailing now avoids the wasted walk.
        DoorLock doorLock = door.GetComponent<DoorLock>();
        bool wasLocked = doorLock != null && doorLock.IsSpawned && doorLock.IsLocked.Value;
        if (wasLocked)
        {
            KeyInstance key = character.CharacterEquipment?.FindKeyForLock(doorLock.LockId, doorLock.RequiredTier);
            if (key == null)
            {
                FailAndCancel($"[DoorTraversal] {character.CharacterName}: door '{door.name}' is locked and no key in inventory.");
                yield break;
            }
        }

        character.CharacterMovement.SetDestination(door.transform.position);

        float elapsed = 0f;
        float timeSinceLastRepath = 0f;

        while (elapsed < WalkTimeoutSeconds)
        {
            if (character == null || !character.IsAlive() || door == null)
            {
                FailAndCancel($"[DoorTraversal] Actor or door became invalid mid-walk.");
                yield break;
            }

            if (door.IsCharacterInInteractionZone(character))
            {
                character.CharacterMovement.Stop();

                // Release our slot so door.Interact can queue CharacterMapTransitionAction.
                // ClearCurrentAction triggers our OnCancel, which unfreezes the controller —
                // exactly what we want before the door takes over.
                character.CharacterActions.ClearCurrentAction();

                door.Interact(character);

                // If the door queued a transition action, our job is done.
                yield return new WaitForSeconds(PostInteractWaitSeconds);
                if (character != null
                    && character.CharacterActions.CurrentAction is CharacterMapTransitionAction)
                {
                    yield break;
                }

                // Locked-with-key path: the door called RequestUnlockServerRpc and returned.
                // Give the unlock a moment to replicate, then re-Interact once.
                if (wasLocked && doorLock != null && !doorLock.IsLocked.Value)
                {
                    door.Interact(character);
                    yield return new WaitForSeconds(PostInteractWaitSeconds);
                    if (character != null
                        && character.CharacterActions.CurrentAction is CharacterMapTransitionAction)
                    {
                        yield break;
                    }
                }

                // Door rejected us (rattle case, or some other door gate). Already released, just log.
                Debug.LogWarning($"<color=orange>[DoorTraversal] {character?.CharacterName}: door '{door.name}' refused entry.</color>");
                yield break;
            }

            timeSinceLastRepath += Time.deltaTime;
            if (timeSinceLastRepath >= RepathIntervalSeconds)
            {
                character.CharacterMovement.SetDestination(door.transform.position);
                timeSinceLastRepath = 0f;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        FailAndCancel($"[DoorTraversal] {character?.CharacterName}: timed out walking to '{door.name}' after {WalkTimeoutSeconds}s.");
    }
}
```

- [ ] **Step 3: Verify compile in Unity**

Run: in Unity, save assets and confirm the Console shows no compile errors in `CharacterDoorTraversalAction.cs`.
Expected: clean compile. (If `KeyInstance` / `DoorLock` references fail to resolve, add the `using` for their namespaces — search for them via `grep -rn "public class KeyInstance"` and `grep -rn "public class DoorLock"`.)

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Character/CharacterActions/CharacterDoorTraversalAction.cs
git commit -m "feat(character-actions): add CharacterDoorTraversalAction abstract base"
```

---

## Task 2: Create `CharacterEnterBuildingAction`

**Files:**
- Create: `Assets/Scripts/Character/CharacterActions/CharacterEnterBuildingAction.cs`

Walks an actor to the closest `BuildingInteriorDoor` of a target `Building` and interacts. No-ops if the actor is already on that building's interior map.

- [ ] **Step 1: Confirm `Building` API**

Verify by reading: `Assets/Scripts/World/Buildings/Building.cs:90-105` — `GetInteriorMap()` returns the interior `MapController` (or null), and `GetInteriorMapId()` returns the interior map's ID (or null if no interior is registered yet).

- [ ] **Step 2: Write the file**

```csharp
using UnityEngine;

/// <summary>
/// Walks the actor to the target building's closest <see cref="BuildingInteriorDoor"/>
/// and triggers it. The door handles the lock check, key unlock, rattle, and queues
/// <see cref="CharacterMapTransitionAction"/> — this action is purely "navigate + tap".
///
/// No-ops if the actor is already on the building's interior map.
/// Cancels with a warning if the building has no <see cref="BuildingInteriorDoor"/> child.
/// </summary>
public class CharacterEnterBuildingAction : CharacterDoorTraversalAction
{
    private readonly Building _target;

    public CharacterEnterBuildingAction(Character actor, Building target) : base(actor)
    {
        _target = target;
    }

    public override string ActionName => "Enter Building";

    public override bool CanExecute()
    {
        if (_target == null)
        {
            Debug.LogWarning($"<color=orange>[EnterBuilding] {character?.CharacterName}: target building is null.</color>");
            return false;
        }
        return base.CanExecute();
    }

    protected override bool IsActionRedundant()
    {
        if (_target == null) return false;

        string interiorMapId = _target.GetInteriorMapId();
        if (string.IsNullOrEmpty(interiorMapId)) return false; // interior not spawned yet — definitely not inside

        var tracker = character.GetComponent<CharacterMapTracker>();
        if (tracker == null) return false;

        return tracker.CurrentMapID.Value.ToString() == interiorMapId;
    }

    protected override MapTransitionDoor ResolveDoor()
    {
        if (_target == null) return null;

        BuildingInteriorDoor[] doors = _target.GetComponentsInChildren<BuildingInteriorDoor>(includeInactive: false);
        if (doors == null || doors.Length == 0) return null;

        BuildingInteriorDoor best = null;
        float bestSqrDist = float.PositiveInfinity;
        Vector3 actorPos = character.transform.position;

        foreach (var door in doors)
        {
            if (door == null) continue;
            float d = (door.transform.position - actorPos).sqrMagnitude;
            if (d < bestSqrDist)
            {
                bestSqrDist = d;
                best = door;
            }
        }

        return best;
    }
}
```

- [ ] **Step 3: Compile check**

Confirm Unity console reports no errors in `CharacterEnterBuildingAction.cs`.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Character/CharacterActions/CharacterEnterBuildingAction.cs
git commit -m "feat(character-actions): add CharacterEnterBuildingAction"
```

---

## Task 3: Create `CharacterLeaveInteriorAction`

**Files:**
- Create: `Assets/Scripts/Character/CharacterActions/CharacterLeaveInteriorAction.cs`

Walks an actor to the closest exit `MapTransitionDoor` on their current interior map and interacts. No-ops if the actor is not on an interior map.

- [ ] **Step 1: Confirm `MapController` / `CharacterMapTracker` API**

Verify by reading:
- `Assets/Scripts/World/MapSystem/MapController.cs:20-29, 33, 98` — `Type` (MapType enum), `IsInteriorOffset`, `ExteriorMapId` (NetworkVariable), and the `static GetByMapId(string)` lookup.
- `Assets/Scripts/Character/CharacterMapTracker/CharacterMapTracker.cs` (search to confirm path) — `CurrentMapID` is a `NetworkVariable<FixedString...>`.

- [ ] **Step 2: Write the file**

```csharp
using UnityEngine;
using MWI.WorldSystem;

/// <summary>
/// Walks the actor to the closest exit <see cref="MapTransitionDoor"/> on their
/// current interior <see cref="MapController"/> and triggers it.
///
/// No-ops if the actor is not on an interior map (already outside).
/// Cancels with a warning if the actor's current interior has no exit door.
///
/// Exit door = any <see cref="MapTransitionDoor"/> child of the actor's current
/// MapController that is NOT a <see cref="BuildingInteriorDoor"/>. (BuildingInteriorDoors
/// are entry doors placed on the exterior building shell; the exit baked into an
/// interior prefab is a regular MapTransitionDoor.)
/// </summary>
public class CharacterLeaveInteriorAction : CharacterDoorTraversalAction
{
    public CharacterLeaveInteriorAction(Character actor) : base(actor) { }

    public override string ActionName => "Leave Interior";

    protected override bool IsActionRedundant()
    {
        var map = ResolveCurrentMap();
        return map == null || map.Type != MapType.Interior;
    }

    protected override MapTransitionDoor ResolveDoor()
    {
        var map = ResolveCurrentMap();
        if (map == null || map.Type != MapType.Interior) return null;

        MapTransitionDoor[] doors = map.GetComponentsInChildren<MapTransitionDoor>(includeInactive: false);
        if (doors == null || doors.Length == 0) return null;

        MapTransitionDoor best = null;
        float bestSqrDist = float.PositiveInfinity;
        Vector3 actorPos = character.transform.position;

        foreach (var door in doors)
        {
            if (door == null) continue;
            // Skip BuildingInteriorDoors — they're entry doors on exterior building shells,
            // not exit doors inside interiors.
            if (door is BuildingInteriorDoor) continue;

            float d = (door.transform.position - actorPos).sqrMagnitude;
            if (d < bestSqrDist)
            {
                bestSqrDist = d;
                best = door;
            }
        }

        return best;
    }

    private MapController ResolveCurrentMap()
    {
        var tracker = character.GetComponent<CharacterMapTracker>();
        if (tracker == null) return null;

        string mapId = tracker.CurrentMapID.Value.ToString();
        if (string.IsNullOrEmpty(mapId)) return null;

        return MapController.GetByMapId(mapId);
    }
}
```

- [ ] **Step 3: Compile check**

Confirm Unity console reports no errors in `CharacterLeaveInteriorAction.cs`.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Character/CharacterActions/CharacterLeaveInteriorAction.cs
git commit -m "feat(character-actions): add CharacterLeaveInteriorAction"
```

---

## Task 4: Refactor `CharacterParty` — delete building branch of door-follow, keep portal-only coroutine

**Files:**
- Modify: `Assets/Scripts/Character/CharacterParty/CharacterParty.cs:962-1133`

Today the party's `DoorFollowRoutine` handles two door types: `BuildingInteriorDoor` (now covered by the new action) and any other `MapTransitionDoor` (portal — outdoor↔outdoor / gates between zones). After the refactor:

- The building branch enqueues `CharacterEnterBuildingAction` on the follower.
- The portal branch keeps a smaller, portal-scoped coroutine (renamed `PortalFollowRoutine`) — same walk-loop pattern as the new base class but inlined here for the one remaining caller. If portal-following becomes a frequent player-issued use case later, it graduates to a third public action.

- [ ] **Step 1: Read the current call site context**

Run: `Read Assets/Scripts/Character/CharacterParty/CharacterParty.cs offset:960 limit:180` to refresh the surrounding code.

Note the cleanup site at `OnDisable()` (line 1148-ish) which currently calls `StopDoorFollow()`. After the refactor, rename to `StopPortalFollow()`.

- [ ] **Step 2: Replace the section (lines 962-1133)**

Replace the entire `// =============================================` block (`OrderFollowersThroughDoor`, `FindDoorToMap`, `StartDoorFollow`, `StopDoorFollow`, `DoorFollowRoutine`) with this:

```csharp
    // =============================================
    //  INTERIOR FOLLOW — NPCs follow leader through doors
    // =============================================

    private Coroutine _portalFollowCoroutine;

    /// <summary>
    /// Called when the party leader transitions to a different map.
    /// For each NPC follower:
    ///   - If the connecting door is a BuildingInteriorDoor → queue CharacterEnterBuildingAction.
    ///   - Otherwise (portal / gate / outdoor↔outdoor) → run a small portal-follow coroutine.
    /// </summary>
    public void OrderFollowersThroughDoor(string leaderTargetMapId)
    {
        if (!IsServer || !IsInParty) return;

        foreach (string memberId in _partyData.MemberIds)
        {
            if (memberId == _partyData.LeaderId) continue;

            Character member = Character.FindByUUID(memberId);
            if (member == null || !member.IsAlive()) continue;
            if (member.IsPlayer()) continue;
            if (member.CharacterCombat != null && member.CharacterCombat.IsInBattle) continue;

            MapTransitionDoor door = FindDoorToMap(member, leaderTargetMapId);
            if (door == null) continue;

            if (member.CharacterParty == null) continue;
            member.CharacterParty.ClearFollowState();

            if (door is BuildingInteriorDoor bd)
            {
                Building building = bd.GetComponentInParent<Building>();
                if (building != null)
                {
                    member.CharacterActions.ExecuteAction(new CharacterEnterBuildingAction(member, building));
                }
            }
            else
            {
                // Portal door (outdoor↔outdoor / gate / non-building map transition).
                member.CharacterParty.StartPortalFollow(door);
            }
        }
    }

    /// <summary>
    /// Searches for a MapTransitionDoor or BuildingInteriorDoor near the follower
    /// that leads to the target map ID.
    /// </summary>
    private MapTransitionDoor FindDoorToMap(Character follower, string targetMapId)
    {
        if (string.IsNullOrEmpty(targetMapId)) return null;

        var allDoors = UnityEngine.Object.FindObjectsByType<MapTransitionDoor>(FindObjectsSortMode.None);
        MapTransitionDoor bestDoor = null;
        float bestDist = float.MaxValue;

        foreach (var door in allDoors)
        {
            string doorTargetMapId = null;

            if (door is BuildingInteriorDoor buildingDoor)
            {
                string interiorId = buildingDoor.GetInteriorMapId();
                if (interiorId == targetMapId)
                {
                    doorTargetMapId = targetMapId;
                }
                else if (buildingDoor.ExteriorMapId == targetMapId)
                {
                    // Leader went to the exterior — but this is an entry door, not an exit. Skip.
                    continue;
                }
            }
            else
            {
                if (door.TargetMapId == targetMapId)
                {
                    doorTargetMapId = targetMapId;
                }
            }

            if (doorTargetMapId == null) continue;

            float dist = Vector3.Distance(follower.transform.position, door.transform.position);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestDoor = door;
            }
        }

        return bestDoor;
    }

    private void StartPortalFollow(MapTransitionDoor door)
    {
        StopPortalFollow();
        _portalFollowCoroutine = StartCoroutine(PortalFollowRoutine(door));
    }

    private void StopPortalFollow()
    {
        if (_portalFollowCoroutine != null)
        {
            StopCoroutine(_portalFollowCoroutine);
            _portalFollowCoroutine = null;
        }
    }

    /// <summary>
    /// Walks the follower to a non-building portal door and triggers it.
    /// Mirrors <see cref="CharacterDoorTraversalAction"/>'s walk-loop, but inlined here
    /// because Enter/Leave actions don't semantically cover outdoor↔outdoor portals.
    /// IMPORTANT: every exit path must Unfreeze the controller — same discipline as
    /// the action base class.
    /// </summary>
    private System.Collections.IEnumerator PortalFollowRoutine(MapTransitionDoor door)
    {
        if (door == null || _character == null) yield break;

        if (_character.Controller != null && !_character.IsPlayer())
            _character.Controller.Freeze();
        _character.CharacterMovement.Resume();
        _character.CharacterMovement.SetDestination(door.transform.position);

        const float Timeout = 15f;
        const float Repath = 2f;
        float elapsed = 0f;
        float sinceRepath = 0f;

        while (elapsed < Timeout)
        {
            if (_character == null || !_character.IsAlive() || door == null) break;

            if (door.IsCharacterInInteractionZone(_character))
            {
                _character.CharacterMovement.Stop();
                if (_character.Controller != null) _character.Controller.Unfreeze();
                door.Interact(_character);
                _portalFollowCoroutine = null;
                yield break;
            }

            sinceRepath += UnityEngine.Time.deltaTime;
            if (sinceRepath >= Repath)
            {
                _character.CharacterMovement.SetDestination(door.transform.position);
                sinceRepath = 0f;
            }

            elapsed += UnityEngine.Time.deltaTime;
            yield return null;
        }

        // Timeout / error path — same Unfreeze discipline.
        _character.CharacterMovement.Stop();
        if (_character.Controller != null) _character.Controller.Unfreeze();
        UpdateFollowState();
        _portalFollowCoroutine = null;
    }

    private bool IsOnSameMapAs(Character a, Character b)
    {
        if (a == null || b == null) return false;
        var trackerA = a.GetComponent<CharacterMapTracker>();
        var trackerB = b.GetComponent<CharacterMapTracker>();
        if (trackerA == null || trackerB == null) return true;
        string mapA = trackerA.CurrentMapID.Value.ToString();
        string mapB = trackerB.CurrentMapID.Value.ToString();
        if (string.IsNullOrEmpty(mapA) || string.IsNullOrEmpty(mapB)) return true;
        return mapA == mapB;
    }
```

- [ ] **Step 3: Update `OnDisable()` cleanup**

Find the existing `protected override void OnDisable()` (around line 1148). It currently calls `StopDoorFollow()`. Change that line to:

```csharp
        StopPortalFollow();
```

- [ ] **Step 4: Compile check**

Confirm Unity reports no compile errors in `CharacterParty.cs`. Note: `IsCharacterInInteractionZone` was already used by the original coroutine via a fallback distance check — switching to the canonical `door.IsCharacterInInteractionZone(_character)` is a small behaviour improvement aligned with the project rule. If the original used `bounds.extents.magnitude` math, the new check is more accurate (uses the authored zone collider directly).

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Character/CharacterParty/CharacterParty.cs
git commit -m "refactor(character-party): delegate building-door follow to CharacterEnterBuildingAction; keep portal-only coroutine"
```

---

## Task 5: Add dev-mode action buttons for manual PlayMode testing

**Files:**
- Create: `Assets/Scripts/Debug/DevMode/Modules/Actions/DevActionEnterBuilding.cs`
- Create: `Assets/Scripts/Debug/DevMode/Modules/Actions/DevActionLeaveInterior.cs`

Pattern matches the existing [DevActionAssignBuilding.cs](../../../Assets/Scripts/Debug/DevMode/Modules/Actions/DevActionAssignBuilding.cs) — `IDevAction`, `MonoBehaviour`, requires a selected character, optionally enters a click-armed state to pick a building.

`DevActionEnterBuilding`:
- Enabled when `sel.SelectedCharacter != null`.
- Click → enter armed state ("Pick a building… (ESC to cancel)") → next click on `Building` layer → queue `CharacterEnterBuildingAction(selected, building)` on the host (server-side, since dev mode is host-only).

`DevActionLeaveInterior`:
- Enabled when `sel.SelectedCharacter != null`.
- Click → immediately queue `CharacterLeaveInteriorAction(selected)` (no second-click needed).

After creating the C# files, the **user must** wire them into the Dev Mode panel UI: open the dev panel prefab, duplicate an existing action button, point its `Button.onClick` to the new component's `OnButtonClicked` callback, and bind the `_button` / `_buttonLabel` / `_selection` fields. (This wiring is a one-time manual Unity Editor step; document it in the commit message.)

- [ ] **Step 1: Write `DevActionEnterBuilding.cs`**

```csharp
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Dev-mode action: queue CharacterEnterBuildingAction on the selected character,
/// targeting a building picked by the next mouse click on the Building layer.
/// Host-only (DevMode is host-only).
/// </summary>
public class DevActionEnterBuilding : MonoBehaviour, IDevAction
{
    [Header("References")]
    [SerializeField] private DevSelectionModule _selection;
    [SerializeField] private Button _button;
    [SerializeField] private TMP_Text _buttonLabel;

    [Header("Raycast")]
    [SerializeField] private LayerMask _buildingLayerMask;
    private bool _layerMaskResolved;

    private const string DEFAULT_LABEL = "Order: Enter Building";
    private const string ARMED_LABEL = "Pick a building to enter… (ESC to cancel)";

    private bool _waitingForBuildingPick;
    private Character _pendingCharacter;

    public string Label => DEFAULT_LABEL;

    public bool IsAvailable(DevSelectionModule sel)
    {
        return sel != null && sel.SelectedCharacter != null;
    }

    public void Execute(DevSelectionModule sel)
    {
        if (!IsAvailable(sel))
        {
            Debug.LogWarning("<color=orange>[DevAction]</color> Enter Building: no character selected.");
            return;
        }

        _pendingCharacter = sel.SelectedCharacter;
        _waitingForBuildingPick = true;

        if (DevModeManager.Instance != null) DevModeManager.Instance.SetClickConsumer(this);
        SetButtonState(armed: true);

        Debug.Log($"<color=cyan>[DevAction]</color> Enter Building: pick a building for {_pendingCharacter.CharacterName} (ESC to cancel).");
    }

    private void Start()
    {
        ResolveLayerMask();
        SetButtonState(armed: false);

        if (_button != null) _button.onClick.AddListener(OnButtonClicked);
        if (_selection != null) _selection.OnSelectionChanged += RefreshAvailability;
        if (DevModeManager.Instance != null)
        {
            DevModeManager.Instance.OnClickConsumerChanged += HandleClickConsumerChanged;
            DevModeManager.Instance.OnDevModeChanged += HandleDevModeChanged;
        }

        RefreshAvailability();
    }

    private void OnDestroy()
    {
        if (_button != null) _button.onClick.RemoveListener(OnButtonClicked);
        if (_selection != null) _selection.OnSelectionChanged -= RefreshAvailability;
        if (DevModeManager.Instance != null)
        {
            DevModeManager.Instance.OnClickConsumerChanged -= HandleClickConsumerChanged;
            DevModeManager.Instance.OnDevModeChanged -= HandleDevModeChanged;
        }
    }

    private void ResolveLayerMask()
    {
        if (_buildingLayerMask.value != 0) { _layerMaskResolved = true; return; }
        int layer = LayerMask.NameToLayer("Building");
        if (layer < 0)
        {
            Debug.LogError("<color=red>[DevAction]</color> 'Building' layer is missing.");
            _layerMaskResolved = false;
            return;
        }
        _buildingLayerMask = 1 << layer;
        _layerMaskResolved = true;
    }

    private void OnButtonClicked() { if (_selection != null) Execute(_selection); }

    private void RefreshAvailability()
    {
        if (_button == null) return;
        _button.interactable = _layerMaskResolved && !_waitingForBuildingPick && IsAvailable(_selection);
    }

    private void SetButtonState(bool armed)
    {
        _waitingForBuildingPick = armed;
        if (_buttonLabel != null) _buttonLabel.text = armed ? ARMED_LABEL : DEFAULT_LABEL;
        RefreshAvailability();
    }

    private void Cancel(string reason)
    {
        if (!_waitingForBuildingPick) return;
        _waitingForBuildingPick = false;
        _pendingCharacter = null;
        SetButtonState(armed: false);
        if (DevModeManager.Instance != null) DevModeManager.Instance.ClearClickConsumer(this);
        Debug.Log($"<color=cyan>[DevAction]</color> Enter Building: {reason}.");
    }

    private void HandleClickConsumerChanged()
    {
        if (!_waitingForBuildingPick) return;
        if (DevModeManager.Instance == null) return;
        if (DevModeManager.Instance.ActiveClickConsumer == this) return;
        Cancel("superseded by another module");
    }

    private void HandleDevModeChanged(bool isEnabled)
    {
        if (!isEnabled && _waitingForBuildingPick) Cancel("dev mode disabled");
    }

    private void Update()
    {
        if (!_waitingForBuildingPick) return;
        if (DevModeManager.Instance == null || !DevModeManager.Instance.IsEnabled) return;
        if (DevModeManager.Instance.ActiveClickConsumer != this) return;
        if (!_layerMaskResolved) return;

        if (Input.GetKeyDown(KeyCode.Escape)) { Cancel("cancelled by user"); return; }
        if (!Input.GetMouseButtonDown(0)) return;

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        Camera cam = Camera.main;
        if (cam == null) { Debug.LogWarning("<color=orange>[DevAction]</color> Camera.main is null."); return; }

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, 500f, _buildingLayerMask))
        {
            Debug.LogWarning("<color=orange>[DevAction]</color> Click missed the Building layer.");
            return;
        }

        Building building = hit.collider.GetComponentInParent<Building>();
        if (building == null) { Debug.LogWarning("<color=orange>[DevAction]</color> No Building in parent."); return; }

        if (_pendingCharacter == null) { Cancel("pending character lost"); return; }

        var action = new CharacterEnterBuildingAction(_pendingCharacter, building);
        bool queued = _pendingCharacter.CharacterActions.ExecuteAction(action);
        Debug.Log($"<color=green>[DevAction]</color> Queued CharacterEnterBuildingAction on {_pendingCharacter.CharacterName} → {building.name}. (queued={queued})");

        _pendingCharacter = null;
        _waitingForBuildingPick = false;
        SetButtonState(armed: false);
        if (DevModeManager.Instance != null) DevModeManager.Instance.ClearClickConsumer(this);
    }
}
```

- [ ] **Step 2: Write `DevActionLeaveInterior.cs`**

```csharp
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Dev-mode action: queue CharacterLeaveInteriorAction on the selected character.
/// No click-armed state — runs immediately on button press.
/// Host-only (DevMode is host-only).
/// </summary>
public class DevActionLeaveInterior : MonoBehaviour, IDevAction
{
    [Header("References")]
    [SerializeField] private DevSelectionModule _selection;
    [SerializeField] private Button _button;
    [SerializeField] private TMP_Text _buttonLabel;

    private const string DEFAULT_LABEL = "Order: Leave Interior";

    public string Label => DEFAULT_LABEL;

    public bool IsAvailable(DevSelectionModule sel)
    {
        return sel != null && sel.SelectedCharacter != null;
    }

    public void Execute(DevSelectionModule sel)
    {
        if (!IsAvailable(sel))
        {
            Debug.LogWarning("<color=orange>[DevAction]</color> Leave Interior: no character selected.");
            return;
        }

        Character target = sel.SelectedCharacter;
        var action = new CharacterLeaveInteriorAction(target);
        bool queued = target.CharacterActions.ExecuteAction(action);
        Debug.Log($"<color=green>[DevAction]</color> Queued CharacterLeaveInteriorAction on {target.CharacterName}. (queued={queued})");
    }

    private void Start()
    {
        if (_buttonLabel != null) _buttonLabel.text = DEFAULT_LABEL;
        if (_button != null) _button.onClick.AddListener(OnButtonClicked);
        if (_selection != null) _selection.OnSelectionChanged += RefreshAvailability;
        RefreshAvailability();
    }

    private void OnDestroy()
    {
        if (_button != null) _button.onClick.RemoveListener(OnButtonClicked);
        if (_selection != null) _selection.OnSelectionChanged -= RefreshAvailability;
    }

    private void OnButtonClicked() { if (_selection != null) Execute(_selection); }

    private void RefreshAvailability()
    {
        if (_button == null) return;
        _button.interactable = IsAvailable(_selection);
    }
}
```

- [ ] **Step 3: Compile check**

Confirm Unity reports no compile errors. The two new files should compile cleanly given Task 2 + Task 3 are committed.

- [ ] **Step 4: Wire the buttons into the Dev Mode panel (manual Unity step)**

In the Unity Editor:
1. Open the Dev Mode panel prefab (search Project for `DevModePanel`).
2. Locate the Select tab → ActionsContainer.
3. Duplicate the existing "Assign Building as Owner" button GameObject twice.
4. On copy 1: remove `DevActionAssignBuilding`, add `DevActionEnterBuilding`. Bind `_selection` (drag the DevSelectionModule), `_button`, `_buttonLabel`.
5. On copy 2: remove `DevActionAssignBuilding`, add `DevActionLeaveInterior`. Bind the same three fields.
6. Save the prefab.

(This is a one-time editor wiring step; the user performs it locally.)

- [ ] **Step 5: Manual PlayMode verification — execute the spec's PlayMode test plan**

Spec test scenarios at [docs/superpowers/specs/2026-04-26-npc-enter-leave-building-interior-design.md:236-248](../specs/2026-04-26-npc-enter-leave-building-interior-design.md):

1. Spawn a free NPC. Open dev mode (F3). Select the NPC. Click "Order: Enter Building" → click a shop. Verify the NPC walks to the shop's door, interacts, and transitions inside (NPC disappears from exterior, appears on interior MapController). ✅
2. Same as (1) but the door has a `DoorLock` set to locked AND the NPC's CharacterEquipment contains a matching `KeyInstance`. Verify the door unlocks then the NPC re-interacts and enters. ✅
3. Same as (1) but the door is locked AND no key. Verify the NPC walks to door, attempts, sees the rattle/jiggle (RequestJiggleServerRpc fires), and the action cancels with a `[DoorTraversal] ... is locked and no key in inventory.` warning in the console. *(Pre-check fires before walking — NPC won't even start moving. This is intentional fail-fast behaviour.)* ✅
4. After (1)'s NPC is inside, select them again. Click "Order: Leave Interior". Verify NPC walks to the interior's exit door and transitions to the exterior. ✅
5. Form a party (player leader + NPC follower). Walk leader to a building's door and enter. Verify the follower automatically queues `CharacterEnterBuildingAction` (check console for the dev-action-style log) and walks in after. ✅
6. (Multiplayer) Host kicks off `CharacterEnterBuildingAction` on a server-owned NPC via dev mode. On the remote client, verify the NPC walks to the door and disappears. ✅
7. (Multiplayer) Connect a client mid-walk. Verify the client sees the NPC at its current movement destination, then sees the transition. ✅
8. (Multiplayer) Two NPCs queue `CharacterEnterBuildingAction` on the same shop concurrently. Verify both end up inside, no race / collision. ✅

Document any failures inline (don't mark step complete if a scenario fails — fix and re-run).

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Debug/DevMode/Modules/Actions/DevActionEnterBuilding.cs Assets/Scripts/Debug/DevMode/Modules/Actions/DevActionLeaveInterior.cs
# Plus any prefab .meta changes from the manual wiring step:
git add Assets/Resources/UI/DevModePanel.prefab  # adjust path to whatever the dev panel prefab is
git commit -m "feat(dev-mode): add Enter Building / Leave Interior dev actions"
```

---

## Task 6: Documentation updates (wiki + skills + agents)

**Files:**
- Edit: `wiki/systems/character.md`
- Edit: `wiki/systems/character-party.md`
- Edit: `wiki/systems/building-interior.md`
- Edit: `.agent/skills/party-system/SKILL.md`
- Edit: `.agent/skills/building_system/SKILL.md`
- Edit: `.claude/agents/building-furniture-specialist.md`
- Edit: `.claude/agents/character-system-specialist.md`

Per project rules #28, #29, #29b — every system change must be reflected in its SKILL.md (procedure), wiki page (architecture), and any owning specialist agent.

- [ ] **Step 1: Update `wiki/systems/character.md`**

Read the current file first, then locate the `## CharacterAction catalogue` section (or the equivalent section that lists action subclasses). Append:

```markdown
### Building interior traversal

- **`CharacterEnterBuildingAction(actor, Building)`** — walks the actor to the closest [[building-interior|`BuildingInteriorDoor`]] of the target building and triggers it. Delegates the actual map transition to the existing `door.Interact` → `CharacterMapTransitionAction` chain. No-ops if the actor is already inside.
- **`CharacterLeaveInteriorAction(actor)`** — walks the actor to the closest exit `MapTransitionDoor` on their current interior `MapController` and triggers it. No-ops if the actor is already on an exterior map.
- Both inherit the internal abstract `CharacterDoorTraversalAction`, which owns the shared walk-loop (freeze, repath, locked-key retry, timeout, unfreeze on cancel). The door itself owns the lock/key/rattle decisions — these actions are pure "navigate + tap".
- Used by [[character-party]]'s building-door follow path; intended consumers also include the upcoming order system, BT decisions ("go home to sleep"), and GOAP plans that need to deposit/pick up from interior storage.
```

Bump `updated:` to today's date and append a `## Change log` line:

```markdown
- 2026-04-26 — added Enter / Leave building actions (CharacterDoorTraversalAction base) — claude
```

Add `[[building-interior]]` to `related:` in frontmatter if not present.

- [ ] **Step 2: Update `wiki/systems/character-party.md`**

Bump `updated:` to today. Append change-log line:

```markdown
- 2026-04-26 — door-follow refactor: building branch now delegates to CharacterEnterBuildingAction; portal branch (outdoor↔outdoor / gates) kept as PortalFollowRoutine — claude
```

In the body, find the section describing `DoorFollowRoutine` / interior follow and replace it with a brief note that the building-door path now queues `CharacterEnterBuildingAction`, while non-building portal doors use the local `PortalFollowRoutine`. Cross-link `[[character|CharacterEnterBuildingAction]]`.

Refresh `depends_on:` to add `[[character]]` (for the action class) if not already present.

- [ ] **Step 3: Update `wiki/systems/building-interior.md`**

Bump `updated:`. Append a `## Programmatic NPC entry / exit` subsection (or extend an existing section):

```markdown
NPCs can autonomously enter and leave any building's interior via two CharacterAction primitives:

- `CharacterEnterBuildingAction(actor, Building)` — walks to and triggers the closest `BuildingInteriorDoor`.
- `CharacterLeaveInteriorAction(actor)` — walks to and triggers the closest exit `MapTransitionDoor` on the current interior MapController.

Both delegate to the existing `door.Interact` → `CharacterMapTransitionAction` chain. See [[character|CharacterAction catalogue]] for details.
```

Append change-log:

```markdown
- 2026-04-26 — documented programmatic NPC entry / exit via CharacterEnterBuildingAction & CharacterLeaveInteriorAction — claude
```

Refresh `depended_on_by:` to include `[[character]]` if not already.

- [ ] **Step 4: Update `.agent/skills/party-system/SKILL.md`**

Read the existing SKILL.md. Find any procedure describing the door-follow coroutine (likely titled "Following leader through doors" or similar). Replace its body with the new procedure:

```markdown
### Following the leader through doors (server-side, host)

When `OrderFollowersThroughDoor(leaderTargetMapId)` runs:

1. For each NPC follower:
   1. `FindDoorToMap(member, leaderTargetMapId)` returns the relevant `MapTransitionDoor` (or `BuildingInteriorDoor`) on the follower's map.
   2. If the door is a `BuildingInteriorDoor`:
      - `member.CharacterActions.ExecuteAction(new CharacterEnterBuildingAction(member, building))` — the action handles walk-to-door, interact, lock/key, transition.
   3. Otherwise (portal door — outdoor↔outdoor / gates):
      - `member.CharacterParty.StartPortalFollow(door)` — small dedicated coroutine, same walk-loop as the action base class but inlined for this single internal user.

Cleanup: `OnDisable` calls `StopPortalFollow()` to stop any active portal coroutine. The Enter action manages its own coroutine via the Character's `CharacterActions` queue.
```

- [ ] **Step 5: Update `.agent/skills/building_system/SKILL.md`**

Append a new section:

```markdown
### Programmatic NPC interior entry / exit

Two reusable `CharacterAction`s let any caller (BT, GOAP, party, quest, future order system) order an NPC to enter or leave a building interior:

```csharp
// Enter
npc.CharacterActions.ExecuteAction(new CharacterEnterBuildingAction(npc, targetBuilding));

// Leave
npc.CharacterActions.ExecuteAction(new CharacterLeaveInteriorAction(npc));
```

Both walk the NPC to the appropriate door and call `door.Interact(npc)`, which triggers the existing lock/key/rattle/transition pipeline. Failure modes (no door, locked-no-key, timeout, unreachable) cancel cleanly with a `Debug.LogWarning` so the caller can observe and react.

Authority: the actions run server-side for NPCs (matching the `CharacterAction` convention). For player actors the action runs on the owning client — currently no UI surfaces it, but it is queueable.
```

- [ ] **Step 6: Update `.claude/agents/building-furniture-specialist.md`**

Read it; locate the building-interior section. Append a brief mention:

```markdown
- **NPC interior entry / exit**: `CharacterEnterBuildingAction(actor, Building)` and `CharacterLeaveInteriorAction(actor)` (in `Assets/Scripts/Character/CharacterActions/`) wrap the existing `BuildingInteriorDoor.Interact` flow with NavMesh walking + 15 s timeout + locked-key retry. Both inherit the abstract `CharacterDoorTraversalAction` which owns the shared walk-loop. Use these instead of hand-rolling a coroutine when an NPC needs to autonomously enter/leave a building.
```

- [ ] **Step 7: Update `.claude/agents/character-system-specialist.md`**

Read it; locate the `CharacterAction` catalogue section. Add to the list:

```markdown
- `CharacterEnterBuildingAction(actor, Building)` — autonomous walk-to-door + interact for entering a specific building.
- `CharacterLeaveInteriorAction(actor)` — autonomous walk-to-door + interact for leaving the current interior.
- `CharacterDoorTraversalAction` — abstract base for both; owns the shared walk-loop, locked-key two-step retry, freeze/unfreeze, timeout. Subclasses override `ResolveDoor()` and `IsActionRedundant()`.
```

- [ ] **Step 8: Commit**

```bash
git add wiki/systems/character.md wiki/systems/character-party.md wiki/systems/building-interior.md \
        .agent/skills/party-system/SKILL.md .agent/skills/building_system/SKILL.md \
        .claude/agents/building-furniture-specialist.md .claude/agents/character-system-specialist.md
git commit -m "docs: document CharacterEnterBuildingAction / CharacterLeaveInteriorAction across wiki, skills, agents"
```

---

## Self-review (post-plan)

**1. Spec coverage check:**

| Spec requirement | Task that implements it |
|---|---|
| Player↔NPC parity (rule #22) — actions are queueable by any caller | Tasks 1, 2, 3 |
| Reuse CharacterMapTransitionAction & door.Interact (no replacement) | Task 1 (action delegates via `door.Interact(actor)`) |
| Server-authoritative for NPCs | Task 1 (coroutine launched on `character`; existing CharacterMovement is server-driven for NPCs) |
| Locked-door + key two-step flow | Task 1 (post-Interact wait + re-Interact branch) |
| Failure modes observable (Debug.LogWarning + cancel) | Task 1 (`FailAndCancel`) |
| Delete `CharacterParty.DoorFollowRoutine`, replace with action enqueue | Task 4 |
| No persistence | Tasks 1-3 (no save hooks added) |
| `CharacterEnterBuildingAction` no-ops when already inside | Task 2 (`IsActionRedundant`) |
| `CharacterLeaveInteriorAction` no-ops when already on exterior | Task 3 (`IsActionRedundant`) |
| `CharacterEnterBuildingAction` cancels when no door | Task 2 (`ResolveDoor` returns null → base FailAndCancel) |
| `CharacterLeaveInteriorAction` cancels when no exit door | Task 3 (`ResolveDoor` returns null → base FailAndCancel) |
| PlayMode test scenarios 1-8 | Task 5 step 5 (manual verification checklist) |
| wiki / SKILL / agent docs updated | Task 6 |

No spec gaps.

**2. Placeholder scan:** No "TBD", no "implement later", no "similar to Task N", no naked "add error handling" without showing how. ✅

**3. Type / signature consistency:**
- `CharacterDoorTraversalAction(Character actor)` — used identically in Tasks 2 & 3. ✅
- `ResolveDoor() : MapTransitionDoor` and `IsActionRedundant() : bool` — same signatures referenced in Tasks 2 & 3. ✅
- `CharacterEnterBuildingAction(Character actor, Building target)` — used in Task 4 (party refactor) and Task 5 (dev action). ✅
- `CharacterLeaveInteriorAction(Character actor)` — used in Task 5. ✅
- `StartPortalFollow` / `StopPortalFollow` / `PortalFollowRoutine` / `_portalFollowCoroutine` — internally consistent within Task 4. `OnDisable` updated to call `StopPortalFollow` (Task 4 step 3). ✅

**4. Known limitation flagged in plan:** The `Building` layer mask in `DevActionEnterBuilding` is identical to the one in `DevActionAssignBuilding`. If the layer name changes, both must update — but that's a project-wide concern, not a plan defect.

---

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-04-26-npc-enter-leave-building-interior.md`. Two execution options:

**1. Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration

**2. Inline Execution** — Execute tasks in this session using executing-plans, batch execution with checkpoints

**Which approach?**
