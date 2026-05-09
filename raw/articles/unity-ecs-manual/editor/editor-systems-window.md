---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/editor-systems-window.html
fetched: 2026-05-05
section: editor
---

# Systems Window Reference

The Systems window provides insights into the system update order within each world of your project. It displays a hierarchical view of systems organized by their system groups and refreshes as systems execute and update throughout your application.

## Opening the Systems Window

Navigate to **Window > Entities > Systems** to access this interface.

## Window Contents

The Systems window presents the following columns:

| Column | Description |
|--------|-------------|
| **Systems** | Lists all systems in your application. Selecting a system reveals its details in the Inspector window. System types are indicated by icons: folder (system group), hexagon with arrows (system), forward arrow (entity command buffer at group start), backward arrow (entity command buffer at group end). |
| **World** | Identifies the world where the system operates. Note that the calling world and operating world may differ, as the ECS framework automatically ticks only the Main World during Play mode and the Editor World during Edit mode. Custom worlds require explicit setup to run automatically. |
| **Namespace** | Shows the namespace containing the system type. |
| **Entity Count** | Displays the quantity of entities matching the system's queries at frame end. |
| **Time (ms)** | Reports the milliseconds consumed by the system during the current frame. |

## Feature Options

**Column Management**: Access the More menu (⋮) to show or hide specific columns.

**System Disabling**: Click the leftmost column (darker than others) adjacent to any system to temporarily disable it. This change applies only to the current Editor session.

**Full Player Loop Display**: Enable "Show Full Player Loop" in the More menu (⋮) to view all low-level Unity player loop methods, including non-Entities components. These non-Entities items appear grayed out and cannot be inspected further.

## Related Resources

- [System user manual](concepts-systems.html)
- [System Inspector reference](editor-system-inspector.html)
- [World user manual](concepts-worlds.html)
- [Entity Query user manual](systems-entityquery.html)

---

## Outgoing Hyperlinks

- https://docs.unity3d.com/Manual/TermsOfUse.html | Trademarks and terms of use
- https://unity.com/legal | Legal
- https://unity.com/legal/privacy-policy | Privacy Policy
- https://unity.com/legal/cookie-policy | Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information | Do Not Sell or Share My Personal Information
- https://docs.unity3d.com/ScriptReference/LowLevel.PlayerLoop.html | player loop
