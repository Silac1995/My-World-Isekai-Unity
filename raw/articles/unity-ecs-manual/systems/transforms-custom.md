---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/transforms-custom.html
fetched: 2026-05-05
section: systems
---

# Custom Transforms | Unity Entities 6.4.0

You can customize the built-in transform system to address specific transform functionality needs of your project. This section explains how to create a custom transform system, using the 2D custom transform system as a concrete example.

## Write Groups

Write groups enable you to override the built-in transform system with your own transforms. The built-in transform system uses write groups internally, and you can configure them to make the system ignore entities you want to process with your custom transform system.

More precisely, write groups exclude specific entities from queries passed to jobs used by the built-in transform system. You can use write groups on certain components to exclude entities with those components from being processed by the built-in system's jobs. Those entities can instead be processed by your own transform systems.

## Create a Custom Transform System

The following steps outline how to create a custom transform system:

- Substitute the `LocalTransform` component
- Create an authoring component to receive your custom transforms
- Replace the `LocalToWorldSystem`

### Substitute the LocalTransform Component

The built-in transform system adds a `LocalTransform` component to each entity by default. It stores data representing an entity's position, rotation, and scale, along with various static helper methods.

To create your own custom transform system, substitute the `LocalTransform` component with your own.

1. Create a .cs file that defines a substitute for the built-in `LocalTransform` component. You can copy the built-in `LocalTransform.cs` file from the Entities package into your assets folder and edit the contents. Go to **Packages > Entities > Unity.Transforms** in your project, copy the `LocalTransform.cs` file, and rename it.

2. Change the properties and methods to suit your needs. See the following example of a custom `LocalTransform2D` component:

```csharp
// By including LocalTransform2D in the LocalToWorld write group, entities
// with LocalTransform2D are not processed by the standard transform system.
[WriteGroup(typeof(LocalToWorld))]
public struct LocalTransform2D : IComponentData
{
    public float2 Position;
    public float Scale;
    public float Rotation;

    public override string ToString()
    {
        return $"Position={Position.ToString()} Rotation={Rotation.ToString()} Scale={Scale.ToString(CultureInfo.InvariantCulture)}";
    }

    public float4x4 ToMatrix()
    {
        quaternion rotation = quaternion.RotateZ(math.radians(Rotation));
        return float4x4.TRS(new float3(Position.xy, 0f), rotation, Scale);
    }
}
```

The above example modifies the built-in `LocalTransform` in the following ways:

- Adds the `[WriteGroup(typeof(LocalToWorld))]` attribute
- Reduces the `Position` field from a `float3` to a `float2`, since entities only move along the XY plane in the 2D sample
- Reduces the `Rotation` field to a `float` representing degrees of rotation around the z-axis, rather than a quaternion representing 3D space rotation
- Removes all methods apart from `ToMatrix` and `ToString`. The `ToMatrix` method has been modified to work in 2D

**Note:** `LocalTransform2D` is in the global namespace. In the linked sample project it's in a sub-namespace to ensure it doesn't interfere with other samples in the same project. Both options work as long as all files of the custom transform system are within the same namespace.

### Create an Authoring Component

Each entity that your custom transform system needs to process must fulfill the following criteria:

- Has a custom replacement for the `LocalTransform` component, with a different name
- Has a `LocalToWorld` component
- If the entity has a parent entity, then it must have a `Parent` component that points to it

To meet this criteria, add an authoring component to each entity and use transform usage flags to prevent the entity from receiving any components from the built-in transform system:

```csharp
public class Transform2DAuthoring : MonoBehaviour
{
    class Baker : Baker<Transform2DAuthoring>
    {
        public override void Bake(Transform2DAuthoring authoring)
        {
            // Ensure that no standard transform components are added.
            var entity = GetEntity(TransformUsageFlags.ManualOverride);
            AddComponent(entity, new LocalTransform2D
            {
                Scale = 1
            });
            AddComponent(entity, new LocalToWorld
            {
                Value = float4x4.Scale(1)
            });

            var parentGO = authoring.transform.parent;
            if (parentGO != null)
            {
                AddComponent(entity, new Parent
                {
                    Value = GetEntity(parentGO, TransformUsageFlags.None)
                });
            }
        }
    }
}
```

The above example adds the custom `LocalTransform2D` component and the built-in `LocalToWorld` component to the authoring component. If applicable, it also adds a `Parent` component that points to the entity's parent entity.

### Replace the LocalToWorldSystem

The built-in `LocalToWorldSystem` computes the `LocalToWorld` matrices of root and child entities in two corresponding jobs: `ComputeRootLocalToWorldJob` and `ComputeChildLocalToWorldJob`. You need to replace this system with your own transform system.

1. Copy the built-in `LocalToWorldSystem.cs` file into your assets folder and edit the contents. Go to **Packages > Entities > Unity.Transforms** in your project, copy the `LocalToWorldSystem.cs` file, and rename it.

2. Replace all instances of the `LocalTransform` component with the name of your custom transform component (`LocalTransform2D` in the example).

3. Remove the `WithOptions(EntityQueryOptions.FilterWriteGroup);` lines from the queries. If you don't remove these lines, your system excludes the corresponding entities like the built-in transform system does.

**Note:** `LocalToWorldSystem` uses unsafe native code. To avoid errors, enable the Allow unsafe code property in your project. Go to **Edit** > **Project Settings** > **Player** > **Other Settings** and select **Allow unsafe code**.

## Additional Resources

- [Using transforms](https://docs.unity3d.com/6.4.0/Documentation/Manual/ecs-transforms-using.html)
- [Write groups overview](https://docs.unity3d.com/6.4.0/Documentation/Manual/systems-write-groups.html)
- [`TransformUsageFlags` API documentation](https://docs.unity3d.com/6.4.0/Documentation/ScriptReference/Unity.Entities.TransformUsageFlags.html)

---

## Outgoing Links

- https://github.com/Unity-Technologies/EntityComponentSystemSamples/tree/master/Dots101/Entities101/Assets/HelloCube/13.%20CustomTransforms - 2D custom transform system example
- https://docs.unity3d.com/6.4.0/Documentation/Manual/systems-write-groups.html - Write groups documentation
- https://docs.unity3d.com/6.4.0/Documentation/ScriptReference/Unity.Transforms.LocalTransform.html - LocalTransform API
- https://docs.unity3d.com/6.4.0/Documentation/ScriptReference/Unity.Transforms.LocalToWorldSystem.html - LocalToWorldSystem API
- https://docs.unity3d.com/6.4.0/Documentation/ScriptReference/Unity.Entities.TransformUsageFlags.html - TransformUsageFlags API
- https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/unsafe-code - Unsafe code in C#
- https://docs.unity3d.com/6000.4/Documentation/ScriptReference/Compilation.ScriptCompilerOptions.AllowUnsafeCode.html - Allow unsafe code setting
- https://docs.unity3d.com/6.4.0/Documentation/Manual/ecs-transforms-using.html - Using transforms
- https://docs.unity3d.com/Manual/TermsOfUse.html - Trademarks and terms of use
- https://unity.com/legal - Legal
- https://unity.com/legal/privacy-policy - Privacy Policy
- https://unity.com/legal/cookie-policy - Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information - Do Not Sell or Share My Personal Information
