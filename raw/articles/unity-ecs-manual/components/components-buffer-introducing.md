---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/components-buffer-introducing.html
fetched: 2026-05-05
section: components
---

# Dynamic Buffer Components Introduction

## Overview

A dynamic buffer component functions as a resizable array of unmanaged structs, enabling you to store array data for entities—such as waypoint positions for navigation paths.

## Core Characteristics

Each buffer maintains three key elements alongside its data:

- **Length**: The number of elements currently in the buffer, starting at zero and incrementing as values are appended
- **Capacity**: The total storage available, initially matching the internal buffer capacity and adjustable through resizing
- **Internal Pointer**: Indicates the buffer data location. Initially `null` (data stored in chunk with entity); updates if Unity relocates data outside the chunk

## Dynamic Buffer Capacity

By default, initial capacity accommodates elements fitting within 128 bytes. You can customize capacity using the `InternalBufferCapacity` attribute.

For additional details, see [Set the capacity of a dynamic buffer](components-buffer-set-capacity.html).

## Structural Changes

Structural modifications can destroy or relocate arrays referenced by dynamic buffers, invalidating buffer handles. You must reacquire buffers following any structural changes:

```csharp
public partial struct DynamicBufferExampleSystem : ISystem
{
    EntityQuery m_BufferEntityQuery;
    
    public void OnCreate(ref SystemState state)
    {
        m_BufferEntityQuery = SystemAPI.QueryBuilder().WithAll<MyElement>().Build();
    }

    public void OnUpdate(ref SystemState state)
    {
        // Acquires entities with the desired buffer.
        var entities = m_BufferEntityQuery.ToEntityArray(Allocator.Persistent);
        if(entities.Length == 0) return;

        // Acquires a dynamic buffer of type MyElement from the first entity in the array.
        DynamicBuffer<MyElement> myBuff = state.EntityManager.GetBuffer<MyElement>(entities[0]);

        // This structural change invalidates the previously acquired DynamicBuffer.
        state.EntityManager.CreateEntity();

        // A safety check will throw an exception on any read or write actions on the buffer.
        var x = myBuff[0];

        // Reacquires the dynamic buffer after the above structural changes.
        myBuff = state.EntityManager.GetBuffer<MyElement>(entities[0]);
        var y = myBuff[0];
    }

}
```

## Comparison to Native Containers

Dynamic buffers lack the job scheduling restrictions inherent to native containers in components, making them preferable where possible. They can be stored inline within chunks, reducing memory bandwidth usage.

Use dynamic buffers when multiple entities require collections. For single-entity scenarios, consider singleton components with native containers.

## Related Resources

- [Create a dynamic buffer component](components-buffer-create.html)
- [Set the capacity of a dynamic buffer](components-buffer-set-capacity.html)

---

## Outgoing Links

- https://docs.unity3d.com/ - docs.unity3d.com
- ../index.html - Documentation home
- ../logo.svg - Unity logo
- components-buffer-set-capacity.html - Set the capacity of a dynamic buffer
- ../api/Unity.Entities.InternalBufferCapacityAttribute.html - InternalBufferCapacityAttribute API
- components-buffer-create.html - Create a dynamic buffer component type
- concepts-structural-changes.html - Structural changes
- components-nativecontainers.html - Native containers in components
- concepts-archetypes.html#archetype-chunks - Archetype chunks
- components-singleton.html - Singleton components
- https://docs.unity3d.com/Manual/TermsOfUse.html - Trademarks and terms of use
- https://unity.com/legal - Legal
- https://unity.com/legal/privacy-policy - Privacy Policy
- https://unity.com/legal/cookie-policy - Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information - Do Not Sell or Share My Personal Information
