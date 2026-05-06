---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/components-cleanup-shared.html
fetched: 2026-05-05
section: components
---

# Cleanup Shared Components

Cleanup shared components are [shared components](components-shared.html) that incorporate the destruction semantics found in [cleanup components](components-cleanup.html). They serve to identify entities requiring consistent information during the cleanup process.

## Creating a Cleanup Shared Component

To implement a cleanup shared component, define a struct that implements the `ICleanupSharedComponentData` interface.

The following demonstrates a basic cleanup shared component structure:

```csharp
public struct ExampleSharedCleanupComponent : ICleanupSharedComponentData
{
    public int Value;
}
```

---

## Outgoing Links

- http://docs.unity3d.com/ - docs.unity3d.com
- components-shared.html - Shared components
- components-cleanup.html - Cleanup components
- https://docs.unity3d.com/Manual/TermsOfUse.html - Trademarks and terms of use
- https://unity.com/legal - Legal
- https://unity.com/legal/privacy-policy - Privacy Policy
- https://unity.com/legal/cookie-policy - Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information - Do Not Sell or Share My Personal Information
