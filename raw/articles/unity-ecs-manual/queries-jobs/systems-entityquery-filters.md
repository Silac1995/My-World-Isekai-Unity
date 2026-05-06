---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/systems-entityquery-filters.html
fetched: 2026-05-05
section: queries-jobs
---

# EntityQuery Filters

## Overview

To refine entity selection, you can apply filters to exclude entities based on specific criteria. The documentation outlines three primary filtering approaches:

- **Shared component filter**: Narrows results based on specific shared component values
- **Change filter**: Selects entities whose component values have been modified
- **Enableable components**: Manages runtime enabled/disabled states

Filters persist until you invoke `ResetFilter()` on the query object.

For better performance when filters aren't needed, use methods ending in `IgnoreFilter`—for instance, `IsEmptyIgnoreFilter()` outperforms its filtered counterpart.

## Using Shared Component Filters

Include the shared component in your `EntityQuery` and call `SetSharedComponentFilter()`, passing a struct of the matching `ISharedComponent` type with values to select. All values must match exactly. You may add up to two different shared components to a single filter.

Changing filters doesn't update existing arrays from `ToComponentDataArray<T>()` or `ToEntityArray()` methods—you must regenerate these arrays.

### Example Implementation

```csharp
struct SharedGrouping : ISharedComponentData
{
    public int Group;
}

[RequireMatchingQueriesForUpdate]
partial class ImpulseSystem : SystemBase
{
    EntityQuery query;

    protected override void OnCreate()
    {
        query = new EntityQueryBuilder(Allocator.Temp)
            .WithAllRW<ObjectPosition>()
            .WithAll<Displacement, SharedGrouping>()
            .Build(this);
    }

    protected override void OnUpdate()
    {
        // Count all entities with required components
        query.ResetFilter();
        int unfilteredCount = query.CalculateEntityCount();
        
        // Count only entities where SharedGrouping=1
        query.SetSharedComponentFilter(new SharedGrouping { Group = 1 });
        int filteredCount = query.CalculateEntityCount();
        
        // More efficient variant ignoring active filters
        int ignoreFilterCount = query.CalculateEntityCountWithoutFiltering();
    }
}
```

## Using Change Filters

Apply `SetChangedVersionFilter()` to update entities only when specific component values change. This example filters for chunks where another system modified the `ObjectPosition` component:

```csharp
EntityQuery query;

protected override void OnCreate()
{
    query = new EntityQueryBuilder(Allocator.Temp)
        .WithAllRW<LocalToWorld>()
        .WithAll<ObjectPosition>()
        .Build(this);
    query.SetChangedVersionFilter(typeof(ObjectPosition));
}
```

**Important**: Change filters operate on entire archetype chunks, not individual entities. The filter detects whether any system with write access ran, regardless of actual data modifications. Always declare read-only access to components you don't modify to optimize efficiency.

## Filtering by Enableable Components

Enableable components allow runtime enabling and disabling without archetype changes. For `EntityQuery` matching purposes, disabled components are treated as absent:

- Entities with disabled component `T` won't match `WithAll<T>()`
- Entities with disabled component `T` will match `WithNone<T>()`

Most `EntityQuery` operations automatically exclude entities whose enableable states violate query requirements. To bypass this behavior, use `IgnoreFilter` variants or pass `EntityQueryOptions.IgnoreComponentEnabledState` during query creation.

## Related Resources

- [Create an EntityQuery](systems-entityquery-create.html)
- [Shared components](components-shared.html)
- [Change filter API](../api/Unity.Entities.EntityQuery.SetChangedVersionFilter.html)
- [Enableable components](components-enableable.html)

---

## Outgoing Links

- https://docs.unity3d.com/ (docs.unity3d.com)
- https://docs.unity3d.com/Manual/TermsOfUse.html (Trademarks and terms of use)
- https://unity.com/legal (Legal)
- https://unity.com/legal/privacy-policy (Privacy Policy)
- https://unity.com/legal/cookie-policy (Cookie Policy)
- https://unity.com/legal/do-not-sell-my-personal-information (Do Not Sell or Share My Personal Information)
