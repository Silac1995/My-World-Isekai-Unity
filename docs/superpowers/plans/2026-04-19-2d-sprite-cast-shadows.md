# 2D Sprite Cast Shadows Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give every 2D sprite a real URP directional-light cast shadow that matches its silhouette, rotates with the existing `DayNightCycle` sun, and survives the upcoming Spine 2D migration without interface changes.

**Architecture:** A new hand-written URP shader with a Forward pass + alpha-tested ShadowCaster pass. Per-prefab opt-in by swapping material and flipping `shadowCastingMode = On`. One small additive change to `DayNightCycle` (parallel `_shadowStrengthCurve`). One `ItemSO.castsShadow` override. URP cascades/blending tuned for the camera's ~30u max character distance. Per-interior `ShadowsOnlyRoof` child quad blocks sun indoors.

**Tech Stack:** Unity URP 17.3, HLSL, C#, Spine 2D (forward compat), URP Renderer Asset, ProBuilder.

**Spec reference:** [docs/superpowers/specs/2026-04-19-2d-sprite-cast-shadows-design.md](../specs/2026-04-19-2d-sprite-cast-shadows-design.md)

**Notes for the implementer:**
- Prefab / scene / URP-asset edits can be done via Unity MCP tools or the Unity Editor manually. Both are valid.
- Unity cannot unit-test shader output; visual verification in an edit-mode scene is the canonical check for the shader + URP config + prefab work. Code changes (`ItemSO`, `DayNightCycle`) get EditMode tests via `com.unity.test-framework`.
- Commit after every task. Never bundle.

---

## Task 0: Clean up stale Shader Graph

**Files:**
- Delete: `Assets/Shaders/Sprite Shadow.shadergraph`
- Delete: `Assets/Shaders/Sprite Shadow.shadergraph.meta`

**Rationale:** User confirmed this is abandoned experimentation from 2026-03-24. We're taking the hand-written shader path per spec; leaving this file would duplicate responsibility and confuse future readers.

- [ ] **Step 1: Verify nothing references it**

```bash
grep -r "Sprite Shadow" Assets/ --include="*.mat" --include="*.prefab" --include="*.asset"
```

Expected: no matches. If anything matches, **stop** — investigate before deleting.

- [ ] **Step 2: Delete both files**

```bash
rm "Assets/Shaders/Sprite Shadow.shadergraph"
rm "Assets/Shaders/Sprite Shadow.shadergraph.meta"
```

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "chore(shaders): remove abandoned Sprite Shadow shadergraph experiment"
```

---

## Task 1: Add `ItemSO.castsShadow` override (TDD)

**Files:**
- Modify: `Assets/Scripts/Item/ItemSO.cs`
- Create: `Assets/Tests/EditMode/Item/ItemSO_CastsShadowTests.cs`

**Rationale:** Tiny props (ring, potion) produce noisy alpha-tested shadows at grazing sun. Default-true bool lets artists disable per-item without touching prefabs.

- [ ] **Step 1: Write the failing test**

```csharp
// Assets/Tests/EditMode/Item/ItemSO_CastsShadowTests.cs
using NUnit.Framework;
using UnityEngine;

public class ItemSO_CastsShadowTests
{
    [Test]
    public void CastsShadow_DefaultValue_IsTrue()
    {
        ItemSO so = ScriptableObject.CreateInstance<ItemSO>();
        Assert.IsTrue(so.CastsShadow, "castsShadow must default to true so existing items keep shadows");
        Object.DestroyImmediate(so);
    }
}
```

- [ ] **Step 2: Run test, verify it fails**

In Unity: `Window → General → Test Runner → EditMode → Run All`
Expected: `ItemSO_CastsShadowTests.CastsShadow_DefaultValue_IsTrue` FAILS with compiler error (no `CastsShadow` property).

- [ ] **Step 3: Add the field + property to `ItemSO`**

Locate the field block in `Assets/Scripts/Item/ItemSO.cs` (add near other visual-related serialized fields; if there is no visual section, add at the end of the fields block):

```csharp
[Header("Rendering")]
[SerializeField, Tooltip("If false, this item's world-prefab SpriteRenderer will have ShadowCastingMode.Off. Use for small/noisy sprites (rings, potions) whose alpha-tested shadows look bad at grazing sun.")]
private bool _castsShadow = true;

public bool CastsShadow => _castsShadow;
```

- [ ] **Step 4: Run test, verify it passes**

Expected: `ItemSO_CastsShadowTests.CastsShadow_DefaultValue_IsTrue` PASSES.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Item/ItemSO.cs Assets/Tests/EditMode/Item/ItemSO_CastsShadowTests.cs
git commit -m "feat(item): add ItemSO.CastsShadow override (default true)"
```

---

## Task 2: Add `DayNightCycle._shadowStrengthCurve` (TDD)

**Files:**
- Modify: `Assets/Scripts/DayNightCycle/DayNightCycle.cs`
- Create: `Assets/Tests/EditMode/DayNightCycle/DayNightCycle_ShadowStrengthTests.cs`

**Rationale:** Parallel curve (not coupled to `_intensityCurve`) so dawn/dusk can run dim-sun-dramatic-shadows for golden-hour moments.

- [ ] **Step 1: Write the failing test**

