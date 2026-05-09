---
type: system
title: "Character Animal"
tags: [character, animal, taming, network, save, tier-2]
created: 2026-04-21
updated: 2026-04-21
sources:
  - "Assets/Scripts/Character/CharacterAnimal/CharacterAnimal.cs"
  - "Assets/Scripts/Character/CharacterAnimal/AnimalSaveData.cs"
  - "Assets/Scripts/Character/CharacterActions/CharacterTameAction.cs"
  - ".agent/skills/character-animal/SKILL.md"
related:
  - "[[character]]"
  - "[[network]]"
  - "[[save-load]]"
  - "[[character-archetype]]"
  - "[[character-interaction]]"
  - "[[world-map-hibernation]]"
  - "[[world-macro-simulation]]"
  - "[[kevin]]"
status: stable
confidence: high
primary_agent: character-system-specialist
secondary_agents: []
owner_code_path: "Assets/Scripts/Character/CharacterAnimal/"
depends_on:
  - "[[character]]"
  - "[[network]]"
  - "[[save-load]]"
  - "[[character-archetype]]"
depended_on_by:
  - "[[character-interaction]]"
---

# Character Animal

## Summary

`CharacterAnimal` is a child-GameObject component on animal-type Characters that
marks the entity as tameable and carries its runtime tame state. It integrates
with the [[character]] facade's capability-registry pattern (via
`IInteractionProvider`) to surface the "Tame" interaction option automatically,
with [[network]] for server-authoritative state, and with [[save-load]] for tame
persistence through NPC hibernation. The taming effect is separated from the
UI/AI trigger layer via `CharacterTameAction`, in compliance with project rule 22.

---

## Purpose

Give any character archetype the ability to be tamed without modifying core
Character logic. A single component presence — `CharacterAnimal` — is enough to
make a creature tameable, register it with the interaction system, and persist its
ownership across sessions.

## Responsibilities

- Holding and syncing the four tame-state `NetworkVariable`s (`IsTameable`,
  `TameDifficulty`, `IsTamed`, `OwnerProfileId`) from server to all clients.
- Seeding archetype-derived values (`IsTameable`, `TameDifficulty`) on server
  spawn via `OnNetworkSpawn`.
- Exposing the "Tame" `InteractionOption` via `IInteractionProvider` so
  [[character-interaction]] picks it up without explicit registration.
- Implementing the server-authoritative tame gate (`TryTameOnServer`) with
  re-validation (tameable, not already tamed, not player-controlled, in range).
- Broadcasting the outcome (floating text) to all clients via `ShowTameResultClientRpc`.
- Serializing and deserializing `AnimalSaveData` through `ICharacterSaveData<T>`
  so tamed state survives NPC hibernation (see [[world-map-hibernation]]).

**Non-responsibilities:**

- Does **not** decide how taming is triggered by a player or NPC — that is
  `CharacterTameAction` queued through `CharacterActions`.
- Does **not** own follow-AI, mount logic, or AI behaviour after taming — future
  `CharacterMountable` sibling and AI goal extensions handle that.
- Does **not** save tamed state into the player's portable profile — currently
  only persisted on the NPC side; cross-host portability is an open issue.

## Key classes / files

| File | Role |
|------|------|
| [CharacterAnimal.cs](../../Assets/Scripts/Character/CharacterAnimal/CharacterAnimal.cs) | Main component. `NetworkBehaviour` child on animal prefabs. |
| [AnimalSaveData.cs](../../Assets/Scripts/Character/CharacterAnimal/AnimalSaveData.cs) | Serializable DTO — `IsTamed` + `OwnerProfileId` only. |
| [IRandomProvider.cs](../../Assets/Scripts/Character/CharacterAnimal/IRandomProvider.cs) | Seam for the tame-roll RNG; swappable in tests. |
| [CharacterTameAction.cs](../../Assets/Scripts/Character/CharacterActions/CharacterTameAction.cs) | `CharacterAction` subclass — routes the tame effect to the server. |

## Architecture

### Capability-registry pattern

