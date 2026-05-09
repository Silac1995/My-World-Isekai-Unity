---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/components-read-and-write.html
fetched: 2026-05-05
section: components
---

# Read and write component values of entities

After adding Components to entities, systems can access, read from, and write to Component values. Several methods are available depending on your use case.

## Access a single component

For reading or writing a single component of one entity at a time on the main thread, use the `EntityManager` to read or write a component value of an individual entity. The `EntityManager` maintains a lookup table for quick access to each entity's chunk and index.

```csharp
public partial struct GetComponentOnSingleEntitySystemExample : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        var entity = state.EntityManager.CreateEntity();
        state.EntityManager.AddComponent<Rotation>(entity);

        // Get the Rotation component
        var rotationComponent = state.EntityManager.GetComponentData<Rotation>(entity);
    }
}
```

## Access multiple components

For most scenarios, you'll need to read or write components across all entities in a chunk or set of chunks:

- An `ArchetypeChunk` directly reads and writes the component arrays of a chunk.
- An `EntityQuery` efficiently retrieves the set of chunks matching the query.
- An `IJobEntity` iterates across components in a query using jobs.

## Deferring component value changes

To defer component value changes for later execution, use an `EntityCommandBuffer` which records your intention to write (but not read) component values. These changes execute when you later play back the `EntityCommandBuffer` on the main thread.

---

## Outgoing Links

- http://docs.unity3d.com/ - docs.unity3d.com
- https://docs.unity3d.com/Manual/TermsOfUse.html - Trademarks and terms of use
- https://unity.com/legal - Legal
- https://unity.com/legal/privacy-policy - Privacy Policy
- https://unity.com/legal/cookie-policy - Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information - Do Not Sell or Share My Personal Information
