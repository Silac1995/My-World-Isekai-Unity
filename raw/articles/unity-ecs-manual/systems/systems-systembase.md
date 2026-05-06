---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/systems-systembase.html
fetched: 2026-05-05
section: systems
---

# SystemBase Overview

## Introduction

To create a managed system, implement the abstract class `SystemBase`. The `OnUpdate` system event callback is required to add work that executes every frame. Other callback methods in the `ComponentSystemBase` namespace remain optional.

## Job Scheduling

All system events run on the main thread. It's recommended to use the `OnUpdate` method to schedule jobs for most processing tasks. Three primary mechanisms exist for job scheduling:

- **`Job.WithCode`**: Executes a lambda expression as a single background job.
- **`IJobEntity`**: Iterates over component data.
- **`IJobChunk`**: Iterates over data by archetype chunk.

## Code Example

```csharp
public struct Position : IComponentData
{
    public float3 Value;
}

public struct Velocity : IComponentData
{
    public float3 Value;
}

[RequireMatchingQueriesForUpdate]
public partial class ECSSystem : SystemBase
{
    [BurstCompile]
    public partial struct ExampleJob : IJobEntity
    {
        public float DeltaTime;
        
        public void Execute(ref Position position, in Velocity velocity)
        {
            position.Value += velocity.Value * DeltaTime;
        }
    }

    protected override void OnUpdate()
    {
        var job = new ExampleJob
        {
            DeltaTime = SystemAPI.Time.DeltaTime
        };

        job.ScheduleParallel();
    }
}
```

## Callback Method Order

The following lifecycle methods are invoked during system execution:

- **`OnCreate`**: Invoked when a system is instantiated.
- **`OnStartRunning`**: Invoked before the first `OnUpdate` call and whenever a system resumes.
- **`OnUpdate`**: Invoked every frame while the system performs work.
- **`OnStopRunning`**: Invoked before `OnDestroy` and when the system stops running (no matching entities or disabled state).
- **`OnDestroy`**: Invoked when a system is destroyed.

The event sequence follows: OnCreate -> OnStartRunning -> OnUpdate -> OnStopRunning -> OnDestroy.

A parent system group's `OnUpdate` method triggers all child system `OnUpdate` methods within that group.

---

## Outgoing Links

- http://docs.unity3d.com/
- https://docs.unity3d.com/Manual/TermsOfUse.html
- https://unity.com/legal
- https://unity.com/legal/privacy-policy
- https://unity.com/legal/cookie-policy
- https://unity.com/legal/do-not-sell-my-personal-information
