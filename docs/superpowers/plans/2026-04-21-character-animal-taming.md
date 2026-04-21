# Character Animal & Taming Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship `CharacterAnimal` as the first real runtime user of the capability-registry foundation — a subsystem that carries `IsTameable`/`IsTamed`/`OwnerProfileId` as NetworkVariables, exposes a "Tame" `IInteractionProvider` option, persists through NPC hibernation via `ICharacterSaveData<AnimalSaveData>`, and routes the effect through a new server-authoritative `CharacterTameAction` (rule 22). Scope is deliberately narrow: instant probability roll, no items, no owner-follow AI, no mount logic.

**Architecture:** Single `CharacterAnimal : CharacterSystem, IInteractionProvider, ICharacterSaveData<AnimalSaveData>` component on a child GO of the animal prefab (matches the existing `ReclaimNPCInteraction` pattern). Server-authoritative tame flow via a `ServerRpc` on `CharacterAnimal`, with a `ClientRpc` to trigger floating text on every client. RNG is injected through an `IRandomProvider` seam so the roll is unit-testable in the future and swappable for modded behavior.

**Tech Stack:** Unity 2022 LTS, C#, Unity Netcode for GameObjects (NGO), Newtonsoft.Json (existing), NetworkVariable + `Rpc(SendTo.Server|Everyone)` attributes.

**Spec reference:** [docs/superpowers/specs/2026-04-21-character-animal-taming-design.md](../specs/2026-04-21-character-animal-taming-design.md)

---

## File Structure

**New files (6):**

| Path | Responsibility |
|------|----------------|
| `Assets/Scripts/Character/CharacterAnimal/CharacterAnimal.cs` | Subsystem: NetworkVariables, interaction provider, save contract, server RPC. |
| `Assets/Scripts/Character/CharacterAnimal/AnimalSaveData.cs` | `[Serializable]` DTO for save/hibernation. |
| `Assets/Scripts/Character/CharacterAnimal/IRandomProvider.cs` | Injectable RNG seam (interface + `UnityRandomProvider` default). |
| `Assets/Scripts/Character/CharacterActions/CharacterTameAction.cs` | `CharacterAction` — queued by the interaction UI/NPC AI, dispatches server-side roll. |
| `.agent/skills/character-animal/SKILL.md` | Procedures: how to add a tameable archetype, query `IsTamed`, extend the roll. |
| `wiki/systems/character-animal.md` | Architecture page: registry role, save flow, network authority, evolution to `CharacterMountable`. |

**New asset (1):**

| Path | Purpose |
|------|---------|
| `Assets/Resources/Data/CharacterArchetype/Deer.asset` | Example tameable archetype for demo/test. `IsTameable=true`, `TameDifficulty=0.5`, `BodyType=Quadruped`, baseline speeds. |

**Edited files (3):**

| Path | Change |
|------|--------|
| `Assets/Scripts/Character/Archetype/CharacterArchetype.cs` | Add `_tameDifficulty` serialized field + getter. |
| `Assets/Scripts/Character/Character.cs` | Add `[SerializeField] private CharacterAnimal _animal;` slot + property with registry-first fallback. |
| `wiki/INDEX.md` | One-line entry for the new systems page. |

---

## Pre-flight — References You Will Need

Keep these open in tabs before starting:

- [Assets/Scripts/Character/Abandoned/ReclaimNPCInteraction.cs](../../../Assets/Scripts/Character/Abandoned/ReclaimNPCInteraction.cs) — 79-line reference for `CharacterSystem, IInteractionProvider` + `ServerRpc`.
- [Assets/Scripts/Character/CharacterNeeds/CharacterNeeds.cs](../../../Assets/Scripts/Character/CharacterNeeds/CharacterNeeds.cs) L1-80 — reference for `ICharacterSaveData<T>` implementation shape (Serialize/Deserialize + non-generic bridge).
- [Assets/Scripts/Character/SaveLoad/CharacterSaveDataBase.cs](../../../Assets/Scripts/Character/SaveLoad/CharacterSaveDataBase.cs) — the `CharacterSaveDataHelper.SerializeToJson`/`DeserializeFromJson` bridge used by every save-data subsystem.
- [Assets/Scripts/Character/CharacterActions/CharacterAction.cs](../../../Assets/Scripts/Character/CharacterActions/CharacterAction.cs) — base class. Key hooks: `CanExecute`, `OnStart`, `OnApplyEffect`, `OnCancel`.
- [Assets/Scripts/Character/CharacterActions/CharacterHarvestAction.cs](../../../Assets/Scripts/Character/CharacterActions/CharacterHarvestAction.cs) — typical action with distance validation, reference for `CanExecute` shape.
- [Assets/Scripts/Character/CharacterActions/CharacterActions.cs](../../../Assets/Scripts/Character/CharacterActions/CharacterActions.cs) L24-76 — `ExecuteAction` flow: instant (`Duration<=0`) vs timed. For `Duration=0`, `OnApplyEffect` runs immediately on the caller.
- [Assets/Scripts/Interactable/CharacterInteractable.cs](../../../Assets/Scripts/Interactable/CharacterInteractable.cs) L100-110 — `GetCapabilityInteractionOptions` auto-collects every `IInteractionProvider` via `_character.GetAll<IInteractionProvider>()`. Zero wiring needed on your side.
- [Assets/Scripts/Character/Character.cs](../../../Assets/Scripts/Character/Character.cs) L263 — `CharacterId` = `NetworkCharacterId.Value.ToString()` is the portable profile GUID you'll write to `OwnerProfileId`.
- [Assets/Scripts/Character/Character.cs](../../../Assets/Scripts/Character/Character.cs) L569 — `IsPlayer()` returns `_controller is PlayerController`. Use this for the "target is currently player-driven" gate.
- [Assets/Scripts/Character/FloatingTextSpawner.cs](../../../Assets/Scripts/Character/FloatingTextSpawner.cs) L59-62 — `SpawnText(string message, Color color)` is the simplest API; works locally on each client.