```csharp
// Assets/Tests/EditMode/DayNightCycle/DayNightCycle_ShadowStrengthTests.cs
using NUnit.Framework;
using UnityEngine;

public class DayNightCycle_ShadowStrengthTests
{
    [Test]
    public void UpdateVisuals_DrivesShadowStrengthFromParallelCurve_AtNoon()
    {
        GameObject go = new GameObject("SunTest");
        Light light = go.AddComponent<Light>();
        light.type = LightType.Directional;
        DayNightCycle cycle = go.AddComponent<DayNightCycle>();

        // Arrange: curve returns 0.8 at noon (t = 0.5)
        AnimationCurve shadowStrength = new AnimationCurve(
            new Keyframe(0f, 0.8f),
            new Keyframe(1f, 0.8f));
        // Existing intensityCurve peaks ~1.0 at noon — verify shadow is NOT simply scaled from it
        AnimationCurve intensity = new AnimationCurve(
            new Keyframe(0f, 1.0f),
            new Keyframe(1f, 1.0f));

        typeof(DayNightCycle).GetField("_shadowStrengthCurve",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .SetValue(cycle, shadowStrength);
        typeof(DayNightCycle).GetField("_intensityCurve",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .SetValue(cycle, intensity);

        // Act: force the private UpdateVisuals path at t = 0.5
        cycle.SendMessage("UpdateVisuals", 0.5f);

        // Assert
        Assert.AreEqual(0.8f, light.shadowStrength, 0.001f,
            "shadowStrength must come from _shadowStrengthCurve, independent of _intensityCurve");

        Object.DestroyImmediate(go);
    }
}
```

- [ ] **Step 2: Run test, verify it fails**

Expected: test fails (field `_shadowStrengthCurve` does not exist / `shadowStrength` still at Unity default of 1.0).

- [ ] **Step 3: Add the field and one line in `UpdateVisuals`**

In `Assets/Scripts/DayNightCycle/DayNightCycle.cs`:

After the existing `[SerializeField] private AnimationCurve _intensityCurve;` (line ~7), add:

```csharp
[SerializeField, Tooltip("Sun shadowStrength by time-of-day, sibling to _intensityCurve. Keep decoupled so dawn/dusk can do dim-sun-dramatic-shadows.")]
private AnimationCurve _shadowStrengthCurve;
```

In `UpdateVisuals(float t)`, after the intensity branch (after line ~106), add:

```csharp
if (_shadowStrengthCurve != null && _shadowStrengthCurve.length > 0)
{
    _directionalLight.shadowStrength = Mathf.Clamp01(_shadowStrengthCurve.Evaluate(t));
}
```

Full surrounding context after the change:

```csharp
if (_intensityCurve != null)
{
    _directionalLight.intensity = _intensityCurve.Evaluate(t);
}
else
{
    float dot = Vector3.Dot(transform.forward, Vector3.down);
    if (dot > 0)
        _directionalLight.intensity = Mathf.SmoothStep(0, 1.2f, dot * 2.5f);
    else
        _directionalLight.intensity = 0;
}

if (_shadowStrengthCurve != null && _shadowStrengthCurve.length > 0)
{
    _directionalLight.shadowStrength = Mathf.Clamp01(_shadowStrengthCurve.Evaluate(t));
}
```

- [ ] **Step 4: Run test, verify it passes**

Expected: `DayNightCycle_ShadowStrengthTests.UpdateVisuals_DrivesShadowStrengthFromParallelCurve_AtNoon` PASSES.

- [ ] **Step 5: Configure the curve on the sun GameObject in `GameScene.unity`**

In Unity Editor, select the directional light with `DayNightCycle`, set `_shadowStrengthCurve` keyframes:
- `(0.0, 0.0)` — midnight, zero (no sun)
- `(0.25, 0.85)` — dawn/morning, dramatic long shadows
- `(0.5, 0.65)` — noon, softer
- `(0.75, 0.9)` — dusk, dramatic again
- `(0.875, 0.2)` — evening fade
- `(1.0, 0.0)` — back to midnight

