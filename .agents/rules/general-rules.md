---
trigger: always_on
---

1. This is a Unity game project — complexity is almost always higher than it first appears.
2. Before writing any code, identify all systems the change could touch or break.
3. Always think out loud before implementing: state your approach and assumptions first.
4. Never silently skip complexity with a // TODO — flag it explicitly and explain why.
5. If you are unsure how a system works in this project, ask instead of guessing.
6. Prefer the correct solution over the fast one. Speed is never the goal.
7. Always check: does this code still work correctly with 2+ Player Objects in the scene?
8. Never underestimate a task that feels simple — look for the non-obvious edge case first.
9. Each class must have one purpose — separate Health, Movement, and Data into distinct components.
10. Add features via interfaces and abstract classes, never by modifying existing logic.
11. Subclasses must be fully substitutable for their base class — no NotImplementedException in overrides.
12. Prefer many small, specific interfaces over one large general-purpose interface.
13. High-level modules must depend on abstractions (interfaces), not concrete implementations.
14. Use Dependency Injection wherever possible instead of direct class references.
15. Always name private attributes with an underscore prefix (e.g., _privateVariable).
16. Always unsubscribe from events and stop or clean up coroutines in OnDestroy.
17. The game uses 2D sprites in a 3D environment — account for this in all visual and physics logic.
18. All network-related logic must follow modular principles — no tight coupling between networked systems.
19. When implementing or modifying any major system (e.g. movement, physics, AI, inventory, save/load), always update the associated SKILL.md file in /mnt/skills/ to reflect the changes, or create a new SKILL.md if no skill exists for that system yet based on the create skill SKILL.MD
20. The project is designed for multiplayer; all systems must account for authority, state synchronization, and late-joining players.