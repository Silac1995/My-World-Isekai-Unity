---
source_url: http://docs.unity3d.com/Packages/com.unity.entities.graphics@6.5/manual/requirements-and-compatibility.html
fetched: 2026-05-05
section: related-packages
package: entities-graphics
---

# Requirements and Compatibility | Entities Graphics 6.5.0

## Render Pipeline Compatibility

Entities Graphics requires a Scriptable Render Pipeline (SRP) to function.

| Render Pipeline | Compatibility |
|---|---|
| Built-in Render Pipeline | Not supported |
| High Definition Render Pipeline (HDRP) | Unity 2022 LTS or later |
| Universal Render Pipeline (URP) | Unity 2022 LTS or later |

For Universal Render Pipeline (URP), only Forward+ rendering path is supported.

See the Entities Graphics feature matrix for details on the supported feature set.

## Unity Player System Requirements

This section covers the Entities Graphics package's target platform requirements. For platforms or use cases not covered here, general Unity Player system requirements apply.

| Platform | HDRP | URP |
|---|---|---|
| Desktop | Supported | Supported |
| Android | Not supported | Vulkan and OpenGL ES 3.1+ only |
| iOS | Not supported | Metal Graphics only |
| Nintendo Switch | Not supported | Supported |
| PlayStation 4 / PlayStation 5 | Supported | Supported |
| Xbox One / Xbox Series | Supported | Supported |
| XR platform | Supported | Supported |
| Web platforms | Not supported | Not supported |

**Note:** Entities Graphics depends on platform support of Scriptable Render Pipeline (SRP).

**Warning:** "Support for OpenGL ES is deprecated, and will be removed in a future version of Entities Graphics."

## ECS Feature Compatibility

Entities Graphics does not support multiple Worlds. Limited support for multiple Worlds is planned for a later version, with one rendering system per renderable World and only one World active for rendering at once.

---

## Outgoing Hyperlinks

- `entities-graphics-versions.html` — Entities Graphics feature matrix
- `https://docs.unity3d.com/6000.0/Documentation/Manual/system-requirements.html` — System requirements for Unity
- `https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@latest?subfolder=/manual/requirements.html` — Universal Render Pipeline (URP)
- `https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@latest?subfolder=/manual/System-Requirements.html` — High Definition Render Pipeline (HDRP)
- `https://docs.unity3d.com/Packages/com.unity.entities@latest?subfolder=/manual/concepts-worlds.html` — Worlds
