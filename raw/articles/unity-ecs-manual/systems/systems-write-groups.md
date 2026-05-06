---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/systems-write-groups.html
fetched: 2026-05-05
section: systems
---

# Write Groups

Write groups provide a mechanism for one system to override another, even when you can't change the other system.

## Overview

A common ECS pattern involves systems that read **input** components and write to **output** components. Write groups enable you to override a system's output without modifying the original system code.

The write group of a target component consists of all component types marked with the `WriteGroup` attribute, specifying that target component as the argument. This filtering mechanism allows system users to exclude entities from processing and update those components using custom logic.

## Using Write Groups

To implement write group functionality:

1. Use the [write group filter option](../api/Unity.Entities.EntityQueryOptions.html) on your system's queries
2. This excludes entities containing components from a write group of any writable components in the query
3. Mark your own components as part of the write group of the output components you want to override
4. The original system ignores entities with your components, allowing custom updates

## Write Groups Example

Consider an external package with health-based coloring:

```csharp
public struct HealthComponent : IComponentData
{
    public int Value;
}

public struct ColorComponent : IComponentData
{
    public float4 Value;
}
```

The package includes:
- `ComputeColorFromHealthSystem`: reads `HealthComponent`, writes `ColorComponent`
- `RenderWithColorComponent`: reads `ColorComponent`

You want to override color computation for invincible characters. Rather than modifying the package system, use write groups:

### Step 1: Mark the Override Component

```csharp
[WriteGroup(typeof(ColorComponent))]
public struct InvincibleTagComponent : IComponentData { }
```

The write group of `ColorComponent` now includes all components marked with `WriteGroup(typeof(ColorComponent))`.

### Step 2: Enable Write Group Filtering

The `ComputeColorFromHealthSystem` must explicitly support write groups:

```csharp
[DisableAutoCreation]
[BurstCompile]
public partial struct ComputeColorFromHealthSystem : ISystem
{
    private EntityQuery m_Query;

    public void OnCreate(ref SystemState state)
    {
        // Create a query with FilterWriteGroup to support write groups.
        // This allows other systems to exclude entities by adding components
        // to the write group of ColorComponent.
        m_Query = new EntityQueryBuilder(Allocator.Temp)
            .WithAllRW<ColorComponent>()
            .WithAll<HealthComponent>()
            .WithOptions(EntityQueryOptions.FilterWriteGroup)
            .Build(ref state);

        state.RequireForUpdate(m_Query);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        new ComputeColorFromHealthJob().ScheduleParallel(m_Query);
    }
}
```

The key addition is `.WithOptions(EntityQueryOptions.FilterWriteGroup)`.

### Job Implementation

```csharp
[BurstCompile]
public partial struct ComputeColorFromHealthJob : IJobEntity
{
    // IJobEntity generates a query for entities that have ColorComponent
    // and HealthComponent. The FilterWriteGroup option is applied via
    // the system's query configuration.
    private void Execute(ref ColorComponent color, in HealthComponent health)
    {
        // Example: map health (0-100) to a color gradient from red to green
        float t = math.saturate(health.Value / 100f);
        color.Value = new float4(1f - t, t, 0f, 1f);
    }
}
```

### Execution Flow

When executed:
1. The system detects writes to `ColorComponent`
2. It looks up the write group of `ColorComponent` and finds `InvincibleTagComponent`
3. All entities with `InvincibleTagComponent` are excluded from processing

This allows exclusion based on component types unknown to the original system, potentially from different packages.

**Note:** See the `Unity.Transforms` code for examples of write groups applied to every updated component, including `LocalTransform`.

## Creating Write Groups

Add the `WriteGroup` attribute to each component type in the group. The attribute takes one parameter: the target component type being updated.

```csharp
public struct W : IComponentData
{
    public int Value;
}

[WriteGroup(typeof(W))]
public struct A : IComponentData
{
    public int Value;
}

[WriteGroup(typeof(W))]
public struct B : IComponentData
{
    public int Value;
}
```

A single component can belong to multiple write groups. Don't add the target component to its own write group.

## Enabling Write Group Filtering

Build queries with the `FilterWriteGroup` option:

```csharp
[BurstCompile]
public partial struct AddingJob : IJobEntity
{
    private void Execute(ref W w, in B b)
    {
        w.Value += b.Value;
    }
}
```

System implementation:

```csharp
[DisableAutoCreation]
[BurstCompile]
public partial struct AddingSystem : ISystem
{
    private EntityQuery m_Query;

    public void OnCreate(ref SystemState state)
    {
        // Support write groups by setting EntityQueryOptions.FilterWriteGroup.
        // This excludes entities that have component A, because W is writable
        // and A is part of the write group of W.
        // It doesn't exclude entities with B, because B is explicitly required.
        m_Query = new EntityQueryBuilder(Allocator.Temp)
            .WithAllRW<W>()
            .WithAll<B>()
            .WithOptions(EntityQueryOptions.FilterWriteGroup)
            .Build(ref state);

        state.RequireForUpdate(m_Query);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        new AddingJob().ScheduleParallel(m_Query);
    }
}
```

### Alternative: Using SystemAPI.Query

```csharp
[DisableAutoCreation]
[BurstCompile]
public partial struct AddingSystemWithQuery : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // Use SystemAPI.Query with WithOptions for write group filtering.
        foreach (var (w, b) in
            SystemAPI.Query<RefRW<W>, RefRO<B>>()
                .WithOptions(EntityQueryOptions.FilterWriteGroup))
        {
            w.ValueRW.Value += b.ValueRO.Value;
        }
    }
}
```

## Behavior with FilterWriteGroup

When write group filtering is enabled, the query adds all write group components to the `None` list unless explicitly included in `All` or `Any` lists. The query selects an entity only if it explicitly requires every component from that write group present on the entity.

In the example above:
- Entities with component `A` are excluded (part of write group, not explicitly required)
- Entities with component `B` are included (explicitly required, even though part of write group)

## Overriding Another System Using Write Groups

To override a system using write group filtering, add your components to the write groups of components the other system writes to. Write group filtering excludes unspecified write group components, causing the original system to ignore your entities.

Example: Creating a custom rotation system to override `Unity.Transforms`:

```csharp
[Serializable]
[WriteGroup(typeof(LocalTransform))]
public struct RotationAngleAxis : IComponentData
{
    public float Angle;
    public float3 Axis;
}
```

Implement the job:

```csharp
[BurstCompile]
public partial struct RotationAngleAxisJob : IJobEntity
{
    private void Execute(ref LocalTransform transform, in RotationAngleAxis source)
    {
        transform.Rotation = quaternion.AxisAngle(
            math.normalize(source.Axis),
            source.Angle);
    }
}
```

Create the system:

```csharp
[DisableAutoCreation]
[BurstCompile]
public partial struct RotationAngleAxisSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        new RotationAngleAxisJob().ScheduleParallel();
    }
}
```

Entities with `RotationAngleAxis` are updated by this system without contention from `Unity.Transforms` systems.

## Extending Systems Using Write Groups

To extend rather than override another system, enable write group filtering in your own system. The query automatically excludes entities with unspecified write group components. Create queries that explicitly include the write group components you handle.

Example: Adding component `C` to the write group:

```csharp
[WriteGroup(typeof(W))]
public struct C : IComponentData
{
    public int Value;
}
```

Create an extended system:

```csharp
[DisableAutoCreation]
[BurstCompile]
public partial struct ExtendedWriteGroupSystem : ISystem
{
    private EntityQuery m_Query;

    public void OnCreate(ref SystemState state)
    {
        // When extending a system that uses write groups, you must explicitly
        // query for each combination of components that make sense.
        // Use WithAny to match entities with A OR B (in addition to C and W).
        m_Query = new EntityQueryBuilder(Allocator.Temp)
            .WithAllRW<W>()
            .WithAll<C>()
            .WithAny<A, B>()
            .WithOptions(EntityQueryOptions.FilterWriteGroup)
            .Build(ref state);

        state.RequireForUpdate(m_Query);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // Process entities that have C and W, and also have A or B.
        foreach (var (w, c) in
            SystemAPI.Query<RefRW<W>, RefRO<C>>()
                .WithAny<A, B>()
                .WithOptions(EntityQueryOptions.FilterWriteGroup))
        {
            w.ValueRW.Value += c.ValueRO.Value;
        }
    }
}
```

**Tip:** Use `WithAny` to match multiple component combinations when appropriate.

If entities exist with unspecified write group component combinations, neither the target system nor its filters handle them. This typically indicates a logical error.

---

## Outgoing Links

- [WriteGroupAttribute API Documentation](../api/Unity.Entities.WriteGroupAttribute.html)
- [EntityQueryOptions Documentation](../api/Unity.Entities.EntityQueryOptions.html)
- [docs.unity3d.com](http://docs.unity3d.com/)
- [Legal](https://unity.com/legal)
- [Privacy Policy](https://unity.com/legal/privacy-policy)
- [Cookie Policy](https://unity.com/legal/cookie-policy)
- [Do Not Sell or Share My Personal Information](https://unity.com/legal/do-not-sell-my-personal-information)
