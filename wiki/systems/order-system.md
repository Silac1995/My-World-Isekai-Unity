---
type: system
title: "Order System"
tags: [orders, ai, social, quest, multiplayer]
created: 2026-04-26
updated: 2026-04-26
sources:
  - Assets/Scripts/Character/CharacterOrders/
  - .agent/skills/order-system/SKILL.md
  - docs/superpowers/specs/2026-04-26-character-order-system-design.md
related:
  - "[[quest-system]]"
  - "[[social-system]]"
  - "[[character-relations]]"
  - "[[ai-goap]]"
  - "[[character-action]]"
  - "[[save-load-system]]"
status: active
confidence: high
primary_agent: order-system-specialist
secondary_agents: [character-social-architect, npc-ai-specialist, quest-system-specialist]
owner_code_path: "Assets/Scripts/Character/CharacterOrders/"
depends_on:
  - "[[quest-system]]"
  - "[[social-system]]"
  - "[[character-relations]]"
  - "[[character-action]]"
  - "[[save-load-system]]"
depended_on_by: []
---

# Order System

## Summary

A generic primitive for one entity (a Character, optionally anonymous) issuing a directive to another Character, who decides whether to obey based on authority, relationship, and personality. Orders carry a timeout and designer-composable consequences (on disobey) or rewards (on compliance). Orders integrate with the existing Quest system as a new producer (`OrderQuest`), reuse the existing relation/combat/save infrastructure, and are designed to plug into NPC AI as a utility-driven goal (GOAP integration deferred).

## Purpose

Before this system, the only way one character could "tell another to do something" was through specialized paths — a captain assigning a guard a `BuildingTask`, a friend issuing a `CharacterInvitation`, an NPC scripting custom dialogue. Each path was hand-rolled and didn't compose. The Order system unifies the directive primitive: any character can issue any of a designer-extensible set of orders to any other, with consistent rules around obedience, timeout, consequences, and persistence.

Without it, gameplay verbs like "the lord orders you to kill the bandit", "the guard tells you to leave the area", or "your boss tells you to fetch berries" would each need bespoke code, different consequence wiring, separate save formats.

## Responsibilities

- **Order primitive** — abstract `Order` base + two abstract subclasses (`OrderQuest` for objective-tracked, `OrderImmediate` for behavior-polled) + concrete v1 orders (`Order_Kill`, `Order_Leave`).
- **Authority resolution** — derive an `AuthorityContextSO` from existing systems (`CharacterJob`, `CharacterParty`, `CharacterRelation`) per (issuer, receiver) pair. Pure function; no new persisted state.
- **Obedience evaluation** — NPC server-side acceptance formula combining priority, relationship, loyalty, aggressivity, and personality compatibility. Player evaluation via Owner-RPC popup with timeout.
- **State machine** — Pending → Accepted → Active → (Complied | Disobeyed | Cancelled). Server polls `IsComplied()` every 0.5s. Server fires consequences/rewards on resolution.
- **Designer-composable consequences/rewards** — `IOrderConsequence` and `IOrderReward` `ScriptableObject` strategies, network-replicable as filename strings.
- **Persistence** — issuer-side ledger via `OrdersSaveData`; receiver-side OrderQuest via `CharacterQuestLog`; receiver-side OrderImmediate is intentionally transient. Dormant pattern for absent receivers.
- **Multiplayer correctness** — server-authoritative; clients see snapshots; player UI via `Rpc(SendTo.Owner)`.

**Non-responsibilities** (common misconceptions):
- Not responsible for NPC AI execution of orders — that's the deferred GOAP integration. The Order system makes the goal *available*; the planner side hasn't been hooked yet.
- Not responsible for social interactions or invitations — those live in [[social-system]] and use the relationship-only acceptance path.
- Not responsible for storing the issuer's authority — authority is *derived* from existing systems on the fly via `AuthorityResolver`.
- Not responsible for issuing orders from buildings — `IOrderIssuer` allows future Faction/Building/divine implementations, but v1 only ships the `Character` impl. CommercialBuilding-driven tasks continue to use the existing `BuildingTask` quest pipeline.

## Key classes / files

