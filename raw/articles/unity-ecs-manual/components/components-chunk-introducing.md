---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/components-chunk-introducing.html
fetched: 2026-05-05
section: components
---

# Chunk Components Introduction

## Overview

Chunk components function as per-chunk value storage rather than per-entity storage. Their primary benefit is optimization—you can implement per-chunk logic to determine whether to process behaviors for all entities within a chunk. For instance, a chunk component might track the bounds of all entities it contains, allowing you to check visibility and selectively process only on-screen chunks.

## Comparison with Shared Components

Chunk components share similarities with shared components but have distinct differences:

- **Ownership**: A chunk component value belongs conceptually to the chunk itself, not to individual entities within it.
- **Structural Changes**: Setting a chunk component value does not trigger a structural change, unlike some other modifications.
- **Deduplication**: Unlike shared components, Unity does not deduplicate identical chunk component values—chunks with matching values maintain separate copies.
- **Type Restrictions**: Chunk components are exclusively unmanaged; managed chunk components cannot be created.
- **Migration Behavior**: When entities move between chunks due to archetype changes or shared component value modifications, the chunk component values of neither source nor destination chunks are affected by these moves.

---

## Outgoing Links

- http://docs.unity3d.com/ — docs.unity3d.com
- https://docs.unity3d.com/Manual/TermsOfUse.html — Trademarks and terms of use
- https://unity.com/legal — Legal
- https://unity.com/legal/privacy-policy — Privacy Policy
- https://unity.com/legal/cookie-policy — Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information — Do Not Sell or Share My Personal Information
