---
description: "Future-Proof" code architecture for multiplayer (No local singletons, Inputs/Logic separation).
---

# Multiplayer Architecture Skill

This skill dictates the "Network-Ready" architectural philosophy that **must be systematically applied** in the project, even if no framework (like Mirror or Netcode) is installed yet.
The golden rule (defined in `global.md`) is to always code under the assumption that the game "will be" multiplayer.

## When to use this skill
- To be applied **systematically** when creating a new system (e.g., Quest, Inventory, Combat systems).
- When adding mechanics involving multiple characters.
- When writing `MonoBehaviours` related to Time or Inputs.

## "Future-Proof" Architecture Rules

### 1. Banning Singletons for Game State
- **The Rule:** NEVER use `FindObjectOfType<Player>()` or a `Player.Instance`.
- **Why?** In multiplayer, there are *multiple* players in the same scene.
- **The Solution:** Use Dependency Injection, explicit references via GetComponent, or isolated local instance managers. (e.g., A `BattleManager` manages a list of `CharacterCombat` that it knows about, rather than guessing who is attacking whom).

### 2. Strict Decoupling of Inputs and Logic
- **The Rule:** The code that reads the keyboard/controller (`InputManager.cs`) **must not** contain gameplay logic (`character.Move()`).
- **Why?** Over a network, a monster does not receive local keyboard inputs. It receives an order (RPC) from the server.
- **The Solution:** Inputs only emit events (e.g., `OnAttackPressed`). The logic (`Attack()`) listens to this event, but could just as easily be called by a network packet (or a `BehaviourTree` decision).

### 3. State vs Visual
- This decoupling has already started in the project: `CharacterStats` owns the data and `CharacterVisual` displays it.
- **The Rule:** Never sync a Visual over the network. Only the State (`CharacterStats.Health`, `CharacterCombat.Initiative`) should eventually be synced by the server.

### 4. The Dictatorship of Time
- **The Rule:** Never manipulate `Time.timeScale` to pause or slow down the game in a local character logic.
- **Why?** Slowing down time locally will catastrophically desync the client from all other players and the physical server.
- **The Solution:** Entrust time management to Server Managers. Typical example: the `BattleManager` uses its own independent "Tick" (`PerformBattleTick()`) separate from Unity's `Time.time`, making it easily synchronizable later.

## "Network-Ready" Code Checklist
Critically review your new code:
- [ ] Does my code survive if there are 2 "Player Objects" in the scene?
- [ ] If I call my shooting or moving method purely through code from anywhere, does it work without depending on an obscure keyboard boolean?
- [ ] Do my cooldowns rely on the local architecture rather than modifying the Unity engine?
