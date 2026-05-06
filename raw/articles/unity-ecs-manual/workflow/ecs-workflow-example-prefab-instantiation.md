---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/ecs-workflow-example-prefab-instantiation.html
fetched: 2026-05-05
section: workflow
---

# Entity Prefab Instantiation Workflow

## Overview

This workflow demonstrates entity prefab instantiation in ECS, covering these concepts:

- Authoring GameObject component for controlling instantiation via the Editor
- Converting GameObject prefabs into ECS prefabs
- Creating a Burst-compatible system

**Note:** If you've completed the Authoring and baking workflow, skip step 1 and begin with "Create a spawner entity for instantiating prefabs."

## Prerequisites

This workflow requires a Unity 6 project with these packages:

- Entities
- Entities Graphics

## Workflow Steps

1. Create the subscene for the example
2. Create a spawner entity for instantiating prefabs
3. Create a system that instantiates prefabs

---

## Create the Subscene for the Example

The ECS workflow begins by creating a subscene. ECS uses subscenes instead of standard scenes because "Unity's core scene system is incompatible with ECS."

**To create a subscene:**

1. Open an existing scene in the Editor
2. Right-click in the Hierarchy and select **New Sub Scene** > **Empty Scene**
3. Enter a name for the subscene and save it

---

## Create a Spawner Entity for Instantiating Prefabs

This section creates an authoring GameObject called **Spawner** to control prefab instantiation. A baker class transfers data from the Spawner to a corresponding ECS entity.

### Step-by-Step Instructions

1. Create a new empty GameObject named **Spawner** in the subscene

2. Create a C# script called **SpawnerAuthoring.cs**:

```csharp
using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;

class SpawnerAuthoring : MonoBehaviour
{
    public GameObject Prefab;
    public float SpawnRate;
}

class SpawnerBaker : Baker<SpawnerAuthoring>
{
    public override void Bake(SpawnerAuthoring authoring)
    {
        // This line converts the Spawner GameObject into an Entity.
        // TransformUsageFlags is None because the Spawner entity is not
        // rendered and does not need a LocalTransform component.
        var entity = GetEntity(TransformUsageFlags.None);
        AddComponent(entity, new Spawner
        {
            // This GetEntity call converts a GameObject prefab into an entity
            // prefab. The prefab is rendered, so it requires the standard Transform
            // components, that's why TransformUsageFlags is set to Dynamic.
            Prefab = GetEntity(authoring.Prefab, TransformUsageFlags.Dynamic),
            SpawnPosition = authoring.transform.position,
            SpawnRate = authoring.SpawnRate,
            NextSpawnTime = 0f
        });
    }
}

public struct Spawner : IComponentData
{
    public Entity Prefab;
    public float3 SpawnPosition;
    public float SpawnRate;
    // This field is used only for the multi-threading example.
    public float NextSpawnTime;
}
```

### Key Implementation Details

The Spawner entity doesn't need Transform components since it's not rendered:

```csharp
var entity = GetEntity(TransformUsageFlags.None);
```

The `AddComponent` method adds the Spawner component, which includes the Prefab field.

The following GetEntity call converts a GameObject prefab into an entity prefab. Since the prefab represents rendered cubes requiring Transform components, TransformUsageFlags is set to Dynamic:

```csharp
Prefab = GetEntity(authoring.Prefab, TransformUsageFlags.Dynamic)
```

3. If you've completed the Authoring and baking workflow, use a cube GameObject with the Rotation Speed Authoring component. Otherwise, refer to the Authoring and baking workflow page to create this component and assign it to a cube.

4. Create a prefab by dragging the cube (with Rotation Speed Authoring component) to a folder in the Project window

5. Select the Spawner GameObject. In the Spawner Authoring component, assign the cube prefab to the Prefab field

### Observing the Entity Prefab

The ECS framework converts the GameObject prefab into an entity prefab immediately upon selection.

**To observe this:**

1. Open the Entities Hierarchy window: **Window** > **Entities** > **Hierarchy**
2. Select the Spawner GameObject in the regular Hierarchy
3. Switch the Entities Hierarchy window to Runtime data mode

The Entities Hierarchy should display:
- The regular Hierarchy window with the Spawner GameObject
- The Inspector window in Authoring data mode (editable from the Editor)
- The Entities Hierarchy window in Runtime data mode, showing the Spawner entity and the Cube entity prefab (marked with a blue icon)

---

## Create a System That Instantiates Prefabs

This section creates a system that instantiates entity prefabs and sets component data on them.

