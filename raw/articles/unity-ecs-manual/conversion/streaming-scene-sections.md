---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/streaming-scene-sections.html
fetched: 2026-05-05
section: conversion
---

# Scene Section Overview

Unity organizes all entities within a scene into **sections**, with section 0 as the default. Each entity carries a `SceneSection` shared component indicating its assigned section, which contains both the scene's GUID as a `Hash128` value and an integer section number.

## Assigning Entities to Sections

To control section assignment, you have two options:

1. Use the authoring component `SceneSectionComponent`, which affects the GameObject it's attached to and all child GameObjects recursively
2. Write a custom baking system to set the `SceneSection` value directly (note: you cannot assign this value within a Baker)

Section indices need not be consecutive. For instance, you could have section 0 and section 123 coexist. The default section 0 always exists, even if empty. In the Editor, scene sections only apply when subscenes are closed; opened subscenes place all entities in section 0.

## Viewing Sections in the Editor

The Component Inspector displays scene section details and GUIDs. When a `SubScene` component is closed, the Inspector lists all present sections, with section 0 appearing first (unlabeled).

## Cross-Section References

Within subscenes, ECS components can only reference:
- Entities in their same section
- Entities in section 0

**Important:** References to entities in different sections or outside section 0 become `Entity.Null` upon loading.

## Entity Prefabs and Sections

All scene entities include a SceneSection component linking them to their section. When a section or scene unloads, matching entities unload as well—including prefab instances. To prevent this behavior, manually remove the SceneSection component from instantiated prefabs.

## Loading Sections

Individual sections can load/unload independently, but section 0 must load first. Similarly, section 0 can only unload after all other sections are already unloaded.

To load a specific section, add the `Unity.Entities.RequestSceneLoaded` component to its meta entity. Query the `ResolvedSectionEntity` buffer on the scene meta entity to access individual section meta entities.

### Code Example

"To load the content of a specific section, add the component `Unity.Entities.RequestSceneLoaded` to the section meta entity."

```csharp
var sectionBuffer = EntityManager.GetBuffer<ResolvedSectionEntity>(sceneEntity);
var sectionEntities = sectionBuffer.ToNativeArray(Allocator.Temp);

for (int i = 0; i < sectionEntities.Length; i += 1)
{
    if (i % 2 == 0)
    {
        var sectionEntity = sectionEntities[i].SectionEntity;
        EntityManager.AddComponent<RequestSceneLoaded>(sectionEntity);
    }
}

sectionEntities.Dispose();
```

To unload a section's content, remove the `Unity.Entities.RequestSceneLoaded` component from its meta entity.

---

## Outgoing Links

- https://docs.unity3d.com/Manual/TermsOfUse.html - Trademarks and terms of use
- https://unity.com/legal - Legal
- https://unity.com/legal/privacy-policy - Privacy Policy
- https://unity.com/legal/cookie-policy - Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information - Do Not Sell or Share My Personal Information
