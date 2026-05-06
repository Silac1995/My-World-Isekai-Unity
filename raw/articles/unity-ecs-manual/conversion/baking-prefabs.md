---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/baking-prefabs.html
fetched: 2026-05-05
section: conversion
---

# Prefabs in baking | Entities 6.4.0

## Overview

During baking, GameObject prefabs transform into entity prefabs. These specialized entities contain:

- **A prefab tag** that identifies them and excludes them from queries by default
- **A `LinkedEntityGroup` buffer** that stores all child entities in a flat list for efficient instantiation

Entity prefabs function similarly to GameObject prefabs through runtime instantiation, requiring the GameObject prefabs to be baked and available in the entity scene.

### Important Notes

When prefab instances appear in subscene hierarchies, they're treated as normal GameObjects since they lack `Prefab` or `LinkedEntityGroup` components.

When baking occurs, the `Dynamic` transform usage flag automatically applies to the prefab root, ensuring the prefab entity has necessary transform components for runtime movement and position changes.

## Creating and Registering Entity Prefabs

Prefabs must be registered to a baker to ensure proper baking and availability in the entity scene. This creates dependency tracking and grants proper components.

### Basic Approach

```csharp
public struct EntityPrefabComponent : IComponentData
{
    public Entity Value;
}

public class EntityPrefabAuthoring : MonoBehaviour
{
    public GameObject Prefab;
}

public class EntityPrefabBaker : Baker<EntityPrefabAuthoring>
{
    public override void Bake(EntityPrefabAuthoring authoring)
    {
        var entityPrefab = GetEntity(authoring.Prefab, TransformUsageFlags.Dynamic);
        var entity = GetEntity(TransformUsageFlags.Dynamic);
        AddComponent(entity, new EntityPrefabComponent() {Value = entityPrefab});
    }
}
```

### Using EntityPrefabReference

For preventing duplication across subscenes, use `EntityPrefabReference` to serialize prefab content into a separate entity scene file:

```csharp
public struct EntityPrefabReferenceComponent : IComponentData
{
    public EntityPrefabReference Value;
}

public class EntityPrefabReferenceAuthoring : MonoBehaviour
{
    public GameObject Prefab;
}

public class EntityPrefabReferenceBaker : Baker<EntityPrefabReferenceAuthoring>
{
    public override void Bake(EntityPrefabReferenceAuthoring authoring)
    {
        var entityPrefabReference = new EntityPrefabReference(authoring.Prefab);
        var entity = GetEntity(TransformUsageFlags.Dynamic);
        AddComponent(entity, new EntityPrefabReferenceComponent() {Value = entityPrefabReference});
    }
}
```

## Instantiating Prefabs

### Direct Entity Reference

Use `EntityManager` or entity command buffers to instantiate prefabs:

```csharp
public partial struct InstantiatePrefabSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        var ecb = new EntityCommandBuffer(Allocator.Temp);

        foreach (var prefab in
                 SystemAPI.Query<RefRO<EntityPrefabComponent>>())
        {
            var instance = ecb.Instantiate(prefab.ValueRO.Value);
            ecb.AddComponent<ComponentA>(instance);
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }
}
```

Instantiated prefabs contain a `SceneSection` component, which may affect entity lifetime.

### EntityPrefabReference Approach

For `EntityPrefabReference` prefabs, add `RequestEntityPrefabLoaded` to load the prefab first:

```csharp
public partial struct InstantiatePrefabReferenceSystem : ISystem
{
    public void OnStartRunning(ref SystemState state)
    {
        foreach (var (prefab, entity) in
                 SystemAPI.Query<RefRO<EntityPrefabReferenceComponent>>()
                 .WithNone<PrefabLoadResult>().WithEntityAccess())
        {
            state.EntityManager.AddComponentData(entity, 
                new RequestEntityPrefabLoaded(){ Prefab = prefab.ValueRO.Value} );
        }
    }

    public void OnUpdate(ref SystemState state)
    {
        var ecb = new EntityCommandBuffer(Allocator.Temp);

        foreach (var (prefab, entity) in
                 SystemAPI.Query<RefRO<PrefabLoadResult>>().WithEntityAccess())
        {
            var instance = ecb.Instantiate(prefab.ValueRO.PrefabRoot);
            ecb.RemoveComponent<RequestEntityPrefabLoaded>(entity);
            ecb.RemoveComponent<PrefabLoadResult>(entity);
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }
}
```

The `PrefabLoadResult` component indicates successful prefab loading and may take several frames.

## Prefabs in Queries

By default, prefabs are excluded from queries. Include them using `IncludePrefab`:

```csharp
var prefabQuery = SystemAPI.QueryBuilder()
    .WithAll<BakedEntity>()
    .WithOptions(EntityQueryOptions.IncludePrefab)
    .Build();
```

## Destroying Prefab Instances

Destroy instances like regular entities using `EntityManager` or entity command buffers. This operation incurs structural change costs:

```csharp
var ecb = new EntityCommandBuffer(Allocator.Temp);

foreach (var (component, entity) in
         SystemAPI.Query<RefRO<RotationSpeed>>().WithEntityAccess())
{
    if (component.ValueRO.RadiansPerSecond <= 0)
    {
        ecb.DestroyEntity(entity);
    }
}

ecb.Playback(state.EntityManager);
ecb.Dispose();
```

## Additional Resources

- [Baker overview](baking-baker-overview.html)
- [Linked entity groups](linked-entity-group.html)

---

## Outgoing Hyperlinks

- https://docs.unity3d.com/6000.0/Documentation/Manual/Prefabs.html - Prefabs
- https://docs.unity3d.com/Manual/TermsOfUse.html - Trademarks and terms of use
- https://unity.com/legal - Legal
- https://unity.com/legal/privacy-policy - Privacy Policy
- https://unity.com/legal/cookie-policy - Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information - Do Not Sell or Share My Personal Information
