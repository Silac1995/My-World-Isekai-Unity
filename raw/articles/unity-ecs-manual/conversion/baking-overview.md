---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/baking-overview.html
fetched: 2026-05-05
section: conversion
---

# Baking Overview

## Overview of Baking in Unity Entities

Baking is the process that "converts GameObject data in the Unity Editor (authoring data) into entities written to Entity Scenes (runtime data)." This transformation is one-way, converting flexible but performance-intensive GameObjects into optimized entities and components.

## Authoring and Runtime Data

Unity's GameObject data comprises both runtime and authoring components. The authoring and runtime separation provides essential flexibility during editing while maintaining runtime performance:

- **Authoring data**: Data created during application development, including scripts and assets. This data is flexible and human-readable.
- **Runtime data**: Data processed by the ECS during gameplay. This data is optimized for performance and storage efficiency.

The Inspector and Hierarchy display data mode circles indicating which type Unity is currently processing.

## The Baking Process

**Authoring GameObjects** contain authoring components and reside in **authoring scenes**. Unity converts this GameObject data into ECS data through the baking process, which occurs exclusively in the Editor (similar to asset importing) because it is resource-intensive.

Changes to authoring data trigger baking automatically. The execution depends on whether the subscene is open:

### Open Subscene (Live Baking)

When a subscene is open, **live baking** occurs while you work. Unity either performs:

- **Full baking**: The entire scene is processed
- **Incremental baking**: Only modified data is baked

### Closed Subscene (Asynchronous Baking)

With a closed subscene, Unity performs asynchronous full baking in the background.

## Full Baking Details

Full baking occurs when the entity scene requests loading with a closed subscene. A background asset importer process handles this without a GUI, keeping the main Editor responsive. However, initial entity scene loading may take several seconds; subsequent loads are faster due to caching.

Full baking is triggered by:

- Missing entity scene files
- Modified authoring scenes with outdated entity scenes
- Modified baking code assemblies lacking `[BakingVersion]` attributes
- Modified `[BakingVersion]` attributes
- Changes to Entities Project Settings
- Manual reimport requests from subscene Inspector
- Cleared baking cache in Editor Preferences

## Incremental Baking Details

When a subscene loads an authoring scene, incremental baking initializes. This baking occurs in-memory rather than via disk round-trips. When authoring GameObject contents change, "Unity re-bakes only the entities and components affected." This subset baking enables real-time ECS data updates, creating the impression of direct ECS editing.

However, incremental baking introduces complexity. Unlike full baking's clean slate approach, incremental baking builds upon previous passes, baking only entities dependent on changed GameObjects. This can create discrepancies between full and incremental results regarding entity ordering, entity size, and chunk layout. Baking code must ensure consistency despite these variances.

## Additional Resources

- Baker overview
- Scenes overview

---

## Outgoing Hyperlinks

- https://docs.unity3d.com/Manual/Components.html - Components
- https://docs.unity3d.com/Manual/TermsOfUse.html - Trademarks and terms of use
- https://unity.com/legal - Legal
- https://unity.com/legal/privacy-policy - Privacy Policy
- https://unity.com/legal/cookie-policy - Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information - Do Not Sell or Share My Personal Information
