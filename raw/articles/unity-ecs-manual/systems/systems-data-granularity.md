---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/systems-data-granularity.html
fetched: 2026-05-05
section: systems
---

# Data Granularity

## Overview

The documentation explains that fine-grained queries are best achieved by constructing entities from numerous small components rather than using a handful of large ones. Small components provide better CPU cache utilization since cache lines contain only necessary data, whereas larger components may fetch unnecessary fields.

However, there's a trade-off: excessive component granularity introduces overhead when managing entity queries, archetypes, and other internal processes due to the increased number of components to process.

## Read-Only Data

Declare data used as input but not modified during a job as `ReadOnly`. This approach offers two key benefits:

1. Enables safe parallelization of system jobs that read the data
2. Provides the job scheduler additional flexibility in arranging jobs from different systems and scripts for optimal CPU thread utilization

For permanently immutable data throughout an application's lifetime, blob assets provide an efficient storage solution. These immutable data structures reside in unmanaged memory and can contain structs, arrays of blittable data, and strings via `BlobString`.

Blob assets are deserialized significantly faster than assets like ScriptableObjects since Unity stores them on disk in their exact memory format. Additionally, they don't occupy chunk space, preventing chunk fragmentation and avoiding interference with mutable component data processing.

## Reactive Systems

Read-only declarations become critical in projects with reactive systems that use change filters. Iterating component data with read/write access marks chunks and entities as changed, triggering reactive systems even when data remains unmodified.

Separate read-only data into different components from read/write data to prevent unnecessary reactive system execution. The `IJobEntityChunkBeginEnd` interface in `IJobEntity` jobs enables chunk pre-evaluation, preventing reactive system triggering even with write access needs while allowing skipping chunks before requesting write access.

Use the Profiler to compare implementations with and without reactive systems for your specific application needs.

## Outgoing Links

- https://docs.unity3d.com/6000.4/Documentation/ScriptReference/Unity.Collections.ReadOnlyAttribute.html - ReadOnly Attribute
- https://docs.unity3d.com/6000.4/Documentation/Manual/Profiler.html - Unity Profiler
- https://docs.unity3d.com/Manual/TermsOfUse.html - Terms of Use
- https://unity.com/legal - Legal
- https://unity.com/legal/privacy-policy - Privacy Policy
- https://unity.com/legal/cookie-policy - Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information - Do Not Sell or Share My Personal Information
