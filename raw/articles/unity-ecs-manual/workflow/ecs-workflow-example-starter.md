---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/ecs-workflow-example-starter.html
fetched: 2026-05-05
section: workflow
---

# Starter ECS Workflow

This guide demonstrates a basic Entity Component System (ECS) workflow with three main tasks:

1. Create an ECS component
2. Create an ECS system that instantiates an entity with a component
3. View the entity in the Entities Hierarchy window during runtime

## Prerequisites

A Unity 6 project with the Entities package installed is required.

## Create an ECS Component

ECS supports multiple component types. This example uses `IComponentData`, the most common approach. The component is structured as an unmanaged struct for performance benefits compared to GameObject components.

**Steps:**

1. Create a new C# script named `HelloWorld.cs` with this code:

```csharp
using Unity.Entities;
using Unity.Collections;
using UnityEngine;

// This is an example of an unmanaged ECS component.
public struct HelloComponent : IComponentData
{
    // FixedString32Bytes is used instead of string, because
    // struct IComponentData can only contain unmanaged types.
    public FixedString32Bytes Message;
}
```

The `HelloComponent` contains a `Message` field using `FixedString32Bytes`. Since unmanaged structs cannot contain standard C# strings, this fixed-size unmanaged alternative is used instead.

## Create an ECS System

Systems in ECS are structs implementing `ISystem`, responsible for creating and manipulating entities and components.

**Steps:**

1. Add this struct to `HelloWorld.cs`:

```csharp
public partial struct ExampleSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        var entity = state.EntityManager.CreateEntity();
        // Initialize and add a HelloComponent component to the entity.
        state.EntityManager.AddComponentData(entity, new HelloComponent 
            { Message = "Hello ECS World" });
        // Set the name of the entity to make it easier to identify it.
        // Note: the entity Name property only exists in the Editor.
        state.EntityManager.SetName(entity, "Hello World Entity");
    }

    public void OnUpdate(ref SystemState state)
    {
        // The query retrieves all entities with a HelloComponent component.
        foreach (var message in
                    SystemAPI.Query<RefRO<HelloComponent>>())
        {
            Debug.Log(message.ValueRO.Message);
        }
    }
}
```

2. Enter Play mode. The console displays the `Hello ECS World` message.

The `OnCreate` method generates a new entity and attaches the `HelloComponent`. The `OnUpdate` method uses a query to locate all entities with `HelloComponent` and logs their messages.

## View the Entity in Entities Hierarchy Window

Since the entity is created at runtime, it only appears in the Editor during Play mode. Unlike GameObjects, it doesn't show in the standard Hierarchy window. ECS provides a dedicated Entities Hierarchy window for viewing entities in an ECS world.

**Steps:**

1. Open the Entities Hierarchy window via **Window** > **Entities** > **Hierarchy**
2. Enter Play mode
3. Switch to **Runtime** data mode in the Entities Hierarchy window
4. The window displays the entity named "Hello World Entity"
5. Select it and view the `HelloComponent` in the Inspector (Runtime mode)

![The Entities Hierarchy window displaying the new entity. The Inspector displays the new ECS component.](images/getting-started/ecs-hello-world-entities-hierarchy-inspector.png)

## Complete Code

```csharp
using Unity.Entities;
using Unity.Collections;
using UnityEngine;

// This is an example of an unmanaged ECS component.
public struct HelloComponent : IComponentData
{
    // FixedString32Bytes is used instead of string, because
    // struct IComponentData can only contain unmanaged types.
    public FixedString32Bytes Message;
}

public partial struct ExampleSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        var entity = state.EntityManager.CreateEntity();
        // Initialize and add a HelloComponent component to the entity.
        state.EntityManager.AddComponentData(entity, new HelloComponent 
            { Message = "Hello ECS World" });
        // Set the name of the entity to make it easier to identify it.
        // Note: the entity Name property only exists in the Editor.
        state.EntityManager.SetName(entity, "Hello World Entity");
    }

    public void OnUpdate(ref SystemState state)
    {
        // The query retrieves all entities with a HelloComponent component.
        foreach (var message in
                    SystemAPI.Query<RefRO<HelloComponent>>())
        {
            Debug.Log(message.ValueRO.Message);
        }
    }
}
```

## Additional Resources

- Introduction to the ECS workflow
- Authoring and baking workflow example
- Prefab instantiation workflow
- Make a system multithreaded

---

## Outgoing Hyperlinks

- http://docs.unity3d.com/ – docs.unity3d.com
- ../logo.svg – Logo
- ../index.html – Home
- editor-hierarchy-window.html – Data mode documentation
- concepts-components.html#component-types – Component types
- ../api/Unity.Entities.ISystem.OnCreate.html – OnCreate API reference
- concepts-worlds.html – ECS worlds
- ecs-workflow-intro.html – Introduction to the ECS workflow
- ecs-workflow-example-authoring-baking.html – Authoring and baking workflow example
- ecs-workflow-example-prefab-instantiation.html – Prefab instantiation workflow
- ecs-workflow-example-multithreading.html – Make a system multithreaded
- https://docs.unity3d.com/Manual/TermsOfUse.html – Trademarks and terms of use
- https://unity.com/legal – Legal
- https://unity.com/legal/privacy-policy – Privacy Policy
- https://unity.com/legal/cookie-policy – Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information – Do Not Sell or Share My Personal Information
