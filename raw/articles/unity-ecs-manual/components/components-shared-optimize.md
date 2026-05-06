---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/components-shared-optimize.html
fetched: 2026-05-05
section: components
---

# Optimize Shared Components

Shared components have different performance considerations compared to other component types. This page outlines shared component-specific performance considerations and optimization techniques.

## Use Unmanaged Shared Components

When possible, prefer unmanaged shared components over managed shared components. Unity stores unmanaged shared components in a location accessible to Burst compiled code via the unmanaged shared component APIs (such as `SetUnmanagedSharedComponentData`). This approach delivers improved performance relative to managed components.

## Avoid Frequent Updates

Updating a shared component value for an entity constitutes a structural change, which causes Unity to relocate the entity to a different chunk. To maintain performance, minimize the frequency of these updates.

## Avoid Lots of Unique Shared Component Values

All entities within a chunk must share identical shared component values. When you assign unique shared component values to numerous entities, those entities become distributed across many sparsely populated chunks.

Consider this scenario: 500 entities of an archetype with a shared component, each having distinct shared component values, results in each entity occupying its own chunk. This approach squanders chunk space and requires looping through all 500 chunks to access every entity of that archetype. This eliminates the performance advantages of the ECS chunk layout architecture. To prevent this issue, consolidate unique shared component values. If those 500 entities instead share only ten unique shared component values, Unity can store them in approximately ten chunks.

Exercise caution with archetypes containing multiple shared component types. Because all entities in an archetype chunk must have identical combinations of shared component values, such archetypes are vulnerable to fragmentation.

**Note:** To assess chunk fragmentation, examine chunk utilization within the Archetypes window.

---

## Outgoing Links

- https://docs.unity3d.com/Packages/com.unity.burst@latest/index.html - Burst compiled documentation
- https://docs.unity3d.com/Manual/TermsOfUse.html - Trademarks and terms of use
- https://unity.com/legal - Legal
- https://unity.com/legal/privacy-policy - Privacy Policy
- https://unity.com/legal/cookie-policy - Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information - Do Not Sell or Share My Personal Information