**LoadPriority conventions (from existing subsystems):**

| Subsystem | Priority |
|-----------|----------|
| CharacterProfile (identity/archetype) | 0 |
| CharacterStats | 10 |
| CharacterAbilities / Skills | 20 |
| CharacterEquipment | 30 |
| CharacterNeeds / Traits | 40 |
| CharacterBookKnowledge / Relation | 50 |
| Community / Job / Party / Schedule | 60 |
| Combat / MapTracker | 70 |

→ **`CharacterAnimal.LoadPriority = 40`** — runs after identity (0) and stats (10), before relationship/job systems. Tamed state doesn't gate or depend on any of those.

---

## Task 1: Add `TameDifficulty` Field to `CharacterArchetype`

**Files:**
- Modify: `Assets/Scripts/Character/Archetype/CharacterArchetype.cs`

- [ ] **Step 1: Read the current file**

Open [Assets/Scripts/Character/Archetype/CharacterArchetype.cs](../../../Assets/Scripts/Character/Archetype/CharacterArchetype.cs). Locate the "Capabilities" header block ending at line 39 (`public bool IsMountable => _isMountable;`).

- [ ] **Step 2: Add `_tameDifficulty` field + getter**

Immediately after the existing capability flags (after line 39 `public bool IsMountable => _isMountable;`), insert:

```csharp

    [Header("Animal Behavior")]
    [Tooltip("0 = always tameable, 1 = untameable. Roll: UnityEngine.Random.value > TameDifficulty.")]
    [SerializeField, Range(0f, 1f)] private float _tameDifficulty = 0.5f;

    public float TameDifficulty => _tameDifficulty;
```

- [ ] **Step 3: Refresh assets, confirm compile**

Run the `assets-refresh` MCP tool and then `console-get-logs` (or check the Unity Console panel) — expected: no compile errors.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Character/Archetype/CharacterArchetype.cs
git commit -m "feat(archetype): add TameDifficulty field (0..1 roll threshold)"
```

---

## Task 2: Create `AnimalSaveData` DTO

**Files:**
- Create: `Assets/Scripts/Character/CharacterAnimal/AnimalSaveData.cs`

- [ ] **Step 1: Create folder + file**

Use `script-update-or-create` MCP tool to create `Assets/Scripts/Character/CharacterAnimal/AnimalSaveData.cs`. (Creating the file auto-creates the folder.)

- [ ] **Step 2: Write the DTO**

```csharp
using System;

/// <summary>
/// Persistent portion of CharacterAnimal state — rides in the NPC hibernation bundle
/// via CharacterDataCoordinator. IsTameable and TameDifficulty are NOT saved here;
/// they are re-seeded from the archetype on respawn.
/// </summary>
[Serializable]
public class AnimalSaveData
{
    public bool IsTamed;
    public string OwnerProfileId;
}
```

- [ ] **Step 3: Refresh assets, confirm compile**

Run `assets-refresh`. Expected: no errors.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Character/CharacterAnimal/AnimalSaveData.cs
git commit -m "feat(animal): add AnimalSaveData DTO (IsTamed + OwnerProfileId)"
```

---

## Task 3: Create `IRandomProvider` Seam + Default Impl

**Files:**
- Create: `Assets/Scripts/Character/CharacterAnimal/IRandomProvider.cs`

- [ ] **Step 1: Write the interface + default**

