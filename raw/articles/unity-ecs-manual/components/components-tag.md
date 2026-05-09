---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/components-tag.html
fetched: 2026-05-05
section: components
---

# Tag Components

Tag components are [unmanaged components](components-unmanaged.html) that store no data and take up no space.

Conceptually, tag components fulfill a similar purpose to [GameObject tags](https://docs.unity3d.com/Manual/Tags.html) and they're useful in queries because you can filter entities by whether they have a tag component. For example, you can use them alongside [cleanup components](components-cleanup.html) and filter entities to perform cleanup.

## Create a Tag Component

To create a tag component, construct an [unmanaged component](components-unmanaged.html) without any properties.

The following code sample demonstrates a tag component:

```csharp
public struct ExampleTagComponent : IComponentData
{

}
```

## Additional Resources

- [Unmanaged components](components-unmanaged.html)
- [Cleanup components](components-cleanup.html)

---

## Outgoing Links

- https://docs.unity3d.com/Manual/Tags.html - GameObject tags
- https://docs.unity3d.com/Manual/TermsOfUse.html - Trademarks and terms of use
- https://unity.com/legal - Legal
- https://unity.com/legal/privacy-policy - Privacy Policy
- https://unity.com/legal/cookie-policy - Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information - Do Not Sell or Share My Personal Information
