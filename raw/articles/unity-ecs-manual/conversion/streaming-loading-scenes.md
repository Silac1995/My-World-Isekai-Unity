---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/streaming-loading-scenes.html
fetched: 2026-05-05
section: conversion
---

# Load a Scene

## Overview

To load a scene in Unity Entities, you have two approaches: use subscenes or employ the `SceneSystem` API. When a `SubScene` component has `AutoLoadScene` enabled, Unity automatically streams the referenced scene. For direct streaming control without a subscene, the `SceneSystem.LoadSceneAsync` method provides a high-level API for asynchronous scene loading within a system's `OnUpdate` method.

## Scene Identification

Scene loading requires one of three identifier types:

- `EntitySceneReference`
- `Hash128` GUID
- Scene meta `Entity`

**Important Note:** The build process only detects scenes referenced via `EntitySceneReference` or `SubScene` components. Scenes referenced by GUIDs alone won't be included in builds. However, in Play mode, all scenes remain available regardless of reference method, with automatic baking triggered if entity scene files are missing or outdated.

The `SceneSystem.LoadSceneAsync` method returns the scene meta `Entity`, enabling subsequent operations like reloading or unloading.

## Using EntitySceneReference (Recommended)

This approach is the recommended practice for maintaining scene references during baking and runtime loading.

**Authoring Component Example:**

```csharp
// Runtime component, SceneSystem uses EntitySceneReference to identify scenes.
public struct SceneLoader : IComponentData
{
    public EntitySceneReference SceneReference;
}

#if UNITY_EDITOR
// Authoring component, a SceneAsset can only be used in the Editor
public class SceneLoaderAuthoring : MonoBehaviour
{
    public UnityEditor.SceneAsset Scene;

    class Baker : Baker<SceneLoaderAuthoring>
    {
        public override void Bake(SceneLoaderAuthoring authoring)
        {
            var reference = new EntitySceneReference(authoring.Scene);
            var entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new SceneLoader
            {
                SceneReference = reference
            });
        }
    }
}
#endif
```

**System Example for Loading:**

```csharp
[RequireMatchingQueriesForUpdate]
public partial class SceneLoaderSystem : SystemBase
{
    private EntityQuery newRequests;

    protected override void OnCreate()
    {
        newRequests = GetEntityQuery(typeof(SceneLoader));
    }

    protected override void OnUpdate()
    {
        var requests = newRequests.ToComponentDataArray<SceneLoader>(Allocator.Temp);

        // Can't use a foreach with a query as SceneSystem.LoadSceneAsync does structural changes
        for (int i = 0; i < requests.Length; i += 1)
        {
            SceneSystem.LoadSceneAsync(World.Unmanaged, requests[i].SceneReference);
        }

        requests.Dispose();
        EntityManager.DestroyEntity(newRequests);
    }
}
```

## Loading Process

During the `SceneSystem.LoadSceneAsync` call, only the scene entity is created immediately. "The scene header, the section entities, and their content aren't loaded during this call and they are ready a few frames later." The structural changes performed by this method prevent it from being called within a foreach loop over a query.

## Load Parameters

The optional `LoadParameters` struct controls loading behavior through `SceneLoadFlags`:

- **DisableAutoLoad**: Creates scene and section meta entities without loading section content. You can then load individual sections via `ResolvedSectionEntity`.
- **BlockOnStreamIn**: Performs synchronous loading, with the method returning only when the scene is fully loaded.
- **NewInstance**: Creates a new scene copy for scene instancing purposes.

## Unloading Scenes

Use `SceneSystem.UnloadScene` to unload scenes:

```csharp
var unloadParameters = SceneSystem.UnloadParameters.DestroyMetaEntities;
SceneSystem.UnloadScene(World.Unmanaged, sceneEntity, unloadParameters);
```

By default, unloading preserves meta entities for faster reloading. Call with `UnloadParameters.DestroyMetaEntities` to remove meta entities as well. While you can identify scenes by GUID or `EntitySceneReference`, using the scene meta entity offers better performance and handles multiple instances correctly.

---

## Outgoing Links

- http://docs.unity3d.com/ - docs.unity3d.com
- https://docs.unity3d.com/Manual/TermsOfUse.html - Trademarks and terms of use
- https://unity.com/legal - Legal
- https://unity.com/legal/privacy-policy - Privacy Policy
- https://unity.com/legal/cookie-policy - Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information - Do Not Sell or Share My Personal Information
