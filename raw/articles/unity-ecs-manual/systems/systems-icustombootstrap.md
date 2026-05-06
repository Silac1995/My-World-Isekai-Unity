---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/systems-icustombootstrap.html
fetched: 2026-05-05
section: systems
---

# Manage Systems in Multiple Worlds

## Overview

You can establish multiple [worlds](concepts-worlds.html) and instantiate the same system types across them. Each system can update at different rates from various points in the update sequence. The Netcode package demonstrates this pattern, creating separate client and server worlds within a single process. This approach is considered advanced and uncommon in typical user code.

## Implementation Using ICustomBootstrap

To manage systems across multiple worlds, implement the [`ICustomBootstrap`](../api/Unity.Entities.ICustomBootstrap.html) interface. Unity invokes this before standard world initialization and uses the return value to determine whether default initialization should proceed:

```csharp
public interface ICustomBootstrap
{
    // Create your own set of worlds or your own custom default world in this method.
    // If true is returned, the default world bootstrap doesn't run at all and no additional worlds are created.
    bool Initialize(string defaultWorldName);
}
```

## Setup Procedure

A typical `MyCustomBootstrap.Initialize` implementation follows these steps:

1. **Create your worlds** - Establish the set of worlds needed for your application.

2. **Configure each world:**
   - Generate a system list for that world using [`DefaultWorldInitialization.GetAllSystems`](../api/Unity.Entities.DefaultWorldInitialization.GetAllSystems.html) (optional)
   - Call `DefaultWorldInitialization.AddSystemsToRootLevelSystemGroups` to add systems while respecting [`CreateAfter`](../api/Unity.Entities.CreateAfterAttribute.html)/[`CreateBefore`](../api/Unity.Entities.CreateBeforeAttribute.html) dependencies
   - Optionally call `ScriptBehaviourUpdateOrder.AppendWorldToCurrentPlayerLoop` to integrate the world into the player loop

3. **Handle the default world:**
   - If you created the default world, set `World.DefaultGameObjectInjectionWorld` and return `true`
   - If you want the default bootstrap to create it, return `false`

## Related Resources

- [DefaultWorldInitialization.AddSystemsToRootLevelSystemGroups](../api/Unity.Entities.DefaultWorldInitialization.AddSystemsToRootLevelSystemGroups.html)
- [Netcode package implementation example](https://docs.unity3d.com/Packages/com.unity.netcode@latest)

---

## Outgoing Links

- http://docs.unity3d.com/ - docs.unity3d.com
- https://docs.unity3d.com/Manual/TermsOfUse.html - Trademarks and terms of use
- https://unity.com/legal - Legal
- https://unity.com/legal/privacy-policy - Privacy Policy
- https://unity.com/legal/cookie-policy - Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information - Do Not Sell or Share My Personal Information