| File | Role |
|------|------|
| [Order.cs](../../Assets/Scripts/Character/CharacterOrders/Order.cs) | Abstract base — identity, parties, authority/priority, lifetime, state, lifecycle hooks |
| [OrderQuest.cs](../../Assets/Scripts/Character/CharacterOrders/OrderQuest.cs) | Abstract `IQuest` bridge — single-receiver, `[P:NN]` title decoration |
| [OrderImmediate.cs](../../Assets/Scripts/Character/CharacterOrders/OrderImmediate.cs) | Abstract — no quest log, polled compliance |
| [Concrete/Order_Kill.cs](../../Assets/Scripts/Character/CharacterOrders/Concrete/Order_Kill.cs) | "Kill target X" — `OrderQuest` with `CharacterTarget` |
| [Concrete/Order_Leave.cs](../../Assets/Scripts/Character/CharacterOrders/Concrete/Order_Leave.cs) | "Leave this area" — `OrderImmediate` with sphere zone |
| [CharacterOrders.cs](../../Assets/Scripts/Character/CharacterOrders/CharacterOrders.cs) | Subsystem MonoBehaviour — NetworkLists, RPCs, state machine, save/load |
| [IOrderIssuer.cs](../../Assets/Scripts/Character/CharacterOrders/IOrderIssuer.cs) | Interface — `Character` is the v1 impl; nullable allowed |
| [Pure/AuthorityContextSO.cs](../../Assets/Scripts/Character/CharacterOrders/Pure/AuthorityContextSO.cs) | Data-only SO in the Pure asmdef |
| [AuthorityResolver.cs](../../Assets/Scripts/Character/CharacterOrders/AuthorityResolver.cs) | Stateless static helper |
| [OrderFactory.cs](../../Assets/Scripts/Character/CharacterOrders/OrderFactory.cs) | Type-name → instance dispatch (used by RPC + save reload) |
| [OrderSyncData.cs](../../Assets/Scripts/Character/CharacterOrders/OrderSyncData.cs) | NetworkList element — fixed schema, `FixedBytes62` payload |
| [PendingOrderSyncData.cs](../../Assets/Scripts/Character/CharacterOrders/PendingOrderSyncData.cs) | Slim variant for in-evaluation orders |
| [OrdersSaveData.cs](../../Assets/Scripts/Character/CharacterOrders/OrdersSaveData.cs) | Issuer-side ledger persistence |
| [Consequences/IOrderConsequence.cs](../../Assets/Scripts/Character/CharacterOrders/Consequences/IOrderConsequence.cs) | Disobey strategy interface |
| [Rewards/IOrderReward.cs](../../Assets/Scripts/Character/CharacterOrders/Rewards/IOrderReward.cs) | Comply strategy interface |
| [CharacterAction_IssueOrder.cs](../../Assets/Scripts/Character/CharacterActions/CharacterAction_IssueOrder.cs) | Rule #22 wrapper for player + NPC issuance |
| [UI_OrderImmediatePopup.cs](../../Assets/Scripts/UI/Order/UI_OrderImmediatePopup.cs) | Player-side popup with countdown |

## Public API / entry points

- `CharacterOrders.IssueOrder(Order)` — server-side helper.
- `CharacterOrders.IssueOrderServerRpc(...)` — player RPC (SO names packed pipe-delimited).
- `CharacterOrders.CancelIssuedOrder(ulong orderId)` — issuer-driven cancel.
- `CharacterOrders.GetTopActiveOrder()` — for future GOAP integration.
- `CharacterOrders.OnOrderReceived/OnOrderAccepted/OnOrderResolved` — events.
- `CharacterOrders.OnOrderPromptShown` — client-side event the popup UI listens to.
- `OrderFactory.Register<T>(string)` — register a new concrete order.

## Data flow

```
Issuer side                                  Receiver side
─────────────────────────────                ──────────────────────────────────
PlayerController                             Server-side coroutine
   │                                            │
   ▼                                            ▼
CharacterAction_IssueOrder                   EvaluateOrderRoutine
   │                                            │
   ▼ OnApplyEffect                              ├─ NPC: WaitForSeconds(_responseDelay) → score formula
IssueOrderServerRpc                             └─ Player: ShowOrderPromptRpc → wait for response
   │ (server)                                      │
   ▼                                               ▼
CharacterOrders.IssueOrder                   ApplyEvaluationResult
   │                                            │
   ▼                                            ├─ Refused → fire Consequences → remove from sync
receiver.CharacterOrders.ReceiveOrder           └─ Accepted → OnAccepted (OrderQuest.TryClaim) → Active
   │                                            │
   ▼                                            ▼ (every 0.5s)
_pendingOrdersSync.Add(snap)                 IsComplied() / timeout check
   │                                            │
   └────────────────────────────────────────────┴─► ResolveActive → fire Rewards/Consequences → remove
```

Server authority everywhere. Clients receive snapshot updates via NetworkList sync. Player UI via Owner-targeted RPCs. Late joiners get all in-flight orders automatically through NetworkList initial sync.

## Dependencies

