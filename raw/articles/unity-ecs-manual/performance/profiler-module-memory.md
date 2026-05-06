---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/profiler-module-memory.html
fetched: 2026-05-05
section: performance
---

# Entities Memory Profiler Module Reference

The Entities Memory Profiler module shows memory usage for Archetypes on a per-frame basis. According to the documentation, this module provides "the same as the Archetypes window but you can investigate the data on a per-frame basis," enabling developers to identify memory spikes and performance-intensive actions.

## Chart Categories

The module displays two visualizations:

- **Allocated Memory**: Shows allocated memory in MB per frame
- **Unused Memory**: Shows unused memory in MB per frame

## Module Details Pane

When selected, the details pane displays Archetype information organized by World, including allocated and unused memory metrics. Selecting an Archetype reveals:

| Property | Description |
|----------|-------------|
| Archetype name | Hash identifier for finding the Archetype across sessions |
| Entities | Count of Entities within the selected Archetype |
| Unused Entities | Total available Entity slots minus active Entities |
| Chunks | Number of chunks the Archetype uses |
| Chunk Capacity | Maximum Entities with this Archetype per chunk |
| Components | Total Components and their memory allocation in KB (expandable) |
| External Components | Lists Chunk Components and Shared Components affecting the Archetype |

---

## Outgoing Links

- https://docs.unity3d.com/Manual/Profiler.html - Profiler window
- https://docs.unity3d.com/Manual/performance-memory-overview.html - Memory in Unity
- https://docs.unity3d.com/Manual/TermsOfUse.html - Trademarks and terms of use
- https://unity.com/legal - Legal
- https://unity.com/legal/privacy-policy - Privacy Policy
- https://unity.com/legal/cookie-policy - Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information - Do Not Sell or Share My Personal Information
