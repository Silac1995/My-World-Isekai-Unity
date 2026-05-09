---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/systems-looking-up-data.html
fetched: 2026-05-05
section: queries-jobs
---

# Look up arbitrary data

## Overview

The most efficient approach to accessing and changing data involves using a system paired with an entity query running in a job, maximizing parallelism and reducing cache misses. However, situations arise where accessing a component on an arbitrary entity becomes necessary at any point during a system update.

You can retrieve data from an entity's `IComponentData` and its dynamic buffers.

## Look up entity data in a system

Within a `SystemBase`, you can iterate entities on the main thread using `SystemAPI.Query` and perform targeted lookups on arbitrary entities through:

- `SystemAPI.HasComponent<T>(Entity)`
- `SystemAPI.GetComponent<T>(Entity)`
- `GetComponentLookup<T>(bool isReadOnly)`
- `GetBufferLookup<T>(bool isReadOnly)`

### Example: Tracking System

This code demonstrates using `GetComponent<T>(Entity)` to retrieve a `Target` component containing an entity field, then rotating tracking entities toward their target:

```csharp
[RequireMatchingQueriesForUpdate]
public partial class TrackingSystem : SystemBase
{
    protected override void OnUpdate()
    {
        float deltaTime = SystemAPI.Time.DeltaTime;

        // Iterate over all entities that have a LocalTransform and a Target component.
        foreach (var (transform, target) in
                 SystemAPI.Query<RefRW<LocalTransform>, RefRO<Target>>())
        {
            var targetEntity = target.ValueRO.entity;

            // Ensure the target entity still exists and has a LocalTransform.
            if (!SystemAPI.HasComponent<LocalTransform>(targetEntity))
                continue;

            // Look up the target entity's transform data.
            var targetTransform = SystemAPI.GetComponent<LocalTransform>(targetEntity);

            // Calculate a smooth rotation towards the target.
            float3 displacement = targetTransform.Position - transform.ValueRO.Position;
            quaternion lookRotation = quaternion.LookRotationSafe(displacement, math.up());

            transform.ValueRW.Rotation =
                math.slerp(transform.ValueRO.Rotation, lookRotation, deltaTime);
        }
    }
}
```

### Example: Buffer Lookup System

To access data in a dynamic buffer on an arbitrary entity, create a local `BufferLookup<T>` via `GetBufferLookup<T>(true)` for read-only access, capture it in the loop, and use it to test and fetch the buffer:

```csharp
public struct BufferData : IBufferElementData
{
    public float Value;
}

[RequireMatchingQueriesForUpdate]
public partial class BufferLookupSystem : SystemBase
{
    protected override void OnUpdate()
    {
        // Acquire a read-only lookup for BufferData on arbitrary entities.
        var bufferLookup = GetBufferLookup<BufferData>(true);

        // Iterate over chaser entities that have a LocalTransform and a Target.
        foreach (var (transform, target) in
                 SystemAPI.Query<RefRW<LocalTransform>, RefRO<Target>>())
                {
            var targetEntity = target.ValueRO.entity;

            // Check the target entity still exists and has this buffer type.
            if (!bufferLookup.HasBuffer(targetEntity))
                continue;

            // Get the dynamic buffer from the target entity.
            DynamicBuffer<BufferData> buffer = bufferLookup[targetEntity];

            // Example use: compute average of buffer values.
            float sum = 0f;
            for (int i = 0; i < buffer.Length; i++)
                sum += buffer[i].Value;

            float avg = buffer.Length > 0 ? sum / buffer.Length : 0f;
                }
    }
}
```

## Look up entity data in a job

To access component data randomly in a job struct such as `IJobChunk`, use:

- `ComponentLookup`
- `BufferLookup`

These provide an array-like interface to components, indexed by `Entity` object. You can also use `ComponentLookup` to determine whether an entity's enableable components are enabled or disabled, or toggle their state.

Declare a field of the appropriate type, set its value, and schedule the job.

### Example: ComponentLookup Declaration

```csharp
[ReadOnly]
public ComponentLookup<LocalToWorld> EntityPositions;
```