```csharp
using UnityEngine;

/// <summary>
/// Random-number seam used by CharacterTameAction's server roll.
/// Keeps UnityEngine.Random out of business logic so the roll is swappable for
/// deterministic tests or modded providers. The default UnityRandomProvider is
/// used in production.
/// </summary>
public interface IRandomProvider
{
    /// <summary>Returns a uniformly-distributed float in [0, 1).</summary>
    float Value();
}

public sealed class UnityRandomProvider : IRandomProvider
{
    public float Value() => Random.value;
}
```

- [ ] **Step 2: Refresh assets, confirm compile**

Run `assets-refresh`. Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Character/CharacterAnimal/IRandomProvider.cs
git commit -m "feat(animal): add IRandomProvider seam + UnityRandomProvider default"
```

---

## Task 4: Create `CharacterAnimal` Skeleton with NetworkVariables

**Files:**
- Create: `Assets/Scripts/Character/CharacterAnimal/CharacterAnimal.cs`

- [ ] **Step 1: Write the skeleton**

```csharp
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Marks a Character as an animal and carries its tameability state.
/// Implements IInteractionProvider (exposes the "Tame" option) and
/// ICharacterSaveData&lt;AnimalSaveData&gt; (persists tamed state through hibernation).
/// See wiki/systems/character-animal.md for the full architecture notes.
/// </summary>
public class CharacterAnimal : CharacterSystem,
    IInteractionProvider,
    ICharacterSaveData<AnimalSaveData>
{
    // ── Network State ───────────────────────────────────────────────────
    private NetworkVariable<bool>  _isTameable     = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<float> _tameDifficulty = new(0.5f,  NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<bool>  _isTamed        = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<FixedString64Bytes> _ownerProfileId =
        new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // ── Public Read-Only API ────────────────────────────────────────────
    public bool   IsTameable     => _isTameable.Value;
    public float  TameDifficulty => _tameDifficulty.Value;
    public bool   IsTamed        => _isTamed.Value;
    public string OwnerProfileId => _ownerProfileId.Value.ToString();

    // ── Random seam (overridable for tests) ─────────────────────────────
    private IRandomProvider _random = new UnityRandomProvider();
    public void SetRandomProvider(IRandomProvider random) => _random = random ?? new UnityRandomProvider();

    // ── Lifecycle ───────────────────────────────────────────────────────
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!IsServer) return;

        // Seed archetype-derived fields on the server.
        var archetype = _character != null ? _character.Archetype : null;
        if (archetype == null)
        {
            Debug.LogWarning($"[CharacterAnimal] No archetype on '{_character?.CharacterName ?? gameObject.name}' — leaving defaults.");
            return;
        }

        _isTameable.Value     = archetype.IsTameable;
        _tameDifficulty.Value = archetype.TameDifficulty;
    }

    // ── IInteractionProvider (Task 6) ───────────────────────────────────
    public List<InteractionOption> GetInteractionOptions(Character interactor)
    {
        // Implemented in Task 6.
        return new List<InteractionOption>();
    }

    // ── ICharacterSaveData<AnimalSaveData> (Task 5) ─────────────────────
    public string SaveKey => "CharacterAnimal";
    public int LoadPriority => 40;

    public AnimalSaveData Serialize() => new AnimalSaveData();
    public void Deserialize(AnimalSaveData data) { }

    string ICharacterSaveData.SerializeToJson() => CharacterSaveDataHelper.SerializeToJson(this);
    void ICharacterSaveData.DeserializeFromJson(string json) => CharacterSaveDataHelper.DeserializeFromJson(this, json);
}
```

- [ ] **Step 2: Refresh assets, confirm compile**

Run `assets-refresh`. Expected: no errors. You now have a compiling stub.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Character/CharacterAnimal/CharacterAnimal.cs
git commit -m "feat(animal): CharacterAnimal skeleton with NetworkVariables + archetype seeding"
```

---

## Task 5: Implement `ICharacterSaveData<AnimalSaveData>` — Real Bodies

**Files:**
- Modify: `Assets/Scripts/Character/CharacterAnimal/CharacterAnimal.cs`

- [ ] **Step 1: Replace the stub `Serialize`/`Deserialize` with real implementations**

Find the stub methods from Task 4:

```csharp
    public AnimalSaveData Serialize() => new AnimalSaveData();
    public void Deserialize(AnimalSaveData data) { }
```

Replace with:

