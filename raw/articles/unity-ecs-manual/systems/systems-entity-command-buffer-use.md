---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/systems-entity-command-buffer-use.html
fetched: 2026-05-05
section: systems
---

# Use an Entity Command Buffer

You can record [entity command buffers](systems-entity-command-buffers.html) (ECBs) in jobs, and on the main thread.

## Use an entity command buffer in a job

You cannot perform [structural changes](concepts-structural-changes.html) in a job except inside an `ExclusiveEntityTransaction`, so you can use an ECB to record structural changes to play back after the job is complete. For example:

```csharp
// Single-threaded scheduling using IJobEntity (replaces deprecated Entities.ForEach).
[BurstCompile]
partial struct AddBarJob : IJobEntity
{
    public EntityCommandBuffer Ecb;

    void Execute(Entity e, in FooComp foo)
        {
            if (foo.Value > 0)
            {
                // Record a command that will later add BarComp to the entity.
            Ecb.AddComponent<BarComp>(e);
            }
    }
}

protected override void OnUpdate()
{
    // Buffer grows as needed.
    var ecb = new EntityCommandBuffer(Allocator.TempJob);

    // Schedule a single job (not parallel) that records commands.
    new AddBarJob { Ecb = ecb }.Schedule();

    // Complete the job so we can play back on the main thread.
    Dependency.Complete();

    // Apply the recorded structural changes.
    ecb.Playback(EntityManager);

    // Dispose the ECB.
    ecb.Dispose();
}
```

### Parallel jobs

For use in a [parallel job](https://docs.unity3d.com/6000.0/Documentation/Manual/JobSystemParallelForJobs.html), employ [`EntityCommandBuffer.ParallelWriter`](../api/Unity.Entities.EntityCommandBuffer.ParallelWriter.html), which concurrently records in a thread-safe manner to a command buffer:

```csharp
EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.TempJob);

// Methods of this writer record commands to
// the EntityCommandBuffer in a thread-safe way.
EntityCommandBuffer.ParallelWriter parallelEcb = ecb.AsParallelWriter();
```

**Note:** "Only recording needs to be thread-safe for concurrency in parallel jobs. Playback is always single-threaded on the main thread."

For information on deterministic playback in parallel jobs, refer to the documentation on [Entity command buffer playback](systems-entity-command-buffer-playback.html#deterministic-playback-in-parallel-jobs).

## Use an entity command buffer on the main thread

You can record ECB changes on the main thread in these situations:

- To delay your changes.
- To play back a set of changes multiple times. Refer to the information on [multi-playback](systems-entity-command-buffer-playback.html#multi-playback).
- To play back many different kinds of changes in one consolidated place, which proves more efficient than distributing changes across different frame segments.

Every structural change operation triggers a [sync point](concepts-structural-changes.html#sync-points), meaning the operation must wait for some or all scheduled jobs to complete. Consolidating structural changes into an ECB reduces sync points during the frame.

**Note:** "If you have a lot of the same types of commands in an ECB, and you can afford to make the change instantly, it can be faster to use the EntityManager variants on whole batches of entities at once."

---

## Outgoing Hyperlinks

- https://docs.unity3d.com/ - docs.unity3d.com
- ../logo.svg - Logo
- ../index.html - Home
- systems-entity-command-buffers.html - Entity command buffers
- concepts-structural-changes.html - Structural changes
- https://docs.unity3d.com/6000.0/Documentation/Manual/JobSystemParallelForJobs.html - Parallel For Jobs
- ../api/Unity.Entities.EntityCommandBuffer.ParallelWriter.html - EntityCommandBuffer.ParallelWriter API
- systems-entity-command-buffer-playback.html - Entity command buffer playback
- https://docs.unity3d.com/Manual/TermsOfUse.html - Trademarks and terms of use
- https://unity.com/legal - Legal
- https://unity.com/legal/privacy-policy - Privacy Policy
- https://unity.com/legal/cookie-policy - Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information - Do Not Sell or Share My Personal Information
