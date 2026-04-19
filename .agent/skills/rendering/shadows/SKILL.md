---
name: rendering/shadows
description: 2D sprite cast-shadow system - Sprite-Lit-ShadowCaster shader, per-prefab opt-in, DayNightCycle-driven shadow strength, interior ShadowsOnlyRoof occluders.
---

# Rendering - Shadows

Real URP directional-light cast shadows for every 2D sprite in the 3D world. Rotates with `DayNightCycle`. Survives the Spine 2D migration without interface changes.

## When to use this skill

- Adding shadow casting to a new sprite prefab (character, tree, furniture, prop).
- Modifying the shadow-casting shader, material, or URP asset configuration.
- Setting up / authoring a new interior prefab and its `ShadowsOnlyRoof` child.
- Debugging shadow acne, Peter-Panning, or cascade popping.
- Wiring per-object shadow behaviour from an `ItemSO.CastsShadow` override.

## Components

- **Shader:** `Assets/Shaders/Sprite-Lit-ShadowCaster.shader` - URP Forward pass + alpha-tested ShadowCaster pass.
- **Materials:**
  - `Assets/Materials/Sprites/DefaultSpriteShadowCaster.mat` - default (`_Cutoff = 0.5`).
  - `Assets/Materials/Sprites/SmallPropShadowCaster.mat` - variant with `_Cutoff = 0.7` for rings/potions.
- **Per-prefab flag:** `Renderer.shadowCastingMode = On` + material swap. Characters, trees, furniture, props.
- **ItemSO override:** `ItemSO.CastsShadow` (default true) drives `WorldItem.ApplyShadowCastingFromItemSO()` at runtime.
- **Sun hook:** `DayNightCycle._shadowStrengthCurve` (parallel to `_intensityCurve`) drives `Light.shadowStrength` per time-of-day. Decoupled intentionally so dawn/dusk can run dim-sun + dramatic-shadows.
- **Indoor occluder:** Each interior prefab has a `ShadowsOnlyRoof` child (plane with `ShadowCastingMode.ShadowsOnly`), sized inline to the interior footprint + shallow-sun margin.
- **URP config:** `PC_RPAsset` + `Mobile_RPAsset` - 2 cascades, 0.4 split, 80u distance, Soft Cascades On.

## How to add shadows to a new prefab

1. Swap its `SpriteRenderer.sharedMaterial` to `DefaultSpriteShadowCaster` (or `SmallPropShadowCaster` if the sprite is small/thin).
2. Set `shadowCastingMode = On`, `receiveShadows = true`.
3. For Spine characters: use a `Spine-Skeleton-Lit-ZWrite` material instead, same two flags on the `SkeletonAnimation.MeshRenderer`.
4. For items: set `ItemSO.castsShadow` on the ItemSO asset (default true; flip false for noisy small sprites).

## How to author a new interior prefab roof

1. Create Empty Child under the interior prefab's root, named `ShadowsOnlyRoof`.
2. Add `MeshFilter` (Plane primitive) + `MeshRenderer`.
3. Material: `DefaultSpriteShadowCaster` (any material works - the quad is shadow-only, visible output is culled).
4. `MeshRenderer.shadowCastingMode = ShadowsOnly`, `receiveShadows = false`.
5. Position at roof height above the floor (~4.2u for a standard 2.5x-human-height interior).
6. Rotation X = 90 (plane faces down).
7. Scale X/Z = interior footprint + shallow-sun margin (e.g. 10x10u interior -> scale 14x14).

## Dependencies

- URP 17.3 (Universal Render Pipeline).
- `DayNightCycle.cs` + `TimeManager.cs` (sun rotation + time-of-day).
- Spine 2D (forward-compat): `Spine-Skeleton-Lit-ZWrite.shader` already in project.

## Integration points

- `CharacterVisual` - untouched. Shadow is a pass on the material the renderer already holds.
- `ICharacterVisual` - untouched. Shadow logic never touches gameplay code.
- `FurnitureInteractable` - untouched. Pure prefab setup.
- `WorldItem` - consumes `ItemSO.CastsShadow` in `Initialize()`.

## Multiplayer

No networked surface. Shadows are per-client rendering. `TimeManager.CurrentTime01` is already shared, so all clients compute the same sun direction - shadows are implicitly consistent across Host <-> Client, Client <-> Client, Host/Client <-> NPC.

## Save / load

Zero surface. Pure visual, no serialization.

## Known gotchas

- **Humanoid characters currently have no cast shadows.** `Humanoid_Base.prefab` and `Humanoid_Base_old.prefab` were skipped during the initial rollout because of pre-existing uncommitted work. Before shipping the feature to players, swap their SpriteRenderers' materials to `DefaultSpriteShadowCaster.mat` + set `shadowCastingMode = On`. Same pattern as the Quadruped_Base commit.
- **No DepthOnly pass in the shader.** The sprite shader has only ForwardLit + ShadowCaster. If the project ever enables SSAO or any URP screen-space effect that relies on the camera depth prepass, sprites will not write to the depth texture and silhouettes will disappear from the depth-dependent effect. Fix when needed: add a standard URP DepthOnly pass (10-line modeled on URP `Unlit`) with the same alpha clip as the ShadowCaster pass.
- **ShadowsOnlyRoof default size is 14x14.** Interior prefabs larger than ~10u footprint will leak sun at shallow dawn/dusk angles. Verify per-interior in Play Mode at `TimeManager.CurrentTime01 = 0.25` and `0.75`; resize the child quad in the Inspector where needed.

## Open items (tunable after playtest)

- `Light.shadowNormalBias` defaults to 0.8 - tune 0.5-1.5 if acne or Peter-Panning appears.
- `DefaultSpriteShadowCaster._Cutoff = 0.5` / `SmallPropShadowCaster._Cutoff = 0.7` - artists can override per material if a specific sprite clips wrong.
- Per-interior `ShadowsOnlyRoof` scale - default `(14, 1, 14)`, resize if the interior footprint is larger or low-angle sun leaks.
- Mobile `m_SoftShadowQuality = Medium` - if jagged shadow edges are visible on-device, raise to High (2-3x fragment cost).

## Out of scope (future work)

- Cloud / weather shadows (separate spec, `WeatherFront`-driven).
- Moonlight shadows at night.
- Cross-quad meshes for wide-canopy trees.
- Normal-bias-by-sun-angle curve.
- Per-archetype custom `_Cutoff` tuning beyond `ItemSO.castsShadow`.

## See also

- Design spec: [docs/superpowers/specs/2026-04-19-2d-sprite-cast-shadows-design.md](../../../docs/superpowers/specs/2026-04-19-2d-sprite-cast-shadows-design.md)
- Implementation plan: [docs/superpowers/plans/2026-04-19-2d-sprite-cast-shadows.md](../../../docs/superpowers/plans/2026-04-19-2d-sprite-cast-shadows.md)
- Wiki architecture page: [wiki/systems/shadows.md](../../../wiki/systems/shadows.md) (created in Task 17)