```csharp
    public AnimalSaveData Serialize()
    {
        return new AnimalSaveData
        {
            IsTamed        = _isTamed.Value,
            OwnerProfileId = _ownerProfileId.Value.ToString()
        };
    }

    public void Deserialize(AnimalSaveData data)
    {
        if (data == null)
        {
            Debug.LogWarning($"[CharacterAnimal] Deserialize called with null data on '{_character?.CharacterName ?? gameObject.name}'.");
            return;
        }

        if (!IsServer)
        {
            // NetworkVariables are server-write only; a client-side Deserialize call is a no-op
            // (NVs will sync from server naturally). This branch is likely unreachable under
            // the current CharacterDataCoordinator flow — verify during manual testing and
            // remove if confirmed dead.
            Debug.Log($"[CharacterAnimal] Deserialize on non-server for '{_character?.CharacterName}' — skipping NV writes.");
            return;
        }

        try
        {
            _isTamed.Value = data.IsTamed;
            _ownerProfileId.Value = string.IsNullOrEmpty(data.OwnerProfileId)
                ? default
                : new FixedString64Bytes(data.OwnerProfileId);
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            Debug.LogError($"[CharacterAnimal] Failed to restore save data on '{_character?.CharacterName ?? gameObject.name}' — " +
                           $"IsTamed={data.IsTamed}, OwnerProfileId='{data.OwnerProfileId}'");
        }
    }
```

- [ ] **Step 2: Refresh assets, confirm compile**

Run `assets-refresh`. Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Character/CharacterAnimal/CharacterAnimal.cs
git commit -m "feat(animal): implement AnimalSaveData serialize/deserialize with defensive guards"
```

---

## Task 6: Implement `IInteractionProvider.GetInteractionOptions`

**Files:**
- Modify: `Assets/Scripts/Character/CharacterAnimal/CharacterAnimal.cs`

- [ ] **Step 1: Replace the stub `GetInteractionOptions` method**

Find the stub from Task 4:

```csharp
    public List<InteractionOption> GetInteractionOptions(Character interactor)
    {
        // Implemented in Task 6.
        return new List<InteractionOption>();
    }
```

Replace with:

```csharp
    public List<InteractionOption> GetInteractionOptions(Character interactor)
    {
        var options = new List<InteractionOption>();

        if (interactor == null || _character == null) return options;
        if (!IsTameable || IsTamed) return options;
        if (interactor == _character) return options;

        // Capture locals for the closure.
        Character interactorRef = interactor;

        options.Add(new InteractionOption("Tame", () =>
        {
            // Queue the action on the interactor. CharacterTameAction's OnApplyEffect
            // routes to the target's server-side tame RPC (Task 8).
            if (interactorRef.CharacterActions == null)
            {
                Debug.LogWarning($"[CharacterAnimal] '{interactorRef.CharacterName}' has no CharacterActions — cannot tame.");
                return;
            }

            interactorRef.CharacterActions.ExecuteAction(new CharacterTameAction(interactorRef, _character));
        }));

        return options;
    }
```

- [ ] **Step 2: Refresh assets, confirm compile**

Run `assets-refresh`. Expected: a compile error on `CharacterTameAction` — that's OK, Task 7 creates it next.

- [ ] **Step 3: Do NOT commit yet**

Wait until Task 7 adds `CharacterTameAction` so the repo compiles.

---

## Task 7: Create `CharacterTameAction` — Skeleton

**Files:**
- Create: `Assets/Scripts/Character/CharacterActions/CharacterTameAction.cs`

- [ ] **Step 1: Write the skeleton**

```csharp
using UnityEngine;

/// <summary>
/// Instant action: the interactor attempts to tame a target CharacterAnimal.
/// Per rule 22, the effect lives here — the UI/NPC AI only queues the action.
/// Runs server-authoritative: if executed on a non-server caller, dispatches
/// through CharacterAnimal.RequestTameServerRpc (Task 8).
/// </summary>
public class CharacterTameAction : CharacterAction
{
    private readonly Character _target;

    public override bool ShouldPlayGenericActionAnimation => false;
    public override string ActionName => "Tame";

    public CharacterTameAction(Character interactor, Character target)
        : base(interactor, duration: 0f)
    {
        _target = target;
    }

    public override bool CanExecute()
    {
        if (_target == null)
        {
            Debug.LogWarning($"[CharacterTameAction] {character?.CharacterName} — null target.");
            return false;
        }

        if (!_target.TryGet<CharacterAnimal>(out var animal))
        {
            Debug.LogWarning($"[CharacterTameAction] Target '{_target.CharacterName}' has no CharacterAnimal.");
            return false;
        }

        if (!animal.IsTameable || animal.IsTamed)
        {
            Debug.Log($"[CharacterTameAction] Target '{_target.CharacterName}' is not currently tameable.");
            return false;
        }

        if (_target == character)
        {
            Debug.LogWarning($"[CharacterTameAction] {character.CharacterName} cannot tame themselves.");
            return false;
        }

        return true;
    }

    public override void OnStart()
    {
        Debug.Log($"<color=cyan>[Tame]</color> {character.CharacterName} attempts to tame {_target.CharacterName}.");
    }

