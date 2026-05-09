---
name: character-animal
description: Procedures for working with the Animal/Taming subsystem. For architecture (capability-registry role, save flow, network authority), see wiki/systems/character-animal.md.
---

# Character Animal — Skill

**Scope:** Procedures for working with the Animal/Taming subsystem.
For architecture (capability-registry role, save flow, network authority), see
[../../../wiki/systems/character-animal.md](../../../wiki/systems/character-animal.md).

## Purpose

CharacterAnimal is the runtime marker + state holder for any Character that is
an animal. It carries tameability state, exposes the "Tame" interaction option,
and persists tamed state through NPC hibernation.

## Public API

| Member | Type | Description |
|--------|------|-------------|
| `IsTameable` | `bool` | True if this animal can be tamed. Seeded from archetype on spawn. |
| `TameDifficulty` | `float` | 0 = trivial, 1 = impossible. Seeded from archetype. |
| `IsTamed` | `bool` | True after a successful tame. Server-authored. |
| `OwnerProfileId` | `string` | The tamer's portable character profile GUID. Empty until tamed. |
| `SetRandomProvider(IRandomProvider)` | `void` | Swap the RNG for tests or mods. |
| `RequestTameServerRpc` / `TryTameOnServer` | server | Internal — called by CharacterTameAction. |

## Events

None yet. If consumers need notification of ownership change, add a
`NetworkVariable.OnValueChanged` subscription externally on `IsTamed` or
`OwnerProfileId`.

## Dependencies

- `CharacterArchetype.IsTameable`, `CharacterArchetype.TameDifficulty`
- `Character.Archetype`, `Character.CharacterId`, `Character.IsPlayer()`,
  `Character.FloatingTextSpawner`, `Character.CharacterActions`
- `CharacterInteractable.GetCapabilityInteractionOptions` (auto-collection)
- `CharacterDataCoordinator` (save/hibernation pipeline)
- `IRandomProvider` / `UnityRandomProvider`

## How to Add a New Tameable Archetype

1. Create a new `CharacterArchetype` asset under `Assets/Resources/Data/CharacterArchetype/`.
2. Set `IsTameable=true`, choose a `TameDifficulty` (0..1).
3. On the character prefab that uses this archetype, add a child GameObject named
   `CharacterAnimal` and attach the `CharacterAnimal` component.
4. Assign the `CharacterAnimal` reference on the root Character's `_animal`
   serialized slot (or let `GetComponentInChildren` resolve it on Awake).

## How to Query Tamed State from Another System

```csharp
if (character.TryGet<CharacterAnimal>(out var animal) && animal.IsTamed)
{
    string ownerId = animal.OwnerProfileId;
    // ...
}
```

## Current Status (2026-04-21)

Feature shipped on branch `multiplayyer` in 15 commits (spec `cbb0db5`, final
commit `6fec3c7`). Smoke-tested in solo mode: a player initiated a tame against
NPC Samantha (Deer archetype, `TameDifficulty=0.5`) via the interaction menu,
the server executed the roll, NetworkVariables wrote correctly, and the
floating-text broadcast fired on the local client.

**Verified paths:**
- Interaction option surfaces only for eligible targets (`IsTameable && !IsTamed`).
- `CharacterTameAction` routes through `OnApplyEffect` → server-side
  `TryTameOnServer`.
- Server-side roll + NV writes + `ShowTameResultClientRpc` broadcast.

**Not yet smoke-tested (see plan Task 16 for the full matrix):**
- Host↔Client parity — multiplayer roundtrip.
- Client↔Client (two non-hosts).
- "Target currently player-controlled" rejection (`Character.IsPlayer()` gate).
- Hibernation round-trip (tamed animal survives map unload/reload).
- Exception safety on corrupted `AnimalSaveData` JSON.

Until those are exercised, treat the system as "works in solo, multiplayer
unverified." None of the unverified paths are expected to break — they all go
through the same `TryTameOnServer` entry point — but book the tests before
building anything that depends on them.

## Known Follow-Ups

Reviewer advisories that were accepted-but-not-applied during implementation:

- **`Deserialize` non-server branch** — likely dead code under the current
  `CharacterDataCoordinator` flow (only the server calls `Deserialize`).
  Verify during hibernation testing and remove if confirmed unreachable.
- **Empty-`CharacterId` log severity** — currently `LogWarning` when a tame
  succeeds but the interactor's profile ID is empty. Consider elevating to
  `LogError` — a tamed animal with blank ownership indicates an identity-init
  bug, not a normal condition.
- **Deterministic roll tests** — the `IRandomProvider` seam is in place but
  unused. When the project grows a test asmdef, a seeded-RNG test of the roll
  formula becomes a ~10-line unit test.
- **`CharacterMountable` + split.** When mounting is added, `CharacterAnimal`
  will likely split into a pure marker + sibling `CharacterTameable` /
  `CharacterMountable` components. Structured the current class so the split
  is mechanical.

## Evolution Path

When `CharacterMountable` is added, consider splitting `CharacterAnimal` into a
pure marker + sibling `CharacterTameable` / `CharacterMountable` components.
See the evolution note in the wiki page.
