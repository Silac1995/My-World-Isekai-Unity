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

## Evolution Path

When `CharacterMountable` is added, consider splitting `CharacterAnimal` into a
pure marker + sibling `CharacterTameable` / `CharacterMountable` components.
See the evolution note in the wiki page.
