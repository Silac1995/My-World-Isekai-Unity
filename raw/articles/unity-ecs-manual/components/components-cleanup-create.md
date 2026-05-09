---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/components-cleanup-create.html
fetched: 2026-05-05
section: components
---

# Create a Cleanup Component

To create a cleanup component, define a struct that inherits from `ICleanupComponentData`. These components must be added at runtime, as they cannot be baked.

## Basic Structure

Here's an example of an empty cleanup component:

```csharp
public struct ExampleCleanupComponent : ICleanupComponentData
{
}
```

**Note:** While empty cleanup components often work well, you can add properties to store information needed for cleanup operations.

## Perform Cleanup

Cleanup components help manage entities requiring cleanup upon destruction. "Unity prevents you from destroying an entity that contains a cleanup component."

When attempting to destroy an entity with a cleanup component, Unity removes all non-cleanup components instead. The entity persists until all cleanup components are removed.

### Implementation Steps

1. Create a tag component and add it to the target archetype
2. Create a cleanup component containing necessary cleanup information
3. Create a system that:
   - Identifies newly created entities with the tag component but without the cleanup component
   - Adds the cleanup component to these entities
4. Create a system handling cleanup component removal:
   - **In OnUpdate:** Process entities provisionally destroyed at runtime (those with cleanup component but without tag component)
   - **In OnDestroy:** Process all entities with cleanup components during shutdown
   - Perform appropriate cleanup work
   - Remove cleanup components

## Example Systems

### System Adding Cleanup Component

```csharp
public partial struct AddCleanupSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        var ecb = new EntityCommandBuffer(state.WorldUpdateAllocator);

        foreach (var (tag, entity) in SystemAPI.Query<ExampleTagComponent>().WithEntityAccess())
        {
            // Add the cleanup component to all entities with the tag component.
            ecb.AddComponent<ExampleCleanupComponent>(entity);
        }
        ecb.Playback(state.EntityManager);
        ecb.Dispose();

        state.Enabled = false;
    }
}
```

### System Destroying Entities

```csharp
[UpdateAfter(typeof(AddCleanupSystem))]
public partial struct DestructionSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        var ecb = new EntityCommandBuffer(state.WorldUpdateAllocator);

        foreach (var (tag, entity) in SystemAPI.Query<ExampleTagComponent>().WithAll<ExampleCleanupComponent>().WithEntityAccess())
        {
            // Destroy the Entity, which means all components, except for the cleanup component, gets removed.
            ecb.DestroyEntity(entity);
        }
        ecb.Playback(state.EntityManager);
        ecb.Dispose();

        state.Enabled = false;
    }
}
```

### System Removing Cleanup Component

```csharp
[UpdateAfter(typeof(DestructionSystem))]
public partial struct CleanupSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        var ecb = new EntityCommandBuffer(state.WorldUpdateAllocator);

        foreach (var (cleanup, entity) in SystemAPI.Query<ExampleCleanupComponent>().WithNone<ExampleTagComponent>().WithEntityAccess())
        {
            // Perform cleanup ...

            // Remove Cleanup Component. This triggers the destruction of the Entity.
            ecb.RemoveComponent<ExampleCleanupComponent>(entity);
        }
        ecb.Playback(state.EntityManager);
        ecb.Dispose();

        state.Enabled = false;
    }
}
```

## Additional Resources

- [Tag components](components-tag.html)

---

## Outgoing Links

- http://docs.unity3d.com/ - docs.unity3d.com
- ../index.html - Home
- ../logo.svg - Unity Logo
- components-tag.html - Tag components
- concepts-archetypes.html - Archetypes
- https://docs.unity3d.com/Manual/TermsOfUse.html - Trademarks and terms of use
- https://unity.com/legal - Legal
- https://unity.com/legal/privacy-policy - Privacy Policy
- https://unity.com/legal/cookie-policy - Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information - Do Not Sell or Share My Personal Information