### Upstream (this system needs)
- [[quest-system]] — `IQuest`, `IQuestTarget`, `CharacterQuestLog`. OrderQuest plugs in as a producer via `TryClaim`.
- [[social-system]] / [[character-relations]] — `CharacterRelation.IsFriend/IsEnemy/UpdateRelation` for evaluation + RelationDrop/RelationGain consequences.
- [[character-action]] — issuance routes through `CharacterAction_IssueOrder`.
- [[save-load-system]] — `ICharacterSaveData<T>` contract, `Character.FindByUUID`, `Character.OnCharacterSpawned` for dormant pattern.

### Downstream (systems that need this)
- (Future) NPC GOAP — will pull `GetTopActiveOrder()` and inject as a `GoapGoal`.

## State & persistence

**Runtime state:**
- Server: live `Order` instances in `_activeOrdersServer` and `_issuedOrdersServer`, indexed by OrderId in `_ordersByIdServer`.
- Server + Clients: `NetworkList<OrderSyncData>` (active) and `NetworkList<PendingOrderSyncData>` (pending). Snapshot-only on clients.

**Persisted state:**
- Issuer-side ledger → `OrdersSaveData.issuedOrders` (`SaveKey = "CharacterOrders"`, `LoadPriority = 60`). Re-armed on reload via `OrderFactory` + `IssueOrder`. Dormant if receiver not in world; resolved on `Character.OnCharacterSpawned`.
- Receiver-side OrderQuest → persisted by [[quest-system]] (each OrderQuest *is* an `IQuest`).
- Receiver-side OrderImmediate → intentionally transient.

Save format: see [OrdersSaveData.cs](../../Assets/Scripts/Character/CharacterOrders/OrdersSaveData.cs).

## Known gotchas / edge cases

- **`MWI.Time` namespace collides with `UnityEngine.Time`.** Always qualify time calls in code under `MWI.Orders`. Caught at compile time during Task 20.
- **`string[]` is not NGO-RPC-serializable.** Pack as `FixedString512Bytes` with pipe-delimited values. Caught at compile time during Task 21.
- **`NetworkList<T>` requires unmanaged `T`.** OrderSyncData payload uses `FixedBytes62` instead of `byte[]`. 62-byte cap on per-order payload. Caught during Task 9 implementation.
- **`OrderQuest.State` clashes with `Order.State`.** Different types (`QuestState` vs `OrderState`). Resolved via explicit interface implementation `MWI.Quests.QuestState IQuest.State`.
- **`CharacterQuestLog.ResolveQuest` doesn't know about OrderQuests.** It only walks `BuildingManager.Instance.allBuildings`. Dormant-promotion of a saved OrderQuest snapshot will silently fail to find the live IQuest if the issuer's character isn't loaded yet. Acceptable for short-lived v1 orders.
- **Sample StatusEffect / GiveItem assets are missing.** They need real `CharacterStatusEffect` / `ItemInstance` references — hand-create in Editor when content exists.

## Open questions / TODO

- [ ] **NPC GOAP integration not done.** Hook `CharacterGoapController.Replan()` to inject order-derived `GoapGoal`s, and add new `GoapAction`s with Effects matching `TargetIsDead_NN` / `OutsideZone_NN`. Players can manually obey orders today; NPCs accept/refuse but don't autonomously execute.
- [ ] **Updates to neighbor SKILL.md / agent / wiki files.** `quest-system`, `goap`, `social_system`, `save-load-system`, `multiplayer` skill files don't yet reference the Order integration. Same for the corresponding wiki pages and `npc-ai-specialist` / `quest-system-specialist` / `character-social-architect` / `network-validator` / `save-persistence-specialist` agents.
- [ ] **`UI_OrderImmediatePopup` prefab binding** — the prefab exists and PlayerUI has the binding hook, but field references inside the prefab (TMP_Text, Buttons, CanvasGroup) need to be hand-wired in the Editor before the popup is functional.
- [ ] **Manual play-mode validation pending** — F9 (Order_Leave) and F10 (Order_Kill) tester hotkeys exist via `DevOrderLeaveTester`. Manual multiplayer scenarios from spec §6 not yet exercised.
- [ ] **`DevOrderLeaveTester` should be removed** once GOAP integration lands and proper tests cover the pipeline.

## Change log

- 2026-04-26 — initial page created with Order system v1 (Order_Kill + Order_Leave; GOAP deferred) — claude

## Sources

- [Assets/Scripts/Character/CharacterOrders/](../../Assets/Scripts/Character/CharacterOrders/)
- [.agent/skills/order-system/SKILL.md](../../.agent/skills/order-system/SKILL.md) — procedural how-to
- [docs/superpowers/specs/2026-04-26-character-order-system-design.md](../../docs/superpowers/specs/2026-04-26-character-order-system-design.md) — design spec
- [docs/superpowers/plans/2026-04-26-character-order-system.md](../../docs/superpowers/plans/2026-04-26-character-order-system.md) — implementation plan

