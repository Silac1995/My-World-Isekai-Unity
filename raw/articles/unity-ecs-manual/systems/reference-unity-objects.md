---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/reference-unity-objects.html
fetched: 2026-05-05
section: systems
---

# Reference Unity Objects in Your Code

## Overview

To store references to `UnityEngine.Object` types in your code, you can use the `UnityObjectRef` struct inside an unmanaged `IComponentData` component. This allows you to access the original object and use it in systems.

A practical example involves storing a reference to a `GameObject` with an `Animator` component that you want to instantiate and access in a system.

## Using UnityObjectRef

### Defining the Component with Baker

Define an `IComponentData` with a `UnityObjectRef` field and use a baker to store a reference during conversion:

```csharp
public class AnimatorAuthoring : MonoBehaviour
{
    public GameObject AnimatorPrefab;

    public class AnimatorBaker : Baker<AnimatorAuthoring>
    {
        public override void Bake(AnimatorAuthoring authoring)
        {
            var e = GetEntity(TransformUsageFlags.Renderable);
            AddComponent(e, new AnimatorRefComponent
            {
                AnimatorAsGO = authoring.AnimatorPrefab
            });
        }
    }
}

public struct AnimatorRefComponent : IComponentData
{
    public UnityObjectRef<GameObject> AnimatorAsGO;
}
```

### Instantiating the Prefab

Use `SystemAPI` to access and instantiate the prefab:

```csharp
public partial struct SpawnAnimatedCubeSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        var entities = SystemAPI.QueryBuilder()
            .WithAll<AnimatorRefComponent>()
            .WithNone<Animator>()
            .Build()
            .ToEntityArray(state.WorldUpdateAllocator);

        foreach (var entity in entities)
        {
            var animRef = SystemAPI.GetComponent<AnimatorRefComponent>(entity);
            var rotatingCube = (GameObject)Object.Instantiate(animRef.AnimatorAsGO);
            state.EntityManager.AddComponentObject(entity, 
                rotatingCube.GetComponent<Animator>());
        }
    }
}
```

### Modifying Animator Properties

Access and modify the `Animator` from a separate system to adjust animation speed dynamically:

```csharp
public partial struct ChangeRotationAnimationSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        foreach (var anim in SystemAPI.Query<SystemAPI.ManagedAPI.UnityEngineComponent<Animator>>())
        {
            var sineSpeed = 1f + Mathf.Sin(Time.time);
            anim.Value.speed = sineSpeed;
        }
    }
}
```

## Referencing the Same Asset in MonoBehaviour and IComponentData Code

During a Player build, Unity collects all `UntypedWeakReferenceId` values from each subscene, including `WeakObjectReference<T>`, `WeakObjectSceneReference` properties, and any Unity objects directly referenced from entity data (including `UnityObjectRef<T>`). These are added to a special `ScriptableObject` that has an `UntypedWeakReferenceId`.

Unity builds these references into `ContentArchive` instances and optimizes them as follows:

- Objects used together are placed in the same archive for maximum loading efficiency
- Shared objects are placed in separate archives to prevent duplication
- Objects with direct references from normal scenes are built directly into Player data, separate from archive data

When an object is referenced by both normal and entity scenes, it is duplicated in both sets with its own InstanceID at runtime. A normal scene can contain a `WeakObjectReference<T>` and use this reference to load from archive data at runtime, provided the reference is also included in an entity scene. This approach includes only one copy of the asset in the build.

## Additional Resources

- [`UnityObjectRef` API Reference](xref:Unity.Entities.UnityObjectRef-1)
- [Unmanaged Components](components-unmanaged.html)
- [Convert Data with Baking](baking.html)

---

## Outgoing Hyperlinks

- https://docs.unity3d.com/ - docs.unity3d.com
- https://docs.unity3d.com/Manual/TermsOfUse.html - Trademarks and Terms of Use
- https://unity.com/legal - Legal
- https://unity.com/legal/privacy-policy - Privacy Policy
- https://unity.com/legal/cookie-policy - Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information - Do Not Sell or Share My Personal Information
