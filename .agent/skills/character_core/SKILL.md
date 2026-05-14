---
name: character-core
description: The central hub of the entity. Dictates rules on the character's availability (IsFree), life cycle (Death/Unconscious), and brain (Player/NPC Switch).
---

# Character Core

## 0. Character Prefab Structure
The root (most parent) GameObject of a Character prefab contains the essential components that form the entity's foundation:
- `Character.cs` (`Assets/Scripts/Character/Character.cs`)
- `CharacterActions.cs` (`Assets/Scripts/Character/CharacterActions/CharacterActions.cs`)
- `NPCController.cs` and `PlayerController.cs` (`Assets/Scripts/Character/CharacterControllers/NPCController.cs` and `Assets/Scripts/Character/CharacterControllers/PlayerController.cs`)
- `Rigidbody` — exposed as `character.Rigidbody`. **This is the physical body used for all proximity/interaction checks.**
- `CapsuleCollider`

The `Character.cs` script is the most important class of the entity. It is the Central Architecture (Facade Pattern) through which **everything** passes.

> [!IMPORTANT]
> **Interaction Proximity Rule — mandatory.** For a `Character` to interact with any `InteractableObject` (items, NPCs, harvestables, doors, furniture, crafting stations…), the Character's **`Rigidbody` position** **MUST** be inside the target's `InteractionZone`. The **only sanctioned check** is:
>
> ```csharp
> interactable.IsCharacterInInteractionZone(character);
> ```
>
> This method lives on the `InteractableObject` base class and is the single source of truth for the rule. Call it at the top of every `Interact()` override, `CharacterAction.CanExecute()`, GOAP precondition, BT action, and server RPC handler that resolves an interaction. If it returns `false`, abort — no distance fallback, no zone-overlap shortcut, no `transform.position` substitute.
>
> **Do not confuse the two kinds of `InteractionZone`:**
> - `InteractableObject.InteractionZone` — authored on every interactable prefab; the single source of truth for "am I close enough to interact with this object?".
> - `CharacterInteraction.InteractionZone` — lives on the social subsystem (`CharacterInteraction` = character-to-character dialogue/invitations) and detects **other characters** for social exchanges only. It is **not** a general-purpose proximity collider and must never be the gate for item pickup, furniture use, harvesting, or any other non-social interaction.
>
> See `.agent/skills/interactable-system/SKILL.md` §Core Rule #1 and §Proximity-Check API for the authoritative definition and forbidden anti-patterns.

## 1. Facade Pattern (Obligations)

> [!IMPORTANT]
> **Capability Registry is the primary lookup mechanism.** With the Character Archetype System (`character-archetype` skill), `Character.cs` now hosts a Capability Registry. Use `character.Get<T>()`, `character.TryGet<T>()`, `character.Has<T>()`, and `character.GetAll<T>()` to discover capabilities at runtime. The legacy facade properties (e.g., `character.CharacterCombat`) delegate to the registry for backward compatibility but are gradually being deprecated. For full details, see `.agent/skills/character-archetype/SKILL.md`.

The Agent **must NEVER search for components linked to a character via isolated `GetComponent` calls**.
If it has a reference to `Character`, then it already has safe and performant access to the majority of the system.
Examples:
- `character.CharacterJob` -> Manages work.
- `character.CharacterCombat` -> Manages fighting.
- `character.CharacterInteraction` -> Manages dialogues.
- `character.CharacterMovement` -> Manages navigation.
- `character.CharacterEquipment` -> Manages equippable inventory.
- `character.Stats` -> Provides vital statistics.
- `character.PathingMemory` -> Specialized memory container that tracks unreachable targets to prevent infinite evaluation/movement loops (self-cleaning on TimeManager resets via `OnDestroy()`).

### CharacterSystem Pipeline (Decoupled Modules)
All core character systems (`CharacterMovement`, `CharacterVisual`, `CharacterInteraction`, `CharacterActions`, `CharacterGameController`, `CharacterGoapController`, `NPCBehaviourTree`, `CharacterCombat`) now inherit from the abstract class **`CharacterSystem`**.
This abstract base automatically caches `_character` during `Awake` and subscribes to essential lifecycle events (`OnIncapacitated`, `OnDeath`, `OnWakeUp`, `OnCombatStateChanged`, `OnOccupyingFurnitureChanged`). `Character.cs` no longer explicitly micro-manages the shutdown of its modules; each subsystem gracefully handles its own cleanup by overriding `HandleIncapacitated(Character)`, `HandleCombatStateChanged(bool)`, or `HandleOccupyingFurnitureChanged(Furniture previous, Furniture current)`.

