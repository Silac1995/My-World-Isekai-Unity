---
type: gotcha
title: "Host player Character UUID isn't stable until ImportProfile — saved-data resolvers keyed by CharacterId miss the host"
tags: [character, save-load, uuid, network-id, gamelauncher, host, timing, race-condition]
created: 2026-05-09
updated: 2026-05-09
sources:
  - "[Assets/Scripts/Core/GameLauncher.cs](../../Assets/Scripts/Core/GameLauncher.cs)"
  - "[Assets/Scripts/Character/Character.cs](../../Assets/Scripts/Character/Character.cs)"
  - "[Assets/Scripts/Character/SaveLoad/CharacterDataCoordinator.cs](../../Assets/Scripts/Character/SaveLoad/CharacterDataCoordinator.cs)"
  - "[Assets/Scripts/World/Buildings/Building.cs](../../Assets/Scripts/World/Buildings/Building.cs)"
  - "2026-05-09 conversation with Kevin — owner save/load worked for NPCs but lost the host's player as building owner on every reload"
related:
  - "[[character]]"
  - "[[character-profile]]"
  - "[[save-load]]"
  - "[[building]]"
  - "[[commercial-building]]"
status: mitigated
confidence: high
---

# Host player Character UUID isn't stable until ImportProfile — saved-data resolvers keyed by CharacterId miss the host

## Summary
Anything that, on the server, calls `Character.FindByUUID(savedId)` during world load to rebind a saved relationship (building owner, employee assignment, dormant relationship target, party leader, …) is at risk of **silently dropping the host player** while it correctly handles every NPC. The host's player Character spawns in `GameLauncher` Step 4 with a fresh `Guid.NewGuid()` assigned by `Character.OnNetworkSpawn`; its **persistent** profile GUID is only written into `NetworkCharacterId.Value` later, by `CharacterDataCoordinator.ImportProfile` in Step 6 — AFTER `OnCharacterSpawned` already fired (with the wrong GUID) and after every save-load resolver that runs in Step 5b/5c (e.g. `MapController.SpawnSavedBuildings`) has scanned the live characters and missed the host.

## Symptom
- After save → reload, the host appears un-owned for any building they previously owned (Inspect tab shows no owner).
- NPC owners on the same building are restored correctly — only the host is missing.
- Player **jobs** restore correctly even for the host. (Reason: `CharacterJob.Deserialize` runs character-side from `ImportProfile` and binds via stable `BuildingId`, not via UUID lookup — it sidesteps the timing window.)
- Server-only console may show `[Building:RestoreOwners] <building>: pending owners=1` followed by `subscribed to OnCharacterSpawned …` and then nothing — the resolver is waiting for a spawn event that already fired with a different GUID.
- Same class of issue can appear for any other UUID-keyed resolver: a `CharacterRelation` entry whose target IS the host can stay dormant; a party NPC whose leader IS the host can fail its initial leader-bind (each subsystem has its own pending mechanism, so symptoms vary).

## Root cause
`GameLauncher.LaunchSequence` ordering on world load:

