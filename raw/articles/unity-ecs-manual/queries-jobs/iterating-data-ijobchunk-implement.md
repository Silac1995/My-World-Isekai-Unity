---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/iterating-data-ijobchunk-implement.html
fetched: 2026-05-05
section: queries-jobs
---

# Implement IJobChunk

## Overview

To implement `IJobChunk`, follow these five steps:

1. Query data with an `EntityQuery` to identify entities for processing
2. Define the job struct using `IJobChunk`
3. Declare data your job accesses via `ComponentTypeHandle` objects
4. Write the `Execute` method to transform your data
5. Schedule the job in the system's `OnUpdate` method

## Query Data with an EntityQuery

An `EntityQuery` defines the set of component types that an `EntityArchetype` must contain for processing. The archetype can have additional components but must include those defined by the query. You can also exclude archetypes with specific component types.

Pass the query selecting entities to the job's schedule method.

### Optional Components

Don't include optional components in the `EntityQuery`. Instead, use the `ArchetypeChunk.Has` method inside `IJobChunk.Execute` to check if the current chunk has the optional component. Since all entities in a chunk share the same components, you only need to check once per chunk rather than per entity.

## Define the Job Struct

A job struct contains an `Execute` method performing the work and fields declaring data the method uses.

### Example Implementation

```csharp
[BurstCompile]
public struct UpdateTranslationFromVelocityJob : IJobChunk
{
    public ComponentTypeHandle<VelocityVector> VelocityTypeHandle;
    public ComponentTypeHandle<ObjectPosition> PositionTypeHandle;
    public float DeltaTime;

    [BurstCompile]
    public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
    {
        NativeArray<VelocityVector> velocityVectors = chunk.GetNativeArray(ref VelocityTypeHandle);
        NativeArray<ObjectPosition> translations = chunk.GetNativeArray(ref PositionTypeHandle);

        var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
        while(enumerator.NextEntityIndex(out var i))
        {
            float3 translation = translations[i].Value;
            float3 velocity = velocityVectors[i].Value;
            float3 newTranslation = translation + velocity * DeltaTime;

            translations[i] = new ObjectPosition() { Value = newTranslation };
        }
    }
}
```

This example accesses two component types (VelocityVector and Translation) and calculates new translation based on elapsed time.

### Compute Indices of Matching Chunks and Entities

Sometimes `IJobChunk` needs individual indices for each entity or chunk matching the `EntityQuery`. To compute these indices:

1. Add a `NativeArray<int> ChunkBaseEntityIndices` field to your `IJobChunk` implementation containing the base entity index for each chunk
2. Call `EntityQuery.CalculateBaseEntityIndexArrayAsync` on the `EntityQuery`, which allocates and populates a NativeArray
3. Assign the output array to `ChunkBaseEntityIndices` and add the returned `JobHandle` to your `IJobChunk`'s input dependencies
4. Inside `Execute`, look up the current chunk's base entity index: `baseEntityIndex = ChunkBaseEntityIndices[unfilteredChunkIndex]`
5. Call `.Dispose()` on the NativeArray after job completion, or use an allocator like `World.UpdateAllocator` that doesn't require explicit disposal

When adding commands to `EntityCommandBuffer.ParallelWriter`, use the `unfilteredChunkIndex` parameter as the `sortKey` argument.

## Declare the Data Your Job Accesses

Job struct fields fall into these categories:

- **ComponentTypeHandle fields**: Access entity components and buffers in the current chunk
- **ComponentLookup and BufferLookup fields**: Look up data for any entity (less efficient random access)
- **Other fields**: Declare additional fields and set values when scheduling
- **Output fields**: Write to NativeContainer fields only

### ComponentTypeHandle Fields

To access component data:

1. Define a `ComponentTypeHandle` field typed to the component's data type:
   ```csharp
   public ComponentTypeHandle<ObjectPosition> PositionTypeHandle;
   ```

2. Use this field in `Execute` to access the NativeArray containing data for that component type:
   ```csharp
   NativeArray<ObjectPosition> translations = chunk.GetNativeArray(ref PositionTypeHandle);
   ```

