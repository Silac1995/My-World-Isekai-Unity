---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/components-chunk-create.html
fetched: 2026-05-05
section: components
---

# Create a Chunk Component

A chunk component's definition follows the same pattern as an unmanaged component. You create a regular struct implementing `IComponentData` to establish a chunk component. The key distinction between chunk components and unmanaged components lies in how you add them to an entity.

## Example Unmanaged Component

```csharp
public struct ExampleChunkComponent : IComponentData
{
    public int Value;
}
```

## Adding a Chunk Component

To use an unmanaged component as a chunk component, apply `EntityManager.AddChunkComponentData<YourChunkComponent>(Entity)` to add it to an entity.

---

## Outgoing Links

- http://docs.unity3d.com/ - docs.unity3d.com
- ../logo.svg - (logo image)
- ../index.html - (home page)
- components-unmanaged.html - Unmanaged components documentation
- ../api/Unity.Entities.EntityManager.AddChunkComponentData.html - EntityManager.AddChunkComponentData API reference
- https://docs.unity3d.com/Manual/TermsOfUse.html - Trademarks and terms of use
- https://unity.com/legal - Legal
- https://unity.com/legal/privacy-policy - Privacy Policy
- https://unity.com/legal/cookie-policy - Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information - Do Not Sell or Share My Personal Information
