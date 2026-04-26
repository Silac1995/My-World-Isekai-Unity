---
name: character-needs
description: The autonomous decision-making layer that pushes NPCs to act based on internal drives (Social interaction, Finding a Job, Dressing up).
---

# Character Needs (GOAP Providers)

The `CharacterNeeds` system holds a list of "Needs" that decrease over time and trigger specialized GOAP goals when they become urgent.

## When to use this skill
- When creating a new interior drive for NPCs (e.g., Hunger, Sleep).
- When debugging why an NPC isn't prioritizing a specific internal state.
- When creating new `GoapAction`s to satisfy a need.

## The SOLID Provider Architecture

Previously, the Needs system was imperative: a need would evaluate itself and directly push a `MoveToTargetBehaviour` into the `NPCController`, entirely bypassing the intelligence of the Planner.

Now, `CharacterNeeds` acts exclusively as a **Dependency Injector** for the `CharacterGoapController`.

### 1. Creating a New Need
Inherit from `CharacterNeed` and implement the abstract provider methods. **Rule:** A Need should NEVER execute logic or touch the Behaviour Tree.
- `IsActive()`: Returns a boolean to indicate if the need should be fulfilled.
- `GetUrgency()`: Returns a priority value.
- `GetGoapGoal()`: Returns the concrete `GoapGoal` (e.g., `isFull = true`) the planner must achieve.
- `GetGoapActions()`: Returns the list of logical actions capable of fulfilling the goal (e.g., `new GoapAction_EatFood()`).

### 2. Event-Driven Decay (`update-usage`)
To adhere to the `update-usage` constraints, do not check `need.Tick(Time.deltaTime)` every frame in `Update()`.
Instead, `CharacterNeeds` manages slow-ticking Coroutines:
```csharp
private IEnumerator SocialDecayCoroutine()
{
    while (true)
    {
        yield return new WaitForSeconds(1f);
        _socialNeed?.DecreaseValue(3f);
    }
}
```

### 3. Execution via GOAP
Because Needs are simply Data Providers, the resolution happens naturally in Priority 5 of the `NPCBehaviourTree` (`BTAction_ExecuteGoapPlan`):
1. `CharacterGoapController` iterates through `_character.CharacterNeeds.AllNeeds`.
2. It extracts their Goals if `IsActive() == true` and their Urgency.
3. The Planner chains the `GoapActions` provided by the needs to reach the Desire state.

### 4. Existing Needs & Actions
- `NeedSocial` -> `GoapGoal("Socialize")` -> `GoapAction_Socialize`.
- `NeedJob` -> `GoapGoal("FindJob")` -> `GoapAction_AskForJob`.
- `NeedToWearClothing` -> `GoapGoal("WearClothing")` -> `GoapAction_WearClothing`.
- `NeedShopping` -> `GoapGoal("GoShopping")` -> `GoapAction_GoShopping`.
- `NeedHunger` -> `GoapGoal({"isHungry": false})` -> `[GoapAction_GoToFood, GoapAction_Eat]`.

---

## NeedHunger

Phase-decay need that drains 25 per `TimeManager.OnPhaseChanged` tick (4× per in-game day, fully empty in 24 h).

**As of 2026-04-26: server-authoritative.** The actual current value lives in a `NetworkVariable<float>` on `CharacterNeeds` (`NetworkVariableReadPermission.Everyone`, `NetworkVariableWritePermission.Server`). `NeedHunger` itself is a thin POCO bridge — it reads the NV through `CharacterNeeds.NetworkedHungerValue`, routes writes through the server (direct NV write if `IsServer`, else `RequestAdjustHungerRpc(delta)` ServerRpc), and bridges `NetworkVariable.OnValueChanged` to its public `Action<float>` events so HUD code is unchanged.

