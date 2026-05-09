---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/systems-comparison.html
fetched: 2026-05-05
section: systems
---

# Systems Comparison

## Overview

The Entities package provides two approaches to create systems: [`ISystem`](../api/Unity.Entities.ISystem.html) and [`SystemBase`](../api/Unity.Entities.SystemBase.html). The choice between them involves performance versus convenience trade-offs.

## Key Differences

"`ISystem` is compatible with Burst, is faster than `SystemBase`, and has a value-based representation."

In general, `ISystem` is recommended for better performance. However, `SystemBase` offers convenient features despite using garbage collection allocations or increased `SourceGen` compilation time.

## Compatibility Comparison Table

| Feature | ISystem | SystemBase |
|---------|---------|-----------|
| Burst compile `OnCreate`, `OnUpdate`, and `OnDestroy` | Yes | No |
| Unmanaged memory allocated | Yes | No |
| GC allocated | No | Yes |
| Can store managed data directly in system type | No | Yes |
| [Idiomatic `foreach`](systems-systemapi-query.html) | Yes | Yes |
| [`Job.WithCode`](../api/Unity.Entities.SystemBase.Job.html#Unity_Entities_SystemBase_Job) | No | Yes |
| [`IJobEntity`](../api/Unity.Entities.IJobEntity.html) | Yes | Yes |
| [`IJobChunk`](../api/Unity.Entities.IJobChunk.html) | Yes | Yes |
| Supports inheritance | No | Yes |

## System Comparison Example

This example demonstrates a system that moves entities along spline paths. The system requires:

- Read-only access to a `FollowingSplineTag` component
- Read-only access to `SplineFollower` components
- Random-access to `SplinePointsBuffer` dynamic buffers
- Random-access to `SplineLength` components
- Read-write access to `LocalTransform` components

### Component Declarations

```csharp
public struct FollowingSplineTag : IComponentData { }

public struct SplineFollower : IComponentData
{
   public Entity Spline;
   public float Distance;
}

public struct SplinePointsBuffer : IBufferElementData
{
   public float3 SplinePoint;
}

public struct SplineLength : IComponentData
{
   public float Value;
}

public struct SplineHelper
{
   public static LocalTransform FollowSpline(
       DynamicBuffer<SplinePointsBuffer> pointsBuf, float length, float distance)
    {
       // Perform spline calculation and return a new LocalTransform here
    }
}
```

### ISystem Foreach Implementation

```csharp
var lengthLookup = SystemAPI.GetComponentLookup<SplineLength>(true);
var pointsBufferLookup = SystemAPI.GetBufferLookup<SplinePointsBuffer>(true);

// Version with writeable buffer lookup
foreach (var (transform, follower) in
        SystemAPI.Query<RefRW<LocalTransform>, RefRO<SplineFollower>>()
        .WithAll<FollowingSplineTag>())
{
   var splineLength = lengthLookup[follower.ValueRO.Spline].Value;
   var pointsBuf = pointsBufferLookup[follower.ValueRO.Spline];
   transform.ValueRW = SplineHelper.FollowSpline(pointsBuf, splineLength, follower.ValueRO.Distance);
}
```

### IJobEntity Implementation

```csharp
// Job declaration
[BurstCompile]
[WithAll(typeof(FollowingSplineTag))]
public partial struct FollowSplineJob : IJobEntity
{
   [ReadOnly] public ComponentLookup<SplineLength> LengthLookup;
   [ReadOnly] public BufferLookup<SplinePointsBuffer> PointsBufferLookup;
  
   public void Execute(ref LocalTransform transform, in SplineFollower follower)
   {
       var splineLength = LengthLookup[follower.Spline].Value;
       var pointsBuf = PointsBufferLookup[follower.Spline];
       transform = SplineHelper.FollowSpline(pointsBuf, splineLength, follower.Distance);
   }
}

// in OnUpdate()...
new FollowSplineJob
{
   LengthLookup =  lengthLookup,
   PointsBufferLookup =  pointsBufferLookup
}.ScheduleParallel();
```

## Additional Resources

- [`ISystem` overview](systems-isystem.html)
- [`SystemBase` overview](systems-systembase.html)

---

## Outgoing Hyperlinks

- https://docs.unity3d.com/docs.unity3d.com
- https://docs.unity3d.com/api/Unity.Entities.ISystem.html
- https://docs.unity3d.com/api/Unity.Entities.SystemBase.html
- https://docs.unity3d.com/Manual/TermsOfUse.html
- https://unity.com/legal
- https://unity.com/legal/privacy-policy
- https://unity.com/legal/cookie-policy
- https://unity.com/legal/do-not-sell-my-personal-information
