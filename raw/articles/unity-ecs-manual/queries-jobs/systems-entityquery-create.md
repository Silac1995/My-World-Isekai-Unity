---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/systems-entityquery-create.html
fetched: 2026-05-05
section: queries-jobs
---

# Create an EntityQuery

## Overview

To establish an entity query, pass component types to the `EntityQueryBuilder` helper. This example demonstrates creating an `EntityQuery` that identifies all entities containing both `ObjectRotation` and `ObjectRotationSpeed` components:

```csharp
EntityQuery query = new EntityQueryBuilder(Allocator.Temp)
    .WithAllRW<ObjectRotation>()
    .WithAll<ObjectRotationSpeed>()
    .Build(this);
```

The query employs `EntityQueryBuilder.WithAllRW<T>()` to indicate that the system modifies `ObjectRotation`. Specify read-only access whenever feasible, as this reduces constraints and allows the job scheduler to operate more efficiently.

## Specify Which Archetypes the System Selects

Queries match only archetypes containing your specified components. The `EntityQueryBuilder` provides these methods:

- **`WithAll<T>()`**: An entity's archetype must contain all required query components, which must be enabled.
- **`WithAny<T>()`**: An entity's archetype must contain at least one optional component, which must be enabled.
- **`WithNone<T>()`**: An entity's archetype must either lack excluded components or have them disabled.
- **`WithDisabled<T>()`**: An entity's archetype must contain this component in a disabled state.
- **`WithAbsent<T>()`**: An entity's archetype must not contain specified components.
- **`WithPresent<T>()`**: An entity's archetype must contain specified components regardless of enabled status.

### Example with Exclusion

This query includes archetypes with `ObjectRotation` and `ObjectRotationSpeed` but excludes those containing `Static`:

```csharp
EntityQuery query = new EntityQueryBuilder(Allocator.Temp)
    .WithAllRW<ObjectRotation>()
    .WithAll<ObjectRotationSpeed>()
    .WithNone<Static>()
    .Build(this);
```

### Important Note

"To handle optional components, use the `ArchetypeChunk.Has<T>()` method to determine whether a chunk contains the optional component or not."

### Specialized Archetype Options

Use `EntityQueryBuilder.WithOptions()` to locate specialized archetypes:

- **`IncludePrefab`**: Includes archetypes containing the Prefab tag component.
- **`IncludeDisabledEntities`**: Includes archetypes containing the `Disabled` tag component.
- **`FilterWriteGroup`**: Includes only entities with components in an explicitly included `WriteGroup`, excluding entities with additional components from the same group.

## Filter by Write Group

This example demonstrates components within the same `WriteGroup` based on `CharacterComponent`:

```csharp
public struct CharacterComponent : IComponentData { }

[WriteGroup(typeof(CharacterComponent))]
public struct LuigiComponent : IComponentData { }

[WriteGroup(typeof(CharacterComponent))]
public struct MarioComponent : IComponentData { }

[RequireMatchingQueriesForUpdate]
public partial class ECSSystem : SystemBase
{
    protected override void OnCreate()
    {
        var query = new EntityQueryBuilder(Allocator.Temp)
            .WithAllRW<CharacterComponent>()
            .WithAll<MarioComponent>()
            .WithOptions(EntityQueryOptions.FilterWriteGroup)
            .Build(this);
    }

    protected override void OnUpdate()
    {
        throw new NotImplementedException();
    }
}
```

This query excludes entities with both `LuigiComponent` and `MarioComponent` since `LuigiComponent` isn't explicitly included.

Write groups offer efficiency advantages, allowing you to extend existing systems without modifying other systems' queries. If you've defined `CharacterComponent` and `LuigiComponent` in a library, adding `MarioComponent` to the same write group changes how `CharacterComponent` updates for entities with `MarioComponent`, while the original system continues updating it for other entities.

## Execute the Query

Execute entity queries typically when scheduling jobs. Alternatively, call these `EntityQuery` methods to return specific data:

- **`ToEntityArray`**: Returns an array of selected entities.
- **`ToComponentDataArray`**: Returns an array of type `T` components for selected entities.
- **`CreateArchetypeChunkArray`**: Returns all chunks containing selected entities. Since queries operate on identical archetypes, shared component values, and change filters within chunks, the returned chunks contain the same entity set as `ToEntityArray`.

Asynchronous versions are available, scheduling jobs to gather data. These variants may return `NativeList` instead of `NativeArray` to support enableable components: `ToEntityListAsync`, `ToComponentDataListAsync`, and `CreateArchetypeChunkArrayAsync`.

## Additional Information

- [EntityQuery filters](systems-entityquery-filters.html)

---

## Outgoing Hyperlinks

- https://docs.unity3d.com/Manual/TermsOfUse.html - Trademarks and terms of use
- https://unity.com/legal - Legal
- https://unity.com/legal/privacy-policy - Privacy Policy
- https://unity.com/legal/cookie-policy - Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information - Do Not Sell or Share My Personal Information
- https://docs.unity3d.com - docs.unity3d.com
