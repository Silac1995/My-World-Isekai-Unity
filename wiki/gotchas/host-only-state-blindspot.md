---
type: gotcha
title: "Host-only state blindspot — every new feature must run the client-side audit before 'done'"
tags: [networking, ngo, multiplayer, client-sync, audit, recurring-pattern]
created: 2026-05-14
updated: 2026-05-14
sources:
  - "[Assets/Scripts/World/Furniture/OccupiableFurniture.cs](../../Assets/Scripts/World/Furniture/OccupiableFurniture.cs)"
  - "[Assets/Scripts/World/Furniture/Cashier.cs](../../Assets/Scripts/World/Furniture/Cashier.cs)"
  - "[Assets/Scripts/World/Furniture/CashierNetSync.cs](../../Assets/Scripts/World/Furniture/CashierNetSync.cs)"
  - "[Assets/Scripts/Interactable/InteractableObject.cs](../../Assets/Scripts/Interactable/InteractableObject.cs)"
  - "[Assets/Scripts/UI/Shop/UI_ShopBuyPanel.cs](../../Assets/Scripts/UI/Shop/UI_ShopBuyPanel.cs)"
  - "[Assets/Scripts/Character/CharacterEquipment/CharacterEquipment.cs](../../Assets/Scripts/Character/CharacterEquipment/CharacterEquipment.cs)"
  - "[Assets/Scripts/Character/CharacterActions/CharacterAction_BuyFromShop.cs](../../Assets/Scripts/Character/CharacterActions/CharacterAction_BuyFromShop.cs)"
  - "[Assets/Scripts/Character/CharacterActions/CharacterTakeFromFurnitureAction.cs](../../Assets/Scripts/Character/CharacterActions/CharacterTakeFromFurnitureAction.cs)"
  - "[Assets/Scripts/Character/CharacterActions/CharacterStoreInFurnitureAction.cs](../../Assets/Scripts/Character/CharacterActions/CharacterStoreInFurnitureAction.cs)"
  - "[Assets/Scripts/Character/CharacterActions/CharacterActions.cs](../../Assets/Scripts/Character/CharacterActions/CharacterActions.cs)"
  - "[Assets/Scripts/World/Furniture/StorageFurnitureNetworkSync.cs](../../Assets/Scripts/World/Furniture/StorageFurnitureNetworkSync.cs)"
  - "[Assets/Scripts/Item/WorldItem.cs](../../Assets/Scripts/Item/WorldItem.cs)"
  - "[.agent/skills/shop_system/SKILL.md](../../.agent/skills/shop_system/SKILL.md)"
  - "[.agent/skills/item_system/SKILL.md](../../.agent/skills/item_system/SKILL.md)"
  - "[.agent/skills/interactable-system/SKILL.md](../../.agent/skills/interactable-system/SKILL.md)"
  - "2026-05-14 conversation with Kevin (cashier on-client interaction debugging — peeled four layers of host/client desync to root)"
  - "2026-05-14 conversation with Kevin (remote-client bag inventory desync — shop buy, chest take, chest store all silently dropped items)"
related:
  - "[[static-registry-late-joiner-race]]"
  - "[[host-player-uuid-timing-on-load]]"
  - "[[network-architecture]]"
  - "[[shops]]"
  - "[[interactable-system]]"
  - "[[character-equipment]]"
  - "[[storage-furniture]]"
status: open
confidence: high
---

# Host-only state blindspot — every new feature must run the client-side audit before "done"

## Summary

A **recurring class** of multiplayer bugs in this project: a new feature ships with state that is correctly written, validated, and read on the **host** (the authoritative server), but no replication channel exists for any **non-host client** that needs the same value. Symptoms always present as "works on the host, broken on the client / joining player". Kevin has had to point this out repeatedly across features (cashier vendor seating, customer locks, interaction-zone Y-edge precision, missing `LinkedShop` on joining clients, static registry initialisation, host player UUID timing, etc.). This page is the **standing rule**: every new feature must explicitly answer the client-side audit checklist **before** the feature is claimed done.

This page exists to **stop the pattern**, not just document the symptom.

## The audit checklist (mandatory before claiming any feature "done")

Run this checklist for **every new state field**, **every new method that mutates state**, **every new UI surface that reads state**, and **every new feature** — even ones that look "server-only at first glance". The default assumption is **"a client will need this"**, not the other way around.

