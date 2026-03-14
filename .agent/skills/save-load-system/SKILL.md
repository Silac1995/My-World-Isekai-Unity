---
name: unity-save-load-system
description: >
  Implement a robust, modular save and load system using DTOs and atomic file operations. 
  Triggers specifically on "Sleep" actions and is restricted to the Host in multiplayer.
---

# Unity Save / Load System

A modular, production-ready save and load system for "My World Isekai". Covers architecture, data separation, async file I/O, atomic writes, versioning, and project-specific triggers (Sleep/Host-only).

## When to use this skill
- When implementating or refactoring game state persistence.
- When adding new saveable systems (Inventory, Stats, World State).
- When ensuring save logic respects multiplayer host authority.
- When integrating the "Sleep" action as a save trigger.

## How to use it

### 1. Identify Saveable Systems
Any system that needs to persist state must implement `ISaveable`.
- See example: [ISaveable.cs](examples/ISaveable.cs)

### 2. Design Data Transfer Objects (DTOs)
Never serialize `MonoBehaviour` directly. Use plain C# classes for state.
- See example: [GameSaveData.cs](examples/GameSaveData.cs)

### 3. Implement Centralized I/O
All file operations (Atomic Writes, Async) are handled by `SaveFileHandler`.
- See example: [SaveFileHandler.cs](examples/SaveFileHandler.cs)

### 4. Coordinate via SaveManager
The `SaveManager` handles registration, trigger checks (Host/Player), and migration.
- See example: [SaveManager.cs](examples/SaveManager.cs)

### 5. Project-Specific Constraints
- **Sleep Trigger**: Saves are triggered ONLY when a Player character sleeps.
- **Host Only**: In multiplayer, ONLY the host can write save data to disk.
- **IsPlayer Check**: Always verify `character.IsPlayer()` before triggering `SaveOnSleep`.

## Examples
- [ISaveable Implementation Pattern](examples/ISaveable.cs)
- [Atomic Async File I/O](examples/SaveFileHandler.cs)
- [Root Save Container (DTO)](examples/GameSaveData.cs)
- [SaveManager Coordination & Triggers](examples/SaveManager.cs)

## Verification Checklist
- [ ] System implements `ISaveable` and registers in `Awake`.
- [ ] `CaptureState` returns a serializable DTO.
- [ ] Save triggers only via `PlayerSleep` (check `IsPlayer`).
- [ ] Save logic check for `IsHost` in multiplayer contexts.
- [ ] File writes are atomic (.tmp swap).
- [ ] Operations are asynchronous to avoid frame drops.