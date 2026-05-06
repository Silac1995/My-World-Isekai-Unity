---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/profiler-modules-entities-introduction.html
fetched: 2026-05-05
section: performance
---

# Entities Profiler Modules

The Unity Profiler enables developers to analyze application performance and gather insights into their Entities code efficiency. The documentation describes two specialized profiler modules available for this purpose.

## Available Profiler Modules

The Entities framework provides two profiling tools:

1. **Entities Structural Changes**: Tracks when the ECS framework creates and destroys Entities and Components, providing visibility into structural modifications.

2. **Entities Memory**: Monitors memory consumption of Archetypes on a per-frame basis, helping identify memory bottlenecks.

## Accessing the Profiler

To launch the profiler, navigate to **Window > Analysis > Profiler**. The Entities modules display by default, though users can toggle them using the **Profiler Modules** dropdown menu.

## Important Considerations

A critical note warns that "The Profiler doesn't collect any data for modules that aren't enabled. If you enable a module after profiling your application, the newly enabled Profiler modules won't display any data."

Profiling can occur in multiple contexts: Play mode, development builds on target devices, or the Unity Editor itself (useful for measuring editor overhead and evaluating Conversion code performance in Edit mode).

---

## Outgoing Hyperlinks

- https://docs.unity3d.com/6000.4/Documentation/Manual/Profiler.html - Profiler window
- https://docs.unity3d.com/6000.4/Documentation/Manual/profiler-profiling-applications.html - Profiling your application
- https://docs.unity3d.com/Manual/TermsOfUse.html - Trademarks and terms of use
- https://unity.com/legal - Legal
- https://unity.com/legal/privacy-policy - Privacy Policy
- https://unity.com/legal/cookie-policy - Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information - Do Not Sell or Share My Personal Information
