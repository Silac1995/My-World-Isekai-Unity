---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/systems-systemapi.html
fetched: 2026-05-05
section: systems
---

# SystemAPI Overview

`SystemAPI` is a class providing caching and utility methods for accessing entity world data. It operates in non-static methods within `SystemBase` and `ISystem` (with `ref SystemState` parameter).

## Core Capabilities

You can use `SystemAPI` to:

- **Iterate through data**: Retrieve per-entity data matching a query
- **Query building**: Obtain cached `EntityQuery` for job scheduling
- **Access data**: Retrieve component data, buffers, and `EntityStorageInfo`
- **Access singletons**: Find single data instances

All `SystemAPI` methods map directly to their containing system. The class uses stub methods calling `ThrowCodeGenException`, replaced by Roslyn source generators with correct lookups. This means `SystemAPI` cannot be called outside supported contexts.

Use IDEs supporting source generation (Visual Studio 2022+, Rider 2021.3.3+) and select **Go To Definition** to inspect generated code. Systems must be marked `partial`.

## Iterating Through Data

The `Query` method enables main-thread iteration using C#'s idiomatic `foreach` syntax in both `ISystem` and `SystemBase` types. See the `SystemAPI.Query` documentation for details.

## Query Building

`QueryBuilder` retrieves an `EntityQuery` for job scheduling or query information, following `EntityQueryBuilder` syntax. The method caches data:

```csharp
/// SystemAPI call
SystemAPI.QueryBuilder().WithAll<HealthData>().Build();

/// ECS compiles it like so:
EntityQuery query;
public void OnCreate(ref SystemState state){
    query = new EntityQueryBuilder(state.WorldUpdateAllocator).WithAll<HealthData>().Build(ref state);
}

public void OnUpdate(ref SystemState state){
    query;
}
```

## Access Data

`SystemAPI` provides utility methods for world data access:

| Data Type | API |
|-----------|-----|
| Component data | `GetComponentLookup`, `GetComponent`, `SetComponent`, `HasComponent`, `IsComponentEnabled`, `SetComponentEnabled` |
| Buffers | `GetBufferLookup`, `GetBuffer`, `HasBuffer`, `IsBufferEnabled`, `SetBufferEnabled` |
| EntityInfo | `GetEntityStorageInfoLookup`, `Exists` |
| Aspects | `GetAspect` |
| Handles | `GetEntityTypeHandle`, `GetComponentTypeHandle`, `GetBufferTypeHandle`, `GetSharedComponentTypeHandle` |

Methods cache in `OnCreate` and call `.Update` before access. ECS synchronizes calls before lookup access - for instance, `SystemAPI.SetBuffer<MyElement>` completes all jobs writing to `MyElement`. Calls like `GetEntityTypeHandle` and `GetBufferLookup` don't trigger syncs.

Pass data to jobs like `IJobEntity` and `IJobChunk` without main-thread syncing:

```csharp
new MyJob{healthLookup=SystemAPI.GetComponentLookup<HealthData>(isReadOnly:true)};
```

Since ECS caches this data, direct `OnUpdate` calls work without manual setup (equivalent to caching in `OnCreate` and updating before use).

## Access Singletons

Singleton methods verify single data instances without syncing, offering performance advantages. Methods include:

| Data Type | API |
|-----------|-----|
| Singleton component data | `GetSingleton`, `TryGetSingleton`, `GetSingletonRW`, `TryGetSingletonRW`, `SetSingleton` |
| Singleton entity data | `GetSingletonEntity`, `TryGetSingletonEntity` |
| Singleton buffers | `GetSingletonBuffer`, `TryGetSingletonBuffer` |
| All singletons | `HasSingleton` |

These methods avoid the sync requirement of `EntityManager.GetComponentData<MyComponent>`, where all writing jobs complete.

## Managed Versions of SystemAPI

The `SystemAPI.ManagedAPI` namespace exposes managed method versions for accessing managed components:

| Data Type | API |
|-----------|-----|
| Component data | `ManagedAPI.GetComponent`, `ManagedAPI.HasComponent`, `ManagedAPI.IsComponentEnabled`, `ManagedAPI.SetComponentEnabled` |
| Handles | `ManagedAPI.GetSharedComponentTypeHandle` |

Singleton managed APIs:

| Data Type | API |
|-----------|-----|
| Singleton component data | `ManagedAPI.GetSingleton`, `ManagedAPI.TryGetSingleton` |
| Singleton entity data | `ManagedAPI.GetSingletonEntity`, `ManagedAPI.TryGetSingletonEntity` |
| All singletons | `ManagedAPI.HasSingleton` |

The `ManagedAPI.UnityEngineComponent` method extends `SystemAPI.Query` to query MonoBehaviours, scriptable objects, and UnityEngine components like `Transform`:

```csharp
foreach (var transformRef in SystemAPI.Query<SystemAPI.ManagedAPI.UnityEngineComponent<Transform>>())
    transformRef.Value.Translate(0,1,0);
```

---

## Outgoing Links

- https://docs.unity3d.com/ - docs.unity3d.com
- ../api/Unity.Entities.SystemAPI.html - `SystemAPI` API documentation
- concepts-worlds.html - world documentation
- systems-systembase.html - `SystemBase` documentation
- systems-isystem.html - `ISystem` documentation
- systems-entityquery.html - EntityQuery documentation
- ../api/Unity.Entities.SystemAPI.GetEntityStorageInfoLookup.html#Unity_Entities_SystemAPI_GetEntityStorageInfoLookup - EntityStorageInfo documentation
- https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/source-generators-overview - Roslyn source generators overview
- components-singleton.html - singletons documentation
- systems-systemapi-query.html - `SystemAPI.Query` overview documentation
- systems-entityquery-create.html - EntityQueryBuilder documentation
- components-managed.html - managed components documentation
- systems-systemapi-query.html - `SystemAPI.Query` documentation
- https://docs.unity3d.com/Manual/TermsOfUse.html - Trademarks and terms of use
- https://unity.com/legal - Legal
- https://unity.com/legal/privacy-policy - Privacy Policy
- https://unity.com/legal/cookie-policy - Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information - Do Not Sell or Share My Personal Information
