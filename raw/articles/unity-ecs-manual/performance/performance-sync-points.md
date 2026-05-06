---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/performance-sync-points.html
fetched: 2026-05-05
section: performance
---

# Manage Sync Points

## Overview

You cannot perform structural changes directly within a job, as this could invalidate other scheduled jobs and create a synchronization point (sync point).

A sync point is an execution point where the main thread pauses and waits for all previously scheduled jobs to finish. These points reduce the ability to utilize all available worker threads. Developers should work to minimize sync points.

## What Causes Sync Points

Structural modifications to ECS data are the primary source of sync points. Additional causes include:

- Using `Run` to execute a job synchronously
- Using idiomatic `foreach` patterns to iterate over component data

In these scenarios, Unity halts the main thread and waits for job dependencies to complete before executing the job synchronously on the main thread.

## Impact of Structural Changes

Structural changes require a sync point and invalidate all direct references to component data, including:

- Instances of `DynamicBuffer`
- Results from methods providing direct component access, such as `ComponentSystemBase.GetComponentDataFromEntity`

## Using Entity Command Buffers

Entity command buffers allow you to queue structural changes rather than executing them immediately. Commands stored in a buffer can be played back later in the frame, consolidating all structural changes into a single operation and improving performance.

Standard `ComponentSystemGroup` instances provide an `EntityCommandBufferSystem` as both the first and last updated system in the group. Obtaining an entity command buffer from these standard systems ensures all structural changes occur at one point, resulting in a single sync point. Command buffers can also record structural changes within jobs, rather than restricting changes to the main thread only.

## Grouping Systems

If an entity command buffer is unsuitable for your use case, group any systems performing structural changes together in execution order. Two systems making structural changes create only one sync point when updating sequentially, unless the first system also schedules jobs—in which case the second will immediately synchronize on those jobs.

## Additional Resources

- [Structural changes concepts](concepts-structural-changes.html)
- [Optimize structural changes](optimize-structural-changes.html)
- [Entity command buffers introduction](systems-entity-command-buffers.html)

---

## Outgoing Hyperlinks

- https://docs.unity3d.com/ - docs.unity3d.com
- https://docs.unity3d.com/Manual/TermsOfUse.html - Trademarks and terms of use
- https://unity.com/legal - Legal
- https://unity.com/legal/privacy-policy - Privacy Policy
- https://unity.com/legal/cookie-policy - Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information - Do Not Sell or Share My Personal Information
