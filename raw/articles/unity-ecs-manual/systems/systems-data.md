---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/systems-data.html
fetched: 2026-05-05
section: systems
---

# Organize System Data

When structuring system-level data, you should organize it as component-level data rather than as fields within the system type.

## Why Not Use Public System Data

Public data access in systems isn't best practice because it requires direct references to system instances, which creates several problems:

- "It creates dependencies between systems, which conflicts with data oriented approaches"
- "It can't guarantee thread or lifetime safety around accessing the system instance"
- "It can't guarantee thread or lifetime safety around accessing the system's data, even if the system still exists and is accessed in a thread-safe manner"

## Store System Data in Components

Rather than storing publicly accessible data as system fields, place it in components. The `World` namespace provides APIs like `GetExistingSystem<T>` and `Create` that return opaque `SystemHandle` objects instead of direct system access. This approach works for both managed `SystemBase` and unmanaged `ISystem` systems.

### Example: Object-Oriented Approach (Not Recommended)

```csharp
/// Object-oriented code example
public partial struct PlayerInputSystem : ISystem
{
    public float AxisX;
    public float AxisY;

    public void OnCreate(ref SystemState state) { }

    public void OnUpdate(ref SystemState state)
    {
        AxisX = [... read controller input];
        AxisY = [... read controller input];
    }

    public void OnDestroy(ref SystemState state) { }
}
```

### Example: Data-Oriented Approach (Recommended)

```csharp
public struct PlayerInputData : IComponentData
{
    public float AxisX;
    public float AxisY;
}

public partial struct PlayerInputSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.EntityManager.AddComponent<PlayerInputData>(state.SystemHandle);
    }

    public void OnUpdate(ref SystemState state)
    {
        SystemAPI.SetComponent(state.SystemHandle, new PlayerInputData {
            AxisX = [...read controller data],
            AxisY = [...read controller data]
        });
    }

    // Component data is automatically destroyed when the system is destroyed. 
    // If a Native Container existed in the component, however, OnDestroy could be used to
    // ensure memory is disposed.
    public void OnDestroy(ref SystemState state) { }  
}
```

This approach defines a data protocol separate from system functionality. The components can exist in a singleton entity or belong to a system-associated entity through `EntityManager.GetComponentData<T>(SystemHandle)` and similar methods. Use the latter when you want data lifetime tied to system lifetime.

With this technique, "you can access the system's data in the same way as any other entity component data. A reference or pointer to the system instance is no longer necessary."

## Using Singleton Entity Components

A singleton component has only one instance per world. The singleton APIs will error if you attempt to use them on a component type with more than one instance in that world.

Key differences from system-associated components:

- "Singletons aren't tied to the system's lifetime"
- "Singletons can only exist per system type, not per system instance"

For details, see the singleton components documentation.

## Direct Access APIs

The Entities package provides several APIs for direct system instance access in exceptional circumstances:

| Method name | ISystem | SystemBase |
|---|---|---|
| [`World.GetExistingSystemManaged<T>`](../api/Unity.Entities.World.GetExistingSystemManaged.html#Unity_Entities_World_GetExistingSystemManaged__1) | No | Yes |
| [`World.GetOrCreateSystemManaged<T>`](../api/Unity.Entities.World.GetOrCreateSystemManaged.html#Unity_Entities_World_GetOrCreateSystemManaged__1) | No | Yes |
| [`World.CreateSystemManaged<T>`](../api/Unity.Entities.World.CreateSystemManaged.html#Unity_Entities_World_CreateSystemManaged__1) | No | Yes |
| [`World.AddSystemManaged<T>`](../api/Unity.Entities.World.AddSystemManaged.html) | No | Yes |
| [`WorldUnmanaged.GetUnsafeSystemRef<T>`](../api/Unity.Entities.WorldUnmanaged.GetUnsafeSystemRef.html) | Yes | No |
| [`WorldUnmanaged.ResolveSystemStateRef<T>`](../api/Unity.Entities.WorldUnmanaged.ResolveSystemStateRef.html) | Yes | Yes |

## Additional Resources

- [Query data with an entity query](systems-entityquery.html)
- [Singleton entities](components-singleton.html)
- [Store immutable data](blob-assets-intro.html)

---

## Outgoing Links

- http://docs.unity3d.com/
- ../logo.svg
- ../index.html
- ../api/Unity.Entities.World.html
- ../api/Unity.Entities.World.GetExistingSystem.html
- ../api/Unity.Entities.SystemHandle.html
- ../api/Unity.Entities.SystemBase.html
- ../api/Unity.Entities.ISystem.html
- ../api/Unity.Entities.EntityManager.GetComponentData.html#Unity_Entities_EntityManager_GetComponentData__1_Unity_Entities_SystemHandle_
- components-singleton.html
- concepts-worlds.html
- systems-comparison.html
- ../api/Unity.Entities.World.GetExistingSystemManaged.html#Unity_Entities_World_GetExistingSystemManaged__1
- ../api/Unity.Entities.World.GetOrCreateSystemManaged.html#Unity_Entities_World_GetOrCreateSystemManaged__1
- ../api/Unity.Entities.World.CreateSystemManaged.html#Unity_Entities_World_CreateSystemManaged__1
- ../api/Unity.Entities.World.AddSystemManaged.html
- ../api/Unity.Entities.WorldUnmanaged.GetUnsafeSystemRef.html
- ../api/Unity.Entities.WorldUnmanaged.ResolveSystemStateRef.html
- systems-entityquery.html
- blob-assets-intro.html
- https://docs.unity3d.com/Manual/TermsOfUse.html
- https://unity.com/legal
- https://unity.com/legal/privacy-policy
- https://unity.com/legal/cookie-policy
- https://unity.com/legal/do-not-sell-my-personal-information