Save the scene.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/DayNightCycle/DayNightCycle.cs Assets/Tests/EditMode/DayNightCycle/ Assets/Scenes/GameScene.unity
git commit -m "feat(daynight): add _shadowStrengthCurve, drive Light.shadowStrength decoupled from intensity"
```

---

## Task 3: Create `Sprite-Lit-ShadowCaster.shader`

**Files:**
- Create: `Assets/Shaders/Sprite-Lit-ShadowCaster.shader`

**Rationale:** The one piece of custom tech. Forward pass samples a sprite texture and applies URP main-light color + shadow attenuation, alpha-clipped. ShadowCaster pass writes depth only with the same alpha clip so sprites cast alpha-accurate silhouettes.

- [ ] **Step 1: Create the shader file**

```hlsl
// Assets/Shaders/Sprite-Lit-ShadowCaster.shader
Shader "MWI/Sprite-Lit-ShadowCaster"
{
    Properties
    {
        [MainTexture] _MainTex ("Sprite Texture", 2D) = "white" {}
        [MainColor]   _Color   ("Tint", Color) = (1,1,1,1)
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType"     = "TransparentCutout"
            "Queue"          = "AlphaTest"
            "IgnoreProjector"= "True"
        }
        LOD 100

        // ---------- Forward Lit Pass ----------
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite On    // critical: depth write so shadow receiver math aligns

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float  _Cutoff;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                float4 color      : COLOR;
                float  fogFactor  : TEXCOORD3;
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = pos.positionCS;
                OUT.positionWS = pos.positionWS;
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv         = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.color      = IN.color;
                OUT.fogFactor  = ComputeFogFactor(pos.positionCS.z);
                return OUT;
            }

            float4 frag (Varyings IN) : SV_Target
            {
                float4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                float4 col = tex * _Color * IN.color;

                // Alpha clip so forward output matches the silhouette we cast
                clip(col.a - _Cutoff);

                // Main light contribution with shadow attenuation
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                float NdotL = saturate(dot(normalize(IN.normalWS), mainLight.direction));
                // Sprites face camera, not sun. Use flat wrap-lighting so sun tint shows through
                // without the sprite going black on its back side.
                float wrap = 0.5 + 0.5 * NdotL;

                float3 sunContrib = mainLight.color * mainLight.shadowAttenuation * wrap;
                float3 ambient = SampleSH(float3(0, 1, 0));

                col.rgb *= sunContrib + ambient;
                col.rgb = MixFog(col.rgb, IN.fogFactor);
                return col;
            }
            ENDHLSL
        }

        // ---------- Shadow Caster Pass ----------
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            Cull Off
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex   shadowVert
            #pragma fragment shadowFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float  _Cutoff;
            CBUFFER_END

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            float4 GetShadowPositionHClip(Attributes IN)
            {
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS   = TransformObjectToWorldNormal(IN.normalOS);

            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));

            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return positionCS;
            }

            Varyings shadowVert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = GetShadowPositionHClip(IN);
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                return OUT;
            }

            half4 shadowFrag (Varyings IN) : SV_Target
            {
                float4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                clip(tex.a * _Color.a - _Cutoff);
                return 0;
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}
```

- [ ] **Step 2: Refresh asset database**

In Unity: `Assets → Refresh` (or Ctrl-R). Watch the Console — any HLSL compile errors must be resolved before continuing.

Expected: no errors. Shader appears under `MWI/Sprite-Lit-ShadowCaster` in the material shader selector.

- [ ] **Step 3: Commit**

```bash
git add Assets/Shaders/Sprite-Lit-ShadowCaster.shader Assets/Shaders/Sprite-Lit-ShadowCaster.shader.meta
git commit -m "feat(shaders): add Sprite-Lit-ShadowCaster URP shader (forward lit + alpha-tested shadow caster pass)"
```

---

## Task 4: Create default material + forward-pass parity check

**Files:**
- Create: `Assets/Materials/Sprites/DefaultSpriteShadowCaster.mat`
- Create: `Assets/Scenes/Tests/ShadowTest.unity` (verification scene, kept in tree)

**Rationale:** One material asset artists can assign to any renderer. Parity check confirms the forward pass matches the current default sprite visual so material swaps don't regress looks.

- [ ] **Step 1: Create the material**

In Unity: Right-click `Assets/Materials/Sprites/` → Create → Material → name it `DefaultSpriteShadowCaster`. (Create the `Sprites` subfolder first if it doesn't exist.)

Select the material, set Shader to `MWI/Sprite-Lit-ShadowCaster`. Leave `_Cutoff = 0.5`, `_Color = white`.

- [ ] **Step 2: Build verification scene**

Create `Assets/Scenes/Tests/ShadowTest.unity`. Populate with:

- A ProBuilder cube ground plane (~50×0.1×50) centered at origin, Receive Shadows = On (default).
- A single `Character` prefab (pick any available from `Assets/Prefabs/` — the goal is one real character with its SpriteRenderer). Place at origin.
- A tree prefab from `Assets/Prefabs/World/`, placed a few units away.
- A Directional Light with `DayNightCycle` + the `_shadowStrengthCurve` from Task 2, Shadow Type = Soft.
- A second Directional Light disabled by default labelled "ControlLight_NoShadow" — used for the parity A/B comparison.

Save.

- [ ] **Step 3: Forward-pass parity check (no shadows yet)**

With the character's SpriteRenderer still on the ORIGINAL material and shadow casting OFF, take a Game-view screenshot (screenshot-game-view MCP tool or Window → Capture).

Now swap the character's material to `DefaultSpriteShadowCaster`, keep cast-shadow OFF, disable the main light's shadow, take the same screenshot.

Compare side by side (the two screenshots). Difference should be ≤ a couple of percent — if the new material is visibly darker/brighter/color-shifted beyond that, the forward pass needs tuning (likely ambient term or wrap-lighting) before proceeding.

Expected: visually identical within ~2% tolerance.

If parity fails: iterate on the forward pass in the shader — adjust `ambient` / `wrap` factors — until parity holds.

- [ ] **Step 4: Commit**

```bash
git add Assets/Materials/Sprites/ Assets/Scenes/Tests/
git commit -m "feat(shaders): add DefaultSpriteShadowCaster material + shadow verification scene"
```

---

## Task 5: Configure URP Renderer Assets (PC + Mobile)

**Files:**
- Modify: `Assets/Settings/PC_RPAsset.asset`
- Modify: `Assets/Settings/Mobile_RPAsset.asset`

**Rationale:** Cascade split tuned for the camera's ~30u max character distance; soft cascades hide the boundary pop with 2 cascades; distance sized to just beyond visible range.

- [ ] **Step 1: Configure PC_RPAsset**

In Unity, select `Assets/Settings/PC_RPAsset.asset`. In the Inspector:

| Field | Value |
|---|---|
| Lighting → Main Light | Per Pixel |
| Lighting → Cast Shadows | On |
| Shadows → Max Distance | 80 |
| Shadows → Cascade Count | 2 |
| Shadows → Cascade 1 Split | 0.4 |
| Shadows → Soft Cascades | On |
| Shadows → Shadow Resolution | 2048 |
| Shadows → Soft Shadows | High (URP 17 — "High" = enables the 5×5 filter) |

- [ ] **Step 2: Configure Mobile_RPAsset identically**

Same fields, but override Resolution = 1024 (mobile budget). Everything else matches PC.

- [ ] **Step 3: Commit**

```bash
git add Assets/Settings/PC_RPAsset.asset Assets/Settings/Mobile_RPAsset.asset
git commit -m "feat(urp): configure cascades, soft blending, 80u distance for sprite shadows"
```

---

## Task 6: Configure sun light (bias + soft shadows)

**Files:**
- Modify: `Assets/Scenes/GameScene.unity` (the directional light with `DayNightCycle`)

**Rationale:** Paper-thin sprite quads have zero thickness for `shadowBias` to push into. Use `normalBias` instead.

- [ ] **Step 1: Find the sun GameObject**

In `GameScene.unity`, select the Directional Light that has the `DayNightCycle` component attached.

- [ ] **Step 2: Set shadow fields**

| Field | Value |
|---|---|
| Light → Shadow Type | Soft Shadows |
| Light → Bias | 0.0 |
| Light → Normal Bias | 0.8 (start value; tune in Task 11 if Peter-Panning or acne appears) |
| Light → Near Plane | 0.1 (default) |

Save the scene.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scenes/GameScene.unity
git commit -m "feat(daynight): configure sun shadow bias for billboard sprites (bias=0, normalBias=0.8)"
```

