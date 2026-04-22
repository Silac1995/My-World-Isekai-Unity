---
type: system
title: "Visuals"
tags: [visuals, sprites, spine, animation, clothing, wounds, tier-2]
created: 2026-04-19
updated: 2026-04-22
sources: []
related:
  - "[[character]]"
  - "[[character-equipment]]"
  - "[[character-dismemberment]]"
  - "[[items]]"
  - "[[kevin]]"
status: wip
confidence: medium
primary_agent: character-system-specialist
secondary_agents: []
owner_code_path: "Assets/Scripts/Character/"
depends_on:
  - "[[character]]"
depended_on_by:
  - "[[character]]"
  - "[[character-equipment]]"
  - "[[character-dismemberment]]"
  - "[[combat]]"
---

# Visuals

## Summary
2D sprites in a 3D environment (project rule #17). Visual abstraction happens via `ICharacterVisual` (the interface every visual backend implements), `IAnimationLayering`, `ICharacterPartCustomization`, and `IBoneAttachment`. Current backend: Unity sprite-based body-parts controller (layered sprites, animator clips). **Planned migration**: Spine 2D. Because save data is visual-system-agnostic, the migration does not require touching character persistence. This page documents both the current sprite approach and the target Spine architecture (clothing, physics, wounds, cross-archetype sockets).

## Purpose
Keep gameplay logic blind to the rendering backend. Character subsystems call `ICharacterVisual.SetFacingDirection`, `PlayAnimation`, `SetTint` — the implementation can be sprite-swap today, Spine tomorrow, 3D skinned mesh later, without code churn outside the visual module.

## Responsibilities
- Rendering character sprites / skeletons / particles.
- Applying per-`ItemInstance` color injection via Material Property Block (project rule #25: shader-first, no batching break).
- Animation sync across the network (owner animation + observer replay).
- Body-part customization (clothing layers, hair, face, ...).
- Bone attachment for weapons, accessories, bag sockets.
- Blink, facial expressions, ambient idle cycles.
- Wound overlays (bruises, cuts) — visual feedback for damage.
- Hiding dismembered body parts and showing prosthetic replacements (see [[character-dismemberment]]).

**Non-responsibilities**:
- Does **not** own character save data — visuals rebuild from the character profile on load.
- Does **not** own combat damage calc — just visual feedback (target indicator, damage numbers).
- Does **not** own dismemberment state — persisted by [[character-dismemberment]]; visuals only render it.
- Does **not** own UI — see [[player-ui]].

## Key classes / files

| Layer | Scripts |
|---|---|
| Visual abstraction | [ICharacterVisual.cs](../../Assets/Scripts/Character/Visual/ICharacterVisual.cs), [ICharacterPartCustomization.cs](../../Assets/Scripts/Character/Visual/ICharacterPartCustomization.cs), [IAnimationLayering.cs](../../Assets/Scripts/Character/Visual/IAnimationLayering.cs), [IBoneAttachment.cs](../../Assets/Scripts/Character/Visual/IBoneAttachment.cs) |
| Sprite backend (current) | `CharacterVisual.cs`, `CharacterBodyPartsController/` (13 files) |
| Body-part controllers | `EyesController.cs`, `HairController.cs`, `HandsController.cs`, `MouthController.cs`, `EarsController.cs` |
| Animation | `CharacterAnimator.cs`, `AnimationSync/` |
| Facial / ambient | `CharacterBlink.cs` |
| Speech bubbles | [[character-speech]] |
| Gender / race visual | `CharacterGender/` |
| Spine backend (planned) | `SpineCharacterVisual.cs` (to create), references `Assets/Spine Examples/Scripts/MixAndMatch.cs` |

## Public API / entry points

From the `ICharacterVisual` contract (backend-agnostic):

- `Initialize(Character, CharacterArchetype)` — wire visuals to the owning character.
- `PlayAnimation(AnimationKey, loop)` — base-track animation.
- `SetFacingDirection(float)` — left/right flip.
- `SetTint(Color)`, `SetHighlight(bool)`, `SetVisible(bool)` — MPB-driven feedback.
- `ConfigureCollider(Collider)` — physics shape.

Optional interfaces (implement only if the backend supports them):
- `ICharacterPartCustomization.SetPart(slotName, attachmentName)` / `CombineSkins(...)` — swap equipment-layer attachments.
- `ICharacterPartCustomization.SetPartColor(slotName, Color)` / `SetPartPalette(slotName, Texture2D)` — per-slot color via MPB or LUT.
- `IAnimationLayering.PlayOverlayAnimation(key, layer, loop)` — overlay tracks (Track 1+ in Spine).
- `IBoneAttachment.GetBoneTransform(boneName)` / `AttachToSocket(socketName, GameObject)` — accessory/weapon mounting.

## Data flow

```
Character.Awake()
   │
   ▼
CharacterArchetype.VisualPreset  ← selects backend (sprite humanoid / Spine humanoid / sprite quadruped)
   │
   ▼
ICharacterVisual.Initialize(character, archetype)
   │
   ├── Loads skeleton / sprite library
   ├── Registers body-part controllers
   └── Applies archetype defaults (skin color, gender, race)

Equipment change
   │
   ▼
CharacterEquipment.Equip(item)
   │
   ├── layer.Set(instance)
   └── visual.SetPart(slot, skinName) OR visual.CombineSkins(...)
        │
        ├── Sprite backend → sprite library lookup + SpriteResolver swap
        └── Spine backend  → skin composition + SetSlotsToSetupPose()

Combat damage
   │
   ▼
CharacterWounds.AddWound(WoundType, BodyRegion)
   │
   └── visual.AddWound(...)        ← skin overlay OR shader MPB (see §Wounds)

Dismemberment trigger
   │
   ▼
CharacterDismemberment.Dismember(BodyPartId)   ← see [[character-dismemberment]]
   │
   ├── Persists in character profile
   └── visual.HidePart(BodyPartId) or visual.SetPart(slot, "prosthetic/...")
```

Server authority: damage, dismemberment state, equipment state. Clients receive state via NetworkVariable/RPC and call local visual methods (no visual sync RPC — visuals rebuild from state).

## Shader-first rule (project rule #25)

Dynamic visual feedback (target indicators, health bars, fade, damage flashes, wound intensity) **must** use Material Property Blocks + shaders. `Image.fillAmount`, `Graphic.color`, and sprite vertex manipulation are forbidden on hot paths — they break batching and cost CPU-to-GPU transfers. Example: the combat target indicator lerps Green→Yellow→Red via `Material.SetFloat("_HealthPercent")` on a custom unlit UI shader.

For character customization, prefer **Palette Swapping (LUT)** over global `SetTint` — preserves artistic shading and can animate per-slot independently.

---

## Spine Skeleton Architecture (Planned)

Every humanoid character shares a **single master skeleton** that bakes in all possible bone chains — including ones not every outfit uses. Rationale: equipment/items only swap skin meshes; they never add/remove bones. An unused chain costs memory but no code churn.

### Bone chains baked into the master skeleton

| Chain | Bones | Used by |
|---|---|---|
| Core body | `root`, `hip`, `spine_lower`, `spine_upper`, `neck`, `head` | Always |
| Arms | `shoulder_R/L`, `upperarm_R/L`, `forearm_R/L`, `hand_R/L` | Always |
| Legs | `thigh_R/L`, `shin_R/L`, `foot_R/L` | Always |
| Skirt chain | `skirt_front_01-03`, `skirt_left_01-03`, `skirt_right_01-03`, `skirt_back_01-03` | Skirts, dresses, robes |
| Cape chain | `cape_01-04` | Capes, cloaks |
| Long hair chain | `hair_long_01-03` (×N strands) | Long hair styles |
| Tail chain | `tail_01-04` | Beastmen, some archetypes |

All chain bones except core have **Physics Constraints** pre-configured in the Spine Editor (see §Physics-Enabled Garments below).

### Slot naming convention

Slots follow `<body_segment>_<layer>` format. One slot per layer per body segment. Draw order in Spine is layer-ordered (bottom → top: base → underwear → clothing → armor → accessories).

```
Body segments × layers:
├── BODY BASE
│   torso_base, upperarm_R_base, forearm_R_base, hand_R_base,
│   upperarm_L_base, ..., head_base, neck_base, thigh_R_base, ...
│
├── UNDERWEAR
│   torso_underwear, leg_R_underwear, leg_L_underwear
│
├── CLOTHING
│   torso_clothing, upperarm_R_clothing, forearm_R_clothing,
│   hand_R_clothing (gloves), head_clothing (helmet),
│   foot_R_clothing (shoes), ...
│
├── ARMOR
│   torso_armor, upperarm_R_armor, ... (mirrors clothing segments)
│
└── ACCESSORIES
    head_accessory, neck_accessory, hair_accessory_0, hair_accessory_1
```

### Front/back draw order for animation

For side-view animations (walk, attack), the "back" arm/leg must render behind the torso. Solution: each limb has **front and back slot variants** (`upperarm_R_back_clothing` + `upperarm_R_front_clothing`), and **Draw Order keys** in the animation switch which is visible depending on facing. Skins supply attachments for **both** slots — the animation handles visibility.

---

## Clothing Layer System (Underwear / Clothing / Armor)

Three layered categories. Each piece of clothing decomposes into **multiple attachments**, one per body segment it covers.

### Example — T-shirt decomposition

A t-shirt fills these slots (and leaves others empty):
- `torso_clothing`
- `upperarm_R_clothing`, `upperarm_L_clothing`
- `forearm_R_clothing`, `forearm_L_clothing` (only if long-sleeve)

Each attachment is a **region** (rigid) or **mesh weighted** (smooth at joints) attachment. Use region for rigid armor/helmets/shoes; use mesh weights for soft fabric that needs to deform across joints (cloth sleeves, pants at knees).

### Skin organization in the Spine Editor

Each equippable item = one Spine skin. Skins are organized hierarchically so multiple layers can compose:

```
Skins/
├── body/
│   ├── humanoid_male
│   └── humanoid_female
│
├── underwear/
│   ├── none
│   └── basic
│
├── clothing/
│   ├── none
│   ├── tshirt, longsleeve, hoodie          ← fill torso + arm segments
│   ├── jeans, shorts                       ← fill thigh + shin segments
│   ├── sneakers, boots                     ← fill foot segments
│   ├── gloves_basic, gauntlets             ← fill hand segments
│   └── cap, hood                           ← fill head segment
│
├── skirt/
│   ├── short_skirt                         ← mesh on hip + skirt_xxx_01/02
│   ├── long_skirt                          ← mesh on hip + skirt_xxx_01/02/03
│   └── dress                               ← torso + skirt chain
│
├── armor/
│   ├── leather_chest                       ← fills torso_armor + upperarm_R/L_armor
│   └── plate_full
│
├── accessories/
│   ├── glasses, monocle                    ← head_accessory
│   ├── necklace                            ← neck_accessory
│   └── hairpin_*                           ← hair_accessory_N
│
└── prosthetic/                             ← see [[character-dismemberment]]
    ├── wooden_arm_R, wooden_arm_L
    └── peg_leg_R, peg_leg_L
```

### Runtime composition

Equipment state → skin composition:

```csharp
visual.CombineSkins(
    "body/humanoid_male",
    "underwear/basic",
    "clothing/tshirt",
    "clothing/jeans",
    "clothing/sneakers",
    "armor/leather_chest",
    "accessories/glasses"
);
```

Internally: build a new `Skin` object by calling `AddSkin()` for each entry, then `skeleton.SetSkin(combined) + SetSlotsToSetupPose()`. **`SetSlotsToSetupPose()` is mandatory** — without it, attachments leak from the previous skin (see [[spine-unity]] SKILL).

---

## Physics-Enabled Garments

Spine 4.2+ Physics Constraints simulate soft garments (skirts, capes, long hair) natively — no Unity physics needed.

### Setup overview

1. **Chain of bones** parented to a rigid anchor (`hip` for skirts, `spine_upper` for capes).
2. **Mesh attachment** weighted across the chain bones (vertex weights decrease with distance from anchor).
3. **Physics Constraint** on each chain bone with tuned `Inertia`, `Strength`, `Damping`, `Mass`, `Gravity`, `Wind`, `Mix`.

Typical values for a skirt: Inertia 0.7, Strength 100, Damping 0.85, Gravity -10, Mix 1.0.

### Switching between physics and rigid garments

When a pantalon (rigid, no physics bones in its mesh) replaces a jupe (physics-bound mesh), the skirt chain bones still exist — their simulation continues invisibly, wasting CPU. Solution: toggle each `PhysicsConstraint.Mix` (0 = disabled, 1 = active) based on the equipped item.

Each `ClothingItemSO` declares `string[] requiredPhysicsGroups` (e.g. `["skirt"]` or `[]`). On equipment change, `SpineCharacterVisual.UpdatePhysicsState()` enables only the constraints whose group is required; disables the rest.

**Reset on activation** — when switching from `Mix=0` back to `Mix=1`, call `constraint.Reset()` to clear accumulated velocity. Without reset, the newly-activated chain oscillates violently for 1-2s because it interprets the sudden re-activation as a brutal impulse.

### Collision with environment

Spine Physics Constraints do **not** collide with Unity world geometry. For top-down / side-view characters this is acceptable. If a specific garment needs collision (cape snagging on a hook), expose its chain bones as Unity Transforms via `SkeletonUtilityBone` and wire `HingeJoint2D`. Reserve this for specific cases — it breaks perf and complicates animation.

---

## Cross-Archetype Equipment Sockets

Humanoid, quadruped, and beastman archetypes have different skeletons. A cap equipped on any of them must "know" where the head is without hardcoding bone names.

### Solution — `EquipmentSocketMap` ScriptableObject

Each `CharacterArchetype` references a `EquipmentSocketMap` SO that declares its logical sockets and maps them to concrete bones with per-archetype offset / rotation / scale:

```
Humanoid SocketMap:
  head    → bone "head",       offset (0, 0.02, 0),    scale (1, 1, 1)
  back    → bone "spine_upper", offset (0, 0, -0.1)
  hand_R  → bone "hand_R"

Wolf SocketMap:
  head    → bone "skull",       offset (0.05, 0, 0),    rotation (0,0,-20), scale (0.8, 0.8, 1)
  back    → bone "spine_mid",   offset (0, 0.1, 0)
  (no hand_R)
```

### API

```csharp
public interface IBoneAttachment
{
    Transform GetBoneTransform(string boneName);
    void AttachToSocket(string socketName, GameObject obj);
    void DetachFromSocket(string socketName, GameObject obj);
}
```

Equipment code calls `visual.AttachToSocket("head", capPrefab)` — the visual backend resolves socket → bone via the archetype's SocketMap, adds a `BoneFollower`, and applies offsets. If the socket is unsupported (wolf has no hands), the attachment silently fails (item is hidden).

Cross-references: [[character-equipment]] §Socket routing, [[character-archetype]] §VisualPreset.

---

## Wound Visual System

Two mechanisms serve different wound types:

### Option A — Skin overlays (narrative wounds)

Cuts and discrete bruises are skins in the `wounds/` skin category, applied on top of the base body skin:

```
Skins/wounds/
├── bruise_face, bruise_arm_R, bruise_torso
├── cut_arm_L, cut_leg_R
└── heavy_damage (combo skin)
```

Applied via `ICharacterWoundVisual.AddWound(type, region)` → `CombineSkins(..., "wounds/cut_arm_L")`. Persisted in character profile (wounds heal over time or on rest).

### Option B — Shader MPB overlays (dynamic damage)

For real-time damage feedback (character progressively more battered as HP drops), a custom Spine shader accepts N wound positions via MPB vector array:

```csharp
_BruisePositions[N] = float4(uv_x, uv_y, radius, intensity)
_BruiseCount       = active count
_BruiseTex         = bruise texture (tiled)
```

Positions can be random (`Random.insideUnitCircle` mapped to UV space) or bone-driven (project bone world position to UV). Intensity ramps with `1 - HP/MaxHP`. Cleared on heal.

**Random bruise placement** — `ApplyRandomBruises(int count)` generates up to 4 random UV positions with randomized radius/intensity, pushed via MPB. Respects project rule #25 (no batching break).

### When to use which

- **Narrative wounds** (persists, heals slowly, drives dialogue) → Option A.
- **Combat visceral feedback** (real-time HP % feedback, dissipates on heal) → Option B.
- Both can coexist — they use different shader channels / skin slots.

---

## Dependencies

### Upstream
- [[character]] — Visuals is a subsystem of the character facade.
- [[character-archetype]] — declares which backend + SocketMap to use.

### Downstream
- [[character-equipment]] — drives skin composition + socket attachments.
- [[character-dismemberment]] — drives part hiding + prosthetic skin overlay.
- [[combat]] — drives wound overlays + hit-react animations.
- [[character-speech]] — anchored to `head` bone.

## State & persistence

- **Visuals own no persistent state.** On load, visuals rebuild from:
  - `CharacterArchetype` (archetype + SocketMap).
  - `CharacterEquipment` (current skin composition).
  - `CharacterDismemberment` (hidden parts + prosthetics) — see [[character-dismemberment]].
  - `CharacterWounds` state (persistent narrative wounds).
- **Network**: equipment and dismemberment state replicate via NetworkVariable; visuals are local reconstructions — never send visual RPCs.

## Known gotchas / edge cases

- **Forgetting `SetSlotsToSetupPose()`** after a skin swap leaks attachments from the previous skin. Always call it.
- **Physics oscillation on switch** — a physics chain reactivated from `Mix=0` oscillates wildly unless `constraint.Reset()` is called.
- **Socket unsupported on archetype** — `AttachToSocket` must fail silently (hide the attached object), never throw.
- **Z-fighting between layers** — the slot draw order in Spine is the source of truth. Underwear slots must precede clothing slots precede armor slots in the slot list. Breaking this causes armor to render under the t-shirt.
- **Batching break via `Graphic.color` / `SpriteRenderer.color`** — always use MPB. Project rule #25.
- **Teleport oscillation** — after a character teleports, physics chains oscillate because the delta is huge. Call `visual.ResetPhysics()` after any position snap.
- **Dismembered parts still rendering** — a skin swap that doesn't include an explicit `prosthetic/none` attachment can leak an old prosthetic. Always include base or prosthetic explicitly in `CombineSkins()`.

## Open questions / TODO

- [ ] `SpineCharacterVisual.cs` implementation — not yet written.
- [ ] Palette swapping (LUT) vs tint — project rule #25 recommends LUT for customization. Confirm usage patterns per slot.
- [ ] Networked visual state — blink/facial sync. Currently assumed local cosmetic only; confirm with combat sync requirements.
- [ ] Exact `EquipmentSocketMap` schema — SO structure to finalize alongside archetype refactor.
- [ ] Wound shader — author the MPB-driven multi-position bruise overlay shader (target: `MWI/Spine-Wounds-Lit`).
- [ ] Migration order — sprite backend parity with Spine before flip day. Track in `project_spine2d_migration.md`.

## Change log
- 2026-04-19 — Stub. Confidence medium — Spine migration reshapes this page's scope. — Claude / [[kevin]]
- 2026-04-22 — Expanded substantially. Added Spine skeleton architecture, clothing layer system, physics-enabled garments, cross-archetype socket mapping, wound visual system. Linked new [[character-dismemberment]] page. Confidence still medium — Spine implementation not yet written. — Claude / [[kevin]]

## Sources
- [ICharacterVisual.cs](../../Assets/Scripts/Character/Visual/ICharacterVisual.cs)
- [ICharacterPartCustomization.cs](../../Assets/Scripts/Character/Visual/ICharacterPartCustomization.cs)
- [IAnimationLayering.cs](../../Assets/Scripts/Character/Visual/IAnimationLayering.cs)
- [IBoneAttachment.cs](../../Assets/Scripts/Character/Visual/IBoneAttachment.cs)
- [.agent/skills/character_visuals/SKILL.md](../../.agent/skills/character_visuals/SKILL.md)
- [.agent/skills/spine-unity/SKILL.md](../../.agent/skills/spine-unity/SKILL.md) — migration target + procedural recipes.
- [Assets/Spine Examples/Scripts/MixAndMatch.cs](../../Assets/Spine%20Examples/Scripts/MixAndMatch.cs) — reference implementation for skin composition.
- 2026-04-22 conversation with Kevin — clothing layer design, physics switching, cross-archetype sockets, wounds, dismemberment.
- Root [CLAUDE.md](../../CLAUDE.md) rules #17, #25.
