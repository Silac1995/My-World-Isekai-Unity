# Unity ECS / Entities 6.4 — Manual Mirror

Local mirror of the official Unity Entities 6.4 documentation (Unity 6.3 LTS), fetched on **2026-05-05** for offline reference.

- **Upstream root:** https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/index.html
- **Feature set hub:** https://docs.unity3d.com/6000.3/Documentation/Manual/ECSFeature.html
- **Package version installed:** `com.unity.feature.ecs@1.0.0` (see `Packages/manifest.json`)

Each page contains YAML frontmatter with the original `source_url`, the `fetched` date, and the `section`. Outgoing hyperlinks are preserved at the bottom of each file as `<slug>.html` references that can be resolved either against the local files in this folder or the upstream URL.

For the wiki integration see:
- [[unity-ecs-manual]] — wiki reference page
- [[unity-ecs]] — wiki concept hub

---

## 1. Getting started — `getting-started/`
Top-level orientation, installation, package list, version notes.

- [getting-started-installation.md](getting-started/getting-started-installation.md) — installing the Entities package
- [ecs-packages.md](getting-started/ecs-packages.md) — list of all packages comprising Unity's ECS
- [whats-new.md](getting-started/whats-new.md) — what's new in Entities 6.4
- [upgrade-guide.md](getting-started/upgrade-guide.md) — upgrade guide
- [content-management.md](getting-started/content-management.md) — content archives interface

## 2. Concepts — `concepts/`
The architectural mental model. Read these first.

- [concepts-intro.md](concepts/concepts-intro.md) — concepts hub
- [concepts-ecs.md](concepts/concepts-ecs.md) — ECS introduction (3-part Entities/Components/Systems model)
- [concepts-entities.md](concepts/concepts-entities.md) — what an entity is
- [concepts-components.md](concepts/concepts-components.md) — component types overview
- [concepts-systems.md](concepts/concepts-systems.md) — SystemBase vs ISystem, OnUpdate/OnCreate/OnDestroy
- [concepts-worlds.md](concepts/concepts-worlds.md) — World, default world, ICustomBootstrap
- [concepts-archetypes.md](concepts/concepts-archetypes.md) — archetype identity, 16 KiB chunks
- [concepts-structural-changes.md](concepts/concepts-structural-changes.md) — what counts as structural, sync points
- [concepts-safety.md](concepts/concepts-safety.md) — safety mechanisms, RefRW/RefRO, ExclusiveEntityTransaction

## 3. ECS workflow tutorial — `workflow/`
End-to-end implementation walkthroughs.

- [ecs-workflow-tutorial.md](workflow/ecs-workflow-tutorial.md) — workflow hub
- [ecs-workflow-intro.md](workflow/ecs-workflow-intro.md) — introduction
- [ecs-workflow-example-starter.md](workflow/ecs-workflow-example-starter.md) — starter workflow
- [ecs-workflow-example-authoring-baking.md](workflow/ecs-workflow-example-authoring-baking.md) — authoring + baking
- [ecs-workflow-example-prefab-instantiation.md](workflow/ecs-workflow-example-prefab-instantiation.md) — prefab instantiation
- [ecs-workflow-example-multithreading.md](workflow/ecs-workflow-example-multithreading.md) — make a system multithreaded
- [ecs-workflow-example-ecb.md](workflow/ecs-workflow-example-ecb.md) — entity command buffer

## 4. Components — `components/`
All component flavours: managed, unmanaged, shared, cleanup, tag, buffer, chunk, enableable, singleton, plus add/remove/read/write APIs.

- Hub: [components-intro.md](components/components-intro.md), [components-type.md](components/components-type.md)
- Plain: [components-unmanaged.md](components/components-unmanaged.md), [components-managed.md](components/components-managed.md), [components-tag.md](components/components-tag.md)
- [Shared](components/components-shared.md): [introducing](components/components-shared-introducing.md), [create](components/components-shared-create.md), [optimize](components/components-shared-optimize.md)
- [Cleanup](components/components-cleanup.md): [introducing](components/components-cleanup-introducing.md), [create](components/components-cleanup-create.md), [shared](components/components-cleanup-shared.md)
- [Buffer](components/components-buffer.md): [introducing](components/components-buffer-introducing.md), [create](components/components-buffer-create.md), [set-capacity](components/components-buffer-set-capacity.md), [get-all-in-chunk](components/components-buffer-get-all-in-chunk.md), [from-jobs](components/components-buffer-jobs.md), [command-buffer](components/components-buffer-command-buffer.md), [reinterpret](components/components-buffer-reinterpret.md)
- [Chunk](components/components-chunk.md): [introducing](components/components-chunk-introducing.md), [create](components/components-chunk-create.md), [use](components/components-chunk-use.md)
- [Enableable](components/components-enableable.md): [intro](components/components-enableable-intro.md), [use](components/components-enableable-use.md)
- [Singleton](components/components-singleton.md), [Native containers](components/components-nativecontainers.md)
- Lifecycle: [add-to-entity](components/components-add-to-entity.md), [remove-from-entity](components/components-remove-from-entity.md), [read-and-write](components/components-read-and-write.md)

