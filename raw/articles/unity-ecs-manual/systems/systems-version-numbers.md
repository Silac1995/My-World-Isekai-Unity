---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/systems-version-numbers.html
fetched: 2026-05-05
section: systems
---

# Version Numbers in Unity ECS

## Overview

Version numbers, also called generations, track changes in ECS architecture components. They enable efficient optimization by allowing developers to skip processing when data hasn't changed since the previous frame. Quick version checks improve application performance.

## Version Number Structure

All version numbers are 32-bit signed integers that increase until they wrap around. To properly compare versions, use equality (`==`) or inequality (`!=`) operators rather than relational operators.

The correct comparison method is:

```
bool VersionBIsMoreRecent = (VersionB - VersionA) > 0;
```

Version increments vary—there's no guaranteed increase amount.

## Entity Version Numbers

An `EntityId` contains an index and version number. Since ECS recycles indices, it increments the version in `EntityManager` when destroying entities. A mismatch indicates the entity no longer exists.

Example use case: Call `ComponentDataFromEntity.Exists` before fetching an enemy position through `EntityId` to verify the entity still exists.

## World Version Numbers

World version numbers increase when managers (such as systems) are created or destroyed.

## System Version Numbers

`EntityDataManager.GlobalVersion` increments before each system update. Use this with `System.LastSystemVersion`, which captures `GlobalVersion` after system updates.

### Chunk.ChangeVersion

This array stores `EntityDataManager.GlobalVersion` values for each component type, indicating when component arrays were last accessed as writeable. It signals potential changes, not guaranteed changes.

Shared components cannot be accessed as writeable and their version numbers serve no practical purpose.

When using `WithChangeFilter()` in `Entities.ForEach`, ECS compares `Chunk.ChangeVersion` against `System.LastSystemVersion`, processing only chunks modified since the system last ran.

**Important:** Don't manually call another system's `Update()` method from within `OnUpdate()` if both process entity data, particularly with `EntityQuery.SetChangedVersionFilter()`. This guidance excludes "pass-through" systems like `ComponentSystemGroup`.

## Non-Shared Component Version Numbers

`EntityManager.m_ComponentTypeOrderVersion[]` increments when iterators become invalid—situations potentially modifying component type arrays.

Use case: Update per-chunk bounding boxes only when type order versions change for relevant components.

## Shared Component Version Numbers

`SharedComponentDataManager.m_SharedComponentVersion[]` increases during structural changes affecting chunks referencing that shared component.

Use case: Recalculate entity counts per shared component only when corresponding version numbers change.

---

## Outgoing Links

- [EntityManager API](https://docs.unity3d.com/api/Unity.Entities.EntityManager.html)
- [Unity Documentation Home](http://docs.unity3d.com/)
- [Terms of Use](https://docs.unity3d.com/Manual/TermsOfUse.html)
- [Legal](https://unity.com/legal)
- [Privacy Policy](https://unity.com/legal/privacy-policy)
- [Cookie Policy](https://unity.com/legal/cookie-policy)
- [Do Not Sell or Share My Personal Information](https://unity.com/legal/do-not-sell-my-personal-information)
