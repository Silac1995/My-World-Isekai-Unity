---
type: concept
title: "Unity ECS / DOTS"
tags: [unity, ecs, dots, entities, performance, architecture]
created: 2026-05-05
updated: 2026-05-05
posture: "ECS is on the table for new systems but gated; default for any new system is MonoBehaviour"
sources:
  - "raw/articles/unity-ecs-manual/concepts/concepts-intro.md"
  - "raw/articles/unity-ecs-manual/concepts/concepts-ecs.md"
  - "raw/articles/unity-ecs-manual/concepts/concepts-archetypes.md"
related:
  - "[[unity-ecs-manual]]"
  - "[[performance-conventions]]"
status: draft
confidence: medium
---

# Unity ECS / DOTS

## Summary

Unity's Entity Component System (ECS) — formally the **Entities** package, part of the Data-Oriented Technology Stack (DOTS) — is an alternative programming model to the GameObject pipeline, optimised for cache-coherent linear iteration over large numbers of similar data records. We have it installed (`com.unity.feature.ecs@1.0.0` since 2026-05-05) but no project code uses it yet. This page is the architectural mental model; the full reference manual lives at [[unity-ecs-manual]].

## Definition

ECS describes any architecture built from three orthogonal pieces:

- **Entity** — a unique, lightweight identifier. Holds no data and no code. Conceptually an integer + version, not an object.
- **Component** — a struct of pure data attached to an entity. Drives behaviour by its presence or absence, not by methods on it. A component is *what an entity has*.
- **System** — code that queries for entities matching a component signature and processes their data in bulk. A system is *what happens to entities that have certain components*.

In Unity's specific implementation (Entities 6.4):