## 5. Systems — `systems/`
SystemBase, ISystem, SystemAPI, update order, EntityManager, ECB, transforms, blob assets, optimisation.

- Hub & lifecycle: [systems-intro.md](systems/systems-intro.md), [systems-comparison.md](systems/systems-comparison.md), [systems-systembase.md](systems/systems-systembase.md), [systems-isystem.md](systems/systems-isystem.md), [systems-systemapi.md](systems/systems-systemapi.md), [systems-update-order.md](systems/systems-update-order.md), [systems-icustombootstrap.md](systems/systems-icustombootstrap.md), [systems-time.md](systems/systems-time.md), [systems-optimizing.md](systems/systems-optimizing.md), [systems-entitymanager.md](systems/systems-entitymanager.md)
- Data access: [systems-data.md](systems/systems-data.md), [systems-data-granularity.md](systems/systems-data-granularity.md), [systems-access-data-intro.md](systems/systems-access-data-intro.md), [systems-access-data.md](systems/systems-access-data.md), [systems-version-numbers.md](systems/systems-version-numbers.md), [systems-write-groups.md](systems/systems-write-groups.md), [reference-unity-objects.md](systems/reference-unity-objects.md)
- Iteration: [systems-iterating-data-intro.md](systems/systems-iterating-data-intro.md), [iterating-manually.md](systems/iterating-manually.md)
- Structural changes: [systems-manage-structural-changes.md](systems/systems-manage-structural-changes.md), [systems-manage-structural-changes-intro.md](systems/systems-manage-structural-changes-intro.md), [systems-deferring-data.md](systems/systems-deferring-data.md), [optimize-structural-changes.md](systems/optimize-structural-changes.md), [structural-changes-enableable-components.md](systems/structural-changes-enableable-components.md)
- Entity Command Buffer: [systems-entity-command-buffers.md](systems/systems-entity-command-buffers.md), [systems-entity-command-buffer-use.md](systems/systems-entity-command-buffer-use.md), [systems-entity-command-buffer-playback.md](systems/systems-entity-command-buffer-playback.md), [systems-entity-command-buffer-automatic-playback.md](systems/systems-entity-command-buffer-automatic-playback.md)
- Job scheduling: [scheduling-jobs-dependencies.md](systems/scheduling-jobs-dependencies.md), [job-overhead.md](systems/job-overhead.md)
- Transforms: [intro](systems/transforms-intro.md), [using](systems/transforms-using.md), [comparison](systems/transforms-comparison.md), [custom](systems/transforms-custom.md)
- Blob assets: [intro](systems/blob-assets-intro.md), [create](systems/blob-assets-create.md)
- [linked-entity-group.md](systems/linked-entity-group.md)

## 6. Queries & jobs — `queries-jobs/`
EntityQuery, IJobEntity, IJobChunk, scheduling, look-up.

- EntityQuery: [systems-entityquery-intro.md](queries-jobs/systems-entityquery-intro.md), [systems-entityquery-create.md](queries-jobs/systems-entityquery-create.md), [systems-entityquery-filters.md](queries-jobs/systems-entityquery-filters.md), [systems-systemapi-query.md](queries-jobs/systems-systemapi-query.md)
- Jobs: [iterating-data-ijobentity.md](queries-jobs/iterating-data-ijobentity.md), [iterating-data-ijobchunk.md](queries-jobs/iterating-data-ijobchunk.md), [iterating-data-ijobchunk-implement.md](queries-jobs/iterating-data-ijobchunk-implement.md), [systems-scheduling-jobs.md](queries-jobs/systems-scheduling-jobs.md)
- Look-up: [systems-looking-up-data.md](queries-jobs/systems-looking-up-data.md)

## 7. Conversion / baking / scenes — `conversion/`
GameObject → Entity baking, subscenes, scene streaming.

- [conversion-intro.md](conversion/conversion-intro.md), [conversion-scene-overview.md](conversion/conversion-scene-overview.md), [conversion-subscenes.md](conversion/conversion-subscenes.md)
- Baking: [baking.md](conversion/baking.md), [baking-overview.md](conversion/baking-overview.md), [baking-baker-overview.md](conversion/baking-baker-overview.md), [baking-baking-systems-overview.md](conversion/baking-baking-systems-overview.md), [baking-baking-worlds-overview.md](conversion/baking-baking-worlds-overview.md), [baking-phases.md](conversion/baking-phases.md), [baking-filter-output.md](conversion/baking-filter-output.md), [baking-prefabs.md](conversion/baking-prefabs.md)
- Streaming: [streaming-scenes.md](conversion/streaming-scenes.md), [streaming-overview.md](conversion/streaming-overview.md), [streaming-loading-scenes.md](conversion/streaming-loading-scenes.md), [streaming-scene-sections.md](conversion/streaming-scene-sections.md), [streaming-scene-instancing.md](conversion/streaming-scene-instancing.md), [streaming-meta-entities.md](conversion/streaming-meta-entities.md)

## 8. Editor reference — `editor/`
Entity Inspector, Hierarchy, Archetypes / Components / Systems / Query windows, Preferences, Project Settings.

