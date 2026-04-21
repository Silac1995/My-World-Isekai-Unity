# Project Rules

These rules are **mandatory** and apply to every conversation, every task, and every line of code.

> **LLM Wiki schema:** wiki content is governed by [wiki/CLAUDE.md](wiki/CLAUDE.md). Those rules only apply to operations inside `wiki/` (INGEST / QUERY / LINT / DOCUMENT-SYSTEM / SAVE / MAP). The 32 project rules below always take precedence.

## General Approach

1. This is a Unity game project — complexity is almost always higher than it first appears.
2. Before writing any code, identify all systems the change could touch or break.
3. Always think out loud before implementing: state your approach and assumptions first.
4. Never silently skip complexity with a // TODO — flag it explicitly and explain why.
5. If you are unsure how a system works in this project, ask instead of guessing.
6. Prefer the correct solution over the fast one. Speed is never the goal.
7. Always check: does this code still work correctly with 2+ Player Objects in the scene?
8. Never underestimate a task that feels simple — look for the non-obvious edge case first.

## Proactive Recommendations

9. Proactively recommend improvements to code structure, architecture, naming, or project organization whenever you spot an issue — do not wait to be asked. Flag anything that violates SOLID, creates tight coupling, or will cause maintenance problems later.

## Architecture (SOLID)

9. Each class must have one purpose — separate Health, Movement, and Data into distinct components.
10. Add features via interfaces and abstract classes, never by modifying existing logic.
11. Subclasses must be fully substitutable for their base class — no NotImplementedException in overrides.
12. Prefer many small, specific interfaces over one large general-purpose interface.
13. High-level modules must depend on abstractions (interfaces), not concrete implementations.
14. Use Dependency Injection wherever possible instead of direct class references.

## Character GameObject Hierarchy

The Character entity uses a **Facade + Child Hierarchy** pattern:
- The root `Character` GameObject holds `Character.cs`, which acts as the single facade and dependency point for all subsystems.
- Each character subsystem (e.g., `CharacterCombat`, `CharacterMovement`, `CharacterNeeds`) lives on its own **child GameObject** with only the scripts it needs. This keeps the Inspector navigable and scopes each system clearly.
- Every subsystem script holds a reference to the root `Character` component (not to other subsystems directly).
- **Cross-system communication must always go through `Character`** — a subsystem must never cache or call another subsystem directly. This enforces a single dependency graph and prevents circular coupling.
- When adding a new character system: create a child GameObject, add the script there, expose it as a `[SerializeField]` on `Character.cs`, and auto-assign it in `Awake()` via `GetComponentInChildren<>()` as a fallback.

## C# Standards

15. Always name private attributes with an underscore prefix (e.g., `_privateVariable`).
16. Always unsubscribe from events and stop or clean up coroutines in `OnDestroy`.

## Game Context

17. The game uses 2D sprites in a 3D environment — account for this in all visual and physics logic.

## Network Architecture

18. All network-related implementation must follow the full network architecture document: `NETWORK_ARCHITECTURE.md`. This document defines all rules around Server authority, Client responsibilities, Host behavior, NPC networking, Client-Side Prediction, Interest Management, Delta Compression, and the correct use of NetworkTransform vs ClientNetworkTransform. For multiplayer implementation specifics, also refer to: `.agent/skills/multiplayer/SKILL.md`. Before writing any networked logic, read the architecture doc and run the checklist at the end of it.
19. Every networked feature must be validated against **all player relationship scenarios**: Host↔Client, Client↔Client, and Host/Client↔NPC. Data that lives only on the server (static registries, runtime dictionaries) is invisible to clients — always ensure clients receive the data they need via `NetworkVariable`, ClientRpc, or `OnValueChanged` callbacks. Never assume a client has access to server-side state.

## Character System

