---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/components-managed.html
fetched: 2026-05-05
section: components
---

# Managed Components

Unlike unmanaged components, managed components can store properties of any type. However, they're more resource intensive to store and access, and have the following restrictions:

- You can't access them in jobs.
- You can't use them in Burst compiled code.
- They require garbage collection.
- They must include a constructor with no parameters for serialization purposes.

## Managed Type Properties

If a property in a managed component uses a managed type, you might need to manually add the ability to clone, compare, and serialize the property.

## Create a Managed Component

To create a managed component, create a class that inherits from `IComponentData` and either has no constructor, or includes a parameterless constructor.

The following code sample shows a managed component:

```csharp
// Declare a class to create a managed component
public class ExampleManagedComponent : IComponentData
{
    public int Value;
}
```

## Manage the Lifecycle of External Resources

For managed components that reference external resources, it's best practice to implement `ICloneable` and `IDisposable`, for example, for a managed component that stores a reference to a `ParticleSystem`.

If you duplicate this managed component's entity, by default this creates two managed components that both reference the same particle system. If you implement `ICloneable` for the managed component, you can duplicate the particle system for the second managed component. If you destroy the managed component, by default the particle system remains behind. If you implement `IDisposable` for the managed component, you can destroy the particle system when the component is destroyed.

```csharp
public class ManagedComponentWithExternalResource : IComponentData, IDisposable, ICloneable
{
    public ParticleSystem ParticleSystem;

    public void Dispose()
    {
        UnityEngine.Object.Destroy(ParticleSystem);
    }

    public object Clone()
    {
        return new ManagedComponentWithExternalResource { ParticleSystem = UnityEngine.Object.Instantiate(ParticleSystem) };
    }
}
```

## Optimize Managed Components

Unlike unmanaged components, Unity doesn't store managed components directly in chunks. Instead, Unity stores them in one big array for the whole `World`. Chunks then store the array indices of the relevant managed components. This means when you access a managed component of an entity, Unity processes an extra index lookup. This makes managed components less optimal than unmanaged components.

The performance implications of managed components mean that you should use unmanaged components instead where possible.

## Additional Resources

- Unmanaged components overview

---

## Outgoing Links

- https://docs.unity3d.com/6000.0/Documentation/Manual/JobSystem.html - Job System
- https://docs.unity3d.com/Packages/com.unity.burst@latest - Burst Documentation
- https://docs.microsoft.com/en-us/dotnet/api/system.icloneable - ICloneable
- https://docs.microsoft.com/en-us/dotnet/api/system.idisposable - IDisposable
- https://docs.unity3d.com/Manual/class-ParticleSystem.html - ParticleSystem
- https://docs.unity3d.com/Manual/TermsOfUse.html - Trademarks and Terms of Use
- https://unity.com/legal - Legal
- https://unity.com/legal/privacy-policy - Privacy Policy
- https://unity.com/legal/cookie-policy - Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information - Do Not Sell or Share My Personal Information
