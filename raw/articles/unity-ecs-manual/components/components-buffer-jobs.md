---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/components-buffer-jobs.html
fetched: 2026-05-05
section: components
---

# Access Dynamic Buffers from Jobs

## Overview

To enable jobs to perform random access to dynamic buffers, utilize the `BufferLookup` lookup table mechanism. Create these lookups in systems and pass them to jobs requiring buffer access.

## Modifying the Job

In jobs requiring random access to dynamic buffers:

1. Include a `ReadOnly` `BufferLookup` member variable
2. Within the `IJobEntity.Execute` method, index the lookup table by entity to access the associated dynamic buffer

```csharp
public partial struct AccessDynamicBufferJob : IJobEntity
{
    [ReadOnly] public BufferLookup<ExampleBufferComponent> BufferLookup;
    public void Execute()
    {
        // ...
    }
}
```

## Modifying the Systems

In systems that instantiate the job:

1. Declare a `BufferLookup` member variable
2. In `OnCreate`, use `SystemState.GetBufferLookup` to initialize the variable
3. At the start of `OnUpdate`, invoke `Update` on the lookup variable to refresh it
4. When constructing the job instance, assign the lookup table to it

```csharp
public partial struct AccessDynamicBufferFromJobSystem : ISystem
{
    private BufferLookup<ExampleBufferComponent> _bufferLookup;

    public void OnCreate(ref SystemState state)
    {
        _bufferLookup = state.GetBufferLookup<ExampleBufferComponent>(true);
    }

    public void OnUpdate(ref SystemState state)
    {
        _bufferLookup.Update(ref state);
        var exampleBufferAccessJob = new AccessDynamicBufferJob { BufferLookup = _bufferLookup };
        exampleBufferAccessJob.ScheduleParallel();
    }
}
```

---

## Outgoing Links

- https://docs.unity3d.com/ScriptReference/Unity.Collections.ReadOnlyAttribute.html - ReadOnly Attribute
- https://docs.unity3d.com/Manual/TermsOfUse.html - Terms of Use
- https://unity.com/legal - Legal
- https://unity.com/legal/privacy-policy - Privacy Policy
- https://unity.com/legal/cookie-policy - Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information - Do Not Sell or Share My Personal Information
