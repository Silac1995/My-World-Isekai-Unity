---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/scheduling-jobs-dependencies.html
fetched: 2026-05-05
section: systems
---

# Job dependencies

Unity examines data dependencies of each system based on ECS components that systems read and write. Systems that schedule jobs typically depend on previously scheduled system jobs according to component access patterns. When one system schedules a job reading a component and a later system schedules a job writing that component, the latter job depends on the former. The job scheduler ensures all dependent jobs finish before running a system's jobs, preventing race conditions.

## Dependency property overview

The update order and read/write access determine a job's dependencies. At system execution start, Unity calculates the initial [`Dependency`](../api/Unity.Entities.SystemBase.Dependency.html#Unity_Entities_SystemBase_Dependency) property value by combining dependent systems' job handles. This enables scheduling jobs with correct dependencies. As jobs schedule, Unity combines their handles and stores them in the system's `Dependency` property, extending the dependency chain. Subsequent executing systems then know which jobs this one scheduled.

Unity calculates the initial `Dependency` property by combining `Dependency` handles from previously executed systems that wrote components the system needs to read or write. It also combines system handles that read components the system needs to write to. For additional information, see the [`SystemState` API documentation](../api/Unity.Entities.SystemState.html).

**Note:** Since this system dependency approach operates at system level, jobs may wait for other jobs accessing components the original jobs don't require. Unity is exploring mitigation approaches for this known issue.

The diagram below shows an example where a job waits for an unnecessary dependency. Green arrows represent explicitly scheduled jobs and dependencies in each system. Red arrows represent job dependencies generated when scheduling with the `Dependency` property (default `Schedule()` method dependency). Dashed borders represent read-only jobs.

![Job dependency diagram depicting one system writing to two jobs, and another system reading one of the jobs.](images/job-dependencies.png)

`System1` schedules two jobs: one writing to `ComponentA`, another writing to `ComponentB`. Using default chaining, `System1`'s `Dependency` property contains the `Write B` job handle, which depends on `Write A`. Later, `System2` schedules a job reading `ComponentA`. `System2`'s jobs must wait for both `System1` jobs completing, even if `System2` doesn't access `ComponentB`.

The `Read A` job unnecessarily waits for the `Write B` job. To eliminate this unneeded dependency, `System1` could schedule only the `Write B` job, then `System2` schedules both `Write A` and `Read A` jobs.

## `Dependency` property

A system's [`Dependency`](../api/Unity.Entities.SystemBase.Dependency.html#Unity_Entities_SystemBase_Dependency) property is a [`JobHandle`](https://docs.unity3d.com/ScriptReference/Unity.Jobs.JobHandle.html) representing the system's ECS-related dependencies. Before [`OnUpdate()`](../api/Unity.Entities.SystemBase.OnUpdate.html), the `Dependency` property reflects incoming dependencies on prior jobs. By default, the system updates `Dependency` based on components each job reads and writes as jobs schedule within a system.

## Override the default dependency structure

To override the default dependency structure, use the `Schedule` method in jobs inheriting from [`IJobEntity`](../api/Unity.Entities.IJobEntity.html). You can use `Schedule` implicitly or explicitly, but explicit use means ECS doesn't automatically combine job handles with the system's `Dependency` property. You must manually combine them when necessary.

The `Dependency` property doesn't track dependencies jobs might have on data passed through a [`NativeArray`](https://docs.unity3d.com/ScriptReference/Unity.Collections.NativeArray_1.html) or similar containers. If one job writes a `NativeArray` and another reads it, you must manually add the first job's `JobHandle` as the second job's dependency. Use [`JobHandle.CombineDependencies`](https://docs.unity3d.com/ScriptReference/Unity.Jobs.JobHandle.CombineDependencies.html) for this purpose.

## Additional resources

* [JobHandle and dependencies](https://docs.unity3d.com/ScriptReference/Unity.Jobs.JobHandle.CombineDependencies.html)
* [Unity's job system](https://docs.unity3d.com/6000.4/Documentation/Manual/job-system.html)

---

## Outgoing hyperlinks

1. https://docs.unity3d.com/ScriptReference/Unity.Entities.SystemBase.Dependency.html#Unity_Entities_SystemBase_Dependency - Dependency property API
2. https://docs.unity3d.com/ScriptReference/Unity.Jobs.JobHandle.html - JobHandle class
3. https://docs.unity3d.com/ScriptReference/Unity.Entities.SystemBase.OnUpdate.html - OnUpdate method
4. https://docs.unity3d.com/ScriptReference/Unity.Entities.IJobEntity.html - IJobEntity interface
5. https://docs.unity3d.com/ScriptReference/Unity.Collections.NativeArray_1.html - NativeArray class
6. https://docs.unity3d.com/ScriptReference/Unity.Jobs.JobHandle.CombineDependencies.html - JobHandle.CombineDependencies method
7. https://docs.unity3d.com/api/Unity.Entities.SystemState.html - SystemState API documentation
8. https://en.wikipedia.org/wiki/Race_condition - Race condition definition
