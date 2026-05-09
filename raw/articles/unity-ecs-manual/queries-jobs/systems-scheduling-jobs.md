---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/systems-scheduling-jobs.html
fetched: 2026-05-05
section: queries-jobs
---

# Job System in Entities Introduction

The Entities package leverages the job system to enable multithreaded code development. Performance-conscious developers should prioritize using jobs whenever feasible.

## Available Techniques

The following approaches are available based on your specific access requirements:

- **Job scheduling**: Utilize `IJobEntity` for convenient job scheduling needs. Refer to the iteration documentation for additional details.

- **Manual scheduling outside the main thread**: Use `IJobChunk`'s `Schedule()` and `ScheduleParallel()` methods to handle data transformation outside the main thread.

## Job Dependencies

When scheduling jobs, ECS automatically tracks which systems read and write to specific components. Subsequent systems' `Dependency` properties automatically include job handles from earlier systems when component read/write operations overlap.

## Main Thread Access

For main thread access outside the job system, employ a `foreach` statement over `Query` objects available in `SystemAPI`.

---

## References

- https://docs.unity3d.com/6000.4/Documentation/Manual/job-system.html - Job system introduction
- ../api/Unity.Entities.IJobEntity.html - IJobEntity API
- iterating-data-ijobentity.html - Iterate over component data in multiple systems
- iterating-data-ijobchunk.html - Iterate over chunks of component data
- ../api/Unity.Entities.SystemAPI.html - SystemAPI
- systems-systemapi.html - SystemAPI overview
- scheduling-jobs-dependencies.html - Job dependencies
