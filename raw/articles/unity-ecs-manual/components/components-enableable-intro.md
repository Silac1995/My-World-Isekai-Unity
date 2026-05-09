---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/components-enableable-intro.html
fetched: 2026-05-05
section: components
---

# Enableable Components Introduction

## Overview

You can use enableable components on `IComponentData` and `IBufferElementData` components to disable or enable individual components on an entity at runtime. To make components enableable, inherit them from `IEnableableComponent`.

## When to Use Enableable Components

Enableable components are particularly useful for:

- **Frequent state changes**: States that change often and unpredictably, or where the number of state permutations are high on a frame-by-frame basis.
- **Reducing structural changes**: Using enableable components can help avoid structural changes in certain situations compared to traditional add/remove approaches.
- **Tag component replacement**: Using enableable components instead of multiple zero-size tag components to represent entity states, which reduces the number of unique entity archetypes and improves chunk usage to lower memory consumption.

## Best Practices

"Adding and removing components is the preferable way to manage components for low-frequency state changes, where you expect the state to persist for many frames."

## Related Resources

- Use enableable components
- Look up arbitrary data
- Manage structural changes with enableable components

---

## Outgoing Links

- http://docs.unity3d.com/ - docs.unity3d.com
- https://docs.unity3d.com/Manual/TermsOfUse.html - Trademarks and terms of use
- https://unity.com/legal - Legal
- https://unity.com/legal/privacy-policy - Privacy Policy
- https://unity.com/legal/cookie-policy - Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information - Do Not Sell or Share My Personal Information
