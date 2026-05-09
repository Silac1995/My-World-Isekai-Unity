---
name: character-visuals
description: 2.5D visual rendering (Billboarding), Race Presets, and logical architecture of body parts (Eyes, Hands) in preparation for Spine2D.
---

# Character Visuals System

This skill details how character visuals (2D sprites in a 3D world) are handled, as well as the "Body Parts" system (Hands, Eyes, Hair, etc.).

> [!WARNING] (Spine2D Migration)
> Unity's local rendering engine **SpriteResolver** and **SpriteLibrary** (currently used by `CharacterEye`, `CharacterHand`...) is **temporary**. It will be entirely replaced by **Spine2D** rigs.
> **GOLDEN RULE**: Although the underlying *rendering* will change, **the logical API of Body Parts must be preserved**. Gameplay code (`CharacterActions`, `GoapActions`, `Animator`) **must** continue to call methods like `CharacterEye.SetClosed(true)` or `CharacterHand.SetPose("fist")`. It is this logical overlay that guarantees the project's modularity, regardless of the underlying animation technology.

## When to use this skill
- To configure new races (`RaceSO`) or manage visual presets (`CharacterVisualPresetSO`).
- To direct gaze or orient a sprite (`Billboarding`, `LookTarget`).
- When implementing a feature requiring a change in a character's expression (blinking, clenching fists during combat).
- To interact with `CharacterBodyPartsController`.

## Architecture & How to use it

### 1. Billboarding & Rendering (`CharacterVisual.cs`)
- **Billboarding**: The 2D character sprites always face the camera. This is managed by rotating the `transform` relative to the main camera's rotation.
- **Orientation (Flip)**: `IsFacingRight` controls the visual flip. It contains an anti-flickering safety (`FLIP_COOLDOWN = 0.15f`) and blocks flipping if the character is in the middle of Knockback.

### 2. Presets and Initialization
The `ApplyPresetFromRace(RaceSO)` method in `CharacterVisual` acts as an initialization hub.
- It delegates organ initialization to `CharacterBodyPartsController.InitializeAllBodyParts()`.
- It applies the `DefaultSkinColor` (or category) across the various sub-controllers (Ears, Hands, etc.).

### 3. Body Parts Logic (The Untouchable API)
The architecture uses a hub, `CharacterBodyPartsController`, which contains sub-controllers (`EyesController`, `HandsController`, etc.), themselves managing the final objects (`CharacterEye`, `CharacterHand`).

**The API that must survive Spine2D**:
- **Blinking / Closing (Eyes)**: `CharacterEye.SetClosed(bool)` is the source of truth to determine if an eye is closed (for sleeping, blinking, expressing pain).
- **Hand Poses (Hands)**: `CharacterHand.SetPose(string)` (e.g., "fist", "normal") dictates the state of the hand. It is designed to synchronize all layers (the thumb *and* fingers underneath the weapon).
- **Categories**: Methods like `SetCategory(string)` allow changing an entire component (e.g., going from a Human ear to an Elf ear).

## Tips & Troubleshooting
- **A sprite does not appear / Visual bug**: Verify that the logic actually calls the basic API (`SetPose()`, `SetClosed()`). The fact that the underlying technology is temporary (SpriteResolver) does not excuse bypassing the modular architecture!
- If you create a new GOAP/BT action (e.g., "Fall asleep"), don't forget to include the visual call for your actions: `Character.CharacterVisual.BodyPartsController.EyesController.SetClosed(true)`.

## Carry persistence (HandsController)