---

## Task 7: Apply shadow casting to character archetype visual prefabs

**Files:**
- Modify: every prefab under `Assets/Prefabs/` that represents a `Character` or a `CharacterArchetype` visual

**Rationale:** Characters are the highest-priority shadow consumers.

- [ ] **Step 1: Enumerate character prefabs**

```bash
grep -rl "Character" Assets/Prefabs/ --include="*.prefab" | head -20
```

Also list prefabs referenced from `Assets/Resources/Data/Archetypes/` (or wherever `CharacterArchetype` SOs live) — the archetype's `visualPrefab` field.

Build a complete list. Name it in the commit message.

- [ ] **Step 2: For each character prefab — swap material and enable cast shadows**

Open the prefab. On each `SpriteRenderer` in the visual hierarchy:

- Material: swap to `DefaultSpriteShadowCaster`
- Cast Shadows: On
- Receive Shadows: On

**Do not** touch VFX / smoke / particle sprites. Only the character body.

Save the prefab.

- [ ] **Step 3: Drop one character into ShadowTest.unity and verify**

In the shadow test scene from Task 4, place one freshly-updated character under the sun. Enable Play Mode. Verify:

- [x] Character casts a shadow on the ground (not a rectangle — a character silhouette)
- [x] Flipping `CharacterVisual.SetFacingDirection(-1)` / `(1)` flips the character AND mirrors the shadow in place, without re-orienting the quad

Exit Play Mode.

- [ ] **Step 4: Commit**

```bash
git add Assets/Prefabs/
git commit -m "feat(characters): enable shadow casting on all character visual prefabs"
```

---

## Task 8: Apply shadow casting to tree/region prefabs

**Files:**
- Modify: tree prefabs under `Assets/Prefabs/World/` (including `Region Plain.prefab`, `Region.prefab`, and any tree variants)

**Rationale:** Tree shadows are the biggest visual win at dawn/dusk.

- [ ] **Step 1: Enumerate tree prefabs**

```bash
ls Assets/Prefabs/World/*.prefab
grep -rl "SpriteRenderer" Assets/Prefabs/World/ --include="*.prefab"
```

- [ ] **Step 2: For each tree/region prefab with a SpriteRenderer**

Open the prefab. On each relevant `SpriteRenderer`:

- Material: swap to `DefaultSpriteShadowCaster`
- Cast Shadows: On
- Receive Shadows: On

Save.

- [ ] **Step 3: Verify in ShadowTest.unity**

Place 3–5 tree prefabs in the scene. Scrub `TimeManager.CurrentTime01` from 0.2 (dawn) to 0.5 (noon) to 0.8 (dusk). Confirm tree shadows appear and rotate.

Note any tree whose shadow silhouette looks "thin" at a low sun angle — log in the commit body as a candidate for cross-quad upgrade (out of scope for this plan).

- [ ] **Step 4: Commit**

```bash
git add Assets/Prefabs/World/
git commit -m "feat(world): enable shadow casting on tree/region prefabs"
```

---

## Task 9: Apply shadow casting to furniture prefabs

**Files:**
- Modify: furniture prefabs (check `Assets/Prefabs/` — anywhere `FurnitureInteractable` component is present)

- [ ] **Step 1: Enumerate furniture prefabs**

```bash
grep -rl "FurnitureInteractable" Assets/Prefabs/ --include="*.prefab"
```

- [ ] **Step 2: Material swap + flag on each**

For each furniture prefab's sprite:
- Material: `DefaultSpriteShadowCaster`
- Cast Shadows: On
- Receive Shadows: On

Save each prefab.

- [ ] **Step 3: Commit**

```bash
git add Assets/Prefabs/
git commit -m "feat(furniture): enable shadow casting on FurnitureInteractable prefabs"
```

---

## Task 10: Apply shadow casting to WorldItem/props, wire `ItemSO.CastsShadow`

