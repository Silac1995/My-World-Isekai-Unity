---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/allocators-overview.html
fetched: 2026-05-05
section: performance
---

# Memory allocators overview

Entities and the Collections package provide various allocators for managing memory allocations. Each allocator organizes and tracks memory differently based on use case requirements.

## Available Allocators

- **Allocator.Temp**: "A fast allocator for short-lived allocations, which is created on every thread."

- **Allocator.TempJob**: "A short-lived allocator, which must be deallocated within 4 frames of their creation."

- **Allocator.Persistent**: "The slowest allocator for indefinite lifetime allocations."

- **Rewindable allocator**: A custom allocator offering both speed and thread safety, with the ability to rewind and free all allocations simultaneously.

- **World update allocator**: A double rewindable allocator owned by a world, providing fast, thread-safe allocation management.

- **Entity command buffer allocator**: A rewindable allocator owned and used by entity command buffer systems.

- **System group allocator**: An optional double rewindable allocator created by component system groups when configuring rate managers.

## Allocator Feature Comparison

| Allocator type | Custom Allocator | Need to create before use | Lifetime | Automatically freed allocations | Can pass allocations to jobs |
|---|---|---|---|---|---|
| Allocator.Temp | No | No | A frame or a job | Yes | No |
| Allocator.TempJob | No | No | Within 4 frames of creation | No | Yes |
| Allocator.Persistent | No | No | Indefinite | No | Yes |
| Rewindable allocator | Yes | Yes | Indefinite | No | Yes |
| World update allocator | Yes - a double rewindable allocator | No | Every 2 frames | Yes | Yes |
| Entity command buffer allocator | Yes - a rewindable allocator | No | Same as the entity command buffer | Yes | Yes |
| System group allocator | Yes - a double rewindable allocator | Yes | 2 fixed rate system group updates | Yes | Yes |

## Additional Resources

- Custom prebuilt allocators overview
- Rewindable allocators
- Allocator benchmarks

---

## Outgoing Hyperlinks

- https://docs.unity3d.com/Packages/com.unity.collections@latest - Collections package documentation
- https://docs.unity3d.com/Packages/com.unity.collections@latest/index.html?subfolder=/manual/allocation.html - Allocator reference
- https://docs.unity3d.com/Packages/com.unity.collections@latest/index.html?subfolder=/manual/allocator-rewindable.html - Rewindable allocators
- allocators-world-update.html - World update allocator documentation
- allocators-entity-command-buffer.html - Entity command buffer allocator documentation
- allocators-system-group.html - System group allocator documentation
- allocators-custom-prebuilt-intro.html - Custom prebuilt allocators overview
- https://docs.unity3d.com/Packages/com.unity.collections@latest/index.html?subfolder=/manual/allocator-benchmarks.html - Allocator benchmarks
- https://docs.unity3d.com/Manual/TermsOfUse.html - Trademarks and terms of use
- https://unity.com/legal - Legal
- https://unity.com/legal/privacy-policy - Privacy Policy
- https://unity.com/legal/cookie-policy - Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information - Do Not Sell or Share My Personal Information
