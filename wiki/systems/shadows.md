---
type: system
title: "Shadows"
tags: [rendering, urp, visuals, tier-1]
created: 2026-04-19
updated: 2026-04-19
sources:
  - ".agent/skills/rendering/shadows/SKILL.md"
  - "docs/superpowers/specs/2026-04-19-2d-sprite-cast-shadows-design.md"
related:
  - "[[character]]"
  - "[[character-visuals]]"
  - "[[items]]"
  - "[[building]]"
  - "[[building-interior]]"
status: stable
confidence: high
primary_agent: null
secondary_agents:
  - character-system-specialist
  - building-furniture-specialist
owner_code_path: "Assets/Shaders/Sprite-Lit-ShadowCaster.shader"
depends_on:
  - "[[daynightcycle]]"
  - "[[time-manager]]"
depended_on_by:
  - "[[character-visuals]]"
  - "[[building-interior]]"
---

# Shadows

## Summary
Real URP directional-light cast shadows for every 2D sprite in the 3D world. Sprites are vertical quads with an alpha-tested `ShadowCaster` pass in a custom shader; the project's existing directional sun — driven by [[daynightcycle]] off [[time-manager]] — rotates, and shadows follow automatically. Per-client rendering with no networked surface: `TimeManager.CurrentTime01` is already shared state, so all clients compute the same sun direction and shadow direction is implicitly consistent across Host ↔ Client, Client ↔ Client, and Host/Client ↔ NPC.

## Purpose
Give 2D sprites real depth cues inside the 3D world without resorting to hand-authored blob shadows or baked decals. A single directional light + alpha-tested `ShadowCaster` pass lets sprites read as grounded actors/props during the entire day/night cycle, including dramatic dawn/dusk silhouettes, while costing nothing on the network and scaling from PC to mobile via separate URP asset tiers.

## Responsibilities
- Alpha-tested shadow casting from 2D sprite quads via [Sprite-Lit-ShadowCaster.shader](../../Assets/Shaders/Sprite-Lit-ShadowCaster.shader).
- Opt-in per sprite category: characters, trees/foliage, furniture, runtime-driven items (via `ItemSO.CastsShadow` + `WorldItem.ApplyShadowCastingFromItemSO()`), buildings (ProBuilder meshes on URP Lit).
- Time-of-day coupling via two decoupled `AnimationCurve` fields on `DayNightCycle` (`_intensityCurve`, `_shadowStrengthCurve`) — lets dawn/dusk run dim-sun + dramatic-shadows.
- Interior sun-leak blocking via per-interior `ShadowsOnlyRoof` child quads — composable with any future camera cut-away system.
- Platform-tiered URP configuration (PC vs Mobile) with shadow cascades sized for the ~30u max camera-to-character distance.

**Non-responsibilities** (common misconceptions):
- Not responsible for cloud/weather shadows — separate future spec, `WeatherFront`-driven projector or scrolling decal.
- Not responsible for moonlight shadows at night — would require a second dimmer directional light (future work).
- Not responsible for shadow gameplay mechanics (stealth, visibility) — purely a rendering system.
- Not responsible for the sun's rotation or intensity — that is owned by [[daynightcycle]].

## Key classes / files
| File | Role |
|------|------|
| [Sprite-Lit-ShadowCaster.shader](../../Assets/Shaders/Sprite-Lit-ShadowCaster.shader) | Custom sprite shader with alpha-tested `ShadowCaster` pass — the system's core. |
| [DayNightCycle.cs](../../Assets/Scripts/DayNightCycle/DayNightCycle.cs) | Drives sun rotation + `_intensityCurve` + `_shadowStrengthCurve`. |
| [TimeManager.cs](../../Assets/Scripts/DayNightCycle/TimeManager.cs) | Source of truth for `CurrentTime01` / `CurrentDay` — sun direction input. |
| [PC_RPAsset.asset](../../Assets/Settings/PC_RPAsset.asset) | URP 3D renderer config for PC: 2 cascades, Shadow Distance 80u, 2048 resolution. |
| [Mobile_RPAsset.asset](../../Assets/Settings/Mobile_RPAsset.asset) | URP 3D renderer config for Mobile: 2 cascades, Shadow Distance 80u, 1024 resolution. |
| `WorldItem.ApplyShadowCastingFromItemSO()` | Runtime toggle driven by `ItemSO.CastsShadow` for dropped/placed items. See [[items]]. |
| `ShadowsOnlyRoof` (child quad on interior prefabs) | Blocks sun leak from above without adding visible geometry. See [[building-interior]]. |
| `Spine-Skeleton-Lit-ZWrite.shader` | Already in the project — includes its own `ShadowCaster` pass; used post-[[character-visuals]] Spine migration. |

