---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/allocators-custom-prebuilt-intro.html
fetched: 2026-05-05
section: performance
---

# Prebuilt Custom Allocators Overview

You can utilize prebuilt custom allocators to handle memory management across [worlds](concepts-worlds.html), [entity command buffers](systems-entity-command-buffers.html), and [system groups](systems-write-groups.html). All options listed are [rewindable allocators](https://docs.unity3d.com/Packages/com.unity.collections@latest/index.html?subfolder=/manual/allocator-rewindable.html).

## Available Allocators

| Allocator | Description |
|-----------|-------------|
| [World update allocator](allocators-world-update.html) | A double rewindable allocator owned by a [world](concepts-worlds.html). Automatically frees allocations every 2 frames, preventing memory leaks and supporting short-lived allocations within worlds. Fast and thread safe. |
| [Entity command buffer allocator](allocators-entity-command-buffer.html) | A rewindable allocator managed by an [entity command buffer](systems-entity-command-buffers.html) system. Automatically frees allocations after playback completes. Fast, thread safe, and leak-free. |
| [System group allocator for rate manager](allocators-system-group.html) | An optional double rewindable allocator created when a component system group sets its rate manager. Supports allocations in fixed or variable rate system groups with different tick rates. Manual deallocation unnecessary. |

## Usage

To allocate and deallocate `Native-` and `Unsafe-` collection types, consult the Collections package documentation on [How to use a custom allocator](https://docs.unity3d.com/Packages/com.unity.collections@latest/index.html?subfolder=/manual/allocator-custom-use.html).

## Additional Resources

- [Allocators overview](allocators-overview.html)
- [Rewindable allocators](https://docs.unity3d.com/Packages/com.unity.collections@latest/index.html?subfolder=/manual/allocator-rewindable.html)
- [Allocator benchmarks](https://docs.unity3d.com/Packages/com.unity.collections@latest/index.html?subfolder=/manual/allocator-benchmarks.html)

---

## Outgoing Hyperlinks

- https://docs.unity3d.com/Packages/com.unity.collections@latest/index.html?subfolder=/manual/allocator-rewindable.html — Rewindable allocators
- https://docs.unity3d.com/Packages/com.unity.collections@latest/index.html?subfolder=/manual/allocator-custom-use.html — How to use a custom allocator
- https://docs.unity3d.com/Manual/TermsOfUse.html — Trademarks and terms of use
- https://unity.com/legal — Legal
- https://unity.com/legal/privacy-policy — Privacy Policy
- https://unity.com/legal/cookie-policy — Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information — Do Not Sell or Share My Personal Information