**Files:**
- Modify: WorldItem / dropped-item prefabs (under `Assets/Prefabs/` — find via `WorldItem` component)
- Modify: `Assets/Scripts/Item/WorldItem.cs` (or whichever script instantiates the sprite from ItemSO)

**Rationale:** Props are the noisiest category at grazing sun; the per-`ItemSO.CastsShadow` override must actually drive `ShadowCastingMode`.

- [ ] **Step 1: Locate WorldItem's sprite setup**

```bash
grep -n "SpriteRenderer\|shadowCastingMode" Assets/Scripts/Item/WorldItem.cs
```

Identify where `WorldItem` assigns the sprite from `ItemSO`.

- [ ] **Step 2: Wire `CastsShadow` into ShadowCastingMode**

In `WorldItem.cs`, in the method that initializes the sprite (after the sprite + ItemSO are set), add:

```csharp
using UnityEngine.Rendering;

// ... inside the init method, after SpriteRenderer sprite is assigned:
_spriteRenderer.shadowCastingMode = _itemSO != null && _itemSO.CastsShadow
    ? ShadowCastingMode.On
    : ShadowCastingMode.Off;
_spriteRenderer.receiveShadows = true;
```

(If `_spriteRenderer` / `_itemSO` have different names in the file, use the existing names.)

- [ ] **Step 3: Material swap on each WorldItem prefab**

For each WorldItem prefab's SpriteRenderer — material only, NOT the cast-shadow flag (that's runtime-driven now):
- Material: `DefaultSpriteShadowCaster`

Save.

- [ ] **Step 4: Raise `_Cutoff` for small props**

Create `Assets/Materials/Sprites/SmallPropShadowCaster.mat` — same shader, `_Cutoff = 0.7`. Assign to noisy small-sprite items (rings, small potions) instead of the default.

- [ ] **Step 5: Spot-check in ShadowTest.unity**

Drop a potion, a ring, and a log-sized prop. Toggle `ItemSO.castsShadow` on each and confirm the runtime ShadowCastingMode follows (use the Frame Debugger or visual inspection — prop should have/lack a shadow accordingly).

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Item/ Assets/Prefabs/ Assets/Materials/Sprites/
git commit -m "feat(items): runtime-drive WorldItem ShadowCastingMode from ItemSO.CastsShadow"
```

---

## Task 11: Add `ShadowsOnlyRoof` children to interior prefabs

**Files:**
- Modify: every interior prefab under `Assets/Prefabs/World/Buildings/` (or wherever building interiors live)

**Rationale:** Invisible caster above each hand-authored interior so sun rays don't leak onto the interior floor. Composable with any future camera cut-away system.

- [ ] **Step 1: Enumerate interior prefabs**

```bash
grep -rl "BuildingInterior\|Interior" Assets/Prefabs/World/ --include="*.prefab"
```

Manually verify the list — include only actual interior prefabs (floor + walls forming an enclosed space), not exteriors.

- [ ] **Step 2: For each interior prefab — add `ShadowsOnlyRoof` child**

Open the prefab. At the root, Create Empty Child → name `ShadowsOnlyRoof`.

On that child:
- Add `MeshFilter` with `Plane` primitive (or a Quad mesh).
- Add `MeshRenderer`:
  - Materials: Element 0 = `DefaultSpriteShadowCaster` (material is shadow-only; visible output is suppressed below)
  - Cast Shadows: **ShadowsOnly**
  - Receive Shadows: Off
- Transform:
  - Position Y = roof-equivalent height of the interior (typically ~2.5× human height in your scale — ~4.2u above the floor)
  - Rotation X = 90 (so the plane faces down)
  - Scale X/Z = interior footprint + shallow-sun margin (e.g. footprint 10×10u → scale 14×14u to account for low-angle dawn rays)

Save the prefab.

- [ ] **Step 3: Interior occluder leak test**

In `ShadowTest.unity`, drop one updated interior prefab. Place a test character inside. Scrub `TimeManager.CurrentTime01` through:
- 0.25 (dawn, low sun)
- 0.5 (noon, high sun)
- 0.75 (dusk, low sun)

Confirm no sun ray hits the floor at ANY of the three times. If leakage appears at 0.25 or 0.75, increase the roof quad's scale margin for that prefab.

Repeat for each interior prefab variant.

- [ ] **Step 4: Commit**

```bash
git add Assets/Prefabs/World/
git commit -m "feat(buildings): add ShadowsOnlyRoof child to each interior prefab (per-prefab sized)"
```

---

## Task 12: Sun-sweep + facing-flip + cascade verification

**Files:**
- Use: `Assets/Scenes/Tests/ShadowTest.unity`

**Rationale:** Single sitting covers the three behavioural verifications the spec promised.

- [ ] **Step 1: Sun-sweep scrub (spec test: Sun sweep + Rule 26)**

Open `ShadowTest.unity`. In Play Mode, set `TimeManager.CurrentTime01` via the debug inspector from 0 → 1 over 10 seconds. Confirm:

- [x] Shadow angle sweeps smoothly across the scene floor
- [x] Shadow fades to nothing when `_intensityCurve` reaches 0 (night)
- [x] Shadow re-appears when sun crosses horizon again

Per spec §9 Rule-26 compliance: this sun-scrub also verifies Giga Speed behaviour because `DayNightCycle.UpdateVisuals` runs every frame off `TimeManager.CurrentTime01`. Accelerate `TimeManager` to Giga Speed and repeat — shadows must track correctly.

- [ ] **Step 2: Facing-flip invariance (spec test: Facing-flip)**

Still in Play Mode. Call `character.CharacterVisual.SetFacingDirection(-1)` via debug or by running the character's flip logic. Confirm:

- [x] Character visual mirrors
- [x] Shadow mirrors in place without the quad re-orienting
- [x] No brief frame of bad-looking shadow during the flip

- [ ] **Step 3: Cascade boundary crossing (spec test: Soft Cascades)**

Still in Play Mode. Move the character (via `transform.position`) from origin outward along +X, crossing the 32u cascade boundary. With Soft Cascades ON:

- [x] No visible sharpness pop in the shadow

Now disable Soft Cascades on the URP asset, repeat. You should now see the pop — confirms the blending setting is doing useful work. Re-enable.

- [ ] **Step 4: Log results**

In commit body, note pass/fail + any quirks (Peter-Panning, acne, cascade seam) seen. Fix immediately if blocking; otherwise log as follow-up.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scenes/Tests/
git commit -m "test(shadows): sun-sweep + facing-flip + cascade boundary verification pass"
```

