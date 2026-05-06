---
source_url: http://docs.unity3d.com/Packages/com.unity.physics@6.5/manual/getting-started-installation.html
fetched: 2026-05-05
section: related-packages
package: unity-physics
---

# Physics Project Setup

When establishing a Physics project, several additional configuration steps are necessary.

## Unity Version

"Physics 1.0 is compatible with Unity version 2022.2.0b8 and later."

## Recommended Packages

Review the available ECS packages offerings. The following core packages should be added to your project:

- `com.unity.physics`
- `com.unity.entities`
- `com.unity.entities.graphics`

## IDE Support

The Entities framework leverages Microsoft's Source Generator feature for code generation. You'll need an IDE that supports source generators, as older versions may cause performance issues or incorrectly flag valid code. Compatible IDEs include:

- Visual Studio 2022+
- Rider 2021.3.3+

## Domain Reload Setting

To optimize Physics project performance, disable Unity's Domain Reload functionality. Navigate to **Edit > Project Settings > Editor** and activate **Enter Play Mode Options**, while keeping **Reload Domain** and **Reload Scene** unchecked.

**Note:** When Domain Reloads are disabled, carefully manage static fields and static event handlers to avoid unintended behavior.

---

## Outgoing Hyperlinks

- `https://unity.com/dots/packages` — ECS packages
- `https://docs.unity3d.com/Packages/com.unity.physics@latest` — com.unity.physics
- `https://docs.unity3d.com/Packages/com.unity.entities@latest` — com.unity.entities
- `https://docs.unity3d.com/Packages/com.unity.entities.graphics@latest` — com.unity.entities.graphics
- `https://docs.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/source-generators-overview` — Microsoft Source Generator
- `https://docs.unity3d.com/Manual/ConfigurableEnterPlayMode.html` — Domain Reload
- `https://docs.unity3d.com/Manual/DomainReloading.html` — Domain Reloading
