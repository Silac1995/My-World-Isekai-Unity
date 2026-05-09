---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/components-unmanaged.html
fetched: 2026-05-05
section: components
---

# Unmanaged Components

Unmanaged components store the most common data types, making them useful in the majority of use-cases.

## Supported Field Types

Unmanaged components can store fields of the following types:

- Blittable types
- `bool`
- `char`
- `BlobAssetReference<T>` (a reference to a Blob data structure)
- `Collections.FixedString` (a fixed-sized character buffer)
- `Collections.FixedList`
- Fixed array (only allowed in an `unsafe` context)
- Other structs that conform to these same restrictions

## Creating an Unmanaged Component

To create an unmanaged component, declare a struct that inherits from `IComponentData`.

### Code Example

```csharp
// Declare a struct to create an unmanaged component
public struct ExampleUnmanagedComponent : IComponentData
{
    public int Value;
}
```

Add fields using compatible types to the struct to define data for the component. Components without any fields function as tag components.

## Additional Resources

- [Tag components](components-tag.html)
- [Managed components](components-managed.html)

---

## Outgoing Links

- https://docs.microsoft.com/en-us/dotnet/framework/interop/blittable-and-non-blittable-types — Blittable and Non-Blittable Types
- https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/fixed-statement — Fixed Statement
- https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/unsafe — Unsafe Keyword
- https://docs.unity3d.com/Manual/TermsOfUse.html — Trademarks and Terms of Use
- https://unity.com/legal — Legal
- https://unity.com/legal/privacy-policy — Privacy Policy
- https://unity.com/legal/cookie-policy — Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information — Do Not Sell or Share My Personal Information