### 1. Who writes this state? Who reads it?

Enumerate **every** writer and **every** reader.

| Role | Writers | Readers |
|------|---------|---------|
| Server-only (NPC AI, ServerRpc handlers, scheduling) | … | … |
| Client (UI pre-gates, HUD widgets, player input handlers) | … | … |
| Host (both — special case of server + local client) | … | … |

If **any** reader is in the client column, the field needs a **replication channel**. No exceptions, no "the server always validates so the client just sees stale state" rationalisations — players see the stale state too, and that's the bug.

### 2. What replication channel does this field use?

For every field with at least one client reader, pick exactly one mechanism:

- **`NetworkVariable<T>`** — best default. Replicates automatically. Initial-state sync on connect delivers the current value to late-joiners for free. Use for: occupant ids, current customer ids, lock flags, simple counters, replicated configuration.
- **`NetworkList<T>` / `NetworkVariable<INetworkSerializable>`** — for collections / structured payloads (till balances, queue contents, equipment slots).
- **`ClientRpc` with payload** — for *transient* events (one-shot toasts, VFX triggers, UI open/close commands). **NOT for state.** A `ClientRpc` only fires for peers connected *at send time*; late-joiners never hear it.
- **`ClientRpc` paired with a `NetworkVariable` for the durable state** — when you want both the "transition event" and the "queryable current state". The NetVar covers late-joiners; the RPC is the visual hook for peers already connected.
- **Resolve-by-id pattern** — for fields that point at *another* NetworkObject (the vendor seated at a cashier, the customer holding a lock). Store the target's `NetworkObjectId` in a `NetworkVariable<ulong>`, and have the property accessor resolve it via `NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(...)` on non-server peers. See `Cashier.Occupant` / `Cashier.CurrentCustomer` for the canonical implementation.

If no mechanism fits, the field probably shouldn't be a field — it should be a method that computes from other replicated state, or it's actually fine being server-only and the client doesn't read it (verify the "doesn't read it" by grep).

### 3. What's the late-joiner story?

This is the audit step that the project has consistently failed.

Sit down and walk through the following thought experiment for every new feature:

> A client connects to a host that has already been running for 10 minutes. The host's world has state X that was mutated 5 minutes ago. Does the new client see X correctly the moment they finish connecting?

If your answer is "well, `ClientRpc` fires when X changes, and the client will see it next time it changes" — **wrong**. The client never saw the original change. Either:

- Use a `NetworkVariable` (initial-state sync handles this), **or**
- Add an `OnNetworkSpawn` server-side backfill that pushes the current value into the replication channel, **or**
- Add a "request current state" `ServerRpc` the joining client calls explicitly on spawn.