---

## Task 13: Small-prop `_Cutoff` sanity + performance baseline

**Files:**
- Use: `Assets/Scenes/Tests/ShadowTest.unity`

- [ ] **Step 1: Small-prop grazing-sun check (spec test: Small-item cutoff)**

Set `TimeManager.CurrentTime01 = 0.25` (dawn) in Play Mode. Drop a potion, a ring, and a log-sized prop near the character.

Inspect each shadow. If noisy / pixel-shaped / fragmented, flip that item's `ItemSO.castsShadow = false`. Rings typically need this.

Commit the `ItemSO` asset changes alongside the test result.

- [ ] **Step 2: Performance baseline (spec test: Performance)**

In Play Mode, instantiate ~20 character prefabs + ~10 tree prefabs in ShadowTest. Open `Window → Analysis → Profiler` → Rendering.

Capture 5 seconds of frame data with:
- Case A: main-light shadows ON (URP asset as configured)
- Case B: main-light shadows temporarily OFF on the URP asset

Record GPU frame-time delta (Case A − Case B) in the commit body. Anything above ~3 ms at this entity count is a flag for follow-up — but not a blocker for shipping the feature.

- [ ] **Step 3: Golden-hour art check (spec test: Golden-hour)**

Scrub `TimeManager.CurrentTime01` to 0.25 (dawn), 0.5 (noon), 0.75 (dusk), 0.95 (night). Screenshot each. Review:

- [x] Dawn/dusk produce dramatic long silhouettes
- [x] Noon shadows not over-darkened
- [x] Night has no visible shadow (sun intensity = 0)

If `_shadowStrengthCurve` values look wrong, tune and retest.

- [ ] **Step 4: Commit**

```bash
git add Assets/Resources/Data/Items/ Assets/Scenes/Tests/
git commit -m "test(shadows): small-prop cutoff + performance baseline + golden-hour art pass"
```

---

## Task 14: Spine smoke test (forward-compat canary)

**Files:**
- Use: one character prefab, swapped to `SkeletonAnimation`

**Rationale:** The spec promises Spine compatibility "for free." Must verify with one concrete character before shipping.

- [ ] **Step 1: Set up a Spine character**

Duplicate one existing character prefab. Replace its `SpriteRenderer` visual with a `SkeletonAnimation` component driving any existing Spine skeleton asset in the project.

