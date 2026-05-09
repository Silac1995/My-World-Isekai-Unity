---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/baking-phases.html
fetched: 2026-05-05
section: conversion
---

# Baking Phases

Baking in Unity Entities involves multiple phases, with two primary components:

- **Bakers:** Convert authoring components on GameObjects into entities and components
- **Baking systems:** Perform additional processing on the resulting entities

## Entity Creation

Before bakers execute, Unity generates an entity for each authoring GameObject in a subscene. Initially, these entities contain only internal metadata.

## Baker Phase

Once entities are created, bakers run to process specific authoring component types. Multiple bakers can handle the same component type.

Key constraints during this phase:

- "There is no guarantee in which order Unity runs the bakers"
- Bakers cannot read or modify existing entity components—they can only add new ones
- Each baker can only modify its own entity or entities it creates
- Accessing and modifying other entities causes undefined behavior

## Baking Systems Phase

After all bakers complete, Unity executes baking systems (ECS systems marked with a `BakingSystem` attribute). These systems can be ordered using `UpdateAfter`, `UpdateBefore`, and `UpdateInGroup` attributes.

The default system groups execute in this order:

1. `PreBakingSystemGroup` (runs before bakers and entity creation)
2. `TransformBakingSystemGroup`
3. `BakingSystemGroup` (default group)
4. `PostBakingSystemGroup`

Following execution of all baking system groups, entity data is either serialized to an entity scene or reflected into the main ECS world during live baking.

## Related Resources

- http://docs.unity3d.com/ — docs.unity3d.com
- https://docs.unity3d.com/Manual/TermsOfUse.html — Trademarks and terms of use