20. The Character system must be decoupled from the World/Server state. Characters must be serialized as independent local files (e.g., .json) that can be loaded into any session (Solo or Multiplayer). When in Multiplayer, all inventory and stat changes must be saved back to the player's local character file upon portal gate return or at bed checkpoints. Use `ICharacterSaveData<T>` interface to ensure each subsystem provides typed, priority-ordered serialization for the portable character profile.
21. Every time a new character subsystem is created (e.g., `CharacterCombat`, `CharacterNeeds`, `CharacterMovement`), a corresponding SKILL.md must be written in `.agent/skills/` to document its purpose, public API, events, dependencies, and integration points. This is mandatory — no character system ships without its skill file.
22. **Anything a player can do, an NPC can do, and vice versa.** All gameplay effects (placing, picking up, crafting, interacting) must go through `CharacterAction`. Player-facing systems (HUD, mouse input, ghost visuals) are UI layers that queue the same `CharacterAction` that NPC AI would queue. Never implement gameplay logic directly in a player-only manager — always route through a shared action.

## Language

23. Speak in English, write everything in English. Comments, documents, code, SKILL.md.

## MCP / Unity Editor

24. You are connected to the Unity Editor via MCP (Model Context Protocol). Use this connection to directly inspect, read, and modify the project hierarchy, components, and scripts. Always verify the actual state of the project through MCP before assuming existing logic or proposing changes.

## Rendering & Performance

25. Prioritize Shader-based solutions over CPU-bound modifications (e.g., `Image.fillAmount`, `Graphic.color`, or Sprite Vertex manipulation) for any dynamic visual feedback. Use Material Property Blocks (MPB) to ensure these changes do not break Batching. For complex color customization, prefer Palette Swapping (LUT) over global Tints to maintain artistic integrity and minimize CPU-to-GPU data transfers, especially for networked entities.

## Time & GameSpeedController

26. All time-dependent logic must explicitly account for the `GameSpeedController`. Distinguish between "Simulation Time" (gameplay mechanics) and "Real Time" (UI, menus, network heartbeats). Use `Time.deltaTime` for simulation and `Time.unscaledDeltaTime` for non-gameplay visuals. Rule: UI elements (menus, buttons, HUD animations, and "Real-Time" bars) should NOT be affected by the GameSpeedController and must use unscaled time to remain functional during pauses or high-speed intervals. For high-speed scales (Giga Speed), all tick-based simulation systems must use catch-up loops (`while` / `ticksToProcess`).

## Bug Reporting & Debugging

27. When the user reports a specific issue or bug, not only propose a fix but also identify potential "blind spots" in the logic. For every suspected cause, provide code that includes explicit `Debug.Log` or `Debug.LogError` statements at critical branching points (If/Else, Null Checks, Network Callbacks). These logs must output the internal state of variables at the exact moment of the failure.

## Skill Files, Agent & Wiki Maintenance

28. **Every** time a system is created, modified, upgraded, or refactored — not just major systems — its corresponding SKILL.md in `.agent/skills/` must be updated to reflect the changes. This includes API changes, new events, changed dependencies, removed methods, or altered behavior. If no skill exists for that system yet, create one following the template and guidelines in `.agent/skills/skill-creator/SKILL.md`. No implementation change ships without its documentation being current.

29. After completing any significant implementation (adding a system, reworking existing code, modifying cross-system behavior), **evaluate whether a specialized agent in `.claude/agents/` needs updating or creating**. Ask: (a) Does this change extend or alter the domain of an existing agent? If yes, update that agent's `.md` file to reflect the new knowledge. (b) Is this a new system complex enough (5+ interconnected scripts, cross-system dependencies, non-obvious rules) to warrant its own agent? If yes, create one. (c) If neither applies, no action is needed — do not create agents for trivial changes. Agents must always use `model: opus`.

