---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/concepts-structural-changes.html
fetched: 2026-05-05
section: concepts
---

# Structural Changes Concepts

Operations that reorganize "chunks of memory or the contents of chunks in memory" are called **structural changes**. These are resource-intensive and can only execute on the main thread, not from jobs.

## Operations Considered Structural Changes

- Creating or destroying an entity
- Adding or removing components
- Setting a shared component value

## Create an Entity

When you create an entity, Unity either adds it to an existing chunk matching its archetype or creates a new chunk if none are available.

## Destroy an Entity

When you destroy an entity, Unity removes it from its chunk. If this creates a gap, the last entity in the chunk fills it. Empty chunks are deallocated.

## Add or Remove Components

Modifying an entity's components changes its archetype. Unity must move the entity to a chunk matching the new archetype. If no suitable chunk exists, one is created. Any gaps or empty chunks are handled through entity relocation or deallocation.

## Set a Shared Component Value

When you set a shared component value, Unity moves the entity to a chunk matching that value. If no suitable chunk exists, Unity creates one. Gaps and empty chunks are managed similarly to component changes.

**Note:** Setting a regular component value is *not* a structural change since it doesn't require entity movement.

## Sync Points

You cannot perform structural changes directly in jobs because this invalidates other scheduled jobs and creates a synchronization point (sync point).

A sync point "waits on the main thread for the completion of all jobs that have been scheduled so far." This limits worker thread utilization. Structural changes in ECS are the primary cause of sync points.

---

## Outgoing Hyperlinks

- http://docs.unity3d.com/ - docs.unity3d.com
- ../logo.svg - Logo
- ../index.html - Home
- concepts-archetypes.html#archetype-chunks - Archetype chunks
- concepts-entities.html - Entities
- concepts-components.html - Components
- components-shared-introducing.html - Shared components
- systems-manage-structural-changes.html - Manage structural changes
- concepts-archetypes.html - Archetype concepts
- systems-entity-command-buffers.html - Entity command buffers
- systems-update-order.html - System update order
- performance-sync-points.html - Managing sync points
- https://docs.unity3d.com/Manual/TermsOfUse.html - Trademarks and terms of use
- https://unity.com/legal - Legal
- https://unity.com/legal/privacy-policy - Privacy Policy
- https://unity.com/legal/cookie-policy - Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information - Do Not Sell or Share My Personal Information
