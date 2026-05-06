---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/baking-filter-output.html
fetched: 2026-05-05
section: conversion
---

# Filter Baking Output

## Overview

By default, every entity and component created in the conversion world becomes part of the baking output. However, not all GameObjects in an authoring scene need to remain as entities after baking. For instance, a spline's control point might only serve authoring purposes and can be removed afterward.

## Excluding Entities

To prevent entities from being stored in the entity scene or merged into the main world, add the `BakingOnlyEntity` tag component. This can be done in two ways:

1. A baker can directly add `BakingOnlyEntity` to an entity
2. Add `BakingOnlyEntityAuthoring` to a GameObject

## Excluding Components

Two attributes filter components from baking output:

**`[BakingType]`**: "Filters any components marked with this attribute from the baking output."

**`[TemporaryBakingType]`**: "Destroys any components marked with this attribute from the baking output. This means that components marked with this attribute don't remain from one baking pass to the next, and only exist during the time that a particular baker ran."

### Use Case Example

Components can be excluded to pass information between bakers and baking systems. For example, a baker records bounding boxes as baking types; later, a baking system collects these boxes to compute a convex hull. If only the hull remains useful, bounding boxes are discarded.

## Code Example

```csharp
public class TemporaryBakingDataAuthoring : MonoBehaviour
{
    public float Value;
}

[TemporaryBakingType]
public struct TemporaryBakingData : IComponentData
{
    public float TempValue;
}

public struct SomeComputedData : IComponentData
{
    public float ComputedValue;
}

public class TemporaryDataBaker : Baker<TemporaryBakingDataAuthoring>
{
    public override void Bake(TemporaryBakingDataAuthoring authoring)
    {
        var entity = GetEntity(TransformUsageFlags.Dynamic);
        AddComponent(entity, new TemporaryBakingData{TempValue = authoring.Value});
        AddComponent(entity, new SomeComputedData());
    }
}

[WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
[BurstCompile]
partial struct SomeComputingBakingSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (computedComponent, bakingData) in
                 SystemAPI.Query<RefRW<SomeComputedData>, RefRO<TemporaryBakingData>>())
        {
            var tempValue = bakingData.ValueRO.TempValue;
            float result = 0;
            computedComponent.ValueRW.ComputedValue = result;
        }
    }
}
```

---

## Outgoing Links

- https://docs.unity3d.com/Manual/TermsOfUse.html — Trademarks and terms of use
- https://unity.com/legal — Legal
- https://unity.com/legal/privacy-policy — Privacy Policy
- https://unity.com/legal/cookie-policy — Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information — Do Not Sell or Share My Personal Information
