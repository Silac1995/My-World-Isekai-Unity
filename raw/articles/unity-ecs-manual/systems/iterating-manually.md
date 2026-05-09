---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/iterating-manually.html
fetched: 2026-05-05
section: systems
---

# Iterate Manually Over Data

## Overview

When the standard `EntityQuery` iteration model doesn't suit your needs, you can explicitly request all archetype chunks in a native array and process them with jobs like `IJobParallelFor`.

## Manual Iteration Example

The documentation provides a complete example using a `RotationSpeedSystem` that demonstrates manual chunk iteration:

```csharp
public partial class RotationSpeedSystem : SystemBase
{
   [BurstCompile]
   struct RotationSpeedJob : IJobParallelFor
   {
       [DeallocateOnJobCompletion] public NativeArray<ArchetypeChunk> Chunks;
       public ComponentTypeHandle<RotationQuaternion> RotationType;
       [ReadOnly] public ComponentTypeHandle<RotationSpeed> RotationSpeedType;
       public float DeltaTime;

       public void Execute(int chunkIndex)
       {
           var chunk = Chunks[chunkIndex];
           var chunkRotation = chunk.GetNativeArray(RotationType);
           var chunkSpeed = chunk.GetNativeArray(RotationSpeedType);
           var instanceCount = chunk.Count;

           for (int i = 0; i < instanceCount; i++)
           {
               var rotation = chunkRotation[i];
               var speed = chunkSpeed[i];
               rotation.Value = math.mul(math.normalize(rotation.Value), 
                   quaternion.AxisAngle(math.up(), 
                   speed.RadiansPerSecond * DeltaTime));
               chunkRotation[i] = rotation;
           }
       }
   }
   
   EntityQuery m_Query;   

   protected override void OnCreate()
   {
       m_Query = new EntityQueryDescBuilder(Allocator.Temp)
       .WithAllRW<RotationQuaternion>()
       .WithAll<RotationSpeed>()
       .Build();
   }

   protected override void OnUpdate()
   {
       var rotationType = GetComponentTypeHandle<RotationQuaternion>();
       var rotationSpeedType = GetComponentTypeHandle<RotationSpeed>(true);
       var chunks = m_Query.ToArchetypeChunkArray(Allocator.TempJob);
       
       var rotationsSpeedJob = new RotationSpeedJob
       {
           Chunks = chunks,
           RotationType = rotationType,
           RotationSpeedType = rotationSpeedType,
           DeltaTime = Time.deltaTime
       };
       this.Dependency = rotationsSpeedJob.Schedule(chunks.Length, 32, this.Dependency);
   }
}
```

## Manual EntityManager Iteration

The `EntityManager` class allows manual iteration, though this approach is "usually inefficient." Use it primarily for testing, debugging, or isolated worlds with controlled entity sets.

### Iterate Through All Entities

```csharp
var entityManager = World.Active.EntityManager;
var allEntities = entityManager.GetAllEntities();
foreach (var entity in allEntities)
{
   //...
}
allEntities.Dispose();
```

### Iterate Through All Chunks

```csharp
var entityManager = World.Active.EntityManager;
var allChunks = entityManager.GetAllChunks();
foreach (var chunk in allChunks)
{
   //...
}
allChunks.Dispose();
```

---

## Links

- [EntityQuery Documentation](systems-entityquery.html)
- [Archetype Chunks Concepts](concepts-archetypes.html#archetype-chunks)
- [NativeArray API Reference](https://docs.unity3d.com/ScriptReference/Unity.Collections.NativeArray_1.html)
- [Unity Trademarks and Terms of Use](https://docs.unity3d.com/Manual/TermsOfUse.html)
- [Unity Legal](https://unity.com/legal)
- [Privacy Policy](https://unity.com/legal/privacy-policy)
- [Cookie Policy](https://unity.com/legal/cookie-policy)
- [Do Not Sell or Share My Personal Information](https://unity.com/legal/do-not-sell-my-personal-information)