    public override void OnApplyEffect()
    {
        // Implemented in Task 8.
    }
}
```

- [ ] **Step 2: Refresh assets, confirm compile**

Run `assets-refresh`. Expected: no errors. Tasks 6 and 7 now compile together.

- [ ] **Step 3: Commit Tasks 6 + 7 together**

```bash
git add Assets/Scripts/Character/CharacterAnimal/CharacterAnimal.cs Assets/Scripts/Character/CharacterActions/CharacterTameAction.cs
git commit -m "feat(animal): expose Tame interaction option + CharacterTameAction skeleton"
```

---

## Task 8: Add `RequestTameServerRpc` + Server Roll to `CharacterAnimal`

**Files:**
- Modify: `Assets/Scripts/Character/CharacterAnimal/CharacterAnimal.cs`

- [ ] **Step 1: Add the server RPC + helpers at the bottom of `CharacterAnimal.cs`**

Insert just before the closing `}` of the class:

```csharp

    // ── Server-Authoritative Tame Flow (called from CharacterTameAction) ──

    [Rpc(SendTo.Server)]
    public void RequestTameServerRpc(NetworkObjectReference interactorRef, RpcParams rpcParams = default)
    {
        TryTameOnServer(interactorRef);
    }

    /// <summary>
    /// Server-side gate + roll. Called directly when the tame action runs on
    /// the server, or via RequestTameServerRpc when it runs on a client.
    /// </summary>
    public void TryTameOnServer(NetworkObjectReference interactorRef)
    {
        if (!IsServer)
        {
            Debug.LogError($"[CharacterAnimal] TryTameOnServer called on non-server — ignored.");
            return;
        }

        if (!interactorRef.TryGet(out NetworkObject interactorNetObj))
        {
            Debug.LogWarning($"[CharacterAnimal] Server could not resolve interactor NetworkObject — rejecting tame.");
            return;
        }

        Character interactor = interactorNetObj.GetComponent<Character>();
        if (interactor == null || _character == null)
        {
            Debug.LogWarning($"[CharacterAnimal] Missing Character on interactor or target — rejecting tame.");
            return;
        }

        // Re-validate server-side (defends against stale client state).
        if (!IsTameable)
        {
            Debug.Log($"[CharacterAnimal] '{_character.CharacterName}' is not tameable — rejecting.");
            return;
        }
        if (IsTamed)
        {
            Debug.Log($"[CharacterAnimal] '{_character.CharacterName}' is already tamed — rejecting.");
            return;
        }
        if (_character.IsPlayer())
        {
            Debug.Log($"[CharacterAnimal] '{_character.CharacterName}' is currently player-driven — tame blocked.");
            return;
        }

        float range = _character.Archetype != null ? _character.Archetype.DefaultInteractionRange : 3.5f;
        float dist = Vector3.Distance(interactor.transform.position, _character.transform.position);
        if (dist > range)
        {
            Debug.Log($"[CharacterAnimal] '{interactor.CharacterName}' too far to tame '{_character.CharacterName}' (dist={dist:F2}, range={range:F2}).");
            return;
        }

        // Roll.
        bool success = _random.Value() > _tameDifficulty.Value;

        Debug.Log($"<color=cyan>[CharacterAnimal]</color> Roll for '{_character.CharacterName}' — " +
                  $"difficulty={_tameDifficulty.Value:F2}, success={success}.");

        if (success)
        {
            _isTamed.Value = true;

            string profileId = interactor.CharacterId;
            if (string.IsNullOrEmpty(profileId))
            {
                Debug.LogWarning($"[CharacterAnimal] Interactor '{interactor.CharacterName}' has empty CharacterId — " +
                                 "OwnerProfileId will be blank until identity resolves.");
                _ownerProfileId.Value = default;
            }
            else
            {
                if (profileId.Length > 63)
                {
                    Debug.LogWarning($"[CharacterAnimal] ProfileId '{profileId}' exceeds FixedString64Bytes capacity — truncating.");
                    profileId = profileId.Substring(0, 63);
                }
                _ownerProfileId.Value = new FixedString64Bytes(profileId);
            }
        }

        // Broadcast the result to every client for the floating text (Task 9).
        ShowTameResultClientRpc(success);
    }
```

- [ ] **Step 2: Refresh assets, confirm compile**

Run `assets-refresh`. Expected: one compile error on `ShowTameResultClientRpc` — that's OK, Task 9 adds it next.

- [ ] **Step 3: Do NOT commit yet**

Wait until Task 9 to commit a compiling repo.

---

## Task 9: Broadcast Tame Result via `ClientRpc` + Floating Text

**Files:**
- Modify: `Assets/Scripts/Character/CharacterAnimal/CharacterAnimal.cs`

- [ ] **Step 1: Add the ClientRpc below `TryTameOnServer`**

Insert immediately after `TryTameOnServer`:

```csharp

    [Rpc(SendTo.Everyone)]
    private void ShowTameResultClientRpc(bool success)
    {
        var spawner = _character != null ? _character.FloatingTextSpawner : null;
        if (spawner == null) return;

        if (success)
            spawner.SpawnText("Tamed!", Color.green);
        else
            spawner.SpawnText("Failed!", new Color(1f, 0.4f, 0.4f));
    }
