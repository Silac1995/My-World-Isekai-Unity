---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/components-singleton.html
fetched: 2026-05-05
section: components
---

# Singleton Components

A singleton component is a component that has only one instance in a given world. For example, if only one entity in a world has a component of type `T`, then `T` is a singleton component.

If a singleton component is added to another entity, then it's no longer a singleton component. Additionally, a singleton component can exist in another world, without affecting its singleton state.

## Singleton Component APIs

The Entities package contains several APIs you can use to work with singleton components:

### EntityManager

- `CreateSingleton`

### EntityQuery

- `GetSingletonEntity`
- `GetSingleton`
- `GetSingletonRW`
- `TryGetSingleton`
- `HasSingleton`
- `TryGetSingletonBuffer`
- `TryGetSingletonEntity`
- `GetSingletonBuffer`
- `SetSingleton`

### SystemAPI

- `GetSingletonEntity`
- `GetSingleton`
- `GetSingletonRW`
- `TryGetSingleton`
- `HasSingleton`
- `TryGetSingletonBuffer`
- `TryGetSingletonEntity`
- `GetSingletonBuffer`
- `SetSingleton`

## Use Cases

The singleton component APIs are useful in situations where you know that there's only one instance of a component. For example, in a single-player application requiring only one `PlayerController` component instance, the singleton APIs simplify code. In server-based architectures, client-side implementations typically track timestamps for their instance only, making singleton APIs convenient.

## Dependency Completion

Singleton components have special-case behavior in dependency completion. "With normal component access, APIs such as `EntityManager.GetComponentData` or `SystemAPI.GetComponent` ensure that any running jobs that might write to the same component data on a worker thread are completed before returning the requested data."

However, singleton API calls don't ensure that running jobs are completed first. The Jobs Debugger logs an error on invalid access. You either need to manually complete dependencies with `EntityManager.CompleteDependencyBeforeRO` or `EntityManager.CompleteDependencyBeforeRW`, or restructure the data dependencies.

### Best Practices for GetSingletonRW

When using `GetSingletonRW` to get read/write access to components, follow these best practices:

- Only use to access a `NativeContainer` in a component, as native containers have their own safety mechanisms compatible with Jobs Debugger
- Check the Jobs Debugger for errors, which indicate dependency issues requiring restructuring or manual completion

---

## Outgoing Links

- [concepts-worlds.html](../concepts-worlds.html) - Worlds concept documentation
- [EntityManager API](../api/Unity.Entities.EntityManager.html)
- [EntityManager.CreateSingleton](../api/Unity.Entities.EntityManager.CreateSingleton.html)
- [EntityQuery API](../api/Unity.Entities.EntityQuery.html)
- [SystemAPI API](../api/Unity.Entities.SystemAPI.html)
- [EntityManager.GetComponentData](../api/Unity.Entities.EntityManager.GetComponentData.html)
- [SystemAPI.GetComponent](../api/Unity.Entities.SystemAPI.GetComponent.html)
- [EntityManager.CompleteDependencyBeforeRO](../api/Unity.Entities.EntityManager.CompleteDependencyBeforeRO.html)
- [EntityManager.CompleteDependencyBeforeRW](../api/Unity.Entities.EntityManager.CompleteDependencyBeforeRW.html)
- [docs.unity3d.com](http://docs.unity3d.com/)
