---
name: character-netcode
description: The Universal Architecture for CharacterSystem Synchronization across Host and Clients.
---

# Character Netcode Synchronization

This standard dictates how ALL components extending `CharacterSystem` (e.g., `CharacterCombat`, `CharacterInteraction`, `CharacterEquipment`) must handle networking to prevent desyncs and single-player legacy bugs.

## When to use this skill
- When refactoring single-player Character logic to support multiplayer.
- When building a new `CharacterSystem` (e.g., `CharacterMagic`, `CharacterBuffs`).
- When fixing issues where an Action, VFX, or Damage only happens locally on one client's screen.

## Core Directives

### 1. The Universal Pattern ("Server Authority & Visual Broadcast")
Because the project was initially solo-focused, you must forcefully decouple **"Intent" (Local)** from **"Execution" (Server)**.

#### Step 1: NetworkVariables for Core State
- Do not use private floats for networked stats. Migrate HP, EquippedWeaponId, etc. to `NetworkVariable<T>`.
- Use `OnValueChanged` to hook up UI updates instantly without polling in `Update()`.

#### Step 2: Client Intents (ServerRpc)
- When `PlayerController` detects input (e.g. Left Click for Attack) or an Interaction (Right Clicks an NPC), the `CharacterSystem` MUST NOT execute the logic locally.
- It must send a `ServerRpc(targetId)` to ask for permission / execution.
- *Exception:* You may spawn visuals locally for Client-Side Prediction, but NO state changes (no damage, no spending mana).

#### Step 3: Server Execution & Asset Resolution
- The Server receives the RPC, validates conditions (Range, Cooldown, Hitboxes).
- The Server modifies the core state (decreases HP, consumes item).
- For interactions, the Server locks the NPC and dictates the Dialogue State globally.
- **CRITICAL - ASSET RESOLUTION:** Never use `Resources.Load()` or Editor-only `AssetDatabase` string-based lookups to resolve data (e.g., `RaceSO`, visual prefabs) coming from a NetworkVariable or RPC. Always maintain an authoritative registry (like `GameSessionManager.Instance.AvailableRaces`) and resolve via matching logic to guarantee the game works perfectly in Standalone Builds.

#### Step 4: Visual Broadcast (ClientRpc)
- If the action requires a generic visual effect (Swinging a Sword, Playing Particles, Voice lines), the Server blasts a `ClientRpc` to all clients (excluding the Owner if they already predicted it) so they see the same animation.

#### Step 5: Physics Sync (NetworkRigidbody) for Hybrid Prefabs
- Because NPCs and Players share the same Prefab, they share the same list of `NetworkBehaviour`s.
- **Players** (WASD) require `NetworkRigidbody` to sync physics forces and velocity.
- **NPCs** (NavMeshAgent) do not need it, as `NetworkTransform` safely syncs their kinematic positions.
- **CRITICAL:** Do NOT `DestroyImmediate()` the `NetworkRigidbody` on the server when spawning an NPC. This breaks the `NetworkBehaviour` index mapping, dropping RPCs like Speech. Always gracefully toggle it: `netRb.enabled = false` during `SwitchToNPC()` and `true` during `SwitchToPlayer()`.

### 2. Hitbox Protection (`CombatStyleAttack.cs`)
- Any physical hitbox triggering an overlap sphere or `OnTriggerEnter` must be gated by `if (!IsServer) return;`.
- Clients spawn Hitbox visuals, but they trigger ZERO damage. Only the Host's invisible overlap validates hits to prevent "double-dipping" damage from multiple clients visually spawning the same spell.

### 3. Persistent Character Identity (UUID)
To ensure characters are uniquely identifiable across sessions, reconnects, and Map Hibernation:
- Each `Character` generates a `NetworkCharacterId` (GUID) on its first `OnNetworkSpawn` (server-side).
- This ID remains stable for the lifetime of that character instance.
- **Usage:** Always use `CharacterId` (the string wrapper for the network variable) when referencing a character in data structures, building ownership, or narrative save files.
- **Lookup:** Use `Character.FindByUUID(string uuid)` to locate a spawned character instance by its persistent ID.

### 4. Character Name Network Sync
`NetworkCharacterName` (FixedString64Bytes, server-write) holds the authoritative character name.
- `Character.OnNetworkSpawn` subscribes to `NetworkCharacterName.OnValueChanged`, applying the value to the local `_characterName` field and updating `gameObject.name` in the hierarchy.
- `Character.OnNetworkDespawn` unsubscribes from the callback.
- **Critical rule:** Any code that changes `_characterName` on the server (profile import, save restore, rename) **must also write to `NetworkCharacterName.Value`**. If only the local field is set, clients will never see the change. Both `CharacterDataCoordinator.ImportProfile` and `CharacterProfile.Deserialize` follow this pattern.
- The `OnValueChanged` callback ensures late-joining clients receive the correct name even if the value arrives slightly after `OnNetworkSpawn`.

## Verification Checklist
- [ ] Does this specific CharacterSystem use NetworkVariables for persistent data?
- [ ] Do Client actions trigger a ServerRpc instead of running local logic?
- [ ] Are colliders/hitboxes gated so ONLY the Server processes `target.TakeDamage`?
- [ ] Are animations synced globally via ClientRpc?