```

- [ ] **Step 2: Refresh assets, confirm compile**

Run `assets-refresh`. Expected: no errors.

- [ ] **Step 3: Commit Tasks 8 + 9 together**

```bash
git add Assets/Scripts/Character/CharacterAnimal/CharacterAnimal.cs
git commit -m "feat(animal): server-side tame roll + ClientRpc floating-text broadcast"
```

---

## Task 10: Wire `CharacterTameAction.OnApplyEffect` — Server Dispatch

**Files:**
- Modify: `Assets/Scripts/Character/CharacterActions/CharacterTameAction.cs`

- [ ] **Step 1: Replace the empty `OnApplyEffect` from Task 7**

Find:

```csharp
    public override void OnApplyEffect()
    {
        // Implemented in Task 8.
    }
```

Replace with:

```csharp
    public override void OnApplyEffect()
    {
        if (_target == null)
        {
            Debug.LogWarning($"[CharacterTameAction] Target vanished before effect on {character?.CharacterName}.");
            return;
        }

        if (!_target.TryGet<CharacterAnimal>(out var animal))
        {
            Debug.LogWarning($"[CharacterTameAction] Target '{_target.CharacterName}' lost its CharacterAnimal component mid-action.");
            return;
        }

        // Route to server. If we're already on the server, call directly;
        // otherwise go through the ServerRpc.
        NetworkObject interactorNetObj = character != null ? character.NetworkObject : null;
        if (interactorNetObj == null || !interactorNetObj.IsSpawned)
        {
            Debug.LogError($"[CharacterTameAction] Interactor has no spawned NetworkObject — cannot route tame.");
            return;
        }

        if (animal.IsServer)
        {
            animal.TryTameOnServer(new NetworkObjectReference(interactorNetObj));
        }
        else
        {
            animal.RequestTameServerRpc(new NetworkObjectReference(interactorNetObj));
        }
    }
```

- [ ] **Step 2: Refresh assets, confirm compile**

Run `assets-refresh`. Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Character/CharacterActions/CharacterTameAction.cs
git commit -m "feat(animal): CharacterTameAction dispatches to server via CharacterAnimal RPC"
```

---

## Task 11: Register `CharacterAnimal` on the `Character` Facade

**Files:**
- Modify: `Assets/Scripts/Character/Character.cs`

- [ ] **Step 1: Add the serialized field**

In the `#region Sub-Systems` block (around line 45–78), add after the line `[SerializeField] private FloatingTextSpawner _floatingTextSpawner;` (line 76) or any sensible spot in the serialized subsystem list:

```csharp
    [SerializeField] private CharacterAnimal _animal;
```

- [ ] **Step 2: Add the property with registry-first fallback**

In the `#region Properties` block (around lines 213–245), add after an existing similar entry (e.g., after `FloatingTextSpawner`):

```csharp
    public CharacterAnimal CharacterAnimal => TryGet<CharacterAnimal>(out var sAnimal) ? sAnimal : _animal;
```

- [ ] **Step 3: Refresh assets, confirm compile**

