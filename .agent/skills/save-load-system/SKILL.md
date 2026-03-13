---
name: save-load-system
description: Architectural principles and best practices for designing, implementing, and refactoring save/load systems in Unity.
---

# Save & Load System

This skill provides a comprehensive set of architectural principles and patterns for managing game state persistence in Unity. It covers data separation, serialization, file I/O safety, and scalability.

## When to use this skill
- When designing a new save/load system for a project.
- When refactoring an existing, messy persistence implementation.
- When implementing a migration system for save data versions.
- When ensuring multiplayer save data follows server-authoritative principles.

## How to use it

### 1. Separate Save Data from Runtime Data
Never serialize your runtime `MonoBehaviour` or `ScriptableObject` directly. Always create dedicated **Data Transfer Objects (DTOs)** — plain C# classes with no Unity dependencies — to represent saved state.
- **Runtime Component**: `PlayerRuntimeData` (lives in scene)
- **Save DTO**: `PlayerSaveData` (plain C# class)

### 2. Choose the Right Serializer
Avoid `BinaryFormatter` (deprecated/insecure). Use one consistently:
- **`Newtonsoft.Json` (Json.NET)**: Flexible, handles complex types (Recommended).
- **`JsonUtility`**: Fast, zero dependencies, but limited (no Dictionaries).
- **Binary (`MemoryPack`/`MessagePack`)**: For performance-critical or large files.

### 3. Centralize Save Logic
Do not scatter save/load calls across `MonoBehaviours`. Create a single `SaveSystem` or `SaveManager` (Service/Singleton) that owns all file I/O operations and exposes clean methods: `SaveAsync()`, `LoadAsync()`, `DeleteSave()`.

### 4. Use a Root Save Data Container
Compose a single root DTO that aggregates all sub-DTOs (player, world, inventory) into one atomic unit.

### 5. Always Version Your Save Data
Include a `saveVersion` integer in the root DTO from day one. Implement migration logic to upgrade old schemas without breaking data.

### 6. Use Safe File Writing (Atomic Write)
To prevent corruption during crashes, write to a `.tmp` file first, then replace the real save file upon success. Always use `Application.persistentDataPath`.

### 7. Make Save/Load Asynchronous
Use `async/await` (`System.Threading.Tasks`) for all file I/O to avoid blocking the main thread.

### 8. Handle Errors Gracefully
Wrap deserialization in `try/catch`. Return defaults for missing or corrupt files instead of crashing.

### 9. Multiplayer: Server is the Source of Truth
Never trust client-side save data for authoritative state. Validate player progression server-side; local saves are for preferences and UI state only.

### 10. Consider Encryption/Checksums
Use basic encryption or HMAC checksums for sensitive offline data to deter tampering, but rely on server validation for multiplayer.

### 11. Implement Multiple Save Slots
Design file paths to include slot identifiers from the start (e.g., `saves/slot_0.json`).

## Save System Checklist
- [ ] Save data uses DTOs, not runtime MonoBehaviours
- [ ] A single serializer is used consistently
- [ ] A centralized SaveManager owns all file I/O
- [ ] Root DTO includes a `saveVersion` field
- [ ] Migration logic exists for version upgrades
- [ ] File writes are atomic (write to .tmp, then swap)
- [ ] Save/load operations are async
- [ ] Missing/corrupt files are handled gracefully
- [ ] Multiplayer: server state takes priority over local saves
- [ ] Sensitive data is checksummed or encrypted
- [ ] Multi-slot support is built into the file path structure
- [ ] Events are unsubscribed and coroutines stopped in OnDestroy (consistent with memory management rules)
