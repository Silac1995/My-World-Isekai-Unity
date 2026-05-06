---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/components-buffer-get-all-in-chunk.html
fetched: 2026-05-05
section: components
---

# Access Dynamic Buffers in a Chunk

To retrieve all dynamic buffers contained within a chunk, utilize the [`ArchetypeChunk.GetBufferAccessor`](../api/Unity.Entities.ArchetypeChunk.GetBufferAccessor.html) method. This method accepts a [`BufferTypeHandle<T>`](../api/Unity.Entities.BufferTypeHandle-1.html) parameter and provides a [`BufferAccessor<T>`](../api/Unity.Entities.BufferAccessor-1.html) in return. By indexing into the `BufferAccessor<T>`, you can access the chunk's buffers of the specified type `T`.

## Code Example

The following demonstrates how to iterate through every dynamic buffer of a particular type within a chunk:

```csharp
[InternalBufferCapacity(16)]
public struct ExampleBufferComponent : IBufferElementData
{
    public int Value;
}

public partial class ExampleSystem : SystemBase
{
    protected override void OnUpdate()
    {
        var query = new EntityQueryBuilder(Allocator.Temp)
            .WithAllRW<ExampleBufferComponent>()
            .Build(EntityManager);
        NativeArray<ArchetypeChunk> chunks = query.ToArchetypeChunkArray(Allocator.Temp);
        for (int i = 0; i < chunks.Length; i++)
        {
            UpdateChunk(chunks[i]);
        }
    }

    private void UpdateChunk(ArchetypeChunk chunk)
    {
        // Get a BufferTypeHandle representing dynamic buffer type ExampleBufferComponent from SystemBase.
        BufferTypeHandle<ExampleBufferComponent> myElementHandle = GetBufferTypeHandle<ExampleBufferComponent>();
        // Get a BufferAccessor from the chunk.
        BufferAccessor<ExampleBufferComponent> buffers = chunk.GetBufferAccessorRW(ref myElementHandle);
        // Iterate through all ExampleBufferComponent buffers of each entity in the chunk.
        for (int i = 0, chunkEntityCount = chunk.Count; i < chunkEntityCount; i++)
        {
            DynamicBuffer<ExampleBufferComponent> buffer = buffers[i];
            // Iterate through all elements of the buffer.
            for (int j = 0; j < buffer.Length; j++)
            {
                // ...
            }
        }
    }
}
```

## Additional Resources

- [Modify dynamic buffers with an `EntityCommandBuffer`](components-buffer-command-buffer.html)

---

## Outgoing Links

- [Unity.Entities.ArchetypeChunk.GetBufferAccessor documentation](../api/Unity.Entities.ArchetypeChunk.GetBufferAccessor.html)
- [Unity.Entities.BufferTypeHandle-1 documentation](../api/Unity.Entities.BufferTypeHandle-1.html)
- [Unity.Entities.BufferAccessor-1 documentation](../api/Unity.Entities.BufferAccessor-1.html)
- [Modify dynamic buffers with an EntityCommandBuffer](components-buffer-command-buffer.html)
- [Unity Legal](https://unity.com/legal)
- [Privacy Policy](https://unity.com/legal/privacy-policy)
- [Cookie Policy](https://unity.com/legal/cookie-policy)
- [Do Not Sell or Share My Personal Information](https://unity.com/legal/do-not-sell-my-personal-information)
