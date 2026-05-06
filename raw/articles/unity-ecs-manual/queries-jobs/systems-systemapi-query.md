---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/systems-systemapi-query.html
fetched: 2026-05-05
section: queries-jobs
---

# Iterate over Component Data with SystemAPI.Query

To iterate through a collection of data on the main thread, you can use the `SystemAPI.Query<T>` method in both `ISystem` and `SystemBase` system types. It uses C#'s idiomatic `foreach` syntax.

## Supported Type Parameters

The method supports overloading with up to seven type parameters:

* `IAspect`
* `IComponentData`
* `ISharedComponentData`
* `DynamicBuffer<T>`
* `RefRO<T>`
* `RefRW<T>`
* `EnabledRefRO<T>` where T : `IEnableableComponent`, `IComponentData`
* `EnabledRefRW<T>` where T : `IEnableableComponent`, `IComponentData`

## SystemAPI.Query Implementation

"Whenever you invoke `SystemAPI.Query<T>`, the source generator solution creates an `EntityQuery` field on the system itself."

The implementation automatically:
* Caches an `EntityQuery` with the queried types and their access modes
* Replaces the invocation in a `foreach` statement with an enumerator during compilation
* Caches required type handles
* Injects `TypeHandle.Update()` calls before each `foreach`
* Completes all necessary read and read-write dependencies

## Query Data Example

```csharp
public partial struct MyRotationSpeedSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;

        foreach (var (transform, speed) in SystemAPI.Query<RefRW<LocalTransform>, RefRO<RotationSpeed>>())
            transform.ValueRW = transform.ValueRO.RotateY(speed.ValueRO.RadiansPerSecond * deltaTime);
    }
}
```

Since the example modifies `LocalTransform` data, it uses `RefRW<T>` for read-write access. The `RotationSpeed` data is only read, so `RefRO<T>` is used. Note that using `RefRO<T>` is optional—you can use the component type directly for read-only access.

The `ValueRW` and `ValueRO` properties return component references. "When called, `ValueRW` conducts a safety check for read-write access, and `ValueRO` does the same for read-only access."

## Accessing Entities in the Foreach Statement

`Unity.Entities.Entity` is not a supported type parameter. To access the entity, use `WithEntityAccess()`:

```csharp
foreach (var (transform, speed, entity) in SystemAPI.Query<RefRW<LocalToWorld>, RefRO<RotationSpeed>>().WithEntityAccess())
{
    // Do stuff;
}
```

Note that the `Entity` argument comes last in the returned tuple.

## Known Limitations

### Dynamic Buffer Read-Only Limitation

"Regarding `DynamicBuffer<T>` type parameters in `SystemAPI.Query<T>`, they are read-write access by default. However, if you want read-only access, you have to create your own implementation":

```csharp
var bufferHandle = state.GetBufferTypeHandle<MyBufferElement>(isReadOnly: true);
var myBufferElementQuery = SystemAPI.QueryBuilder().WithAll<MyBufferElement>().Build();
var chunks = myBufferElementQuery.ToArchetypeChunkArray(Allocator.Temp);

foreach (var chunk in chunks)
{
    var numEntities = chunk.Count;
    var bufferAccessor = chunk.GetBufferAccessorRO(ref bufferHandle);

    for (int j = 0; j < numEntities; j++)
    {
        var dynamicBuffer = bufferAccessor[j];
        // Read from dynamicBuffer and perform various operations
    }
}
```

### Reusing SystemAPI.Query

You cannot store `SystemAPI.Query<T>` in a variable for use in multiple `foreach` statements. "The source-generation solution doesn't know at compile-time what `EntityQuery` to generate and cache, which type handles to call `Update` on, nor which dependencies to complete."

---

## Outgoing Hyperlinks

* https://docs.unity3d.com/Packages/com.unity.entities@latest/api/Unity.Entities.SystemAPI.Query.html - SystemAPI.Query API reference
* https://docs.unity3d.com/Packages/com.unity.entities@latest/manual/systems-isystem.html - ISystem documentation
* https://docs.unity3d.com/Packages/com.unity.entities@latest/manual/systems-systembase.html - SystemBase documentation
* https://docs.unity3d.com/Packages/com.unity.entities@latest/api/Unity.Entities.RefRW-1.ValueRW.html - RefRW<T>.ValueRW API
* https://docs.unity3d.com/Packages/com.unity.entities@latest/api/Unity.Entities.RefRW-1.ValueRO.html - RefRW<T>.ValueRO API
* https://docs.unity3d.com/Packages/com.unity.entities@latest/api/Unity.Entities.RefRO-1.ValueRO.html - RefRO<T>.ValueRO API
* https://docs.unity3d.com/Packages/com.unity.entities@latest/api/Unity.Entities.QueryEnumerable-1.WithEntityAccess.html - WithEntityAccess API
* http://docs.unity3d.com/ - Unity Documentation Home
* https://docs.unity3d.com/Manual/TermsOfUse.html - Trademarks and terms of use
* https://unity.com/legal - Legal
* https://unity.com/legal/privacy-policy - Privacy Policy
* https://unity.com/legal/cookie-policy - Cookie Policy
* https://unity.com/legal/do-not-sell-my-personal-information - Do Not Sell or Share My Personal Information