### System-to-System Communication (Inspector Linking)
> [!IMPORTANT]
> **New Architectural Rule**: Every time a `CharacterSystem` needs to call another `CharacterSystem` on the same entity, you **must use a [SerializeField]** to link them directly in the Unity Inspector instead of dynamically querying the Facade at runtime. This prevents missing component bugs and reduces rigid caching dependency. If you add a reference this way, always remind the user to link it in the prefab inspector!

## 2. Justice of the Peace and Availability (`IsFree()`)
This is the ultimate safety method. `Character` scrutinizes all of its child components to tell the global system (GOAP, Player commands, Interactions) whether the character is allowed to be interrupted or is already busy.

`IsFree(out CharacterBusyReason reason)` will return False and explain why if the character is:
- Dead (`Dead`)
- KO (`Unconscious`)
- Currently fighting (`InCombat`)
- In dialogue (`Interacting`)
- Forging or building a complex object (`Crafting`)
- Teaching a class (`Teaching`)

## 3. Life Cycle and Statuses 
`Character` is responsible for major state changes. You must never manually tinker with HP or the collider to "kill" someone.

- **SetUnconscious(true)**:
  - Calls `AutoLeaveOccupiedFurniture("became unconscious")` first so the seat is released before any subsystem reacts.
  - Triggers the `OnIncapacitated` event.
  - Subsystems inheriting from `CharacterSystem` independently react to power down (e.g., `CharacterMovement.Stop()`, `NPCBehaviourTree.CancelOrder()`, `CharacterVisual.ClearLookTarget()`).
  - The entity becomes physically inert (Rigidbody switches to Kinematic so falls are managed).
- **Die()**:
  - Calls `AutoLeaveOccupiedFurniture("died")` first.
  - Performs the same routine (fires `OnDeath` and `OnIncapacitated`).
  - But death (`_isDead = true`) permanently overrides the rest.
- **SetCombatState(true)**:
  - Calls `AutoLeaveOccupiedFurniture("entered combat")` so seated vendors / chair-sitters stand up before combat reactions fire.
  - Triggers `OnCombatStateChanged(true)`.

### 3.b Occupying-Furniture State

`Character.OccupyingFurniture` (read-only property) is the single source of truth for "is this character currently sitting/manning/sleeping in a piece of furniture". Set server-side via `SetOccupyingFurniture(Furniture)` — only `OccupiableFurniture.Use` / `Leave` / `Release` call it.

- **Replication (2026-05-14):** `Character.NetworkOccupyingFurnitureNetId` is a `NetworkVariable<ulong>` (EveryoneRead / ServerWrite) carrying the furniture's parent NetworkObjectId. The property getter resolves it via `NetworkManager.Singleton.SpawnManager.SpawnedObjects` on clients; `GetComponent<Furniture>()` fast-path for furniture that owns its NO (Cashier), then `GetComponentInChildren<Furniture>()` fallback for furniture under a building NO (Bed/Chair). `OnValueChanged` fires `OnOccupyingFurnitureChanged` on remote peers so any `CharacterSystem.HandleOccupyingFurnitureChanged` listener stays accurate cross-peer. Required so the literal "OccupyingFurniture != null ⇒ no movement" gate fires correctly on every peer (rule #19b).
- `OnOccupyingFurnitureChanged(prev, next)` fires on every transition (sit-down, stand-up, swap).
- `AutoLeaveOccupiedFurniture(string reason)` is the central helper that calls `OccupiableFurniture.Leave(this)` on the current furniture, log-traced. Used internally by combat/incap/death; any future trigger (job change, schedule flip, knockback out of range) should call this method rather than duplicating the cast + null-check.
- `OccupiableFurniture.Leave(Character c)` is the **per-character** inverse of `Use(Character)`. Single-slot furniture (Cashier, Chair) default to delegating to `Release()`. `BedFurniture` overrides to release **only** the caller's slot — never delegate auto-leave to the parameterless `Release()`, which evicts every slot in a shared bed.

### 3.c Furniture Occupancy via CharacterAction (2026-05-14)

