# 2D Sprite Cast Shadows — Design

**Status:** Draft — awaiting user review
**Date:** 2026-04-19
**Scope:** Real URP cast shadows for all 2D sprites in the 3D world (characters, trees, furniture, props, buildings). Cloud/weather shadows deferred to a follow-up spec.

---

## 1. Goal

Give every 2D sprite a real directional-light cast shadow that matches its silhouette, rotates with the existing `DayNightCycle` sun, and costs little enough to ship with the full Living World entity count.

## 2. Context

- **Render pipeline:** URP 17.3
- **Camera:** perspective, fixed yaw (`Euler(13°, 0°, 0°)`), Y offset 13–18u, Z offset -12.5 to -23.5u. Max camera-to-character ground distance ≈ 30u at full zoom-out.
- **Sprites:** currently `SpriteRenderer`. **Characters will migrate to Spine 2D** (`SkeletonAnimation` component rendering via `MeshRenderer`); design must survive that migration without interface changes.
- **Existing sun system:** `DayNightCycle.cs` rotates a `Light` through 5 keyframes driven by `TimeManager.CurrentTime01` (sim-time). Light intensity already goes to 0 below the horizon.
- **Existing shaders:** `Spine-Skeleton-Lit-ZWrite.shader` (Spine's shadow-capable variant) is already in the project; it is the architectural reference for the new sprite shader.
- **World scale:** 11 Unity units = 1.67 m (project rule 32).

## 3. Decision

**Single vertical quad with an alpha-tested URP `ShadowCaster` pass**, applied uniformly to characters, trees, furniture and props. Buildings use URP defaults (already 3D meshes). A per-interior invisible quad occludes the sun indoors.

Approaches rejected during brainstorming:
- **Blob shadow** — cheap but doesn't match the chosen "grounded, PBR-like" art direction.
- **Projected sprite silhouette (flattened quad)** — stylized look, rejected in favor of real shadows.
- **Cross-quad meshes for sprites** — deferred; only worth the cost if tree silhouettes look weak at low sun angles in QA.
- **Invisible proxy shadow meshes (capsule/cone)** — generic silhouette defeats the reason for picking real shadows.

## 4. Architecture

Three concerns, cleanly separated:

### 4.1 Sprite shadow caster shader

New hand-written URP shader `Assets/Shaders/Sprite-Lit-ShadowCaster.shader`, modelled on `Spine-Skeleton-Lit-ZWrite.shader` and URP's `Sprites-Lit-Default.shader`.

**Passes:**
1. **Forward Lit** — standard sprite sampling with URP 2D lighting, alpha-blended, no ZWrite. Matches the visual output of the current default sprite shader.
2. **ShadowCaster** — `LightMode = "ShadowCaster"`, `ZWrite On`, `Cull Off` (sprites must cast from both sides). Samples `_MainTex.a`, `clip(alpha - _Cutoff)`, writes depth only. Uses URP's `ShadowCasterPass.hlsl`.
3. **DepthOnly** — harmless inclusion; keeps SSAO / screen-space features happy.

**Shader properties:**
- `_MainTex` (2D) — sprite texture
- `_Color` (Color) — tint, parity with default sprite shader
- `_Cutoff` (Range 0–1, default 0.5) — shadow pass alpha threshold. Exposed per material so artists can tune thin elements (hair, cloth, ring-shaped props).

**Material:** `Assets/Materials/Sprites/DefaultSpriteShadowCaster.mat` — default instance of the new shader. Prefabs opt in by swapping their renderer material to this.

**Transparent VFX sprites** (smoke, particles) keep their existing non-shadow-casting material.

### 4.2 Per-prefab opt-in

No new runtime code, no per-frame work. Each category flips `Renderer.shadowCastingMode = On` on its prefab and switches to the new material:

| Category | Authored where | Notes |
|---|---|---|
| Characters | `CharacterArchetype` visual prefab | `CharacterVisual` / `ICharacterVisual` untouched. Post-Spine: swap material to `Spine-Skeleton-Lit-ZWrite`, flag on `MeshRenderer`. |
| Trees / foliage | Tree prefabs under `Assets/Prefabs/World/` | Apply to base prefab; variants inherit. |
| Furniture | `FurnitureInteractable` prefabs | Indoor placements blocked by the ShadowsOnly roof quad anyway. |
| Buildings / walls | ProBuilder meshes | URP default Lit; verify `Cast Shadows = On`, `Receive Shadows = On`. |
| Small props / dropped items | `WorldItem` prefabs | Higher `_Cutoff` (~0.7). `ItemSO.castsShadow` (bool, default true) override for noisy sprites. |

**Receivers:** terrain, ProBuilder ground/walls, Region mesh — all `Receive Shadows = On` (default).

**Sprite orientation:** because camera yaw is locked at 0°, a vertical quad authored at `rotation = (0,0,0)` stays broadside to the camera forever. No billboard script. `CharacterVisual.SetFacingDirection` flips via `scale.x`, which mirrors the quad in place without breaking the shadow pass.

### 4.3 URP + sun configuration

**URP Renderer Asset** (`Settings/UniversalRP-*.asset`):
- Main Light → Cast Shadows: **On**
- Shadow Resolution: **2048** (revisit 4096 after first Spine character content)
- Cascade Count: **2**
- Cascade 1 Split: **~0.4** (cascade 0 ≈ 32u — comfortably covers the ~30u max camera-to-character distance with margin)
- Shadow Distance: **80u** (~12m)
- Soft Shadows: **On**
- **Soft Cascades (cascade blending): On** — hides the cascade-boundary sharpness pop with only 2 cascades active

**Sun light** (the `DayNightCycle` GameObject):
- `shadows = Soft`
- `shadowBias ≈ 0`, **raise `normalBias` instead** (tune 0.5–1.5) — paper-thin quads have no thickness for `shadowBias` to push into
- `shadowStrength` is driven by a new `_shadowStrengthCurve` (see 4.4)

### 4.4 DayNightCycle hook

`DayNightCycle.cs` gains one new serialized field and one line in `UpdateVisuals`:

```csharp
[SerializeField] private AnimationCurve _shadowStrengthCurve;
// in UpdateVisuals:
_directionalLight.shadowStrength = _shadowStrengthCurve.Evaluate(t);
```

**Why a parallel curve, not a scaled `_intensityCurve`:** dawn/dusk want a **dim warm sun with strong dramatic shadows** (golden-hour silhouettes). Coupling shadow strength to intensity would give the opposite — dim sun, weak shadow — and kill the moment. The curve default ≈ `_intensityCurve × 0.7` but stays independently editable.

### 4.5 Indoor sun occlusion

Each hand-authored interior prefab gains a **`ShadowsOnlyRoof` child GameObject** — a flat quad sized inline to cover the interior footprint plus a **shallow-sun margin** (extra area on the sun-travel axis so low-angle dawn/dusk rays still hit the roof before reaching the floor), with `MeshRenderer.shadowCastingMode = ShadowsOnly`. Invisible to the camera, opaque to the sun's shadow pass. Composable with any future camera cut-away system (roof-hide-on-enter etc.) because visible geometry stays untouched.

### 4.6 Night

`DayNightCycle` already forces `Light.intensity = 0` when the sun is below the horizon. A zero-intensity light casts no shadow. Night handles itself. Moonlight shadows are deferred.

## 5. Spine migration compatibility

The design is already Spine-safe:

- `ICharacterVisual` interface untouched — no shadow logic in gameplay code.
- Spine's `Spine-Skeleton-Lit-ZWrite.shader` already has a `ShadowCaster` pass; Spine characters post-migration just use that material and flip `shadowCastingMode = On` on the `SkeletonAnimation`'s `MeshRenderer`.
- The URP settings, sun config and DayNightCycle hook are all shader-agnostic.

## 6. Asset layout

```
Assets/Shaders/Sprite-Lit-ShadowCaster.shader            (new)
Assets/Materials/Sprites/DefaultSpriteShadowCaster.mat   (new)
Assets/Prefabs/World/Interiors/*.prefab                  (modified: +ShadowsOnlyRoof child, sized inline)
Assets/Scripts/DayNightCycle/DayNightCycle.cs            (modified: +_shadowStrengthCurve field + one line in UpdateVisuals)
Assets/Scripts/Item/ItemSO.cs                            (modified: +castsShadow bool, default true)
Assets/Settings/UniversalRP-*.asset                      (modified: cascades, blending, distance, resolution)
```

No new runtime scripts. No new `MonoBehaviour`. The only code delta is the two-line additive change on `DayNightCycle.cs` and one serialized bool on `ItemSO`.

## 7. Multiplayer

Zero networked surface:
- Shadows are a per-client rendering pass.
- `TimeManager.CurrentTime01` is already shared state, so every client's `DayNightCycle` computes the same sun rotation — shadow direction is implicitly consistent across Host ↔ Client, Client ↔ Client, Host/Client ↔ NPC.
- No `NetworkVariable`, no RPC, no interest management impact.

## 8. Save / load

Zero surface. Pure visual, no serialization.

## 9. Testing

**Sun sweep (edit-mode scene):**
One character prefab + one tree + one ProBuilder wall + the directional sun. Scrub `TimeManager.CurrentTime01` 0→1 and confirm shadow angle sweeps, fades at horizon, returns on a second day.

**Facing-flip invariance (play-mode):**
Toggle `CharacterVisual.SetFacingDirection` left/right — confirm shadow mirrors in place without re-orienting the caster plane.

**Interior occluder leak (hand-authored promise verification):**
For each interior prefab variant, drop a character inside and scrub through noon. Confirm no sun rays hit the floor. This justifies the `ShadowsOnlyRoof` pattern; untested, "hand-authored" becomes "hand-broken per variant."

**Cascade boundary crossing (Soft Cascades verification):**
Walk a character from camera-center out past the ~32u cascade 0 ring with Soft Cascades on. Confirm no visible sharpness pop on the shadow. Justifies the cascade-blending toggle.

**Small-item `_Cutoff` sanity (ItemSO.castsShadow justification):**
Drop a potion, a ring, and a log-sized prop at grazing sun. Eyeball shadow quality; flag any sprite (the ring is the prime suspect) as `castsShadow = false` in its `ItemSO` if noisy.

**Performance:**
Scene with ~20 NPCs + tree cluster. Compare frame time with main-light shadows on vs off using the Profiler. Record as baseline.

**Golden-hour art check:**
Noon, dawn, dusk, night with `_shadowStrengthCurve` default. Verify dramatic dawn/dusk silhouettes, no over-darkening, clean fade at horizon.

**Spine smoke test (forward-compat):**
Swap one character to Spine (`SkeletonAnimation` + `Spine-Skeleton-Lit-ZWrite`) and rerun sun sweep. Shadow must behave identically — canary for the Spine migration.

**Rule 26 (GameSpeedController) compliance:**
`DayNightCycle.UpdateVisuals` runs per-frame off `TimeManager.CurrentTime01` (sim-time), so shadow angle + `_shadowStrengthCurve` update correctly at Giga Speed. The edit-mode sun-sweep scrub test doubles as the Giga-Speed verification — no additional harness needed.

## 10. Documentation obligations

- **New skill:** `.agent/skills/rendering/shadows/SKILL.md` — shader passes, material contract, per-prefab setup, URP settings, DayNightCycle hook, ShadowsOnly quad convention.
- **Existing skill updates:** add a "Shadow casting" section wherever `CharacterVisual` is documented; add the new `_shadowStrengthCurve` field to the time / day-night skill.
- **Wiki:** `wiki/systems/shadows.md` — architecture only (no procedures), linking to the skill, shader, `DayNightCycle.cs`, and URP asset. Per `wiki/CLAUDE.md` architecture-vs-procedure rules.

## 11. Agent maintenance (project rule 29)

No existing agent covers rendering/shaders. A Visual/Rendering Specialist is already logged as a future agent in memory (`project_future_agents.md`). This spec alone doesn't cross the 5+ interconnected scripts threshold. **Log as the first concrete seed for the future agent, do not create it yet.**

## 12. Out of scope (explicit)

- **Cloud / weather shadows** — separate spec, `WeatherFront`-driven projector or scrolling decal.
- **Moonlight shadows** — second dimmer directional light for the night half. Revisit after art direction call.
- **Normal-bias-by-sun-angle curve** — fix for near-grazing-sun shadow detachment if it surfaces in QA.
- **4096 shadow atlas / per-platform tiers** — revisit after first Spine character content lands.
- **Cross-quad tree meshes** — upgrade for trees only, if silhouettes look weak at low sun angles.
- **Per-archetype custom shadow tuning** (distinct `_Cutoff` values, `ShadowCastingMode.Off` overrides beyond `ItemSO.castsShadow`) — ad-hoc until a real need surfaces.
