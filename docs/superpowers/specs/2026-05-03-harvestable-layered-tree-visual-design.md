---
title: Harvestable Layered Tree Visual
date: 2026-05-03
status: approved
owner: claude / kevin
related:
  - Assets/Scripts/Interactable/Harvestable.cs
  - Assets/Scripts/Interactable/HarvestableNetSync.cs
  - Assets/Scripts/Interactable/Pure/HarvestableSO.cs
  - Assets/Scripts/DayNightCycle/TimeManager.cs
  - wiki/systems/harvestable.md
---

# Harvestable Layered Tree Visual

## Goal

Replace the single-sprite tree visual with a **3-layer composition** so trees can express:

1. A static **trunk / branch** silhouette (always visible).
2. A **foliage** layer whose colour shifts continuously over the in-game year (spring green → summer dark green → autumn orange → winter bare).
3. A set of **randomised harvestable overlays** (e.g. apples on an apple tree) — **one sprite per remaining harvestable**, positioned procedurally inside the foliage area, that disappear one-by-one as the tree is harvested.

The system must (a) be deterministic across all networked peers, (b) cost zero per-frame work in steady state, (c) coexist with the existing `Harvestable.ApplyVisual()` growth-stage scale lerp, and (d) require no changes to non-tree harvestables (rocks, ore veins, plain berries).

## Non-goals (v1)

- **Runtime-spawned trees.** Scene-authored only. A future `TreeRegistry` (mirror of `CropRegistry`) is required before runtime spawning can resolve the SO on clients — not in this cycle.
- **Discrete `Season` enum.** Deferred per the farming spec. Year-progress is sampled as a continuous `[0..1]` from `TimeManager.CurrentYearProgress01`. Adding the enum later is purely additive.
- **Per-leaf wind animation, particle drops, fruit-fall physics.** Out of scope.
- **Foliage sprite swap (e.g. "bare branches in winter").** Implementable today by authoring the gradient with `Color.clear` at the winter end (alpha = 0 hides foliage). A dedicated swap-sprite path can be added later if the gradient approach proves insufficient.

## Architecture

### New types

**`TreeHarvestableSO : HarvestableSO`** (in `MWI.Interactables.Pure` asmdef, alongside `HarvestableSO`).

