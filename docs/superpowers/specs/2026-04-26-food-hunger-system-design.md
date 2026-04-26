# Food & Hunger System — Design Spec

**Date:** 2026-04-26
**Author:** brainstorming session (Kevin + Claude)
**Status:** Draft, awaiting user review

## Summary

A new `NeedHunger` decays once per `DayPhase` transition (4 ticks/day) and is restored by consuming a new `FoodSO` item subtype. Both player and NPC eat through the existing `CharacterUseConsumableAction` pipeline (rule #22 parity). v1 has no mechanical consequence at hunger 0 beyond a `Starving` flag + event; the future "max-stamina cap" status effect is a follow-up PR that hooks the event.

## Goals

- New `FoodSO : ConsumableSO` carrying `_hungerRestored` and a `_foodCategory` enum.
- New `NeedHunger : CharacterNeed` with phase-based decay and offline catch-up.
- Player can eat by carrying a consumable in hand and pressing **E**.
- Hungry NPCs autonomously seek food in their job/home `StorageFurniture` via GOAP.
- Hunger persists through portal-gate save and bed save (existing `NeedsSaveData` path).
- Player HUD shows a hunger bar.
- Clean event seam (`OnStarvingChanged`) for a future `StarvingStatusEffect` to plug in without modifying `NeedHunger`.

## Non-Goals (v1, deferred)

- HP damage / movement debuff while starving — v1 only emits the event.
- The actual `StarvingStatusEffect` that caps max stamina (separate follow-up PR).
- Foraging from `WildernessZone.Harvestables`.
- Cooking jobs in `CommercialBuilding` kitchens.
- Spoilage / freshness timers on food.
- Remote-player hunger bar (would need `NetworkVariable<float>`). Player only sees their own.
- NPC hunger bar above their head.
- Hotkey "eat fastest food in inventory without holding it first."
- Race/class-specific hunger curves (every Character gets the same `NeedHunger`).

## Architecture

### Approach choice: virtual `ConsumableInstance.ApplyEffect(Character)`

Each `ConsumableInstance` subclass overrides `ApplyEffect`. `Character.UseConsumable` calls the virtual and then removes the item if `DestroyOnUse`. Future consumables (potions, scrolls, antidotes) plug in by adding a new `ConsumableInstance` subclass — no branches in `Character.cs`. Satisfies SOLID rules #9/#10/#11.

Rejected alternatives:
- **`if (so is FoodSO) …` switch** in `Character.UseConsumable` — adds a branch per consumable subtype, violates Open/Closed.
- **`IItemEffect` SO-side strategy list** — composable but overkill; v1 has one effect per food.

### Files added (6)

| File | Role |
|------|------|
| `Assets/Resources/Data/Item/FoodSO.cs` | `: ConsumableSO`. Fields: `_hungerRestored: float`, `_foodCategory: FoodCategory { Raw, Cooked, Preserved }`. Overrides `InstanceType => typeof(FoodInstance)` and `CreateInstance() => new FoodInstance(this)`. |
| `Assets/Scripts/Item/FoodInstance.cs` | `: ConsumableInstance`. Overrides `ApplyEffect(Character)` → `character.CharacterNeeds.GetNeed<NeedHunger>().IncreaseValue(FoodData.HungerRestored)`. |
| `Assets/Scripts/Character/CharacterNeeds/NeedHunger.cs` | `: CharacterNeed`. Subscribes to `TimeManager.OnPhaseChanged`. Mirrors `NeedSocial`'s decay/threshold pattern. Adds `IsStarving`, `OnStarvingChanged`, `OnValueChanged` events for HUD + future status effect. |
| `Assets/Scripts/AI/GOAP/Actions/GoapAction_GoToFood.cs` | Walks to the `StorageFurniture` selected by `NeedHunger.GetGoapActions()`. Uses `InteractableObject.IsCharacterInInteractionZone(Character)` per the established API. |
| `Assets/Scripts/AI/GOAP/Actions/GoapAction_Eat.cs` | Pulls a `FoodInstance` from the open furniture slot, enqueues `CharacterUseConsumableAction` (same path as player), sets `"isHungry" = false` on plan state. |
| `Assets/UI/Player HUD/UI_HungerBar.cs` | Small MonoBehaviour subscribing to `NeedHunger.OnValueChanged`. Reuses the existing health-bar shader/material; does **not** subclass `UI_HealthBar` (which is hard-coupled to `CharacterPrimaryStats`). |

### Files modified (7) + 1 prefab

| File | Change |
|------|--------|
| `Assets/Scripts/Item/ConsumableInstance.cs` | Add `public virtual void ApplyEffect(Character character) { }` — no-op default so existing `MiscInstance`-typed consumables stay valid. |
| `Assets/Scripts/Character/Character.cs` | Replace `UseConsumable` stub: call `consumable.ApplyEffect(this)`, then if `consumable.ConsumableData.DestroyOnUse`, clear the carried item via `HandsController` and remove from `CharacterInventory`. |
| `Assets/Scripts/Character/CharacterControllers/PlayerController.cs` | Add `KeyCode.E` handler (`HandleConsumeCarriedItem`) inside the existing `IsOwner && !devMode` input block. Reads `_character.CharacterVisual.BodyPartsController.HandsController.CarriedItem`, casts to `ConsumableInstance`, enqueues `CharacterUseConsumableAction`. |
| `Assets/Scripts/Character/CharacterNeeds/CharacterNeeds.cs` | Register `NeedHunger` in `Start()`. Add `public T GetNeed<T>() where T : CharacterNeed` typed accessor. |
| `Assets/Scripts/UI/UI_PlayerInfo.cs` | Add `[SerializeField] private UI_HungerBar _hungerBar;` and `_hungerBar.Initialize(characterComponent.CharacterNeeds.GetNeed<NeedHunger>())` in `Initialize()`. |
| `Assets/Scripts/World/Simulation/MacroSimulator.cs` | Add hunger catch-up step: `newValue = max(0, oldValue - decayPerPhase * phasesElapsed)` against the `NeedHunger` entry in `HibernatedNPCData`. Mark `IsStarving = true` if it landed at 0 so the NPC respawns hungry. |
| `Assets/UI/Player HUD/UI_PlayerInfo.prefab` | Add child GameObject `HungerBar` next to `_staminaBar` carrying the new `UI_HungerBar` script. Wire the `_hungerBar` SerializeField on the prefab root. **Done via MCP Unity** (not a code edit). |

> Note: `Assets/Resources/Data/Item/ConsumableSO.cs` does **not** change — the virtual lives on `ConsumableInstance` (the runtime instance class), not the SO. Original brainstorming had this in the modified list; corrected.

### Wiki & SKILL updates (mandatory per rules #28 / #29b)

- `wiki/systems/character-needs.md` — bump `updated:`, append change-log line, update Public API to list `NeedHunger`, add a "FoodSO consumable subtype" cross-reference under `related:`.
- `.agent/skills/character-needs/SKILL.md` — create or update; document `NeedHunger`, the phase-tick subscription, `IsStarving` event, GOAP integration. (Existence to be verified by the agent during implementation.)
- `wiki/systems/items.md` (or equivalent) — note `FoodSO` in the consumable taxonomy.
- Agent files to touch (rule #29): `npc-ai-specialist` (new GOAP actions), `character-system-specialist` (new need + UseConsumable wiring), `item-inventory-specialist` (new SO subtype). Each gets a one-line append. No new agent.

## Data flow

### 1. Phase-based decay
```
TimeManager.ProgressTime → UpdatePhase → OnPhaseChanged event
  → NeedHunger.HandlePhaseChanged
      → DecreaseValue(decayPerPhase)
      → if newValue == 0 && !_isStarving: _isStarving = true; OnStarvingChanged?.Invoke(true)
```
Runs server-side for NPCs; owner-side for the player.

### 2. Player eat path (E key)
```
PlayerController.Update (IsOwner) detects KeyCode.E
  → HandleConsumeCarriedItem()
      → hands = _character.CharacterVisual.BodyPartsController.HandsController
      → if (!hands.IsCarrying) return
      → if (hands.CarriedItem is not ConsumableInstance c) return
      → if (_character.CharacterActions.CurrentAction != null) return
      → _character.CharacterActions.ExecuteAction(new CharacterUseConsumableAction(_character, c))

CharacterUseConsumableAction.OnApplyEffect (existing, 1.5s timer + Trigger_Consume anim)
  → Character.UseConsumable(c)
      → c.ApplyEffect(this)                        // FoodInstance overrides
          → CharacterNeeds.GetNeed<NeedHunger>().IncreaseValue(food.HungerRestored)
              → fires OnValueChanged → HUD bar updates
              → if was starving and now > 0: OnStarvingChanged?.Invoke(false)
      → if c.ConsumableData.DestroyOnUse: hands.Drop()/Consume() + inventory remove
```

### 3. NPC eat path (GOAP)
```
NeedHunger.IsActive() → CurrentValue ≤ 30 && cooldown elapsed && controller is NPCController
  → GetGoapGoal: { "isHungry": false }, urgency = 100 - CurrentValue
  → GetGoapActions:
      Step 1: pick a CommercialBuilding source — _character.CharacterJob?.AssignedBuilding first;
              fallback to whichever building the NPC currently has membership in (exact lookup
              determined at implementation time using existing building-membership APIs — must NOT
              scan all buildings globally).
      Step 2: scan source.GetItemsInStorageFurniture() for first pair where item.ItemSO is FoodSO.
      Step 3: if found → return [GoapAction_GoToFood(furniture), GoapAction_Eat(furniture, foodInstance)]
              if not  → cooldown set (15s, mirrors NeedSocial), return empty list

GoapAction_GoToFood:
  → walks to InteractableObject.IsCharacterInInteractionZone(_character) == true
  → relies on existing nav stack (no new movement code).

GoapAction_Eat:
  → opens furniture slot via existing StorageFurniture API to extract the FoodInstance,
  → enqueues CharacterUseConsumableAction(character, foodInstance) on _character.CharacterActions
    (same path as player — rule #22 parity),
  → on OnApplyEffect, sets "isHungry" = false in GOAP plan state.
```

### 4. Save / load
Already handled. `NeedsSaveData` serializes by `GetType().Name + CurrentValue`; adding `NeedHunger` works automatically through `CharacterNeeds.Serialize()` / `Deserialize()`. Persists in `CharacterProfileSaveData` on portal-gate / bed save.

### 5. MacroSim catch-up (rule #30)
```
MacroSimulator.RunCatchup(hibernatedNpc, phasesElapsed)
  → find NeedHunger entry by name in hibernatedNpc.NeedsData
  → newValue = max(0, oldValue - decayPerPhase * phasesElapsed)
  → write back; mark IsStarving=true if newValue == 0
```
No food consumption while hibernated. NPC eats via the GOAP path on wake.

## Networking (rule #19, validated across all relationship pairs)

| Scenario | Behavior |
|---|---|
| **Host → Client** | Host runs the NPC `NeedHunger` decay + GOAP. Client never sees NPC hunger value (UI shows nothing for NPCs in v1). |
| **Client → Host** | Client owns its own player's `NeedHunger` decay (locally driven by `TimeManager.OnPhaseChanged`, which already fires identically on both peers). Eat result is owner-authoritative. The `CharacterUseConsumableAction` and inventory removal go through existing networked paths. |
| **Client → Client** | Each client only sees its own hunger bar in v1. Listed in Open Questions. |
| **Host/Client ↔ NPC** | NPC eat is host-authoritative. Inventory slot removal in the `StorageFurniture` already syncs via the existing storage furniture network layer. |

No new `NetworkVariable` or RPC required for v1.

## HUD

- New `UI_HungerBar` script on a new child GameObject under `UI_PlayerInfo.prefab`, placed next to `_staminaBar`.
- Subscribes to `NeedHunger.OnValueChanged` and `NeedHunger.OnStarvingChanged`.
- Same shader/material as `UI_HealthBar` for visual consistency (orange/yellow palette to differentiate from health-red and stamina-green).
- All animations use `Time.unscaledDeltaTime` (rule #26 — HUD must run during pauses / GameSpeedController).
- Bar flashes red when `IsStarving == true`.
- `UI_PlayerInfo.Initialize(...)` adds the bar wiring next to existing health/stamina init.

## Tuning defaults

All `[SerializeField]` on `NeedHunger` so they can be tweaked without code changes:

| Field | Default | Notes |
|---|---|---|
| `_maxValue` | 100 | Match `NeedSocial`. |
| `_startValue` | 80 | Character spawns at 80% full (one phase below max). |
| `_lowThreshold` | 30 | Triggers GOAP search. |
| `_decayPerPhase` | 25 | 4×25 = 100/day = full empty per in-game day. |
| `_searchCooldown` | 15s | Match `NeedSocial`. |
| `FoodSO._hungerRestored` (per asset) | 10 / 30 / 50 | Apple / bread / full meal — author-tunable. |

## State & persistence

- **Per-character runtime state:** `NeedHunger.CurrentValue` (float), `NeedHunger._isStarving` (bool, derived from CurrentValue but cached so we only fire the event on transitions).
- **Save:** existing `NeedsSaveData` (no schema change).
- **Hibernation:** existing `HibernatedNPCData.NeedsData` (no schema change).
- **Macro catch-up:** new step in `MacroSimulator`.

## Validation checklist (run before declaring done — rule #verification)

- [ ] Hunger ticks 4× per in-game day; full empty in 24 in-game hours at default decay.
- [ ] Player carries a `FoodSO` item, presses **E**, animation plays, hunger restored, item removed from hands.
- [ ] Player tries to eat with no consumable in hand → no-op, no error spam.
- [ ] Player tries to eat while another `CharacterAction` is running → no-op (same guard as `HandleDropCarriedItem`).
- [ ] Hungry NPC (CurrentValue ≤ 30) walks to job-building storage, opens it, eats food, hunger restored, food slot now empty.
- [ ] NPC with no accessible food → `_searchCooldown` set, GOAP doesn't loop-spam.
- [ ] Player hunger persists through portal-gate save and bed save (`CharacterProfileSaveData`).
- [ ] Map hibernated for 3 in-game days → on wake, NPC starts at `max(0, old − 12 × decayPerPhase)`.
- [ ] Host↔Client: client player's hunger decays locally, eat works, server sees no NPC hunger leakage.
- [ ] Client↔Client: each client sees only its own bar.
- [ ] Host↔NPC: NPC eat works under host authority; storage slot removal syncs to clients.
- [ ] No exception spam if `TimeManager.Instance == null` at character spawn (`try/catch` per rule #31).
- [ ] HUD bar updates without per-frame poll (event-driven only).
- [ ] HUD bar uses `Time.unscaledDeltaTime` for animation.
- [ ] `OnStarvingChanged(true)` fires exactly once when CurrentValue first hits 0; `OnStarvingChanged(false)` fires exactly once when eating brings it above 0.

## Open questions (deferred to follow-up PRs)

1. Should remote-player hunger be visible to teammates? (Requires `NetworkVariable<float>`.)
2. Should NPCs show a hunger bar above their head when starving? (UI scope.)
3. Should the `StarvingStatusEffect` (max-stamina cap) ship in the same PR or as a follow-up? **Recommendation: follow-up.**
4. NPC food acquisition fallback: if no accessible storage has food, should the NPC walk to a market stall / shop?
5. Hotkey "eat first food in inventory without holding it" — UX call.

## Implementation order (high-level)

1. `FoodSO` + `FoodInstance` + `ConsumableInstance.ApplyEffect` virtual.
2. `NeedHunger` with phase-tick subscription + events + save piggyback.
3. Register `NeedHunger` in `CharacterNeeds.Start()` + `GetNeed<T>()` accessor.
4. `Character.UseConsumable` implementation.
5. `PlayerController.HandleConsumeCarriedItem` + `KeyCode.E` wire.
6. `UI_HungerBar` script.
7. MCP Unity step: open `UI_PlayerInfo.prefab`, add HungerBar child, wire SerializeField, save.
8. `GoapAction_GoToFood` + `GoapAction_Eat` + `NeedHunger.GetGoapActions` integration.
9. `MacroSimulator` catch-up step.
10. SKILL.md + wiki page updates.
11. Validation pass over the checklist above.

## Sources

- `Assets/Scripts/Character/CharacterNeeds/CharacterNeed.cs`
- `Assets/Scripts/Character/CharacterNeeds/CharacterNeeds.cs`
- `Assets/Scripts/Character/CharacterNeeds/NeedSocial.cs` (canonical decay reference)
- `Assets/Resources/Data/Item/ConsumableSO.cs`
- `Assets/Scripts/Item/ConsumableInstance.cs`
- `Assets/Scripts/Character/CharacterActions/CharacterUseConsumableAction.cs`
- `Assets/Scripts/DayNightCycle/TimeManager.cs`
- `Assets/Scripts/Character/CharacterControllers/PlayerController.cs`
- `Assets/Scripts/UI/UI_PlayerInfo.cs` + `UI_HealthBar.cs`
- `Assets/Scripts/Character/CharacterStats/Primary Stats/CharacterPrimaryStats.cs` (for understanding why we don't reuse `UI_HealthBar`)
- `Assets/Scripts/World/Buildings/CommercialBuilding.cs` (`GetItemsInStorageFurniture`)
- Project rules: CLAUDE.md (#9/#10/#11/#19/#22/#26/#28/#29/#29b/#30/#31/#33)
