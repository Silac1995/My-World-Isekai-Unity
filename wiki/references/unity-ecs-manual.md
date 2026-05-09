---
type: reference
title: "Unity ECS / Entities 6.4 Manual"
tags: [unity, ecs, dots, entities, performance, multiplayer, external-docs]
created: 2026-05-05
updated: 2026-05-05
sources:
  - "https://docs.unity3d.com/6000.3/Documentation/Manual/ECSFeature.html"
  - "https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/index.html"
related:
  - "[[unity-ecs]]"
status: active
confidence: high
url: "https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/index.html"
kind: spec
---

# Unity ECS / Entities 6.4 Manual

## Summary

The official Unity Entity Component System (ECS) manual for the Entities package version 6.4, shipped as part of `com.unity.feature.ecs@1.0.0` for Unity 6.3 LTS. Covers the data-oriented programming model that complements the GameObject pipeline: Entity / Component / System architecture, archetype-based memory layout, baking authoring → runtime data, multithreaded jobs with Burst, and editor windows for inspecting ECS state. The full manual has been mirrored locally under `raw/articles/unity-ecs-manual/` so we can read it offline and grep against it without round-tripping to the browser.

## Key takeaways

- **Three-package feature set.** `com.unity.feature.ecs` bundles **Entities** (core ECS runtime), **Entities Graphics** (DOTS rendering pipeline integrated with URP), and **Unity Physics** (DOTS-friendly physics). **Netcode for Entities** is a fourth, optional package for ECS-native multiplayer.
- **Entity = unmanaged ID.** Entities hold no code and no behaviour — they are pure identifiers tagged with components. Systems own all logic and operate on bulk component data via queries.
- **Archetypes drive memory layout.** Every unique combination of components forms an archetype; entities of the same archetype share a contiguous **16 KiB chunk**. Adding/removing a component is a *structural change* that relocates the entity to a new archetype and triggers a sync point.
- **Two system flavours.** `SystemBase` (managed, easier, allows allocations) and `ISystem` (unmanaged, Burst-compilable, faster, no GC). `SystemAPI` is the unified entry surface used inside both.
- **Baking is one-way.** Authoring data lives on GameObjects in a SubScene; a baker converts it into ECS components at editor time / build time, producing a serialised entity scene that the runtime streams in. There is no automatic round-trip from runtime to authoring.
- **Safety system enforces job correctness.** Read/write conflicts across jobs throw at schedule time (Editor) and silently corrupt at runtime (Player) — the manual repeatedly emphasises that violations are *not* user errors but logic bugs.
- **Performance philosophy.** ECS is built around the cache: linear iteration over chunks, Burst-compiled SIMD, parallel `IJobEntity` scheduling, and explicit allocators (`World.UpdateAllocator`, system-group, ECB) rather than GC.

## Where we use it

- **Currently:** ECS is **not yet used** in the project — installed but no `Entities` code exists in `Assets/Scripts/`. The package is on hand for systems where data-oriented execution will pay off (offline simulation catch-up, large-scale crowd / vegetation / particle work, future networked replication).
- **Likely first applications** (speculative, not committed):
  - [[world-system-living]] / [[world-macro-simulation]] — Macro-Simulator catch-up math could be expressed as a pure ECS pass over hibernated NPC data, since it's already allocation-free integer arithmetic over millions of ticks.
  - [[character-terrain]] / [[terrain-grid]] — per-cell weather/vegetation ticks across thousands of cells are a textbook ECS workload.
  - Crowd / ambient NPC rendering inside cities once micro-simulation populations grow past a few hundred concurrent NPCs.
- **Will not use ECS for:**
  - The Character system — it is GameObject-based by design (rule #20, [[character]] facade pattern, [[character-archetype]] visual abstraction). Mixing GameObject characters and ECS entities for the same logical actor is an explicit non-goal.
  - Networking-critical actors — [[network]] is `com.unity.netcode.gameobjects` (NGO), not Netcode for Entities. Adopting Netcode for Entities would be a separate decision and a parallel network stack, not a swap.

## Local mirror

Full local copy of the manual at [raw/articles/unity-ecs-manual/](../../raw/articles/unity-ecs-manual/README.md). Sections:

- **Getting started** — installation, ECS package list, what's new, upgrade guide, content management
- **Concepts** — entity / component / system / world / archetype / structural changes / safety
- **Workflow tutorial** — starter, authoring + baking, prefab instantiation, multithreading, ECB
- **Components** — unmanaged, managed, shared, cleanup, tag, buffer, chunk, enableable, singleton, lifecycle
- **Systems** — SystemBase, ISystem, SystemAPI, update order, transforms, blob assets, ECB playback
- **Queries & jobs** — EntityQuery, IJobEntity, scheduling
- **Conversion / baking** — baker, baking system, baking world, scene streaming, subscenes
- **Editor reference** — Entity Inspector, Hierarchy, Archetypes / Components / Systems / Query windows
- **Performance & debugging** — profiler modules, journaling, allocators, sync points, common errors
- **Related packages** — Entities Graphics, Unity Physics, Netcode for Entities (orientation only)

Each mirrored file carries `source_url` frontmatter pointing to the canonical Unity URL — when a page becomes stale, refetch from the URL rather than guessing.

## Links

- [[unity-ecs]] — concept hub for the architecture itself
- [[network]] — current GameObjects-based netcode stack (contrast with Netcode for Entities)
- [[character]] — GameObject-based actor system, deliberately *not* ECS
- [[world-macro-simulation]] — speculative future ECS adopter
- [[performance-conventions]] — project-wide performance rules; many of the same principles (no per-frame allocations, dirty flags, pooling) map onto ECS by design

## Sources

- [https://docs.unity3d.com/6000.3/Documentation/Manual/ECSFeature.html](https://docs.unity3d.com/6000.3/Documentation/Manual/ECSFeature.html) — Unity 6.3 LTS ECS feature set hub
- [https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/index.html](https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/index.html) — Entities 6.4 manual root
- [raw/articles/unity-ecs-manual/](../../raw/articles/unity-ecs-manual/README.md) — local mirror, fetched 2026-05-05 (~140 pages, 760 KB)
- [Packages/manifest.json](../../Packages/manifest.json) — confirms `com.unity.feature.ecs@1.0.0` is installed
