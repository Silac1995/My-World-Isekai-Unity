---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/allocators-world-update.html
fetched: 2026-05-05
section: performance
---

# World Update Allocator

The world update allocator is a rewindable allocator that ECS rewinds during every world update. Every world contains double rewindable allocators that are created when the world is initiated.

The `WorldUpdateAllocatorResetSystem` system switches the double rewindable allocators in every world update. After an allocator swaps in, it rewinds the allocator. Because of this, "the lifetime of an allocation from a world update allocator spans two frames." You don't need to manually free the allocations, so there isn't any memory leakage.

You can pass allocations from the world update allocator into a job. You can access the world update allocator through:

- `World.UpdateAllocator`
- `ComponentSystemBase.WorldUpdateAllocator`
- `SystemState.WorldUpdateAllocator`

## Accessing Through World

```csharp
// Access world update allocator through World.UpdateAllocator.
public void WorldUpdateAllocatorFromWorld_works()
{
    // Create a test world.
    World world = new World("Test World");

    // Create a native array using world update allocator.
    var nativeArray = CollectionHelper.CreateNativeArray<int>(5, world.UpdateAllocator.ToAllocator);
    for (int i = 0; i < 5; i++)
    {
        nativeArray[i] = i;
    }

    Assert.AreEqual(nativeArray[3], 3);

    // Dispose the test world.
    world.Dispose();
}
```

## Accessing Through SystemBase

```csharp
// Access world update allocator through SystemBase.WorldUpdateAllocator.
unsafe partial class AllocateNativeArraySystem : SystemBase
{
    public NativeArray<int> nativeArray = default;

    protected override void OnUpdate()
    {
        // Get world update allocator through SystemBase.WorldUpdateAllocator.
        var allocator = WorldUpdateAllocator;

        // Create a native array using world update allocator.
        nativeArray = CollectionHelper.CreateNativeArray<int>(5, allocator);
        for (int i = 0; i < 5; i++)
        {
            nativeArray[i] = i;
        }
    }
}
```

## Accessing Through SystemState

```csharp
// Access world update allocator through SystemState.WorldUpdateAllocator.
unsafe partial struct AllocateNativeArrayISystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        // Get world update allocator through SystemState.WorldUpdateAllocator.
        var allocator = state.WorldUpdateAllocator;

        // Create a native array using world update allocator.
        var nativeArray = CollectionHelper.CreateNativeArray<int>(10, allocator);

        for (int i = 0; i < 10; i++)
        {
            nativeArray[i] = i;
        }
    }
}
```

## Additional Resources

- [Allocators overview](allocators-overview.html)
- [Rewindable allocators](https://docs.unity3d.com/Packages/com.unity.collections@latest/index.html?subfolder=/manual/allocator-rewindable.html)
- [Allocator benchmarks](https://docs.unity3d.com/Packages/com.unity.collections@latest/index.html?subfolder=/manual/allocator-benchmarks.html)

---

## Outgoing Links

- https://docs.unity3d.com/Packages/com.unity.collections@latest/index.html?subfolder=/manual/allocator-rewindable.html - Rewindable allocators
- https://docs.unity3d.com/Packages/com.unity.collections@latest/index.html?subfolder=/manual/allocator-benchmarks.html - Allocator benchmarks
- https://docs.unity3d.com/Manual/TermsOfUse.html - Trademarks and terms of use
- https://unity.com/legal - Legal
- https://unity.com/legal/privacy-policy - Privacy Policy
- https://unity.com/legal/cookie-policy - Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information - Do Not Sell or Share My Personal Information
