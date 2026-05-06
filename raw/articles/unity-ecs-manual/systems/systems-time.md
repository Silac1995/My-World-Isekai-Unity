---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/systems-time.html
fetched: 2026-05-05
section: systems
---

# Time Considerations

A world manages the [`Time`](../api/Unity.Entities.ComponentSystemBase.Time.html#Unity_Entities_ComponentSystemBase_Time) property for its [systems](concepts-systems.html). This property represents the current world time.

By default, Unity generates a [`TimeData`](../api/Unity.Core.TimeData.html) entity per world. An [`UpdateWorldTimeSystem`](../api/Unity.Entities.UpdateWorldTimeSystem.html) instance maintains this, tracking the elapsed duration from the previous frame.

## Fixed Step Simulation Behavior

Systems within the [`FixedStepSimulationSystemGroup`](../api/Unity.Entities.FixedStepSimulationSystemGroup.html) operate distinctly. Rather than executing once per frame at current delta time, these systems update at consistent intervals. This may result in multiple updates per frame when the fixed interval is sufficiently small relative to frame duration.

## Manual Time Control

For enhanced time management, several methods are available:

- Use [`World.SetTime`](../api/Unity.Entities.World.SetTime.html) to assign a specific time value directly
- Employ [`PushTime`](../api/Unity.Entities.World.PushTime.html#Unity_Entities_World_PushTime_Unity_Core_TimeData_) to temporarily override world time
- Call [`PopTime`](../api/Unity.Entities.World.PopTime.html#Unity_Entities_World_PopTime) to restore the prior time from the time stack

---

## Outgoing Links

- https://docs.unity3d.com/Manual/TermsOfUse.html - Trademarks and terms of use
- https://unity.com/legal - Legal
- https://unity.com/legal/privacy-policy - Privacy Policy
- https://unity.com/legal/cookie-policy - Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information - Do Not Sell or Share My Personal Information
