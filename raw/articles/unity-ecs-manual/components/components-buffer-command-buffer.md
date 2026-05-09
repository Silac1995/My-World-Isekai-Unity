---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/components-buffer-command-buffer.html
fetched: 2026-05-05
section: components
---

# Modify dynamic buffers with an entity command buffer

An `EntityCommandBuffer` (ECB) records commands to add, remove, or set buffer components for entities. Dynamic buffer-specific APIs differ from regular component APIs.

An ECB can only record commands for future execution, limiting dynamic buffer manipulation to these methods:

## SetBuffer<T>

Returns a `DynamicBuffer<T>` that the recording thread can populate. At playback, buffer contents overwrite existing data. This method doesn't fail if the target entity already contains the buffer component. When multiple threads record `SetBuffer` commands on the same entity, only the last command's contents (by `sortKey` order) remain visible. `SetBuffer` functions identically to `AddBuffer<T>`, except `AddBuffer` adds the buffer component first if absent.

## AppendToBuffer<T>

Appends a single buffer element to an existing buffer component while preserving existing contents. Multiple threads can safely append to the same buffer, with `sortKey` determining element order. This method fails at playback if the target entity lacks the buffer component type `T`. Best practice involves preceding each `AppendToBuffer` command with `AddComponent<T>` to ensure the buffer component exists.

## AddComponent<T> and RemoveComponent<T>

These methods safely work with dynamic buffers when `T` is `IBufferElementData`. They add empty buffers or remove existing ones without errors from multiple threads or duplicate/non-existent operations.

## Code Example

```csharp
private void Example(Entity e, Entity otherEntity)
{
    EntityCommandBuffer ecb = new(Allocator.TempJob);

    // Record a command to remove the MyElement dynamic buffer from an entity.
    ecb.RemoveComponent<MyElement>(e);

    // Record a command to add a MyElement dynamic buffer to an existing entity.
    // This doesn't fail if the target entity already contains the buffer component.
    // The data of the returned DynamicBuffer is stored in the EntityCommandBuffer,
    // so changes to the returned buffer are also recorded changes.
    DynamicBuffer<MyElement> myBuff = ecb.AddBuffer<MyElement>(e);

    // After playback, the entity will have a MyElement buffer with
    // Length 20 and these recorded values.
    myBuff.Length = 20;
    myBuff[0] = new MyElement { Value = 5 };
    myBuff[3] = new MyElement { Value = -9 };

    // SetBuffer is like AddBuffer, but safety checks will throw an exception at playback if
    // the entity doesn't already have a MyElement buffer.
    DynamicBuffer<MyElement> otherBuf = ecb.SetBuffer<MyElement>(otherEntity);

    // Records a MyElement value to append to the buffer. Throws an exception at
    // playback if the entity doesn't already have a MyElement buffer.
    // ecb.AddBuffer<MyElement>(otherEntity) is a safe way to ensure a buffer
    // exists before appending to it.
    ecb.AppendToBuffer(otherEntity, new MyElement { Value = 12 });
}
```

Setting `Length`, `Capacity`, and content of the `DynamicBuffer` records changes into the `EntityCommandBuffer`. During playback, ECS applies these changes to the dynamic buffer.

## Related Resources

- https://docs.unity3d.com/Packages/com.unity.entities@6.4/api/Unity.Entities.EntityCommandBuffer.html - EntityCommandBuffer API reference
- https://docs.unity3d.com/Packages/com.unity.entities@6.4/api/Unity.Entities.EntityCommandBuffer.SetBuffer.html - SetBuffer<T> documentation
- https://docs.unity3d.com/Packages/com.unity.entities@6.4/api/Unity.Entities.EntityCommandBuffer.AddBuffer.html - AddBuffer<T> documentation
- https://docs.unity3d.com/Packages/com.unity.entities@6.4/api/Unity.Entities.EntityCommandBuffer.AppendToBuffer.html - AppendToBuffer<T> documentation
- https://docs.unity3d.com/Packages/com.unity.entities@6.4/api/Unity.Entities.EntityCommandBuffer.AddComponent.html - AddComponent<T> documentation
- https://docs.unity3d.com/Packages/com.unity.entities@6.4/api/Unity.Entities.EntityCommandBuffer.RemoveComponent.html - RemoveComponent<T> documentation
- https://docs.unity3d.com/Manual/TermsOfUse.html - Trademarks and terms of use
- https://unity.com/legal - Legal information
- https://unity.com/legal/privacy-policy - Privacy Policy
- https://unity.com/legal/cookie-policy - Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information - Do Not Sell or Share My Personal Information