On the `SkeletonAnimation`'s `MeshRenderer`:
- Material: `Spine-Skeleton-Lit-ZWrite` (use the project's existing Spine ZWrite material, or create one from the Spine shader if none exists)
- Cast Shadows: On
- Receive Shadows: On

- [ ] **Step 2: Drop into ShadowTest and verify**

Place in `ShadowTest.unity` next to a regular sprite character. Play Mode, scrub the sun.

Confirm:
- [x] Spine character casts a shadow
- [x] Shadow silhouette follows Spine animation frames (not a static rectangle)
- [x] Shadow behaves consistently with the regular sprite character next to it

- [ ] **Step 3: Commit the test artifact**

```bash
git add Assets/Scenes/Tests/ Assets/Prefabs/
git commit -m "test(shadows): Spine SkeletonAnimation smoke test — shadow casting verified"
```

---

## Task 15: Write rendering/shadows skill

**Files:**
- Create: `.agent/skills/rendering/shadows/SKILL.md`

**Rationale:** Project rule 28 — every system gets a skill. Future agents/engineers need a single source of truth for how this system works.

- [ ] **Step 1: Check project skill template**

```bash
cat .agent/skills/skill-creator/SKILL.md | head -80
```

Follow the project's skill template convention (frontmatter, purpose, API, events, dependencies, integration points).

- [ ] **Step 2: Write the skill file**

```markdown
---
name: rendering/shadows
description: 2D sprite cast-shadow system — Sprite-Lit-ShadowCaster shader, per-prefab opt-in, DayNightCycle-driven shadow strength, interior ShadowsOnlyRoof occluders.
---

# Rendering · Shadows

## Purpose
Real URP directional-light cast shadows for every 2D sprite in the 3D world. Rotates with `DayNightCycle`. Survives Spine 2D migration without interface changes.

## Components
- **Shader:** `Assets/Shaders/Sprite-Lit-ShadowCaster.shader` — URP Forward pass + alpha-tested ShadowCaster pass.
- **Material:** `Assets/Materials/Sprites/DefaultSpriteShadowCaster.mat` — default material; `SmallPropShadowCaster.mat` variant for tiny sprites (`_Cutoff = 0.7`).
- **Per-prefab flag:** `Renderer.shadowCastingMode = On` + material swap. Characters, trees, furniture, props.
- **ItemSO override:** `ItemSO.CastsShadow` (default true) drives `WorldItem` runtime ShadowCastingMode.
- **Sun hook:** `DayNightCycle._shadowStrengthCurve` — parallel to `_intensityCurve`, drives `Light.shadowStrength` per time-of-day.
- **Indoor occluder:** Each interior prefab has a `ShadowsOnlyRoof` child (plane with `ShadowCastingMode.ShadowsOnly`), sized inline to the interior footprint + shallow-sun margin.
- **URP config:** `PC_RPAsset` + `Mobile_RPAsset` — 2 cascades, 0.4 split, 80u distance, Soft Cascades On.

## How to add shadows to a new prefab
1. Swap its `SpriteRenderer.sharedMaterial` to `DefaultSpriteShadowCaster` (or `SmallPropShadowCaster` if tiny).
2. `shadowCastingMode = On`, `receiveShadows = true`.
3. For Spine characters: use `Spine-Skeleton-Lit-ZWrite.shader` material, same two flags on the `SkeletonAnimation.MeshRenderer`.
4. For items: set `ItemSO.castsShadow` (default true; flip false for noisy small sprites).

## Dependencies
- URP 17.3 (Universal Render Pipeline)
- `DayNightCycle.cs` + `TimeManager.cs` (sun rotation + time-of-day)
- Spine 2D (forward-compat): `Spine-Skeleton-Lit-ZWrite` already in project.

## Integration points
- `CharacterVisual` — untouched. Shadow is a pass on the material the renderer already holds.
- `ICharacterVisual` — untouched. Shadow logic never touches gameplay code.
- `FurnitureInteractable` — untouched. Pure prefab setup.
- `WorldItem` — consumes `ItemSO.CastsShadow` on sprite init.

## Multiplayer
No networked surface. Shadows are per-client rendering. `TimeManager.CurrentTime01` is already shared, so all clients compute the same sun direction — shadows are implicitly consistent across Host↔Client, Client↔Client, Host/Client↔NPC.

## Out of scope (future work)
- Cloud/weather shadows (separate spec, `WeatherFront`-driven).
- Moonlight shadows at night.
- Cross-quad meshes for wide-canopy trees.
- Normal-bias-by-sun-angle curve.
- Per-archetype custom `_Cutoff` tuning beyond `ItemSO.castsShadow`.

## Design spec
[docs/superpowers/specs/2026-04-19-2d-sprite-cast-shadows-design.md](../../../docs/superpowers/specs/2026-04-19-2d-sprite-cast-shadows-design.md)
```

- [ ] **Step 3: Commit**

```bash
git add .agent/skills/rendering/
git commit -m "docs(skills): add rendering/shadows skill"
```

---

## Task 16: Update related skill files

**Files:**
- Modify: wherever `CharacterVisual` skill lives (search `.agent/skills/` for the file)
- Modify: wherever day-night / time skill lives

**Rationale:** Project rule 28 — modifications to existing systems update the existing skill.

- [ ] **Step 1: Find existing skills**

```bash
grep -rl "CharacterVisual\|DayNightCycle" .agent/skills/
```

- [ ] **Step 2: Add a "Shadow casting" section to the CharacterVisual skill**

Append at the end of the file:

```markdown
## Shadow casting

`CharacterVisual` does not own shadow logic. Shadows are a pass on the material the `SpriteRenderer` already holds (`DefaultSpriteShadowCaster`). `SetFacingDirection` flips the quad via `scale.x`, which mirrors the shadow in place without breaking the shadow pass.

See: [.agent/skills/rendering/shadows/SKILL.md](../rendering/shadows/SKILL.md)
```

- [ ] **Step 3: Document `_shadowStrengthCurve` in the day-night skill**

Append:

```markdown
## `_shadowStrengthCurve`

Parallel to `_intensityCurve`. Drives `Light.shadowStrength` per time-of-day, decoupled from intensity so dawn/dusk can run dim-sun + dramatic-shadows (golden-hour art direction). Evaluated in `UpdateVisuals(t)`.
```

- [ ] **Step 4: Commit**

```bash
git add .agent/skills/
git commit -m "docs(skills): document shadow-casting touchpoints on CharacterVisual + day-night"
```

---

## Task 17: Write wiki systems page

**Files:**
- Create: `wiki/systems/shadows.md`

**Rationale:** Per `wiki/CLAUDE.md` — architecture lives in wiki, procedures live in skills. No duplication; wiki links to the skill and source files.

- [ ] **Step 1: Check wiki rules**

```bash
cat wiki/CLAUDE.md | head -60
```

Follow the project's wiki page conventions (frontmatter, link style, one-sentence-per-line where that's the convention).

- [ ] **Step 2: Write the wiki page**

