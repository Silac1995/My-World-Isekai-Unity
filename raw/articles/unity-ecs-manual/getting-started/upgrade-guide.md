---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/upgrade-guide.html
fetched: 2026-05-05
section: getting-started
---

# Upgrading to Entities 1.4

## Overview

Entities 1.4 introduces changes that may generate warnings in existing projects. This guide covers the necessary updates for two main areas.

## Change Entities.ForEach Code

The `Entities.ForEach` approach is now deprecated in Entities 1.4. Two recommended alternatives exist:

### IJobEntity

This job-based approach allows copying lambda logic directly into an `Execute` method that supports `ref` and `in` parameters for managing read/write access. `IJobEntity` maintains compatibility with all scheduling options from the previous pattern.

**Important note:** "IJobEntity isn't Burst-compiled by default and it can't capture variables because there is no lambda body. Use the `[BurstCompile]` attribute to enable Burst compilation and write captured variables into fields on the job struct."

#### Before (Entities.ForEach):
```csharp
public partial class RotationSpeedSystemForEachISystem : SystemBase
{
    protected override void OnUpdate()
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        Entities
            .ForEach((ref LocalTransform transform, in RotationSpeed rotationSpeed) =>
            {
                transform.Rotation = math.mul(
                    math.normalize(transform.Rotation),
                    quaternion.AxisAngle(math.up(), rotationSpeed.RadiansPerSecond * deltaTime));
            })
            .ScheduleParallel();
    }
}
```

#### After (IJobEntity):
```csharp
[BurstCompile]
public partial struct ASampleJob : IJobEntity
{
    public float DeltaTime;
    void Execute(ref LocalTransform transform, in RotationSpeed rotationSpeed)
    {
        transform.Rotation = math.mul(
            math.normalize(transform.Rotation),
            quaternion.AxisAngle(math.up(), rotationSpeed.RadiansPerSecond * DeltaTime));
    }
}

public partial class ASample : SystemBase
{
    protected override void OnUpdate()
    {
        var deltaTime = SystemAPI.Time.DeltaTime;
        new ASampleJob{ DeltaTime = deltaTime }.ScheduleParallel();
    }
}
```

### SystemAPI.Query

For iteration outside of jobs (while remaining Burst-compatible), this method offers simplicity through `RefRO` and `RefRW` wrapper types indicating access permissions. Additional builder methods support `WithAll`, `WithNone`, `WithAny`, and similar filters.

#### Converted Example:
```csharp
public partial class ASample : SystemBase
{
    protected override void OnUpdate()
    {
        var deltaTime = SystemAPI.Time.DeltaTime;
        foreach (var (transform, rotationSpeed) in 
            SystemAPI.Query<RefRW<LocalTransform>, RefRO<RotationSpeed>>())
        {
            transform.ValueRW.Rotation = math.mul(
                math.normalize(transform.ValueRO.Rotation),
                quaternion.AxisAngle(math.up(), rotationSpeed.ValueRO.RadiansPerSecond * deltaTime));
        }
    }
}
```

## Change Aspects Code

Aspects are deprecated without direct replacement. Instead, replace this abstraction with explicit component queries and helper methods.

#### Before (Aspects):
```csharp
public partial struct RotationSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var deltaTime = SystemAPI.Time.DeltaTime;
        var elapsedTime = SystemAPI.Time.ElapsedTime;

        foreach (var movement in SystemAPI.Query<VerticalMovementAspect>())
        {
            movement.Move(elapsedTime);
        }
    }
}

readonly partial struct VerticalMovementAspect : IAspect
{
    readonly RefRW<LocalTransform> m_Transform;
    readonly RefRO<RotationSpeed> m_Speed;

    public void Move(double elapsedTime)
    {
        m_Transform.ValueRW.Position.y = (float)math.sin(elapsedTime * m_Speed.ValueRO.RadiansPerSecond);
    }
}
```

#### After (EntityQuery with Helper):
```csharp
public partial struct RotationSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var elapsedTime = SystemAPI.Time.ElapsedTime;

        foreach (var (transform, speed) in SystemAPI.Query<RefRW<LocalTransform>, RefRO<RotationSpeed>>())
        {
            VerticalMovementHelper.Move(elapsedTime, transform, speed);
        }
    }
}

static class VerticalMovementHelper
{
    public static void Move(double elapsedTime, RefRW<LocalTransform> transform, RefRO<RotationSpeed> speed)
    {
        transform.ValueRW.Position.y = (float)math.sin(elapsedTime * speed.ValueRO.RadiansPerSecond);
    }
}
```

---

## Outgoing Hyperlinks

- http://docs.unity3d.com/
- [Iterate over component data with IJobEntity](iterating-data-ijobentity.html)
- [Iterate over component data with SystemAPI.Query](systems-systemapi-query.html)
- https://docs.unity3d.com/Manual/TermsOfUse.html
- https://unity.com/legal
- https://unity.com/legal/privacy-policy
- https://unity.com/legal/cookie-policy
- https://unity.com/legal/do-not-sell-my-personal-information
