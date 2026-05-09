---
type: system
title: "Character Dismemberment"
tags: [character, combat, visuals, dismemberment, prosthetics, tier-2, planned]
created: 2026-04-22
updated: 2026-04-22
sources: []
related:
  - "[[character]]"
  - "[[visuals]]"
  - "[[character-equipment]]"
  - "[[combat]]"
  - "[[items]]"
  - "[[kevin]]"
status: planned
confidence: low
primary_agent: character-system-specialist
secondary_agents:
  - item-inventory-specialist
owner_code_path: "Assets/Scripts/Character/CharacterDismemberment/"
depends_on:
  - "[[character]]"
  - "[[visuals]]"
  - "[[combat]]"
depended_on_by:
  - "[[visuals]]"
  - "[[character-equipment]]"
---

# Character Dismemberment

## Summary
Permanent body-part loss and prosthetic replacement system. When a character suffers a qualifying combat hit (severing attacks, critical damage thresholds), an affected body part is permanently amputated — it vanishes visually, gameplay abilities dependent on it are disabled, and the wound persists across sessions via the character save profile. A compatible prosthetic item can be crafted or looted and **equipped to restore the part visually**, with gameplay restrictions depending on the prosthetic's tier (wooden peg leg: slower movement; steel arm: reduced grip strength; etc.). This page is a design plan — no implementation exists yet.

## Purpose
Add a permanent-consequence layer to combat: some hits leave lasting marks that shape the character's identity and drive gameplay/narrative choices (amputee farmer → pays a blacksmith for a steel prosthetic → regains farming ability with modified stats). Differentiates high-stakes combat from generic HP drain.

## Responsibilities
- Track which body parts are amputated on each character (persistent state).
- Trigger amputation from combat events meeting dismemberment criteria.
- Route visual side-effects to [[visuals]] (hide segments, attach blood VFX, later attach prosthetic skin).
- Expose amputation state to gameplay systems that gate abilities (combat, carrying, movement) via queries.
- Manage prosthetic items (a subtype of [[items]]) — equipping, unequipping, compatibility.

**Non-responsibilities**:
- Does **not** compute combat damage — [[combat]] decides when dismemberment triggers.
- Does **not** render the body — hides/shows slots via [[visuals]]'s `ICharacterPartCustomization`.
- Does **not** own prosthetic item definitions — those are `ProstheticItemSO` assets under [[items]].
- Does **not** heal amputations — amputations are permanent by design.

## Key classes / files

_All planned — none exist yet._

| File | Role |
|---|---|
| `CharacterDismemberment.cs` | Root component. Owns amputation state + equipped prosthetics. |
| `ICharacterDismemberment.cs` | Query interface for read-only access by combat, movement, etc. |
| `BodyPartId.cs` | Enum: `ArmLeft`, `ArmRight`, `LegLeft`, `LegRight`, `Head`, `HandLeft`, `HandRight`, `FootLeft`, `FootRight`, `Ear_L`, `Ear_R`, `Eye_L`, `Eye_R`, `Finger_*`. |
| `DismembermentSaveData.cs` | Serializable struct: list of amputated parts + equipped prosthetics. Integrates with `ICharacterSaveData<T>`. |
| `ProstheticItemSO.cs` | ScriptableObject under [[items]]. Fields: `BodyPartId targetPart`, `string spineSkinName`, stat modifiers, durability, compatibility tier. |
| `DismembermentCriteria.cs` | Data-driven rules: which damage types + thresholds trigger which body parts. |

## Public API / entry points

### Mutations (authoritative on server)
- `CharacterDismemberment.Dismember(BodyPartId part, DismembermentCause cause)` — permanent amputation. Updates save data, routes visual update, emits `OnPartLost` event.
- `CharacterDismemberment.AttachProsthetic(BodyPartId part, ProstheticItemSO prosthetic)` — equip prosthetic. Must match `part`; validates compatibility; routes visual update.
- `CharacterDismemberment.RemoveProsthetic(BodyPartId part)` — unequip (returns to inventory). Visual reverts to stump.

### Queries (any caller)
- `bool IsDismembered(BodyPartId part)` — part lost, no prosthetic.
- `bool HasProsthetic(BodyPartId part)` — part lost, prosthetic equipped.
- `bool IsFunctional(BodyPartId part)` — `!IsDismembered || HasProsthetic` — use this for ability gating.
- `ProstheticItemSO GetProsthetic(BodyPartId part)` — current prosthetic on that slot, null if none.

