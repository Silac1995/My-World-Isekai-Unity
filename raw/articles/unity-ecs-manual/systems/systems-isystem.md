---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/systems-isystem.html
fetched: 2026-05-05
section: systems
---

# ISystem Overview

## Introduction

To create an unmanaged system in Unity ECS, implement the [`ISystem`](../api/Unity.Entities.ISystem.html) interface type.

## Implement Abstract Methods

You must implement the following abstract methods, which support [Burst compilation](https://docs.unity3d.com/Packages/com.unity.burst@latest):

| Method | Description |
|--------|-------------|
| [`OnCreate`](../api/Unity.Entities.ISystem.OnCreate.html) | System event callback to initialize the system and its data before usage. |
| [`OnUpdate`](../api/Unity.Entities.ISystem.OnUpdate.html) | System event callback to add the work that your system must perform every frame. |
| [`OnDestroy`](../api/Unity.Entities.ISystem.OnDestroy.html) | System event callback to clean up resources before destruction. |

Unlike [`SystemBase`](systems-systembase.html) systems that inherit from a base class, `ISystem` implementations receive a [`ref SystemState`](../api/Unity.Entities.SystemState.html) argument in each callback method. This parameter provides access to the [`World`](../api/Unity.Entities.World.html), [`WorldUnmanaged`](../api/Unity.Entities.WorldUnmanaged.html), contextual world data, and APIs such as [`EntityManager`](../api/Unity.Entities.EntityManager.html).

## Optional ISystemStartStop Implementation

You can optionally implement the [`ISystemStartStop`](../api/Unity.Entities.ISystemStartStop.html) interface, which provides these callbacks:

| Method | Description |
|--------|-------------|
| [`OnStartRunning`](../api/Unity.Entities.ISystemStartStop.OnStartRunning.html) | Called before the first `OnUpdate` call and when a system resumes after stopping or disabling. |
| [`OnStopRunning`](../api/Unity.Entities.ISystemStartStop.OnStopRunning.html) | Called when a system is disabled or doesn't match required components for update. |

## Scheduling Jobs

All system events execute on the main thread. Best practice involves using the `OnUpdate` method to schedule jobs for most work. Schedule jobs using:

- [`IJobEntity`](iterating-data-ijobentity.html): Iterates over component data across multiple entities, reusable across systems.
- [`IJobChunk`](../api/Unity.Entities.IJobChunk.html): Iterates over data by [archetype chunk](concepts-archetypes.html#archetype-chunks).

## Callback Method Order

Unity invokes several callbacks during the system lifecycle:

- [`OnCreate`](../api/Unity.Entities.ISystem.OnCreate.html): Triggered when ECS creates the system.
- [`OnStartRunning`](../api/Unity.Entities.ISystemStartStop.OnStartRunning.html): Called before the first `OnUpdate` and whenever the system resumes.
- [`OnUpdate`](../api/Unity.Entities.ISystem.OnUpdate.html): Invoked every frame while the system has work. See [`ShouldRunSystem`](../api/Unity.Entities.SystemState.ShouldRunSystem.html#Unity_Entities_SystemState_ShouldRunSystem) for work determination details.
- [`OnStopRunning`](../api/Unity.Entities.ISystemStartStop.OnStopRunning.html): Called before `OnDestroy` and when the system stops due to no matching entities in [`RequireForUpdate`](../api/Unity.Entities.SystemState.RequireForUpdate.html) or when [`Enabled`](../api/Unity.Entities.SystemState.Enabled.html#Unity_Entities_SystemState_Enabled) is set to `false`. Without `RequireForUpdate` specified, the system runs continuously unless disabled or destroyed.
- [`OnDestroy`](../api/Unity.Entities.ISystem.OnDestroy.html): Called when ECS destroys the system.

The system event order diagram shows: OnCreate -> OnStartRunning -> OnUpdate -> OnStopRunning -> OnDestroy.

A parent [system group's](systems-update-order.html) `OnUpdate` method triggers the `OnUpdate` methods of all child systems. For detailed information, see [Update order of systems](systems-update-order.html#update-order-of-systems).

## Additional Resources

- [Access data introduction](systems-access-data-intro.html)

---

## Outgoing Hyperlinks

- https://docs.unity3d.com/Packages/com.unity.burst@latest - Burst compilation documentation
- ../api/Unity.Entities.ISystem.html - ISystem interface
- ../api/Unity.Entities.ISystem.OnCreate.html - OnCreate method
- ../api/Unity.Entities.ISystem.OnUpdate.html - OnUpdate method
- ../api/Unity.Entities.ISystem.OnDestroy.html - OnDestroy method
- systems-systembase.html - SystemBase systems
- ../api/Unity.Entities.SystemState.html - SystemState reference
- ../api/Unity.Entities.World.html - World class
- ../api/Unity.Entities.WorldUnmanaged.html - WorldUnmanaged class
- ../api/Unity.Entities.EntityManager.html - EntityManager class
- ../api/Unity.Entities.ISystemStartStop.html - ISystemStartStop interface
- ../api/Unity.Entities.ISystemStartStop.OnStartRunning.html - OnStartRunning method
- ../api/Unity.Entities.ISystemStartStop.OnStopRunning.html - OnStopRunning method
- iterating-data-ijobentity.html - IJobEntity documentation
- ../api/Unity.Entities.IJobChunk.html - IJobChunk interface
- concepts-archetypes.html#archetype-chunks - Archetype chunks
- ../api/Unity.Entities.SystemState.ShouldRunSystem.html#Unity_Entities_SystemState_ShouldRunSystem - ShouldRunSystem method
- ../api/Unity.Entities.SystemState.RequireForUpdate.html - RequireForUpdate property
- ../api/Unity.Entities.SystemState.Enabled.html#Unity_Entities_SystemState_Enabled - Enabled property
- systems-update-order.html - System update order documentation
- systems-update-order.html#update-order-of-systems - Update order of systems details
- systems-access-data-intro.html - Access data introduction
