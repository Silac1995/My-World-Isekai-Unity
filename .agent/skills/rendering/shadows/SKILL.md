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
