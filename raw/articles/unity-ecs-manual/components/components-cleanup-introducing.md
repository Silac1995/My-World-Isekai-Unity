---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/components-cleanup-introducing.html
fetched: 2026-05-05
section: components
---

# Cleanup Components Introduction

Cleanup components function as specialized tags within Unity's ECS system. When an entity containing a cleanup component is destroyed, the system behaves differently than with regular components: "Unity removes all non-cleanup components instead. The entity still exists until you remove all cleanup components from it."

## Component Lifecycle

The following example demonstrates how cleanup components affect entity destruction:

```csharp
// Creates an entity that contains a cleanup component.
Entity e = EntityManager.CreateEntity(
    typeof(Translation), typeof(Rotation), typeof(ExampleCleanup));

// Attempts to destroy the entity but, because the entity has a cleanup component, 
// Unity doesn't actually destroy the entity. Instead, Unity just removes the 
// Translation and Rotation components. 
EntityManager.DestroyEntity(e);

// The entity still exists so this demonstrates that you can still use the 
// entity normally.
EntityManager.AddComponent<Translation>(e);

// Removes all the remaining components from the entity.
// Removing the final cleanup component (ExampleCleanup) automatically 
// destroys the entity.
EntityManager.RemoveComponent(e, new ComponentTypeSet(
    typeof(ExampleCleanup), typeof(Translation)));

// Demonstrates that the entity no longer exists.
bool entityExists = EntityManager.Exists(e);
```

## Important Constraints

Cleanup components are unmanaged and share the same restrictions as unmanaged components. Additionally:

- Cleanup components are excluded when entities transfer between Worlds
- Cleanup components added during baking won't be serialized
- Cleanup components on prefab entities won't appear on instantiated prefab instances

---

## Outgoing Links

- [Unmanaged components](components-unmanaged.html)
- [Use cleanup components](components-cleanup-create.html#perform-cleanup)
- [docs.unity3d.com](http://docs.unity3d.com/)
