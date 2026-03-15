---
name: global
description: Global project rules, C# coding style, optimization, and best practices specific to the 2D/3D Unity environment.
---

# Global Project Rules

This skill contains the fundamental rules and best practices to systematically apply for any development in this Unity project.

## When to use this skill
- **Always**: When writing, modifying, or reviewing any C# script in this project.
- Before proposing a new architecture or feature (to ensure it respects the optimization and multiplayer vision).
- When managing coroutines, events, and memory.

## How to use it
Strictly apply the following rules when writing code:

### 1. C# Style and Architecture
- **Private attributes**: Always prefix private attributes with an underscore `_` (e.g., `_skeletonAnimation`).
- **Encapsulation**: Favor private attributes with accessors (`get` properties) or `[SerializeField] private` for the Unity Inspector. Public attributes should be avoided unless absolutely necessary.
- **Dependency Injection over Singletons**: High-level modules must depend on abstractions (interfaces), not concrete implementations. Avoid singletons or tightly coupled dependencies that would hinder multiplayer scalability.

### 2. Game Context
- **3D/2D Hybrid**: The game runs in a 3D Unity environment but uses 2D character sprites (notably Spine). Always account for 3D/2D interactions, billboarding, and Spine-specific lifecycle events.
- **Multiplayer**: The game is designed to be multiplayer. Think about network architecture at every design decision. Avoid patterns that assume a single authoritative local client.

### 3. Optimization and Memory Safety
- **Performance**: Optimization is an absolute priority. Avoid any unnecessary allocation in the `Update` loop and prevent memory leaks. For all rules governing `Update()`, `FixedUpdate()`, and `LateUpdate()` usage, refer to the `update-usage` skill.
- **Coroutine Management**:
    - *Never* let a coroutine run unchecked.
    - Always keep a reference to running coroutines. Every `StartCoroutine` must be accompanied by a `StopCoroutine` (or `StopAllCoroutines`) in `OnDisable` or `OnDestroy`.
- **Event Management**:
    - Always unsubscribe from events (C# actions, UnityEvents, Spine animation events) in `OnDisable` or `OnDestroy` to avoid memory leaks and ghost callbacks.
    - Always pair subscriptions in `Awake`/`OnEnable` with unsubscriptions in `OnDisable`/`OnDestroy`.

---

## Project Skills Overview

This section maps all specialized skills available in this project. Refer to the relevant skill before designing, implementing, or refactoring any system.

### Core (apply broadly across all development)
| Skill | Purpose |
|---|---|
| `global` | Fundamental project rules, coding style, optimization, memory safety (this file) |
| `update-usage` | Rules for Update(), FixedUpdate(), LateUpdate() — when to use, when to avoid |
| `time-manager` | Event-driven architecture for Day/Night cycle, hours, and phase transitions |
| `multiplayer` | Future-proof architecture rules, decoupling, and safe game state management |
| `save-load-system` | Game state persistence, DTOs, versioning, atomic file operations |
| `character-core` | Central entity hub — availability (IsFree), lifecycle, player/NPC context |

### NPC & AI
| Skill | Purpose |
|---|---|
| `behaviour-tree` | Structure, priorities, and control API for the NPC Behaviour Tree system |
| `goap` | Goal-Oriented Action Planning for NPC daily life and long-term goals |
| `character-needs` | Autonomous decision-making based on internal drives (Social, Work, Survival) |
| `character-invitation` | Template Method pattern for propositions and delayed responses between characters |

### Characters & Visuals
| Skill | Purpose |
|---|---|
| `character-visuals` | 2.5D rendering (Billboarding), Race Presets, and logical Body Part API |
| `spine-unity` | Technical standards and implementation patterns for Spine-Unity integration |
| `dialogue-system` | Scripted conversations, speech bubbles, and player input advancement |
| `social-system` | Dynamic interactions and compatibility-based long-term relationships |

### Game Systems
| Skill | Purpose |
|---|---|
| `combat-system` | Initiative Ticks, BattleManager, and Weapon Styles architecture |
| `item-system` | Items: universal data (SO), runtime instances, and world physical objects |
| `shop-system` | Commercial logic: customer queuing, vendors, and sales |
| `job-system` | Ecosystem connecting employees, jobs, and commercial building workplaces |
| `logistics-cycle` | Supply chain management, restocking, and transport orders |
| `community-system` | Social and territorial structure: hierarchy, membership, and zones |

### UI
| Skill | Purpose |
|---|---|
| `player-ui` | Event-driven UI linking character stats to reactive display elements |
