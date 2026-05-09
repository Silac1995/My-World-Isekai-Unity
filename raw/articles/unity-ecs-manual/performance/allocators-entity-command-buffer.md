---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/allocators-entity-command-buffer.html
fetched: 2026-05-05
section: performance
---

# Entity Command Buffer Allocator

The entity command buffer allocator is a custom rewindable allocator. When you create an entity command buffer system, it generates its own allocator. The lifespan of any allocation from this allocator matches the lifespan of the entity command buffer itself.

## How It Works

When using `EntityCommandBufferSystem.CreateCommandBuffer()` to create a command buffer, the allocator handles memory automatically:

- Memory is allocated during command buffer recording
- Memory is deallocated after the buffer is played back

## Singleton Registration

You can register an unmanaged singleton that implements `IECBSingleton` through `ECBExtensionMethods.RegisterSingleton()`. During registration, the entity command buffer allocator from the parent system is assigned to the singleton's allocator. This ensures all command buffers created by the singleton draw from this shared allocator and are cleaned up after playback.

## Key Takeaway

"The entity command buffer allocator works in the background, and you don't need to make specific code changes to use it." The system handles allocation and deallocation automatically as part of normal command buffer operations.

---

## Outgoing Links

- https://docs.unity3d.com/Packages/com.unity.collections@latest/index.html?subfolder=/manual/allocator-rewindable.html - Rewindable allocators
- https://docs.unity3d.com/Packages/com.unity.collections@latest/index.html?subfolder=/manual/allocator-benchmarks.html - Allocator benchmarks
- https://docs.unity3d.com/Manual/TermsOfUse.html - Trademarks and terms of use
- https://unity.com/legal - Legal
- https://unity.com/legal/privacy-policy - Privacy Policy
- https://unity.com/legal/cookie-policy - Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information - Do Not Sell or Share My Personal Information
