---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/concepts-systems.html
fetched: 2026-05-05
section: concepts
---

# System Concepts

## Overview

A **system** contains the logic that transforms component data from one state to the next. For instance, a system might update entity positions based on velocity and elapsed time.

![Conceptual diagram showing Entity A and B with shared components (Speed, Direction, Position, Renderer), Entity C with Speed, Direction, and Position. A system in the center manipulates these components.](images/entities-concepts.png)

*A system with logic that determines the position of entities.*

Systems execute on the main thread once per frame and are organized into hierarchical system groups that control update ordering.

## System Types

You can create two kinds of systems in Entities:

- **Managed systems**: Classes inheriting from `SystemBase`
- **Unmanaged systems**: Structs inheriting from `ISystem`

Both types support three overrideable methods: `OnUpdate`, `OnCreate`, and `OnDestroy`. The `OnUpdate` method executes once per frame.

Each system processes entities in a single world and can be accessed via the `World` property. By default, automatic bootstrapping creates instances of systems and groups, establishing a default world with three system groups: `InitializationSystemGroup`, `SimulationSystemGroup`, and `PresentationSystemGroup`. Systems are added to `SimulationSystemGroup` by default, but you can override this with the `[UpdateInGroup]` attribute.

To disable automatic bootstrapping, use the scripting define `#UNITY_DISABLE_AUTOMATIC_SYSTEM_BOOTSTRAP`.

## System Categories

- `SystemBase`: Base class for managed systems
- `ISystem`: Interface for unmanaged systems
- `EntityCommandBufferSystem`: Provides command buffer instances to group structural changes and improve performance
- `ComponentSystemGroup`: Offers nested organization and update ordering

## System Groups

System groups can contain systems and other groups as children. They have overrideable update methods and process children in sorted order. See the system groups documentation for more details.

## Inspecting Systems

The Systems window allows you to view system update order and the complete system group hierarchy across worlds. Four icons represent different system types in Editor windows:

| Icon | Represents |
|------|---|
| ![Folder icon](images/editor-system-group.png) | System group |
| ![Hexagon arrows icon](images/editor-system.png) | System |
| ![Forward arrow icon](images/editor-system-start-step.png) | Entity command buffer system with `OrderFirst` |
| ![Backward arrow icon](images/editor-system-end-step.png) | Entity command buffer system with `OrderLast` |

## Related Resources

- Introduction to systems
- Systems window reference
- System update order

---

## Outgoing Hyperlinks

1. https://docs.unity3d.com/Manual/TermsOfUse.html - Trademarks and terms of use
2. https://unity.com/legal - Legal
3. https://unity.com/legal/privacy-policy - Privacy Policy
4. https://unity.com/legal/cookie-policy - Cookie Policy
5. https://unity.com/legal/do-not-sell-my-personal-information - Do Not Sell or Share My Personal Information
