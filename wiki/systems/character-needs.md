---
type: system
title: "Character Needs"
tags: [character, needs, ai, tier-2]
created: 2026-04-18
updated: 2026-05-15
sources: []
related:
  - "[[character]]"
  - "[[ai]]"
  - "[[world]]"
  - "[[shops]]"
  - "[[world-time-skip]]"
  - "[[kevin]]"
  - "[[interactable-proximity-distance-anti-pattern]]"
status: stable
confidence: high
primary_agent: character-system-specialist
secondary_agents:
  - npc-ai-specialist
owner_code_path: "Assets/Scripts/Character/CharacterNeeds/"
depends_on:
  - "[[character]]"
  - "[[shops]]"
depended_on_by:
  - "[[ai]]"
  - "[[world]]"
  - "[[items]]"
---

# Character Needs

## Summary
Per-character drives (Hunger, Social, Sleep, etc.) that decay over simulation time and feed GOAP state. Each need is a provider with a current value, decay rate, and thresholds. When a need crosses a threshold it fires events ([[ai]] reads these to select goals like "eat", "sleep", "socialize"). Needs are macro-simulation friendly — the `MacroSimulator` computes offline decay as pure math during map hibernation (see [[world]]).

## Purpose
Give every character a tractable set of drives that AI can plan against, without framelocking decay to Unity's Update. The same need definition runs during live ticks **and** during offline catch-up, ensuring the player returns to a coherent world where NPCs are hungrier / sleepier / lonelier in proportion to time away.

## Responsibilities
- Defining a need (`CharacterNeed` providers) — current value, min/max, decay per in-game day.
- Decaying needs on simulation tick (scaled by [[game-speed-controller]] — Simulation Time).
- Computing offline delta for the `MacroSimulator` catch-up pass.
- Firing threshold events (`OnNeedCritical`, `OnNeedSatisfied`) consumed by [[ai]].
- Satisfying needs via actions (eat, sleep, socialize, buy item) — restore or set to max.
- Providing a registry so new needs can be injected without modifying core code.

**Non-responsibilities**:
- Does **not** decide what action to take — see [[ai]] `GoapAction_*` handles resolution.
- Does **not** own shop buying logic — see [[shops]] for `NeedItem` resolution.
- Does **not** own stamina/mana/HP regeneration — that's [[character-stats]].

## Key classes / files

- `Assets/Scripts/Character/CharacterNeeds/CharacterNeeds.cs` — component on child GameObject.
- `Assets/Scripts/Character/CharacterNeeds/CharacterNeed.cs` — base need provider.
- Specialized subclasses per need type (Hunger, Social, Sleep, …).
- `Assets/Scripts/Character/CharacterNeeds/NeedProvider.cs` — registry pattern entry.
- Referenced by `HibernatedNPCData` for serialization.

## Public API

- `character.Needs.GetNeed<T>()` — typed getter.
- `need.CurrentValue`, `need.MaxValue`, `need.DecayPerDay`.
- `need.Satisfy(amount)` / `need.Set(value)`.
- `need.OnNeedCritical`, `need.OnNeedSatisfied` events.
- `CharacterNeeds.ComputeOfflineDecay(deltaDays)` — used by [[world]] macro-sim.

### NeedHunger (added 2026-04-26, made server-authoritative 2026-04-26)

Phase-decay need (25 per `TimeManager.OnPhaseChanged`, 4× per in-game day → fully empty in 24 h).

**Server-authoritative as of 2026-04-26:** the actual current value lives in a `NetworkVariable<float>` on `CharacterNeeds` (read: Everyone, write: Server). `NeedHunger` is a thin POCO bridge — reads the NV, routes writes through the server (direct or via `RequestAdjustHungerRpc` ServerRpc), and forwards `NetworkVariable.OnValueChanged` to its public `OnValueChanged` / `OnStarvingChanged` events. HUD code is unchanged because the public surface (`MaxValue`, `CurrentValue`, `OnValueChanged`, `OnStarvingChanged`) is preserved.

- `IsStarving` — true when networked value ≤ 0.
- `OnStarvingChanged(bool)` — fired on transitions, on every peer.
- `OnValueChanged(float)` — fired on every networked value change, on every peer.
- `IncreaseValue(float)` / `DecreaseValue(float)` — server: direct write. Client: ServerRpc.
- `IsLow()` — true at or below 30 (the GOAP activation threshold).
- **Emergency threshold:** `NeedHungerMath.DEFAULT_EMERGENCY_THRESHOLD = 10` (need ≥ 90%). Used to gate the ground-pickup fallback inside `GetGoapActions`.
- `TrySubscribeToPhase()` / `UnsubscribeFromPhase()` — defensive TimeManager subscription. Decay handler gated by `IsServer` to prevent double-decay.
- `BindNetworkBridge()` / `UnbindNetworkBridge()` — wires NV.OnValueChanged → public events. Called in `OnNetworkSpawn` / `OnNetworkDespawn`.
- `SetCooldown()` — rearms the GOAP activation cooldown after eating.