Run `assets-refresh`. Expected: no errors.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Character/Character.cs
git commit -m "feat(character): add CharacterAnimal to facade (serialized slot + registry-first property)"
```

---

## Task 12: Create the Example `Deer.asset` Archetype

**Files:**
- Create: `Assets/Resources/Data/CharacterArchetype/Deer.asset`

This uses Unity's asset creation — no code change. Multiple tool paths are valid; pick whichever is available.

- [ ] **Step 1: Create the asset**

Option A — manual (Unity Editor, recommended for first-time authoring):
1. In the Project window: right-click `Assets/Resources/Data/CharacterArchetype/` → `Create → MWI → Character → Character Archetype`.
2. Rename the new asset to `Deer`.

Option B — MCP driven (if doing this agentically via `assets-copy`):
1. Duplicate the existing `Assets/Resources/Data/CharacterArchetype/New Character Archetype.asset` and rename the copy to `Deer.asset`.

- [ ] **Step 2: Set the fields on `Deer.asset`**

Use `object-modify` MCP tool (or set manually in Inspector):

| Field | Value |
|-------|-------|
| `_archetypeName` | `Deer` |
| `_bodyType` | `Quadruped` |
| `_canEnterCombat` | `false` (passive animal) |
| `_canEquipItems` | `false` |
| `_canDialogue` | `false` |
| `_canCraft` | `false` |
| `_hasInventory` | `false` |
| `_hasNeeds` | `true` (hunger/thirst ok) |
| `_isTameable` | `true` |
| `_isMountable` | `false` |
| `_tameDifficulty` | `0.5` |
| `_movementModes` | `Walk | Run` |
| `_defaultSpeed` | `3.5` |
| `_runSpeed` | `6` |
| `_defaultWanderStyle` | `Nervous` |
| `_defaultInteractionRange` | `3.5` |
| `_isTargetable` | `true` |

Leave `_defaultBehaviourTree`, `_animationProfile`, `_visualPrefab` empty for now — the test scene will use any existing NPC prefab with this archetype assigned.

- [ ] **Step 3: Commit**

```bash
git add Assets/Resources/Data/CharacterArchetype/Deer.asset
git commit -m "feat(animal): add Deer example archetype (IsTameable=true, difficulty 0.5)"
```

---

## Task 13: Write `.agent/skills/character-animal/SKILL.md`

**Files:**
- Create: `.agent/skills/character-animal/SKILL.md`

- [ ] **Step 1: Write the skill doc (procedures only — architecture goes in the wiki page)**

Model the structure on an existing `.agent/skills/<system>/SKILL.md` (e.g., `save-load-system/SKILL.md`). Required sections:

```markdown
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
```

- [ ] **Step 2: Commit**

```bash
git add .agent/skills/character-animal/SKILL.md
git commit -m "docs(skills): add character-animal skill (procedures + API)"
```

---

## Task 14: Write `wiki/systems/character-animal.md`

**Files:**
- Create: `wiki/systems/character-animal.md`

- [ ] **Step 1: Check existing wiki structure**

Before writing, read [wiki/CLAUDE.md](../../wiki/CLAUDE.md) for any wiki-specific schema rules (frontmatter, link conventions). Skim one or two existing pages in `wiki/systems/` for the house style.

- [ ] **Step 2: Write the architecture page**

Content must cover (non-duplicating with SKILL.md):

- **What it is** — one-paragraph definition of the animal subsystem and its place in the Character facade.
- **Architecture** — how `CharacterAnimal` plugs into the capability registry (`Character.GetAll<IInteractionProvider>()`), why `IsTameable`/`TameDifficulty` are NetworkVariables rather than direct archetype reads, and how `CharacterTameAction` enforces rule 22.
- **Network authority** — diagram or table of the four NetworkVariables (server-write, everyone-read), the `RequestTameServerRpc` roundtrip, the `ShowTameResultClientRpc` floating-text broadcast, and server-side re-validation.
- **Save / hibernation flow** — how `AnimalSaveData` rides in the NPC hibernation bundle via `CharacterDataCoordinator`, what's saved vs re-seeded, `LoadPriority=40`.
- **Player ↔ NPC symmetry** — the matrix from spec §3: who can tame whom, the `IsPlayer()` gate on the target, owner-ID resolution for player-inhabited animals.
- **Open issues / future work** — `CharacterMountable` as sibling component, tamed state on player profile (cross-host travel), owner-follow AI, timed/item-gated taming.
- **Cross-links** — link back to [.agent/skills/character-animal/SKILL.md](../../.agent/skills/character-animal/SKILL.md) for procedures, to the spec, to `wiki/systems/character-archetype.md` (if present) for capability-registry context.

- [ ] **Step 3: Commit**

```bash
git add wiki/systems/character-animal.md
git commit -m "docs(wiki): add character-animal architecture page"
```

---

## Task 15: Update `wiki/INDEX.md`

**Files:**
- Modify: `wiki/INDEX.md`

- [ ] **Step 1: Add a one-line entry for the new page**

Locate the "Systems" section in `wiki/INDEX.md` (or whatever the project's convention is — read the file first). Add an entry in alphabetical order:

```markdown
- [Character Animal](systems/character-animal.md) — Animal/taming subsystem; capability-registry, save persistence, network authority.
```

If the wiki has a `/map` slash command that regenerates this file automatically, consider running it instead — check `wiki/CLAUDE.md` for the project's preferred flow.

- [ ] **Step 2: Commit**

```bash
git add wiki/INDEX.md
git commit -m "docs(wiki): index character-animal systems page"
```

---

## Task 16: Manual Smoke Test in Unity Editor

**Files:** none — editor testing only.

> **No automated tests.** The project does not currently have an NUnit/test asmdef. The `IRandomProvider` seam (Task 3) exists so future unit tests can be added cheaply; for now, use the manual checks below.

Each of these checks must pass before the plan is considered complete.

- [ ] **Check 1: Compiles clean**

Run `assets-refresh` and `console-get-logs`. Expected: zero compile errors, zero warnings from the new files.

- [ ] **Check 2: Archetype field visible**

Open `Assets/Resources/Data/CharacterArchetype/Deer.asset` in the Inspector. Confirm `_tameDifficulty` appears with a 0..1 slider and defaults to 0.5 on newly-created archetypes.

- [ ] **Check 3: "Tame" option appears only when eligible**

- Create/load a test scene with a player prefab and an NPC character prefab.
- On the NPC: add a `CharacterAnimal` child GO with the `CharacterAnimal` component, assign its archetype to `Deer.asset`.
- Enter Play mode. Walk the player into interaction range of the NPC.
- Expected: "Tame" option shows. Click it — verify floating text appears above the target ("Tamed!" or "Failed!") and one of:
  - Success → `IsTamed=true`, `OwnerProfileId=<player's CharacterId>` in the Inspector NV view.
  - Failure → state unchanged; re-attempt shows "Tame" option again.