## Public API / entry points
- **Per-prefab opt-in:** swap material to one using `Sprite-Lit-ShadowCaster.shader` and set `SpriteRenderer.shadowCastingMode = On`.
- **Per-item runtime toggle:** `ItemSO.CastsShadow` (bool, authoring-time) → consumed by `WorldItem.ApplyShadowCastingFromItemSO()` on spawn.
- **Time-of-day tuning:** `DayNightCycle._intensityCurve` and `DayNightCycle._shadowStrengthCurve` `AnimationCurve` fields — decoupled by design so dim-sun + dramatic-shadows golden-hour silhouettes are expressible.
- **Interior roof authoring:** add a `ShadowsOnlyRoof` child quad (MeshRenderer with `shadowCastingMode = ShadowsOnly`) to any interior prefab that must not leak sun from above.
- **URP tuning entry points:** `PC_RPAsset` / `Mobile_RPAsset` — cascades, Shadow Distance, Shadow Resolution, Soft Cascades, Cascade Split.
- **Sun light inspector (on `DayNightCycle` GameObject):** Soft Shadows, `shadowBias ≈ 0`, elevated `normalBias` — compensation for paper-thin sprite quads.

## Data flow

```
TimeManager.CurrentTime01
        │
        ▼
DayNightCycle  ── evaluates _intensityCurve + _shadowStrengthCurve
        │
        ├─► sun.transform.rotation  (same on every client)
        ├─► sun.intensity           (0 below horizon → no shadows at night, free)
        └─► sun.shadowStrength
                │
                ▼
        URP renderer (PC_RPAsset / Mobile_RPAsset)
                │
        ┌───────┴────────────────────────────────┐
        ▼                                        ▼
  ShadowCaster pass on                   Default Lit pass on
  Sprite-Lit-ShadowCaster                ProBuilder building meshes
  (alpha-tested)                         (opaque receivers/casters)
        │
        ▼
  Characters, trees, furniture, WorldItem.CastsShadow=true props
```

All of the above runs locally on each client. No RPC, no NetworkVariable, no server authority. Consistency comes from `TimeManager.CurrentTime01` already being replicated state.

## Dependencies

### Upstream (this system needs)
- [[daynightcycle]] — owns the directional sun light and the intensity + shadow-strength curves.
- [[time-manager]] — owns `CurrentTime01` / `CurrentDay`; single source of truth for sun direction across clients.

### Downstream (systems that need this)
- [[character-visuals]] — `CharacterArchetype` visual prefabs opt in via material swap + `shadowCastingMode = On`. Also the Spine post-migration target (`Spine-Skeleton-Lit-ZWrite.shader` already includes `ShadowCaster`, `ICharacterVisual` is untouched).
- [[building-interior]] — each interior needs a `ShadowsOnlyRoof` child quad to prevent sun leak.
- [[items]] — `ItemSO.CastsShadow` + `WorldItem.ApplyShadowCastingFromItemSO()` drive runtime casting for dropped/placed items.
- [[building]] — ProBuilder meshes are URP-default-Lit shadow receivers by default.