```markdown
# Shadows

Real URP directional-light cast shadows for every 2D sprite in the 3D world.

## Architecture

Sprites are vertical quads with an alpha-tested `ShadowCaster` pass in [MWI/Sprite-Lit-ShadowCaster](../../Assets/Shaders/Sprite-Lit-ShadowCaster.shader). The project's existing directional sun — driven by [DayNightCycle](../../Assets/Scripts/DayNightCycle/DayNightCycle.cs) off [TimeManager](../../Assets/Scripts/DayNightCycle/TimeManager.cs) — rotates, and shadows follow automatically.

Each sprite category is opt-in via material swap + `shadowCastingMode = On` on its prefab:
- Characters — on `CharacterArchetype` visual prefabs
- Trees / foliage — on tree prefabs under `Assets/Prefabs/World/`
- Furniture — on `FurnitureInteractable` prefabs
- Props / items — runtime-driven by `ItemSO.CastsShadow`
- Buildings — ProBuilder meshes with URP default Lit

Interiors each contain a `ShadowsOnlyRoof` child quad that blocks sun leak from above without adding visible geometry — composable with any future camera cut-away system.

## Time-of-day coupling

[DayNightCycle](../../Assets/Scripts/DayNightCycle/DayNightCycle.cs) drives both intensity and shadow strength via two independent AnimationCurves (`_intensityCurve` + `_shadowStrengthCurve`). Decoupling lets dawn/dusk run dim-sun + dramatic-shadows for golden-hour silhouettes.

## Multiplayer

Zero networked surface. All clients compute the same sun direction because `TimeManager` state is shared — shadow direction is implicitly consistent.

## Spine migration

Spine characters already have `Spine-Skeleton-Lit-ZWrite.shader` (includes ShadowCaster pass). No interface change to `ICharacterVisual`.

## See also

- Procedure: [.agent/skills/rendering/shadows/SKILL.md](../../.agent/skills/rendering/shadows/SKILL.md)
- Design spec: [docs/superpowers/specs/2026-04-19-2d-sprite-cast-shadows-design.md](../../docs/superpowers/specs/2026-04-19-2d-sprite-cast-shadows-design.md)
- URP config: [Assets/Settings/PC_RPAsset.asset](../../Assets/Settings/PC_RPAsset.asset), [Mobile_RPAsset.asset](../../Assets/Settings/Mobile_RPAsset.asset)
```

- [ ] **Step 3: Regenerate wiki INDEX**

Run the project's wiki `/map` slash command (or update `wiki/INDEX.md` manually if preferred) so the new page is discoverable.

- [ ] **Step 4: Commit**

```bash
git add wiki/
git commit -m "docs(wiki): add systems/shadows page linking shader, skill, spec"
```

---

## Task 18: Log future-agent seed + close out

**Files:**
- Update: `C:\Users\Kevin\.claude\projects\...\memory\project_future_agents.md` (the auto-memory entry)

**Rationale:** Project rule 29 — this work doesn't yet warrant a dedicated Visual/Rendering agent, but it's the first concrete seed.

- [ ] **Step 1: Append seed note to memory**

Open the memory entry `project_future_agents.md`. Under the Visual/Rendering Specialist section, add one line:

```markdown
- Seed #1 (2026-04-19): 2D Sprite Cast Shadows system — `Sprite-Lit-ShadowCaster.shader`, `DayNightCycle._shadowStrengthCurve`, `ShadowsOnlyRoof` convention, URP cascade config. See plan `docs/superpowers/plans/2026-04-19-2d-sprite-cast-shadows.md`.
```

- [ ] **Step 2: Final sanity — run all EditMode tests**

In Unity: `Window → General → Test Runner → EditMode → Run All`.
Expected: all tests pass, including the two added in Tasks 1 & 2.

- [ ] **Step 3: Final commit**

```bash
# Memory file is outside the repo; no git commit needed for it.
# Final commit only if any fixup was needed in step 2.
git status
# If clean: done.
```

---

## Self-review (plan author's check, before handoff)

**Spec coverage:**
- §3 Decision (hand-written shader, per-prefab opt-in) — Tasks 3, 7–11
- §4.1 Shader passes — Task 3
- §4.2 Per-prefab opt-in table — Tasks 7–11
- §4.3 URP + sun config — Tasks 5, 6
- §4.4 DayNightCycle hook — Task 2
- §4.5 Indoor occlusion — Task 11
- §4.6 Night (no-op) — verified Task 12
- §5 Spine compatibility — Task 14
- §6 Asset layout — all create/modify paths match
- §7 Multiplayer (no-op) — documented in skill + wiki
- §8 Save/load (no-op) — documented
- §9 Testing (all 8 entries) — Tasks 4, 12, 13, 14
  - Sun sweep — Task 12 Step 1
  - Facing-flip invariance — Task 12 Step 2
  - Interior occluder leak — Task 11 Step 3
  - Cascade boundary crossing — Task 12 Step 3
  - Small-item cutoff — Task 13 Step 1
  - Performance — Task 13 Step 2
  - Golden-hour art — Task 13 Step 3
  - Spine smoke test — Task 14
  - Rule 26 compliance — Task 12 Step 1 (piggybacked on sun-sweep)
- §10 Documentation obligations — Tasks 15, 16, 17
- §11 Agent seed — Task 18
- §12 Out-of-scope — explicitly excluded

**Type / name consistency:** `CastsShadow` (property) / `castsShadow` (tooltip text reference) / `_castsShadow` (field) — consistent across `ItemSO` (Task 1) and `WorldItem` wiring (Task 10). `_shadowStrengthCurve` name consistent across `DayNightCycle` (Task 2) and scene config (Task 2 Step 5) and skill doc (Task 16). Shader name `MWI/Sprite-Lit-ShadowCaster` consistent across shader creation (Task 3), material creation (Task 4), and skill doc (Task 15).

**No placeholders found.** Every code block is concrete; every verification step names the expected observable.
