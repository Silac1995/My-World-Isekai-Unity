# Character Animal & Taming System Design

**Date:** 2026-04-21
**Branch:** `multiplayyer`
**Status:** Approved Design

## Problem Statement

The `CharacterArchetype` ScriptableObject already declares `_isTameable` and `_isMountable` capability flags (lines 29–39) as editor-validation hints, but no runtime subsystem implements them. The commented-out `CharacterBio/BioBeast.cs` is a placeholder stub. No `Animal*`, `Creature*`, `Pet*`, `Mount*`, or `Tame*` scripts exist anywhere in the codebase.

The foundation is in place — the capability registry, the facade, the archetype SO, the `IInteractionProvider` hook, the `CharacterAction` pipeline, the `ICharacterSaveData<T>` contract, the NPC hibernation pipeline — but the **animal story itself** is unimplemented. Players cannot tame anything. NPCs cannot tame anything. Archetypes flagged `IsTameable=true` are, at runtime, no different from archetypes flagged `false`.

This spec introduces `CharacterAnimal` as the first real runtime user of the capability-registry system: a `CharacterSystem` subsystem that carries animal-nature state (tameable/tamed/owner), exposes a "Tame" interaction, routes the effect through a new `CharacterTameAction`, and persists tamed state through map hibernation.

### Requirements

1. **Any character — player-controlled or NPC-controlled — can be an animal.** The component sits on the root `Character` the same way every other subsystem does. A player inhabiting a wolf is still a `CharacterAnimal`; a feral NPC deer is also a `CharacterAnimal`. Zero controller branching.
2. **Tame interaction is discoverable through the existing `IInteractionProvider` pipeline.** No special-case wiring in `CharacterInteractable`.
3. **Tame effect goes through `CharacterAction` (rule 22).** UI queues the action; the action runs server-authoritative.
4. **Tame mechanic for v1 is a single instant probability roll** modulated by `TameDifficulty` on the archetype. No items consumed, no progress bar, no cooldown.
5. **Owner identity uses the portable character-profile ID (rule 20)** so ownership survives hibernation, reconnects, and host migration.
6. **Tamed state persists through NPC hibernation (rule 30)** via `ICharacterSaveData<AnimalSaveData>`. `IsTameable` and `TameDifficulty` are NOT saved — they come from the archetype on wake.
7. **Networked correctly (rules 18, 19)** — all state flows through server-write NetworkVariables, re-validated on the server, and visible to every client (Host↔Client, Client↔Client, Host/Client↔NPC).
8. **Documentation shipped alongside code (rules 21, 28)** — SKILL.md in `.agent/skills/character-animal/` and an architecture page in `wiki/systems/character-animal.md`, cross-linked and non-duplicating.

### Non-Goals

- Owner-follow AI, fetch AI, or any behavioral change after taming. Tamed animals keep their existing BT.
- Storage of tamed state on the *player's* portable profile. Tamed state lives on the map's NPC save bundle for v1.
- Inventory-item-gated taming, timed/interruptible taming, repeat cooldowns. These are future scope.
- Mountable, rideable, breedable, or pack-animal features. `CharacterMountable` will ship as a sibling component later — this spec deliberately sets up for that split without premature abstraction.
- Taming of humanoid/sapient archetypes. Achieved by setting `IsTameable=false` on their archetype assets; no code gate needed.

---

## Architecture Overview

### Approach: Single Subsystem (Approach 1)

`CharacterAnimal` is one `CharacterSystem` child component that wears three hats: data container (NetworkVariables), interaction provider (`IInteractionProvider`), and save contract (`ICharacterSaveData<AnimalSaveData>`). This matches the existing reference pattern — `ReclaimNPCInteraction : CharacterSystem, IInteractionProvider` ([ReclaimNPCInteraction.cs](Assets/Scripts/Character/Abandoned/ReclaimNPCInteraction.cs)) — and keeps the entire animal story in one file.

```
Character (root facade)
  +-- [Child GO] CharacterAnimal  <-- NEW
  +-- [Child GO] CharacterMovement  (existing)
  +-- [Child GO] CharacterCombat    (optional, per archetype)
  +-- [Child GO] CharacterNeeds     (existing)
  +-- ... other subsystems
```

`CharacterTameAction` is a separate class under `Assets/Scripts/Character/CharacterActions/` — it is the *effect*, not a subsystem. Rule 22 forbids effects from living in UI or interaction providers.

### Evolution Path (Not in This Spec)

When `CharacterMountable` is added as a sibling component, `CharacterAnimal` may split into:
- `CharacterAnimal` — pure marker + shared species data
- `CharacterTameable` — the `IInteractionProvider` + `ICharacterSaveData` concerns currently bundled

