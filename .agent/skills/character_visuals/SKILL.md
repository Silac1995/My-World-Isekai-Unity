---
description: 2.5D visual rendering (Billboarding), Race Presets, and logical architecture of body parts (Eyes, Hands) in preparation for Spine2D.
---

# Character Visuals System Skill

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
