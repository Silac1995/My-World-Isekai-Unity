---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/concepts-safety.html
fetched: 2026-05-05
section: concepts
---

# Safety in Entities

The Entities package enables data-oriented design principles for efficient data transformation. It leverages the Burst compiler and native interop to access data directly, which sometimes conflicts with C# safety mechanisms.

## Overview

Many Entities package APIs use unsafe code blocks and raw pointers for optimal performance. Some APIs return references that might outlive their referenced data. This documentation explains how safety functions within Entities and common pitfalls to avoid.

## Guarded Safety Violation

The Entities framework typically guards against safety issues in the Editor and when safety checks are enabled. In these contexts, safety errors should throw informative exceptions preventing crashes. However, runtime builds offer no such guarantees—crashes or memory corruption may occur.

You can disable certain safety checks for jobs through the **Safety Checks** setting in the Editor (**Jobs** > **Burst** > **Safety Checks**). See the Data access errors documentation for additional details.

### Structural Changes

Structural changes represent one of the most common safety issues. When an entity's archetype is modified during a structural change, the entity relocates to another chunk.

**Note:** Enabling and disabling enableable components is not a structural change. However, all jobs modifying component enabled status must complete before checking that status.

The Entities API stores data in chunks typically accessed through the job system or main thread. The job system manages data safety for NativeContainers using read/write notations. However, structural changes may move data in memory, invalidating any held references.

#### ExclusiveEntityTransaction

Structural changes must generally occur on the main thread via the world's `EntityManager`. The `ExclusiveEntityTransaction` feature temporarily allows a single worker thread (running `IJob`) to safely perform structural-change operations on a World's entities, freeing the main thread for other work.

This feature primarily enables secondary or streaming Worlds to modify entities safely without blocking main-thread processing. Certain `EntityManager` operations depend on main-thread-only features and won't function correctly from worker threads. Only operations directly exposed by `ExclusiveEntityTransaction` are officially supported.

### RefRW/RefRO

The Entities package provides explicit reference types marking contained types as ReadWrite (`RefRW`) or ReadOnly (`RefRO`). These types include checks ensuring validity when safety checks are enabled. Structural changes may invalidate contained types.

## Unguarded Safety Violation

Several cases lack protection. This section outlines scenarios where crashes or memory corruption may occur through Entities APIs in the Editor.

### IJobEntity

`IJobEntity` schedules jobs with an external `EntityQuery`, retrieving entities and executing the `Execute` method. ECS doesn't verify that entities possess the component arguments, so you must keep these synchronized. Mismatched `Execute` parameters and query components may cause crashes or memory corruption.

### InternalCompilerInterface

The `InternalCompilerInterface` static class exposes DOTS internals to source-generated code, necessary because generated code typically calls only public APIs.

**Warning:** Do not use `InternalCompilerInterface` APIs. They exist solely for generated-code context and will likely change.

---

## Outgoing Hyperlinks

- **Burst compiler documentation:** https://docs.unity3d.com/Packages/com.unity.burst@latest
- **Job System documentation:** https://docs.unity3d.com/6000.0/Documentation/Manual/JobSystem.html
- **Data access errors documentation:** systems-looking-up-data.html#data-access-errors
- **Structural changes documentation:** concepts-structural-changes.html
- **Enableable components documentation:** components-enableable.html
- **EntityQuery documentation:** systems-entityquery.html
- **IJobEntity documentation:** iterating-data-ijobentity.html
- **Terms of Use:** https://docs.unity3d.com/Manual/TermsOfUse.html
- **Privacy Policy:** https://unity.com/legal/privacy-policy
- **Cookie Policy:** https://unity.com/legal/cookie-policy
