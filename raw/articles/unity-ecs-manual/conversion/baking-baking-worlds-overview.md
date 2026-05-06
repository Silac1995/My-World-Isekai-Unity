---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/baking-baking-worlds-overview.html
fetched: 2026-05-05
section: conversion
---

# Baking Worlds Overview

Unity processes each entity scene individually during the baking process. When the Editor live bakes open subscenes, it utilizes separate worlds to keep them isolated from one another. Each live baked subscene operates with two distinct worlds:

## The Two Worlds

**Conversion World**: This is where the actual baking takes place. Bakers and baking systems execute their operations within this world.

**Shadow World**: This world maintains a snapshot of the previous baking output. It serves as a reference point for Unity to identify what has changed since the last baking cycle.

## How the Process Works

Unity creates entities representing each authoring GameObject from the scene in the conversion world, then executes bakers and baking systems within that same environment. Since live baking aims to minimize unnecessary processing, baking results remain in the conversion world as long as the subscene stays open.

At the conclusion of baking, Unity must transfer any modified data from the last baking pass into the main world. For instance, if an authoring GameObject's transform has been updated, only the affected ECS components need copying to the main world. This approach prevents unintended side effects—such as resetting a treasure chest's contents when relocating it if the game is in Play mode and the chest was already emptied.

The shadow world fulfills this purpose by holding earlier baking output. During subsequent baking passes, Unity compares the conversion world against the shadow world, copies differing entities and components to the main world, and updates the shadow world to reflect the current conversion world state.

---

## Outgoing Links

- http://docs.unity3d.com/ | docs.unity3d.com
- conversion-scene-overview.html | entity scene
- concepts-worlds.html | worlds
- baking-baker-overview.html | Bakers
- baking-baking-systems-overview.html | baking systems
- baking-overview.html | live baking
- https://docs.unity3d.com/Manual/TermsOfUse.html | Trademarks and terms of use
- https://unity.com/legal | Legal
- https://unity.com/legal/privacy-policy | Privacy Policy
- https://unity.com/legal/cookie-policy | Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information | Do Not Sell or Share My Personal Information