The occupy/leave lifecycle is **action-driven** — same `CharacterAction_OccupyFurniture` queued from both player and NPC paths. Controller swaps (PlayerController ↔ NPCController) are no-ops for seating state because the action runs on `CharacterActions`, which lives on the Character regardless of who drives it.

**Action contract:**
- `CharacterAction_OccupyFurniture : CharacterAction_Continuous`. Server-only execution (continuous actions are rejected on clients at `CharacterActions.cs:73`).
- `OnStart` → `_target.Use(character)`; `OnTick` → validate, return true on invalidation; `OnCancel` → `_target.Leave(character)` (idempotent).
- `IsReplicatedInternally = false` → standard 600s-sentinel visual proxy fires on every peer so movement gates can rely on `_currentAction != null` as a fallback signal.
- `ShouldPlayGenericActionAnimation = false` — the character idles at the StandingPoint, no `isDoingAction` animator trigger.

**Entry points (uniform across player + NPC):**
- `OccupiableFurniture.OnInteract` (default tap-E): server path queues directly; client-owner relays via `CharacterActions.RequestOccupyFurnitureServerRpc(NetworkBehaviourReference, Vector3)`. The position carry-along disambiguates multi-furniture-per-building cases (Bed/Chair) the same way `RequestSleepOnFurnitureServerRpc` does with `FindClosestBedUnder`.
- `CashierInteractable.Interact`: bespoke E-press handler. Branch 1 (seated occupant → leave). Branches 2+3 collapse into `CashierNetSync.RequestUseCashierServerRpc` — server-side role routing decides vendor (occupy) vs customer (buy).
- `JobVendor.Execute` step 3: NPC arrives at the InteractionPoint → server-side `ExecuteAction(new CharacterAction_OccupyFurniture(...))`.

**Authorization gate:**
- `OccupiableFurniture.IsCharacterAllowedToOccupy(Character)` — virtual, default true. `Cashier` overrides to require the assigned `JobVendor` for the linked shop when `RequiresVendor`. Server-side authoritative; `CharacterJob._activeJobs` is not NetVar-replicated, so the gate intentionally lives on the server side (called from `CanExecute` + the ServerRpc handlers).

**Voluntary leave paths:**
- Player E-press on the seated cashier → Branch 1 of `CashierInteractable.Interact` → `RequestLeaveOccupiedFurnitureServerRpc` → `ClearCurrentAction` → `OnCancel` → `Leave`.
- `JobVendor.Unassign`: routes through `ClearCurrentAction` for the seated case so future listeners fire; defensive direct `Leave` as belt-and-suspenders.

**Forced leave paths (already shipped):**
- `Character.AutoLeaveOccupiedFurniture("…")` called from `SetCombatState(true)`, `SetUnconscious(true)`, `Die()` — runs **before** the matching event so subscribers see `OccupyingFurniture == null`. The subsequent `ClearCurrentActionLocally` fires `OnCancel` which calls `Leave` again — idempotent.

**Movement lockout:**
- `PlayerController.Move`, `CharacterMovement.SetDestination`, `CharacterMovement.SetDesiredDirection` all early-return on `OccupyingFurniture != null`. Reads the replicated value from §3.b, so the gate fires correctly on every peer.

**Replaced (deleted 2026-05-14):** `Cashier.ServerTickAutoOccupy` — proximity-driven server tick that bypassed the action system. See [docs/superpowers/specs/2026-05-14-furniture-occupancy-via-characteraction-design.md](../../../docs/superpowers/specs/2026-05-14-furniture-occupancy-via-characteraction-design.md).

## 4. Context Switching (The Brain)
**A Player is exactly like an NPC character.** They share the exact same `Character` object, underlying components, stats, and game logic. A character in your game can switch from an autonomous civilian AI (NPC) to a Player-controlled Avatar with a snap of a finger just by swapping the active controller.

- `SwitchToPlayer()`: 
  - Swaps controllers and interaction detectors.
  - **UI Setup**: Finds the GameObject **"UI_PlayerHUD"** and calls `PlayerUI.Initialize(this)`. This pushes notification channels to the equipment system.
- `SwitchToNPC()`: 
  - Reverts controllers and reactivates NavMesh.
  ## 5. Character Actions and Movement Control
The `CharacterActions` component manages distinct, timed actions (Harvesting, Crafting, Attacking). These actions are integrated into the `CharacterGameController` via an event-driven system to manage character availability and movement.

