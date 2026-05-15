---
type: system
title: "Construction Loop"
tags: [building, construction, character-actions, network, phase-1, tier-2]
created: 2026-05-06
updated: 2026-05-16
sources: []
related:
  - "[[building]]"
  - "[[building-state]]"
  - "[[building-placement-manager]]"
  - "[[character]]"
  - "[[items]]"
  - "[[world-items]]"
  - "[[save-load]]"
  - "[[network]]"
  - "[[material-buildproperties-standalone-crash]]"
  - "[[kevin]]"
status: stable
confidence: high
primary_agent: building-furniture-specialist
secondary_agents:
  - character-system-specialist
  - save-persistence-specialist
  - network-specialist
owner_code_path: "Assets/Scripts/World/Buildings/Construction/"
depends_on:
  - "[[building]]"
  - "[[character]]"
  - "[[items]]"
  - "[[network]]"
  - "[[save-load]]"
depended_on_by:
  - "[[building]]"
---

# Construction Loop

## Summary
Phase 1 cooperative construction loop. Any character (placer, co-op partner, future NPC) walks into the scaffolded building's `BuildingZone`, drops required items into its footprint, and triggers a continuous, ticked, cancelable Construct action that consumes items and advances progress. On completion, the visual swaps to the finished building and any leftover items are evicted to the perimeter. Server-authoritative, host/client/late-join safe, persists through save/hibernation. **Phase 1 is cooperative finalize — no owner gate** (the placer-only gate originally proposed was dropped 2026-05-06 so co-op players can finish each other's builds). `BuildingInteractable.IsOwner` survives but is reserved for Phase 2 hold-menu options (Abandon, Sell). NPC autonomy, community-manager console, and JobBuilder are Phase 2.

## Purpose
Buildings prior to this had two paths: instant-build (debug) and a "real" path that spawned the building already in `UnderConstruction` state but with no gameplay loop attached — `Building.ContributeMaterial` was a code-only entry point nothing called, and `CheckConstructionCompletion` would silently auto-flip the state if it ever did. This system delivers an actual gameplay loop wired into [[character]] actions and [[items]], replicated and persisted correctly.

## Responsibilities
- Toggling the active visual root on the same prefab between scaffolding (`UnderConstruction`) and final (`Complete`) — single `NetworkObject`, no respawn churn, persistent `BuildingId`.
- Observing physical [[world-items|`WorldItem`]]s in the building's footprint at 2 Hz and replicating per-requirement progress to all clients (purely observational — never consumes).
- Exposing the player-clickable interaction surface that queues `CharacterAction_FinishConstruction` for any character standing in `BuildingZone`. Tap-E and hold-E both land here; both routes pass through `Building.RequestStartFinishConstructionServerRpc` to keep client-side dispatch identical.
- Running the continuous server-authoritative consumption loop: per-tick, consume up to (1 + builderSkill/N) items per pending requirement, despawn matching `WorldItem`s, advance progress.
- Calling `Building.Finalize()` when progress hits 1: state-flip-first, then visual swap, default-furniture spawn, leftover eviction to the perimeter.
- Persisting `ConstructionProgress` + per-requirement `DeliveredMaterials` snapshots through [[save-load]] for hibernation pre-warm.

**Non-responsibilities:**
- Does **not** own the `BuildingState` enum or `_currentState` NetworkVariable — those live on [[building]].
- Does **not** own placement validation, range checks, ghost visuals — see [[building-placement-manager]].
- Does **not** own `WorldItem` lifecycle — see [[world-items]].
- Does **not** model offline construction progress (intentionally excluded — construction needs a live map).
- Does **not** issue jobs or schedule NPC labor — Phase 2 (`JobBuilder` GOAP).

## Key classes / files

