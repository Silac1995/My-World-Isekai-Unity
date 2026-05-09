---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/systems-optimizing.html
fetched: 2026-05-05
section: systems
---

# Optimize Systems

Every system has inherent performance overhead. When you create a system, it includes these default behaviors:

- Each system accesses an `EntityTypeHandle` structure to iterate over chunks matching an `EntityQuery`. Because `EntityTypeHandle` structs become invalidated by structural changes, each system gets its own copy before each `OnUpdate`, causing CPU overhead that scales linearly with the number of active systems.

- When scheduling or running a job, your code might need to obtain a `ComponentLookup` or `BufferLookup` to pass to the job. Applications with many systems might create identical lookup structures across different systems. These copies require updating every frame when used, accounting for potential structural changes.

- Every system contains one `JobHandle` named `Dependency`, either in the `SystemState` object or the `SystemBase` class. There is overhead in calculating this before system execution and considering the system's scheduled jobs for the next system's `Dependency` handle. More systems means more jobs and greater complexity in the `JobHandle` dependency chain.

## Managing OnUpdate Calls

You can add the `[RequireMatchingQueriesForUpdate]` attribute to a system to ensure its `OnUpdate` method only executes when there's data to process. However, the system still performs a check each frame to determine if entities match any of its queries.

Systems with matching entities run their `Update` methods; systems without don't update. While the test is fast, the cumulative time grows with many systems. Alternatively, you can use an `if` check at the top of `OnUpdate`, which may be faster than `[RequireMatchingQueriesForUpdate]` depending on implementation.

Avoid using `[RequireMatchingQueriesForUpdate]` on systems that don't run frequently. For instance, a system for a player character doesn't need this check since the player always exists. However, a level-specific system can use this attribute if it only runs at certain application points.

## Burst Compiler Behavior

Using the Burst compiler results in lower overhead for systems created with `ISystem` compared to those created with `SystemBase`. For optimal performance with Burst, use `ISystem` based systems.

## Additional Resources

- [Organize system data](systems-data.html)
- [`[RequireMatchingQueriesForUpdate]` API documentation](../api/Unity.Entities.RequireMatchingQueriesForUpdateAttribute.html)

---

## Outgoing Hyperlinks

- https://docs.unity3d.com/Documentation/ScriptReference/Unity.Jobs.JobHandle.html (JobHandle documentation)
- https://docs.unity3d.com/Packages/com.unity.burst@latest (Burst compiler documentation)
