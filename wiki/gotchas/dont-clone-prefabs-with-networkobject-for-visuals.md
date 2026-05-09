---
type: gotcha
title: "Don't clone prefabs with NetworkObject for visual-only purposes"
tags: [networking, ngo, prefabs, rendering, visuals, host-vs-client, scene-sync, late-joiner]
created: 2026-04-25
updated: 2026-05-01
sources: []
related: ["[[storage-furniture]]", "[[network]]", "[[items]]", "[[character-visuals]]"]
status: mitigated
confidence: high
---

# Don't clone prefabs with NetworkObject for visual-only purposes

## Summary
When you `Instantiate` a prefab that carries a `NetworkObject` (e.g. `WorldItemPrefab`) purely for **visual display** — i.e. you have no intent to call `NetworkObject.Spawn()` on the clone — the cloned `NetworkObject` is "homeless". This bug-class has **two distinct failure modes**, picked by where you parent the clone:

1. **Host scene-sync NRE (failure mode A):** if the clone is parented under another spawned `NetworkObject` (e.g. a player's hand bone, a NetworkBehaviour'd container), NGO's `SortParentedNetworkObjects` walks the parent's `GetComponentsInChildren<NetworkObject>()` during late-joiner sync, picks up the unspawned clone, and `NetworkObject.Serialize` (`NetworkObject.cs:3172`) NRE's because `NetworkManagerOwner` is null (never went through `SpawnInternal`). **Late-joining clients can't connect at all** — host throws every `ProcessPendingApprovals` tick.
2. **Client rendering silently breaks (failure mode B):** if the clone is parented under a non-NetworkObject anchor (e.g. a static display shelf), the host renders fine but on clients the GameObject ends up at world `(0,0,0)`, parented to scene root, or in a non-rendering state. **No console error** — NGO's stricter client-side enforcement silently overrides the parenting/positioning.

Both failures share the same anti-pattern: a `NetworkObject` exists in the scene without ever going through `NetworkObject.Spawn()`. Whoever ends up walking the hierarchy first (host serializer for A, client receiver for B) trips over the inconsistent state.

## Symptom — Failure mode A (host scene-sync NRE)
- Late-joining clients can never connect — they get past handshake, then the host loops on `NullReferenceException: Object reference not set to an instance of an object` in `NetworkObject.Serialize` line 3172, inside `WriteSceneSynchronizationData → ProcessPendingApprovals`.
- The bug only fires when the **host** has the visual clone in its hierarchy. A *client* with the same visual clone in their local scene doesn't break anything — only the host writes scene-sync to joiners.
- Removing the visual (e.g. dropping the held item, closing the inventory preview) immediately unblocks the next connection attempt.
- Repro: host carries any item via `HandsController` → second client tries to join → host NRE storm.

## Symptom — Failure mode B (client rendering)
- Server / host renders the visual correctly at the intended position.
- Clients see nothing — no GameObject children under the intended parent, OR a GameObject at world `(0,0,0)`, OR a GameObject parented to scene root instead of the intended anchor.
- Console shows no errors. NGO is technically in a valid state on every peer; it just refuses to let your `SetParent` / `localPosition` commands stick on clients.

## Root cause
NGO tracks `NetworkObject` instances per peer through the `SpawnManager`. On the host, `Instantiate(prefabWithNO)` produces an untracked NetworkObject that's effectively dormant — `Spawn()` was never called, no replication, no parent enforcement. But the GameObject still **exists in the transform hierarchy**, and that's where the two failure modes diverge:

- **For mode A:** `SceneEventData.SortParentedNetworkObjects` (`SceneEventData.cs:283-316`) iterates every spawned root NO and calls `GetComponentsInChildren<NetworkObject>().ToList()` on each. The list-rebuild logic at line 308 / 312 `AddRange` / `InsertRange`'s the walk results into `m_NetworkObjectsSync` — so an unspawned NO that's a transform-child of a spawned root gets surfaced into the sync list. `Serialize` then NRE's at line 3172 because `NetworkManagerOwner` was set in `SpawnInternal`, which never ran.
- **For mode B:** on clients, NGO's spawn-tracking is stricter: any `NetworkObject` the client knows about (whether truly spawned or just instantiated) is subject to parent-enforcement rules (`NetworkObject` parent must itself be a `NetworkObject` or scene root) AND `NetworkTransform` replication intent. The combination silently overrides client-side `SetParent` / `localPosition` calls.

The host doesn't see mode B because the client-side enforcement code paths don't run on the writing peer. Mode A goes the other way — the writing peer's serializer is what NREs.