`HandsController` is a body-part controller, but it also owns gameplay state: the **carried in-hand item** (`CarriedItem` — distinct from the equipped weapon, which lives on `CharacterEquipment._weapon`). Because of this, it implements `ICharacterSaveData<HandsSaveData>`:
- `SaveKey = "HandsController"`, `LoadPriority = 35` — runs **after** `CharacterEquipment` (priority 30) so the weapon slot is restored first.
- `Serialize()` writes `ItemSO.ItemId` + `JsonUtility.ToJson(ItemInstance)`.
- `Deserialize()` rebuilds the `ItemInstance` from `Resources.LoadAll<ItemSO>("Data/Item")`, then calls a private `ApplyRestoredCarry` that bypasses the `AreHandsFree()` check (the saved state is the source of truth at that point).
- If `_hands.Count == 0` (the visual hierarchy hasn't been scanned yet — `Initialize()` not called), the rebuilt instance is parked in `_pendingRestoreItem` and consumed when `Initialize()` runs.

When extending the visual hand system, **do not** drop `_carriedItem` into a non-saved field or a separate sibling component without porting the save contract — the in-hand item must round-trip through bed-save / portal-save / pause-save.

## Carry visual — Network-component strip (CRITICAL)

`HandsController.AttachVisualToHand` instantiates `ItemSO.WorldItemPrefab` as a transform-child of the player's hand bone (so the carried item visually follows hand animation). The `WorldItemPrefab` carries a `NetworkObject` (because real dropped instances need it), and that's a problem for this carry-clone path: the clone is **never** `Spawn()`'d, so its `NetworkObject` is "homeless" — but it's still a transform-child of the player's spawned `NetworkObject`.

NGO's `SceneEventData.SortParentedNetworkObjects` walks every spawned root NO's `GetComponentsInChildren<NetworkObject>()` during initial-sync to a late joiner. With the carry visual still in the hierarchy, the unspawned clone is surfaced into the sync list, and `NetworkObject.Serialize` (`NetworkObject.cs:3172`) NREs because `NetworkManagerOwner == null`. **Symptom:** while the host is carrying any item, late-joining clients can never connect — host throws every `ProcessPendingApprovals` tick. Drop the item → next connection works.

**Fix (in place since 2026-05-01):** `AttachVisualToHand` calls a private static `StripNetworkComponents(_carriedVisual)` after the visual setup completes. The helper recursively `DestroyImmediate`'s every `NetworkBehaviour` (including the just-`Initialize`'d `WorldItem`) and every `NetworkObject`. Order matters — NetworkBehaviours first, then NetworkObject. After this, the carry visual is a pure rendering hierarchy with no networking surface, and NGO's child walk finds nothing.

**When extending the carry-visual flow** (e.g. swapping `WorldItemPrefab` for a different visual source, adding multi-hand carry, mirroring this for a backpack / shoulder visual): you MUST keep the strip in place, or switch to instantiating `ItemSO.ItemPrefab` directly (the visual sub-prefab without networking — same approach `StorageVisualDisplay` uses). See [[wiki/gotchas/dont-clone-prefabs-with-networkobject-for-visuals]] for the full rule and the alternative paths.

**Test multiplayer end-to-end:** the bug is host-only (only host writes scene-sync to joiners; client-side carry visuals never propagate). Always test "host carries → second client tries to join" — host-only smoke testing hides this for hours.

## Extensions beyond body parts

The logical API documented here (eyes, hands, mouth, ears, hair) is **one layer** of the visual system. The broader visual architecture — clothing layers (underwear/clothing/armor), physics-enabled garments (skirts/capes), wound overlays (bruises/cuts), dismemberment (amputations + prosthetics), and cross-archetype equipment sockets (cap on a human vs a wolf) — is documented here:

- **Architecture** (why/what/how-it-connects): see the [Visuals](../../wiki/systems/visuals.md) wiki page.
- **Procedures** (Spine-specific how-to recipes): see [spine-unity/SKILL.md](../spine-unity/SKILL.md).
- **Dismemberment system** (amputation + prosthetics): see the [Character Dismemberment](../../wiki/systems/character-dismemberment.md) wiki page — planned, not yet implemented.

## Shadow casting

`CharacterVisual` does not own shadow logic. Cast shadows are a pass on the material the `SpriteRenderer` already holds — the `MWI/Sprite-Lit-ShadowCaster` shader's `ShadowCaster` pass alpha-tests the sprite and writes depth only. A prefab opts in by swapping its `SpriteRenderer.sharedMaterial` to `Assets/Materials/Sprites/DefaultSpriteShadowCaster.mat` and setting `shadowCastingMode = On`.

Because the shadow pass renders from the sun's viewpoint (not the camera), the billboard rotation applied at runtime is irrelevant to the cast shadow — the quad's silhouette is what gets projected. `IsFacingRight` (scale.x flip) mirrors the quad in place, and the shadow mirrors correspondingly without re-orienting the caster plane.

After the Spine 2D migration, the `SkeletonAnimation.MeshRenderer` uses `Spine-Skeleton-Lit-ZWrite` instead (already in the project). Shadow behaviour stays identical; `ICharacterVisual` is not touched.

See: [.agent/skills/rendering/shadows/SKILL.md](../rendering/shadows/SKILL.md)