**Spawn-order fix:** `CharacterNeeds` registration was moved from `Start()` to `Awake()` so `GetNeed<NeedHunger>()` works inside `OnNetworkSpawn`, before `PlayerUI.Initialize → UI_HungerBar.Initialize` fires. Previously the local-owner client's HUD initialised with `null` and displayed `0/0`.

**GOAP resolver — shop-first with emergency-only ground fallback (rewritten 2026-05-15):** `NeedHunger.GetGoapActions()` returns one chain at a time. The default route is to buy food from a shop; the loose-food-on-the-ground route is reserved for extreme hunger.

1. **Shop path (default).** `TryFindShopFood` walks every `ShopBuilding` registered with `BuildingManager.allBuildings`, iterates each shop's `Catalog` for entries whose `ItemSO is FoodSO`, then filters by: cashier available, wallet can afford `ShopBuilding.ResolvePrice(entry)`, at least one matching `ItemInstance` is on a sell-shelf, and the NPC has inventory or hands space. The candidate with the highest `FoodSO.HungerRestored / price` (most filling per coin) wins; price ≤ 0 entries get a very large constant score so they always win. Chain: `[GoapAction_BuyFood(shop, cashier, foodSO) → GoapAction_EatCarriedFood]`. Effect keys: `carryingFood` → `isHungry=false`. `GoapAction_BuyFood` queues the same `CharacterAction_BuyFromShop(BuyMode.NPC)` the player buy uses.
2. **Ground pickup (emergency only).** Only runs when `CurrentValue ≤ NeedHungerMath.DEFAULT_EMERGENCY_THRESHOLD` (10 — the need is at ≥ 90%). Scans `_character.CharacterAwareness.GetVisibleInteractables()` for the first non-carried `WorldItem` whose `ItemInstance is FoodInstance`. Chain: `[GoapAction_GoToWorldFood → GoapAction_PickupWorldFood → GoapAction_EatCarriedFood]`. Effect keys: `atWorldFood` → `carryingFood` → `isHungry=false`.

The legacy workplace-storage path (`GoapAction_GoToFood` + `GoapAction_Eat`) is retired from `NeedHunger`: NPCs no longer self-serve from their employer's storage furniture. The action classes are kept in the codebase pending a future personal/owned-storage concept. See `[[shops]]` for the buy commit pipeline.

For procedural details (decay formula, full GOAP integration, macro-sim catch-up, ServerRpc surface) see [.agent/skills/character_needs/SKILL.md](../../.agent/skills/character_needs/SKILL.md).

### NeedToWearClothing (shop-buy migration added 2026-05-15)

Slot-presence need: active when `CharacterEquipment.IsChestExposed()` OR `IsGroinExposed()` returns true across all three wearable layers (Underwear / Clothing / Armor). Urgency is 100 (groin exposed) or 60 (chest only). Goal is `{"isNaked": false}`.

**GOAP resolver — shop-first with ground-pickup fallback (rewritten 2026-05-15):** `NeedToWearClothing.GetGoapActions()` returns one chain at a time, mirroring the `NeedHunger` shape:

1. **Shop path (default).** `TryFindShopClothing` walks `BuildingManager.allBuildings` and, for each missing slot in priority order (`Pants` first if groin exposed, then `Armor` if chest exposed — matches the urgency math), iterates each shop's `Catalog` for `ItemSO is WearableSO ws && ws.WearableType == slot`. Filters: cashier available, wallet covers `ShopBuilding.ResolvePrice(entry)`, sell-shelf stock present, inventory or hands space free. Scoring picks the cheapest in-slot. Chain: `[GoapAction_BuyClothing(shop, cashier, wearableSO) → GoapAction_EquipCarriedClothing]`. Effect keys: `carryingClothing` → `isNaked=false`. The buy reuses `CharacterAction_BuyFromShop(BuyMode.NPC)`; the equip reuses `CharacterEquipAction` (same path the player uses).
2. **Ground-pickup fallback.** The pre-existing monolithic `GoapAction_WearClothing` — scans `CharacterAwareness.GetVisibleInteractables<ItemInteractable>()` for a loose `WearableInstance` matching a missing type, walks, queues `CharacterEquipAction`. Used only when no shop carries an affordable matching wearable.

