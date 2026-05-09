# Character Order System — Design

**Status:** Design accepted, awaiting implementation plan
**Date:** 2026-04-26
**Owner:** Kevin (Silac)

---

## 1. Purpose

Introduce a generic **Order** primitive: a mechanism by which one entity (a Character, or eventually a Faction/Building/System) can issue a directive to another Character, who then *decides whether to obey* based on authority, relationship, personality, and current situation. Orders carry a timeout and a list of consequences (fired on disobey) and rewards (fired on compliance). Orders integrate with the existing Quest system as a new producer, reuse the existing relation/combat/save infrastructure, and plug into NPC AI as a utility-driven GOAP goal.

The design favors **long-term correctness and performance** over quick wins:

- One primitive that can absorb future use cases (faction edicts, divine commands, environmental rules) without redesign.
- Server-authoritative for full multiplayer correctness across Host↔Client, Client↔Client, Host/Client↔NPC.
- Designer-composable consequences/rewards via ScriptableObject strategies — no hardcoded `if/else` per order.
- NPC AI integration via existing GOAP utility, no special-case interrupt code.

## 2. Scope

### In scope (v1)
- `Order` abstract base + state machine.
- Two abstract subclasses: `OrderQuest` (implements `IQuest`) and `OrderImmediate`.
- Two concrete orders: `Order_Kill : OrderQuest`, `Order_Leave : OrderImmediate`.
- `IOrderIssuer` interface; `Character` implementation; nullable issuer for anonymous/system orders.
- `AuthorityContextSO` ScriptableObject + 7 baseline assets (Stranger, Friend, Parent, PartyLeader, Employer, Captain, Lord).
- `AuthorityResolver` static helper deriving context from existing systems (`CharacterJob`, `CharacterParty`, `CharacterRelation`, future Family/Faction).
- `CharacterOrders` subsystem (new child component on the Character GameObject).
- `IOrderConsequence` / `IOrderReward` interfaces + 3 v1 consequence SOs + 3 v1 reward SOs.
- `Goal_FollowOrder` GOAP goal.
- `CharacterAction_IssueOrder` for player and NPC issuance (rule #22 compliance).
- Player UI: blocking popup for `OrderImmediate` (mirrors `CharacterInvitation`'s receive UI), quest log entry with priority badge for `OrderQuest`.
- Save/load: issuer-side ledger persists outstanding orders; receiver-side `OrderQuest`s persist via existing `CharacterQuestLog`; receiver-side `OrderImmediate`s are intentionally transient.
- Multiplayer correctness across all Host↔Client, Client↔Client, Host/Client↔NPC scenarios.
- Documentation: new `order-system` SKILL.md, new `wiki/systems/order-system.md`, updates to existing skill/wiki/agent files.

### Out of scope (deferred)
- Building-as-issuer (will use existing CommercialBuilding quest pipeline; not a new Order issuer in v1).
- Faction-as-issuer.
- Long-range orders (the `BypassProximity` flag exists on `AuthorityContextSO` but is always `false` in v1; future opt-in).
- Order chaining ("after you finish A, do B").
- Order pre-emption based on authority differences (in v1, all accepted orders coexist; priority only drives AI selection, not interruption of in-flight orders).

## 3. Foundational Design Decisions

| # | Decision | Choice |
|---|---|---|
| 1 | Generic primitive vs. specific verb? | Hybrid — generic primitive + 2 narrow concrete orders to validate. |
| 2 | Authority model? | Relationship + personality + AuthorityContext (derived from existing systems, not a new persisted field). |
| 3 | Order ↔ Quest split? | Hybrid — `OrderQuest` implements `IQuest`; `OrderImmediate` does not. |
| 4 | Issuer types? | `IOrderIssuer` interface + nullable issuer; v1 implementation only on `Character`. No buildings/factions in v1. |
| 5 | Receiver concurrency? | Multiple concurrent orders allowed; priority drives NPC AI selection. |
| 6 | Priority semantics? | Numeric 0–100, derived from `AuthorityContext.BasePriority + Urgency`, consumed as GOAP utility. |
| 7 | Consequences/rewards? | Pluggable `IOrderConsequence` / `IOrderReward` SO strategies, designer-composable per order instance. |
| 8 | Spatial constraint? | Proximity required by default (`InteractableObject.IsCharacterInInteractionZone`); future-proofed via `AuthorityContextSO.BypassProximity` flag. |
| 9 | Persistence? | Hybrid — `OrderQuest`s persist via `CharacterQuestLog`; `OrderImmediate`s do not persist; issuer-side ledger persists separately. |
| 10 | Player UI? | `OrderQuest` → quest log entry + toast; `OrderImmediate` → blocking popup with countdown (Invitation-style). |

## 4. Architecture

Four cooperating units, each with a single responsibility and a typed interface boundary.

### Unit 1 — Order runtime tree (server-only)
- `Order` — abstract base. Pure C# (no Unity dependency on the base). Holds identity, parties, authority/priority, lifetime, state, composition (consequences/rewards), and lifecycle hooks.
- `OrderQuest : Order, IQuest` — abstract; the `IQuest` bridge. On accept, registers self with `CharacterQuestLog`. On resolve, removes the quest entry.
- `OrderImmediate : Order` — abstract; no quest log entry; the subsystem polls `IsComplied()`.
- v1 concrete: `Order_Kill : OrderQuest`, `Order_Leave : OrderImmediate`.

### Unit 2 — `IOrderIssuer` + `AuthorityContext` resolution
- `IOrderIssuer` — minimal interface (`AsCharacter`, `DisplayName`, `IssuerNetId`).
- `AuthorityContextSO` — designer asset: `{ ContextName, BasePriority, BypassProximity }`.
- `AuthorityResolver` — static, server-only, stateless. Pure function `(IOrderIssuer issuer, Character receiver) → AuthorityContextSO`.

### Unit 3 — `CharacterOrders` subsystem
- New child component on the Character GameObject (per rule #9 and the Character hierarchy convention).
- Server-authoritative; clients are pure observers.
- Holds two `NetworkList`s: active and pending orders.
- Issuer-side: stores ledger of orders this character has issued.
- Receiver-side: routes incoming orders, runs evaluation, drives compliance polling, fires consequences/rewards.
- Implements `ICharacterSaveData<OrdersSaveData>` with `LoadPriority = 60`.

### Unit 4 — Consequence/Reward SO catalog
- `IOrderConsequence` and `IOrderReward` interfaces.
- Each implementation is a `ScriptableObject` with a stable filename (used as the network identifier).
- v1 catalog: 3 consequence *types* and 3 reward *types* (see §5), each with one or more tuned asset variants (see §10).

### Cross-cutting boundaries (rule #9)
- `CharacterOrders` only reaches sibling subsystems via the `Character` facade.
- `AuthorityResolver` is server-only and stateless.
- Live `Order` instances exist only on the server; clients see only `OrderSyncData` snapshots.
- The GOAP layer reads orders via public `CharacterOrders` accessors and never imports concrete `Order` types.

## 5. Data Model

### `Order` (abstract base, server-only)

```csharp
public abstract class Order
{
    // Identity
    public ulong  OrderId;
    public string OrderTypeName;            // Stable name for sync ("Order_Kill")

    // Parties
    public IOrderIssuer Issuer;             // Nullable
    public Character    Receiver;           // Always non-null

    // Authority + priority
    public AuthorityContextSO AuthorityContext;
    public OrderUrgency       Urgency;
    public int                Priority;     // BasePriority + UrgencyBonus, clamped 0..100

    // Lifetime
    public float       TimeoutSeconds;
    public float       ElapsedSeconds;
    public OrderState  State;

    // Composition
    public List<IOrderConsequence> Consequences;
    public List<IOrderReward>      Rewards;

    // Lifecycle hooks
    public abstract bool   CanIssueAgainst(Character receiver);
    public abstract void   OnAccepted();
    public abstract bool   IsComplied();
    public virtual  void   OnTick(float dt) { ElapsedSeconds += dt; }
    public abstract void   OnResolved(OrderState finalState);
    public abstract byte[] SerializeOrderPayload();
    public abstract void   DeserializeOrderPayload(byte[] data);

    // GOAP integration
    public abstract Dictionary<string, object> GetGoapPrecondition();
}

public enum OrderUrgency : byte { Routine = 0, Important = 15, Urgent = 25, Critical = 35 }
public enum OrderState   : byte { Pending, Accepted, Active, Complied, Disobeyed, Cancelled }
```

### `OrderQuest` (abstract, the `IQuest` bridge)

```csharp
public abstract class OrderQuest : Order, IQuest
{
    public ulong LinkedQuestId;

    public override void OnAccepted()
    {
        LinkedQuestId = Receiver.CharacterQuestLog.RegisterQuest(this);
    }

    public override void OnResolved(OrderState finalState)
    {
        Receiver.CharacterQuestLog.RemoveQuest(LinkedQuestId,
            finalState == OrderState.Complied ? QuestRemovalReason.Completed
                                              : QuestRemovalReason.Failed);
    }

    // IQuest members supplied by concrete subclass
    public abstract string         Title       { get; }
    public abstract string         Description { get; }
    public abstract IQuestTarget[] Targets     { get; }
    public abstract bool           IsCompleted();
    public override  bool          IsComplied() => IsCompleted();
}
```

### `OrderImmediate`

```csharp
public abstract class OrderImmediate : Order
{
    public override void OnAccepted() { /* no quest log */ }
    public override void OnResolved(OrderState finalState) { /* no log cleanup */ }
    // IsComplied() supplied by concrete subclass
}
```

### `IOrderIssuer`

```csharp
public interface IOrderIssuer
{
    Character AsCharacter { get; }    // null for non-character issuers
    string    DisplayName { get; }
    ulong     IssuerNetId { get; }    // 0 for anonymous
}
```

`Character` implements this directly. Future Faction/BuildingOwnerProxy implementations plug in without touching anything else.

### `AuthorityContextSO`

```csharp
[CreateAssetMenu(menuName = "MWI/Orders/Authority Context")]
public class AuthorityContextSO : ScriptableObject
{
    public string ContextName;
    [Range(0, 100)] public int BasePriority;
    public bool BypassProximity;     // v1: always false
}
```

v1 assets in `Assets/Resources/Data/AuthorityContexts/`:
- `Authority_Stranger` (BasePriority = 20)
- `Authority_Friend` (35)
- `Authority_Parent` (45)
- `Authority_PartyLeader` (50)
- `Authority_Employer` (55)
- `Authority_Captain` (70)
- `Authority_Lord` (85)

### `OrderSyncData` (NetworkList element)

Single fixed schema; type-specific data lives in `OrderPayload` to keep the NetworkList polymorphic-safe.

```csharp
public struct OrderSyncData : INetworkSerializable, IEquatable<OrderSyncData>
{
    public ulong              OrderId;
    public ulong              IssuerNetId;            // 0 = anonymous
    public ulong              ReceiverNetId;
    public FixedString64Bytes OrderTypeName;
    public FixedString32Bytes AuthorityContextName;
    public byte               Priority;
    public byte               Urgency;
    public byte               State;
    public float              TimeoutSeconds;
    public float              ElapsedSeconds;
    public bool               IsQuestBacked;
    public ulong              LinkedQuestId;
    public byte[]             OrderPayload;
}
```

`PendingOrderSyncData` is a slimmer struct for in-evaluation orders (no `LinkedQuestId`, no payload — just enough to drive the player popup countdown).

### `OrdersSaveData`

```csharp
[Serializable]
public class OrdersSaveData
{
    public List<IssuedOrderSaveEntry> issuedOrders = new();
    // Receiver-side state intentionally NOT serialized:
    //   - OrderQuests are persisted by CharacterQuestLog (they are IQuest)
    //   - OrderImmediates are transient by design
}

[Serializable]
public class IssuedOrderSaveEntry
{
    public ulong        receiverCharacterId;
    public string       orderTypeName;
    public string       authorityContextName;
    public byte         urgency;
    public float        timeoutRemaining;
    public byte[]       orderPayload;
    public bool         isQuestBacked;
    public ulong        linkedQuestId;
    public List<string> consequenceSoNames;
    public List<string> rewardSoNames;
}
```

### `IOrderConsequence` / `IOrderReward`

```csharp
public interface IOrderConsequence
{
    string SoName { get; }
    void Apply(Order order, Character receiver, IOrderIssuer issuer);
}

public interface IOrderReward
{
    string SoName { get; }
    void Apply(Order order, Character receiver, IOrderIssuer issuer);
}
```

v1 catalog (3 consequence *types* + 3 reward *types*, each shipped as one or more tuned `ScriptableObject` assets — see §10 for asset variants):

**Consequence types** in `Assets/Resources/Data/OrderConsequences/`:
- `Consequence_RelationDrop` — configurable `int amount`. No-ops if `issuer == null`.
- `Consequence_IssuerAttacks` — `issuer.AsCharacter?.CharacterCombat.SetTarget(receiver)`. No-ops if `issuer` null or dead.
- `Consequence_StatusEffect` — applies a configurable `StatusEffectSO`. Works with null issuer.

**Reward types** in `Assets/Resources/Data/OrderRewards/`:
- `Reward_RelationGain` — configurable `int amount`. No-ops if `issuer == null`.
- `Reward_GiveItem` — configurable `ItemSO` + `int count`. Server grants via inventory.
- `Reward_StatusEffect` — applies a configurable `StatusEffectSO`.

Per-strategy null-issuer handling. Order core never branches on `issuer == null`.

### `CharacterOrders` — public surface

```csharp
public class CharacterOrders : CharacterSystem, ICharacterSaveData<OrdersSaveData>
{
    // Issuer side
    public ulong IssueOrder(Order order);                           // Server-side helper
    [Rpc(SendTo.Server)] void IssueOrderServerRpc(...);             // Player-issued path
    public bool CancelIssuedOrder(ulong orderId);                   // Issuer-driven cancel
    public IReadOnlyList<Order> IssuedOrders { get; }               // Server-side ledger view

    // Receiver side
    public IReadOnlyList<Order> ActiveOrders { get; }               // Server only
    public Order GetTopActiveOrder();                               // Server only — for GOAP
    public IEnumerable<Order> GetActiveOrdersByPriority();          // Server only

    // Player UI (Owner-side)
    [Rpc(SendTo.Owner)] void ShowOrderPromptRpc(PendingOrderSyncData data);
    public void ResolvePlayerOrder(ulong orderId, bool accept);

    // Network sync
    private NetworkList<OrderSyncData>        _activeOrdersSync;
    private NetworkList<PendingOrderSyncData> _pendingOrdersSync;

    // Events
    public event Action<Order>             OnOrderReceived;
    public event Action<Order>             OnOrderAccepted;
    public event Action<Order, OrderState> OnOrderResolved;

    // ICharacterSaveData<OrdersSaveData>
    public string SaveKey      => "CharacterOrders";
    public int    LoadPriority => 60;   // After CharacterRelation (50) and CharacterQuestLog (55)
    public OrdersSaveData Serialize();
    public void Deserialize(OrdersSaveData data);
}
```

`Character` exposes it as `Character.CharacterOrders`, auto-assigned via `GetComponentInChildren<>` in `Character.Awake` (rule #9 hierarchy convention).

## 6. Lifecycle, Networking, and State Machine

### State machine

```
   IssueOrderServerRpc()
            │
            ▼
       ┌─Pending─┐  (in _pendingOrdersSync, evaluation coroutine running)
       │         │
   accept       refuse / timeout-during-evaluation
       │         │
       ▼         ▼
   Accepted   Disobeyed ──► OnResolved() ──► fire Consequences ──► remove from sync
       │
       ▼
    Active   (in _activeOrdersSync; OnTick() each server frame; IsComplied() polled at 0.5s)
       │
       ├── IsComplied() == true ──► Complied  ──► OnResolved() ──► fire Rewards
       ├── ElapsedSeconds >= TimeoutSeconds ──► Disobeyed ──► OnResolved() ──► fire Consequences
       └── CancelIssuedOrder() ──► Cancelled  ──► OnResolved() (no consequences, no rewards)
```

Each transition mutates `OrderSyncData.State` in the NetworkList, which clients observe via `OnListChanged`.

### Issuance flow — full sequence

**Step 1. Initiator decides to issue an order.**
- **Player initiator (Host or Client):** UI / `PlayerController` queues a `CharacterAction_IssueOrder` (per rule #22). The action's `OnApplyEffect` calls `Character.CharacterOrders.IssueOrderServerRpc(...)` carrying receiver NetId, OrderTypeName, urgency, payload bytes, optional consequence/reward override SO names.
- **NPC initiator (server-side):** GOAP action calls `CharacterOrders.IssueOrder(order)` directly (already on server).

**Step 2. Server validates** (in `IssueOrderServerRpc` / `IssueOrder`):
- Sender authority check: `rpcParams.Receive.SenderClientId == _character.OwnerClientId` (or call originated server-side for NPCs). Reject + Owner RPC error toast otherwise.
- Receiver lookup via `NetworkManager.SpawnedObjects.TryGetValue(receiverNetId, ...)`. Try/catch + structured log on failure (rule #31).
- Proximity check via `receiver.CharacterInteractable.IsCharacterInInteractionZone(issuer.AsCharacter)`, unless `AuthorityContext.BypassProximity` (always false in v1).
- Concrete order's `CanIssueAgainst(receiver)` precondition.
- `AuthorityResolver.Resolve(issuer, receiver)` returns the highest-applying `AuthorityContextSO`.
- Compute final `Priority = clamp(context.BasePriority + (int)urgency, 0, 100)`.

**Step 3. Server creates the live `Order` instance** with a freshly-assigned OrderId, sets `State = Pending`, appends a `PendingOrderSyncData` entry to receiver's `_pendingOrdersSync`. Receiver's `CharacterOrders` starts the evaluation coroutine.

**Step 4. Evaluation branches by receiver type.**
- **NPC receiver:** server coroutine waits `_responseDelay` seconds (a serialized field on `CharacterOrders`, default 1.0s, mirroring `CharacterInvitation._responseDelay`), then computes acceptance:
  ```
  acceptScore = 0.5
              + (priority - 50) / 100
              + relationModifier(issuer, receiver)    // friend +0.2, enemy -0.4, neutral 0
              + (loyalty - 0.5) * 0.3                 // CharacterTraits.GetLoyalty()
              - (aggressivity - 0.5) * 0.2            // CharacterTraits.GetAggressivity()
  accepted = Random.value < clamp01(acceptScore)
  ```
  Where `relationModifier` is derived from `Character.CharacterRelation.IsFriend(issuer)` / `IsEnemy(issuer)`. Personality compatibility filter via `CharacterProfile.GetCompatibilityWith(issuerProfile)` is applied to `acceptScore` after the formula, matching existing relation logic.
- **Player receiver (Host or Client):** server fires `ShowOrderPromptRpc` to the receiver via `Rpc(SendTo.Owner)`:
  - For `OrderImmediate`: triggers a blocking popup (Invitation-style). Player has `TimeoutSeconds` to choose. Auto-refuse on timeout.
  - For `OrderQuest`: shows a non-blocking toast + a "Pending order from X" entry visible in the quest log with **Accept / Refuse** buttons. Auto-refuse on timeout.
  - Player choice → `ResolvePlayerOrderServerRpc(orderId, accept)`. Server validates ownership and applies the result.

**Step 5. Acceptance.** `State = Accepted`, remove from `_pendingOrdersSync`, append `OrderSyncData` to `_activeOrdersSync`. For `OrderQuest`, call `Receiver.CharacterQuestLog.RegisterQuest(this)` — that subsystem's existing `NetworkList<QuestSnapshotEntry>` sync handles the quest log update on every client. Then immediately transition `State = Active`.

**Step 6. Refusal.** `State = Disobeyed`, fire each `IOrderConsequence.Apply(...)` server-side. Per-strategy try/catch so one failing consequence cannot abort the rest. Effects propagate naturally:
- `RelationDrop`: `CharacterRelation.UpdateRelation()` → existing NetworkList syncs to all clients.
- `IssuerAttacks`: `CharacterCombat.SetTarget(receiver)` → existing combat replication.
- `StatusEffect`: existing status effect system replicates.

Then remove from sync lists.

**Step 7. Active phase.** Server `Update()` ticks every active order with `OnTick(deltaTime)`. Every 0.5s it polls `IsComplied()`. For `Order_Kill` that is `target.IsDead`. For `Order_Leave` that is `!targetZone.Contains(receiver.position)`. On compliance → `State = Complied`, fire rewards, remove from sync lists.

**Step 8. Timeout.** When `ElapsedSeconds >= TimeoutSeconds` and not complied → `State = Disobeyed`, fire consequences, remove.

**Step 9. Issuer cancellation.** `CancelIssuedOrderServerRpc(orderId)` (callable by the issuer's owning client) → `State = Cancelled`, no consequences, no rewards.

### Multiplayer scenarios — explicitly validated

All order logic runs server-side; all sync flows through NetworkLists or Owner RPCs.

| Scenario | Issuer side | Receiver side | Mechanism |
|---|---|---|---|
| Host player → Host's NPC | Local server call | Local server tick | Direct (no RPC) |
| Host player → Client player | `IssueOrderServerRpc` (no-op, already server) | `ShowOrderPromptRpc` to client owner | Owner RPC → `ResolvePlayerOrderServerRpc` |
| Host player → Client's NPC | Local server call | Server-side coroutine | NPC always server-owned |
| Client player → Host player | `IssueOrderServerRpc` from client | `ShowOrderPromptRpc` to host owner | Owner RPC routes to host's local UI |
| Client player → Other Client player | `IssueOrderServerRpc` | Server resolves target client, `ShowOrderPromptRpc` | Standard NGO ownership routing |
| Client player → NPC | `IssueOrderServerRpc` | Server-side NPC coroutine | NPC always server-side |
| NPC → Player (Host or Client) | Server local issuance | `ShowOrderPromptRpc` | Owner RPC |
| NPC → NPC | Server local | Server local coroutine | All server-side |

**Late joiners** see all `_activeOrdersSync` and `_pendingOrdersSync` entries automatically through NetworkList initial sync (mirrors `CharacterRelation.OnNetworkSpawn` at lines 73–82). For `OrderQuest`s, `CharacterQuestLog`'s existing dormant-snapshot wake-on-map-change handles re-resolution.

**Authority verification on every ServerRpc:**
- `IssueOrderServerRpc`: sender must own the issuer Character.
- `ResolvePlayerOrderServerRpc`: sender must own the receiver Character.
- `CancelIssuedOrderServerRpc`: sender must own the issuer Character.

## 7. Save / Load

Three persistence paths, no overlap:

**Path 1 — Issuer-side ledger** → `CharacterOrders.OrdersSaveData.issuedOrders`.
Saves outstanding orders this character has issued. On reload, server walks the list and tries to re-resolve each receiver via `Character.FindByUUID`:
- Receiver present → re-arm the live Order on the server, re-link to the receiver's `CharacterQuestLog` entry (for OrderQuest, by `LinkedQuestId`), resume timeout.
- Receiver not loaded → mark dormant, hook `Character.OnCharacterSpawned`. Same dormant pattern `CharacterRelation` uses (lines 95–135).

**Path 2 — Receiver-side `OrderQuest`s** → already persisted by `CharacterQuestLog`.
Each `OrderQuest` *is* an `IQuest`, so it serializes through the existing snapshot machinery. On reload, `CharacterQuestLog` deserializes its snapshots; `CharacterOrders.OnNetworkSpawn` walks them, recognizes `OrderQuest`-typed entries (via `OrderTypeName`), and rebuilds the live Order references on the server so timeouts, polling, and consequences continue.

**Path 3 — Receiver-side `OrderImmediate`s** → not persisted by design.
Documented in `OrdersSaveData` comments and `CharacterOrders` SKILL.md.

**Multiplayer save handoff** (rule #20): on portal-gate return, the player's local profile saves `CharacterOrders.OrdersSaveData` (issuer-side ledger only) and `CharacterQuestLog` (which captures their OrderQuests). On rejoin to another session, both deserialize and dormant-resolve as the world spawns characters.

**`LoadPriority` ordering:**
- `CharacterRelation` = 50 (existing)
- `CharacterQuestLog` = 55 (existing; must load before `CharacterOrders`)
- `CharacterOrders` = 60 (new)

## 8. NPC AI Integration

One new GOAP goal:

```csharp
public class Goal_FollowOrder : GoapGoal
{
    public override int Utility(Character agent)
    {
        var top = agent.CharacterOrders?.GetTopActiveOrder();
        return top?.Priority ?? 0;          // 0 = goal silent, planner ignores
    }

    public override Dictionary<string, object> DesiredWorldState(Character agent)
    {
        var top = agent.CharacterOrders.GetTopActiveOrder();
        return top.GetGoapPrecondition();   // Each Order subclass supplies its own goal-state
    }
}
```

Each concrete order tells GOAP what world-state it needs satisfied:
- `Order_Kill.GetGoapPrecondition()` → `{ "TargetIsDead_<id>" : true }` — planner picks `Goap_AttackTarget`.
- `Order_Leave.GetGoapPrecondition()` → `{ "OutsideZone_<id>" : true }` — planner picks `Goap_MoveToPosition`.

The planner already chooses goals by utility:
- A priority-90 Kill order's utility (90) naturally dominates routine goals (work=50, eat=40, socialize=20).
- A priority-30 Fetch order only wins when nothing else is pressing.
- No "drop everything" branch, no priority queue management, no separate interrupt path.

When the order resolves, `OnOrderResolved` fires → planner re-plans next tick → utility drops to 0 → normal life resumes.

If the agent has multiple active orders, `GetTopActiveOrder()` returns the highest-priority unsatisfied one. Lower-priority orders sit waiting; their independent timeouts may expire and fire their consequences — that is the intentional dramatic tension of conflicting orders.

## 9. v1 Concrete Orders

### `Order_Kill : OrderQuest`
- **Payload:** `ulong targetCharacterNetId`.
- **`CanIssueAgainst(receiver)`:** target ≠ receiver, target ≠ issuer, target alive, receiver has combat capability.
- **`Targets`:** `[ new CharacterQuestTarget(_target) ]`.
- **`IsCompleted()`:** `_target == null || !_target.IsAlive()`.
- **`Title`:** `"Kill {targetName}"`.
- **`Description`:** `"{IssuerName} has ordered you to kill {targetName} within {timeoutDays} days."`.
- **`GetGoapPrecondition()`:** `{ $"TargetIsDead_{_target.CharacterId}" : true }`.
- **Default consequences:** `[Consequence_RelationDrop_Heavy(-30), Consequence_IssuerAttacks]`.
- **Default rewards:** `[Reward_RelationGain_Heavy(+30)]`.
- **Default urgency:** `Important`.

### `Order_Leave : OrderImmediate`
- **Payload:** `Vector3 zoneCenter, float zoneRadius, ulong zoneEntityId` (zone is identified by an `IWorldZone` ref when one is in play, else a free-floating sphere).
- **`CanIssueAgainst(receiver)`:** receiver currently inside the zone.
- **`IsComplied()`:** `Vector3.Distance(receiver.position, zoneCenter) > zoneRadius`.
- **`GetGoapPrecondition()`:** `{ $"OutsideZone_{zoneEntityId}" : true }`.
- **Default consequences:** `[Consequence_RelationDrop_Light(-10), Consequence_IssuerAttacks]`.
- **Default rewards:** none.
- **Default urgency:** `Urgent` (~10–20s typical timeout).

## 10. File Layout

```
Assets/Scripts/Character/CharacterOrders/
  Order.cs                       (abstract base)
  OrderQuest.cs                  (abstract IQuest bridge)
  OrderImmediate.cs              (abstract)
  CharacterOrders.cs             (subsystem MonoBehaviour, ICharacterSaveData)
  IOrderIssuer.cs                (interface)
  AuthorityContextSO.cs
  AuthorityResolver.cs           (static helper)
  OrderSyncData.cs               (INetworkSerializable)
  PendingOrderSyncData.cs
  OrdersSaveData.cs
  CharacterAction_IssueOrder.cs
  Consequences/
    IOrderConsequence.cs
    Consequence_RelationDrop.cs
    Consequence_IssuerAttacks.cs
    Consequence_StatusEffect.cs
  Rewards/
    IOrderReward.cs
    Reward_RelationGain.cs
    Reward_GiveItem.cs
    Reward_StatusEffect.cs
  Concrete/
    Order_Kill.cs
    Order_Leave.cs
  AI/
    Goal_FollowOrder.cs

Assets/Resources/Data/AuthorityContexts/
  Authority_Stranger.asset
  Authority_Friend.asset
  Authority_Parent.asset
  Authority_PartyLeader.asset
  Authority_Employer.asset
  Authority_Captain.asset
  Authority_Lord.asset

Assets/Resources/Data/OrderConsequences/
  Consequence_RelationDrop_Light.asset    (-10)
  Consequence_RelationDrop_Heavy.asset    (-30)
  Consequence_IssuerAttacks.asset
  Consequence_StatusEffect_Wanted.asset

Assets/Resources/Data/OrderRewards/
  Reward_RelationGain_Light.asset         (+10)
  Reward_RelationGain_Heavy.asset         (+30)
  Reward_GiveItem_Sample.asset
  Reward_StatusEffect_Sample.asset

Assets/UI/Order/
  UI_OrderImmediatePopup.prefab           (mirrors UI_InvitationPopup)
  UI_OrderQuestEntry.prefab               (extends UI_QuestLogEntry with priority badge)
```

## 11. Defensive Coding (rule #31)

- All ServerRpc bodies wrapped in `try/catch` with `Debug.LogException(e)` + abort.
- All NetworkList mutations guarded by `if (!IsServer) return;`.
- All character lookups (`SpawnedObjects.TryGetValue`, `Character.FindByUUID`) check for null with structured logs that name the order ID, issuer, and receiver.
- All `IOrderConsequence.Apply()` and `IOrderReward.Apply()` calls wrapped per-strategy: one failing strategy cannot abort the rest.
- Each consequence/reward null-checks `issuer` per its own contract; `Order` core never branches on `issuer == null`.

## 12. Build Order (Phased Implementation)

Each phase is independently testable.

### Phase 1 — Foundation (no gameplay yet)
- `Order` base, `OrderState` / `OrderUrgency` enums.
- `IOrderIssuer`, `AuthorityContextSO`, `AuthorityResolver`.
- 7 AuthorityContext SO assets.
- `OrderSyncData`, `PendingOrderSyncData`, `OrdersSaveData` types.
- `CharacterOrders` skeleton with NetworkLists + empty hooks. Wire into `Character` facade.
- **Test:** assets load, NetworkLists serialize, `AuthorityResolver` returns expected context for known issuer/receiver pairs (Employer for boss/employee, Friend for friends, Stranger fallback).

### Phase 2 — Consequences & Rewards
- `IOrderConsequence` / `IOrderReward` interfaces.
- 3 consequence SOs + 3 reward SOs with their assets.
- **Test:** `Apply()` for each runs server-side and visible state changes (relation drop, item granted, status applied) replicate to clients in a 2-player session.

### Phase 3 — `OrderImmediate` + `Order_Leave` end-to-end
- `OrderImmediate` abstract subclass, `Order_Leave` concrete.
- Server lifecycle: issue → evaluate → accept → poll → resolve.
- Player UI: `UI_OrderImmediatePopup` prefab (forked from invitation popup).
- `CharacterAction_IssueOrder`.
- **Test:** NPC issues `Order_Leave` to player, player ignores → leaves zone vs. doesn't leave → consequences fire correctly. Validate Host↔Client + Client↔NPC scenarios.

### Phase 4 — `OrderQuest` + `Order_Kill` end-to-end
- `OrderQuest` abstract subclass (the `IQuest` bridge).
- `Order_Kill` concrete with `IQuestTarget` wrapper.
- Quest log entry rendering with priority badge (extend `UI_QuestLogEntry`).
- **Test:** Captain NPC issues `Order_Kill` to guard NPC; guard accepts; GOAP planner picks `Goal_FollowOrder` and pursues the target. Player Kill order: appears in quest log, Accept/Refuse buttons work, completion grants reward.

### Phase 5 — GOAP integration
- `Goal_FollowOrder` registered with the planner.
- **Test:** NPC with active priority-90 order interrupts work shift; on resolve, returns to work.

### Phase 6 — Save/Load + Late joiners
- `CharacterOrders.ICharacterSaveData` implementation.
- Issuer-ledger dormant pattern (mirrors `CharacterRelation`).
- Late-joiner NetworkList replay validated.
- **Test:** save mid-order, reload, order resumes correctly. Host + Client + second-Client late join all see existing orders.

### Phase 7 — Documentation + Agents
- All SKILL.md / wiki / agent updates listed in §13.
- **Test:** spec compliance review against rules #28, #29, #29b.

## 13. Documentation Deliverables

### New
- `.agent/skills/order-system/SKILL.md` — purpose, public API, events, dependencies, integration points (Quest / CharacterAction / GOAP), multiplayer behavior, save/load contract.
- `wiki/systems/order-system.md` — architecture page, 10-section template, cross-linked to `quest-system.md`, `character-relations.md`, `goap.md`, `character-action.md`, `character-traits.md`, `social-system.md`.
- `.claude/agents/order-system-specialist.md` — `model: opus`. Scope: Order base + subclasses, IOrderIssuer + AuthorityContext, IOrderConsequence/IOrderReward catalog, CharacterOrders subsystem, Goal_FollowOrder, multiplayer flow.

### Updated SKILL.md files
- `quest-system/SKILL.md` — add `OrderQuest` as a quest producer.
- `goap/SKILL.md` — add `Goal_FollowOrder` as a utility-driven goal.
- `social_system/SKILL.md` — note orders use authority-driven evaluation (different from invitations' relationship-only path).
- `save-load-system/SKILL.md` — note `OrdersSaveData` contract and the issuer-ledger dormant pattern.
- `multiplayer/SKILL.md` — add the order ServerRpc + Owner RPC patterns to the reference table.

### Updated wiki pages
- `wiki/systems/quest-system.md`, `goap.md`, `character-relations.md`, `npc-ai.md`, `social-system.md` — bump `updated:`, append `## Change log` line `- 2026-04-26 — added Order system integration — claude`, refresh `depends_on` / `depended_on_by`.

### Updated agents
- `npc-ai-specialist.md` — add `Goal_FollowOrder` and `Order` consumption to scope.
- `quest-system-specialist.md` — add `OrderQuest` to known producers.
- `character-social-architect.md` — note that `CharacterRelation` is now also driven by Order consequences/rewards.
- `network-validator.md` — add the order RPC patterns to the audit checklist.
- `save-persistence-specialist.md` — add `OrdersSaveData` and the issuer-ledger dormant pattern.

## 14. Open Questions / Future Work

- **Building-as-issuer:** deferred to v2 once a concrete gameplay use case appears. The `IOrderIssuer` interface and nullable issuer slot leave the door open with no refactor.
- **Faction-as-issuer:** same — deferred until faction system exists.
- **Long-range orders:** `AuthorityContextSO.BypassProximity` flag is in the schema but always `false` in v1.
- **Order chaining** ("after A, do B"): not in v1; could be added as a higher-level scheduler that issues sequential orders.
- **Order pre-emption** based on authority tier comparison: not in v1 — multi-order coexistence + GOAP utility selection covers the gameplay.
- **Player-issued order UI inputs:** the spec assumes a context-menu / right-click target → "Issue Order" workflow consistent with other player verbs. The exact UI panel for selecting order type, urgency, and consequence presets is in scope for the implementation plan but not enumerated here.

## 15. Sources

- `Assets/Scripts/Character/CharacterRelation/CharacterRelation.cs` — bilateral relations, NetworkList sync, dormant pattern, personality compatibility filter.
- `Assets/Scripts/Character/CharacterTraits/CharacterTraits.cs` — `Loyalty`, `Aggressivity`, `Sociability` for NPC evaluation.
- `Assets/Scripts/Character/CharacterInvitation/CharacterInvitation.cs` — reference pattern for delayed evaluation, popup UI, Owner RPC, follow-target.
- `Assets/Scripts/Character/CharacterActions/CharacterAction.cs` — base contract for all character verbs (rule #22).
- `.agent/skills/social_system/SKILL.md` — relation/interaction architecture.
- `.agent/skills/quest-system/SKILL.md` — `IQuest` contract, `IQuestTarget` wrappers, `CharacterQuestLog` snapshot sync.
- `.agent/skills/goap/SKILL.md` — utility-driven goal system.
- `.agent/skills/multiplayer/SKILL.md` — RPC patterns, ownership, late-joiner support.
- `.agent/skills/save-load-system/SKILL.md` — `ICharacterSaveData<T>` contract.
- `.agent/skills/character_core/SKILL.md` — `Character` facade and child-hierarchy convention.
- `CLAUDE.md` rules #9, #18, #19, #20, #22, #28, #29, #29b, #31, #33.
