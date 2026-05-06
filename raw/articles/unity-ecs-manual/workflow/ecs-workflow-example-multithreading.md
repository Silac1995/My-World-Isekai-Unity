---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/ecs-workflow-example-multithreading.html
fetched: 2026-05-05
section: workflow
---

# Make a System Multithreaded - Unity Entities 6.4.0

## Overview

This documentation explains how to modify a system to execute jobs in parallel across multiple threads using Unity's Entity Component System (ECS).

## Prerequisites

- Unity 6.X project with Entities and Entities Graphics packages installed
- Optional: Completion of the Authoring and baking workflow example

## Multithreading Implementation Overview

The documentation begins by presenting a single-threaded rotation system using `ISystem` and a `foreach` loop. The example shows how to replace this approach with parallel job execution.

## Creating an IJobEntity Job and Scheduling It

### Step-by-step implementation:

1. **Create a new job struct** implementing `IJobEntity` interface with an `Execute` method
2. **Query components** using method parameters with access specifiers:
   - `ref LocalTransform` = read-write access
   - `in RotationSpeed` = read-only access
3. **Add a public field** for `deltaTime` to pass data to the job
4. **Implement Execute method** with the rotation logic

### Sample Job Structure:

```csharp
[BurstCompile]
public partial struct RotationJob : IJobEntity
{
    public float deltaTime;

    private void Execute(ref LocalTransform transform, in RotationSpeed speed)
    {
        transform = transform.RotateY(speed.RadiansPerSecond * deltaTime);
    }
}
```

### Scheduling the Job:

```csharp
[BurstCompile]
public void OnUpdate(ref SystemState state)
{
    new RotationJob
    {
        deltaTime = SystemAPI.Time.DeltaTime
    }.ScheduleParallel();
}
```

### System Lifecycle:

```csharp
public void OnCreate(ref SystemState state)
{
    state.RequireForUpdate<RotationSpeed>();
}
```

## Visualizing Multithreading in the Profiler

To observe parallel execution:

1. Add CPU-intensive work (e.g., Fibonacci calculation) to the Execute method
2. Create hundreds of GameObjects with `RotationSpeedAuthoring` components
3. Enter Play mode and open the Profiler
4. The Timeline view displays multiple `RotationJob` instances running on worker threads

The documentation notes that "CPU worker threads are allocated per chunk" with a maximum of 128 entities per chunk.

## Complete Multithreaded System Code

```csharp
using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;

[BurstCompile]
public partial struct RotationSystemMultithreaded : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<RotationSpeed>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        new RotationJob
        {
            deltaTime = SystemAPI.Time.DeltaTime
        }.ScheduleParallel();
    }
}

[BurstCompile]
public partial struct RotationJob : IJobEntity
{
    public float deltaTime;

    private void Execute(ref LocalTransform transform, in RotationSpeed speed)
    {
        transform = transform.RotateY(speed.RadiansPerSecond * deltaTime);
    }
}
```

## Key Concepts

- **BurstCompile attribute**: Compiles methods into optimized native CPU code
- **IJobEntity interface**: Enables automatic query generation from Execute parameters
- **ScheduleParallel()**: Adds jobs to scheduler queue for parallel execution
- **SystemAPI restrictions**: Jobs cannot access `SystemAPI` after scheduling
- **DisableAutoCreation attribute**: Disables automatic system execution for testing

---

## Outgoing Hyperlinks

1. https://docs.unity3d.com/6000.0/Documentation/Manual/JobSystem.html - Job System documentation
2. https://docs.unity3d.com/Packages/com.unity.entities@latest/index.html - Entities package
3. https://docs.unity3d.com/Packages/com.unity.entities.graphics@latest/index.html - Entities Graphics package
4. https://docs.unity3d.com/Manual/Profiler.html - Unity Profiler manual
5. https://docs.unity3d.com/Manual/profiler-introduction.html - Profiler introduction
6. https://docs.unity3d.com/Manual/TermsOfUse.html - Unity terms of use
7. https://unity.com/legal - Unity legal page
8. https://unity.com/legal/privacy-policy - Privacy policy
9. https://unity.com/legal/cookie-policy - Cookie policy
10. https://unity.com/legal/do-not-sell-my-personal-information - Do not sell information
