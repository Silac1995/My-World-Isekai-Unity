---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/systems-entity-command-buffer-playback.html
fetched: 2026-05-05
section: systems
---

# Entity Command Buffer Playback

## Overview

When recording commands in an entity command buffer (ECB) across multiple threads in a parallel job, the order becomes non-deterministic due to job scheduling variations. While determinism isn't always critical, it facilitates debugging and enables networking scenarios requiring consistent cross-machine results - though it may impact performance.

## Deterministic Playback in Parallel Jobs

You cannot prevent non-deterministic recording order in parallel jobs, but you can ensure deterministic playback:

1. Record an `int` sort key as the first argument to each ECB method
2. Sort commands by these keys during playback, before execution

"If the recorded sort keys are independent from the scheduling, then the sorting makes the playback order deterministic."

For parallel jobs, use `ChunkIndexInQuery` as your sort key - a zero-based index representing each chunk's position in the query results. This provides consistent, unique associations independent of scheduling.

### Example Implementation

```csharp
[RequireMatchingQueriesForUpdate]
partial struct MultiThreadedSchedule_ECB : ISystem
{
    partial struct ParallelRecordingJob : IJobEntity
    {
        internal EntityCommandBuffer.ParallelWriter ecbParallel;

        // ChunkIndexInQuery ensures deterministic playback
        void Execute(Entity e, [ChunkIndexInQuery] int sortKey, in FooComp foo)
        {
            if (foo.Value > 0)
            {
                ecbParallel.AddComponent<BarComp>(sortKey, e);
            }
        }
    }

    public void OnUpdate(ref SystemState state)
    {
        EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.TempJob);
        new ParallelRecordingJob { ecbParallel = ecb.AsParallelWriter() }.Schedule();
        
        state.Dependency.Complete();
        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }
}
```

## Multi Playback

Calling `Playback` multiple times throws an exception by default. Use `PlaybackPolicy.MultiPlayback` to enable repeated playback:

```csharp
EntityCommandBuffer ecb =
    new EntityCommandBuffer(Allocator.TempJob, PlaybackPolicy.MultiPlayback);

// ... record commands

ecb.Playback(state.EntityManager);
ecb.Playback(state.EntityManager); // Additional playbacks now allowed
ecb.Dispose();
```

Multi-playback enables repeatedly spawning entity sets by recording once and replaying multiple times.

## Related Resources

- [Automatically play back entity command buffers](systems-entity-command-buffer-automatic-playback.html)

---

**Outgoing Links:**
- https://docs.unity3d.com/Manual/TermsOfUse.html - Trademarks and terms of use
- https://unity.com/legal - Legal
- https://unity.com/legal/privacy-policy - Privacy Policy
- https://unity.com/legal/cookie-policy - Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information - Do Not Sell or Share My Personal Information
