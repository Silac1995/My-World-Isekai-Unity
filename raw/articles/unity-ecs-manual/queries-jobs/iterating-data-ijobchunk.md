---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/iterating-data-ijobchunk.html
fetched: 2026-05-05
section: queries-jobs
---

# Iterate over chunks of data with IJobChunk

## Overview

To iterate through data at the chunk level of entities, implement `IJobChunk` within a system. When you schedule an `IJobChunk` job in the `OnUpdate` method of a system, "the system uses the entity query you pass to the schedule method to identify the chunks that it should pass to the job."

The job invokes your `Execute` method once for each matching chunk, and excludes those where no entities match the query because of enableable components. Within your job's `Execute` method, you can iterate over the data inside each chunk on an entity-by-entity basis.

## When to Use IJobChunk

> "Iterating with `IJobChunk` is more complicated and requires more code setup than using `IJobEntity`"

For most jobs performing a single iteration over a chunk's entities, `IJobEntity` is recommended. Consider using `IJobChunk` for:

- Jobs which do not iterate over each chunk's entities at all (such as gathering per-chunk statistics)
- Jobs which perform multiple iterations over a chunk's entities, or which iterate in an unusual order

## Additional Resources

- [Implementing IJobChunk](iterating-data-ijobchunk-implement.html)
- [ECS samples repository](https://github.com/Unity-Technologies/EntityComponentSystemSamples) — contains a HelloCube example demonstrating `IJobChunk` usage

---

## Outgoing Hyperlinks

- https://docs.unity3d.com/Manual/TermsOfUse.html — Trademarks and terms of use
- https://unity.com/legal — Legal
- https://unity.com/legal/privacy-policy — Privacy Policy
- https://unity.com/legal/cookie-policy — Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information — Do Not Sell or Share My Personal Information
- https://github.com/Unity-Technologies/EntityComponentSystemSamples — ECS samples repository
