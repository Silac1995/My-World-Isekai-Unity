---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/job-overhead.html
fetched: 2026-05-05
section: systems
---

# Job Scheduling Overhead

## Overview

Every job carries a small CPU overhead, regardless of whether it runs on a single thread or multiple threads. "Before a job can run, Unity must allocate thread memory and copy data so that the job has access to the data it needs to process."

While this overhead is minimal compared to traditional multithreading systems, it becomes noticeable when applications schedule numerous short-lived jobs.

## Identifying Scheduling Overhead

To determine if scheduling costs exceed execution time, use the Profiler to examine the system's profiler marker. If the system marker is larger than the job's marker, you likely have scheduling overhead issues. Third-party profiling tools can provide additional detail about scheduling method durations.

## Reducing Scheduling Overhead

Several strategies minimize overhead:

- **Consolidate related jobs**: Combine jobs operating on similar datasets into single, larger jobs
- **Adjust parallelization**: Replace multi-threaded parallel scheduling with single-thread execution when per-thread work is minimal
- **Batch operations**: Group jobs operating on identical components into one job performing multiple operations on cached data

## Main Thread Overhead Considerations

Running work synchronously on the main thread can introduce sync points when other scheduled jobs need access to the same data. Job system overhead may be preferable to sync points in such cases.

Run code on the main thread only when:

- Prototyping before optimizing with jobs
- Manipulating minimal data with no cross-job dependencies
- Performing main-thread-only operations (structural changes, GameObject interactions, core Unity engine APIs)

Use idiomatic `foreach` loops instead of the job system in these situations—jobs carry additional CPU overhead the job dependency system's race condition guards introduce.

## Job Worker Configuration

Configure worker threads using [`JobUtility.JobWorkerCount`](https://docs.unity3d.com/6000.4/Documentation/ScriptReference/Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobWorkerCount.html). Balance thread count to handle required work without CPU bottlenecks or excessive idle time. As Unity versions advance, overhead reduction changes may shift optimal worker counts, making target hardware experimentation essential.

## Measuring Optimization Results

Use the Profiler's CPU Usage module Timeline view to track worker thread time allocation. Native profiling tools (Instruments, Windows Performance Recorder, Superluminal) can compare scheduling duration against execution time and measure parallelism effectiveness.

## Related Resources

- [The job system](https://docs.unity3d.com/6000.4/Documentation/Manual/job-system.html)
- [Job dependencies](scheduling-jobs-dependencies.html)
- [The Profiler](https://docs.unity3d.com/6000.4/Documentation/Manual/Profiler.html)
- [Profiler introduction](https://docs.unity3d.com/6000.4/Documentation/Manual/profiler-introduction.html)
- [Performance profiling tools](https://docs.unity3d.com/6000.4/Documentation/Manual/performance-profiling-tools.html)
- [CPU Usage Profiler module Timeline view](https://docs.unity3d.com/6000.4/Documentation/Manual/ProfilerCPU.html)
