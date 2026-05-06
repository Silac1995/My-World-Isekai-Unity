---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/ecs-workflow-example-ecb.html
fetched: 2026-05-05
section: workflow
---

# Use Entity Command Buffers for Structural Changes

## Overview

When iterating through entities in an ECS system, you may need to perform operations that modify entity components or the entities themselves—such as removing components or destroying entities. These modifications are called "structural changes" and cannot be performed immediately during iteration. Entity command buffers (ECBs) defer these changes until after the iteration completes.

## Why Immediate Structural Changes Fail

The `EntityManager.RemoveComponent` method works for synchronous changes outside iteration loops. However, attempting to remove components during entity iteration causes an `InvalidOperationException`.

When you iterate with `SystemAPI.Query`, the ECS reads from memory chunks organized by archetype (the specific combination of components an entity possesses). Removing a component changes the entity's archetype, requiring it to move to a different chunk. This invalidates the chunk layout currently being iterated, preventing safe continuation.

### Operations Considered Structural Changes

- Creating or destroying entities
- Adding or removing components
- Setting shared component values

## Solution: Use Built-in ECB Systems

The recommended approach leverages Unity's built-in `EndSimulationEntityCommandBufferSystem`:

1. Retrieve the ECB singleton using `SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()`
2. Create a command buffer via `CreateCommandBuffer(state.WorldUnmanaged)`
3. Queue commands within loops using methods like `ecb.RemoveComponent<T>(entity)`
4. The system automatically executes queued commands after iteration completes

### Example Implementation

```csharp
using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;

[DisableAutoCreation]
public partial struct RotationSystemECB : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;

        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem
            .Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        foreach (var (transform, speed, lifetime, entity) in
                    SystemAPI.Query<RefRW<LocalTransform>, RefRO<RotationSpeed>,
                    RefRW<RotationLifetime>>().WithEntityAccess())
        {
            float rotationThisFrame = speed.ValueRO.RadiansPerSecond * deltaTime;
            transform.ValueRW = transform.ValueRO.RotateY(rotationThisFrame);
            lifetime.ValueRW.RadiansRemaining -= rotationThisFrame;

            if (lifetime.ValueRO.RadiansRemaining <= 0)
            {
                ecb.RemoveComponent<RotationSpeed>(entity);
            }
        }
    }
}
```

## Alternative: Manual ECB Management

For scenarios requiring immediate command execution:

1. Create an `EntityCommandBuffer` with `Allocator.Temp` before the loop
2. Queue commands during iteration
3. Execute commands via `ecb.Playback(state.EntityManager)` after the loop
4. Dispose the buffer with `ecb.Dispose()`

```csharp
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;

public partial struct RotationSystemManualECB : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        var ecb = new EntityCommandBuffer(Allocator.Temp);

        foreach (var (transform, speed, lifetime, entity) in
                    SystemAPI.Query<RefRW<LocalTransform>, RefRO<RotationSpeed>,
                    RefRW<RotationLifetime>>().WithEntityAccess())
        {
            float rotationThisFrame = speed.ValueRO.RadiansPerSecond * deltaTime;
            transform.ValueRW = transform.ValueRO.RotateY(rotationThisFrame);
            lifetime.ValueRW.RadiansRemaining -= rotationThisFrame;

            if (lifetime.ValueRO.RadiansRemaining <= 0)
            {
                ecb.RemoveComponent<RotationSpeed>(entity);
            }
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }
}
```

## Key Benefit

ECBs enable deferred structural changes at safe points in the frame and allow multiple systems or parallel jobs to queue commands independently, reducing synchronization bottlenecks.

---

## Outgoing Hyperlinks

- https://docs.unity3d.com/Packages/com.unity.entities@latest/index.html (Entities package)
- https://docs.unity3d.com/Packages/com.unity.entities.graphics@latest/index.html (Entities Graphics package)
- https://docs.unity3d.com/Manual/TermsOfUse.html (Trademarks and terms of use)
- https://unity.com/legal (Legal)
- https://unity.com/legal/privacy-policy (Privacy Policy)
- https://unity.com/legal/cookie-policy (Cookie Policy)
- https://unity.com/legal/do-not-sell-my-personal-information (Do Not Sell or Share My Personal Information)