Movement gate inside `GoapAction_BuyClothing` follows CLAUDE.md rule #36 (`IsCharacterInInteractionZone` containment + softlock guard + path-loss recovery — see [[interactable-proximity-distance-anti-pattern]]).

For full GOAP integration, multiplayer audit, and key files see [.agent/skills/character_needs/SKILL.md §NeedToWearClothing](../../.agent/skills/character_needs/SKILL.md).

## Data flow

```
TimeManager.CurrentTime01 advances (scaled by GameSpeedController)
       │
       ▼
CharacterNeeds.Tick(delta)
       │
       ├── for each need: current -= rate * delta
       ├── fire OnNeedCritical if threshold crossed
       └── fire OnNeedSatisfied on restore
       │
       ▼
[[ai]] reads need state via GOAP state variables
       │
       ▼
Selects goal (Eat, Sleep, Socialize) → plans action chain
```

Offline (hibernation):
```
Map wakes up
       │
       ▼
MacroSimulator.CatchUp(deltaDays)
       │
       ▼
for each HibernatedNPCData:
       └── CharacterNeeds.ComputeOfflineDecay(deltaDays) — pure math, no Update
```

## Dependencies

### Upstream
- [[character]] — component on a child GameObject.
- [[game-speed-controller]] — ticks run in Simulation Time.

### Downstream
- [[ai]] — GOAP state variables derive from needs; BT social slot triggers on `WantsToSocialize`.
- [[world]] — macro-sim uses offline decay formula.

## State & persistence

- Current value, min/max, decay rate per need — saved on the character profile.
- Decay rates generally live on the need's asset/config, not per-character.
- `HibernatedNPCData` snapshots current values only; decay rate is recomputed from the definition.

## Known gotchas

- **Time base = Simulation Time** — use `Time.deltaTime * GameSpeedController.Scale` (or the managed tick). Never `Time.unscaledDeltaTime` for decay.
- **Offline formula must match online** — the macro-sim integrates over the time delta exactly as the online tick would. Divergence causes "NPC looks surprisingly hungry" on wake.
- **Satisfy saturates** — `Satisfy(x)` caps at `MaxValue`. Clamp on the way in, not on read.
- **Thresholds drive AI** — pick thresholds per need carefully; oscillation at the boundary churns GOAP replans.

## Open questions

- [ ] Full list of concrete needs — scan `CharacterNeeds/` for subclasses when expanding.
- [ ] Do any needs interact (e.g., low sleep caps max HP)? Probably yes in future — flag.

## Change log
- 2026-04-18 — Initial pass. — Claude / [[kevin]]
- 2026-04-26 — added NeedHunger (phase-tick decay, IsStarving event) + FoodSO consumable subtype + GoapAction_GoToFood/Eat — claude
- 2026-04-26 — NeedHunger made server-authoritative via `NetworkVariable<float>` on `CharacterNeeds`; eat path routes through `RequestAdjustHungerRpc`; need registration moved from Start→Awake to fix `0/0` HUD bug on local-owner clients — claude
- 2026-04-26 — NeedHunger gained a second food source: loose `WorldItem`s in awareness radius. New chain `[GoapAction_GoToWorldFood → GoapAction_PickupWorldFood → GoapAction_EatCarriedFood]` preempts the workplace-storage chain when ground food is detected. Disjoint state keys (`atWorldFood` / `carryingFood` vs `atFood`) prevent planner cross-linking — claude
- 2026-05-15 — NeedHunger food-acquisition rewired: shop-buy is now the default path via new `GoapAction_BuyFood` (chains `[GoapAction_BuyFood → GoapAction_EatCarriedFood]` and reuses `CharacterAction_BuyFromShop(BuyMode.NPC)`). The ground-pickup chain is gated behind a new emergency threshold (`NeedHungerMath.DEFAULT_EMERGENCY_THRESHOLD = 10`, i.e. need ≥ 90%). The workplace-storage path was retired — NPCs no longer self-serve from their employer's storage. Action classes `GoapAction_GoToFood` / `GoapAction_Eat` remain in the codebase pending a personal/owned-storage concept — claude
- 2026-05-15 — NeedToWearClothing clothing-acquisition rewired: shop-buy is now the default path via new `GoapAction_BuyClothing` + `GoapAction_EquipCarriedClothing` (chains `[GoapAction_BuyClothing → GoapAction_EquipCarriedClothing]` and reuses `CharacterAction_BuyFromShop(BuyMode.NPC)` + `CharacterEquipAction`). The legacy monolithic `GoapAction_WearClothing` (ground pickup from `CharacterAwareness`) is preserved as the fallback chain — used only when no shop carries an affordable matching wearable. Scoring prefers the most-urgent missing slot (Pants > Armor, matching the existing urgency math), then cheapest in slot. Movement gate inside `GoapAction_BuyClothing` follows CLAUDE.md rule #36 (InteractionZone containment + softlock guard + path-loss recovery). Mirrors the NeedHunger shop-buy migration — claude