- [editor-workflows.md](editor/editor-workflows.md), [editor-authoring-runtime.md](editor/editor-authoring-runtime.md), [editor-inspectors.md](editor/editor-inspectors.md)
- Inspectors: [editor-entity-inspector.md](editor/editor-entity-inspector.md), [editor-component-inspector.md](editor/editor-component-inspector.md), [editor-system-inspector.md](editor/editor-system-inspector.md)
- Windows: [editor-hierarchy-window.md](editor/editor-hierarchy-window.md), [editor-archetypes-window.md](editor/editor-archetypes-window.md), [editor-components-window.md](editor/editor-components-window.md), [editor-systems-window.md](editor/editor-systems-window.md), [editor-query-window.md](editor/editor-query-window.md)
- Settings: [editor-preferences.md](editor/editor-preferences.md), [editor-project-settings.md](editor/editor-project-settings.md)

## 9. Performance & debugging — `performance/`
Profiler modules, journaling, allocators, sync points, common errors.

- [performance-debugging.md](performance/performance-debugging.md), [performance-entities.md](performance/performance-entities.md), [performance-chunk-allocations.md](performance/performance-chunk-allocations.md), [performance-sync-points.md](performance/performance-sync-points.md)
- Profiler: [profiler-modules-entities.md](performance/profiler-modules-entities.md), [profiler-modules-entities-introduction.md](performance/profiler-modules-entities-introduction.md), [profiler-module-memory.md](performance/profiler-module-memory.md), [profiler-module-structural-changes.md](performance/profiler-module-structural-changes.md)
- Allocators: [memory-allocators.md](performance/memory-allocators.md), [allocators-overview.md](performance/allocators-overview.md), [allocators-world-update.md](performance/allocators-world-update.md), [allocators-system-group.md](performance/allocators-system-group.md), [allocators-entity-command-buffer.md](performance/allocators-entity-command-buffer.md), [allocators-custom-prebuilt-intro.md](performance/allocators-custom-prebuilt-intro.md)
- Diagnostics: [entities-journaling.md](performance/entities-journaling.md), [common-errors.md](performance/common-errors.md)

## 10. Related packages — `related-packages/`
Top-level orientation only. Each package's full docs live upstream.

- **Entities Graphics**: [graphics-index.md](related-packages/graphics-index.md), [graphics-getting-started.md](related-packages/graphics-getting-started.md), [graphics-creating-a-new-project.md](related-packages/graphics-creating-a-new-project.md), [graphics-requirements-and-compatibility.md](related-packages/graphics-requirements-and-compatibility.md)
- **Unity Physics (DOTS)**: [physics-index.md](related-packages/physics-index.md), [physics-installation.md](related-packages/physics-installation.md), [physics-getting-started.md](related-packages/physics-getting-started.md), [physics-concepts-intro.md](related-packages/physics-concepts-intro.md)
- **Netcode for Entities**: [netcode-index.md](related-packages/netcode-index.md), [netcode-installation.md](related-packages/netcode-installation.md), [netcode-networked-cube.md](related-packages/netcode-networked-cube.md), [netcode-ghost-snapshots.md](related-packages/netcode-ghost-snapshots.md), [netcode-prediction.md](related-packages/netcode-prediction.md)

---

## Coverage notes

Two follow-up passes confirmed the canonical TOC for v6.4. Pages that still aren't on disk genuinely don't exist as standalone pages — their material is folded into the indexed pages here, or lives in an adjacent package's docs:

- **EntityQuery `use` / `write-groups` / `iterate` / `singleton`** — folded into `systems-entityquery-intro.md`, `systems-entityquery-create.md`, `systems-entityquery-filters.md`, and `systems-write-groups.md`.
- **`IJobEntity` / `IJobChunk` / job dependencies** — covered by `iterating-data-ijobentity.md`, `iterating-data-ijobchunk.md`, `iterating-data-ijobchunk-implement.md`, `systems-scheduling-jobs.md`, `scheduling-jobs-dependencies.md`, `job-overhead.md`.
- **Burst compiler** — has its own package documentation outside `com.unity.entities`. See [com.unity.burst](https://docs.unity3d.com/Packages/com.unity.burst@latest) when needed.
- **`systems-state` / `systems-systemstate` / `systems-changes-since-last-update`** — these slugs do not exist in v6.4. Per-system change-detection material is in `systems-version-numbers.md`.
- **`systems-component-system-groups`** — does not exist as a standalone page; system-group structure is covered in `systems-update-order.md` and `systems-icustombootstrap.md`.
- **Baker / baking-system deeper internals** (`baking-baker.html`, `baking-baking-system.html`, `baking-incremental.html`, `baking-companion-components.html`, `baking-blob-assets.html`, `baking-additional-entities.html`, etc.) — none exist as standalone pages in v6.4. The full baking TOC is the 8 pages already on disk under `conversion/`.

If a URL slug differs from a candidate guessed in the fetch, the page returned 404 and was skipped — the canonical slugs are the ones present here. Total mirrored pages: **160**.
