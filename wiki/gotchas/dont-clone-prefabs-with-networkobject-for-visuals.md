---
type: gotcha
title: "Don't clone prefabs with NetworkObject for visual-only purposes"
tags: [networking, ngo, prefabs, rendering, visuals, host-vs-client]
created: 2026-04-25
updated: 2026-04-25
sources: []
related: ["[[storage-furniture]]", "[[network]]", "[[items]]"]
status: mitigated
confidence: high
---

# Don't clone prefabs with NetworkObject for visual-only purposes

## Summary
When you `Instantiate` a prefab that carries a `NetworkObject` (e.g. `WorldItemPrefab`) purely for **visual display** — i.e. you have no intent to call `NetworkObject.Spawn()` on the clone — the cloned `NetworkObject` is "homeless". The host tolerates this; clients silently break. The clone ends up either at world `(0,0,0)`, in a non-rendering state, or with a parent that NGO has reverted. The symptom is "works on host, invisible on clients" with **no error in the console**, which is the worst kind of bug to chase.

## Symptom
- Server / host renders the visual correctly at the intended position.
- Clients see nothing — no GameObject children under the intended parent, OR a GameObject at world `(0,0,0)`, OR a GameObject parented to scene root instead of the intended anchor.
- Console shows no errors. NGO is technically in a valid state on every peer; it just refuses to let your `SetParent` / `localPosition` commands stick on clients.
- Destroying the cloned `NetworkObject` after `Initialize` (à la `DestroyImmediate(netObj)`) helps on the host but **does not fix the client side** — by the time you destroy it, NGO has already snapshotted the GameObject's "managed" state.

## Root cause
NGO tracks `NetworkObject` instances per peer through the `SpawnManager`. On the host, `Instantiate(prefabWithNO)` produces an untracked NetworkObject that's effectively dormant — `Spawn()` was never called, no replication, no parent enforcement. On clients, NGO's spawn-tracking is stricter: any `NetworkObject` the client knows about (whether truly spawned or just instantiated) is subject to parent-enforcement rules (`NetworkObject` parent must itself be a `NetworkObject` or scene root) AND `NetworkTransform` replication intent. The combination silently overrides client-side `SetParent` / `localPosition` calls.

The host doesn't see this because the client-side enforcement code paths don't run on the writing peer.

## How to avoid
- **Default rule:** if the clone is purely for visual display (no networking intent), instantiate the **visual sub-prefab** (e.g. `ItemSO.ItemPrefab` instead of `ItemSO.WorldItemPrefab`). The visual sub-prefab has no `NetworkObject` and no `NetworkTransform` — nothing for NGO to interfere with.
- If you need behaviour that the wrapper prefab provides (e.g. `SortingGroup` for 2D sprite layering), **add that component to your visual clone manually** — `go.AddComponent<SortingGroup>()` is one line.
- Mirror any wrapper-level configuration logic in your own helper. For items, `WorldItem.Initialize` does: instantiate the visual into `_visualRoot`, configure `WearableHandlerBase`, fall back to `ItemInstance.InitializeWorldPrefab`, apply `ShadowCastingMode`. Replicate those steps directly on your visual clone.
- **If you absolutely must clone a prefab with a `NetworkObject`,** the only reliable path is to spawn it via `NetworkObject.Spawn()` (so it goes through the canonical replicate path) and then RPC the visual setup. Don't try to "clone-and-strip" — the strip is a host-only escape hatch.

## How to fix (if already hit)
1. Identify the prefab being cloned. Look for an `Instantiate(...)` whose argument has a `NetworkObject` component on its root.
2. Find the visual sub-prefab counterpart on the same `ItemSO` / source data (often a sibling `ItemPrefab` field).
3. Switch the `Instantiate` to use the visual sub-prefab.
4. Audit anything the wrapper provided beyond raw visuals (`SortingGroup`, custom layer, animator override). Add those manually to the visual clone.
5. Audit any "init" call on the wrapper component (e.g. `WorldItem.Initialize`). Replicate its visual-config block directly in your code (see how [StorageVisualDisplay.cs](../../Assets/Scripts/World/Furniture/StorageVisualDisplay.cs) `ApplyItemVisual` mirrors `WorldItem.Initialize`).
6. Test on **client** specifically — host-only testing is what hides this bug-class for hours.

## Affected systems
- [[storage-furniture]] — `StorageVisualDisplay` originally cloned `WorldItemPrefab`; switched to `ItemPrefab` + manual `SortingGroup` on 2026-04-25 to render on clients.
- Any future system that needs to display items in static positions (UI viewers, preview cameras, world map markers, mannequins).

## Links
- [[network]] — NGO authority model and `NetworkObject` lifecycle.
- [[items]] — `ItemSO.ItemPrefab` vs `ItemSO.WorldItemPrefab` distinction.

## Sources
- 2026-04-25 conversation with Kevin — `StorageVisualDisplay` not rendering for clients in multiplayer; resolved by switching from `WorldItemPrefab` to `ItemPrefab` + `SortingGroup`.
- [Assets/Scripts/World/Furniture/StorageVisualDisplay.cs](../../Assets/Scripts/World/Furniture/StorageVisualDisplay.cs) — canonical reference implementation for "clone-for-display".
