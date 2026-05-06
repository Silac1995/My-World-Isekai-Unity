---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/systems-entity-command-buffer-automatic-playback.html
fetched: 2026-05-05
section: systems
---

# Automatic Playback and Disposal of Entity Command Buffers

## Overview

The `EntityCommandBufferSystem` automates the playback and disposal of entity command buffers (ECBs), eliminating the need for manual management.

## Basic Usage

To use automatic ECB playback:

1. Obtain the singleton instance of your desired `EntityCommandBufferSystem`
2. Create an `EntityCommandBuffer` through that singleton
3. Record commands to the buffer

### Example Code

```csharp
// ... in a system.

// Assume an EntityCommandBufferSystem exists named FooECBSystem.
// This call to GetSingleton automatically registers the job so that
// it gets completed by the ECB system.
var singleton = SystemAPI.GetSingleton<FooECBSystem.Singleton>();

// Create a command buffer that will be played back
// and disposed by MyECBSystem.
EntityCommandBuffer ecb = singleton.CreateCommandBuffer(state.WorldUnmanaged);

// An IJobEntity with no argument to Schedule implicitly
// assigns its returned JobHandle to this.Dependency
new MyParallelRecordingJob() { ecbParallel = ecb.AsParallelWriter() }.Schedule();
```

For `SystemBase` systems, access the unmanaged world via: `World.Unmanaged`.

## Important Note

"Don't manually play back or dispose of an EntityCommandBuffer that you've created with an EntityCommandBufferSystem. The EntityCommandBufferSystem does both for you when it runs."

## EntityCommandBufferSystem Lifecycle

Each update cycle, an `EntityCommandBufferSystem` performs three operations:

1. Completes all registered jobs and singleton component jobs to ensure recording finishes
2. Plays back all ECBs in creation order
3. Disposes of all `EntityCommandBuffer` instances

## Default Systems

The default world includes these `EntityCommandBufferSystem` implementations:

- `BeginInitializationEntityCommandBufferSystem`
- `EndInitializationEntityCommandBufferSystem`
- `BeginFixedStepSimulationEntityCommandBufferSystem`
- `EndFixedStepSimulationEntityCommandBufferSystem`
- `BeginVariableRateSimulationEntityCommandBufferSystem`
- `EndVariableRateSimulationEntityCommandBufferSystem`
- `BeginSimulationEntityCommandBufferSystem`
- `EndSimulationEntityCommandBufferSystem`
- `BeginPresentationEntityCommandBufferSystem`

No `EndPresentationEntityCommandBufferSystem` exists because structural changes cannot occur after rendering data reaches the renderer. Use `BeginInitializationEntityCommandBufferSystem` instead, as each frame's end precedes the next frame's beginning.

## Creating Custom Systems

If default systems don't suit your needs, create a custom `EntityCommandBufferSystem`:

```csharp
// You should specify where exactly in the frame this ECB system should update.
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(FooSystem))]
public partial class MyECBSystem : EntityCommandBufferSystem
{
    // The singleton component data access pattern should be used to safely access
    // the command buffer system. This data will be stored in the derived ECB System's
    // system entity.

    public unsafe struct Singleton : IComponentData, IECBSingleton
    {
        internal UnsafeList<EntityCommandBuffer>* pendingBuffers;
        internal AllocatorManager.AllocatorHandle allocator;

        public EntityCommandBuffer CreateCommandBuffer(WorldUnmanaged world)
        {
            return EntityCommandBufferSystem
                .CreateCommandBuffer(ref *pendingBuffers, allocator, world);
        }

        // Required by IECBSingleton
        public void SetPendingBufferList(ref UnsafeList<EntityCommandBuffer> buffers)
        {
            var ptr = UnsafeUtility.AddressOf(ref buffers);
            pendingBuffers = (UnsafeList<EntityCommandBuffer>*)ptr;
        }

        // Required by IECBSingleton
        public void SetAllocator(Allocator allocatorIn)
        {
            allocator = allocatorIn;
        }

        // Required by IECBSingleton
        public void SetAllocator(AllocatorManager.AllocatorHandle allocatorIn)
        {
            allocator = allocatorIn;
        }
    }

    protected override void OnCreate()
    {
        base.OnCreate();

        this.RegisterSingleton<Singleton>(ref PendingBuffers, World.Unmanaged);
    }
}
```

## Deferred Entities

The `CreateEntity` and `Instantiate` methods record creation commands without immediately creating entities. They return `Entity` values with negative indices representing placeholder entities that exist only within the recorded ECB:

```csharp
// ... in a system

EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.TempJob);

Entity placeholderEntity = ecb.CreateEntity();

// Valid to use placeholderEntity in later commands of same ECB.
ecb.AddComponent<FooComp>(placeholderEntity);

// The real entity is created, and
// FooComp is added to the real entity.
ecb.Playback(state.EntityManager);

// Exception! The placeholderEntity has no meaning outside
// the ECB which created it, even after playback.
state.EntityManager.AddComponent<BarComp>(placeholderEntity);

ecb.Dispose();
```

## Entity Field Remapping

Commands containing `Entity` fields in `AddComponent`, `SetComponent`, or `SetBuffer` have their placeholder entity references automatically remapped to actual entities during playback:

```csharp
// ... in a system

EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.TempJob);

// For all entities with a FooComp component...
foreach (var (f, e) in SystemAPI.Query<FooComp>().WithEntityAccess())
{
    // In playback, an actual entity will be created
    // that corresponds to this placeholder entity.
    Entity placeholderEntity = ecb.CreateEntity();

    // (Assume BarComp has an Entity field called TargetEnt.)
    BarComp bar = new BarComp { TargetEnt = placeholderEntity };

    // In playback, TargetEnt will be assigned the
    // actual Entity that corresponds to placeholderEntity.
    ecb.AddComponent(e, bar);
}

// After playback, each entity with FooComp now has a
// BarComp component whose TargetEnt references a new entity.
ecb.Playback(state.EntityManager);

ecb.Dispose();
```

---

## Outgoing Hyperlinks

- https://docs.unity3d.com/Manual/TermsOfUse.html - Trademarks and terms of use
- https://unity.com/legal - Legal
- https://unity.com/legal/privacy-policy - Privacy Policy
- https://unity.com/legal/cookie-policy - Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information - Do Not Sell or Share My Personal Information