`CashierNetSync.OnNetworkSpawn` is the canonical backfill example: even though the NetVar gets the initial-state sync for free, the server also explicitly writes the current `Occupant` id at spawn time as a defence against pre-NetSync-spawn races (vendor was seated via `Use()` before this peer's `OnNetworkSpawn` callback ran).

### 4. What does the client-side pre-gate read?

This is the OTHER step the project has consistently failed. When a feature has a UI pre-gate ("show the toast immediately on click without a server round-trip"), the pre-gate reads **client-side state**. If the client doesn't have the same state the server has, the pre-gate fires the wrong message — "No vendor on duty" when there IS a vendor, or "Vendor is busy" when nobody is buying.

For every pre-gate on a UI button / `Interact()` method / hover prompt:

- Confirm every field the pre-gate reads is either (a) a `NetworkVariable` directly, or (b) resolved through a property that goes through a `NetworkVariable`-backed source on non-server peers.
- Confirm the property override returns the same value as the server's authoritative read would.
- Test by joining a client AFTER state mutation on the host (the "late-joiner repro" — start host, mutate state, join client, click the button).

### 5. What does the spawn race look like?

The `Building._defaultFurnitureLayout` pipeline does `Instantiate` then `SetParent` as two separate calls. On a joining client this means **`Awake` runs on the furniture before NGO applies the re-parent**. Anything in `Awake` that calls `GetComponentInParent<T>()` returns null. Common victims: `Cashier._linkedBuilding`, `*Furniture._room`, `*Interactable._owningBuilding`.

For any new component with `GetComponentInParent` in Awake/OnEnable:

- Provide an idempotent `TryRegisterWith*` method that re-resolves the parent and registers with it. Mark it safe to call repeatedly.
- Call it from `OnEnable` (happy path), from the parent's `OnNetworkSpawn` (server late-bind), and from any **consumer** that hits a null in the value (lazy late-bind from UI code, GOAP action, etc.).
- See `Cashier.TryRegisterWithShop` for the canonical pattern, and `UI_ShopBuyPanel.Initialize`'s `if (_shop == null) cashier.TryRegisterWithShop()` fallback as a consumer using it.

### 6. What does the proximity check look like?

If the feature involves a player getting "close enough" to an `InteractableObject`, the proximity gate must go through `InteractableObject.IsCharacterInInteractionZone(Character)` — the canonical helper. As of 2026-05-14 the check is **2D X-Z only** (see `[[interactable-system]]` SKILL — bottom-edge Y precision was a recurring class of false negatives on networked clients). Never inline a `Vector3.Distance` check, never use `Bounds.Contains(rb.position)`, never write a custom proximity helper.

## How to apply this

Every PR / commit that adds or modifies a feature with at least one of:

- A new `NetworkBehaviour` field
- A new state-mutating method called only from server-side paths
- A new UI surface that reads game state
- A new `InteractableObject` subclass
- A new `CharacterAction`
- A new GOAP/BT action with state preconditions

…must include, in the commit description **or** in the SKILL.md update, a one-paragraph statement of:

1. **Replication channel** for every client-visible field added or modified.
2. **Late-joiner test result** — "joined a fresh client after state mutation, observed correct value".
3. **Pre-gate audit** — every UI button / Interact method that reads the new state was confirmed to see the same value on both host and client.

If these three statements can't be made truthfully, **the feature is not done**. Bringing it to a playtest where Kevin has to point out the client-side gap is a process failure, not a code failure.

## Concrete examples that hit this gotcha

### Cashier vendor occupancy (2026-05-14)

- **Symptom:** client presses E on cashier with vendor seated → "No vendor on duty" toast.
- **Root cause:** `OccupiableFurniture._occupant` is a plain C# field; the only writer is `Use()` called from `ServerTickAutoOccupy` (server-only). `NotifyOccupiedClientRpc` existed but its body was empty (visual-hook placeholder, not state transport).
- **Fix:** added `OccupantNetworkObjectId` `NetworkVariable<ulong>` on `CashierNetSync` written by `Cashier.Use`/`Release`; overrode `Cashier.Occupant` to resolve the id via `SpawnedObjects` on non-server peers; added `OnNetworkSpawn` server-side backfill.
- **What the audit would have caught:** Step 1 — `_occupant` reader includes `CashierInteractable.Interact` (client pre-gate). Step 2 — no replication channel existed. Step 4 — the pre-gate explicitly reads the field; would have surfaced immediately.

### Cashier current customer (2026-05-14, latent)

- **Symptom (latent):** client presses E on cashier with another customer mid-transaction → no "vendor busy" pre-gate fires; the RPC goes to the server, the server sends a busy toast back, extra roundtrip + worse UX. Bug never visible because server fallback masked it.
- **Root cause:** same shape — `Cashier._currentCustomer` plain field, only the server writes via `TryAcquireCustomerLock`. The mirror NetVar `CurrentCustomerNetworkObjectId` existed but `CurrentCustomer` property read the field directly.
- **Fix:** rewrote the `CurrentCustomer` getter with the same id-resolution pattern as `Occupant`.

### UI_ShopBuyPanel LinkedShop on joining client (2026-05-14)

- **Symptom:** client passes the cashier pre-gate but the buy panel doesn't visually appear.
- **Root cause:** `Cashier.Awake`'s `GetComponentInParent<CommercialBuilding>()` returned null because the joining client received `Cashier.Instantiate` before NGO's `AutoObjectParentSync` re-parent. `UI_ShopBuyPanel.Initialize` saw `_shop == null` and silently `CloseWindow()`d.
- **Fix:** late-bind fallback in `Initialize` calling `cashier.TryRegisterWithShop()` if `_shop == null`.

### IsCharacterInInteractionZone Y-edge (2026-05-14)

- **Symptom:** client presses E next to cashier → silent early-return, no toast, no panel. Host works fine in identical position.
- **Root cause:** `Bounds.Contains(player.transform.position)` was full 3D AABB. Player y == 0 sits exactly on `bounds.min.y` for floor-authored InteractionZones. Float precision on `ClientNetworkTransform` rounded the value a hair below zero on the joining client.
- **Fix:** dropped Y from the check; gate is now 2D X-Z containment, matching the construction-loop convention.

### Bag-inventory state not replicated — buy / take / store all silent on remote clients (2026-05-14)

This is a **new shape** of the pattern: the field IS legitimately split between host-only and client-only state, but the **mutation methods** were called only on the server, so the owner client never saw the change. Three flows were broken simultaneously on a joining client, all root-causing to the same architectural fact.

- **Symptom A — shop buy:** joining client presses Confirm in the buy panel → wallet debited, item disappears from the sell-shelf, but **never appears in the client's bag**. Host works fine.
- **Symptom B — chest take:** joining client clicks a chest slot → item disappears from chest grid but **never appears in client's bag**. Host works fine.
- **Symptom C — chest store (bag → chest):** joining client clicks a bag slot in the storage panel → **nothing happens** server-side. Host works fine. (`RequestStoreFromBagServerRpc` early-returned because `inv.ItemSlots[slotIndex].IsEmpty()` on the server-side shadow copy.)
- **Root cause:** `CharacterEquipment._networkEquipment` (a `NetworkList<NetworkEquipmentSyncData>`) replicates the **equipment slots only**: weapon (slot 0), bag *shell* (slot 1), wearables (slots 100+). The **items inside the bag's `Inventory.ItemSlots` are not in any replication channel**. They live as two independent copies — server-side on the server peer (populated for the host's character because host == server; mostly empty for remote-client characters), and client-side on the owning client (populated at session start by `CharacterEquipment.Deserialize` from the local profile). Pre-existing mutations from the host's perspective worked because both copies are the same C# object on the host; remote-client mutations through server-side actions only touched the server's shadow.
- **Fix:** mirror `WorldItem.RequestInteractServerRpc`'s ownership branch for both directions:
  - **Server → client direction (buy + take):** in the action's server-side path, gate on `character.IsSpawned && !character.IsOwnedByServer`. For remote-client characters, build a `NetworkItemData` (ItemSO id + `JsonUtility.ToJson(instance)`) and call `character.CharacterActions.ReceiveItemPickupClientRpc(...)`. The owner reconstructs the item via `ItemSO.CreateInstance()` + `JsonUtility.FromJsonOverwrite` (preserves `WeaponInstance` / `WearableInstance` polymorphism) and calls `PickUpItem` locally. Host + NPC characters keep the existing direct path.
  - **Client → server direction (store):** the client UI now sends the item payload (id + JSON) **plus** the source slot index (or `-1` for hands) inside the ServerRpc. The server reconstructs the `ItemInstance`, validates the chest (lock + capacity), and queues `CharacterStoreInFurnitureAction` with the slot index. The action's `OnApplyEffect` has three resolution modes: remote-client (server `AddItem` → ack via `RemoveFromInventoryAfterStoreClientRpc(slotIndex)`), host-with-slot (server looks up its own bag slot directly and uses that ItemInstance reference for the chest insertion), and the legacy by-reference path (`_sourceSlotIndex == -2`, used by GOAP NPC deposits — unchanged).
