---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/linked-entity-group.html
fetched: 2026-05-05
section: systems
---

# Linked Entity Groups

A `LinkedEntityGroup` instance is a Dynamic Buffer that has special semantics:

- **Instantiating**: `EntityManager.Instantiate` instantiates all the entities which are part of the group.
- **Destroying**: `EntityManager.DestroyEntity` destroys all the entities which are part of the group.
- **Enabling or disabling**: `EntityManager.SetEnabled` adds or removes the `Disabled` tag component on all the entities which are part of the group.

The first element of an entity's `LinkedEntityGroup` buffer must always be the entity itself.

Prefabs that the baking process creates always have a `LinkedEntityGroup` at the root. Instances created from those prefabs also have one.

## Working with linked entity groups

`LinkedEntityGroup` and transform hierarchy are separate concepts. For example, adding children under a parent with a `LinkedEntityGroup` doesn't automatically add them to the `LinkedEntityGroup`. Similarly, removing entities from a `LinkedEntityGroup` doesn't remove them from the parent.

Unity doesn't process `LinkedEntityGroup` instances recursively. If an entity which is part of a `LinkedEntityGroup` A has a `LinkedEntityGroup` B of its own, processing `LinkedEntityGroup` A doesn't include the contents of `LinkedEntityGroup` B. To prevent confusion, avoid nesting groups.

### Destroying entities

`LinkedEntityGroup` must only contain valid entities. When entities which are part of a `LinkedEntityGroup` are individually destroyed, they also have to be explicitly removed from the group.

When destroying entities with a query, either all the entities within a `LinkedEntityGroup` need to match the query, or that none of them match. Therefore, the contents of a `LinkedEntityGroup` can't partially match the query.

This is relevant if you use entity scenes, because when Unity unloads an entity scene, it uses the `SceneTag` shared component value of the entity scene to identify the entities that it needs to destroy. When you add entities to `LinkedEntityGroups` which are part of a scene, make sure that those entities have the proper `SceneTag`.

### Add an entity to a linked entity group

The following is an example of how to add an entity to a linked entity group in a system:

```csharp
partial struct SomeSystem:ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        //Get the targeted entity for which you would like to modify the LinkedEntityGroup
        var q = SystemAPI.QueryBuilder().WithAll<SomeComponent>().WithAll<LinkedEntityGroup>().Build().ToEntityArray(Allocator.Temp);

        //Create the child entity and add the SceneTag
        var child = state.EntityManager.CreateEntity();
        var sceneTag = state.EntityManager.GetSharedComponent<SceneTag>(q[0]);
        state.EntityManager.AddSharedComponent<SceneTag>(child, sceneTag);

        //If needed add the new entity as a child in the transform hierarchy 
        state.EntityManager.AddComponentData(child, new Parent { Value = q[0] });

        //Get the LinkedEntityGroup and add the newly created child entity
        var leg = SystemAPI.GetBuffer<LinkedEntityGroup>(q[0]);
        leg.Add(child);

        state.Enabled = false;
    }
}
```

## Additional resources

- [Dynamic buffer components](components-buffer.html)
- [Entity concepts](concepts-entities.html)
- [System concepts](concepts-systems.html)

---

## Outgoing Links

- https://docs.unity3d.com/Manual/TermsOfUse.html - Trademarks and terms of use
- https://unity.com/legal - Legal
- https://unity.com/legal/privacy-policy - Privacy Policy
- https://unity.com/legal/cookie-policy - Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information - Do Not Sell or Share My Personal Information
- https://docs.unity3d.com/ - docs.unity3d.com
- ../api/Unity.Entities.LinkedEntityGroup.html - LinkedEntityGroup API
- ../api/Unity.Entities.EntityManager.Instantiate.html - EntityManager.Instantiate API
- ../api/Unity.Entities.EntityManager.DestroyEntity.html - EntityManager.DestroyEntity API
- ../api/Unity.Entities.EntityManager.SetEnabled.html - EntityManager.SetEnabled API
- components-buffer.html - Dynamic buffer components
- baking-prefabs.html - Prefabs that the baking process creates
- transforms-concepts.html#transform-hierarchy - Transform hierarchy
- systems-entityquery-intro.html - Systems and entity queries
