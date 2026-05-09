---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/ecs-workflow-intro.html
fetched: 2026-05-05
section: workflow
---

# Understand the ECS Workflow

The entity component system (ECS) framework in Unity requires a different approach than traditional object-oriented development. Understanding this workflow is essential before starting an ECS project.

## Create a Subscene

ECS applications use subscenes to organize content. "You add GameObjects and MonoBehaviour components to a subscene, and bakers convert the GameObjects and MonoBehaviour components into entities and ECS components."

## Create ECS Components

Components serve as data storage for applications. "To create behavior in your application, systems provide logic that reads from and writes to ECS component data." The framework emphasizes data-oriented design, making it advisable to plan your data structure and establish components before developing systems or entities.

Different component types exist for various purposes, detailed in the Component types documentation.

## Create Entities

Entities represent individual objects within an application. The typical creation process involves:

- Adding GameObjects to a subscene in the Editor
- The baking process converts these GameObjects into entities
- Optionally, create bakers to attach ECS components to converted entities
- MonoBehaviour components used in this context are called authoring components

**Tip:** "It's a good organizational practice to append `Authoring` to the class name of any authoring components you create."

You may also instantiate entities at runtime using spawner systems.

## Create Systems

Systems implement application behavior by querying and modifying component data, managing entities, and adjusting components. Different system types serve distinct purposes.

## Optimize Systems

By default, system code executes synchronously on the main thread. For systems processing many entities, "it's best practice to create Burst-compatible jobs, and schedule them to run in parallel when possible."

For systems with minimal workload, the scheduling overhead may outweigh performance benefits. Use the CPU profiler to evaluate performance with and without multi-threading. Optimization strategies include:

- Running jobs on the main thread
- For unmanaged ISystem instances, replacing jobs with `SystemAPI.Query` and standard `foreach` loops, then applying the `BurstCompile` attribute

## Additional Resources

- ECS workflow examples

---

## Outgoing Hyperlinks

- http://docs.unity3d.com/
- ../logo.svg
- ../index.html
- conversion-subscenes.html
- baking-baker-overview.html
- concepts-components.html
- concepts-systems.html
- components-type.html
- baking-overview.html
- ecs-workflow-tutorial.html
- concepts-worlds.html
- https://docs.unity3d.com/Packages/com.unity.burst@latest/index.html
- https://docs.unity3d.com/6000.0/Documentation/Manual/JobSystem.html
- https://docs.unity3d.com/6000.0/Documentation/Manual/Profiler.html
- ../api/Unity.Entities.IJobEntityExtensions.Run.html
- systems-isystem.html
- ../api/Unity.Entities.SystemAPI.Query.html
- https://docs.unity3d.com/Packages/com.unity.burst@latest/index.html?subfolder=/manual/compilation-burstcompile.html
- https://docs.unity3d.com/Manual/TermsOfUse.html
- https://unity.com/legal
- https://unity.com/legal/privacy-policy
- https://unity.com/legal/cookie-policy
- https://unity.com/legal/do-not-sell-my-personal-information
