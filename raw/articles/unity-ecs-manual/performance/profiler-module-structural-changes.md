---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/profiler-module-structural-changes.html
fetched: 2026-05-05
section: performance
---

# Entities Structural Changes Profiler Module Reference

## Overview

The Entities Structural Changes Profiler module tracks when the ECS framework creates or destroys entities and adds or removes components. This monitoring is valuable because "when a structural change happens, the ECS framework moves an Entity and a full copy of its data to a different Archetype, which is a performance-intensive operation."

## Chart Categories

The module displays four distinct chart categories measuring the time required for:

- **Creating Entities**
- **Destroying Entities**
- **Adding Components**
- **Removing Components**

## Module Details Pane

When selected, the details pane shows additional information organized in a table format:

| Property | Description |
|----------|-------------|
| Structural Changes | A list of structural changes ordered by World, expandable to reveal the specific systems that triggered them |
| Cost (ms) | The duration of the structural change in milliseconds |
| Count | The frequency of the structural change occurrence |

## Related Resources

- https://docs.unity3d.com/Manual/Profiler.html - Profiler window
- concepts-structural-changes.html - Structural changes concepts