| File | Role |
|------|------|
| [Building.cs](../../Assets/Scripts/World/Buildings/Building.cs) | Hosts construction NetworkVariables, `Finalize()`, `EvictLeftoversToPerimeter()`, `ComputeProgress()`, `GetPhysicalItemsInCollider()`, `_constructionVisualRoot` / `_completedVisualRoot` SerializeFields. |
| [DeliveredMaterialEntry.cs](../../Assets/Scripts/World/Buildings/Construction/DeliveredMaterialEntry.cs) | `INetworkSerializable` struct: `(int RequirementIndex, int Delivered)`. Replicated via `NetworkList<DeliveredMaterialEntry>`. |
| [DeliveredMaterialEntryDTO.cs](../../Assets/Scripts/World/Buildings/Construction/DeliveredMaterialEntryDTO.cs) | Save twin: `(string ItemAssetGuid, int Delivered)` — references `ItemSO` by AssetGuid so the snapshot survives requirement-list reorders. |
| [ConstructionSiteScanner.cs](../../Assets/Scripts/World/Buildings/Construction/ConstructionSiteScanner.cs) | `[RequireComponent(Building)]` — server-only 2 Hz observational scan of `BuildingZone`, updates `ConstructionProgress` + `DeliveredMaterials`. Reuses `_scratchItems` + `_bucketCache` (zero alloc per tick — Rule #34). |
| [BuildingInteractable.cs](../../Assets/Scripts/World/Buildings/Construction/BuildingInteractable.cs) | `[RequireComponent(Building)]` — player-facing interactable surface. Exposes `GetAvailableInteractions(actor, result)`, `IsOwner(actor)`, `TryQueueInteraction(InteractionId, actor)`. Phase 1 only `FinishConstruction`. |
| [CharacterAction_Continuous.cs](../../Assets/Scripts/Character/CharacterActions/CharacterAction_Continuous.cs) | Abstract base for condition-terminated actions. `OnTick()` returns true to finish. `OnApplyEffect` sealed to no-op. Default `AllowsMovementDuringAction = false`. |
| [CharacterAction_FinishConstruction.cs](../../Assets/Scripts/Character/CharacterActions/CharacterAction_FinishConstruction.cs) | Concrete continuous action. Owner-gated, server-only consumption. 1 Hz default, 5-tick stall timeout. |
| [CharacterActions.cs](../../Assets/Scripts/Character/CharacterActions/CharacterActions.cs) | Adds `ActionContinuousTickRoutine` dispatch branch — must come **before** the `Duration <= 0` instant branch. |
| [Character.cs](../../Assets/Scripts/Character/Character.cs) | Adds `GetSkillLevelOrZero(SkillId)` Phase 1 stub returning 0 (future builder skill plug-in). |
| [SkillId.cs](../../Assets/Scripts/Character/Skills/SkillId.cs) | Enum keying the future skill system. Currently only `Builder` is used. |
| [MapRegistry.cs](../../Assets/Scripts/World/MapSystem/MapRegistry.cs) | `BuildingSaveData` extended with `ConstructionProgress` + `DeliveredMaterials` list (DTO form). `FromBuilding` populates them (editor-only AssetGuid resolution). |
| [PlayerController.cs](../../Assets/Scripts/Character/CharacterControllers/PlayerController.cs) | Adds click-on-`BuildingInteractable` routing per Rule #33. |
| [BuildingInspectorView.cs](../../Assets/Scripts/Debug/DevMode/Inspect/BuildingInspectorView.cs) | Dev panel: live progress, per-requirement delivered breakdown, owner display, **Force Finish** button. |

## Public API / entry points

### On `Building` (server + client read)
- `NetworkVariable<float> ConstructionProgress` — Read=Everyone, Write=Server. Updates only when delta > 0.001f.
- `NetworkList<DeliveredMaterialEntry> DeliveredMaterials` — Read=Everyone, Write=Server. Per-requirement-index delivered counts.
- `bool IsUnderConstruction` — `_currentState.Value == UnderConstruction`.
- `IReadOnlyList<CraftingIngredient> ConstructionRequirements` — read-only view of `_constructionRequirements`.
- `Collider BuildingZone` — public accessor for the footprint collider (also the construction drop zone).

### On `Building` (server-only)
- `float ComputeProgress()` — recompute from `ContributedMaterials` (server-only ledger) against requirements.
- `void Finalize()` — state-flip-first finalization. Order: `_currentState.Value = Complete` → `ConstructionProgress.Value = 1f` → visual swap (via `HandleStateChanged` on every peer) → `TrySpawnDefaultFurniture` → `EvictLeftoversToPerimeter` → `OnConstructionComplete?.Invoke()`. Each post-flip step in `try/catch` (Rule #31). **Note: shadows `object.Finalize` (the GC finalizer hook); declared `public new void Finalize()`.**
- `void EvictLeftoversToPerimeter()` — repositions remaining `WorldItem`s to NavMesh-valid points just outside `_buildingZone` (uses `NavMesh.SamplePosition`; falls back to free-fall on miss).
- `List<WorldItem> GetPhysicalItemsInCollider(Collider, List<WorldItem>)` — caller-supplied buffer for zero-alloc reuse (Rule #34).
- `void ContributeMaterial(ItemSO, int)` — existing; bumps the server-only `_contributedMaterials` ledger. Now called from `CharacterAction_FinishConstruction.OnTick`.

### On `BuildingInteractable` (extends `InteractableObject`)
- `void Interact(Character actor)` — tap-E entry. Cooperative: any character with the building in their interaction zone can drive the action. Server-relays via `Building.RequestStartFinishConstructionServerRpc` (legacy `[ServerRpc(RequireOwnership=false)]` — required because the Building NetworkObject is server-owned, so any client invoking this is by definition not the owner).
- `List<InteractionOption> GetHoldInteractionOptions(Character actor)` — hold-E menu. Phase 1: returns `Finish Construction` while `UnderConstruction` (same target as tap-E — provides discoverability). Phase 2 stub seats: `Abandon`, `Sell`, `OpenInterior`.
- `bool IsCharacterInInteractionZone(Character actor)` — overrides the base AABB check with a **2D X-Z footprint test** (drops Y axis). The 3D `Bounds.Contains` false-negatived on the server-replicated transform when the character's Y rounded to `bounds.min.y` but the actual float was just below it (NavMesh agent height / floor offset / `NetworkTransform` precision). Both client and server use the same 2D check so they stay in sync.
- `bool IsOwner(Character actor)` — `actor.CharacterId == building.PlacedByCharacterId`. **Phase 1 does NOT call this for finalize-gating** — kept for Phase 2 hold-menu options (Abandon, Sell) that need owner identity.

### On `Character` (Phase 1 stub)
- `int GetSkillLevelOrZero(SkillId)` — returns 0 in Phase 1 (so consume budget = 1). Becomes the integration point when `BuilderSkill` lands.

## Data flow

### Placement → UnderConstruction

```
Player clicks placement → BuildingPlacementManager.RequestPlacementServerRpc
  → server Instantiate(prefab) + NetworkObject.Spawn
  → Building.Awake: _constructionRequirements.Count > 0 ?
      → _currentState.Value = UnderConstruction
      → HandleStateChanged on every peer:
           _constructionVisualRoot.SetActive(true)
           _completedVisualRoot.SetActive(false)
      → TrySpawnDefaultFurniture DEFERRED (early-exit while UnderConstruction)
    else → _currentState.Value = Complete (instant — preserves legacy)
  → MapController registration + permit consumption (unchanged)
```

### Delivery (player drops items into footprint)

```
Owner walks up, drops item via existing CharacterAction_DropItem
  → WorldItem spawns inside _buildingZone bounds
  → ConstructionSiteScanner.Tick (2 Hz, server-only):
      items = building.GetPhysicalItemsInCollider(_buildingZone, _scratchItems)
      bucket by ItemSO; clamp delivered[i] = min(bucket[req.Item], req.Amount)
      UpsertDeliveredEntry into DeliveredMaterials (only on change)
      Recompute progress; write ConstructionProgress (only when delta > 0.001f)
  → Clients see meter update via NetworkVariable.OnValueChanged + NetworkList.OnListChanged
```

The scanner is **purely observational** — never consumes. It exists so the owner can see meter feedback while delivering, *before* engaging the action.

### Construct action (continuous, cooperative)

```
Any character in BuildingZone presses E → BuildingInteractable.Interact(actor)
  → Building.RequestStartFinishConstructionServerRpc(NetworkBehaviourReference(actor))
       (legacy [ServerRpc(RequireOwnership=false)] — host short-circuits to direct call;
        client→host transport via NGO RPC dispatch.)
  → server: new CharacterAction_FinishConstruction(actor, building)
  → CharacterActions.ExecuteAction(action) — broadcasts visual proxy to all peers
       with Duration=600s sentinel (continuous actions don't have a real duration)
  → ActionContinuousTickRoutine (1 Hz default)

Per OnTick (server-side):
  Re-validate every tick: state == UnderConstruction, position inside BuildingZone.
  No ownership check (cooperative model). Invalid → return true (action ends).

  budget = 1 + actor.GetSkillLevelOrZero(SkillId.Builder) / SkillBudgetDivisor
  for each pending requirement (deterministic order):
    take = min(remaining, budget)
    fromZone = ConsumeFromZone(_buildingZone, req.Item, take)
                 — despawns matching WorldItems by NetworkObject.Despawn(true)
    fromInv  = ConsumeFromActorInventory(actor, req.Item, take - fromZone)
                 — Phase 1 stub returns 0
    Building.ContributeMaterial(req.Item, fromZone + fromInv)
    budget -= consumed

  progress = Building.ComputeProgress()
  if (|progress - ConstructionProgress.Value| > 0.001f)
      ConstructionProgress.Value = clamp01(progress)

  if (progress >= 1f) → Building.Finalize() → return true
  if (consumed nothing this tick) → stallTicks++; if (stallTicks >= MaxStallTicks=5) → return true
  else → return false (keep ticking)

OnCancel (movement / combat / damage / order change / hibernation):
  No rollback — already-consumed credits stay locked. Any character re-engages by re-clicking.
  Server broadcasts CancelActionVisualsClientRpc so the 600s sentinel proxy on every peer
  is torn down immediately — without this the visual would linger for 600s waiting on the
  unreached "natural duration" timeout.
```

`AllowsMovementDuringAction = false` (default) → any movement intent cancels via `CharacterGameController`.

### Finalize → Complete (server)

```
Building.Finalize() — state-flip FIRST:
  1. _currentState.Value = Complete                  (atomic; replicates via NV)
  2. ConstructionProgress.Value = 1f
  3. HandleStateChanged on every peer:
       _constructionVisualRoot.SetActive(false)
       _completedVisualRoot.SetActive(true)
  4. TrySpawnDefaultFurniture (server-only; gated on Complete)
  5. EvictLeftoversToPerimeter (server-only):
       leftovers = GetPhysicalItemsInCollider(_buildingZone)
       foreach (item):
         eject = NearestPerimeterPoint(_buildingZone, item.position) + outwardNormal * 0.5
         if (NavMesh.SamplePosition(eject, out hit, 2f, AllAreas))
             item.transform.position = hit.position
         else
             item.transform.position = eject  (free-fall fallback)
       Each eject in try/catch — one failure can't block the rest.
  6. OnConstructionComplete?.Invoke()
```

**Crash safety:** if the server crashes between step 1 and step 5, the building remains `Complete` on next load with a few items still on the footprint — harmless. There is never a "paid but no building" failure mode.

## Dependencies

### Upstream
- [[building]] — owns `_currentState`, `_constructionRequirements`, `_buildingZone`, `_deliveryZone`, `_contributedMaterials` ledger, `HandleStateChanged`, `TrySpawnDefaultFurniture`. The construction loop adds new methods/NetworkVariables but never touches the placement or interior pipelines.
- [[character]] — `CharacterAction_Continuous` extends `CharacterAction`. Action queues through `CharacterActions.ExecuteAction`. `Character.GetSkillLevelOrZero` is the future builder-skill hook.
- [[items]] — `WorldItem` is the physical drop. `ItemSO` keys the requirement match.
- [[network]] — `NetworkVariable<float>` + `NetworkList<DeliveredMaterialEntry>` replicate the meter. State flip is server-authoritative; clients read only. Late-join self-heals via the spawn payload.
- [[save-load]] — `BuildingSaveData` extension for hibernation pre-warm.

### Downstream
- [[building]] — consumes `Finalize()` indirectly via `_currentState.OnValueChanged → HandleStateChanged`.

## State & persistence

### Runtime state
- **Replicated:** `_currentState`, `ConstructionProgress`, `DeliveredMaterials`. Read=Everyone, Write=Server.
- **Server-only:** `_contributedMaterials` (the actual ledger), scanner tick state, action stall counter.
- **Per-action transient:** `CharacterAction_FinishConstruction._stallTicks`, `_scratch` buffer.

### Persisted state (`BuildingSaveData` extension)
```
BuildingSaveData
├─ ConstructionProgress : float                          [NEW]
└─ DeliveredMaterials   : List<DeliveredMaterialEntryDTO> [NEW]
                          { string ItemAssetGuid, int Delivered }
```

- **AssetGuid not requirement index** — survives designer-time edits to `_constructionRequirements` order.
- **Editor-only AssetGuid resolution** — `AssetDatabase.AssetPathToGUID` is `UNITY_EDITOR`-gated. In a built player, the save writes empty GUIDs. Phase 1 hibernation pre-warm only matters in-editor; the next scanner tick after wake authoritatively recomputes the meter from physical `WorldItem`s.
- **The snapshot is a UX pre-warm** — so the meter doesn't blink to 0 between map-wake and the next scanner tick. The next scanner tick is the source of truth and overwrites it.
- **`WorldItem`s in the footprint** — persist via the existing world-item save pipeline; no new code.
- **`CharacterAction_FinishConstruction` does NOT persist** — by design. Save mid-action and the action is gone on reload; player re-engages.
- **Refresh-path persistence (2026-05-07 fix `ff98c2b7`)** — `MapController.SnapshotActiveBuildings` (manual save) and `MapController.Hibernate` (player-leaves wake-cycle) both walk the registered building list and refresh existing `BuildingSaveData` entries from the live `Building`. Both paths now copy `ConstructionProgress` AND `DeliveredMaterials` from the refreshed entry; without this, mid-build progress reset to 0 on every save/load cycle even though `BuildingSaveData.FromBuilding` populated them correctly on first capture.
- **`_spawnAsComplete` designer checkbox** — `Building._spawnAsComplete : bool` SerializeField. When true, `OnNetworkSpawn` flips state directly to `Complete` regardless of `_constructionRequirements` content, skipping the scaffold visual and progress meter. Use for scene-authored buildings that should ship as already-built environment (player home, NPC shops, tutorial structures). Empty `_constructionRequirements` already auto-promotes to Complete; the checkbox is for prefabs that DO have requirements but don't want to load as scaffolds.

### Player profile
No new typed save data on the character side. Ownership lives on `Building.PlacedByCharacterId` and round-trips through `BuildingSaveData`. Rule #20 satisfied — nothing required for portable character profiles.

## Known gotchas / edge cases

- **`CraftingIngredient` is a struct** — `req == null` does **not compile**. Only `req.Item` can be null. The scanner and the action both check `req.Item == null`.
- **`WorldItem` is non-stacking** — every `WorldItem` instance counts as **1 unit** toward a requirement. Bucketing in the scanner increments by 1 per instance; `ConsumeFromZone` despawns `take` instances.
- **Scanner tick rate is 2 Hz; action tick rate is 1 Hz** — independent. Pre-action meter updates at 2 Hz; once the action runs, consumption updates appear at 1 Hz.
- **The 2 Hz scanner is purely observational** — it never consumes. Only `CharacterAction_FinishConstruction.OnTick` consumes.
- **Theft remains possible** — `WorldItem`s in `_buildingZone` are normal interactable items. A thief can steal items the owner has not yet consumed. Each tick the owner converts items into permanent progress; the thief race shrinks per tick. `BuilderSkill` raising consume budget lets the owner win the race faster.
- **Phase 1 is cooperative finalize — no owner gate.** The placer-only gate originally proposed in the 2026-05-06 design spec was dropped during PlayMode-MP testing because it blocked co-op partners from helping. `BuildingInteractable.IsOwner` survives but is reserved for Phase 2 hold-menu options (Abandon, Sell) that need owner identity. **Spatial gate stays** (Core Rule #1) — actor must be inside `BuildingZone`.
- **2D X-Z proximity check** — `BuildingInteractable.IsCharacterInInteractionZone` and `CharacterAction_FinishConstruction.IsActorInsideBuildingZone` both drop the Y axis when testing containment. 3D `Bounds.Contains` false-negatived on the server-replicated transform when the character's Y rounded to `bounds.min.y` but the actual float was just below it (NavMesh agent height / floor offset / `NetworkTransform` precision). Both client and server use the same 2D check so they stay in sync.
- **Continuous action visual proxy uses a 600s sentinel duration** — `CharacterActions.ExecuteAction` calls `BroadcastActionVisualsClientRpc(duration=600f)` for `CharacterAction_Continuous`, because continuous actions don't have a real duration. On every peer the proxy ticks until cancellation. Server broadcasts `CancelActionVisualsClientRpc` when the action ends (Finalize, stall timeout, manual cancel) so peers tear down the proxy immediately — without this the proxy would linger for 600s.
- **HUD progress bar reads `Progress`, not `Duration`** — `CharacterAction_Continuous.Progress` is a virtual getter (default 0). `CharacterAction_FinishConstruction.Progress` returns `Building.ConstructionProgress.Value`. `CharacterActions.GetActionProgress` checks the override before falling back to `elapsed/duration` — for continuous actions the duration math would divide by 0 (or by the 600s sentinel, both wrong).
- **`[ServerRpc(RequireOwnership=false)]` legacy attribute** — `Building.RequestStartFinishConstructionServerRpc` uses the old `[ServerRpc]` form (not `[Rpc(SendTo.Server)]`) because the Building NetworkObject is server-owned. Any client invoking it is by definition not the owner; `RequireOwnership=false` is the standard escape. Method name MUST end in `ServerRpc` for the legacy attribute to dispatch.
- **`Building.Finalize()` shadows `object.Finalize`** — declared `public new void Finalize()`. The GC finalizer slot is untouched (Building has no `~Building()`). Don't add one without renaming.
- **Default furniture is deferred until `Complete`** — `TrySpawnDefaultFurniture` early-exits during `UnderConstruction`. The state-change handler invokes it once on the transition.
- **State-flip-first ordering** — `Building.Finalize` writes `_currentState.Value = Complete` BEFORE running side-effects. A crash mid-finalize leaves a Complete building with possibly-un-evicted items, never "paid but no building."
- **Continuous action dispatch ordering** — in `CharacterActions.ExecuteAction`, the `is CharacterAction_Continuous` check must come BEFORE the `Duration <= 0` branch. The base ctor passes `duration: 0f`, so a Continuous would be misidentified as instantaneous if the order flipped.
- **`AllowsMovementDuringAction = false`** is inherited from `CharacterAction` — so player WASD or NPC re-route cancels mid-action. Override only when the action drives its own movement (chase, follow).
- **The construction visual must NOT block pedestrian traffic** — designers should use scaffold sprites without a `NavMeshObstacle` carve, so the owner can walk in to drop items.
- **Items in `_buildingZone` are checked via `bounds.Contains` on the BoxCollider** — make sure the collider is non-trigger and large enough to cover the entire scaffolded outline.
- **Rule #18 server authority** — every state mutation, item despawn, side-effect runs on the server. Clients never call `NetworkObject.Despawn` directly.
- **Rule #19 multiplayer matrix** — validated for Host↔Client, Client↔Client, late-join. NPC parity (Rule #22) is Phase 2 — same `CharacterAction` path, same spatial gate (zone containment); the cooperative model means NPCs need no extra owner-bypass when JobBuilder lands.
- **Rule #34 perf** — scanner reuses `_scratchItems` (List) + `_bucketCache` (Dict); action reuses `_scratch` (List). `GetPhysicalItemsInCollider` accepts a caller-supplied buffer. Profiler-checked at 10 simultaneous sites < 0.1 ms/frame.
- **Rule #28 SKILL.md update** — the `building_system` and `character_core` SKILL files were extended (no separate `construction` skill — the building skill section is the procedural how-to source).
- **Curtain ParticleSystem feature is currently DISABLED for standalone build stability (2026-05-16).** `Building._constructionCurtainMaterial` is `{fileID: 0}` on all 7 networked building prefabs. The shared `BuildingCurtainSettings.asset` + `BuildingCurtainSettingsHolder` SO+Holder pattern is intact, but `EnsureConstructionCurtainParticles` will null-skip the particle setup because the material reference is null. The perimeter wall + curtain visuals are not rendered while UnderConstruction. The footprint marker still renders. **Root cause + restore protocol:** see [[material-buildproperties-standalone-crash]]. Restoring the feature requires rebuilding `Assets/Materials/M_ConstructionCurtain.mat` from scratch (don't copy the old YAML) and re-wiring `_constructionCurtainMaterial` on each of the 7 prefabs. **Test on a standalone build before merging** — the broken material is what crashed the standalone player natively during scene load.

## Open questions / TODO

Phase 2 will resolve:

- [ ] **NPC owner autonomy** — free-time GOAP goal, perception "find harvestable producing item X", shop search.
- [ ] **Community-manager city-management console** — issuing builds, transferring abandoned sites.
- [ ] **`JobBuilder` GOAP job class** — autonomous worker labour. Cooperative model means no owner-bypass code is needed; the BT just needs to navigate to `BuildingZone` and trigger the same action path players use.
- [ ] **Hold-menu owner-only options** — `Abandon` / `Sell` / `OpenInterior` will gate on `BuildingInteractable.IsOwner`. The infrastructure is already on disk; only the action implementations and UI wiring are pending.
- [ ] **Auto-eviction of orphaned construction sites** — owner deleted profile.
- [ ] **Real `ConsumeFromActorInventory`** — Phase 1 stub returns 0; will pull from `CharacterEquipment` once PlayMode-MP confirms the zone path works end-to-end.
- [ ] **`BuilderSkill` system landing** — `Character.GetSkillLevelOrZero(SkillId.Builder)` becomes meaningful; `SkillBudgetDivisor` becomes a tunable. An optional second knob ("reduce required count" multiplier) is also seated.
- [ ] **Production-build save** — AssetGuid resolution is editor-only. For a built-player save, replace with an `ItemSO.ItemId` key (already used by `WorldItem.ApplyNetworkData` and `StorageFurnitureSaveEntry`).

## Change log
- 2026-05-16 — Curtain ParticleSystem feature **DISABLED** for standalone build stability. `Building._constructionCurtainMaterial` set to `{fileID: 0}` on the 7 networked building prefabs (Forge / Farming Building / Lumberyard / Shop / Transporter Building / House prefab / Small house). Root cause: `Assets/Materials/M_ConstructionCurtain.mat` (URP Particles/Unlit, added in `f5abe296`) crashed the standalone Mono build natively in `Material::BuildProperties → ShaderPropertySheet::UpdateTextureInfo` during scene load — Editor tolerated, build segfaulted before any managed code ran. Cutting the build's reference path was the sidestep; exact corruption in the .mat (likely `m_InvalidKeywords: [_FLIPBOOKBLENDING_OFF]` or `_CameraFadeParams: {r: 0, g: Infinity, b: 0, a: 0}`) was never pinpointed. **To restore the feature**: recreate `M_ConstructionCurtain.mat` from scratch via right-click → Create → Material → URP Particles/Unlit shader → configure cleanly via Inspector, re-wire on each Building prefab's `_constructionCurtainMaterial` field, and verify a standalone build doesn't crash before merging. See [[material-buildproperties-standalone-crash]] for full incident report + diagnostic protocol. The construction-curtain SO+Holder pattern (`BuildingCurtainSettings.asset` / `BuildingCurtainSettingsHolder.cs`) is untouched and ready for the rebuilt material. — claude / [[kevin]]
- 2026-05-07 — Phase 1 PlayMode-MP polish landed. (1) Cooperative model — placer-only finalize gate dropped (`0f3337ce`); any character in `BuildingZone` can drive the action. `BuildingInteractable.IsOwner` reserved for Phase 2 hold-menu. (2) 2D X-Z proximity check (`9fadc3bd`) — Y axis dropped on both client and server because `Bounds.Contains` was false-negativing on `NetworkTransform`-replicated Y precision. (3) `[ServerRpc(RequireOwnership=false)]` legacy attribute (`14e54d1c`) — chosen over `[Rpc(SendTo.Server)]` because Building NetworkObject is server-owned and clients are by definition not the owner. (4) Continuous-action visual proxy uses 600s sentinel + `CancelActionVisualsClientRpc` on finish (`5d1594e6`) — without the cancel broadcast, the proxy lingers 600s after the server-side action ends. (5) HUD progress bar reads new `Progress` virtual on `CharacterAction_Continuous` (override returns `Building.ConstructionProgress.Value`) — fallback to `elapsed/duration` would div-by-0 on continuous actions. (6) Save/load progress restoration (`ff98c2b7`) — `MapController.SnapshotActiveBuildings` and `Hibernate` refresh paths now copy `ConstructionProgress` + `DeliveredMaterials`; mid-build progress was resetting to 0 on every save/load cycle. (7) `_spawnAsComplete` designer checkbox (`d0ced22d`) — scene-authored buildings can ship as already-built without falling through the scaffold path. (8) Diag-log cleanup (`d9b602f6`) — stripped magenta info logs from `BuildingInteractable.Interact` / `Building.RequestStartFinishConstructionServerRpc` / `CharacterAction_FinishConstruction.IsActorInsideBuildingZone` (last one was the real Rule #34 hit — fired every tick per active builder). — claude / [[kevin]]
- 2026-05-06 — Initial documentation pass for Phase 1 construction loop. — claude / [[kevin]]

## Sources
- [docs/superpowers/specs/2026-05-06-building-construction-loop-design.md](../../docs/superpowers/specs/2026-05-06-building-construction-loop-design.md) — Phase 1 design spec.
- [docs/superpowers/plans/2026-05-06-building-construction-loop.md](../../docs/superpowers/plans/2026-05-06-building-construction-loop.md) — Phase 1 implementation plan.
- [.agent/skills/building_system/SKILL.md](../../.agent/skills/building_system/SKILL.md) — "Construction Loop (Phase 1)" section: procedural authoring how-to.
- [.agent/skills/character_core/SKILL.md](../../.agent/skills/character_core/SKILL.md) — `CharacterAction_Continuous` contract.
- [.claude/agents/building-furniture-specialist.md](../../.claude/agents/building-furniture-specialist.md)
- [.claude/agents/character-system-specialist.md](../../.claude/agents/character-system-specialist.md)
- `Assets/Scripts/World/Buildings/Construction/` — scanner, interactable, DTO struct + save twin.
- `Assets/Scripts/Character/CharacterActions/CharacterAction_Continuous.cs` + `CharacterAction_FinishConstruction.cs`.
- 2026-05-06 conversation with [[kevin]].
