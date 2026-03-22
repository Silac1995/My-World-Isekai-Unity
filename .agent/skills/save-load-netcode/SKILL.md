---
name: save-load-netcode
description: Architecture for managing Save/Load operations in a Multiplayer environment, strictly enforcing Host Authority.
---

# Save & Load Netcode

This skill dictates how the persistence layer (SaveManager, ISaveable, File I/O) integrates with Netcode for GameObjects (NGO) 2.10. 

## When to use this skill
- When debugging why specific state isn't saving when playing as a Client.
- When creating a new ISaveable system that interacts with Player or World state in multiplayer.
- When expanding the "Sleep" save mechanic.

## Core Directives

### 1. Host Authority Only
- **World State File I/O:** Only the Host (Server) reads and writes the `GameSaveData` to disk. 
- **Client Security:** Clients never write World Save data to their local disk to prevent desyncs and cheating. 
- **Check Condition:** Any call to `SaveManager.Instance.SaveWorldAsync()` MUST be enclosed in an `if (IsServer)` block.

### 2. The Trigger: "Sleep" Action
- **Intent Sync:** When a Player (Host or Client) approaches a bed and clicks "Sleep", they do NOT execute the save locally.
- **ServerRpc Pipeline:** The client must send a `RequestSleepServerRpc()`.
- **Server Execution:** The Server receives the RPC, validates if the character can sleep, and then the Server (Host) triggers the Global Save routine on its own machine.

### 3. Client Reconnection & Profile Loading
- **Connecting Clients:** When a client joins, the Server handles spawning them. 
- **Profile Matching:** The Server determines which cached Player Profile to assign to them (often based on Unity Authentication ID or ClientId mapping).
- **Network Sync:** Once spawned, the Client's local `CharacterStats`, `CharacterInventory`, and visuals are populated natively by the `NetworkVariable`s updated by the Server from the loaded save file.

### 4. DTO Transfer (Avoid if possible)
- The architecture prefers the Server holding the entire World State.
- If a Client *absolutely must* have their local inventory cached locally (e.g. for offline solo play in the future), the Server must serialize the DTO into a JSON string and send it back via a `ClientRpc` targeted only at that Owner. However, the true source of truth during gameplay remains the Server.

## Verification Checklist
- [ ] Save/Load triggers only on the Host (`IsServer == true`).
- [ ] Client Save-Triggers use `ServerRpc`.
- [ ] Connecting Clients correctly inherit data from Server's NetworkVariables, not their local disk.
