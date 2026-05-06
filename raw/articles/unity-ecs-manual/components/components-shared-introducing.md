---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/components-shared-introducing.html
fetched: 2026-05-05
section: components
---

# Shared Components Introduction

Shared components organize entities into chunks based on matching shared component values, which helps eliminate duplicate data across entities. Unity stores all entities of the same archetype with identical shared component values together within chunks.

You can create both managed and unmanaged shared components, with managed variants sharing the same characteristics as regular managed components.

## Shared Component Value Storage

Within each world, Unity maintains shared component values in separate arrays rather than inside ECS chunks. Chunks store handles that reference the appropriate shared component values for their archetype. Entities residing in the same chunk share an identical shared component value. Multiple chunks can reference the same shared component handle, meaning there's no cap on how many entities can utilize the same shared component value.

Modifying a shared component value for an entity triggers a structural change. Unity relocates the entity to a chunk using the new shared component value. If an equivalent value already exists in the shared component value array, the entity moves to a chunk storing that existing value's index. Otherwise, Unity appends the new value to the array and moves the entity to a fresh chunk storing that new index.

Unmanaged and managed shared components are stored separately. Unmanaged shared components are accessible to Burst-compiled code through unmanaged shared component APIs like `SetUnmanagedSharedComponentData`.

## Overriding Default Comparison Behavior

Implement `IEquatable<YourSharedComponent>` to customize how ECS compares shared component instances. For unmanaged shared components, apply the `[BurstCompile]` attribute to the struct, `Equals` method, and `GetHashCode` method for performance improvements.

## Sharing Components Between Worlds

For resource-intensive managed objects like blob assets, implement the `IRefCounted` interface with `Retain` and `Release` methods to maintain a single copy across all worlds. For unmanaged shared components, add `[BurstCompile]` attributes to the struct and both methods for enhanced performance.

## Important Considerations

Shared components depend on using the Entities API to modify their values and any referenced objects. If a shared component contains reference types or pointers, avoid modifying the referenced object outside the Entities API.

## Outgoing Links

- [Optimize shared components](components-shared-optimize.html)
- [IEquatable<T>](https://docs.microsoft.com/en-us/dotnet/api/system.iequatable-1.equals)
- [IRefCounted](../api/Unity.Entities.IRefCounted.html)
- [Burst Documentation](https://docs.unity3d.com/Packages/com.unity.burst@latest/index.html)
