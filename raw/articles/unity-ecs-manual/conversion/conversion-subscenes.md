---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/conversion-subscenes.html
fetched: 2026-05-05
section: conversion
---

# Subscenes Overview

## Overview

The entity component system (ECS) uses subscenes instead of scenes for managing application content because Unity's core scene system is incompatible with ECS.

You can incorporate GameObjects and MonoBehaviour components into subscenes, where a process called "baking" converts them into entities and ECS components. Custom bakers can be created to attach additional ECS components to converted entities.

## Creating Subscenes

### Create an Empty Subscene

1. Open the target scene
2. Right-click in the Hierarchy window
3. Select **New Sub Scene** > **Empty Scene**

### Create a Subscene from Existing GameObjects

1. Open the scene containing desired GameObjects
2. Select the GameObjects in the Hierarchy window
3. Right-click and select **New Sub Scene** > **From Selection**

### Add an Existing Subscene

1. Open the target scene
2. Create an empty GameObject
3. Add the `SubScene` component
4. Set the **Scene Asset** property to your desired scene

## Subscene Component Behavior

The `SubScene` component triggers baking and streaming. When enabled with `AutoLoadScene` set to true, it streams in the referenced scene. Enable **Auto Load Scene** in the Inspector by selecting the subscene and checking the checkbox.

### Open Subscenes

When opened, subscenes display:

- Authoring GameObjects in the Hierarchy beneath the `SubScene` component GameObject
- Runtime entities or authoring GameObjects based on Scene View Mode settings
- Initial baking pass on all authoring components
- Incremental baking triggered by component changes

### Closed Subscenes

"Unity streams in the content of the baked scene" when closed. Entities take several frames to become available in Play mode and aren't available immediately in builds.

> "Unity doesn't stream the content of opened subscenes. The entities in an open subscene are immediately available when you enter Play mode."

**Note:** Subscene changes made during Play mode persist after exiting, unlike standard MonoBehaviour scenes.

## Outgoing Links

- https://docs.unity3d.com/6000.0/Documentation/Manual/CreatingScenes.html - Scene system documentation
- ../api/Unity.Scenes.SubScene.html - SubScene API reference
- baking-overview.html - Baking overview
- conversion-scene-overview.html - Scenes overview
- streaming-scenes.html - Scene streaming
- editor-preferences.html - Preferences window
- https://docs.unity3d.com/Manual/TermsOfUse.html - Trademarks and terms of use
- https://unity.com/legal - Legal
- https://unity.com/legal/privacy-policy - Privacy Policy
- https://unity.com/legal/cookie-policy - Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information - Do Not Sell or Share My Personal Information