### Events
- `event Action<BodyPartId, DismembermentCause> OnPartLost` — combat VFX, dialogue triggers, achievement checks.
- `event Action<BodyPartId, ProstheticItemSO> OnProstheticAttached` / `OnProstheticRemoved` — equipment UI refresh.

## Data flow

```
Combat hit lands
       │
       ▼
CharacterCombat.ApplyDamage(hit)
       │
       └── if DismembermentCriteria.CheckTrigger(hit): 
            │
            ▼
CharacterDismemberment.Dismember(partId, cause)   [server]
       │
       ├── SaveData.amputatedParts.Add(partId)
       ├── Equipment.ForceUnequipOn(partId)           ← glove on lost hand → inventory
       ├── Visual.ICharacterPartCustomization.RemovePart("upperarm_R_base")
       │   Visual.ICharacterPartCustomization.RemovePart("forearm_R_base")
       │   Visual.ICharacterPartCustomization.RemovePart("hand_R_base")
       │   Visual.SpawnStumpVFX(partId)
       │
       ├── NetworkVariable<DismembermentState>.Value = updated   → replicates to clients
       └── OnPartLost(partId, cause) fires

Client receives state change
       │
       └── Applies same visual steps locally (no RPC — state-driven)


Player crafts prosthetic, drags onto amputation slot
       │
       ▼
CharacterDismemberment.AttachProsthetic(ArmRight, woodenArmSO)   [server]
       │
       ├── Validates: IsDismembered(ArmRight) = true
       ├── Validates: prosthetic.targetPart == ArmRight
       ├── SaveData.equippedProsthetics[ArmRight] = prostheticInstance
       ├── Visual.CombineSkins(..., prosthetic.spineSkinName)   ← e.g. "prosthetic/wooden_arm_R"
       ├── Stats.ApplyModifiers(prosthetic.modifiers)
       └── NetworkVariable update → clients re-apply skin
```

## Dependencies

### Upstream (this system needs)
- [[character]] — subsystem of the facade.
- [[visuals]] — uses `ICharacterPartCustomization.SetPart/RemovePart` for slot hiding + prosthetic skin overlay.
- [[combat]] — source of dismemberment triggers (damage type, hit location, crit).
- [[items]] — `ProstheticItemSO` is an item subtype.

### Downstream (systems that consume this)
- [[visuals]] — queries state when rebuilding the visual on load.
- [[character-equipment]] — refuses to equip gloves/boots on amputated hands/feet via `IsFunctional` check.
- [[combat]] — gates abilities (can't two-hand weapon with one arm; can't kick without a leg).
- [[character-movement]] — reduces speed for missing/prosthetic legs.
- [[character-needs]] / [[character-skills]] — long-term stat penalties for missing limbs.
- [[character-speech]] — NPCs may react to visible amputations in dialogue.

## State & persistence