- **`OnActionStarted`**: Triggered when a `CharacterAction` begins. The controller automatically stops movement and sets the `isDoingAction` animator bool (if the action allows it).
- **`OnActionFinished`**: Triggered when an action ends or is cancelled. This initiates a short **Action Cooldown** (default: 0.5s) before the character can resume navigation.
- **`ShouldPlayGenericActionAnimation`**: Each `CharacterAction` can opt-out of the generic "busy" animation to prevent flickering or overriding specific animations (like Combat).

### CharacterAction_Continuous — condition-terminated actions

A sibling of `CharacterAction` for actions that **terminate on a condition rather than a fixed timer**. Authored 2026-05-06 for the construction loop ([spec](../../docs/superpowers/specs/2026-05-06-building-construction-loop-design.md)).

**When to inherit `CharacterAction_Continuous`:**
- The action consumes/produces resources progressively until a goal is met (construction, smelting in a furnace, healing a target until full HP, escorting until destination reached).
- The duration is data-driven, unknown up-front, or open-ended.
- You want native cancel-on-movement (default `AllowsMovementDuringAction = false` cancels via `CharacterGameController` the moment the controller detects movement intent).
- You want the action to outlive the cooldown of a normal timed action without polluting the duration model.

**Contract:**

```csharp
public abstract class CharacterAction_Continuous : CharacterAction
{
    // Server tick cadence. Default 1 Hz; subclasses may override in their constructor.
    public float TickIntervalSeconds { get; protected set; } = 1f;

    // Server-ticked. Return true when the terminating condition has been met.
    public abstract bool OnTick();

    // Sealed to prevent accidental subclass overrides re-introducing duration semantics.
    public sealed override void OnApplyEffect() { /* no-op */ }

    // HUD progress bar reads this. Default 0 (no bar fill). Override to drive the bar
    // from your own state (e.g. Building.ConstructionProgress.Value). Added 2026-05-07.
    // CharacterActions.GetActionProgress checks this BEFORE falling back to elapsed/duration
    // — that fallback would div-by-0 (or read the 600s sentinel, see below) for continuous actions.
    public virtual float Progress => 0f;

    // Base ctor passes Duration = 0 — the dispatcher must check Continuous BEFORE the
    // Duration <= 0 branch, otherwise these would be treated as instantaneous actions.
    protected CharacterAction_Continuous(Character c) : base(c, duration: 0f) { }
}
```

**Visual proxy 600s sentinel + cancel broadcast** (added 2026-05-07): `CharacterActions.ExecuteAction` calls `BroadcastActionVisualsClientRpc(duration=600f)` for `CharacterAction_Continuous` because continuous actions don't have a real duration. The proxy on every peer ticks until cancellation. **Server MUST broadcast `CancelActionVisualsClientRpc` when `OnTick` returns true** (or on any other action-ending path: stall timeout, manual cancel) so peers tear down the proxy immediately — without it the proxy lingers 600s after the server-side action ends. `CharacterActions.Finish` already does this; if you wire a custom termination path, make sure the cancel broadcast still fires.

**Dispatcher contract in `CharacterActions.ExecuteAction`:**

The continuous-action branch must come **before** the `Duration <= 0` instant-action branch. Order matters because the base constructor passes `duration: 0f`:

```csharp
// CharacterActions.ExecuteAction (excerpt)
_currentAction.OnStart();

if (action is CharacterAction_Continuous continuous)
{
    _actionRoutine = StartCoroutine(ActionContinuousTickRoutine(continuous));
}
else if (action.Duration <= 0)        // instant
{
    action.OnApplyEffect();
    Finish(action);
}
else                                  // timed
{
    _actionRoutine = StartCoroutine(ActionRoutine(action));
}
```

`ActionContinuousTickRoutine` waits `WaitForSeconds(action.TickIntervalSeconds)`, calls `OnTick()`, and finishes the action if `OnTick` returns `true`. It does NOT touch `OnApplyEffect`.

