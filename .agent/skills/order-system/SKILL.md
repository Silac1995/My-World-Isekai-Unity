---
name: order-system
description: System of directives a Character (or anonymous source) issues to a Character — server-authoritative, multiplayer-correct, integrates with Quest, Relation, Combat, Save. NPC GOAP integration deferred (see Open Questions).
---

# Order System

Generic Order primitive: one entity (a Character, optionally anonymous) issues a directive to another Character, who decides whether to obey based on authority + relationship + personality. Orders carry a timeout, designer-composable consequences (on disobey), and rewards (on compliance). Two abstract subclasses (`OrderQuest` for objective-tracked orders, `OrderImmediate` for behavior-polled orders) and two v1 concretes (`Order_Kill`, `Order_Leave`).

## When to use this skill
- Adding a new concrete order (subclass `OrderQuest` or `OrderImmediate`).
- Adding a new `IOrderConsequence` / `IOrderReward` strategy SO.
- Adding a new `AuthorityContextSO` (e.g., when the future Faction/Family system lands).
- Debugging order issuance, evaluation, timeout, consequence/reward firing, or save-load.
- Touching the `CharacterOrders` subsystem, RPC layer, or persistence pipeline.

## Architecture (four units)

### Unit 1 — Order runtime tree (server-only)
- `Order` (abstract base) — identity, parties, authority/priority, lifetime, state, consequences/rewards, lifecycle hooks. Pure C# (no Unity deps on the base).
- `OrderQuest : Order, IQuest` — the `IQuest` bridge. On accept, registers self with `CharacterQuestLog.TryClaim`. On resolve, fires `OnStateChanged` so the log auto-abandons.
- `OrderImmediate : Order` — no quest log entry. Server polls `IsComplied()` every 0.5s.
- v1 concretes: `Order_Kill : OrderQuest`, `Order_Leave : OrderImmediate`.

### Unit 2 — `IOrderIssuer` + `AuthorityContext` resolution
- `IOrderIssuer` — minimal interface (`AsCharacter`, `DisplayName`, `IssuerNetId`). v1 implementer: `Character`. Nullable issuer = anonymous order.
- `AuthorityContextSO` — pure data SO (`ContextName`, `BasePriority` 0–100, `BypassProximity` flag). 7 v1 assets in `Resources/Data/AuthorityContexts/`. Lives in `MWI.Orders.Pure` asmdef so the `AuthorityResolverTests` edit-mode suite can reach it.
- `AuthorityResolver` — server-only stateless static helper. Pure function `(IOrderIssuer, Character) → AuthorityContextSO`. Resolves Employer (via `CharacterJob.Workplace.Owner`), PartyLeader (via `CharacterParty.PartyData.IsLeader(charId)`), Friend (via `CharacterRelation.IsFriend`), or Stranger fallback.

### Unit 3 — `CharacterOrders` subsystem
- New child component on the Character GameObject. Inherits `CharacterSystem`, implements `ICharacterSaveData<OrdersSaveData>` with `LoadPriority = 60`.
- Server-authoritative. Holds two `NetworkList`s: `_activeOrdersSync` and `_pendingOrdersSync`.
- Issuer-side: `IssueOrder(Order)` (server) + `IssueOrderServerRpc(...)` (player). Stores ledger of orders this character has issued.
- Receiver-side: `ReceiveOrder(Order)` → evaluation coroutine → accept/refuse → state machine ticking → consequence/reward firing.
- Player UI hook: `ShowOrderPromptRpc` (Owner RPC) → `OnOrderPromptShown` event → `UI_OrderImmediatePopup`.

### Unit 4 — Consequence/Reward SO catalog
- `IOrderConsequence` (Disobey) and `IOrderReward` (Comply). Each impl is a `ScriptableObject` with a stable filename.
- v1 catalog (3 + 3 types, 5 tuned assets):
  - `Consequence_RelationDrop` (Light=10 / Heavy=30 assets)
  - `Consequence_IssuerAttacks` (single asset; uses `CharacterCombat.SetPlannedTarget`)
  - `Consequence_StatusEffect` (no v1 asset — needs a `CharacterStatusEffect` reference)
  - `Reward_RelationGain` (Light=10 / Heavy=30 assets)
  - `Reward_GiveItem` (no v1 asset — needs an `ItemInstance` template)
  - `Reward_StatusEffect` (no v1 asset)

## Public API