## Sources
- [.agent/skills/character_needs/SKILL.md](../../.agent/skills/character_needs/SKILL.md)
- [.agent/skills/character_needs/examples/need_patterns.md](../../.agent/skills/character_needs/examples/need_patterns.md)
- [Assets/Scripts/Character/CharacterNeeds/NeedHunger.cs](../../Assets/Scripts/Character/CharacterNeeds/NeedHunger.cs)
- [Assets/Scripts/Character/CharacterNeeds/Pure/NeedHungerMath.cs](../../Assets/Scripts/Character/CharacterNeeds/Pure/NeedHungerMath.cs)
- [Assets/Scripts/Character/CharacterNeeds/Pure/HungerCatchUpMath.cs](../../Assets/Scripts/Character/CharacterNeeds/Pure/HungerCatchUpMath.cs)
- [Assets/Resources/Data/Item/FoodSO.cs](../../Assets/Resources/Data/Item/FoodSO.cs)
- [Assets/Scripts/Item/FoodInstance.cs](../../Assets/Scripts/Item/FoodInstance.cs)
- [Assets/Scripts/AI/GOAP/Actions/GoapAction_BuyFood.cs](../../Assets/Scripts/AI/GOAP/Actions/GoapAction_BuyFood.cs) — shop path (registered by NeedHunger)
- [Assets/Scripts/AI/GOAP/Actions/GoapAction_EatCarriedFood.cs](../../Assets/Scripts/AI/GOAP/Actions/GoapAction_EatCarriedFood.cs) — terminator for both surviving paths
- [Assets/Scripts/AI/GOAP/Actions/GoapAction_GoToWorldFood.cs](../../Assets/Scripts/AI/GOAP/Actions/GoapAction_GoToWorldFood.cs) — emergency ground path
- [Assets/Scripts/AI/GOAP/Actions/GoapAction_PickupWorldFood.cs](../../Assets/Scripts/AI/GOAP/Actions/GoapAction_PickupWorldFood.cs) — emergency ground path
- [Assets/Scripts/AI/GOAP/Actions/GoapAction_GoToFood.cs](../../Assets/Scripts/AI/GOAP/Actions/GoapAction_GoToFood.cs) — retired from NeedHunger; kept pending personal-storage concept
- [Assets/Scripts/AI/GOAP/Actions/GoapAction_Eat.cs](../../Assets/Scripts/AI/GOAP/Actions/GoapAction_Eat.cs) — retired from NeedHunger; kept pending personal-storage concept
- [Assets/Scripts/Character/CharacterActions/CharacterAction_BuyFromShop.cs](../../Assets/Scripts/Character/CharacterActions/CharacterAction_BuyFromShop.cs) — shared buy pipeline (BuyMode.NPC reused by GoapAction_BuyFood + GoapAction_BuyClothing)
- [Assets/Scripts/Character/CharacterNeeds/NeedToWearClothing.cs](../../Assets/Scripts/Character/CharacterNeeds/NeedToWearClothing.cs) — need implementation; shop-first / ground-fallback as of 2026-05-15.
- [Assets/Scripts/AI/GOAP/Actions/GoapAction_BuyClothing.cs](../../Assets/Scripts/AI/GOAP/Actions/GoapAction_BuyClothing.cs) — shop path (registered by NeedToWearClothing). Mirror of GoapAction_BuyFood.
- [Assets/Scripts/AI/GOAP/Actions/GoapAction_EquipCarriedClothing.cs](../../Assets/Scripts/AI/GOAP/Actions/GoapAction_EquipCarriedClothing.cs) — terminator for the shop chain; runs CharacterEquipAction.
- [Assets/Scripts/AI/GOAP/Actions/GoapAction_WearClothing.cs](../../Assets/Scripts/AI/GOAP/Actions/GoapAction_WearClothing.cs) — ground-pickup fallback (legacy monolithic action).
- [Assets/Resources/Data/Item/WearableSO.cs](../../Assets/Resources/Data/Item/WearableSO.cs) — EquipmentSO subtype carrying WearableType + WearableLayerEnum.
- [[ai]] and [[world]] (parents-of-interest).