- **What the audit would have caught:** Step 1 — `_bag.Inventory.ItemSlots` reader includes the client's UI panels (bag side of the storage panel, inventory HUD); writer must reach the owner. Step 2 — no replication channel exists for bag-inventory contents, and the chosen pattern is NOT to add one but to route mutations to the owner via ClientRpc (avoiding a per-item NetworkList and matching the WorldItem flow). Step 3 — late-joiner is fine because the client loads its own profile locally at session start; subsequent mutations route through the ClientRpc / ServerRpc-with-payload pattern.
- **New rule that comes from this case:** when a state field is **deliberately not replicated** (some "shared C# state" lives on both peers independently — like bag inventory contents, or hands controller carry state), **every server-side mutation that needs to be visible on the owner client must route through a ClientRpc to that owner**. Direct mutation of the server's shadow copy is silently lost. Match the inverse direction: **every client→server delivery of such state must include the payload in the RPC** because the server cannot read the client's authoritative copy. The canonical reference implementations are `WorldItem.RequestInteractServerRpc` (pickup), `CharacterAction_BuyFromShop.DeliverToCustomer`, `CharacterTakeFromFurnitureAction.OnApplyEffect`, `StorageFurnitureNetworkSync.RequestStoreFrom{Bag,Hands}ServerRpc`, and `CharacterStoreInFurnitureAction.OnApplyEffect`. See [.agent/skills/item_system/SKILL.md](../../.agent/skills/item_system/SKILL.md) §"Bag-inventory replication authority" for the canonical code snippets and decision tree.