3. Declare the field on the system and initialize it in `OnCreate`:
   ```csharp
   ComponentTypeHandle<ObjectPosition> positionTypeHandle;
   positionTypeHandle = this.GetComponentTypeHandle<ObjectPosition>(false);
   ```

4. Update and set the field each time you schedule the job:
   ```csharp
   positionTypeHandle.Update(this);
   updateFromVelocityJob.PositionTypeHandle = positionTypeHandle;
   ```

**Always update component handle fields every scheduling**. Stale handles have outdated version numbers and trigger errors.

Array indices align across component data in a chunk—the same index accesses data for the same entity across all arrays.

Use `ComponentTypeHandle` to access types outside the `EntityQuery`, but check with `ArchetypeChunk.Has` before accessing.

The `isReadOnly` argument of `GetComponentTypeHandle` must accurately reflect component access in the job, preventing race conditions.

### ComponentLookup and BufferLookup Fields

When you need random-access data lookup (e.g., one entity depends on another's data):

- `ComponentLookup`: Access any entity's component
- `BufferLookup`: Access any entity's buffer

These provide array-like interfaces indexed by `Entity` objects.

This approach is less efficient than `EntityQuery` access and increases race condition risks. The job safety system cannot verify safety when you have access to all data through these objects.

Declare these fields on the job struct and set values before scheduling.

### Accessing Other Data

Define fields for other information needed during execution. Set values when scheduling—they remain constant for all chunks.

Example: Pass `DeltaTime` for moving objects:
```csharp
public float DeltaTime;
// Set in OnUpdate before scheduling
updateFromVelocityJob.DeltaTime = World.Time.DeltaTime;
```

## Write the Execute Method

Transform data from input state to desired output state using the job's `Execute` method.

### Execute Method Signature

```csharp
void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
```

### The chunk Parameter

The `chunk` parameter provides the `ArchetypeChunk` instance containing entities and components for this iteration. All chunk entities share the same component set because a chunk belongs to only one archetype.

Use `chunk` to get NativeArray instances for accessing component data. You must declare a corresponding component type handle field and set it when scheduling.

### The unfilteredChunkIndex Parameter

This is the current chunk's index in the list of all matching chunks. Chunks aren't necessarily processed in indexed order.

Use this value when:
- Writing to a native container with one element per chunk
- Using parallel-writing entity command buffers (pass as `sortKey`)

This index doesn't account for query filtering. Chunks not matching active filters aren't passed to `Execute`. If you need a filtered chunk index relative to processed chunks, use `EntityQuery.CalculateFilteredChunkIndexArrayAsync`.

### The useEnabledMask and chunkEnabledMask Parameters

If the `EntityQuery` includes enableable components, entities in a chunk might not match the query. For example, if an entity has a required component disabled, it doesn't match and shouldn't be processed. `IJobChunk` doesn't automatically skip these entities—you must handle enableable components correctly.

Parameters:
- `chunkEnabledMask`: Contains a bitmask where bit N indicates entity N matches the query
- `useEnabledMask`: Boolean providing early-out when `chunkEnabledMask` should be ignored (e.g., no enableable components, or all entities match)

**Best practice**: Pass these parameters to a new `ChunkEntityEnumerator` object:

```csharp
var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
while(enumerator.NextEntityIndex(out var i))
{
    // Process entity i
}
```

This efficiently handles cases with and without valid `chunkEnabledMask`.

If confident no enableable components exist, use a `for` loop from `0` to `chunk.Count`, but add `Assert.IsFalse(useEnabledMask)` for validation.

### Optional Components

For queries with `Any` filters or completely optional components, use `ArchetypeChunk.Has` before accessing:

```csharp
if (chunk.Has<Rotation>() && chunk.Has<LocalToWorld>())
{
    NativeArray<Rotation> rotations = chunk.GetNativeArray(ref RotationTypeHandle);
    NativeArray<LocalToWorld> transforms = chunk.GetNativeArray(ref LocalToWorldTypeHandle);

    var enumerator2 = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
    while(enumerator2.NextEntityIndex(out var i))
    {
        float3 direction = math.normalize(velocityVectors[i].Value);
        float3 up = transforms[i].Up;
        quaternion rotation = rotations[i].Value;

        quaternion look = quaternion.LookRotation(direction, up);
        quaternion newRotation = math.slerp(rotation, look, DeltaTime);

        rotations[i] = new Rotation() { Value = newRotation };
    }
}
```

Put the loop inside the optional component check to validate once per batch rather than per entity.

## Schedule the Job

Create an instance of your job struct, set fields, and schedule in `OnUpdate` of a `SystemBase` implementation:

```csharp
[RequireMatchingQueriesForUpdate]
public partial class UpdateTranslationFromVelocitySystem : SystemBase
{
    EntityQuery query;

    protected override void OnCreate()
    {
        query = new EntityQueryBuilder(Allocator.Temp)
            .WithAllRW<ObjectPosition>()
            .WithAll<VelocityVector>()
            .Build(this);
    }

    protected override void OnUpdate()
    {
        var updateFromVelocityJob = new UpdateTranslationFromVelocityJob();

        updateFromVelocityJob.PositionTypeHandle = 
            this.GetComponentTypeHandle<ObjectPosition>(false);
        updateFromVelocityJob.VelocityTypeHandle = 
            this.GetComponentTypeHandle<VelocityVector>(true);

        updateFromVelocityJob.DeltaTime = World.Time.DeltaTime;

        this.Dependency = updateFromVelocityJob.ScheduleParallel(query, this.Dependency);
    }
}
```

When calling `GetComponentTypeHandle`, set `isReadOnly` to true for read-only components. This significantly impacts job scheduling efficiency. Settings must match struct definitions and `EntityQuery` declarations.

### Scheduling Options

Choose the appropriate method to control job execution:

- **Run**: Executes immediately on the main thread and completes any dependent scheduled jobs
- **Schedule**: Schedules on a worker thread after dependencies. The `Execute` method runs once per matching chunk, processed sequentially
- **ScheduleParallel**: Like Schedule, but chunks process in parallel (with available worker threads)

## Skipping Chunks with Unchanged Entities

If updates are only needed when component values change, add that component type to the change filter of the `EntityQuery`:

```csharp
EntityQuery query;

protected override void OnCreate()
{
    query = new EntityQueryBuilder(Allocator.Temp)
        .WithAllRW<Output>()
        .WithAll<InputA, InputB>()
        .Build(this);
    query.SetChangedVersionFilter(
        new ComponentType[]
        {
            typeof(InputA),
            typeof(InputB)
        }
    );
}
```

The change filter supports up to two components. For more components or without `EntityQuery`, check manually using `ArchetypeChunk.DidChange` comparing the chunk's change version to the system's `LastSystemVersion`. If it returns false, skip the chunk because no components of that type changed:

```csharp
[BurstCompile]
struct UpdateOnChangeJob : IJobChunk
{
    [ReadOnly] public ComponentTypeHandle<InputA> InputATypeHandle;
    [ReadOnly] public ComponentTypeHandle<InputB> InputBTypeHandle;
    public ComponentTypeHandle<Output> OutputTypeHandle;
    public uint LastSystemVersion;

    [BurstCompile]
    public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
    {
        var inputAChanged = chunk.DidChange(ref InputATypeHandle, LastSystemVersion);
        var inputBChanged = chunk.DidChange(ref InputBTypeHandle, LastSystemVersion);

        if (!(inputAChanged || inputBChanged))
            return;

        var inputAs = chunk.GetNativeArray(ref InputATypeHandle);
        var inputBs = chunk.GetNativeArray(ref InputBTypeHandle);
        var outputs = chunk.GetNativeArray(ref OutputTypeHandle);

        var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
        while(enumerator.NextEntityIndex(out var i))
        {
            outputs[i] = new Output { Value = inputAs[i].Value + inputBs[i].Value };
        }
    }
}
```

Assign the field value before scheduling:

```csharp
[RequireMatchingQueriesForUpdate]
public partial class UpdateDataOnChangeSystem : SystemBase {

    EntityQuery query;

    protected override void OnUpdate()
    {
        var job = new UpdateOnChangeJob();

        job.LastSystemVersion = this.LastSystemVersion;

        job.InputATypeHandle = GetComponentTypeHandle<InputA>(true);
        job.InputBTypeHandle = GetComponentTypeHandle<InputB>(true);
        job.OutputTypeHandle = GetComponentTypeHandle<Output>(false);

        this.Dependency = job.ScheduleParallel(query, this.Dependency);
    }

    protected override void OnCreate()
    {
        query = new EntityQueryBuilder(Allocator.Temp)
            .WithAllRW<Output>()
            .WithAll<InputA, InputB>()
            .Build(this);
    }
}
```

For efficiency, change version applies to whole chunks, not individual entities. If another job with write access to that component type accesses a chunk, ECS increments the change version and `DidChange` returns true—even if the job doesn't actually modify values. Always use read-only access when reading without updating.

---

## Outgoing Links

- https://docs.unity3d.com/ - docs.unity3d.com
- https://docs.unity3d.com/6000.4/Documentation/ScriptReference/Unity.Collections.NativeArray_1.html - NativeArray
- ../api/Unity.Entities.ChunkEntityEnumerator.html - ChunkEntityEnumerator
- ../api/Unity.Entities.ComponentTypeHandle-1.html - ComponentTypeHandle
- systems-entityquery-intro.html - EntityQuery
- ../api/Unity.Entities.EntityArchetype.html - EntityArchetype
- systems-entityquery-create.html - Create an EntityQuery
- ../api/Unity.Entities.ArchetypeChunk.Has.html - ArchetypeChunk.Has
- ../api/Unity.Entities.IJobChunk.Execute.html - IJobChunk.Execute
- ../api/Unity.Entities.ArchetypeChunk.html - ArchetypeChunk
- ../api/Unity.Entities.ComponentSystemBase.GetComponentTypeHandle.html - ComponentSystemBase.GetComponentTypeHandle
- systems-version-numbers.html - version number
- ../api/Unity.Entities.ComponentLookup-1.html - ComponentLookup
- ../api/Unity.Entities.BufferLookup-1.html - BufferLookup
- ../api/Unity.Entities.Entity.html - Entity
- components-enableable.html - enableable components
- iterating-data-ijobentity.html - IJobEntity
- ../api/Unity.Entities.EntityQueryBuilder.WithAny.html - Any
- ../api/Unity.Entities.JobChunkExtensions.Run.html - Run
- ../api/Unity.Entities.JobChunkExtensions.Schedule.html - Schedule
- ../api/Unity.Entities.JobChunkExtensions.ScheduleParallel.html - ScheduleParallel
- ../api/Unity.Entities.ArchetypeChunk.DidChange.html - ArchetypeChunk.DidChange
- ../api/Unity.Entities.ComponentSystemBase.LastSystemVersion.html - LastSystemVersion
- systems-entity-command-buffers.html - entity command buffer
- ../api/Unity.Entities.EntityCommandBuffer.ParallelWriter.html - EntityCommandBuffer.ParallelWriter
- ../api/Unity.Entities.EntityQuery.CalculateBaseEntityIndexArrayAsync.html - EntityQuery.CalculateBaseEntityIndexArrayAsync
- ../api/Unity.Entities.EntityQuery.CalculateFilteredChunkIndexArrayAsync.html - EntityQuery.CalculateFilteredChunkIndexArrayAsync
- ../api/Unity.Entities.SystemBase.html - SystemBase
- https://docs.unity3d.com/6000.0/Documentation/Manual/JobSystemNativeContainer.html - NativeContainer
- https://docs.unity3d.com/Manual/TermsOfUse.html - Trademarks and terms of use
- https://unity.com/legal - Legal
- https://unity.com/legal/privacy-policy - Privacy Policy
- https://unity.com/legal/cookie-policy - Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information - Do Not Sell or Share My Personal Information
