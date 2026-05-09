---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/components-remove-from-entity.html
fetched: 2026-05-05
section: components
---

# Remove Components from an Entity

To remove components from an entity, use the [`EntityManager`](../api/Unity.Entities.EntityManager.html) for the [World](concepts-worlds.html) that the entity is in.

## Important

"Removing a component from an entity is a [structural change](concepts-structural-changes.html) which means that the entity moves to a different archetype chunk."

## From the Main Thread

You can directly remove components from an entity from the main thread. The following code sample gets every entity with an attached [`Rotation`](../api/Unity.Entities.TransformAuthoring.Rotation.html) component and then removes the `Rotation` component.

```csharp
public partial struct RemoveComponentSystemExample : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        var query = state.GetEntityQuery(typeof(Rotation));
        state.EntityManager.RemoveComponent<Rotation>(query);
    }
}
```

## From a Job

"Because removing a component from an entity is a structural change, you can't directly do it in a job. Instead, you must use an `EntityCommandBuffer` to record your intention to remove components later."

---

## Outgoing Links

- http://docs.unity3d.com/ - docs.unity3d.com
- ../api/Unity.Entities.EntityManager.html - EntityManager
- concepts-worlds.html - World
- concepts-structural-changes.html - structural change
- ../api/Unity.Entities.TransformAuthoring.Rotation.html - Rotation
- https://docs.unity3d.com/Manual/TermsOfUse.html - Trademarks and terms of use
- https://unity.com/legal - Legal
- https://unity.com/legal/privacy-policy - Privacy Policy
- https://unity.com/legal/cookie-policy - Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information - Do Not Sell or Share My Personal Information
