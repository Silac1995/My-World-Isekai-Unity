---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/components-buffer-create.html
fetched: 2026-05-05
section: components
---

# Create a Dynamic Buffer Component

## Overview

To establish a dynamic buffer component, construct a struct that implements `IBufferElementData`. This struct both defines the element type of the dynamic buffer and represents the dynamic buffer Component itself.

## Initial Capacity Configuration

Use the `InternalBufferCapacity` attribute to set the buffer's starting capacity. Refer to the Capacity documentation for details on how Unity manages buffer capacity.

## Code Example

```csharp
[InternalBufferCapacity(16)]
public struct ExampleBufferComponent : IBufferElementData
{
    public int Value;
}
```

## Adding to Entities

Like standard components, you can attach a dynamic buffer component to an entity. However, dynamic buffer components use the `DynamicBuffer<T>` representation and require specific `EntityManager` APIs for interaction, such as `EntityManager.GetBuffer<T>()`.

Example usage:

```csharp
public void GetDynamicBufferComponentExample(Entity e)
{
    DynamicBuffer<ExampleBufferComponent> myDynamicBuffer = 
        EntityManager.GetBuffer<ExampleBufferComponent>(e);
}
```

## Additional Resources

- [Access dynamic buffers from jobs](components-buffer-jobs.html)
- [Reinterpret a dynamic buffer](components-buffer-reinterpret.html)

---

## Outgoing Links

- https://docs.unity3d.com/Manual/TermsOfUse.html - Trademarks and terms of use
- https://unity.com/legal - Legal
- https://unity.com/legal/privacy-policy - Privacy Policy
- https://unity.com/legal/cookie-policy - Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information - Do Not Sell or Share My Personal Information