### Static registry not initialised on joining client (2026-04-29)

- See [[static-registry-late-joiner-race]] — same audit failure: `Initialize()` ran only in the host-only `GameLauncher.LaunchSequence` path.

### Host player UUID timing (earlier)

- See [[host-player-uuid-timing-on-load]] — same audit failure: state set in a path the joining client doesn't traverse.

## How to avoid

1. **Treat the audit checklist as part of "done"**, not "polish for later".
2. **Default to "a client reads this"** for every new state field. Make the developer prove otherwise (by grepping every consumer) before they're allowed to keep the field non-replicated.
3. **Always pair a `ClientRpc` event with a `NetworkVariable` for the durable state.** If you find yourself writing only a `ClientRpc`, ask: "what does a client see if they connect right now?" If the answer is "nothing" or "the previous state forever", you also need a NetVar.
4. **Run the late-joiner repro before shipping.** Host the session, mutate the state, join a fresh client, click the thing. This is one command in MPPM, ~20 seconds of clicking.
5. **Read this page when you touch any networked feature.** If you find a new example of the pattern, add it under "Concrete examples".

## How to fix (if already hit)

1. Identify which field the client reader needs.
2. Pick the replication channel (NetVar is the default).
3. If the field already has a NetVar mirror (very common — the project has a habit of declaring NetVars and then forgetting to wire the property accessor through them), fix the property accessor instead of adding a new NetVar. See `Cashier.Occupant` / `Cashier.CurrentCustomer` for the resolve-by-id property pattern.
4. Add an `OnNetworkSpawn` backfill if the state can be mutated before this peer's NetSync spawns.
5. Add a `TryRegister*` late-bind fallback if `GetComponentInParent` returned null in `Awake`.
6. Run the late-joiner repro to confirm.
7. Update the feature's SKILL.md to document the contract.

## Links

- [[interactable-system]] — canonical proximity gate (`IsCharacterInInteractionZone`) and the 2D X-Z rule.
- [[shops]] — the feature that surfaced this pattern most recently.
- [[network-architecture]] — the over-arching server-authoritative rules this audit operationalises.

## Sources

- 2026-05-14 conversation with [[kevin]] — multi-layer debugging session that peeled four distinct host/client desync layers off the cashier interaction flow: (1) `Occupant` not replicated, (2) `CurrentCustomer` latent variant, (3) `LinkedShop` spawn race, (4) `IsCharacterInInteractionZone` Y-edge precision. Kevin's frustration with the recurring pattern triggered the standing-rule formalisation.
- [Assets/Scripts/World/Furniture/Cashier.cs](../../Assets/Scripts/World/Furniture/Cashier.cs) — canonical resolve-by-id property pattern.
- [Assets/Scripts/World/Furniture/CashierNetSync.cs](../../Assets/Scripts/World/Furniture/CashierNetSync.cs) — canonical NetworkVariable + OnNetworkSpawn backfill pattern.
- [Assets/Scripts/UI/Shop/UI_ShopBuyPanel.cs](../../Assets/Scripts/UI/Shop/UI_ShopBuyPanel.cs) — canonical UI-side late-bind fallback.
- [Assets/Scripts/Interactable/InteractableObject.cs](../../Assets/Scripts/Interactable/InteractableObject.cs) — 2D X-Z proximity gate.
- [.agent/skills/shop_system/SKILL.md](../../.agent/skills/shop_system/SKILL.md) — feature-specific contract this gotcha generalises from.
- [.agent/skills/interactable-system/SKILL.md](../../.agent/skills/interactable-system/SKILL.md) — proximity gate rule, updated 2026-05-14.
