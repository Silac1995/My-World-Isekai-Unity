---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/components-chunk-use.html
fetched: 2026-05-05
section: components
---

# Use Chunk Components

Chunk components utilize distinct APIs for adding, removing, getting, and setting values compared to standard component types. Rather than using `EntityManager.AddComponent`, you'll use `EntityManager.AddChunkComponentData` to add chunk components to an entity.

## Basic Operations

The following example demonstrates adding, setting, and getting a chunk component, assuming an `ExampleChunkComponent` exists along with a regular `ExampleComponent`:

```csharp
private void ChunkComponentExample(Entity e)
{
    // Adds ExampleChunkComponent to the passed in entity's chunk.
    EntityManager.AddChunkComponentData<ExampleChunkComponent>(e);

    // Finds all chunks with an ExampleComponent and an ExampleChunkComponent.
    // To distinguish chunk components from a regular IComponentData, You must
    // specify the chunk component with ComponentType.ChunkComponent.
    EntityQuery query = GetEntityQuery(typeof(ExampleComponent), 
        ComponentType.ChunkComponent<ExampleChunkComponent>());
    NativeArray<ArchetypeChunk> chunks = query.ToArchetypeChunkArray(Allocator.Temp);

    // Sets the ExampleChunkComponent value of the first chunk.
    EntityManager.SetChunkComponentData<ExampleChunkComponent>(chunks[0], 
        new ExampleChunkComponent { Value = 6 });

    // Gets the ExampleChunkComponent value of the first chunk.
    ExampleChunkComponent exampleChunkComponent = 
        EntityManager.GetChunkComponentData<ExampleChunkComponent>(chunks[0]);
    Debug.Log(exampleChunkComponent.Value)    // 6
}
```

> "If you only want to read from a chunk component and not write to it, use `ComponentType.ChunkComponentReadOnly` when you define the query."

**Important:** Although chunk components belong to chunks, adding or removing them on an entity changes its archetype and causes a structural change. New chunk components initialize to default type values.

## Accessing Via Entity

You can also get and set chunk components through any of the chunk's entities:

```csharp
private void ChunkComponentExample(Entity e)
{
    var entityChunk = EntityManager.GetChunk(e);
    // Sets the ExampleChunkComponent value of the entity's chunk.
    EntityManager.SetChunkComponentData<ExampleChunkComponent>(entityChunk,
        new ExampleChunkComponent { Value = 6 });

    // Gets the ExampleChunkComponent value of the entity's chunk.
    ExampleChunkComponent exampleChunkComponent = 
        EntityManager.GetChunkComponentData<ExampleChunkComponent>(e);
    Debug.Log(exampleChunkComponent.Value)    // 6
}
```

## Use Chunk Components in Jobs

Since jobs cannot use `EntityManager`, you must use `ComponentTypeHandle` to access chunk components:

```csharp
struct MyJob : IJobChunk
{
    public ComponentTypeHandle<ExampleChunkComponent> ExampleChunkComponentHandle;

    public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
    {
        // Get the chunk's ExampleChunkComponent.
        ExampleChunkComponent exampleChunkComponent = 
            chunk.GetChunkComponentData(ExampleChunkComponentHandle);

        // Set the chunk's ExampleChunkComponent. 
        chunk.SetChunkComponentData(ExampleChunkComponentHandle, 
            new ExampleChunkComponent { Value = 7 });
    }
}
```

---

## Outgoing Links

- https://docs.unity3d.com/Manual/TermsOfUse.html - Trademarks and terms of use
- https://unity.com/legal - Legal
- https://unity.com/legal/privacy-policy - Privacy Policy
- https://unity.com/legal/cookie-policy - Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information - Do Not Sell or Share My Personal Information