- **Runtime state**: `Dictionary<BodyPartId, PartState>` where `PartState` is `{ bool amputated, ProstheticInstance prosthetic }`.
- **Persisted state**: via `ICharacterSaveData<DismembermentSaveData>` — integrated into the character profile JSON alongside equipment, stats, etc. (project rule #20).
- **Network**: `NetworkVariable<DismembermentState>` on the root Character. Server-authoritative. Clients apply visual state on `OnValueChanged`.
- **Permanence**: amputations never auto-heal. Only endgame magical items or specific dialogue outcomes could reverse them — out of scope for the initial system.

## Spine visual integration

### Hiding an amputated limb

Each limb = a set of slots with consistent naming. On `Dismember(ArmRight)`, iterate all base-layer slots matching `*_R_base` in the arm chain and null their attachments:

```csharp
string[] armRightSlots = { "upperarm_R_base", "forearm_R_base", "hand_R_base" };
foreach (var slot in armRightSlots)
    visual.RemovePart(slot);

// Also hide clothing/armor layers on those segments (otherwise a floating glove sleeve remains)
foreach (var slot in armRightSlots.Select(s => s.Replace("_base", "_clothing")))
    visual.RemovePart(slot);
// Same for _armor layer.
```

### Stump sprite (optional)

A stump attachment replaces the clean cut — e.g. `stump/upperarm_R_clean` or `stump/upperarm_R_bandaged`. Stored in the `stump/` skin category, applied to a dedicated `upperarm_R_stump` slot positioned at the joint.

### Prosthetic attachment

Prosthetics are **skins**, not bone attachments. Each prosthetic fills the base-layer slots it replaces:

```
Skins/prosthetic/
├── wooden_arm_R     ← fills upperarm_R_base, forearm_R_base, hand_R_base
├── steel_arm_R      ← same slots, different mesh + material
├── peg_leg_R        ← fills thigh_R_base, shin_R_base, foot_R_base
└── hook_hand_R      ← fills hand_R_base only (forearm stays organic if forearm not lost)
```

Equipment composition on `AttachProsthetic(ArmRight, woodenArm)`:
```csharp
visual.CombineSkins(
    "body/humanoid_male",
    "underwear/basic",
    "clothing/tshirt",
    "prosthetic/wooden_arm_R",   ← overrides hidden upperarm_R_base + chain
    ... 
);
```

Because prosthetics fill base-layer slots, they also receive **clothing layer** normally — a wooden arm wears a sleeve if a longsleeve t-shirt is equipped. This is an intentional design choice (prosthetics are visually part of the body).

### Fine-grained amputations (fingers, ears, eyes)

Requires additional slot granularity in the master skeleton:
- `finger_index_R_base`, `finger_middle_R_base`, ... (5 per hand).
- `ear_R_base`, `ear_L_base`.
- `eye_R_base`, `eye_L_base` (paired with eyepatch accessory skins).

Out of scope for initial implementation — phase these in later.

## Known gotchas / edge cases

- **Equipment on amputated slot** — unequip gloves/boots automatically on dismemberment; refuse equip attempts on amputated-without-prosthetic slots. [[character-equipment]] must consult `IsFunctional`.
- **Prosthetic compatibility** — an `ArmRight` prosthetic cannot go on `ArmLeft`. Validate at both UI layer (grey out slots) and server (reject invalid equip).
- **Partial arm loss** — hand lost but forearm intact: only `hand_R_base` is hidden. Prosthetic `hook_hand_R` fills only `hand_R_base`. The forearm keeps wearing the clothing sleeve normally.
- **Save migration** — characters created before this system exists need a default `DismembermentSaveData` with empty state. Handle missing field gracefully in the profile deserializer.
- **Networked visual rebuild** — state change triggers `CombineSkins` rebuild; never send RPCs for "hide slot X" — derive from state only.
- **Dismemberment during animation** — if the limb is mid-swing when amputated, the animation may look strange for one frame. Acceptable; the VFX + knockback hide this.
- **Dual-wield restrictions** — ability code must query `IsFunctional(ArmLeft) && IsFunctional(ArmRight)` before allowing a dual-wield stance.

## Open questions / TODO

- [ ] **Dismemberment criteria design** — which damage types / thresholds trigger? Per-weapon bias (axes dismember more than maces)?
- [ ] **Hit location system** — combat currently deals damage without hit-location data. Does dismemberment pick a random surviving limb, or does combat need a hit-location roll?
- [ ] **Prosthetic tiers and stat modifiers** — design pass with [[character-stats]]: how much penalty does a wooden leg impose vs a steel leg?
- [ ] **NPC dismemberment** — do hostile NPCs also suffer amputations? Performance impact if yes.
- [ ] **Combat ability gating** — full list of abilities blocked by each missing part. Needs cross-system inventory (combat + crafting + needs).
- [ ] **Visual stumps assets** — art pass for stump meshes per segment per skin tone.
- [ ] **Prosthetic crafting recipes** — integrate with [[crafting]] (pending).
- [ ] **`ICharacterSaveData` priority** — where in save order does dismemberment go? Before equipment (so equipment sees it) is required.

## Change log
- 2026-04-22 — Initial design page created. Status: planned, confidence low — no implementation, pending combat hit-location system design. — Claude / [[kevin]]

## Sources
- 2026-04-22 conversation with Kevin — dismemberment + prosthetic requirement.
- [[visuals]] — skin composition and slot model.
- [[character-equipment]] — layer architecture that prosthetics override.
- Root [CLAUDE.md](../../CLAUDE.md) rules #20 (save data), #21 (per-subsystem SKILL.md), #22 (player ↔ NPC parity).