`CharacterOrders`:
- `IssueOrder(Order order) → ulong` — server-side helper.
- `IssueOrderServerRpc(receiverNetId, orderTypeName, urgency, payload, consequenceSoNamesPacked, rewardSoNamesPacked, timeoutSeconds)` — player-side RPC. SO names are pipe-delimited `FixedString512Bytes` because NGO rejects `string[]` in RPC signatures.
- `CancelIssuedOrder(ulong orderId) → bool` — issuer-driven cancel.
- `GetTopActiveOrder() → Order` — for future GOAP integration.
- `ResolvePlayerOrderServerRpc(orderId, accept)` — owning client → server response.
- `ShowOrderPromptRpc(PendingOrderSyncData)` — server → owning client UI.
- Events: `OnOrderReceived`, `OnOrderAccepted`, `OnOrderResolved`, `OnOrderPromptShown`.
- Inspector: `_responseDelay` (NPC think time, default 1.0s), `_compliancePollInterval` (default 0.5s).

`CharacterAction_IssueOrder` — rule #22 wrapper. Player HUD enqueues it; NPC GOAP would enqueue the same.

`OrderFactory.Create(typeName) → Order` — dispatches by `OrderTypeName`. Self-register subclasses in the static constructor.

## Lifecycle (state machine)

```
IssueOrderServerRpc
        │
        ▼
   Pending  ──refuse / timeout──► Disobeyed → fire Consequences → remove
        │
   accept
        │
        ▼
   Accepted (transient) → Active
        │
        ├── IsComplied() == true ──► Complied → fire Rewards → remove
        ├── ElapsedSeconds ≥ TimeoutSeconds ──► Disobeyed → fire Consequences → remove
        └── CancelIssuedOrder() ──► Cancelled (no consequences, no rewards)
```

## Adding a new concrete order
1. Subclass `OrderQuest` (objective-tracked) or `OrderImmediate` (compliance-polled).
2. Implement: `CanIssueAgainst`, `IsComplied()` (or `IsCompleted()` for OrderQuest), `GetGoapPrecondition()`, `SerializeOrderPayload()`, `DeserializeOrderPayload(byte[])`.
3. For `OrderQuest`: also `DisplayTitle`, `Description`, `Target` (an `IQuestTarget` wrapper). The base wraps `DisplayTitle` with `[P:NN]` priority decoration for the quest log.
4. Register in `OrderFactory.cs` static constructor: `Register<MyOrder>("MyOrder")`.
5. Payload must fit within 62 bytes (`OrderSyncData.OrderPayload` is `FixedBytes62` because `NetworkList<T>` requires unmanaged `T`). Larger payloads: use a separate ClientRpc.

## Adding a new consequence or reward
1. Create a `ScriptableObject` implementing `IOrderConsequence` or `IOrderReward`.
2. Add `[CreateAssetMenu]`. Create one or more tuned asset variants under `Resources/Data/OrderConsequences/` or `…/OrderRewards/`.
3. Document the null-issuer behavior in your strategy (no-op for issuer-dependent effects like `RelationDrop` / `IssuerAttacks`).

## NPC evaluation formula

```
acceptScore = 0.5
            + (priority - 50) / 100
            + (IsFriend ? +0.2 : IsEnemy ? -0.4 : 0)
            + (loyalty - 0.5) * 0.3       // CharacterTraits.GetLoyalty()
            - (aggressivity - 0.5) * 0.2  // CharacterTraits.GetAggressivity()
            + (compatibility > 0 ? +0.1 : compatibility < 0 ? -0.1 : 0)
accepted = Random.value < clamp01(acceptScore)
```

Server-side, after `_responseDelay` seconds (default 1.0s).

## Multiplayer

All evaluation, ticking, consequence/reward firing is server-side. Clients see only the `OrderSyncData` snapshots. Player UI prompts via `Rpc(SendTo.Owner)`. Player responses via `ResolvePlayerOrderServerRpc` with sender authority verified (`SenderClientId == OwnerClientId`).

Late joiners get all `_activeOrdersSync` and `_pendingOrdersSync` automatically through NetworkList initial sync. For `OrderQuest`s, `CharacterQuestLog`'s existing dormant-snapshot wake-on-map-change handles re-resolution (BUT — see Open Questions about ResolveQuest).

Validated multiplayer scenarios: Host↔Client, Client↔Client, Host/Client↔NPC for both issue and receive sides. See spec §6 for the explicit table.

## Save / Load

Three persistence paths, no overlap:

- **Issuer-side ledger** → `OrdersSaveData.issuedOrders`. On reload, server walks each entry; if the receiver is in the world (`Character.FindByUUID(receiverCharacterIdString)`), revives the live Order via `OrderFactory.Create` + `IssueOrder`, restoring consequences/rewards from SO names. If the receiver isn't loaded, marks dormant and resolves on `Character.OnCharacterSpawned` (mirrors `CharacterRelation` pattern at lines 95–135).
- **Receiver-side `OrderQuest`s** → persisted by `CharacterQuestLog` (they're `IQuest`s). Snapshot survives via the existing quest log machinery.
- **Receiver-side `OrderImmediate`s** → intentionally transient. A "leave this area" order from yesterday is meaningless.

`LoadPriority` ordering:
- `CharacterRelation` = 50
- `CharacterQuestLog` = 55 (must load before CharacterOrders)
- `CharacterOrders` = 60

## Known gotchas

- **`MWI.Time` namespace collision** — code in `MWI.Orders` resolves bare `Time.deltaTime` as `MWI.Time.deltaTime`. Always qualify as `UnityEngine.Time.deltaTime` / `unscaledTime`.
- **`string[]` not NGO-RPC-serializable** — pack as `FixedString512Bytes` with pipe-delimited values.
- **`OrderSyncData.OrderPayload` is `FixedBytes62`** — `NetworkList<T>` requires unmanaged `T`. 62-byte cap. Order_Kill = 8 bytes, Order_Leave = 24 bytes — both fit comfortably.
- **`FixedBytes62` can't use `INetworkSerializeByMemcpy`** — serialize byte-by-byte via the named-field pattern (`offset0000.byte0000` etc.) matching what `OrderSyncData.NetworkSerialize` already does.
- **OrderQuest.State clashes with Order.State** — explicit `IQuest.State` interface implementation avoids the collision.
- **Anonymous orders (null issuer)** — every consequence/reward must early-return on null issuer for issuer-dependent effects. Order core never branches on null.

## Open questions / Deferred work

- **NPC AI / GOAP integration NOT done.** `Order.GetGoapPrecondition()` returns `Dictionary<string, bool>` but `CharacterGoapController.Replan()` does not yet inject order-derived `GoapGoal`s alongside need-derived ones. Also no `GoapAction` exists whose `Effects` satisfy the dynamic per-target keys (`TargetIsDead_NN`, `OutsideZone_NN`). Hooking it requires:
  1. A new `CharacterOrders.GetGoapGoalForTopActiveOrder()` accessor returning a `GoapGoal` from the active order.
  2. A patch to `CharacterGoapController.Replan` to add it to `potentialGoals`.
  3. New GoapActions: `GoapAction_AttackOrderTarget` (Effect: `TargetIsDead_NN = true`) and `GoapAction_LeaveOrderZone` (Effect: `OutsideZone_NN = true`). Both query `_character.CharacterOrders.GetTopActiveOrder()` at execute time to find the actual target/zone.
- **Quest log entry priority badge** — chose to bake `[P:NN]` into the title via `OrderQuest.Title`. The plan's original idea of a colored Image badge would require creating a `UI_QuestLogEntry` MonoBehaviour and modifying the prefab — neither exists today.
- **`ResolveQuest` doesn't know about OrderQuests** — `CharacterQuestLog.ResolveQuest` only walks `BuildingManager.Instance.allBuildings`. If a player loads a save with an active OrderQuest snapshot but the issuer isn't in the world, dormant-promotion via `HandleMapChanged` will fail to resolve the live IQuest. Acceptable for v1 (orders are short-lived), but a future fix should extend ResolveQuest to also walk `Character.CharacterOrders.IssuedOrders`.
- **Status/Item sample assets missing** — `Consequence_StatusEffect_Wanted.asset` and the `Reward_GiveItem` / `Reward_StatusEffect` samples need real `CharacterStatusEffect` / `ItemInstance` references — hand-create in Editor when the assets exist.

## Tips & troubleshooting

- **Order rejected silently:** check `[Order] … rejected: …` log lines. Common causes: `CanIssueAgainst` false, proximity check failed, receiver missing CharacterOrders.
- **NPC always refuses:** check the evaluation log line — relation modifier (-0.4 for enemies) + low priority can drop score below 0.5.
- **Consequence didn't fire:** consequence SOs no-op when `issuer == null`. Check the log for which strategies ran. Per-strategy try/catch means one failure doesn't kill the rest.
- **Order persists after intended completion:** `IsComplied()` is polled every 0.5s — verify the predicate becomes true at the right moment.
- **Player popup never appears:** verify `PlayerUI._orderImmediatePopup` is wired to a UI_OrderImmediatePopup prefab and `BindToLocalPlayer(playerCharacter.CharacterOrders)` is called when the local player Character spawns.

## Sources

- Spec: [docs/superpowers/specs/2026-04-26-character-order-system-design.md](../../docs/superpowers/specs/2026-04-26-character-order-system-design.md)
- Plan: [docs/superpowers/plans/2026-04-26-character-order-system.md](../../docs/superpowers/plans/2026-04-26-character-order-system.md)
- Implementation: [Assets/Scripts/Character/CharacterOrders/](../../Assets/Scripts/Character/CharacterOrders/)
- Related skills: `quest-system`, `social_system`, `goap`, `multiplayer`, `save-load-system`.

