---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/systems-manage-structural-changes-intro.html
fetched: 2026-05-05
section: systems
---

# Manage Structural Changes Introduction

The entity component system (ECS) provides three primary approaches for managing structural changes in your project:

1. **Entity Command Buffers (ECB)** - defer data changes for later playback
2. **EntityManager methods** - manage data changes directly on the main thread
3. **Enableable components** - toggle components on and off without structural modification

## Entity Command Buffer vs EntityManager

### Key Differences

When working with jobs, you must use ECBs since "the job system doesn't allow you to schedule or parallelize jobs that use `EntityManager`" or execute them anywhere except the main thread.

For main thread operations, choose based on timing needs:
- **Immediate execution**: Use EntityManager methods
- **Deferred execution**: Use ECB (after job completion, for example)

### Important Considerations

The documentation emphasizes that "changes recorded in an ECB only are applied when `Playback` is called on the main thread." Attempting further modifications after playback triggers an exception.

Using `EntityQuery` with EntityManager methods is most efficient since "the method can operate on whole chunks rather than individual entities."

### Performance Trade-offs

ECBs allow scheduling playback at existing frame sync points, avoiding new synchronization overhead. EntityManager creates a new sync point with each structural change. However, merging sync points is possible if the EntityManager-using system executes before or after an EntityCommandBufferSystem without intervening jobs.

## Enableable Components Alternative

Enabling and disabling components offers faster performance than component addition/removal when changes occur frequently. However, they may impact job and system performance for affected archetypes. If changes are infrequent, traditional component addition/removal might optimize chunk fragmentation and cache usage better.

### Decision Framework

Examine the Systems window and CPU Usage Timeline view in the Profiler to determine the optimal approach for your specific use case.

---

## Outgoing Links

- https://docs.unity3d.com/ - docs.unity3d.com
- ../logo.svg - Logo
- ../index.html - Home
- concepts-structural-changes.html - Structural Changes Concepts
- systems-entity-command-buffers.html - Entity Command Buffers Overview
- systems-entitymanager.html - EntityManager Overview
- ../api/Unity.Entities.EntityCommandBuffer.Playback.html - EntityCommandBuffer.Playback API
- performance-sync-points.html - Sync Points Documentation
- editor-systems-window.html - Systems Window Editor
- https://docs.unity3d.com/6000.4/Documentation/Manual/ProfilerCPU.html - CPU Usage Timeline View
- components-enableable.html - Enableable Components
- structural-changes-enableable-components.html - Structural Changes with Enableable Components
- https://docs.unity3d.com/Manual/TermsOfUse.html - Trademarks and Terms of Use
- https://unity.com/legal - Legal
- https://unity.com/legal/privacy-policy - Privacy Policy
- https://unity.com/legal/cookie-policy - Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information - Do Not Sell or Share My Personal Information
