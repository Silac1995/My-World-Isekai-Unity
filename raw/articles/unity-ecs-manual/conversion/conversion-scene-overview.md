---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/conversion-scene-overview.html
fetched: 2026-05-05
section: conversion
---

# Scenes Overview

In the entity component system (ECS), scenes function differently from Unity's core scene system, which is incompatible with ECS. Understanding the following scene concepts is essential:

## Scene Types

**Authoring Scenes**
An authoring scene is a standard editable scene designed for the baking process. "It contains GameObjects and MonoBehaviour components that Unity converts to ECS data at runtime."

**Entity Scenes**
An entity scene contains the ECS data produced by the baking process.

**Subscenes**
A subscene serves as a reference to an authoring or entity scene. "In the Unity Editor, you create a subscene to add authoring elements to. When the subscene is closed, it triggers the baking process for related entity scenes."

## Efficiency Considerations

Projects with substantial data benefit from distributed authoring scenes. While ECS efficiently handles millions of entities, their GameObject representations can cause editor performance issues. "Therefore, it's more efficient to place authoring data into several smaller authoring scenes."

The MegaCity example demonstrates this approach, organizing each building as a separate subscene while enabling the entire city to load as performant ECS data for contextual viewing.

## Subscenes vs. Entity Scenes

"Subscenes and entity scenes are often confused with each other. But a subscene is nothing more than an attachment point, to conveniently load entity scenes." This distinction clarifies that subscenes facilitate workflow management rather than representing a fundamentally different data structure.

---

## Outgoing Links

- https://docs.unity3d.com/6000.0/Documentation/Manual/CreatingScenes.html — Scene system documentation
- baking-overview.html — Baking overview
- conversion-subscenes.html — Subscenes overview
- streaming-scenes.html — Scene streaming
- https://github.com/Unity-Technologies/Megacity-Sample — MegaCity example project
