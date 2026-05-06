---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/baking-baker-overview.html
fetched: 2026-05-05
section: conversion
---

# Baker Overview - Unity Entities Documentation

## Overview

A baker is a system component that reads data from authoring scenes and converts it into ECS entity components. Developers create bakers by inheriting from the `Baker<TAuthoringType>` generic class, which requires implementing a single `Bake` method.

## Key Concepts

**Baking Process:**
Unity invokes the Bake method on authoring components during either full baking (all components) or incremental baking (only modified components and their dependencies).

**Stateless Architecture:**
"A baker is only instantiated once and its `Bake` method is called many times, in a non-deterministic order." Bakers must remain stateless and avoid caching values, as this breaks the baking system's invariants.

**Dependency Tracking:**
The baker automatically tracks modifications to authoring component fields. However, external data sources require explicit dependency declarations using the `DependsOn()` method to trigger re-baking when those sources change.

## Creating a Baker

Bakers inherit from the `Baker` class and implement the `Bake` method. They can add components to:
- The primary entity for the authoring component
- Additional entities the baker creates using `CreateAdditionalEntity()`

**Example Structure:**
- Define an authoring MonoBehaviour class
- Create an IComponentData struct for the runtime component
- Implement a baker that converts authoring data to runtime components

## Accessing External Data

When accessing data beyond the authoring component, declare dependencies explicitly:
- Use `DependsOn()` for referenced GameObjects and assets
- Use `GetComponent()` to access components on other objects
- These methods automatically register dependencies for incremental baking

---

## Outgoing Links

- https://docs.unity3d.com/Manual/TermsOfUse.html - Trademarks and terms of use
- https://unity.com/legal - Legal
- https://unity.com/legal/privacy-policy - Privacy Policy
- https://unity.com/legal/cookie-policy - Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information - Do Not Sell or Share My Personal Information