**Authoring rules:**
- Implement `OnTick()` to do all per-tick work and return `true` when done.
- Use `OnStart()` to initialize per-action state (counters, scratch buffers, target captures).
- Use `OnCancel()` to release any per-action holds — the runner already handles routine teardown.
- Override `CanExecute()` for entry-time validation (state, ownership, range). Re-validate inside `OnTick()` for any condition that can change mid-action — the runner will not re-call `CanExecute`.
- Default `AllowsMovementDuringAction = false` (inherited from `CharacterAction`). Override to `true` only if your action drives its own movement (chase, follow, escort).
- For server-authoritative effects (spawning/despawning `NetworkObject`s, mutating scene-shared state), follow the same `IsSpawned && !IsServer` pattern as regular actions — see "Client-vs-server routing pattern for OnApplyEffect" below. **For continuous actions, the routing happens inside `OnTick`, not `OnApplyEffect` (which is sealed to no-op).**

**Reference implementation:** `CharacterAction_FinishConstruction` (`Assets/Scripts/Character/CharacterActions/CharacterAction_FinishConstruction.cs`) — cooperative (no owner gate; spatial gate only — actor must be inside `Building.BuildingZone` per a 2D X-Z check), server-only consumption of `WorldItem`s in a `Building`'s footprint until `Building.ComputeProgress() >= 1f`, then calls `Building.Finalize()`. Overrides `Progress` to return `Building.ConstructionProgress.Value` (replicated NetworkVariable read by HUD). See the `building_system` SKILL ("Construction Loop (Phase 1 — Cooperative)" section) for the full lifecycle.

### Server RPCs on CharacterActions

`CharacterActions` hosts ServerRpcs for operations that require server authority but are triggered from client-owned actions:

- **`RequestDespawnServerRpc(NetworkObjectReference)`**: Generic despawn for any NetworkObject. Used by `CharacterPickUpItem` and `CharacterPickUpFurnitureAction` to remove WorldItems/Furniture from the network. Clients cannot call `NetworkObject.Despawn()` directly — always route through this RPC.
- **`RequestCraftServerRpc(...)`**: Server-side crafting via CraftingStation.
- **`RequestHarvestServerRpc(Vector3 harvestablePosition)`**: Server-side harvest execution. Resolves the `Harvestable` by position, runs `Harvest()`, spawns the yield as a `WorldItem`, and registers a `PickupLooseItemTask` on the worker's workplace. Paired with `ApplyHarvestOnServer(Harvestable)` which is the shared server/offline helper — callable directly from the server or offline path.
- **`RequestItemDropServerRpc(itemId, jsonData, ownerPosition)`**: Server-side drop. Rehydrates the `ItemInstance` from JSON and spawns a `WorldItem` near the character.
- **`RequestFurniturePlaceServerRpc(itemSOId, visualPos, gridAnchor, rotation)`**: Server instantiates + spawns furniture + registers on grid.
- **`RequestFurniturePickUpServerRpc(NetworkObjectReference)`**: Server unregisters furniture from grid + despawns.

### Client-vs-server routing pattern for `OnApplyEffect`

An action whose effect is server-authoritative (spawning/despawning NetworkObjects, mutating scene-shared state) must detect whether it runs on a networked client and forward the work via ServerRpc. The canonical check is:

```csharp
var actions = character.CharacterActions;
bool isNetworkedClient = actions.IsSpawned && !actions.IsServer;
if (isNetworkedClient)
    actions.RequestXxxServerRpc(...);      // networked client → server
else
    actions.ApplyXxxOnServer(...);         // server OR offline (IsSpawned == false)
```

Using `!IsServer` alone is **wrong** — it also matches offline mode (no active NetworkManager), where the RPC would silently drop. `IsSpawned && !IsServer` is the safe "networked-client only" test. See `CharacterHarvestAction`, `CharacterCraftAction`, `CharacterDropItem` for the pattern in practice.

> [!IMPORTANT]
> Any `CharacterAction.OnApplyEffect()` that needs to Spawn or Despawn a `NetworkObject` **must** use a ServerRpc on `CharacterActions`. `OnApplyEffect` runs on the owner (which may be a client), but only the server can Spawn/Despawn. Never call `NetworkObject.Spawn()` or `Despawn()` directly in an action — always route through `character.CharacterActions.RequestDespawnServerRpc()` or a specialized RPC.

> [!IMPORTANT]
> To stop a character during an action, always prefer using the `CharacterActions` system rather than manually calling `Stop()` in `Update()`. This ensures consistent behavior across Player and NPC controllers.

> In case of an input or navigation bug, always first verify that the correct Controller is turned on via this Switch system.