`CharacterAnimal` implements `IInteractionProvider`. When [[character-interaction]]
(via `CharacterInteractable.GetCapabilityInteractionOptions`) collects interaction
options from a target, it iterates every `IInteractionProvider` found on the
target's hierarchy. Because `CharacterAnimal` implements that interface, no
explicit registration call is needed — placing the component is enough.

This is the standard capability-registry pattern used across the character
subsystem: the presence of a component signals the capability; any higher-level
system that wants to consume it does so through a shared interface, not a concrete
type check.

### Why NetworkVariables instead of direct archetype reads

Two values — `IsTameable` and `TameDifficulty` — could in principle be read
directly from the `CharacterArchetype` asset. They are stored as
`NetworkVariable`s instead for two reasons:

1. **Tooltip clients without archetype load.** A client showing the tame
   interaction tooltip needs these values immediately; requiring an archetype
   asset load on the client adds latency and coupling.
2. **Runtime override flexibility.** A server-side event (quest unlock, debuff)
   can change tamability at runtime without touching the archetype asset.
   `NetworkVariable` propagates the change to all clients automatically.

`IsTamed` and `OwnerProfileId` are runtime state with no archetype equivalent —
they must be `NetworkVariable`s.

### Character hierarchy with CharacterAnimal

```
Character (root)
│  Character.cs          ← facade; exposes _animal getter
│  CharacterActions.cs
│  ...
│
├─ CharacterAnimal (child GO)
│     CharacterAnimal.cs  ← this system
│
├─ CharacterStats (child GO)
├─ CharacterNeeds (child GO)
├─ CharacterMovement (child GO)
├─ CharacterInteraction (child GO)
│     CharacterInteractable.cs  ← collects IInteractionProvider from all children
└─ ... (other subsystems)
```

### CharacterTameAction — separating effect from UI

Per project rule 22, gameplay effects must go through `CharacterAction`, not be
implemented inside player input handlers or NPC GOAP nodes. `CharacterTameAction`
is the shared action:

- A player clicking "Tame" queues `CharacterTameAction` on their `CharacterActions`.
- An NPC AI planning a tame goal queues the same `CharacterTameAction`.
- `OnApplyEffect` routes to `TryTameOnServer` (if already on server) or
  `RequestTameServerRpc` (if on a client). The server is the single point of truth.

## Public API / entry points

| Member | Kind | Description |
|--------|------|-------------|
| `IsTameable` | `bool` (read) | True if the animal can be tamed. Seeded from archetype on spawn. |
| `TameDifficulty` | `float` (read) | 0 = trivial, 1 = impossible. Used in the server-side roll. |
| `IsTamed` | `bool` (read) | True after a successful server tame. |
| `OwnerProfileId` | `string` (read) | The tamer's portable character GUID. Empty until tamed. |
| `SetRandomProvider(IRandomProvider)` | `void` | Swap the tame-roll RNG (test seam). |
| `RequestTameServerRpc` | server RPC | Called by `CharacterTameAction.OnApplyEffect` from non-server callers. |
| `TryTameOnServer` | server method | Server-only gate and roll. Called directly from `OnApplyEffect` on the host. |

`SaveKey = "CharacterAnimal"`, `LoadPriority = 40`.

## Data flow

### Live tame roundtrip

```
Player clicks "Tame"  OR  NPC AI queues tame goal
          │
          ▼
CharacterActions.ExecuteAction(new CharacterTameAction(interactor, target))
          │
          ▼ OnApplyEffect()
          │
          ├── IsServer? ──Yes──► TryTameOnServer(interactorRef)
          │                              │
          └── No ─────────────► RequestTameServerRpc(interactorRef)
                                         │
                                         ▼
                               TryTameOnServer (server)
                                         │
                               Re-validate:
                                 IsTameable? ──No──► reject (log)
                                 IsTamed?    ──Yes──► reject (log)
                                 IsPlayer()? ──Yes──► reject (log)
                                 dist > range?──Yes──► reject (log)
                                         │
                                         ▼
                               Roll: _random.Value() > TameDifficulty
                                         │
                               Success: NV writes _isTamed=true, _ownerProfileId=...
                                         │
                               SpawnTameResultText() [server-local]
                               ShowTameResultClientRpc(success) [→ all non-server clients]
```

### NV sync to clients

