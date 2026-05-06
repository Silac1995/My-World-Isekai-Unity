---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/systems-entitymanager.html
fetched: 2026-05-05
section: systems
---

# Manage Structural Changes with EntityManager

## Overview

The `EntityManager` API provides utility methods to create, read, update, and destroy entities within a project. Each world contains an `EntityManager` that manages all entities in that world.

While `SystemAPI` methods are preferred for accessing entity data, `EntityManager` is particularly useful for managing structural changes on the main thread.

## Structural Changes

Certain `EntityManager` operations trigger structural changes, which require all running jobs to complete first. This creates a synchronization point that blocks the main thread, potentially limiting CPU core utilization and impacting performance.

### EntityManager vs Entity Command Buffers

**EntityManager approach:**
- Use for instant structural changes on the main thread
- More efficient than entity command buffers for immediate operations
- Cannot be used within jobs (incompatible with `IJobChunk`, `IJobEntity`)
- Only `CreateEntity`, `CreateArchetype`, and `Instantiate` work inside `SystemAPI.Query` loops

**Entity Command Buffer (ECB) approach:**
- Queue structural changes for execution at a specific point
- Compatible with job types
- Must execute on main thread after jobs complete
- Has distinct performance considerations

## Key EntityManager Methods

| Method | Description |
|--------|-------------|
| `CreateEntity` | Creates a new entity |
| `Instantiate` | Creates entity with copied components from existing entity |
| `DestroyEntity` | Destroys an existing entity |
| `AddComponent<T>` | Adds component type T to entity |
| `RemoveComponent<T>` | Removes component type T from entity |
| `HasComponent<T>` | Returns true if entity has component type T |

All listed methods are structural change operations.

## Related Resources

- [EntityManager API Documentation](../api/Unity.Entities.EntityManager.html)
- [Structural Changes Overview](concepts-structural-changes.html)
- [Entity Command Buffer Overview](systems-entity-command-buffers.html)
- [Manage Structural Changes Introduction](systems-manage-structural-changes-intro.html)

---

## Outgoing Hyperlinks

- https://docs.unity3d.com/Manual/TermsOfUse.html – Trademarks and terms of use
- https://unity.com/legal – Legal
- https://unity.com/legal/privacy-policy – Privacy Policy
- https://unity.com/legal/cookie-policy – Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information – Do Not Sell or Share My Personal Information
