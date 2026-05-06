---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/allocators-system-group.html
fetched: 2026-05-05
section: performance
---

# System Group Allocator

Each `ComponentSystemGroup` has an option to create a system group allocator when setting its rate manager. To do this, use `ComponentSystemGroup.SetRateManagerCreateAllocator`. If you use the property `RateManager` to set a rate manager in the system group, then the component system group doesn't create a system group allocator.

## Setting Up a System Group Allocator

The following example uses `ComponentSystemGroup.SetRateManagerCreateAllocator` to set a rate manager and create a system group allocator:

```csharp
[WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor | WorldSystemFilterFlags.ThinClientSimulation)]
[UpdateInGroup(typeof(FixedStepSimulationSystemGroup), OrderFirst = true)]
public partial class FixedStepTestSimulationSystemGroup : ComponentSystemGroup
{
    // Set the timestep use by this group, in seconds. The default value is 1/60 seconds.
    // This value will be clamped to the range [0.0001f ... 10.0f].
    public float Timestep
    {
        get => RateManager != null ? RateManager.Timestep : 0;
        set
        {
            if (RateManager != null)
                RateManager.Timestep = value;
        }
    }

    // Default constructor
    public FixedStepTestSimulationSystemGroup()
    {
        float defaultFixedTimestep = 1.0f / 60.0f;

        // Set FixedRateSimpleManager to be the rate manager and create a system group allocator
        SetRateManagerCreateAllocator(new RateUtils.FixedRateSimpleManager(defaultFixedTimestep));
    }
}
```

## How System Group Allocators Work

The component system group that creates a system group allocator contains double rewindable allocators. `World.SetGroupAllocator` and `World.RestoreGroupAllocator` are used in `IRateManager.ShouldGroupUpdate` to replace the world update allocator with the system group allocator, and later to restore the world update allocator.

The example below shows how to use `World.SetGroupAllocator` and `World.RestoreGroupAllocator`:

```csharp
public unsafe class FixedRateSimpleManager : IRateManager
{
    const float MinFixedDeltaTime = 0.0001f;
    const float MaxFixedDeltaTime = 10.0f;

    float m_FixedTimestep;
    public float Timestep
    {
        get => m_FixedTimestep;
        set => m_FixedTimestep = math.clamp(value, MinFixedDeltaTime, MaxFixedDeltaTime);
    }

    double m_LastFixedUpdateTime;
    bool m_DidPushTime;

    DoubleRewindableAllocators* m_OldGroupAllocators = null;

    public FixedRateSimpleManager(float fixedDeltaTime)
    {
        Timestep = fixedDeltaTime;
    }

    public bool ShouldGroupUpdate(ComponentSystemGroup group)
    {
        // if this is true, means we're being called a second or later time in a loop.
        if (m_DidPushTime)
        {
            group.World.PopTime();
            m_DidPushTime = false;

            // Update the group allocators and restore the old allocator
            group.World.RestoreGroupAllocator(m_OldGroupAllocators);

            return false;
        }

        group.World.PushTime(new TimeData(
            elapsedTime: m_LastFixedUpdateTime,
            deltaTime: m_FixedTimestep));

        m_LastFixedUpdateTime += m_FixedTimestep;

        m_DidPushTime = true;

        // Back up current world or group allocator.
        m_OldGroupAllocators = group.World.CurrentGroupAllocators;
        // Replace current world or group allocator with this system group allocator.
        group.World.SetGroupAllocator(group.RateGroupAllocators);

        return true;
    }
}
```

## Allocation Behavior

The system group allocator contains double rewindable allocators and operates similarly to the world update allocator. Before a system group proceeds with its update, its system group allocator is placed in the world update allocator, and allocations from the world update allocator are allocated from the system group allocator.

If the system group skips its update, it switches the double rewindable allocators of the system group allocator, rewinds the one that swaps in, and then restores the world update allocator. Because this uses double rewindable allocators, "the lifetime of an allocation from a system group allocator lasts two system group updates." Manual deallocation isn't required, preventing memory leaks.

## Usage Example

The following example shows the system group allocator in use within `AllocateNativeArrayISystem`, which is in a fixed rate system group with the rate manager shown above:

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

- https://docs.unity3d.com/Manual/TermsOfUse.html | Trademarks and terms of use
- https://unity.com/legal | Legal
- https://unity.com/legal/privacy-policy | Privacy Policy
- https://unity.com/legal/cookie-policy | Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information | Do Not Sell or Share My Personal Information
- ../api/Unity.Entities.ComponentSystemGroup.html | ComponentSystemGroup
- ../api/Unity.Entities.ComponentSystemGroup.SetRateManagerCreateAllocator.html | ComponentSystemGroup.SetRateManagerCreateAllocator
- ../api/Unity.Entities.ComponentSystemGroup.RateManager.html | RateManager
- ../api/Unity.Entities.World.SetGroupAllocator.html | World.SetGroupAllocator
- ../api/Unity.Entities.World.RestoreGroupAllocator.html | World.RestoreGroupAllocator
- ../api/Unity.Entities.IRateManager.ShouldGroupUpdate.html | IRateManager.ShouldGroupUpdate
- allocators-world-update.html | world update allocator