All four `NetworkVariable`s use `ReadPermission.Everyone` / `WritePermission.Server`.
Clients receive automatic delta-sync on change; no additional ClientRpc is needed
for the state itself — only the floating text result requires an explicit broadcast.

## Network authority

| NetworkVariable | Server write | Client read | Notes |
|-----------------|:------------:|:-----------:|-------|
| `_isTameable` | Yes | Yes | Seeded from archetype on `OnNetworkSpawn`. |
| `_tameDifficulty` | Yes | Yes | Seeded from archetype on `OnNetworkSpawn`. |
| `_isTamed` | Yes | Yes | Written on successful tame roll. |
| `_ownerProfileId` | Yes | Yes | `FixedString64Bytes`; truncated at 63 chars if GUID exceeds limit. |

Server-side re-validation in `TryTameOnServer` guards against four stale-client
attack vectors: non-tameable targets, already-tamed targets, player-controlled
targets, and out-of-range initiators. The check uses
`character.Archetype.DefaultInteractionRange` (fallback: `3.5f` Unity units).

## State & persistence

### What is saved

`AnimalSaveData` carries only runtime-mutable state:

| Field | Type | Saved? | Reason |
|-------|------|--------|--------|
| `IsTamed` | `bool` | Yes | Changes at runtime; must survive hibernation. |
| `OwnerProfileId` | `string` | Yes | Changes at runtime; must survive hibernation. |
| `IsTameable` | — | **No** | Deterministic from archetype; re-seeded on spawn. |
| `TameDifficulty` | — | **No** | Deterministic from archetype; re-seeded on spawn. |

### Hibernation flow

`CharacterDataCoordinator` collects all `ICharacterSaveData` implementors on an
NPC before it hibernates (see [[world-map-hibernation]]). `CharacterAnimal`
participates at `LoadPriority = 40`.

```
Map player count → 0
       │
       ▼
CharacterDataCoordinator.SerializeAll()
       │
       └── CharacterAnimal.Serialize() → AnimalSaveData{ IsTamed, OwnerProfileId }
                                         stored in HibernatedNPCData
Map wakes up
       │
       ▼
CharacterDataCoordinator.DeserializeAll()
       │
       └── CharacterAnimal.Deserialize(data)
                └── server-only NV writes: _isTamed, _ownerProfileId
                    (IsTameable, TameDifficulty re-seeded by OnNetworkSpawn)
```

`Deserialize` is a no-op on non-server peers; `NetworkVariable` sync from the
server propagates state to clients automatically after spawn.

## Dependencies

### Upstream

- [[character]] — `CharacterAnimal` is a `CharacterSystem` child; accesses
  `_character.Archetype`, `_character.CharacterId`, `_character.IsPlayer()`,
  `_character.FloatingTextSpawner`, `_character.CharacterActions`.
- [[character-archetype]] — `IsTameable` and `TameDifficulty` fields seed the NVs.
- [[network]] — four `NetworkVariable`s; `RequestTameServerRpc`; `ShowTameResultClientRpc`.
- [[save-load]] — `ICharacterSaveData<AnimalSaveData>` contract; `CharacterDataCoordinator`.

### Downstream

- [[character-interaction]] — `CharacterInteractable.GetCapabilityInteractionOptions`
  collects the "Tame" option via `IInteractionProvider`.

## Player ↔ NPC symmetry

Per project rule 22, any character can initiate taming and any non-player-controlled
character can be tamed.

| Role | Can initiate tame | Can be tamed |
|------|:-----------------:|:------------:|
| Player | Yes (via HUD interaction click) | No — blocked by `_character.IsPlayer()` server check |
| NPC | Yes (via `CharacterTameAction` in AI plan) | Yes — if `IsTameable` and not `IsTamed` |

The `IsPlayer()` block is enforced server-side, so a client cannot spoof a tame
attempt on a player-controlled character even if it somehow queues the RPC.

## Known gotchas / edge cases

- **`FixedString64Bytes` cap** — `OwnerProfileId` is truncated at 63 characters
  if the GUID is longer. Standard Unity GUIDs (36 chars) fit safely; custom IDs
  longer than 63 chars will be silently truncated with a `LogWarning`.