Bookmarked in the SKILL.md and wiki page under "Evolution path." The v1 class is structured so the split is a mechanical refactor, not a rewrite.

---

## Section 1: Data Model

### CharacterArchetype — Add One Field

Existing flags (`_isTameable`, `_isMountable`) stay as editor-validation hints. Add:

```csharp
[Header("Animal Behavior")]
[SerializeField, Range(0f, 1f)]
[Tooltip("0 = always tameable, 1 = untameable. Roll: Random.value > TameDifficulty.")]
private float _tameDifficulty = 0.5f;

public float TameDifficulty => _tameDifficulty;
```

Semantics: success when `UnityEngine.Random.value > TameDifficulty`. `TameDifficulty=0.8` → ~20% success rate.

### CharacterAnimal — NetworkVariables

All four are server-write, everyone-read. Setters are `private`.

```csharp
private NetworkVariable<bool>  _isTameable     = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
private NetworkVariable<float> _tameDifficulty = new(0.5f,  NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
private NetworkVariable<bool>  _isTamed        = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
private NetworkVariable<FixedString64Bytes> _ownerProfileId =
    new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

public bool   IsTameable      => _isTameable.Value;
public float  TameDifficulty  => _tameDifficulty.Value;
public bool   IsTamed         => _isTamed.Value;
public string OwnerProfileId  => _ownerProfileId.Value.ToString();
```

On `OnNetworkSpawn`, the server seeds `_isTameable` and `_tameDifficulty` from `_character.Archetype`. Clients receive these via NV sync — no need for clients to resolve archetypes for this data.

**Why NetworkVariables for `IsTameable`/`TameDifficulty` instead of direct archetype reads:** clients may need to display "This animal is tameable" in UI tooltips without loading the archetype asset graph; future systems may want to temporarily override these at runtime (e.g., a "calm aura" buff lowers difficulty for nearby animals). Paying 8 bytes per animal to keep the door open is cheap.

### AnimalSaveData — DTO

```csharp
[Serializable]
public class AnimalSaveData
{
    public bool IsTamed;
    public string OwnerProfileId;
}
```

Only runtime state. Archetype-derived fields are re-seeded on respawn.

---

## Section 2: Tame Flow

### Happy Path

