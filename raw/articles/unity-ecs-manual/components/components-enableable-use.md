---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/components-enableable-use.html
fetched: 2026-05-05
section: components
---

# Use Enableable Components

## Overview

You can only make `IComponentData` and `IBufferElementData` components enableable by implementing the `IEnableableComponent` interface.

When using enableable components, "the target entity doesn't change its archetype, ECS doesn't move any data, and the component's existing value remains the same." This allows you to enable and disable components on worker thread jobs without entity command buffers or sync points.

However, jobs with write access to enableable components may cause main-thread operations to block until completion, even if no components are actually enabled or disabled.

When instantiating entities from prefab entities, enableable components retain their enabled/disabled state from the prefab.

## Enableable Component Methods

### In IJobEntity and Idiomatic Foreach

The `EnabledRefRW` type enables or disables components on the active entity and queries their state, similar to `RefRW` but for enabled state. This approach is most efficient for linear entity iteration:

```csharp
public partial struct EnableAliveFromHealthSystem : ISystem
{
    public void OnUpdate(ref SystemState system)
    {
        foreach (var (health, aliveEnabled) in SystemAPI.Query<RefRO<Health>, EnabledRefRW<AliveTag>>())
        {
            if (health.ValueRO.Value <= 0)
                aliveEnabled.ValueRW = false;
        }
    }
}
```

```csharp
public partial struct EnableAliveFromHealthJob : IJobEntity
{
    void Execute(in Health health, EnabledRefRW<AliveTag> aliveEnabled)
    {
        if (health.Value <= 0)
            aliveEnabled.ValueRW = false;
    }
}
```

The `EnabledRefRO` type provides read-only access to query component enabled status.

### In IJobChunk

For chunk iteration, use the `EnabledMask` interface to enable and disable components by entity index within chunks:

```csharp
public struct EnableAliveFromHealthChunkJob : IJobChunk
{
    [ReadOnly] public ComponentTypeHandle<Health> HealthTypeHandle;
    public ComponentTypeHandle<AliveTag> AliveTagTypeHandle;

    public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
    {
        NativeArray<Health> chunkHealthValues = chunk.GetNativeArray(ref HealthTypeHandle);
        EnabledMask chunkAliveEnabledMask = chunk.GetEnabledMask(ref AliveTagTypeHandle);
        ChunkEntityEnumerator enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
        while(enumerator.NextEntityIndex(out var i))
        {
            if (chunkHealthValues[i].Value <= 0)
                chunkAliveEnabledMask[i] = false;
        }
    }
}
```

`EnabledMask` also provides methods to create `EnabledRefRW` and `EnabledRefRO` instances for individual entities.

### Random Access Methods

For arbitrary entity access, use these methods on `EntityManager`, `ComponentLookup<T>`, `EntityCommandBuffer`, and `ArchetypeChunk`:

- **`IsComponentEnabled<T>(Entity e)`**: Returns true if entity has component T and it's enabled; false if disabled. Asserts if entity lacks component T or if T doesn't implement `IEnableableComponent`.

- **`SetComponentEnabled<T>(Entity e, bool enable)`**: Enables or disables component T on entity e based on the boolean value. Asserts if preconditions aren't met.

Example usage:

```csharp
public partial struct EnableableComponentSystem : ISystem
{
    public void OnUpdate(ref SystemState system)
    {
        Entity e = system.EntityManager.CreateEntity(typeof(Health));
        ComponentLookup<Health> healthLookup = system.GetComponentLookup<Health>();

        // true
        bool b = healthLookup.IsComponentEnabled(e);

        // disable the Health component of the entity
        healthLookup.SetComponentEnabled(e, false);

        // though disabled, the component can still be read and modified
        Health h = healthLookup[e];
    }
}
```

You can safely use `ComponentLookup<T>.SetComponentEnabled()` from worker threads without structural changes, provided the job has write access to component T. Avoid enabling or disabling components on entities being processed by other threads to prevent race conditions.

Random-access methods have additional overhead due to entity lookup. Use iteration-based methods when performance matters.

## Querying Enableable Components

An entity with a disabled component T matches queries as if it lacks that component entirely. For example, if entity E has components T1 (enabled), T2 (disabled), and T3 (disabled):

- Doesn't match queries requiring both T1 and T2
- Matches queries requiring T1 and excluding T2
- Doesn't match queries with T2 and T3 as optional components (lacks at least one enabled)

All `EntityQuery` methods automatically handle enableable components, including `CalculateEntityCount()`. Two exceptions exist:

- Methods ending in `IgnoreFilter` treat all components as enabled and don't require sync points
- Queries created with `EntityQueryOptions.IgnoreComponentEnabledState` ignore enabled/disabled states

Example querying disabled components:

```csharp
public partial struct EnableableHealthSystem : ISystem
{
    public void OnUpdate(ref SystemState system)
    {
        Entity e1 = system.EntityManager.CreateEntity(typeof(Health), typeof(Translation));
        Entity e2 = system.EntityManager.CreateEntity(typeof(Health), typeof(Translation));

        // true (components begin life enabled)
        bool b = system.EntityManager.IsComponentEnabled<Health>(e1);

        // disable the Health component on the first entity
        system.EntityManager.SetComponentEnabled<Health>(e1, false);

        EntityQuery query = new EntityQueryBuilder(Allocator.Temp).WithAll<Health, Translation>().Build(ref system);

        // the returned array does not include the first entity
        var entities = query.ToEntityArray(Allocator.Temp);

        // the returned array does not include the Health of the first entity
        var healths = query.ToComponentDataArray<Health>(Allocator.Temp);

        // the returned array does not include the Translation of the first entity
        var translations = query.ToComponentDataArray<Translation>(Allocator.Temp);

        // This query matches components whether they're enabled or disabled
        var queryIgnoreEnableable = new EntityQueryBuilder(Allocator.Temp).WithAll<Health, Translation>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).Build(ref system);

        // the returned array includes the Translations of both entities
        var translationsAll = queryIgnoreEnableable.ToComponentDataArray<Translation>(Allocator.Temp);
    }
}
```

## Asynchronous Operations

All synchronous `EntityQuery` operations (except filtering-ignored variants) automatically wait for running jobs with write access to enableable components. All asynchronous operations automatically insert input dependencies on these jobs.

Asynchronous gather and scatter operations like `EntityQuery.ToEntityArrayAsync()` schedule jobs to perform requested operations. These methods return `NativeList` instead of `NativeArray` because final entity counts aren't known until runtime, but containers must return immediately.

The list uses conservatively sized initial capacity based on maximum possible matching entities. Until async jobs complete, any reads or writes to the list (including length, capacity, or base pointer) cause JobsDebugger safety errors. However, you can safely pass the list to dependent follow-up jobs.

---

## Outgoing Hyperlinks

- https://docs.unity3d.com/Manual/TermsOfUse.html — Trademarks and terms of use
- https://unity.com/legal — Legal
- https://unity.com/legal/privacy-policy — Privacy Policy
- https://unity.com/legal/cookie-policy — Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information — Do Not Sell or Share My Personal Information
- https://docs.unity3d.com — docs.unity3d.com
