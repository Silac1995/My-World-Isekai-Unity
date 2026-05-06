---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/transforms-using.html
fetched: 2026-05-05
section: systems
---

# Use Transforms

To use transforms in your project, leverage the `Unity.Transforms` namespace to control the position, rotation, and scale of any entity.

## LocalTransform Component

The `LocalTransform` struct represents the relative position, rotation, and scale of an entity. When a parent exists, the transform is relative to that parent; otherwise, it's relative to the world origin. This component is readable and writable.

```csharp
public struct LocalTransform : IComponentData
{
    public float3 Position;
    public float Scale;
    public quaternion Rotation;
}
```

## Using the API

The API provides no direct modification methods for `LocalTransform` - all methods return new values without modifying the original. To update a transform, use the assignment operator:

```csharp
myTransform = myTransform.RotateZ(someAngle);
```

You can also directly modify Position, Rotation, and Scale properties:

```csharp
myTransform.Position += math.up();
```

This is equivalent to:

```csharp
myTransform = myTransform.Translate(math.up());
```

Constructor methods simplify transform creation. To create a `LocalTransform` with a specified position and default rotation/scale:

```csharp
var myTransform = LocalTransform.FromPosition(1, 2, 3);
```

## Using a Hierarchy

While `LocalTransform` works independently, hierarchical entities require the `Parent` component:

```csharp
public struct Parent : IComponentData
{
    public Entity Value;
}
```

For hierarchies to function properly, `ParentSystem` must execute to establish parent-child relationships and manage child components.

### Performance Considerations

Mark entities that won't move with the `static` flag to improve performance and reduce memory usage. The transform system is optimized for numerous root-level hierarchies. Avoid creating large hierarchies beneath a single root, as hierarchy processing is distributed across jobs at the root level.

### Hierarchy Component Structure

The hierarchy components on an entity depend on its position within the transform hierarchy:

- **Root entities** have the `Child` component but not `Parent`
- **Leaf entities** have the `Parent` component but not `Child`
- **Interior entities** have both `Parent` and `Child` components
- **Non-hierarchical entities** have neither component

### Important Constraints

- The `Child` and `PreviousParent` components are exclusively managed by `ParentSystem` - never directly add, remove, or modify these
- After adding, removing, or modifying `Parent`, the hierarchy remains inconsistent until the next `ParentSystem` update
- The `LocalToWorld` component value isn't synchronized with `LocalTransform` during frames; it's only guaranteed valid immediately after `LocalToWorldSystem` updates

---

## Outgoing Links

- [Unity.Transforms API](../api/Unity.Transforms.html)
- [Parent API](../api/Unity.Transforms.Parent.html)
- [ParentSystem API](../api/Unity.Transforms.ParentSystem.html)
- [Trademarks and Terms of Use](https://docs.unity3d.com/Manual/TermsOfUse.html)
- [Legal](https://unity.com/legal)
- [Privacy Policy](https://unity.com/legal/privacy-policy)
- [Cookie Policy](https://unity.com/legal/cookie-policy)
- [Do Not Sell or Share My Personal Information](https://unity.com/legal/do-not-sell-my-personal-information)
