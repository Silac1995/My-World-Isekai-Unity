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
- `NeedHunger` -> `GoapGoal({"isHungry": false})` -> shop-first, ground-emergency-only (one chain at a time — see GOAP integration below):
  - **Shop (default):** `[GoapAction_BuyFood, GoapAction_EatCarriedFood]` — NPC walks to a `Cashier` and buys a `FoodSO` from the shop's catalog via `CharacterAction_BuyFromShop(BuyMode.NPC)`.
  - **Ground (emergency only):** `[GoapAction_GoToWorldFood, GoapAction_PickupWorldFood, GoapAction_EatCarriedFood]` — only triggered when `CurrentValue ≤ NeedHungerMath.DEFAULT_EMERGENCY_THRESHOLD` (10, i.e. need ≥ 90%).
  - **Workplace storage path (`GoapAction_GoToFood` + `GoapAction_Eat`) is retired from this need.** NPCs no longer self-serve from their employer's storage. The action classes are kept in the codebase pending a future personal/owned-storage concept but are not registered by `NeedHunger`.

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
- `IsLow()` — true at or below 30 (the GOAP activation threshold).
- **Emergency threshold:** `NeedHungerMath.DEFAULT_EMERGENCY_THRESHOLD = 10`. When `CurrentValue ≤ 10` the need is at ≥ 90% — `GetGoapActions` allows the ground-pickup fallback in addition to the shop path.
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

NPCs satisfy hunger by **buying food from a shop** (default) and only fall back to **picking food off the ground** when in extreme necessity. The legacy workplace-storage path is retired — NPCs no longer take from their employer's stores.

- `IsActive()` returns true when controller is `NPCController` AND `IsLow()` (CurrentValue ≤ 30) AND cooldown has elapsed.
- `GetGoapGoal()` → `{"isHungry": false}` with urgency `MaxValue - CurrentValue`.
- `GetGoapActions()` runs at most two scans and returns the first chain that matches:
  1. **Shop scan (`TryFindShopFood`, default).** Walks every `ShopBuilding` registered with `BuildingManager.allBuildings`. For each shop, iterates `Catalog` for entries whose `ItemSO is FoodSO`, then filters by: cashier available (`GetFirstAvailableCashier`), wallet can afford `ShopBuilding.ResolvePrice(entry)`, at least one matching `ItemInstance` is on a sell-shelf (`ShopHasItemInStock`), and the NPC has inventory or hands space (`HasFreeSpaceForItemSO` / `AreHandsFree`). Scoring picks the maximum `FoodSO.HungerRestored / price` — most filling per coin (free entries with `price ≤ 0` get a very large constant score so they always win). On a hit returns `[GoapAction_BuyFood(shop, cashier, foodSO), GoapAction_EatCarriedFood]`.
  2. **Emergency ground scan (`TryFindWorldFood`).** Only runs when `CurrentValue ≤ NeedHungerMath.DEFAULT_EMERGENCY_THRESHOLD` (10). Reads `_character.CharacterAwareness.GetVisibleInteractables()` for the first non-carried `WorldItem` whose `ItemInstance is FoodInstance`. On hit returns `[GoapAction_GoToWorldFood, GoapAction_PickupWorldFood, GoapAction_EatCarriedFood]`.
- The two chains share the single `_searchCooldown` bucket — there is no separate cooldown per path.
- The two chains use **disjoint** intermediate world-state keys (`atWorldFood` + `carryingFood` for the ground path, just `carryingFood` for the shop path) — both terminate in `GoapAction_EatCarriedFood`, which is reusable because the planner reaches `carryingFood = true` either by buying or by picking up.

### Multiplayer audit (six-question, per rule #19b)
1. **Who writes / who reads the state?** `NeedHunger.CurrentValue` is server-written (phase decay + eat effects) and read by everyone via the `_networkedHunger` NetworkVariable. The new `GoapAction_BuyFood` lives entirely server-side — GOAP planning and `CharacterAction_BuyFromShop(BuyMode.NPC)` are both server-only paths.
2. **Replication channel?** Hunger value: existing NetworkVariable. The buy itself replicates via the existing `CharacterAction_BuyFromShop` pipeline (`Cashier.CreditTill` → `CashierNetSync` ClientRpc, `CharacterWallet.RemoveCoins` → wallet ClientRpc, `StorageFurniture` slot removal via existing slot replication, item delivery via `CharacterEquipment.PickUpItem` / `ReceiveItemPickupClientRpc`). No new RPCs.
3. **Late-joiner?** New connecting clients see the NPC walking to a cashier, the wallet/till update, and the food in the NPC's inventory all through existing replication — nothing new to publish. NetworkVariable seeds catch them up.
4. **Client-side pre-gate?** None. Players never trigger this code path (`IsActive` excludes `PlayerController`).
5. **`GetComponentInParent` Awake races?** None added — `GoapAction_BuyFood` is a plain C# class instantiated by `NeedHunger.GetGoapActions`; it holds direct references to `ShopBuilding` / `Cashier` / `FoodSO` chosen at planning time.
6. **Proximity gating?** Movement to the cashier uses `Vector3.Distance` against `Cashier.GetInteractionPosition(worker.transform.position)` — matches the existing `GoapAction_GoShopping` shape. The buy action itself uses the same `IsCharacterInInteractionZone` gate as the player buy via shared `CharacterAction_BuyFromShop`.

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
- `Assets/Scripts/AI/GOAP/Actions/GoapAction_BuyFood.cs` — **shop path**: walks to a `Cashier` and runs `CharacterAction_BuyFromShop(BuyMode.NPC)` for the chosen `FoodSO` (effect `carryingFood = true`).
- `Assets/Scripts/AI/GOAP/Actions/GoapAction_EatCarriedFood.cs` — terminator for both surviving paths: scans hands first then inventory for a `FoodInstance`, runs `CharacterUseConsumableAction` (effect `isHungry = false`).
- `Assets/Scripts/AI/GOAP/Actions/GoapAction_GoToWorldFood.cs` — **emergency ground path**: navigates to a loose `WorldItem` whose instance is a `FoodInstance` (effect `atWorldFood = true`).
- `Assets/Scripts/AI/GOAP/Actions/GoapAction_PickupWorldFood.cs` — **emergency ground path**: runs `CharacterPickUpItem` on the loose food (effect `carryingFood = true`).
- `Assets/Scripts/AI/GOAP/Actions/GoapAction_GoToFood.cs` / `GoapAction_Eat.cs` — **retired** for hunger as of the shop-buy migration; left in the codebase pending a future personal/owned-storage concept. Not registered by `NeedHunger`.
