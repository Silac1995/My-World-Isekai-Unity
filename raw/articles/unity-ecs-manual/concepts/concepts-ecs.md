---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/concepts-ecs.html
fetched: 2026-05-05
section: concepts
---

# Entity Component System Introduction

> Note: the URL `manual/ecs-intro.html` returned 404. The actual canonical slug for this page is `concepts-ecs.html` (linked from `concepts-intro.html`). Saved here under that slug.

## Overview

The Entities package implements an entity component system (ECS) architecture. An "entity is a unique identifier, like a lightweight unmanaged alternative to a GameObject." Rather than inheriting from MonoBehaviour, entities serve as references to collections of data that systems process.

The architecture separates concerns into three key elements:

- **Entities**: Unique identifiers without code or data
- **Components**: Data containers associated with entities
- **Systems**: Logic that reads and manipulates component data

## How ECS Works Together

A system reads component data, performs calculations, and updates results. For example, a system might "read Speed and Direction components, multipl[y] them and then update the corresponding Position components."

The presence or absence of non-essential components doesn't affect system execution. Systems can be configured to require specific components or exclude entities with particular components, allowing fine-grained control over which entities a system processes.

## Archetypes

An archetype represents "a unique combination of component types." Entities sharing identical component compositions belong to the same archetype.

---

## Outgoing Hyperlinks

- http://docs.unity3d.com/ — docs.unity3d.com
- ../index.html — (Logo/Home)
- concepts-entities.html — Entity concepts
- concepts-components.html — Component concepts
- concepts-systems.html — System concepts
- https://docs.unity3d.com/Manual/TermsOfUse.html — Trademarks and terms of use
- https://unity.com/legal — Legal
- https://unity.com/legal/privacy-policy — Privacy Policy
- https://unity.com/legal/cookie-policy — Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information — Do Not Sell or Share My Personal Information
