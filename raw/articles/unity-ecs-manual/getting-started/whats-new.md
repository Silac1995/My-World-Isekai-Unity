---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/whats-new.html
fetched: 2026-05-05
section: getting-started
---

# What's New in Entities 1.4

## Deprecated API

Several APIs are marked as obsolete in this release:

- **Entities.ForEach and Job.WithCode**: Developers should use `IJobEntity` and `SystemAPI.Query` instead of `Entities.ForEach`, and `IJob` instead of `Job.WithCode`.

- **IAspect interface**: The `Component` and `EntityQuery` APIs are recommended as replacements.

- **ComponentLookup methods**: `GetRefRWOptional` and `GetRefROOptional` are deprecated in favor of `TryGetRefRO` and `TryGetRefRW` for improved safety.

All deprecated APIs remain supported in Entities 1.x but will be removed in a future major release.

## Improvements

### Inspector and Query Tools
- The System Inspector's **Queries** tab now displays Disabled, Present, Absent, and None components during query execution
- A new **Dependencies** tab shows which components systems depend on
- The Query window highlights prefabs with distinguishing icons

### New Methods and APIs
- `WorldUnmanaged` struct now includes `GetSystemTypeIndex(SystemHandle)` for retrieving `SystemTypeIndex` values
- `ComponentLookup.TryGetRefRW` and `TryGetRefRO` enable safe component access in a single call
- `ArchetypeChunk` gained `GetBufferAccessorRO` and `GetBufferAccessorRW` methods for explicit access mode specification
- New `GetUntypedBufferAccessorReinterpret<T>` method for compile-time-typed buffer accessors from runtime types

### Remote Content and Bootstrapping
- `RemoteContentCatalogBuildUtility.PublishContent` now creates content sets for all objects and scenes, generates a `DebugCatalog.txt` file, and accepts enumerable file lists
- New `DisableBootstrapOverridesAttribute` prevents unintended bootstrap implementations

### Custom Editor Enhancement
- `WeakReferencePropertyDrawer` class now properly displays `WeakObjectReference`, `WeakObjectSceneReference`, `EntitySceneReference`, and `EntityPrefabReference` fields

## Performance Improvements

- `SetSharedComponentManaged` and `IEntitiesPlayerSettings.GetFilterSettings` are optimized
- `TypeManager.Initialize` runs approximately twice as fast in player builds for large projects, no longer scanning assemblies for `UnityEngine.Object` types
- `ChunkEntityEnumerator` constructor performance improved in most cases

## Documentation Improvements

- New documentation on the `UnityObjectRef` API for storing `UnityEngine.Object` references
- Expanded coverage of LinkedEntityGroup buffers and entity transforms

---

## Outgoing Hyperlinks

| URL | Link Text |
|-----|-----------|
| http://docs.unity3d.com/ | docs.unity3d.com |
| ../index.html | docs.unity3d.com (logo) |
| ../changelog/CHANGELOG.html | Changelog |
| ../api/Unity.Entities.SystemBase.Entities.html | Entities.ForEach |
| ../api/Unity.Entities.SystemBase.Job.html | Job.WithCode |
| ../api/Unity.Entities.IJobEntity.html | IJobEntity |
| ../api/Unity.Entities.SystemAPI.Query.html | SystemAPI.Query |
| https://docs.unity3d.com/6000.1/Documentation/ScriptReference/Unity.Jobs.IJob.html | IJob |
| ../api/Unity.Entities.IAspect.html | IAspect |
| components-read-and-write.html | Component |
| ../api/Unity.Entities.EntityQuery.html | EntityQuery |
| ../api/Unity.Entities.ComponentLookup-1.GetRefRWOptional.html | ComponentLookup.GetRefRWOptional |
| ../api/Unity.Entities.ComponentLookup-1.GetRefROOptional.html | GetRefROOptional |
| ../api/Unity.Entities.ComponentLookup-1.TryGetRefRO.html | TryGetRefRO |
| ../api/Unity.Entities.ComponentLookup-1.TryGetRefRW.html | TryGetRefRW |
| upgrade-guide.html | upgrade guide |
| ../api/Unity.Entities.WorldUnmanaged.html | WorldUnmanaged |
| ../api/Unity.Entities.WorldUnmanaged.GetSystemTypeIndex.html | GetSystemTypeIndex(SystemHandle SystemHandle) |
| ../api/Unity.Entities.Content.RemoteContentCatalogBuildUtility.PublishContent.html | RemoteContentCatalogBuildUtility.PublishContent |
| ../api/Unity.Entities.ComponentLookup-1.TryGetRefRW.html | ComponentLookup.TryGetRefRW |
| ../api/Unity.Entities.ComponentLookup-1.TryGetRefRO.html | ComponentLookup.TryGetRefRO |
| ../api/Unity.Entities.DisableBootstrapOverridesAttribute.html | DisableBootstrapOverridesAttribute |
| ../api/Unity.Entities.ICustomBootstrap.html | ICustomBootstrap |
| ../api/Unity.Entities.ArchetypeChunk.GetBufferAccessorRO.html | GetBufferAccessorRO |
| ../api/Unity.Entities.ArchetypeChunk.GetBufferAccessorRW.html | GetBufferAccessorRW |
| ../api/Unity.Entities.ArchetypeChunk.GetUntypedBufferAccessorReinterpret.html | GetUntypedBufferAccessorReinterpret<T> |
| ../api/Unity.Entities.ArchetypeChunk.GetDynamicComponentDataArrayReinterpret.html | GetDynamicComponentDataArrayReinterpret<T> |
| ../api/Unity.Entities.EntityCommandBuffer.SetSharedComponentManaged.html | SetSharedComponentManaged |
| ../api/Unity.Entities.Build.IEntitiesPlayerSettings.GetFilterSettings.html | IEntitiesPlayerSettings.GetFilterSettings |
| ../api/Unity.Entities.TypeManager.Initialize.html | TypeManager.Initialize |
| ../api/Unity.Entities.RegisterUnityEngineComponentTypeAttribute.html | RegisterUnityEngineComponentTypeAttribute |
| reference-unity-objects.html | UnityObjectRef |
| linked-entity-group.html | LinkedEntityGroup |
| transforms-intro.html | Transforms in entities |
| https://docs.unity3d.com/Manual/TermsOfUse.html | Trademarks and terms of use |
| https://unity.com/legal | Legal |
| https://unity.com/legal/privacy-policy | Privacy Policy |
| https://unity.com/legal/cookie-policy | Cookie Policy |
| https://unity.com/legal/do-not-sell-my-personal-information | Do Not Sell or Share My Personal Information |