- Spawn another humanoid NPC with `_isTameable=false`. Verify: no "Tame" option.
- Spawn a tamed deer (use `TameDifficulty=0` to guarantee success). Verify: "Tame" option disappears after success.

- [ ] **Check 4: Host ↔ Client parity**

Start a Host instance and a Client instance. Spawn a shared Deer.
- Host tames it. Confirm Client sees `IsTamed=true` and the floating text.
- On a fresh deer, Client tames it. Confirm Host sees the update.

- [ ] **Check 5: Blocked — target is player-controlled**

Have Player A's character use the Deer archetype and swap Player A into inhabiting it (`SwitchToPlayer`). Have Player B approach.
- Expected: Tame attempt on the Player-A-driven Deer is silently rejected server-side (console log), no floating text, no state change.
- Player A swaps out (back to NPCController). Player B retries.
- Expected: tame proceeds normally.

- [ ] **Check 6: Hibernation round-trip**

Tame a Deer on a map. Trigger hibernation (leave the map so player count hits 0) and re-enter. On respawn:
- `IsTamed` remains `true`.
- `OwnerProfileId` matches the original tamer's profile.
- `IsTameable`/`TameDifficulty` are re-seeded from the Deer archetype (unchanged).

- [ ] **Check 7: Exception safety**

Corrupt the Deer's `AnimalSaveData` JSON in the save file (e.g., invalid `OwnerProfileId` field). Attempt map wake-up.
- Expected: a `Debug.LogException` / `LogError` fires from `CharacterAnimal.Deserialize`; the Deer still respawns (possibly with default state); map wake-up completes without aborting.

- [ ] **Check 8: Commit the "testing done" marker**

No code changes — just tag the plan as verified:

```bash
git commit --allow-empty -m "chore(animal): manual smoke tests pass (see plan Task 16)"
```

---

## Verification Before Claiming Complete

Before merging/marking done, re-run the spec's Exit Criteria (spec §Exit Criteria):

- [ ] All files in the manifest exist and compile cleanly.
- [ ] `CharacterArchetype.TameDifficulty` editable in Inspector.
- [ ] `Deer.asset` exists and is demonstrable.
- [ ] All 7 smoke-test checks above pass.
- [ ] `.agent/skills/character-animal/SKILL.md` and `wiki/systems/character-animal.md` exist and are cross-linked.
- [ ] `wiki/INDEX.md` includes the new page.
- [ ] No new Unity Console warnings/errors during a full play session.

Then and only then, claim complete.

---

## Appendix: Plan-Time Verifications Carried Over from the Spec

These are flagged in the spec as plan-time checks — confirm during execution:

- **Profile ID accessor.** ✅ Resolved: use `_character.CharacterId` (from `NetworkCharacterId`, [Character.cs:263](../../../Assets/Scripts/Character/Character.cs#L263)).
- **ID-space collision between player and NPC profile IDs.** ⚠️ Both use `NetworkCharacterId` (a GUID/string). Collisions between profile spaces are extremely unlikely with GUIDs. If a future system binds owner-ID to a lookup that MUST distinguish player-origin vs NPC-origin, introduce a `"p:"` / `"n:"` prefix then (not now).
- **`FixedString64Bytes` length fit.** Check: standard GUIDs (36 chars) fit comfortably. Code in Task 8 truncates + warns if exceeded, so no silent data loss.
- **Client-side `Deserialize` branch.** Flagged as possibly dead code. Task 5 keeps the no-op branch with a log; remove during a follow-up refactor if confirmed unreachable.
- **`CharacterDataCoordinator.LoadPriority` for Animal.** ✅ Chosen: `40` (same tier as Needs/Traits), fits between identity/stats (0/10) and relationship/job (60+).
