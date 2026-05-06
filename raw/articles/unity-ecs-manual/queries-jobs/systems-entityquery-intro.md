---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/systems-entityquery-intro.html
fetched: 2026-05-05
section: queries-jobs
---

# EntityQuery Overview

## Main Concept

An `EntityQuery` identifies [archetypes](../concepts-archetypes.html) containing a designated set of component types. It then collects the archetype's chunks into an array for system processing.

## How Matching Works

When a query targets component types A and B, it gathers chunks from all archetypes containing those components, regardless of additional component types present. For instance, an archetype with components A, B, and C would satisfy this query.

## Primary Use Cases

`EntityQuery` enables developers to:

- Execute jobs that process matched entities and their components
- Retrieve a `NativeArray` containing all matched entities
- Obtain a `NativeArray` of selected entities organized by component type

## Parallel Arrays

Entity and component arrays returned by `EntityQuery` maintain parallel structure, meaning identical index values correspond to the same entity across all arrays.

## Editor Representation

In the Editor, queries are indicated by this icon: ![Query icon - a hexagon with an arrow inside.](images/editor-query-icon.png)

This symbol appears within the [Entities windows and Inspectors](editor-workflows.html). The [Query window](editor-query-window.html) provides visibility into Components and Entities matching a selected query.

---

## Outgoing Links

- https://docs.unity3d.com/Manual/TermsOfUse.html - Trademarks and terms of use
- https://unity.com/legal - Legal
- https://unity.com/legal/privacy-policy - Privacy Policy
- https://unity.com/legal/cookie-policy - Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information - Do Not Sell or Share My Personal Information