| Field | Type | Notes |
|---|---|---|
| `_trunkSprite` | `Sprite` | Static, never tinted. |
| `_foliageSprite` | `Sprite` | Single sprite, MPB-tinted by gradient. Null = no foliage renderer (dead tree). |
| `_foliageColorOverYear` | `Gradient` | Sampled by `CurrentYearProgress01`. Author with `Color.clear` at any season for "leafless winter". |
| `_fruitSpriteVariants` | `Sprite[]` | Random pick per spawned fruit. Empty = no fruit. |
| `_fruitSpawnArea` | `Rect` | Local-space rect (in foliage's coordinate frame) where fruits may spawn. `Rect.zero` = use the foliage sprite's bounds. |
| `_fruitScale` | `Vector2` | Optional scale applied to each spawned fruit sprite. Default `(1,1)`. |

Public read-only accessors mirror the field set. Identity, harvest outputs, depletion, destruction, etc. inherit from `HarvestableSO` unchanged.

**`HarvestableLayeredVisual : MonoBehaviour`** (Assembly-CSharp, sibling on the tree prefab).

| Member | Purpose |
|---|---|
| `[SerializeField] SpriteRenderer _trunkRenderer` | Hand-wired in prefab. |
| `[SerializeField] SpriteRenderer _foliageRenderer` | Hand-wired in prefab. |
| `[SerializeField] Transform _fruitContainer` | Empty Transform child; fruits are instantiated under it. Local-space rect on the SO is interpreted in this transform's frame. |
| `List<SpriteRenderer> _fruitInstances` | Runtime-built at spawn; one entry per `_maxHarvestCount`. |
| `Harvestable _harvestable` | `GetComponent` in `Awake`. |
| `HarvestableNetSync _netSync` | `GetComponent` in `Awake`. |
| `MaterialPropertyBlock _mpb` | Reused. |
| `void RefreshAll()` | Drives all 3 layers. |
| `void RefreshFoliageColor()` | Sample gradient → MPB. |
| `void RefreshFruitVisibility()` | Walk `_fruitInstances`, enable iff `i < visibleCount`. |
| `void SpawnFruits()` | Procedurally instantiate `_maxHarvestCount` fruit renderers at deterministic random positions inside `_fruitSpawnArea`. |
| `void OnNetworkSpawn()` | One-shot init: sprites, fruit spawn, subscribe events, first refresh. |
| `void OnNetworkDespawn()` | Unsubscribe. |

### Existing types touched

**`TimeManager`**
- Add `[SerializeField] int _daysPerYear = 28;`
- Add `public int DaysPerYear => _daysPerYear;`
- Add `public float CurrentYearProgress01 => _daysPerYear > 0 ? ((CurrentDay - 1) % _daysPerYear) / (float)_daysPerYear : 0f;`

**`HarvestableNetSync`**
- Add `public NetworkVariable<byte> RemainingYield = new(...);` with server-write/everyone-read perms (mirroring the existing NetVar setup).
- Hook its `OnValueChanged` to call `Harvestable.OnNetSyncChanged()` (existing bridge).

**`Harvestable`**
- In `Harvest()` after `_currentHarvestCount++`, push `_netSync.RemainingYield.Value = (byte)Mathf.Min(255, RemainingYield)`.
- In `ResetHarvestState()` and `Refill()`, push `_netSync.RemainingYield.Value = (byte)_maxHarvestCount` so post-respawn clients see the full set.
- Fire `OnStateChanged` (existing) — `HarvestableLayeredVisual` listens for the depletion / refill broadcasts.

No other changes to `Harvestable`.

## Data flow

### On spawn (client + server)

1. `OnNetworkSpawn` runs.
2. Read `_harvestable.SO as TreeHarvestableSO`. Bail (disable component) if null.
3. Assign `_trunkSprite` / `_foliageSprite` to the renderers once.
4. **Deterministic fruit spawn (`SpawnFruits`)**:
   - Capture `Random.state`, call `Random.InitState((int)NetworkObject.NetworkObjectId)`.
   - Resolve `Rect area = _so.FruitSpawnArea == Rect.zero ? _foliageRenderer.sprite.bounds.AsRect() : _so.FruitSpawnArea`.
   - For `i in 0.._harvestable.MaxHarvestCount`:
     - `var go = new GameObject($"Fruit{i}");`
     - `go.transform.SetParent(_fruitContainer, worldPositionStays: false);`
     - `go.transform.localPosition = new Vector3(Random.Range(area.xMin, area.xMax), Random.Range(area.yMin, area.yMax), -0.001f * (i + 1));`
     - `go.transform.localScale = (Vector3)_so.FruitScale + Vector3.forward;`
     - `var sr = go.AddComponent<SpriteRenderer>();`
     - `sr.sprite = _fruitSpriteVariants[Random.Range(0, _fruitSpriteVariants.Length)];`
     - `sr.sortingOrder = 2;`
     - `_fruitInstances.Add(sr);`
   - Restore `Random.state`.
5. Subscribe `TimeManager.OnNewDay → RefreshFoliageColor`, `Harvestable.OnStateChanged → RefreshFruitVisibility`, `_netSync.RemainingYield.OnValueChanged → (_,_) => RefreshFruitVisibility()`.
6. `RefreshAll()`.

### On day change

`TimeManager.OnNewDay` → `RefreshFoliageColor`:
- `var color = _so.FoliageColorOverYear.Evaluate(TimeManager.Instance.CurrentYearProgress01);`
- `_foliageRenderer.GetPropertyBlock(_mpb); _mpb.SetColor("_Color", color); _foliageRenderer.SetPropertyBlock(_mpb);`

### On harvest / depletion / refill

Either `OnStateChanged` or `RemainingYield.OnValueChanged` → `RefreshFruitVisibility` (both can fire on the same harvest tick — the method is idempotent so double-fire is harmless):
- `int visible = _harvestable.IsDepleted ? 0 : _netSync.RemainingYield.Value;`
- `for (int i = 0; i < _fruitInstances.Count; i++) _fruitInstances[i].enabled = i < visible;`

### During growth (pre-mature)

`Harvestable.ApplyVisual()` already lerps `transform.localScale` from 0.25× → 1×. The trunk, foliage, and `FruitContainer` ride along automatically. `RefreshFruitVisibility` additionally hides all fruit instances while `stage < mature` (read via `_netSync.CurrentStage` + `crop.DaysToMature` if the SO is also a `CropSO`; for non-crop trees, fruit visibility is purely `RemainingYield`-driven).

## Networking matrix

| State | Source of truth | Replication | Client behaviour |
|---|---|---|---|
| Trunk sprite | SO ref baked on prefab | None | Read locally — SO is identical on every peer |
| Foliage sprite | SO ref baked on prefab | None | Read locally |
| Foliage color | `TimeManager.CurrentDay` | TimeManager already syncs day | Sample gradient on `OnNewDay` |
| Fruit visibility | `HarvestableNetSync.RemainingYield` | New `NetworkVariable<byte>` | Walk `_fruitInstances` |
| Fruit count | `Harvestable._maxHarvestCount` (SO-driven) | None | SO is identical on every peer |
| Fruit position + sprite per fruit | Deterministic seed = `NetworkObjectId` | None | Same `Random.Range` sequence on every peer → identical layout |

**Validated against the three player relationships (rule #19):**
- **Host ↔ Client:** Host writes `RemainingYield`; client receives via NGO. Foliage color computed locally on both sides from the same `CurrentDay`. Fruit picks deterministic from `NetworkObjectId` (NGO replicates this). ✓
- **Client ↔ Client:** Both clients receive the same NetVar updates and the same `NetworkObjectId`. ✓
- **Host/Client ↔ NPC:** NPC harvests via `CharacterHarvestAction` → `Harvestable.Harvest` (server-side) → NetVar updates → all peers see fruit disappear. ✓

## Performance (rule #34)

- **Zero per-frame work.** All updates are event-driven: `OnNewDay`, `OnStateChanged`, `RemainingYield.OnValueChanged`.
- **Allocation budget per event:** zero. `Gradient.Evaluate` returns a struct. `MaterialPropertyBlock` is reused. Fruit-instance loop has no allocs.
- **MPB preserves SRP batching** for the foliage renderer (rule #25).
- **OnNewDay cost:** one `Gradient.Evaluate` + one `SetPropertyBlock` per active tree. With ~100 visible trees per map, this is sub-millisecond on day-rollover only.
- **Spawn cost:** one `Random.InitState` + `2N+1` `Random.Range` calls (position x, position y, sprite index per fruit) + N `new GameObject` + N `AddComponent<SpriteRenderer>`. With `_maxHarvestCount` typically 3–8, this is sub-millisecond. For very large `_maxHarvestCount` (>30 — e.g. giant berry bush), revisit pooling.

## Edge cases

- **Empty `_fruitSpriteVariants`** → `SpawnFruits` early-returns; no fruits instantiated.
- **Null `_foliageSprite`** → foliage renderer disabled in spawn init; `RefreshFoliageColor` early-returns; if `_fruitSpawnArea == Rect.zero` we fall back to a tiny default rect (e.g. (-0.5, -0.5, 1, 1)) so fruits still spawn somewhere sensible.
- **`_daysPerYear == 0`** → `CurrentYearProgress01` returns 0; gradient samples its first key. Defensive divide-by-zero guard.
- **Pre-mature crop tree** (e.g. apple tree at growth stage 1/4) → existing scale lerp shrinks the whole composition; fruits are spawned at full count but hidden by `RefreshFruitVisibility` until mature.
- **Fruit overlap.** With purely random placement, fruits can spawn on top of each other. Accepted for v1 — typically 3–8 fruits over a generous foliage area looks fine. Designer can author a wider `_fruitSpawnArea` to spread them. Poisson-disk sampling is a v2 follow-up if clustering becomes a problem.
- **Tree without `HarvestableNetSync`** (server-only static scenery) → `HarvestableLayeredVisual` falls back to reading `_harvestable.RemainingYield` directly and skips the NetVar subscription. Foliage color still works (TimeManager local).

## Authoring workflow

1. Designer creates a `TreeHarvestableSO` asset in `Assets/Resources/Data/Harvestables/Trees/`.
2. Assigns trunk + foliage sprites, paints the year gradient, drops 1–4 fruit sprite variants (or leaves empty for non-fruit trees), optionally tweaks `_fruitSpawnArea` (leave at `Rect.zero` to use foliage bounds) and `_fruitScale`.
3. Duplicates the existing tree prefab template, adds `HarvestableLayeredVisual` to the root, creates `Trunk` / `Foliage` child SpriteRenderers + an empty `FruitContainer` child.
4. Wires the renderer + container fields on `HarvestableLayeredVisual` and assigns the `TreeHarvestableSO` to `Harvestable._so`. Designer sets `Harvestable._maxHarvestCount` to the desired number of visible fruits.
5. Drops the prefab into a scene.

## Open questions

- **Sorting order vs Y-sorting.** The 2D-in-3D rule (#17) means most sprites use Y-position-based sorting. A 3-layer tree probably wants a deterministic relative ordering between trunk / foliage / fruit (foliage always over trunk, fruit always over foliage), not Y-derived. Decision: use explicit `sortingOrder` overrides on the children (0 / 1 / 2) and let the root's Y-position drive the tree-vs-tree sort. Confirm with Kevin during implementation if this looks wrong against existing trees.
- **Fruit anchor sprite tint with foliage gradient?** Apples on a snow-covered tree look weird if they stay bright red against pale foliage. Out of scope for v1 — fruit sprites are static. Easy follow-up: optional per-fruit-variant colour curve.

## Build sequence (preview — full plan in writing-plans output)

1. `TimeManager` — add `_daysPerYear` + `CurrentYearProgress01` (~5 lines).
2. `HarvestableNetSync` — add `RemainingYield : NetworkVariable<byte>` + bridge.
3. `Harvestable` — push `RemainingYield` after each Harvest / Reset / Refill.
4. `TreeHarvestableSO` (new file in `Pure` asmdef).
5. `HarvestableLayeredVisual` (new file in Assembly-CSharp).
6. Prefab work — Tree.prefab variant with the 3-layer hierarchy.
7. Author one `AppleTreeSO` asset for smoke-test.
8. Update `wiki/systems/harvestable.md` change log + add a "Layered tree visual" section.
9. Update `.agent/skills/harvestable-resource-node-specialist/SKILL.md` with the new component.

## Sources

- Procedure: see SKILL.md updates in step 9.
- Wiki: `wiki/systems/harvestable.md`.
