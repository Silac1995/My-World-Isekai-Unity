---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/concepts-components.html
fetched: 2026-05-05
section: concepts
---

# Component Concepts | Entities 6.4.0

## Overview

In the Entity Component System (ECS) architecture, **components contain entity data that systems can read or write.**

Components are defined using the `IComponentData` interface, which marks a struct as a component type. Unmanaged components can only contain unmanaged data fields, though they may include methods. For components needing to store managed types, use a class-based managed component instead.

A unique set of an entity's components forms an **archetype**. The ECS stores component data by archetype in 16KiB memory blocks called chunks.

## Component Types

The following table describes the different component types available:

| Component | Description |
|-----------|-------------|
| Unmanaged components | The most common type; limited to certain field types |
| Managed components | Can store any field type as a class-based component |
| Shared components | Group entities in chunks based on shared values |
| Cleanup components | Remove all non-cleanup components when entity is destroyed |
| Tag components | Unmanaged, store no data, used for entity query filtering |
| Buffer components | Function as resizable arrays |
| Chunk components | Store values associated with entire chunks rather than individual entities |
| Enableable components | Can be toggled at runtime without structural changes |
| Singleton components | Limited to one instance per world |

## Additional Resources

- Working with components
- Component types
- Entity concepts
- Archetype concepts

---

## Outgoing Hyperlinks

- http://docs.unity3d.com/ — docs.unity3d.com
- ../index.html — (logo/home)
- concepts-entities.html — entity
- concepts-systems.html — systems
- ../api/Unity.Entities.IComponentData.html — IComponentData
- components-managed.html — Managed components
- concepts-archetypes.html — Archetype concepts
- components-unmanaged.html — Unmanaged components
- components-shared.html — Shared components
- components-cleanup.html — Cleanup components
- components-tag.html — Tag components
- components-buffer.html — Buffer components
- components-chunk.html — Chunk components
- components-enableable.html — Enableable components
- components-singleton.html — Singleton components
- systems-entityquery-intro.html — entity queries
- components-intro.html — Working with components
- components-type.html — Component types
- https://docs.unity3d.com/Manual/TermsOfUse.html — Trademarks and terms of use
- https://unity.com/legal — Legal
- https://unity.com/legal/privacy-policy — Privacy Policy
- https://unity.com/legal/cookie-policy — Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information — Do Not Sell or Share My Personal Information