## How to avoid (preferred approach)
- **Default rule:** if the clone is purely for visual display (no networking intent), instantiate the **visual sub-prefab** (e.g. `ItemSO.ItemPrefab` instead of `ItemSO.WorldItemPrefab`). The visual sub-prefab has no `NetworkObject` and no `NetworkTransform` — nothing for NGO to interfere with.
- If you need behaviour that the wrapper prefab provides (e.g. `SortingGroup` for 2D sprite layering), **add that component to your visual clone manually** — `go.AddComponent<SortingGroup>()` is one line.
- Mirror any wrapper-level configuration logic in your own helper. For items, `WorldItem.Initialize` does: instantiate the visual into `_visualRoot`, configure `WearableHandlerBase`, fall back to `ItemInstance.InitializeWorldPrefab`, apply `ShadowCastingMode`. Replicate those steps directly on your visual clone.

## How to avoid (fallback when wrapper-prefab is structurally needed)
- If switching to the visual sub-prefab is non-trivial (e.g. the wrapper carries 10+ child components and refactoring the prefab is out of scope), the **clone-and-strip** path works: `Instantiate` the wrapper prefab, complete any `Initialize`-style visual setup synchronously, then `DestroyImmediate` every `NetworkBehaviour` and the `NetworkObject` recursively. Order: NetworkBehaviours first, then NetworkObject. Use `GetComponentsInChildren<…>(true)` to catch nested clones.
- Canonical implementation: [HandsController.StripNetworkComponents](../../Assets/Scripts/Character/CharacterBodyPartsController/HandsController.cs) (added 2026-05-01 in response to the held-item NRE repro).
- This was previously called out in this gotcha as "host-only escape hatch" — that earlier wording was wrong. `DestroyImmediate` removes the clone from the hierarchy synchronously, so neither mode A nor mode B triggers afterward, on any peer. The 2026-04-25 mode-B repro tried `Destroy` (async, end-of-frame) which doesn't strip in time; `DestroyImmediate` does.

## How to fix (if already hit)
1. Identify the prefab being cloned. Look for an `Instantiate(...)` whose argument has a `NetworkObject` component on its root.
2. Find the visual sub-prefab counterpart on the same `ItemSO` / source data (often a sibling `ItemPrefab` field).
3. **Preferred:** switch the `Instantiate` to use the visual sub-prefab. Audit anything the wrapper provided beyond raw visuals (`SortingGroup`, custom layer, animator override). Add those manually to the visual clone. Audit any "init" call on the wrapper component (e.g. `WorldItem.Initialize`). Replicate its visual-config block directly in your code (see how [StorageVisualDisplay.cs](../../Assets/Scripts/World/Furniture/StorageVisualDisplay.cs) `ApplyItemVisual` mirrors `WorldItem.Initialize`).
4. **Fallback:** call `StripNetworkComponents`-style helper after `Initialize` (see `HandsController` for the canonical impl).
5. **Test multiplayer both ways** — for failure mode A, host carries / equips / previews the visual then have a client join (the bug only fires for the host's clones, not clients'). For failure mode B, test client-side rendering of static visuals. Host-only testing hides both bug-classes for hours.

## Affected systems
- [[storage-furniture]] — `StorageVisualDisplay` originally cloned `WorldItemPrefab`; switched to `ItemPrefab` + manual `SortingGroup` on 2026-04-25 to render on clients (failure mode B).
- [[character-visuals]] — `HandsController.AttachVisualToHand` cloned `WorldItemPrefab` under the player's hand bone; fixed on 2026-05-01 by adding `StripNetworkComponents` after `Initialize` (failure mode A — host scene-sync NRE blocked all late-joiner connections while host carried any item).
- Any future system that needs to display items in static positions (UI viewers, preview cameras, world map markers, mannequins) or as carried/equipped visuals on a networked actor.

## Links
- [[network]] — NGO authority model and `NetworkObject` lifecycle.
- [[items]] — `ItemSO.ItemPrefab` vs `ItemSO.WorldItemPrefab` distinction.
- [[character-visuals]] — `HandsController` carry system.

## Sources
- 2026-04-25 conversation with Kevin — `StorageVisualDisplay` not rendering for clients in multiplayer; resolved by switching from `WorldItemPrefab` to `ItemPrefab` + `SortingGroup` (failure mode B).
- 2026-05-01 conversation with Kevin — host carrying any held item caused late-joiner clients to NRE-loop on host scene-sync; resolved by `StripNetworkComponents` in `HandsController.AttachVisualToHand` (failure mode A).
- [Assets/Scripts/World/Furniture/StorageVisualDisplay.cs](../../Assets/Scripts/World/Furniture/StorageVisualDisplay.cs) — canonical reference for "use ItemPrefab" approach.
- [Assets/Scripts/Character/CharacterBodyPartsController/HandsController.cs](../../Assets/Scripts/Character/CharacterBodyPartsController/HandsController.cs) — canonical reference for "Instantiate-then-strip" approach.
- [Library/PackageCache/com.unity.netcode.gameobjects@*/Runtime/SceneManagement/SceneEventData.cs:283-316](#) — NGO `SortParentedNetworkObjects` source (the walk that surfaces unspawned children into the sync list).
- [Library/PackageCache/com.unity.netcode.gameobjects@*/Runtime/Core/NetworkObject.cs:3170-3204](#) — NGO `Serialize` source (the NRE site for failure mode A).
