---
name: pickup_zone_and_reachability_net
description: PickupZone is optional per-CommercialBuilding outbound staging zone; paired with a NavMesh.CalculatePath safety net in GoapAction_MoveToTarget
type: project
---

# PickupZone (Phase A) + NavMesh reachability safety net (Phase B) — shipped 2026-04-23

**Why:** Transporters were walking into StorageZone, picking up an item, then stalling because NavMesh pathing failed somewhere in the in/out trip. Symptom: worker picks up + drops, then freezes.

**How to apply:**
- `CommercialBuilding._pickupZone` (optional `Zone`) — when authored, `JobLogisticsManager` runs a new `GoapAction_StageItemForPickup` that walks reserved items from StorageZone → PickupZone BEFORE the transporter arrives. Transporter then pathfinds to PickupZone only (never into StorageZone). Left null => legacy behaviour (go directly to WorldItem position).
- `BuyOrder.PathUnreachableCount` + `MaxPathUnreachableAttempts = 3` — bumped by `OnPathUnreachable` rollback in `GoapAction_MoveToItem` / `GoapAction_MoveToDestination`. `LogisticsTransportDispatcher.ProcessActiveBuyOrders` skips any BuyOrder with `IsReachabilityStalled == true` so stale orders don't loop forever — they expire naturally via `DecreaseRemainingDays`.
- `GoapAction_MoveToTarget` base now runs `NavMesh.CalculatePath` probe before every first-commit `SetDestination`. Skipped when `failCount > 0` (path-diversification retry). On `PathInvalid` / `PathPartial`, calls virtual `OnPathUnreachable(worker, dest, status)` — subclasses override with domain-specific rollback.
- `GoapAction_GatherStorageItems.FindLooseWorldItem` skips items inside PickupZone (don't re-gather staged items).
- `CommercialBuilding.RefreshStorageInventory` Pass 1 merges PickupZone scan into physical-items set (staged items aren't ghosts). Pass 2 absorption intentionally unchanged — staged items are already reserved.

**Staging policy:** New `GoapAction_StageItemForPickup` (SRP — separate from `GatherStorageItems`). Cost 0.2f — prefers staging over GatherStorageItems (0.5f) but after PlaceOrder (0.1f).

**Backward compat:** Every existing scene works unchanged when `_pickupZone == null`. Transporter falls back to raw `TargetWorldItem.transform.position` and the legacy arrival collider (`ItemInteractable.InteractionZone`).

**Deferred:** Supplier and destination ranking is still first-match in `FindSupplierFor` / `FindTransporterBuilding` — a reachability-stalled BuyOrder currently just parks instead of retrying via a different supplier. Upgrade later.