- **Deserialize is server-only** — calling `Deserialize` on a client is a no-op
  by design. Clients receive state through NV sync, not through the save pipeline.
  If `CharacterDataCoordinator` ever calls `Deserialize` on clients, this branch
  will log and return rather than silently corrupt state.
- **Archetype null on spawn** — if the Character has no archetype set, `OnNetworkSpawn`
  logs a warning and leaves the NVs at their defaults (`IsTameable=false`,
  `TameDifficulty=0.5f`). The animal is effectively non-tameable.
- **Roll at `TameDifficulty=1.0`** — `_random.Value() > 1.0f` is always false
  (Unity's `Random.value` returns `[0, 1]`), so `TameDifficulty=1.0` means
  impossible. Intended behavior; document in archetype authoring notes.
- **Empty `CharacterId`** — if the tamer's `Character.CharacterId` is empty at
  tame time, `OwnerProfileId` is written as `default` (empty). A warning is
  logged. This can happen if the player's profile has not yet resolved its GUID.

## Open questions / TODO

- [ ] **Multiplayer / hibernation / exception-safety paths unverified** — as of
  2026-04-21 the feature has been smoke-tested only in solo mode (happy path:
  player tames NPC, server rolls, NVs update, floating text fires). The
  scenarios from [[character-animal]]'s test matrix that remain unexercised:
  Host↔Client parity, Client↔Client, the "target currently player-controlled"
  rejection, hibernation round-trip, and corrupted-`AnimalSaveData` exception
  safety. All of these go through the same server entry point
  (`TryTameOnServer`) so they are expected to work, but treat ownership and
  persistence as unconfirmed until the full matrix is exercised.
- [ ] **`CharacterMountable`** — sibling component planned to handle mount logic
  (enter/exit, speed modifiers, dismount). When added, evaluate splitting
  `CharacterAnimal` into a pure marker + `CharacterTameable` + `CharacterMountable`
  to keep each component single-responsibility.
- [ ] **Timed / item-gated taming** — the current roll is instant. A
  `duration > 0f` on `CharacterTameAction` and an item-cost check in `CanExecute`
  would enable item-gated or channeled taming without changing the server gate.
- [ ] **Owner-follow AI** — after taming, the animal has no goal to follow its
  owner. A GOAP goal / BT leaf that checks `IsTamed && OwnerProfileId ==
  myOwner` is needed; tracked as a future [[ai]] extension.
- [ ] **Tamed state in portable player profile** — `AnimalSaveData` lives on the
  NPC side only. If a player tames an animal on Server A then travels to Server B,
  the tame relationship is not carried. Requires either mirroring ownership into
  the player's `CharacterProfileSaveData`, or a cross-server ownership registry.
- [ ] **No `primary_agent` for animal-specific scenarios** — `character-system-specialist`
  covers CharacterAnimal by default; a dedicated agent is not yet warranted. Revisit
  when mount/follow/cross-server ownership work begins.

## Change log

- 2026-04-21 — Initial architecture page. — Claude / [[kevin]]
- 2026-04-21 — Solo-mode smoke test passed (player tames NPC, server rolls, NVs update, floating text broadcasts). Multiplayer/hibernation/exception paths remain unverified; noted in Open questions. — Claude / [[kevin]]

## Links

- SKILL.md (procedures): [.agent/skills/character-animal/SKILL.md](../../.agent/skills/character-animal/SKILL.md)

## Sources

- [CharacterAnimal.cs](../../Assets/Scripts/Character/CharacterAnimal/CharacterAnimal.cs) — primary implementation
- [AnimalSaveData.cs](../../Assets/Scripts/Character/CharacterAnimal/AnimalSaveData.cs) — save DTO
- [IRandomProvider.cs](../../Assets/Scripts/Character/CharacterAnimal/IRandomProvider.cs) — RNG seam
- [CharacterTameAction.cs](../../Assets/Scripts/Character/CharacterActions/CharacterTameAction.cs) — action layer
- [.agent/skills/character-animal/SKILL.md](../../.agent/skills/character-animal/SKILL.md) — operational procedures
- 2026-04-21 conversation with [[kevin]] — Tasks 14 & 15