- **Archetype** — every unique set of component types forms an archetype. Entities sharing the same set of components share an archetype, and therefore share the same memory layout — a contiguous **16 KiB chunk** of struct-of-arrays storage. This is what makes ECS fast: iterating "all entities with components A and B" walks one chunk's tightly-packed memory in cache order.
- **World** — a container for entities, archetypes, chunks, and systems. Default world is created automatically; multiple worlds can coexist (e.g. main + baking + netcode prediction).
- **Structural change** — adding/removing a component, creating/destroying an entity, or modifying a shared component value. Each one moves the entity to a different archetype, requires the job scheduler to drain (a *sync point*), and is the dominant cost in real ECS code.
- **SystemBase / ISystem** — two flavours of system: managed (`SystemBase`, allows GC, easier) vs unmanaged (`ISystem`, Burst-compilable, no GC, fastest). `SystemAPI` is the unified entry surface inside both.
- **Baking** — the editor-time process that converts authoring GameObjects (in a SubScene) into ECS components. One-way; runtime entities cannot be edited back into authoring GameObjects.
- **Burst** — the Mono-incompatible compiler that turns `[BurstCompile]` jobs and `ISystem`s into native SIMD code. ECS is designed around it; opting out of Burst sacrifices most of the performance argument.
- **Job system** — the standard Unity C# Jobs runtime. ECS adds `IJobEntity` / `IJobChunk` / `IJobEntityChunkBeginEnd` flavours that walk archetype chunks in parallel.
- **Entity Command Buffer (ECB)** — a deferred-mutation queue. Inside a job (which can't perform structural changes), record the change to an ECB; an ECB system plays it back on the main thread at a sync point.

## Context

ECS is **installed but unused** in the project as of 2026-05-05. This page exists so we have a shared mental model when the question "should this be ECS?" comes up.

Where it might fit in the project:
- [[world-macro-simulation]] / Macro-Simulator catch-up — pure integer math over many records, no live GameObjects, no networking, no physics. Textbook ECS.
- [[terrain-grid]] / [[character-terrain]] — per-cell weather, vegetation, transition systems running across thousands of cells.
- Future ambient crowd rendering (city NPCs, distant villagers).

Where it explicitly does **not** fit:
- The [[character]] system — facade + child-hierarchy is GameObject-based by design (rule #20, ICharacterVisual archetype abstraction, NGO networking). Splitting an actor across both worlds is not on the table.
- The [[network]] stack — Netcode for GameObjects is the project's authority. Netcode for Entities would require a parallel networked actor pipeline.
- Anything where the prefab + Inspector workflow is the primary content-authoring affordance for non-engineers.

## Decision gate — when to reach for ECS

The project is GameObject-based. **Default for any new system is MonoBehaviour.** ECS is the exception, not the baseline. CLAUDE.md rule #35 makes this enforceable; this section is the operational gate.

### Use ECS (Entities + `ISystem` / `SystemBase`) when ALL hold:

- Iterating **~1000+ entities** per frame or per tick.
- **No NGO replication required** (or pure server-side simulation that doesn't need to mirror to clients). We use `com.unity.netcode.gameobjects`; Netcode for Entities is a *separate stack*, not interop.
- **No Inspector / prefab / ScriptableObject** as the primary authoring workflow for the data.
- **No `ICharacterVisual` / Spine 2D binding** required — entities can't carry a Spine skeleton sensibly.
- **No Character / Building / Item / Furniture / Order entanglement** — these systems are GameObject-shaped by rule #20 / #22.
- Workload has a clear "**row of data, single transformation**" shape (struct-of-arrays, no irregular branching, no event-driven state machine).
- **Profiler confirms** a real bottleneck *or* target N is too large for plain `[BurstCompile]` + `NativeArray<T>` to handle.

If any one of these fails, ECS is the wrong tool.

### Use Burst + Jobs *without* ECS when:

- N is moderate (~100-1000), data shape is contiguous, the inner loop is mathy.
- Workload bridges to GameObject state, but the hot inner section can run isolated and `JobHandle.Complete()` cleanly at the end.
- A single `[BurstCompile]` `IJob` / `IJobParallelFor` over `NativeArray<T>` is enough — no archetype filtering, no chunk iteration, no entity lifetimes to manage.
- **This is the highest-ROI path when "we need this faster" comes up.** Most of the speedup people attribute to ECS is actually Burst + jobs over flat arrays. You get 70% of the win for 10% of the architectural commitment.

### Stay on plain MonoBehaviour / coroutine when:

- N is small (<100).
- Logic is irregular, event-driven, or state-machine-shaped.
- Tight Inspector / NGO / Spine / Character / Order integration is needed.
- Authoring is content-team-driven (prefabs, ScriptableObjects, level designers, etc.).
- The system already works at its current scale — *don't refactor for the principle*.

### Currently-flagged candidates (no commitment until profiled)

- **Macro-Simulation catch-up** — pure offline math over hibernated NPCs (rule #30). High-N, server-only, no GameObjects, no networking. Textbook fit. Try Burst+Jobs first; escalate to ECS only if N truly explodes.
- **Per-cell terrain / weather / vegetation ticks** at scale — thousands of `TerrainCell`s, identical struct, identical update.
- **Future ambient crowd rendering** — city background NPCs past ~500 concurrent, rendered via Entities Graphics. Separate from named NPCs.
- **Future bulk VFX** — projectiles, damage numbers, particle clouds at scale. None exist yet.

### Permanent non-fits (do not consider)

- The **Character system** — facade + child hierarchy is GameObject-shaped by design (rule #20). `ICharacterVisual` archetype abstraction, NGO networking, Spine 2D, `ICharacterSaveData<T>` portable profiles, `CharacterAction` parity (rule #22) — none translate.
- **Buildings, items, furniture, interactables, orders, quests** — Inspector-authored, NGO-replicated, prefab-instantiated. Same conclusion.
- **UI, dialogue, combat actions, character orders, GOAP planning** — irregular, event-driven, low N.
- **Anything currently working well at its current scale.** "ECS mindset" is the gate, not a refactor mandate.
- [[performance-conventions]] — project-wide performance rules. ECS is a tool that shares the same goals (no per-frame allocations, cache-friendly layout, explicit allocators, dirty-flag gating) but at a much deeper structural level. Adopting ECS for a workload commits to those rules at the language level rather than by convention.
- [[network]] — DOTS adoption interacts with the netcode choice. We use NGO, so Netcode for Entities is *not implied* by installing ECS.

## Examples

**Idiomatic ECS workload (good fit):**
> A daily macro-simulation tick walks every hibernated NPC's needs (`HungerComponent`, `RestComponent`, `MoraleComponent`), advances each one by `daysElapsed * decayRate`, and clamps to `[0, 1]`. No physics, no rendering, no GameObjects. 50,000 NPCs in <1 ms with `IJobEntity` + Burst.

**Anti-fit (don't):**
> A Character with skeletal animation, an inventory UI, NGO replication, and an interactable detector. The system already works as a GameObject facade with child subsystems ([[character]]), and ECS would force a parallel pipeline for everything from animation to UI binding to networking.

**Mixed (proceed carefully):**
> Vegetation: thousands of grass / wheat / berry instances on a terrain. Authoring as GameObject prefabs in a SubScene, baking into ECS components, rendering via Entities Graphics — likely net-positive once instance counts get high enough (≥10k). Below that, plain GPU instancing is simpler and sufficient.

## Links

- [[unity-ecs-manual]] — reference for the official manual
- [[network]] — current netcode stack (NGO, not Netcode for Entities)
- [[character]] — explicitly non-ECS actor system
- [[performance-conventions]] — project performance rules
- [[world-macro-simulation]] — likely first ECS adopter

## Sources

- [raw/articles/unity-ecs-manual/concepts/concepts-intro.md](../../raw/articles/unity-ecs-manual/concepts/concepts-intro.md) — concepts hub
- [raw/articles/unity-ecs-manual/concepts/concepts-ecs.md](../../raw/articles/unity-ecs-manual/concepts/concepts-ecs.md) — three-part mental model
- [raw/articles/unity-ecs-manual/concepts/concepts-entities.md](../../raw/articles/unity-ecs-manual/concepts/concepts-entities.md) — what an entity is
- [raw/articles/unity-ecs-manual/concepts/concepts-components.md](../../raw/articles/unity-ecs-manual/concepts/concepts-components.md) — component types
- [raw/articles/unity-ecs-manual/concepts/concepts-systems.md](../../raw/articles/unity-ecs-manual/concepts/concepts-systems.md) — SystemBase / ISystem
- [raw/articles/unity-ecs-manual/concepts/concepts-worlds.md](../../raw/articles/unity-ecs-manual/concepts/concepts-worlds.md) — World concept
- [raw/articles/unity-ecs-manual/concepts/concepts-archetypes.md](../../raw/articles/unity-ecs-manual/concepts/concepts-archetypes.md) — archetypes + 16 KiB chunks
- [raw/articles/unity-ecs-manual/concepts/concepts-structural-changes.md](../../raw/articles/unity-ecs-manual/concepts/concepts-structural-changes.md) — sync points
- [raw/articles/unity-ecs-manual/concepts/concepts-safety.md](../../raw/articles/unity-ecs-manual/concepts/concepts-safety.md) — job safety system
- [Packages/manifest.json](../../Packages/manifest.json) — `com.unity.feature.ecs@1.0.0`
- 2026-05-05 conversation with Kevin — request to fetch and integrate the docs after installing the ECS package
- 2026-05-05 conversation with Kevin — adopted "ECS-as-gated-tool" posture; CLAUDE.md rule #35 added; decision gate codified in this page
