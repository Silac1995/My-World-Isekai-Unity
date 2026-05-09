---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/iterating-data-ijobentity.html
fetched: 2026-05-05
section: queries-jobs
---

# Iterate over component data with IJobEntity

`IJobEntity` enables iteration across `ComponentData` for data transformations that can be reused across multiple systems with different invocations. It generates an `IJobChunk` job behind the scenes, so you only need to focus on the data transformation logic.

Note: `IJobEntity` functions identically with both SystemBase and ISystem.

## Create an IJobEntity job

To create an `IJobEntity` job, define a struct implementing the `IJobEntity` interface with a custom `Execute` method. Use the `partial` keyword since source generation creates a separate struct implementing `IJobChunk`.

**Example: Basic IJobEntity job**

```csharp
public struct SampleComponent : IComponentData { public float Value; }

public partial struct ASampleJob : IJobEntity
{
    // Adds one to every SampleComponent value
    void Execute(ref SampleComponent sample)
    {
        sample.Value += 1f;
    }
}

public partial class ASample : SystemBase
{
    protected override void OnUpdate()
    {
        // Schedules the job
        new ASampleJob().ScheduleParallel();
    }
}
```

## Specify a query

You can define queries for `IJobEntity` in two ways:

- Manually create a query for custom invocation requirements
- Use attributes on the job struct to auto-generate queries based on `Execute` parameters

**Example: Multiple queries with the same job**

```csharp
partial struct QueryJob : IJobEntity
{
    // Iterates over all SampleComponents and increments their value
    public void Execute(ref SampleComponent sample)
    {
        sample.Value += 1;
    }
}

[RequireMatchingQueriesForUpdate]
public partial class QuerySystem : SystemBase
{
    EntityQuery query_boidtarget;
    EntityQuery query_boidobstacle;

    protected override void OnCreate()
    {
        query_boidtarget = GetEntityQuery(
            ComponentType.ReadWrite<SampleComponent>(),
            ComponentType.ReadOnly<BoidTarget>());

        query_boidobstacle = GetEntityQuery(
            ComponentType.ReadWrite<SampleComponent>(),
            ComponentType.ReadOnly<BoidObstacle>());
    }

    protected override void OnUpdate()
    {
        new QueryJob().ScheduleParallel(query_boidtarget);
        new QueryJob().ScheduleParallel(query_boidobstacle);
        new QueryJob().ScheduleParallel();
    }
}
```

## Attributes

| Attribute | Description |
|-----------|-------------|
| `Unity.Entities.WithAll(params Type[])` | Narrows query to entities matching all specified types |
| `Unity.Entities.WithAny(params Type[])` | Narrows query to entities matching any specified types |
| `Unity.Entities.WithNone(params Type[])` | Narrows query to entities matching none of the specified types |
| `Unity.Entities.WithChangeFilter(params Type[])` | Includes only entities with changes in archetype chunks for given components |
| `Unity.Entities.WithOptions(params EntityQueryOptions[])` | Changes query scope using `EntityQueryOptions` |
| `Unity.Entities.EntityIndexInQuery` | Gets current index in query for entity iteration |

**Example: EntityIndexInQuery usage**

```csharp
[BurstCompile]
partial struct CopyPositionsJob : IJobEntity
{
    public NativeArray<float3> copyPositions;

    public void Execute([EntityIndexInQuery] int entityIndexInQuery, in LocalToWorld localToWorld)
    {
        copyPositions[entityIndexInQuery] = localToWorld.Position;
    }
}

[RequireMatchingQueriesForUpdate]
public partial struct EntityInQuerySystem : ISystem
{
    EntityQuery query;

    public void OnCreate(ref SystemState state)
    {
        query = state.GetEntityQuery(ComponentType.ReadOnly<LocalToWorld>());
    }

    public void OnUpdate(ref SystemState state)
    {
        var positions = new NativeArray<float3>(
            query.CalculateEntityCount(), 
            state.WorldUnmanaged.UpdateAllocator.ToAllocator);

        new CopyPositionsJob{copyPositions = positions}.ScheduleParallel();

        positions.Dispose(state.Dependency);
    }
}
```

### Additional job attributes

These standard job attributes also work with `IJobEntity`:

- `Unity.Burst.BurstCompile`
- `Unity.Collections.DeallocateOnJobCompletion`
- `Unity.Collections.NativeDisableParallelForRestriction`
- `Unity.Burst.BurstDiscard`
- `Unity.Collections.LowLevel.Unsafe.NativeSetThreadIndex`
- `Unity.Burst.NoAlias`

## Execute parameters

| Parameter | Description |
|-----------|-------------|
| `IComponentData` | Mark as `ref` for read-write or `in` for read-only access |
| `ICleanupComponentData` | Mark as `ref` for read-write or `in` for read-only access |
| `ISharedComponent` | Mark `in` for read-only access (can't Burst compile; use `.Run` instead) |
| Managed components | Value-copy for read-write or `in` for read-only (can't use `ref`; use `.Run`) |
| `Entity` | Gets current entity (value copy only) |
| `DynamicBuffer<T>` | Mark `ref` for read-write, `in` for read-only access |
| `IAspect` | Mark as value-copy or `ref` for read-write, `in` for read-only |
| `int` | Three variants with attributes: `[ChunkIndexInQuery]`, `[EntityIndexInChunk]`, or `[EntityIndexInQuery]` |

---

## Outgoing Hyperlinks

- https://docs.unity3d.com/Packages/com.unity.entities@latest/api/Unity.Entities.IJobEntity.html - IJobEntity API documentation
- https://docs.unity3d.com/Packages/com.unity.entities@latest/api/Unity.Entities.IJobChunk.html - IJobChunk API documentation
- https://docs.unity3d.com/Packages/com.unity.entities@latest/api/Unity.Entities.WithAllAttribute.html - WithAll attribute
- https://docs.unity3d.com/Packages/com.unity.entities@latest/api/Unity.Entities.WithAnyAttribute.html - WithAny attribute
- https://docs.unity3d.com/Packages/com.unity.entities@latest/api/Unity.Entities.WithNoneAttribute.html - WithNone attribute
- https://docs.unity3d.com/Packages/com.unity.entities@latest/api/Unity.Entities.WithChangeFilterAttribute.html - WithChangeFilter attribute
- https://docs.unity3d.com/Packages/com.unity.entities@latest/api/Unity.Entities.WithOptionsAttribute.html - WithOptions attribute
- https://docs.unity3d.com/Packages/com.unity.entities@latest/api/Unity.Entities.EntityIndexInQuery.html - EntityIndexInQuery attribute
- https://docs.unity3d.com/Packages/com.unity.entities@latest/manual/components-managed.html - Managed components documentation
- https://docs.unity3d.com/ - docs.unity3d.com home