| Step | What happens | Host player's `NetworkCharacterId.Value` |
|------|--------------|------------------------------------------|
| 4 | NGO spawns the host's `PlayerPrefab`. `Character.OnNetworkSpawn` runs `if (IsServer && NetworkCharacterId.Value.IsEmpty) NetworkCharacterId.Value = Guid.NewGuid().ToString("N");`. `Character.OnCharacterSpawned` fires. | **fresh Guid (wrong)** |
| 5  | `LoadWorldData` populates `CommunityData.ConstructedBuildings` from disk — entries carry the saved-from-last-session host GUID in `OwnerCharacterIds`. | fresh Guid (wrong) |
| 5b | `MapController.SpawnSavedBuildings` runs. `Building.RestoreOwnersFromSaveData` calls `Character.FindByUUID(savedHostGuid)` → no match (host's live GUID ≠ saved). Pending list keeps the saved id and subscribes to `Character.OnCharacterSpawned`. | fresh Guid (wrong) |
| 5c | `SpawnNPCsFromPendingSnapshot` spawns hibernated NPCs. Each NPC's `NetworkCharacterId` was set BEFORE `Spawn(true)` (see `MapController.SpawnNPCsFromSnapshot:1252`), so `OnCharacterSpawned` fires for them with the correct GUID — **NPC owners resolve correctly via the pending list's subscription.** | fresh Guid (wrong) |
| 6  | `LoadAndImportProfile` → `CharacterDataCoordinator.ImportProfile` overwrites `NetworkCharacterId.Value = data.characterGuid`. | **correct GUID, but the spawn event already fired** |

The host-vs-NPC asymmetry is structural: NPCs go through "set ID → spawn → fire spawn event", the host goes through "spawn → fire spawn event → set ID". `OnCharacterSpawned` fires exactly once per Character at NGO spawn time and never fires again when the GUID later changes.

`CharacterJob` survives because it is bidirectional: in addition to the building-side `RestoreEmployeesFromSaveData` (which does suffer the same UUID-miss for the host), there is a character-side `CharacterJob.Deserialize` that runs from `ImportProfile` itself and binds via `BuildingManager.OnBuildingRegistered` keyed by stable `BuildingId` — no UUID lookup needed. Most other resolvers don't have a character-side path, so they fail silently for the host.

## How to avoid
- **For any new server-side resolver that keys saved data by `CharacterId`, subscribe to BOTH events**:
  - `Character.OnCharacterSpawned` — catches NPCs (their UUID is correct at spawn time).
  - `Character.OnCharacterIdReassigned` — catches the host (fired from `CharacterDataCoordinator.ImportProfile` only when `NetworkCharacterId.Value` actually changes).
- A single `HandleCharacterIdentityResolved(Character resolved)` handler is fine for both; the resolution logic is identical (walk the pending list, bind anything that now resolves). Both subscriptions get torn down in the same `OnNetworkDespawn` cleanup. See `Building.RestoreOwnersFromSaveData` + `CommercialBuilding.RestoreEmployeesFromSaveData` for the canonical shape.
- Do **NOT** re-fire `OnCharacterSpawned` from `ImportProfile`. It is the wrong tool — many other subscribers (relations, orders, ambitions, job worker bind, …) treat it as "first-time setup" and re-running them risks double-binding or breaking invariants. `OnCharacterIdReassigned` is the surgical hook.
- Prefer character-side restore where possible (the `CharacterJob` pattern): persist data on the character profile itself and bind via stable IDs (BuildingId, FurnitureId, ItemId) that don't suffer the UUID timing issue. This sidesteps the trap entirely without needing the dual subscription.

## How to fix (if already hit)
1. Find the resolver that keys by `CharacterId` (`Character.FindByUUID(savedId)` is the smoking gun).
2. If it has a pending list + `Character.OnCharacterSpawned` subscription, add a parallel `Character.OnCharacterIdReassigned += sameHandler` next to it. Mirror the `-=` in the cleanup path (typically `OnNetworkDespawn`).
3. Verify the handler is idempotent — re-running it on an already-resolved Character must be a no-op. `AddOwner`-style methods that early-return when the id is already present are usually fine.
4. Test: load a world where the host owns a building. Reload. Console should show `[Building:RestoreOwners] <name>: subscribed to OnCharacterSpawned + OnCharacterIdReassigned for 1 owner(s)` followed by `bound owner '<host name>' (id=…)` after Step 6 imports the profile.

## Affected systems
- [[building]] — `Building.RestoreOwnersFromSaveData` (fixed 2026-05-09).
- [[commercial-building]] — `CommercialBuilding.RestoreEmployeesFromSaveData` (fixed 2026-05-09 alongside owner restore).
- [[character-relation]] — `CharacterRelation.Deserialize` keys saved entries by target `CharacterId`. Symptom would be: an NPC's saved relationship pointing at the host stays dormant on reload until the NPC despawns/respawns. Not yet fixed — flag for follow-up if relations-by-host become user-visible.
- [[character-party]] — `CharacterParty.SubscribeToLeader` resolves leader by UUID at deserialize time. A party NPC whose leader IS the host could miss the initial leader-bind. Not yet fixed.
- Any future server-side restore path that does `Character.FindByUUID(savedId)` immediately after load — audit before shipping.

## Links
- [[character]] — `OnCharacterSpawned` and the new `OnCharacterIdReassigned` events live on the `Character` static surface.
- [[character-profile]] — `CharacterDataCoordinator.ImportProfile` is where the host's persistent GUID lands and the new event fires.
- [[save-load]] — change log entry documents this fix (2026-05-09).
- [[static-registry-late-joiner-race]] — different race but same shape: a piece of state that the host populates during `LaunchSequence` and that joining clients (or, here, post-spawn ID changes) miss.

## Sources
- 2026-05-09 conversation with [[kevin]] — multi-message debugging session: bug initially looked like "ResidentialBuilding owners aren't restored" (fixed by hoisting the resolver to base `Building`), then re-surfaced as "owners persist for NPCs but not for the host's player Character" — which is this gotcha. Final fix shipped as the new `Character.OnCharacterIdReassigned` static event + dual subscription in `Building.RestoreOwnersFromSaveData` and `CommercialBuilding.RestoreEmployeesFromSaveData`.
- [Assets/Scripts/Core/GameLauncher.cs](../../Assets/Scripts/Core/GameLauncher.cs) — `LaunchSequence` Steps 4 → 5b → 5c → 6 ordering that creates the timing window.
- [Assets/Scripts/Character/Character.cs](../../Assets/Scripts/Character/Character.cs) — `OnNetworkSpawn` fresh-GUID assignment for the host (line ~455) + the new `OnCharacterIdReassigned` event surface.
- [Assets/Scripts/Character/SaveLoad/CharacterDataCoordinator.cs](../../Assets/Scripts/Character/SaveLoad/CharacterDataCoordinator.cs) — `ImportProfile` previousId-vs-new-id comparison + `Character.RaiseCharacterIdReassigned(_character)` invocation.
- [Assets/Scripts/World/Buildings/Building.cs](../../Assets/Scripts/World/Buildings/Building.cs) — canonical dual-subscription consumer.
