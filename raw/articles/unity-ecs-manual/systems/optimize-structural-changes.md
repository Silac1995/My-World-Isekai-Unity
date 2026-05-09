---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/optimize-structural-changes.html
fetched: 2026-05-05
section: systems
---

# Optimize Structural Changes

Structural changes cause sync points that impact performance. Unity must perform several CPU tasks during structural changes that can also affect performance.

## Structural Change Process

When adding a component to an entity, Unity performs these steps:

1. Check if an `EntityArchetype` for the new configuration already exists, creating one if needed
2. Check if the archetype has free chunk space; allocate a new chunk if necessary
3. Use `Memcpy()` to copy existing components into the new chunk
4. Create or copy the new component into the new chunk
5. Update `EntityManager` so the entity points to its new index in the new chunk
6. Use `swap_back()` to remove the original entity from the old chunk
   - Free chunk memory if it was the only entity in that chunk
   - Otherwise, update `EntityManager` with the new index
7. Clear cached chunk lists for every `EntityQuery` involving that chunk's archetype

Individual steps aren't inherently slow, but "when thousands of entities change archetypes in a single frame, this can significantly impact performance." Processing overhead scales with the number of EntityArchetypes and EntityQueries declared at runtime.

## Structural Changes Approach Comparison

| Method | Description | Time (ms) |
|--------|-------------|-----------|
| EntityManager and query with enableable components | Enable a previously disabled component implementing `IEnableable` | 0.03 |
| EntityManager and query | Pass `EntityQuery` to `EntityManager.AddComponent` | 3.5 |
| EntityManager and NativeArray | Pass `NativeArray<Entity>` to `EntityManager` | 35 |
| Entity command buffer and playback query | Pass `EntityQuery` to `EntityCommandBuffer` with `EntityQueryCaptureMode.AtPlayback` flag | 3.5 |
| Entity command buffer and NativeArray | Pass `NativeArray<Entity>` to `EntityCommandBuffer` | 35 |
| Entity command buffer and job system with IJobChunk | Use `IJobChunk` across multiple worker threads with `EntityCommandBuffer.ParallelWriter` | 17 |
| Entity command buffer and job system with IJobEntity | Use `IJobEntity` across multiple worker threads | 170 |

## Optimize Native Arrays for Chunks

Build `NativeArray` entities to match their order in memory. Use `IJobChunk` to iterate over chunks matching your target query. The job can iterate over entities in order and build the `NativeArray`. Pass this to `EntityCommandBuffer.ParallelWriter` to queue changes. When executed, "entities are accessed one by one via lookups to the `EntityManager`. This process increases the chance of CPU cache hits because it accesses the entities in order."

## Entity Command Buffers and Entity Queries

When an `EntityQuery` is passed to an `EntityManager` method, it operates at chunk level rather than individual entities. When passed to an `EntityCommandBuffer` method, chunk contents might change before the buffer executes due to other structural changes.

Use `EntityQueryCaptureMode.AtPlayback` to store the `EntityQuery` and evaluate it when the buffer executes, avoiding one-entity-at-a-time structural changes.

## Enable Systems to Avoid Structural Changes

Instead of removing components from entities to stop system processing, disable the system itself. Add or remove a component to signal if the system should be enabled, then call `SystemState.RequireForUpdate()` in your system's `OnCreate()` method with that component specified. If the component exists, your system updates; removing it stops updates with only one component add/remove operation.

You can also use the `Enabled` flag in `SystemState` to disable a system.

## Structural Changes During Entity Creation

Avoid adding components one at a time at runtime. "Calling `EntityManager.AddComponent()` creates a new archetype and moves the entity into a whole new chunk." Each archetype persists for your application's runtime and contributes performance overhead.

Create the final archetype first, then create the entity from it:

```csharp
// Cache this archetype if we intend to use it again later  
var newEntityArchetype = state.EntityManager.CreateArchetype(typeof(Foo), typeof(Bar), typeof(Baz));  
var entity = EntityManager.CreateEntity(newEntityArchetype);

// Better yet, if you want to create lots of identical entities at the same time  
var entities = new NativeArray<Entity>(10000, Allocator.Temp);  
state.EntityManager.CreateEntity(newEntityArchetype, entities);
```

## Adding or Removing Multiple Components Simultaneously

Use `ComponentTypeSet` to specify all components to add or remove at once, minimizing structural changes and redundant archetypes:

- `AddComponent(Entity, ComponentTypeSet)`
- `AddComponent(EntityQuery, ComponentTypeSet)`
- `AddComponent(SystemHandle, ComponentTypeSet)`
- Equivalent `RemoveComponent()` methods

## Measuring Performance of Structural Changes

Use the Structural Changes Profiler module to monitor structural changes' impact on runtime performance.

## Additional Resources

- [Structural changes concepts](concepts-structural-changes.html)
- [Use entity command buffers](systems-entity-command-buffer-use.html)
- [Enableable components](components-enableable.html)

---

## Outgoing Links

- http://docs.unity3d.com/ - docs.unity3d.com
- ../index.html - Unity Entities Documentation Home
- concepts-structural-changes.html - Structural changes
- concepts-archetypes.html - archetype
- components-enableable.html - Enableable components
- systems-entity-command-buffer-use.html - Entity command buffers
- iterating-data-ijobchunk.html - IJobChunk
- ../api/Unity.Entities.EntityQueryCaptureMode.html - EntityQueryCaptureMode.AtPlayback
- ../api/Unity.Entities.SystemState.html - SystemState
- ../api/Unity.Entities.SystemState.RequireForUpdate.html - RequireForUpdate()
- ../api/Unity.Entities.ComponentTypeSet.html - ComponentTypeSet
- ../api/Unity.Entities.EntityManager.AddComponent.html - AddComponent methods
- ../api/Unity.Entities.EntityManager.RemoveComponent.html - RemoveComponent() methods
- profiler-module-structural-changes.html - Structural Changes Profiler module
- https://docs.unity3d.com/6000.4/Documentation/Manual/Profiler.html - Profiler overview
- https://docs.unity3d.com/Manual/TermsOfUse.html - Trademarks and terms of use
- https://unity.com/legal - Legal
- https://unity.com/legal/privacy-policy - Privacy Policy
- https://unity.com/legal/cookie-policy - Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information - Do Not Sell or Share My Personal Information
