---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/streaming-overview.html
fetched: 2026-05-05
section: conversion
---

# Scene Streaming Overview

## Overview

According to Unity's documentation, "Loading large scenes might take several frames. To avoid stalls, all scene loading in Entities is asynchronous. This is called **streaming**."

## Main Advantages

The benefits of scene streaming include:

- **Responsiveness**: "Your application can remain responsive while Unity streams scenes in the background."
- **Dynamic Loading**: "Unity can dynamically load and unload scenes in seamless worlds that are larger than can fit memory without interrupting gameplay."
- **Editor Efficiency**: In Play mode, if an entity scene file is missing or outdated, Unity converts the scene on demand. Because baking and loading occurs asynchronously in a separate process, the Editor stays responsive.

## Main Disadvantages

The drawbacks to consider:

- **Data Availability**: "Your application can't assume scene data is present, particularly at startup. This might make your code a bit more complicated."
- **Timing Considerations**: Scenes load from the scene system group within the initialization group. Systems updating later receive loaded data in the same frame, but earlier systems must wait until the next frame.

## Related Resources

- [Load a scene](streaming-loading-scenes.html)
- [Subscenes](conversion-subscenes.html)

---

## Outgoing Links

- https://docs.unity3d.com/Manual/TermsOfUse.html | Trademarks and terms of use
- https://unity.com/legal | Legal
- https://unity.com/legal/privacy-policy | Privacy Policy
- https://unity.com/legal/cookie-policy | Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information | Do Not Sell or Share My Personal Information
