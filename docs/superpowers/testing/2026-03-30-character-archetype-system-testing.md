# Character Archetype System — Testing Guide

**Branch:** `feature/character-archetype-system`
**Date:** 2026-03-30

## 1. Compilation Check

- Switch to Unity. It should auto-recompile when it detects the new files.
- **Expected:** Zero compilation errors in the Console.

## 2. Registry Verification (Play Mode)

- Enter Play Mode in your normal test scene.
- **Expected:** Characters spawn and behave exactly as before — walking, interacting, combat, everything unchanged.
- Open a Character in the Inspector. All subsystem references should still be assigned via `[SerializeField]`.

## 3. Quick Registry Debug (Optional)

To verify the registry is populating, temporarily add this to `Character.cs` in `OnNetworkSpawn()`:

```csharp
Debug.Log($"[Registry] {CharacterName} has {_allCapabilities.Count} capabilities");
```

**Expected:** ~30 capabilities registered per character. Remove the log after confirming.

## 4. Interaction Sanity Check

- Right-click an NPC to open the interaction menu.
- **Expected:** Same options as before (Follow, Greet, Party Invite, Talk, etc.). No new options yet — no subsystems implement `IInteractionProvider` yet.

## 5. Verify New Files Exist

In the Project window, confirm these paths:

| Path | Contents |
|------|----------|
| `Assets/Scripts/Character/Archetype/` | BodyType, MovementMode, WanderStyle, CharacterArchetype |
| `Assets/Scripts/Character/Visual/` | AnimationKey, AnimationProfile, ICharacterVisual, IAnimationLayering, ICharacterPartCustomization, IBoneAttachment |
| `Assets/Scripts/Character/SaveLoad/` | IOfflineCatchUp, ICharacterSaveData |
| `Assets/Scripts/Interactable/` | InteractionOption.cs (standalone), IInteractionProvider.cs |

## 6. CharacterArchetype Asset Creation

- Right-click in Project > **Create > MWI > Character > Character Archetype**
- **Expected:** Inspector shows all fields: Identity, Capabilities, Locomotion, AI Defaults, Visual, Interaction.

## 7. Multiplayer Verification

- Host a session and have a client join.
- **Expected:** Both host and client characters behave identically to before the changes. No new errors on either side.
- Verify character switching (Player <-> NPC) still works via the existing debug commands.

## Key Principle

**Nothing should look or behave differently.** This was a foundation-only change. The new capabilities activate when non-humanoid archetypes are created or when Spine visual migration happens.
