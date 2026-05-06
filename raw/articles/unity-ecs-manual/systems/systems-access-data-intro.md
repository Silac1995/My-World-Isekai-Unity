---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/systems-access-data-intro.html
fetched: 2026-05-05
section: systems
---

# Access Data on the Main Thread

## Overview

The documentation describes three approaches for accessing data in the Unity Entities system, each suited to different implementation patterns:

- **SystemAPI**: Provides cached data access through codegen with no runtime overhead
- **SystemState**: Offers raw access to entity state data in ISystem systems
- **SystemBase**: Mirrors SystemState functionality with `this.` prefixed calls

## SystemAPI

SystemAPI is a utility class that "provides caching and utility methods for accessing data in an entity's world." It functions within non-static methods of SystemBase and ISystem implementations that accept `ref SystemState` parameters.

The key advantage is that methods can be called directly in Update loops without runtime costs. However, the codegen approach introduces iteration time penalties during development. Developers should use SystemAPI wherever possible unless codegen overhead is a concern.

## SystemState

SystemState supports data access through several mechanisms:

- Retrieving world information
- Querying system metadata
- Obtaining data as system dependencies (similar to EntityManager but with enhanced dependency management)

Detailed API reference is available in the official documentation.

## SystemBase

All SystemState methods are accessible within SystemBase implementations, using `this.` syntax instead of `state.` prefixes.

## Related Resources

- Systems comparison documentation
- SystemAPI overview documentation

---

## Outgoing Links

- http://docs.unity3d.com/
- https://docs.unity3d.com/Manual/TermsOfUse.html
- https://unity.com/legal
- https://unity.com/legal/privacy-policy
- https://unity.com/legal/cookie-policy
- https://unity.com/legal/do-not-sell-my-personal-information
