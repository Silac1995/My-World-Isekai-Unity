---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/components-add-to-entity.html
fetched: 2026-05-05
section: components
---

# Add Components to an Entity

## Overview

To add components to an entity, utilize the `EntityManager` for the relevant world containing that entity. You can add components to a single entity or to multiple entities simultaneously.

**Important Note:** Adding a component to an entity constitutes a structural change, causing the entity to relocate to a different chunk. Because of this, you cannot directly add components to an entity from a job. Instead, record your intention using an `EntityCommandBuffer` for later execution.

## Add a Component to a Single Entity

The following example demonstrates creating a new entity and attaching a component from the main thread:

```csharp
public partial struct AddComponentToSingleEntitySystemExample : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        var entity = state.EntityManager.CreateEntity();
        state.EntityManager.AddComponent<Rotation>(entity);
    }

}
```

## Add a Component to Multiple Entities

This example retrieves all entities containing a specific component and adds another component to them from the main thread:

```csharp
struct ComponentA : IComponentData {}
struct ComponentB : IComponentData {}
public partial struct AddComponentToMultipleEntitiesSystemExample : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        var query = state.GetEntityQuery(typeof(ComponentA));
        state.EntityManager.AddComponent<ComponentB>(query);
    }
}
```

## Additional Resources

- [`EntityManager.AddComponent`](../api/Unity.Entities.EntityManager.AddComponent.html)

---

## Outgoing Links

- https://docs.unity3d.com/Manual/TermsOfUse.html - Trademarks and terms of use
- https://unity.com/legal - Legal
- https://unity.com/legal/privacy-policy - Privacy Policy
- https://unity.com/legal/cookie-policy - Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information - Do Not Sell or Share My Personal Information