## State & persistence
- **Runtime state:** none — purely render-pipeline state. All inputs (sun rotation, intensity, shadow strength) are re-derived every frame from `TimeManager` via `DayNightCycle`.
- **Persisted state:** none system-specific. `ItemSO.CastsShadow` is authoring-time data persisted with the ScriptableObject asset; URP settings are persisted on the two `_RPAsset.asset` files; per-prefab `shadowCastingMode` is persisted with the prefab.
- **Network replication:** none. `TimeManager.CurrentTime01` is already replicated; shadows are a pure-local rendering consequence of it.

## Known gotchas / edge cases
- **Paper-thin sprite quads have no thickness** — traditional `shadowBias` pushes the shadow off the ground. Keep `shadowBias ≈ 0` and tune `normalBias` upward instead. At extreme near-grazing sun angles this can still surface as shadow detachment — flagged in Open Questions below.
- **Night shadows are free** — `DayNightCycle` zeroes intensity when the sun is below the horizon, and a zero-intensity light casts no shadow. No special night branch needed.
- **Coupled curves would ruin golden hour** — a single curve that multiplied intensity by shadow strength would make dim sun produce dim shadows. The two curves are intentionally independent.
- **Interior sun leak** — without the `ShadowsOnlyRoof` child quad, the directional sun will light interior floors/walls through the missing ceiling. The quad is `ShadowsOnly` so it never renders but still occludes.
- **Cascade split matters more than Shadow Distance** — cascade 0 (≈32u on PC at split 0.4) must cover the ~30u max camera-to-character distance, or characters start sampling from blurrier cascade 1 at normal play range.
- **Mobile at 1024 resolution is visibly softer** — this is intentional; the PC-only 2048 tier keeps desktop crispness without paying for it on mobile.

## Open questions / TODO
- [ ] **Cloud / weather shadows** — future spec. Candidates: `WeatherFront`-driven URP projector, or a scrolling decal. Integration point likely on [[daynightcycle]] or a new `WeatherShadowController`.
- [ ] **Moonlight shadows at night** — requires a second dimmer directional light that activates when the sun goes below horizon. Would double shadow cost at night — verify mobile budget before committing.
- [ ] **Cross-quad tree meshes** — single-quad trees look weak in silhouette at low sun angles (wide canopy reads as a thin slab). Upgrade path: authored 2- or 3-quad cross meshes for wide-canopy trees only.
- [ ] **Normal-bias-by-sun-angle curve** — if near-grazing-sun shadow detachment surfaces in playtesting, drive `normalBias` from a `DayNightCycle` curve rather than a constant.
- [ ] **No matching agent** — neither `character-system-specialist` nor `building-furniture-specialist` fully owns shadows. A dedicated `rendering-specialist` agent may be warranted once this system grows (weather shadows, moonlight, LUT palette swaps).

## Change log
- 2026-04-19 — Initial page created from `.agent/skills/rendering/shadows/SKILL.md` and `docs/superpowers/specs/2026-04-19-2d-sprite-cast-shadows-design.md`. — Claude

## Sources
- Procedure (how to add shadows / configure URP / author interior roofs): [.agent/skills/rendering/shadows/SKILL.md](../../.agent/skills/rendering/shadows/SKILL.md)
- Design spec: [docs/superpowers/specs/2026-04-19-2d-sprite-cast-shadows-design.md](../../docs/superpowers/specs/2026-04-19-2d-sprite-cast-shadows-design.md)
- Shader: [Assets/Shaders/Sprite-Lit-ShadowCaster.shader](../../Assets/Shaders/Sprite-Lit-ShadowCaster.shader)
- Sun driver: [Assets/Scripts/DayNightCycle/DayNightCycle.cs](../../Assets/Scripts/DayNightCycle/DayNightCycle.cs)
- Time source: [Assets/Scripts/DayNightCycle/TimeManager.cs](../../Assets/Scripts/DayNightCycle/TimeManager.cs)
- URP assets: [PC_RPAsset.asset](../../Assets/Settings/PC_RPAsset.asset), [Mobile_RPAsset.asset](../../Assets/Settings/Mobile_RPAsset.asset)
