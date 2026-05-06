---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/concepts-worlds.html
fetched: 2026-05-05
section: concepts
---

# World Concepts

A **world** represents a collection of entities, where each entity's ID is only unique within that world. Every world contains an `EntityManager` struct that manages entity creation, destruction, and modification operations.

## Key Components

Worlds organize their contained entities into groups called archetypes based on shared component types. This structure determines how components are arranged in memory. A world also owns a set of systems that typically operate only on entities within that same world.

## Initialization Process

When Play mode begins, Unity automatically creates a default `World` instance and registers all systems to it by default.

### Custom Initialization Options

For manual system management, implement the `ICustomBootstrap` interface to control which systems are added to the default world.

For complete bootstrapping control, use these compiler defines to prevent automatic world creation:

- `#UNITY_DISABLE_AUTOMATIC_SYSTEM_BOOTSTRAP_RUNTIME_WORLD` — prevents default runtime world generation
- `#UNITY_DISABLE_AUTOMATIC_SYSTEM_BOOTSTRAP_EDITOR_WORLD` — prevents default Editor world generation
- `#UNITY_DISABLE_AUTOMATIC_SYSTEM_BOOTSTRAP` — prevents both default worlds

When using these defines, your code must manually create worlds and systems, then integrate world updates into the Unity scriptable `PlayerLoop`. The `WorldFlags` API enables creation of specialized Editor worlds.

## Related Resources

- Entities concepts
- Systems concepts

---

## Outgoing Hyperlinks

- `http://docs.unity3d.com/` — docs.unity3d.com
- `../logo.svg` — Logo
- `../index.html` — Home
- `../api/Unity.Entities.EntityManager.html` — EntityManager API
- `../api/Unity.Entities.ICustomBootstrap.html` — ICustomBootstrap API
- `https://docs.unity3d.com/6000.4/Documentation/ScriptReference/LowLevel.PlayerLoop.html` — PlayerLoop documentation
- `systems-icustombootstrap.html` — Manage systems in multiple worlds
- `../api/Unity.Entities.WorldFlags.html` — WorldFlags API
- `concepts-entities.html` — Entities concepts
- `concepts-systems.html` — Systems concepts
- `https://docs.unity3d.com/Manual/TermsOfUse.html` — Trademarks and terms of use
- `https://unity.com/legal` — Legal
- `https://unity.com/legal/privacy-policy` — Privacy Policy
- `https://unity.com/legal/cookie-policy` — Cookie Policy
- `https://unity.com/legal/do-not-sell-my-personal-information` — Do Not Sell or Share My Personal Information