Create a C# script called **SpawnerSystem.cs**:

```csharp
using Unity.Entities;
using Unity.Transforms;
using Unity.Burst;
using Unity.Mathematics;

public partial struct SpawnerSystem : ISystem
{
    private float nextSpawn;

    // The Random struct is from the Unity Mathematics package, which provides types
    // and functions optimized for Burst.
    private Random random;

    public void OnCreate(ref SystemState state)
    {
        // This call prevents the system from updating unless at least one entity with
        // the Spawner component exists in the ECS world.
        // This also prevents GetSingleton from throwing an exception if it doesn't find
        // an object of type Spawner.
        state.RequireForUpdate<Spawner>();

        random = new Random((uint)System.DateTime.Now.Ticks);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // Use the GetSingleton method when there is only one entity of a 
        // specific type in the ECS world.
        Spawner spawner = SystemAPI.GetSingleton<Spawner>();

        if (nextSpawn < SystemAPI.Time.ElapsedTime)
        {
            // The Prefab field of the spawner variable contains a reference to 
            // the entity prefab which ECS converts during the baking stage.
            Entity newEntity = state.EntityManager.Instantiate(spawner.Prefab);

            float3 randomOffset = (random.NextFloat3() - 0.5f) * 10f;
            randomOffset.y = 0;

            float3 newPosition = spawner.SpawnPosition + randomOffset;

            state.EntityManager.SetComponentData(newEntity,
                                        LocalTransform.FromPosition(newPosition));

            nextSpawn = (float)SystemAPI.Time.ElapsedTime + spawner.SpawnRate;
        }
    }
}
```

### Implementation Details

Since systems aren't attached to specific entities, the system's OnUpdate method might run before scene initialization. The RequireForUpdate method ensures the system doesn't execute before a Spawner entity exists:

```csharp
state.RequireForUpdate<Spawner>();
```

With only one spawner entity, use GetSingleton instead of a query:

```csharp
Spawner spawner = SystemAPI.GetSingleton<Spawner>();
```

Entity prefabs are instantiated using EntityManager.Instantiate. The Prefab field contains the entity prefab reference that ECS converted during baking:

```csharp
Entity newEntity = state.EntityManager.Instantiate(spawner.Prefab);
```

To avoid spawning entities at identical locations, SetComponentData sets LocalTransform values to random positions near the spawner:

```csharp
state.EntityManager.SetComponentData(newEntity, LocalTransform.FromPosition(newPosition));
```

The Random method comes from the Unity Mathematics package, providing types and functions optimized for Burst.

---

## Try the System in Action

1. Enter Play mode. SpawnerSystem creates entity prefab instances at the rate specified in the Spawner GameObject's Spawn Rate property

2. If you've completed the Authoring and baking workflow with RotationSystem.cs, the prefabs should spin in the Game view

3. Pause Play mode and open the Entities Hierarchy window, switching to Runtime data mode

The window displays the source entity prefab with a solid blue icon and instantiated prefabs with hollow grey icons and blue names.

Select the source prefab to view it in the Inspector (Runtime data mode). It displays a Prefab tag that excludes it from system queries affecting prefab instances.

---

## Additional Resources

- Introduction to the ECS workflow
- Starter ECS workflow
- Authoring and baking workflow example
- Make a system multithreaded
- Use entity command buffer for structural changes

---

## Outgoing Hyperlinks

1. https://docs.unity3d.com/Packages/com.unity.entities@latest/index.html – "Entities" package documentation
2. https://docs.unity3d.com/Packages/com.unity.entities.graphics@latest/index.html – "Entities Graphics" package documentation
3. https://docs.unity3d.com/Documentation/Manual/CreatingScenes.html – Unity scene creation manual
4. https://docs.unity3d.com/6000.0/Documentation/Manual/CreatingScenes.html – Unity 6 scene creation documentation
5. https://docs.unity3d.com/Packages/com.unity.mathematics@latest/index.html?subfolder=/manual/random-numbers.html – Unity Mathematics random numbers documentation
6. https://docs.unity3d.com/Packages/com.unity.mathematics@latest/index.html?subfolder=/manual/index.html – Unity Mathematics package documentation
7. https://docs.unity3d.com/Manual/TermsOfUse.html – Unity Terms of Use
8. https://unity.com/legal – Unity Legal
9. https://unity.com/legal/privacy-policy – Unity Privacy Policy
10. https://unity.com/legal/cookie-policy – Unity Cookie Policy
11. https://unity.com/legal/do-not-sell-my-personal-information – Do Not Sell My Information
