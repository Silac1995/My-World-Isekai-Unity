---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/components-nativecontainers.html
fetched: 2026-05-05
section: components
---

# Native Container Component Support

## Overview

When developing game and engine code, developers often need to maintain and update persistent data structures that may be accessed by multiple systems. A common pattern involves placing such containers on [Singleton components](components-singleton.html).

## Safety Restrictions

Native containers placed on components have specific constraints to maintain safety. The key restriction is that you cannot schedule jobs against these components using [IJobChunk](iterating-data-ijobchunk.html) or [IJobEntity](iterating-data-ijobentity.html). This limitation exists because these job types already access the components through containers, and the job safety system cannot efficiently scan for nested containers during scheduling without significantly impacting performance.

## Recommended Approach

Instead of scheduling jobs against components containing native containers, the recommended pattern is to:

1. Obtain the component on the main thread
2. Extract the native container from the component
3. Schedule the job directly against the container itself

The Singleton functions are purpose-built for this use case. They are designed to avoid completing unnecessary job dependencies, enabling you to chain multiple jobs across different systems while operating on the same container in a singleton component. This approach prevents unnecessary synchronization points between systems.

---

## Outgoing Links

- [Singleton Components](components-singleton.html)
- [IJobChunk Documentation](iterating-data-ijobchunk.html)
- [IJobEntity Documentation](iterating-data-ijobentity.html)
- [Unity Trademarks and Terms of Use](https://docs.unity3d.com/Manual/TermsOfUse.html)
- [Unity Legal](https://unity.com/legal)
- [Privacy Policy](https://unity.com/legal/privacy-policy)