**Note:** Always declare `ComponentLookup` objects as read-only unless you intend to write to the accessed components.

### Example: Setting data fields and scheduling

```csharp
protected override void OnUpdate()
{
    var job = new ChaserSystemJob();

    // Set non-ECS data fields
    job.deltaTime = SystemAPI.Time.DeltaTime;

    // Schedule the job using Dependency property
    Dependency = job.ScheduleParallel(query, this.Dependency);
}
```

### Example: Looking up component values

Inside the job's `Execute` method, use an entity object to look up values:

```csharp
float3 targetPosition = EntityPositions[targetEntity].Position;
float3 chaserPosition = transform.Position;
float3 displacement = targetPosition - chaserPosition;
float3 newPosition = chaserPosition + displacement * deltaTime;
transform.Position = newPosition;
```

### Full Example: Move Towards Entity System

```csharp
[RequireMatchingQueriesForUpdate]
public partial class MoveTowardsEntitySystem : SystemBase
{
    private EntityQuery query;

    [BurstCompile]
    private partial struct MoveTowardsJob : IJobEntity
    {

        // Read-only data stored (potentially) in other chunks
        [ReadOnly]
        public ComponentLookup<LocalToWorld> EntityPositions;

        // Non-entity data
        public float deltaTime;

        public void Execute(ref LocalTransform transform, in Target target, in LocalToWorld entityPosition)
        {
            // Get the target Entity object
            Entity targetEntity = target.entity;

            // Check that the target still exists
            if (!EntityPositions.HasComponent(targetEntity))
                return;

            // Update translation to move the chasing entity toward the target
            float3 targetPosition = EntityPositions[targetEntity].Position;
            float3 chaserPosition = transform.Position;

            float3 displacement = targetPosition - chaserPosition;
            transform.Position = chaserPosition + displacement * deltaTime;
        }
    }

    protected override void OnCreate()
    {
        // Select all entities that have Translation and Target Component
        query = this.GetEntityQuery
            (
                typeof(LocalTransform),
                ComponentType.ReadOnly<Target>()
            );
    }

    protected override void OnUpdate()
    {
        // Create the job
        var job = new MoveTowardsJob();

        // Set the component data lookup field
        job.EntityPositions = GetComponentLookup<LocalToWorld>(true);

        // Set non-ECS data fields
        job.deltaTime = SystemAPI.Time.DeltaTime;

        // Schedule the job using Dependency property
        Dependency = job.ScheduleParallel(query, Dependency);
    }
}
```

## Data access errors

If lookup data overlaps with data you read and write to in the job, random access might create race conditions.

Mark an accessor object with the `NativeDisableParallelForRestriction` attribute if you're confident no overlap exists between directly accessed entity data and randomly accessed entity data.

---

## Outgoing Links

- http://docs.unity3d.com/ - docs.unity3d.com
- ../logo.svg - Logo
- ../index.html - Home
- concepts-systems.html - System documentation
- systems-entityquery.html - Entity query documentation
- ../api/Unity.Entities.IComponentData.html - IComponentData API
- components-buffer-introducing.html - Dynamic buffers introduction
- systems-systemapi-query.html - SystemAPI.Query documentation
- ../api/Unity.Entities.IJobChunk.html - IJobChunk API
- ../api/Unity.Entities.ComponentLookup-1.html - ComponentLookup API
- ../api/Unity.Entities.BufferLookup-1.html - BufferLookup API
- ../api/Unity.Entities.Entity.html - Entity API
- components-enableable-intro.html - Enableable components documentation
- https://docs.unity3d.com/ScriptReference/Unity.Collections.ReadOnlyAttribute.html - ReadOnly attribute
- https://docs.unity3d.com/ScriptReference/Unity.Collections.NativeDisableParallelForRestrictionAttribute.html - NativeDisableParallelForRestriction attribute
- https://docs.unity3d.com/Manual/TermsOfUse.html - Trademarks and terms of use
- https://unity.com/legal - Legal
- https://unity.com/legal/privacy-policy - Privacy Policy
- https://unity.com/legal/cookie-policy - Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information - Do Not Sell or Share My Personal Information
