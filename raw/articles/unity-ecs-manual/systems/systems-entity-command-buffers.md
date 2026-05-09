---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/systems-entity-command-buffers.html
fetched: 2026-05-05
section: systems
---

# Entity Command Buffer Overview

An entity command buffer (ECB) is a queue of thread-safe commands that can be recorded and played back later. The primary use cases include scheduling structural changes from jobs and performing changes on the main thread after jobs complete. ECBs can also delay changes or replay a set of modifications multiple times.

## Command Methods

The `EntityCommandBuffer` API mirrors `EntityManager` methods, including:

- `CreateEntity(EntityArchetype)`: Registers command to create an entity with specified archetype
- `DestroyEntity(Entity)`: Registers command to destroy an entity
- `SetComponent<T>(Entity, T)`: Registers command to set component value
- `AddComponent<T>(Entity)`: Registers command to add component type
- `RemoveComponent<T>(EntityQuery)`: Registers command to remove component from matching entities

## Temporary Entities

Entities created via `CreateEntity()` and `Instantiate()` don't fully exist until playback, but can be referenced within the same buffer. Valid uses include:

1. Commands targeting temporary entities created earlier in the same buffer
2. Unmanaged component values containing references to temporary entities (including dynamic buffers with entity fields)

During playback, references to temporary entities are automatically replaced with real entities. **Note:** "There is no way to determine which 'real' entity corresponds to a given temporary entity after its command buffer has been played back."

Invalid operations that throw exceptions:
- Passing temporary entities to `EntityManager` methods
- Referencing temporary entities across different command buffers

## Safety Checks

ECBs include job safety handles (Editor only, not in player builds). Safety exceptions occur when:

- Accessing incomplete scheduled jobs using ECBs
- Scheduling dependent jobs without proper dependency declarations

**Best practice:** Use separate ECBs for distinct jobs to avoid interleaved commands from overlapping sort keys.

---

## Outgoing Links

- https://docs.unity3d.com/6000.0/Documentation/Manual/JobSystemNativeContainer.html - Native containers documentation
- https://docs.unity3d.com/6000.0/Documentation/Manual/JobSystemJobDependencies.html - Job dependencies documentation
- https://docs.unity3d.com/Manual/TermsOfUse.html - Trademarks and terms of use
- https://unity.com/legal - Legal
- https://unity.com/legal/privacy-policy - Privacy Policy
- https://unity.com/legal/cookie-policy - Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information - Do Not Sell or Share My Personal Information
