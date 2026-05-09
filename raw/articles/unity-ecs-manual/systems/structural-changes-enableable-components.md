---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/structural-changes-enableable-components.html
fetched: 2026-05-05
section: systems
---

# Manage Structural Changes with Enableable Components

## Overview

Enableable components provide an alternative to traditional component addition and removal. Rather than creating [structural changes](concepts-structural-changes.html), disabled components are treated as absent when evaluating entity queries. An entity with a disabled component won't match queries requiring that component, but will match queries excluding it (if other criteria are met).

## Component Operations Behavior

The `EntityManager` continues to recognize entities with disabled components as still possessing those components. Here's how standard operations behave when component `T` is disabled on entity `E`:

| Method | Outcome |
|--------|---------|
| `HasComponent<T>(E)` | Returns true |
| `GetComponent<T>(E)` | Returns the component's current value |
| `SetComponent<T>(E,value)` | Updates the component's value |
| `RemoveComponent<T>(E)` | Removes the component from the entity |
| `AddComponent<T>(E)` | Performs no action (component already exists) |

## When to Use Enableable Components

Enableable components aren't universally optimal. They can influence job and system performance when accessing archetypes containing them. Consider alternatives if:

- Structural changes occur infrequently
- You prioritize minimizing chunk fragmentation and maximizing CPU cache efficiency

### Performance Considerations

**Advantages:** When iterating entities with disabled components, chunks where all instances are disabled get skipped entirely. Fully-enabled chunks process efficiently, allowing Burst compilation to optimize multiple simultaneous component accesses.

**Trade-offs:** Entities with numerous components consume more chunk space. This density sometimes forces more chunks, potentially increasing runtime memory usage and contributing to chunk fragmentation.

Serialized prefabs and subscenes with many components require additional disk storage, affecting load times.

## Related Resources

- [Enableable components introduction](components-enableable-intro.html)
- [Using enableable components](components-enableable-use.html)
- [Manage structural changes](systems-manage-structural-changes-intro.html)
- [Optimize structural changes](optimize-structural-changes.html)
- [Chunk fragmentation performance guide](performance-chunk-allocations.html)
