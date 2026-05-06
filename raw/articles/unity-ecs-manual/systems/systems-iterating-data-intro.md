---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/systems-iterating-data-intro.html
fetched: 2026-05-05
section: systems
---

# Iterate over component data

Iterating over data represents one of the most common tasks when creating a system. A system typically processes a set of entities, reads data from one or more components, performs a calculation, and then writes the result to another component.

The most efficient way to iterate over entities and components is in a job that processes the components in order. This approach takes advantage of the processing power from all available cores and data locality to avoid CPU cache misses.

## Iteration Methods

This section explains how to iterate over entity data in the following ways:

| Topic | Description |
|-------|-------------|
| Iterate over component data with SystemAPI.Query | Iterate through a collection of data on the main thread. |
| Iterate over component data with IJobEntity | Write once and create multiple schedules with `IJobEntity`. |
| Iterate over chunks of data with IJobChunk | Iterate over archetype chunks that contain matching entities with `IJobChunk`. |
| Iterate manually over data | Manually iterate over entities or archetype chunks. |
| Query data with an entity query | Find component data with entity queries. |
| Look up arbitrary data | Access arbitrary data without using an entity query. |

## Additional resources

You can also use the `EntityQuery` class to construct a view of your data that contains only the specific data you need for a given algorithm or process. Many of the iteration methods in the list above use an `EntityQuery`, either explicitly or internally.

---

## Outgoing Links

- http://docs.unity3d.com/ - docs.unity3d.com
- https://docs.unity3d.com/Manual/TermsOfUse.html - Trademarks and terms of use
- https://unity.com/legal - Legal
- https://unity.com/legal/privacy-policy - Privacy Policy
- https://unity.com/legal/cookie-policy - Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information - Do Not Sell or Share My Personal Information
