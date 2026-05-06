---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/streaming-scene-instancing.html
fetched: 2026-05-05
section: conversion
---

# Scene Instancing in Unity Entities 6.4.0

## Overview

Scene instancing enables creation of multiple identical copies of the same scene within a world using `SceneSystem.LoadSceneAsync` with the `SceneLoadFlags.NewInstance` flag. This technique proves valuable when populating worlds with repeated elements, such as tile-based environments.

## Key Concepts

### The Streaming World

Scene loading occurs in a dedicated **streaming world** rather than the main world. Each section loads into its own streaming world instance. Once loading completes, the content transfers to the main world. This separation allows for preprocessing before final integration.

### ProcessAfterLoadGroup System Group

The `ProcessAfterLoadGroup` executes in the streaming world after content loads but before the transition to the main world. Custom systems added to this group can apply transformations to scene instances, making each copy unique despite being loaded from identical data.

### PostLoadCommandBuffer Component

This managed component wraps an `EntityCommandBuffer`. During section loading, the streaming system checks for its presence on the section meta entity. If found, the system executes the command buffer before running `ProcessAfterLoadGroup`.

When applied to a scene meta entity, the command buffer executes for all sections within that scene during loading.

## Implementation Workflow

1. Load scene with `SceneSystem.LoadSceneAsync` using `SceneLoadFlags.NewInstance`
2. Retain the returned scene meta entity reference
3. Attach a `PostLoadCommandBuffer` to the meta entity:
   - Create an `EntityCommandBuffer`
   - Generate a new entity with instance-specific components
   - Package the buffer in `PostLoadCommandBuffer`
   - Assign to the meta entity
4. Create a system assigned to `ProcessAfterLoadGroup` that queries instance data and applies transformations

## Example Implementation

```csharp
var loadParameters = new SceneSystem.LoadParameters()
    { Flags = SceneLoadFlags.NewInstance };
var sceneEntity = SceneSystem.LoadSceneAsync(state.WorldUnmanaged,
    sceneReference, loadParameters);

var ecb = new EntityCommandBuffer(Allocator.Persistent,
    PlaybackPolicy.MultiPlayback);
var postLoadEntity = ecb.CreateEntity();
var postLoadOffset = new PostLoadOffset
{
    Offset = sceneOffset
};
ecb.AddComponent(postLoadEntity, postLoadOffset);

var postLoadCommandBuffer = new PostLoadCommandBuffer()
{
    CommandBuffer = ecb
};
state.EntityManager.AddComponentData(sceneEntity, postLoadCommandBuffer);
```

### Custom Component

```csharp
public struct PostLoadOffset : IComponentData
{
    public float3 Offset;
}
```

### Postprocessing System

```csharp
[WorldSystemFilter(WorldSystemFilterFlags.ProcessAfterLoad)]
public partial struct PostprocessSystem : ISystem
{
    private EntityQuery offsetQuery;

    public void OnCreate(ref SystemState state)
    {
        offsetQuery = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<PostLoadOffset>()
            .Build(ref state);
        state.RequireForUpdate(offsetQuery);
    }

    public void OnUpdate(ref SystemState state)
    {
        var offsets = offsetQuery.ToComponentDataArray<PostLoadOffset>(Allocator.Temp);
        foreach (var offset in offsets)
        {
            foreach (var transform in SystemAPI.Query<RefRW<LocalTransform>>())
            {
                transform.ValueRW.Position += offset.Offset;
            }
        }
        state.EntityManager.DestroyEntity(offsetQuery);
    }
}
```

---

## Outgoing Links

- https://docs.unity3d.com/Manual/TermsOfUse.html - Trademarks and terms of use
- https://unity.com/legal - Legal information
- https://unity.com/legal/privacy-policy - Privacy Policy
- https://unity.com/legal/cookie-policy - Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information - Do Not Sell or Share My Personal Information
- https://docs.unity3d.com - Unity Documentation
