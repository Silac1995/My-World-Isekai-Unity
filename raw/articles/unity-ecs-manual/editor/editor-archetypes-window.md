---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/editor-archetypes-window.html
fetched: 2026-05-05
section: editor
---

# Archetypes Window Reference

The Archetypes window displays memory allocation information for each archetype in your Entity Component System (ECS) project across all worlds.

## Accessing the Window

Navigate to **Window > Entities > Archetypes** to open this tool.

## Overview

This window presents a list of archetypes with their associated memory usage data. When you select an archetype, detailed information appears in the right panel.

## Displaying Empty Archetypes

By default, the window excludes empty archetypes. To include archetypes with zero entities, access the More menu (⋮) and enable "Show Empty Archetypes." These often result from incrementally adding components using `AddComponent`.

## Archetype Information Panel

When an archetype is selected, the following details are displayed:

| Property | Description |
|----------|-------------|
| Archetype name | A hash identifier usable across future Unity sessions |
| Entities | Count of entities within the archetype |
| Unused Entities | Available entity slots minus active entities |
| Chunks | Number of chunks this archetype uses |
| Chunk Capacity | Maximum entities with this archetype per chunk |
| Components | Total component count and KB memory allocation; expandable for per-component details |
| External Components | Lists Chunk and Shared components affecting this archetype |

## Related Resources

- [Archetype user manual](concepts-archetypes.html)
- [Chunk user manual](concepts-archetypes.html#archetype-chunks)
- [Shared Components user manual](components-shared.html)
- [Chunk Components user manual](components-chunk.html)

---

## Outgoing Links

- https://docs.unity3d.com/Manual/TermsOfUse.html – Trademarks and terms of use
- https://unity.com/legal – Legal
- https://unity.com/legal/privacy-policy – Privacy Policy
- https://unity.com/legal/cookie-policy – Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information – Do Not Sell or Share My Personal Information
