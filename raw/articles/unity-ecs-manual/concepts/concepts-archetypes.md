---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/concepts-archetypes.html
fetched: 2026-05-05
section: concepts
---

# Archetypes Concepts

An archetype serves as a unique identifier for all entities within a world that share the same combination of component types. For instance, entities possessing `Speed`, `Direction`, `Position`, and `Renderer` components form one archetype, while entities with only `Speed`, `Direction`, and `Position` constitute a different archetype.

## Entity Movement Between Archetypes

When you add or remove component types from an entity, the `EntityManager` relocates that entity to the corresponding archetype. If the target archetype doesn't exist, the `EntityManager` creates it automatically.

> "Moving entities frequently is resource-intensive and reduces the performance of your application."

For performance optimization details, refer to structural change documentation.

## Query Efficiency

The archetype-based structure enables efficient entity queries by component type. Rather than scanning individual entities, the system locates all archetypes containing specific component types. Since archetypes typically stabilize early in program execution, cached queries provide significant performance improvements.

Archetypes persist throughout a world's lifetime and are only destroyed when the world itself is destroyed.

## Archetype Chunks

Entities and components sharing the same archetype are organized in uniform memory blocks called chunks, each occupying 16 KiB of memory.

### Chunk Structure

Each chunk contains:
- One array per component type
- One additional array for entity IDs

For an archetype with component types A and B, each chunk maintains three arrays: component A values, component B values, and entity IDs.

### Data Organization

Arrays maintain tight packing: entities occupy consecutive indices starting from 0. When new entities are added, they fill the first available index. When entities are removed, the final entity in the chunk fills any gaps. The `EntityManager` creates new chunks when existing ones fill completely and destroys chunks when they become empty.

## Editor Support

The Archetypes window displays all worlds' archetypes and shows allocated versus unused memory for each archetype. Archetypes appear with a hexagonal icon intersected by lines.

---

## Outgoing Hyperlinks

- http://docs.unity3d.com/ - docs.unity3d.com
- ../index.html - Home
- concepts-entities.html - entities
- concepts-worlds.html - world
- concepts-components.html - component
- ../api/Unity.Entities.EntityManager.html - EntityManager
- concepts-structural-changes.html - Structural change concepts
- performance-chunk-allocations.html - Managing chunk allocations
- editor-archetypes-window.html - Archetypes window
- https://docs.unity3d.com/Manual/TermsOfUse.html - Trademarks and terms of use
- https://unity.com/legal - Legal
- https://unity.com/legal/privacy-policy - Privacy Policy
- https://unity.com/legal/cookie-policy - Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information - Do Not Sell or Share My Personal Information
