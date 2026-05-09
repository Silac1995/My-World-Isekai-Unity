---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/editor-preferences.html
fetched: 2026-05-05
section: editor
---

# Entities Preferences Reference

The **Preferences** window accessible via **Unity > Settings** in the Unity Editor contains configuration options specific to the Entities system across several categories.

## Hierarchy Window

| Property | Description |
|----------|-------------|
| **Update Mode** | Controls hierarchy window refresh behavior: "Synchronous" updates in a blocking manner with always-current data but potential performance impact; "Asynchronous" updates non-blockingly across multiple frames with possible stale data but minimal performance cost |
| **Minimum Milliseconds Between Hierarchy Update Cycle** | Defines minimum wait time (in milliseconds) between hierarchy refresh cycles; higher values reduce update frequency and performance impact |
| **Exclude Unnamed Nodes For Search** | Filters unnamed entities from string search results to improve search speed when dealing with many unnamed entities |

## Advanced

| Property | Description |
|----------|-------------|
| **Show Advanced Worlds** | Toggles visibility of specialized worlds (such as Staging world or Streaming world) in world dropdown menus |

## Journaling

| Property | Description |
|----------|-------------|
| **Enabled** | Activates recording of Journaling data |
| **Total Memory MB** | Sets memory allocation (in megabytes) for Journaling records; older records are overwritten once capacity is reached |
| **Post Process** | Enables post-processing of journaling data, including converting `GetComponentDataRW` to `SetComponentData` where applicable |

## Baking

| Property | Description |
|----------|-------------|
| **Scene View Mode** | Selects data mode for Scene view: either "Authoring Data" or "Runtime Data" |
| **Live Baking Logging** | Outputs logging of live baking triggers to assist in diagnosing baking activation |
| **Clear Entity cache** | Forces re-baking of all Sub Scenes on next Editor load or standalone player build |

## Systems Window

| Property | Description |
|----------|-------------|
| **Show 0s in Entity Count And Time Column** | Displays `0` when systems match no entities; disabling shows nothing instead |
| **Show More Precision For Running Time** | Increases system runtime precision from 2 to 4 decimal places in the `Time (ms)` column |

---

## Outgoing Links

- http://docs.unity3d.com/ - docs.unity3d.com
- https://docs.unity3d.com/Manual/TermsOfUse.html - Trademarks and terms of use
- https://unity.com/legal - Legal
- https://unity.com/legal/privacy-policy - Privacy Policy
- https://unity.com/legal/cookie-policy - Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information - Do Not Sell or Share My Personal Information
