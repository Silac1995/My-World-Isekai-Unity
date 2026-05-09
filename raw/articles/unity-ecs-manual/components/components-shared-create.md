---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/components-shared-create.html
fetched: 2026-05-05
section: components
---

# Create a Shared Component

You can create both managed and unmanaged shared components.

## Create an Unmanaged Shared Component

To create an unmanaged shared component, construct a struct that implements the marker interface `ISharedComponentData`.

The following code sample shows an unmanaged shared component:

```csharp
public struct ExampleUnmanagedSharedComponent : ISharedComponentData
{
    public int Value;
}
```

To override the way that a shared component is checked for equality, you can implement the `IEquatable<>` interface, and ensure `public override int GetHashCode()` is implemented. Entities then internally uses these methods to compare shared components for equality, and therefore partitions entities differently that way. You can also put `[BurstCompile]` on these methods, and they will be compiled with Burst if they comply with Burst's restrictions.

## Create a Managed Shared Component

If you create a shared component struct that has any managed fields (such as class types like strings), that component will be treated as a managed shared component. In that case, you also must implement `IEquatable<>`, and ensure `public override int GetHashCode()` is implemented. The equality methods are necessary to ensure comparisons don't generate unnecessary managed allocations due to implicit boxing when using the default `Equals` and `GetHashCode` implementations.

In contrast to IComponentData components, all shared components must be `struct`s, irrespective of whether they are managed or unmanaged.

The following code sample shows a managed shared component:

```csharp
public struct ExampleManagedSharedComponent : ISharedComponentData, IEquatable<ExampleManagedSharedComponent>
{
    public string Value; // A managed field type

    public bool Equals(ExampleManagedSharedComponent other)
    {
        return Value.Equals(other.Value);
    }

    public override int GetHashCode()
    {
        return Value.GetHashCode();
    }
}
```

---

## Outgoing Links

- https://docs.unity3d.com/Manual/TermsOfUse.html — Trademarks and terms of use
- https://unity.com/legal — Legal
- https://unity.com/legal/privacy-policy — Privacy Policy
- https://unity.com/legal/cookie-policy — Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information — Do Not Sell or Share My Personal Information