1. **Discovery.** `CharacterInteractable` proximity-detects the target and calls `_character.GetAll<IInteractionProvider>()` ([CharacterInteractable.cs:100-110](Assets/Scripts/Character/CharacterInteractable.cs#L100-L110)). `CharacterAnimal` is collected automatically.
2. **Option.** `CharacterAnimal.GetInteractionOptions(interactor)` returns a `"Tame"` option **only if** `IsTameable && !IsTamed && interactor != _character`. Otherwise returns empty.
3. **Queue.** Player selects "Tame" in the interaction UI; the UI queues a `CharacterTameAction(target)` on the interactor's `CharacterActions`. NPCs reach the same code path via GOAP/BT.
4. **Server dispatch.** `CharacterTameAction.Execute()` uses the existing action server-routing pattern — if non-server, routes through a `ServerRpc` on the interactor's `Character`. All subsequent steps are server-side.
5. **Server re-validation.** The server re-resolves the target by `NetworkObjectId`, then checks:
   - Target has a `CharacterAnimal` component.
   - `IsTameable && !IsTamed`.
   - Distance from interactor ≤ `archetype.DefaultInteractionRange`.
   - **Target's current controller is NOT a `PlayerController`.** Can't tame a character while a human drives it. (See §3.)
   - Any check fails → silent rejection, no state change, optional server log. No floating text for rejections — those are "illegal" attempts, not "failures."
6. **Roll.** `bool success = UnityEngine.Random.value > target.TameDifficulty;`
7. **Success:** server writes `_isTamed.Value = true`, `_ownerProfileId.Value = interactor.ProfileId`. Broadcasts "Tamed!" floating text via the existing `FloatingTextSpawner` path (already RPC-routed).
8. **Failure:** server broadcasts "Failed!" floating text. No state change.

### Network Authority (Rule 18)

| Concern | Authority |
|---------|-----------|
| NetworkVariable writes | Server only |
| Random roll | Server only — never trust a client roll |
| Re-validation (range, state, controller) | Server only |
| Option exposure (GetInteractionOptions) | Any party — read-only NV access, cheap |
| Floating text | ClientRpc from server |

### Multiplayer Scenarios (Rule 19)

| Scenario | Behavior |
|----------|----------|
| Host tames NPC animal | Host's server-side action writes NVs; client receives via NV sync. |
| Client tames NPC animal | Client's action routes to host via ServerRpc; host writes NVs; both see the result. |
| Client↔Client (two non-hosts) | Both route through host; identical state seen by all. |
| Player-inhabited animal tames another | Same action; `OwnerProfileId` = driving player's profile ID. |
| Tame target is currently player-controlled | Server rejects. Player later releases control → target is tameable again with no special code. |
| Two interactors tame same animal same frame | Server processes actions serially; second one fails re-validation on `!IsTamed`. |
| Late-joining client | Receives current NV state on spawn-sync. No extra RPC. |
| Hibernated map | No live NPCs → no tame attempts possible. No edge case. |

---

## Section 3: Player ↔ NPC Symmetry

Per rule 22 and the player↔NPC parity feedback, **every path through the system must be controller-agnostic.** The only check that mentions a controller is one server-side gate: "is the target currently player-driven?" This gate prevents taming a Character while a human is inhabiting it, but does not preclude that Character from being tamed later when released.

| Role | Can initiate tame? | Can be tamed? |
|------|--------------------|----------------|
| Player (human archetype) | Yes | Only if archetype `IsTameable=true` and not currently player-controlled |
| Player-inhabited animal | Yes | Only if archetype `IsTameable=true` and not currently player-controlled |
| NPC (humanoid archetype) | Yes | Only if archetype `IsTameable=true` |
| NPC animal | Yes | Only if archetype `IsTameable=true` |

Owner identity (`OwnerProfileId`) resolves to *whoever is driving the interactor Character at the moment of the roll*:
- Player driving → player's character-profile ID (portable GUID/string).
- NPC driving → NPC's stable hibernation ID.

This means a player inhabiting a wolf can tame animals under that wolf-avatar's identity; ownership stays with the profile even if the player later swaps controllers.

**Open question flagged for verification at plan time:** the exact accessor on `Character` that returns the "current owner profile ID" regardless of controller type. Candidate names: `Character.ProfileId`, `Character.OwnerId`, `Character.GetSaveId()`. The plan phase will grep the codebase before committing to a name; if none exists, add one as part of the plan.

---

## Section 4: Save / Load Integration

### Hibernation Round-Trip (Rule 30)

`CharacterAnimal` implements `ICharacterSaveData<AnimalSaveData>`:

- `Export()` returns `new AnimalSaveData { IsTamed = _isTamed.Value, OwnerProfileId = _ownerProfileId.Value.ToString() }`.
- `Import(AnimalSaveData data)` runs server-side on respawn; writes both NVs. Client-side calls are no-ops with a debug log.
- Priority in `CharacterDataCoordinator`: mid-tier. Must run *after* identity/archetype resolution (so the archetype is known and `_isTameable`/`_tameDifficulty` are already seeded) and *before* any future tamed-state-dependent systems (e.g., owner-follow AI). Exact numeric priority to be chosen at plan time by reading existing providers.

### What Is NOT Saved

- `IsTameable` and `TameDifficulty` — blueprint data, re-derived from archetype on every wake. Changing an archetype's `IsTameable` later will affect already-saved animals on their next respawn. This is a feature: it lets designers rebalance tameability without migrating save files.

### Player-Inhabited Animal During Hibernation

This is an edge case this spec does *not* fully resolve: what happens if a player is inhabiting an animal at the moment their presence leaves the map and triggers hibernation? Controller-swap + hibernation is its own unresolved problem. For v1:

- Assumption: animals are NPC-controlled at hibernation time.
- Enforcement: on hibernation entry, if any `CharacterAnimal` has a `PlayerController`, log a `Debug.LogWarning` with the character ID and proceed — do not block hibernation, do not crash. This surfaces the edge case without gating unrelated work.
- Follow-up: resolved in the controller-swap spec, not this one.

### Exception Safety (Rule 31)

Every entry point that can fail — `Import`, archetype lookup during seeding, the `ServerRpc` callback inside `CharacterTameAction`, the floating-text broadcast — is wrapped in `try/catch` with `Debug.LogException(e)` plus context (animal `NetworkObjectId`, interactor profile ID, action name). A corrupted `AnimalSaveData` on one NPC must not kill map wake-up.

---

## Section 5: Component Layout & File Manifest

### New Files

| Path | Purpose |
|------|---------|
| `Assets/Scripts/Character/CharacterAnimal/CharacterAnimal.cs` | `CharacterSystem, IInteractionProvider, ICharacterSaveData<AnimalSaveData>`. |
| `Assets/Scripts/Character/CharacterAnimal/AnimalSaveData.cs` | `[Serializable]` DTO. |
| `Assets/Scripts/Character/CharacterActions/CharacterTameAction.cs` | `CharacterAction` subclass. Server roll, state write, floating text. |
| `.agent/skills/character-animal/SKILL.md` | Procedures: add a tameable archetype, query `IsTamed`, extend the roll. |
| `wiki/systems/character-animal.md` | Architecture: registry role, save flow, network authority, evolution to `CharacterMountable`. |
| `Assets/Resources/Data/CharacterArchetype/Deer.asset` (or similar) | One example tameable archetype for demo/test. |

### Edited Files

| Path | Change |
|------|--------|
| `Assets/Scripts/Character/Archetype/CharacterArchetype.cs` | Add `[SerializeField, Range(0,1)] private float _tameDifficulty = 0.5f;` + `TameDifficulty` getter. |
| `Assets/Scripts/Character/Character.cs` | Add `[SerializeField] private CharacterAnimal _animal;` slot; auto-assign in `Awake()` via `GetComponentInChildren<CharacterAnimal>()` fallback (facade pattern). |
| `wiki/INDEX.md` | Add entry for the new systems page (run `/map` to regenerate). |

### GameObject Hierarchy

```
Character (root)
├── CharacterAnimal       <-- new child GO, holds CharacterAnimal.cs only
├── CharacterMovement     (existing)
├── CharacterCombat       (existing, optional)
├── CharacterNeeds        (existing)
└── ... other subsystems
```

The Animal prefab is assembled only for animal archetypes. Humanoid/sapient archetypes do not have a `CharacterAnimal` child.

---

## Section 6: Testing Plan

### Unit / Deterministic

- `TameDifficulty=0` → 100 rolls succeed.
- `TameDifficulty=1` → 100 rolls fail.
- `TameDifficulty=0.5` with a seeded `Random` over N=1000 → ~50% success within a tolerance band.

### Editor Smoke (Solo)

- Spawn a deer archetype (`IsTameable=true`, `TameDifficulty=0.5`), walk up, observe the "Tame" interaction option, invoke it, verify floating text and `IsTamed` / `OwnerProfileId` change via Dev Mode inspector.
- Spawn a humanoid archetype (`IsTameable=false`), verify no "Tame" option exposed.
- Spawn a tamed deer (`IsTamed=true`), verify no "Tame" option re-exposed.

### Multiplayer (Host + 1 Client)

- Host tames animal A, client tames animal B simultaneously — both outcomes visible to both sides via NV sync.
- Client tames; host sees state. Host tames; client sees state.
- Late-joining client observes existing `IsTamed` animals correctly.

### NPC-Initiated

- Use Dev Mode to force an NPC wolf to queue `CharacterTameAction` targeting a deer; verify identical code path and result.

### Blocked Case

- Player A inhabits wolf (archetype `IsTameable=true`); player B attempts tame; server rejects silently; no floating text; no state change; no crash.
- Player A releases control; player B retries; now succeeds (or fails per roll), state writes correctly, `OwnerProfileId` = player B.

### Hibernation Round-Trip

- Tame an animal on an active map, leave map (trigger hibernation), re-enter. Assert `IsTamed=true` and `OwnerProfileId` identical.
- Hibernate while a player is inhabiting an animal → warning logged, hibernation proceeds, no crash.

---

## Exit Criteria

A reviewer can mark this spec as "implemented" when:

1. All files in the manifest are present and compile cleanly.
2. `CharacterArchetype.TameDifficulty` is editable in the Inspector.
3. The example tameable archetype asset exists and is demonstrable in an editor test scene.
4. All testing scenarios in Section 6 pass manually (or are automated where feasible).
5. `.agent/skills/character-animal/SKILL.md` and `wiki/systems/character-animal.md` exist, are cross-linked, and do not duplicate content (wiki = architecture, skill = procedures).
6. `wiki/INDEX.md` includes the new system page.
7. No new warnings or errors in the Unity Console during a full play session.

---

## Appendix: Evolution Bookmarks

Not in this spec, but deliberately accommodated:

- **`CharacterMountable`** — sibling component. Would likely motivate splitting `CharacterAnimal` into a pure marker + `CharacterTameable` pair.
- **Timed / interruptible taming** (original scope C). Would replace the instant roll in `CharacterTameAction` with a progress tick, preserving the surrounding contracts.
- **Item-gated taming.** Would add an inventory check in the action's server re-validation step.
- **Owner-follow / fetch AI.** New BT node or GOAP action that reads `CharacterAnimal.IsTamed` and `OwnerProfileId`.
- **Tamed state on player's portable profile.** Would require a new `ICharacterSaveData` on the player's side listing owned animal IDs, plus a cross-map lookup.
- **Repeated-attempt cooldown.** Add a `_lastTameAttemptTime` NV and a cooldown check in server re-validation.
