# Multiplayer Skill (Netcode for GameObjects)

This skill provides mandatory architectural and implementation patterns for multiplayer in the **My World Isekai** project using Unity's **Netcode for GameObjects (NGO)**.

## Why this Skill?
Multiplayer is one of the most complex systems in game development. To avoid technical debt and "spaghetti" networking code, we follow strict rules:
1. **Server-Authoritative Logic:** The server is the source of truth.
2. **Modular Decoupling:** Keep networking logic separate from visuals and input.
3. **Reactive State:** Use `NetworkVariable` and events to drive changes.

## Structure
- [SKILL.md](file:///c:/Users/Kevin/Unity/Unity%20Projects/Git/MWI%20-%20Version%20Control/My-World-Isekai-Unity/.agent/skills/multiplayer/SKILL.md): The core rules and concepts.
- [examples/netcode_patterns.md](file:///c:/Users/Kevin/Unity/Unity%20Projects/Git/MWI%20-%20Version%20Control/My-World-Isekai-Unity/.agent/skills/multiplayer/examples/netcode_patterns.md): Concrete examples for movement, combat, and state syncing.

## References
- [Unity Netcode for GameObjects Documentation](https://docs.unity3d.com/Packages/com.unity.netcode.gameobjects@2.10/manual/index.html)
