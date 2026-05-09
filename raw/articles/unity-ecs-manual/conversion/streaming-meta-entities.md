---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/streaming-meta-entities.html
fetched: 2026-05-05
section: conversion
---

# Scene and Section Meta Entities

## Overview

When you bake an authoring scene, Unity produces an entity scene file with a header containing:

- A list of sections with metadata (filenames, sizes, bounding volumes)
- AssetBundle dependency references (GUIDs)
- Optional custom metadata for game-specific purposes

"The list of sections and bundles determines the list of files that Unity needs to load when loading the scene."

## Loading Process

Entity scene loading occurs in two stages:

1. **Resolve stage**: Loads the header and creates one meta entity per scene and per section
2. **Content loading**: Unity loads section contents

You can query the `ResolvedSectionEntity` buffer on the scene meta entity to access individual section meta entities.

## Custom Section Metadata

Section meta entities are available before their content loads, making them ideal for storing custom metadata. Common use cases include storing bounding box dimensions to determine when sections should load.

### Implementation

Add ECS components to section meta entities during baking using the `SerializeUtility.GetSceneSectionEntity` method within a baking system:

```csharp
public struct RadiusSectionMetadata : IComponentData
{
    public float Radius;
    public float3 Position;
}

[WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
partial struct RadiusSectionMetadataBakingSystem : ISystem
{
    private EntityQuery sectionEntityQuery;

    public void OnCreate(ref SystemState state)
    {
        sectionEntityQuery = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<SectionMetadataSetup>().Build(ref state);
    }

    public void OnUpdate(ref SystemState state)
    {
        int section = 3;
        float radius = 10f;
        float3 position = new float3(0f);

        var sectionEntity = SerializeUtility.GetSceneSectionEntity(section,
            state.EntityManager, ref sectionEntityQuery, true);
        state.EntityManager.AddComponentData(sectionEntity, new RadiusSectionMetadata
        {
            Radius   = radius,
            Position = position
        });
    }
}
```

**Performance Note**: Create the entity query outside the method and pass it for efficiency rather than relying on internal creation.

---

## Outgoing Links

- https://docs.unity3d.com/Manual/TermsOfUse.html — Trademarks and terms of use
- https://unity.com/legal — Legal
- https://unity.com/legal/privacy-policy — Privacy Policy
- https://unity.com/legal/cookie-policy — Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information — Do Not Sell or Share My Personal Information
- https://docs.unity3d.com/2022.2/Documentation/Manual/baking-overview.html — Baking
- https://docs.unity3d.com/2022.2/Documentation/Manual/streaming-scene-sections.html — Scene sections
- https://docs.unity3d.com/2022.2/Documentation/ScriptReference/Unity.Scenes.ResolvedSectionEntity.html — ResolvedSectionEntity API
- https://docs.unity3d.com/2022.2/Documentation/ScriptReference/Unity.Entities.Serialization.SerializeUtility.GetSceneSectionEntity.html — SerializeUtility.GetSceneSectionEntity