29b. **Every** time a system is created, modified, or refactored — in addition to rules #28 (SKILL.md) and #29 (agents) — the matching page in `wiki/systems/` must be updated or created so the LLM wiki stays in sync with the code. The wiki is the source of truth for **architecture** (what the system is, why it exists, how it connects to others); SKILL.md is the source of truth for **procedure**. Do not duplicate procedural content — link to the SKILL.md in the page's `Sources` section. Required actions: (a) If a page exists, bump the `updated:` frontmatter date, append a line to `## Change log` (`- YYYY-MM-DD — <summary> — claude`), refresh `depends_on` / `depended_on_by` / `related` if cross-system relationships changed, and update any of the ten required sections affected by the change (Purpose, Responsibilities, Key classes / files, Public API, Data flow, Dependencies, State & persistence, Gotchas, Open questions, Change log). (b) If no page exists and the change qualifies as a system (owns a coherent responsibility, has multiple files, or is referenced by other systems), create it from `wiki/_templates/` following the rules in `wiki/CLAUDE.md`. (c) Trivial fixes with zero architectural impact (typos, local refactors, internal-only tweaks) may be skipped. Always read `wiki/CLAUDE.md` before touching any file under `wiki/` — it governs frontmatter, naming, wikilinks, sources, and the diff-preview rule for >5-file operations.

## World System & Simulation

30. The game uses a Living World architecture based on Map Hibernation and Macro/Micro Simulation. Before implementing any system that involves NPCs, resources, buildings, time, or map state, you must account for both simulation layers:

- **Micro-Simulation** (Map is Active): Real-time GOAP, NavMesh pathfinding, live logistics orders, physical harvestables, and NetworkObject presence. All live logic runs only when at least one player is present on the map.
- **Macro-Simulation** (Map is Hibernating): When player count reaches 0, the map freezes. All NPCs are serialized into `HibernatedNPCData` and despawned. On wake-up, `MacroSimulator` runs a catch-up loop in strict order: (1) Resource Pool Regeneration, (2) Inventory Yields via `JobYieldRegistry` + `BiomeDefinition`, (3) Needs Decay, (4) Position Snap. No live Unity systems (NavMesh, physics, NetworkObject) exist during hibernation — all offline progress is pure math.

**Key rules:**
- Any new NPC stat, need, or behavior that changes over time must have a corresponding offline catch-up formula in `MacroSimulator`.
- Any new resource or harvestable must be registered in `BiomeDefinition` and have a `ResourcePoolEntry` in `CommunityData`. Never hardcode resource availability.
- Any new job type must have a `JobYieldRecipe` entry in `JobYieldRegistry`. Biome-driven jobs must set `IsBiomeDriven = true`.
- `TimeManager` global time is the single source of truth for all simulation math. Never use `Time.time` or `Time.deltaTime` for offline delta calculations — always use `TimeManager.CurrentDay` + `CurrentTime01`.
- Maps are dynamic. New maps are born via `CommunityTracker` → `WorldOffsetAllocator` → `MapController` bootstrapping. Abandoned cities never release their spatial slot. Predefined maps register via `SaveManager.RegisterPredefinedMaps()` on boot.
- Always refer to `world-system/SKILL.md` before touching any map, NPC lifecycle, or simulation logic.

## Defensive Coding & Exception Handling

31. Wrap operations that can fail at runtime (file I/O, deserialization, network callbacks, external data parsing, `GetComponent` on uncertain targets) in `try/catch` blocks. Log the exception with `Debug.LogException(e)` or `Debug.LogError` including context (what was being attempted, which object, which data). The goal is to prevent one failing subsystem from crashing the entire game — gracefully degrade instead. Do **not** swallow exceptions silently; always log them. For performance-critical paths (Update loops, tight loops), prefer null-checks and validation over try/catch.

## World Scale Reference

32. **11 Unity units = 1.67m (average human height).** Use this ratio for all spatial calculations — distances, sizes, speeds, ranges, spawn offsets, combat positions, building dimensions, furniture placement, and any other measurement. When designing or reviewing any value that represents a real-world distance, convert using this scale: **1 Unity unit ≈ 0.152m (≈15.2 cm)**.
