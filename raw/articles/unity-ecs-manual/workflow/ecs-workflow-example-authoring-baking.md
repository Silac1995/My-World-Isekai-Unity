---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/ecs-workflow-example-authoring-baking.html
fetched: 2026-05-05
section: workflow
---

# ECS Authoring and Baking Workflow

## Overview

The Entity Component System (ECS) authoring and baking workflow bridges GameObject-based editing with high-performance ECS runtime architecture. This workflow enables developers to author content using familiar GameObjects and MonoBehaviour components while benefiting from ECS's optimized runtime performance.

## Prerequisites

A Unity 6 project requires these packages:
- Entities
- Entities Graphics

## Workflow Steps

### 1. Create the Subscene

ECS uses subscenes rather than standard scenes due to incompatibility with Unity's core scene system.

**Process:**
1. Open an existing scene in the Editor
2. Right-click in Hierarchy and select **New Sub Scene** > **Empty Scene**
3. Name and save the subscene

### 2. Create an Entity from a GameObject

When you create a GameObject within a subscene, ECS automatically converts it into an entity during the baking process.

**Steps:**
1. Select the subscene in the Hierarchy window
2. Create a cube GameObject via **GameObject** > **3D Object** > **Cube**

Select the Cube to view the **Entity Baking Preview** section in the Inspector. This displays converted entity components, such as `Unity.Transforms.LocalToWorld`, which ECS generates from the GameObject's Transform component.

### 3. Create a New ECS Component

Components in ECS store entity data that systems read or modify. This example creates a rotation speed component using the `IComponentData` interface.

**Code Example:**
```csharp
using Unity.Entities;

// This component defines the rotation speed of an entity.
public struct RotationSpeed : IComponentData
{
    public float RadiansPerSecond;
}
```

### 4. Add a Component to an Entity

Components are added to entities through either the baking process or runtime APIs. This section focuses on the baking approach.

#### Create an Authoring Component

Authoring components are MonoBehaviour classes that pass GameObject editor data into ECS components.

**Code Example:**
```csharp
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

// The authoring component provides a way to define the rotation speed of
// a GameObject in the Editor. ECS does not use the authoring component at 
// runtime, but converts it into an entity component using the Baker class.    
public class RotationSpeedAuthoring : MonoBehaviour
{
    public float DegreesPerSecond = 360.0f;
}
```

Add this script to the Cube GameObject.

#### Inspect in Authoring and Runtime Modes

The Inspector offers two data modes accessible via a circle icon in the top-right corner:

- **Authoring Mode:** Displays the `DegreesPerSecond` property from the MonoBehaviour
- **Runtime Mode:** Shows the converted entity data (initially empty until a baker is created)

#### Create the Baker Class

The Baker class defines the conversion process from GameObject data to entity data.

**Code Example:**
```csharp
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class RotationSpeedAuthoring : MonoBehaviour
{
    public float DegreesPerSecond = 360.0f;
}

// In the baking process, this Baker runs once for every RotationSpeedAuthoring
// instance in a subscene.
class RotationSpeedBaker : Baker<RotationSpeedAuthoring>
{
    public override void Bake(RotationSpeedAuthoring authoring)
    {
        // GetEntity returns an entity that ECS creates from the GameObject using
        // pre-built ECS baker methods. TransformUsageFlags.Dynamic instructs the
        // Bake method to add the Transforms.LocalTransform component to the entity.
        var entity = GetEntity(authoring, TransformUsageFlags.Dynamic);

        var rotationSpeed = new RotationSpeed
        {
            // The math class is from the Unity.Mathematics namespace.
            // Unity.Mathematics is optimized for Burst-compiled code.
            RadiansPerSecond = math.radians(authoring.DegreesPerSecond)
        };

        AddComponent(entity, rotationSpeed);
    }
}
```

**Key Methods:**

- `Bake()` - Executes for each authoring component instance, defining conversion logic
- `GetEntity()` - Returns an entity created from the GameObject; `TransformUsageFlags.Dynamic` adds the LocalTransform component
- `AddComponent()` - Attaches the ECS component to the entity

After saving, inspect the cube in Runtime mode to see the **Rotation Speed** component with **Radians Per Second** property.

### 5. Create a System That Rotates Entities

Systems query for entities matching specific criteria and perform operations on all matching entities.

**Code Example:**
```csharp
using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;

// This example defines an unmanaged system based on the ISystem interface.
// ECS uses code generation, which is why the struct must be declared as partial.
public partial struct RotationSystem : ISystem
{
    // The BurstCompile attribute indicates that the method should be compiled
    // with the Burst compiler into highly-optimized native CPU code.
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // In ECS, use the DeltaTime property from Entities.SystemAPI.Time.
        float deltaTime = SystemAPI.Time.DeltaTime;

        // Create a query that selects all entities that have a LocalTransform
        // component and a RotationSpeed component.
        // In each loop iteration, the transform variable is assigned 
        // a read-write reference to LocalTransform, and the speed variable is
        // assigned a read-only reference to the RotationSpeed component.
        foreach (var (transform, speed) in
                    SystemAPI.Query<RefRW<LocalTransform>, RefRO<RotationSpeed>>())
        {
            // ValueRW and ValueRO both return a reference to the actual component
            // value. The difference is that ValueRW does a safety check for 
            // read-write access while ValueRO does a safety check for read-only
            // access.
            transform.ValueRW = transform.ValueRO.RotateY(
                speed.ValueRO.RadiansPerSecond * deltaTime);
        }
    }
}
```

**Process:**

1. Enter Play mode—ECS automatically instantiates systems implementing `ISystem`
2. The cube rotates in both Game and Scene views

The `SystemAPI.Query<RefRW<LocalTransform>, RefRO<RotationSpeed>>()` selects all entities with both components. The rotation is applied via: "transform.ValueRW = transform.ValueRO.RotateY(speed.ValueRO.RadiansPerSecond * deltaTime)".

**`ValueRW` and `ValueRO` Methods:**

These special ECS methods return component references with safety checks—`ValueRW` for read-write access, `ValueRO` for read-only access.

**Testing with Multiple Cubes:**

1. Create additional cube GameObjects in the subscene
2. Add the **Rotation Speed Authoring** component to some cubes
3. Set different **Degrees Per Second** values
4. Enter Play mode—only cubes with the component rotate, at their specified speeds

## Key Concepts

**Baking Process:** Converts GameObjects into optimized ECS entities and components during scene preparation

**Authoring Components:** MonoBehaviour classes providing editor-friendly interfaces for ECS data

**Systems:** Query entities matching specific component criteria and execute operations on all matches

**Data Modes:** Inspector views switching between authoring (editor-friendly) and runtime (entity-focused) representations

---

## Outgoing Hyperlinks

| URL | Link Text |
|-----|-----------|
| https://docs.unity3d.com/Packages/com.unity.entities@latest/index.html | Entities |
| https://docs.unity3d.com/Packages/com.unity.entities.graphics@latest/index.html | Entities Graphics |
| https://docs.unity3d.com/Documentation/Manual/CreatingScenes.html | scene system |
| https://docs.unity3d.com/6000.0/Documentation/Manual/CreatingScenes.html | scene |
| https://docs.unity3d.com/Packages/com.unity.burst@latest | Burst compiler |
| https://docs.unity3d.com/Manual/TermsOfUse.html | Trademarks and terms of use |
| https://unity.com/legal | Legal |
| https://unity.com/legal/privacy-policy | Privacy Policy |
| https://unity.com/legal/cookie-policy | Cookie Policy |
| https://unity.com/legal/do-not-sell-my-personal-information | Do Not Sell or Share My Personal Information |