### Public API
- `OnValueChanged(float)` — fired on every networked value change (every peer).
- `OnStarvingChanged(bool)` — fired whenever the starving flag transitions (every peer).
- `IncreaseValue(float)`, `DecreaseValue(float)` — server: direct NV write. Client: ServerRpc.
- `CurrentValue` (getter) — reads the NV. Setter: server-direct or ServerRpc.
- `IsStarving` — recomputed every NV change; true when networked value ≤ 0.
- `IsLow()` — true at or below 30.
- `TrySubscribeToPhase()` / `UnsubscribeFromPhase()` — defensive TimeManager subscription. Now called in `CharacterNeeds.OnNetworkSpawn` (every peer); decay handler is gated by `IsServer` so only the server actually decays.
- `BindNetworkBridge()` / `UnbindNetworkBridge()` — wires `NetworkVariable.OnValueChanged` → `OnValueChanged` / `OnStarvingChanged`. Idempotent. Subscribed in `OnNetworkSpawn`, unsubscribed in `OnNetworkDespawn`.
- `SetCooldown()` — rearms the GOAP activation cooldown after eating.

### Lifecycle
- Constructed in `CharacterNeeds.Awake()` (moved from `Start()` so `GetNeed<NeedHunger>()` works inside `OnNetworkSpawn`, before HUD initialization).
- Server seeds the NV to `DEFAULT_START` (80) in `CharacterNeeds.OnNetworkPreSpawn`. Save-restore (`Deserialize`) overwrites with the saved value if applicable.
- Bridge bound in `CharacterNeeds.OnNetworkSpawn` (every peer); unbound in `OnNetworkDespawn`.
- Phase decay subscribed in `OnNetworkSpawn`; the handler is server-gated.

### Networking authority
- **Server** runs phase decay, NPC GOAP eat effects, and player-character eat effects (via ServerRpc from the client).
- **Clients** observe via `NetworkVariable.OnValueChanged` → `NeedHunger.OnValueChanged`.
- **Eat path:** `FoodInstance.ApplyEffect` calls `hunger.IncreaseValue(amount)`. On the server this writes the NV directly. On a client (player E-key flow) it fires `RequestAdjustHungerRpc(amount)`. Either way the new value replicates to all peers.
- **Pre-existing inventory/hands gap:** `Inventory.RemoveItem` and `HandsController.ClearCarriedItem` are NOT yet networked. When a client-owned player eats, the host does NOT see the bread leave the client's inventory or hands — that's a separate bug outside the hunger-sync fix. Hunger value is correctly synced; inventory state is not. Track separately if needed.

### GOAP integration
- `IsActive()` returns true when controller is `NPCController` AND `IsLow()` AND cooldown has elapsed.
- `GetGoapGoal()` → `{"isHungry": false}` with urgency `MaxValue - CurrentValue`.
- `GetGoapActions()` scans `CharacterJob.Workplace.GetItemsInStorageFurniture()` for any `FoodSO` item and returns `[GoapAction_GoToFood, GoapAction_Eat]`.

### Persistence
- `Serialize()` reads `NeedHunger.CurrentValue` which reads the NV — works on the server (the only place save runs).
- `Deserialize(NeedsSaveData)` writes `matchingNeed.CurrentValue = entry.value`. On the server this writes the NV directly; the value replicates to all clients.
- Macro-sim catch-up still mutates `HibernatedNPCData.SavedNeeds` (offline data) directly — that's not a live `NeedHunger` instance and is unaffected by the network-authority change.

### Macro-simulation catch-up
- `MacroSimulator.SimulateNPCCatchUp` has a NeedHunger branch that calls `MWI.Needs.HungerCatchUpMath.ApplyDecay` at a rate of 100/24 per hour (matching the online decay of 25 per phase × 4 phases/day).

### Key files
- `Assets/Scripts/Character/CharacterNeeds/NeedHunger.cs` — need implementation.
- `Assets/Scripts/Character/CharacterNeeds/Pure/NeedHungerMath.cs` — pure math helpers (no Unity dependencies).
- `Assets/Scripts/Character/CharacterNeeds/Pure/HungerCatchUpMath.cs` — offline catch-up formula.
- `Assets/Resources/Data/Item/FoodSO.cs` — `ConsumableSO` subtype with `_hungerRestored` + `FoodCategory`.
- `Assets/Scripts/Item/FoodInstance.cs` — `ConsumableInstance` subtype; `ApplyEffect` overrides to call `NeedHunger.IncreaseValue`.
- `Assets/Scripts/AI/GOAP/Actions/GoapAction_GoToFood.cs` — navigates to storage furniture with food.
- `Assets/Scripts/AI/GOAP/Actions/GoapAction_Eat.cs` — executes the eat action and restores hunger.
