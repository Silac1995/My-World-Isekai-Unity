---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/components-buffer-set-capacity.html
fetched: 2026-05-05
section: components
---

# Set the Capacity of a Dynamic Buffer

## Overview

The initial capacity of a dynamic buffer is determined by the type it stores. By default, capacity equals the number of elements fitting within 128 bytes, as defined by [`DefaultBufferCapacityNumerator`](../api/Unity.Entities.TypeManager.DefaultBufferCapacityNumerator.html). To customize this, apply the [`InternalBufferCapacity`](../api/Unity.Entities.InternalBufferCapacityAttribute.html) attribute.

## Capacity Overview

Unity initially stores dynamic buffer data directly in the [archetype chunk](concepts-archetypes.html#archetype-chunks) of an entity's component. When buffer length stays within initial capacity, data remains inline in the chunk.

Once buffer length exceeds internal capacity, Unity allocates memory outside the chunk and copies data there. Even if the buffer later shrinks below capacity, the data remains external. This creates performance concerns:

- **Reallocation overhead**: Adding elements beyond capacity triggers memory allocation and data copying
- **Cache misses**: External buffer data causes poor cache locality
- **Chunk fragmentation**: Unused inline space persists in chunks for the buffer's lifetime

The best practice balances two considerations: keep data inline when possible by setting capacity appropriately to your entities' needs, but avoid excessive capacity that wastes space. For highly variable buffer sizes, set `InternalBufferCapacity` to `0` to store data externally from the start.

## Setting Internal Buffer Capacity

All DynamicBuffers default to a capacity calculated using [`TypeManager.DefaultBufferCapacityNumerator`](../api/Unity.Entities.TypeManager.DefaultBufferCapacityNumerator.html), which defaults to 128 bytes (for example, 32 integers).

When you know expected element counts, use the [`[InternalBufferCapacity]`](../api/Unity.Entities.InternalBufferCapacityAttribute.html) attribute during buffer declaration:

```csharp
// My buffer can contain up to 42 elements inline in the chunk
// If I add any more then ECS will reallocate the buffer onto a heap  
[InternalBufferCapacity(42)]  
public struct MyBufferElement : IBufferElementData  
{  
    public int Value;  
}  
```

This prevents reallocation as long as the buffer doesn't exceed the specified capacity. Consider chunk fragmentation impact when choosing capacity values.

## Dynamic Capacity Control

When compile-time capacity requirements are unknown, use dynamic control methods. This proves useful when adding elements individually, since default behavior reallocates on each `Add()` that increases capacity.

Use [`DynamicBuffer.EnsureCapacity`](../api/Unity.Entities.DynamicBuffer-1.EnsureCapacity.html) to forcibly reallocate the buffer with sufficient memory for the specified capacity, eliminating per-element reallocation overhead. If buffers consume excess memory from capacity padding no longer needed, call [`DynamicBuffer.TrimExcess`](../api/Unity.Entities.DynamicBuffer-1.TrimExcess.html) to reduce size.

## Alternative Array Data Storage

For projects where dynamic buffer capacity presents challenges:

- **[Blob assets](blob-assets-intro.html)**: Store tightly packed read-only structured data including arrays; multiple entities can share a single blob asset; thread-safe simultaneous access
- **[Native containers](https://docs.unity3d.com/6000.4/Documentation/Manual/job-system-native-container.html)**: Use with unmanaged [`IComponentData`](../api/Unity.Entities.IComponentData.html) components

## Additional Resources

- [`[InternalBufferCapacity]` API reference](../api/Unity.Entities.InternalBufferCapacityAttribute.html)
- [Dynamic buffer components introduction](components-buffer-introducing.html)
- [Create a dynamic buffer component](components-buffer-create.html)
- [Access dynamic buffers in a chunk](components-buffer-get-all-in-chunk.html)
- [Manage chunk allocations](performance-chunk-allocations.html)
