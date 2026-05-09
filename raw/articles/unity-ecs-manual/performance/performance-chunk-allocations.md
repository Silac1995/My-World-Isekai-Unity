---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/performance-chunk-allocations.html
fetched: 2026-05-05
section: performance
---

# Manage Chunk Allocations

## Overview

One challenge in the Entity Component System is chunk fragmentation—when 16 KiB chunks don't get used efficiently. As noted in the documentation, "Chunk fragmentation means that archetype chunks aren't being used efficiently."

The core issue: Unity stores components contiguously within chunks, but empty space wastes memory. Additionally, systems encounter cache misses when jumping between chunks. A concrete example illustrates the severity: 100,000 entities with unique archetypes could allocate over 1.5 GB of chunk data, mostly empty.

## Detecting Chunk Fragmentation

The Archetypes window helps identify fragmentation by displaying:
- Memory allocation per archetype
- Unused memory
- Chunk capacity (max 128 entities per chunk)
- Number of allocated chunks
- Hosted entities
- Unused entity space

Three fragmentation categories exist:

**Large entities:** Low unused space indicates efficient utilization, though few large entities fit per chunk due to their size.

**Fragmented archetypes:** High unused space suggests entities could pack better, but other factors (often incorrect shared component usage) split them across chunks.

**Too many archetypes:** Many archetypes with low chunk counts indicates excessive component variety—from adding/removing components for temporary buffs, for example.

## Addressing Large Entities

Rather than thinking of entities as objects (as in OOP), recognize that "an entity is little more than an index to a data structure that provides access to one specific collection of components." This enables decomposition: split character components across multiple entities organized by processing need (AI, physics, animation, rendering). Lighter entities pack more efficiently.

Alternative optimization: extract shared data into blob assets. Monitor changes via the Profiler to ensure gains outweigh indirect access overhead.

Note: Reducing entity size provides no benefit once a chunk reaches its 128-entity limit.

## Shared Components and Fragmentation

Shared components group entities sharing identical component values, reducing memory footprint. However, they cause fragmentation if misused. They're only beneficial when:
- Systems benefit from operating on individual subgroups
- Few subgroups exist
- Memory saved exceeds memory lost to additional chunks

Chunk components offer similar functionality without fragmentation penalties.

## Prefabs and Fragmentation

Prefab entities carry a `Prefab` component that queries implicitly exclude. During instantiation, the EntityManager removes this component from copies. This means prefabs have different archetypes than instantiated entities, creating separate 16 KiB chunks per prefab. Loading many different prefabs accumulates memory overhead quickly.

## Reducing Archetype Count

To minimize excessive archetypes:

**Tag components:** Each tag doubles archetype permutations; replace with enableable components instead.

**Temporary component addition/removal:** Use dynamic buffers for temporary data rather than modifying entity archetypes.

**Large entities:** Split overly broad entities by common usage patterns.

Consider grouping components (accepting unused fields) or reusing components across purposes.

---

## Outgoing Links

- https://docs.unity3d.com/Manual/TermsOfUse.html - Trademarks and terms of use
- https://unity.com/legal - Legal
- https://unity.com/legal/privacy-policy - Privacy Policy
- https://unity.com/legal/cookie-policy - Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information - Do Not Sell or Share My Personal Information
- https://docs.unity3d.com/Packages/com.unity.charactercontroller@latest - Character Controller package
- https://docs.unity3d.com/6000.4/Documentation/Manual/Profiler.html - Profiler
