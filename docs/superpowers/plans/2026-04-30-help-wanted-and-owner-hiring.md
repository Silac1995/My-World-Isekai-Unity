# Help Wanted + Owner-Controlled Hiring Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the **Help Wanted Sign + Owner-Controlled Hiring** primitives — a generic `DisplayTextFurniture` (signboard / placard / notice-board) and an explicit `IsHiring` toggle on `CommercialBuilding` with a clean Owner-only API. The player's path into a job goes: walk to the farm's Help Wanted sign → read the open positions → click Apply → existing `InteractionAskForJob` runs → become an employee. Hiring state gates `AskForJob` admissions for both players and NPCs (`NeedJob` AI). The sign text auto-updates on hiring open/close + on hire-or-quit churn. Player owners control their own buildings via the same API NPC owners will use in Phase 2.

**Architecture:** Two coupled-but-independent primitives. (1) `DisplayTextFurniture : Furniture` (MonoBehaviour) + `DisplayTextFurnitureNetSync : NetworkBehaviour` sibling for the `_displayText: NetworkVariable<FixedString512Bytes>` — mirrors the existing `StorageFurniture` + `StorageFurnitureNetworkSync` pattern. (2) `CommercialBuilding._isHiring: NetworkVariable<bool>` + designer-set `_helpWantedFurniture` reference. The hiring API (`TryOpenHiring`, `TryCloseHiring`, `CanRequesterControlHiring`, `GetVacantJobs`, `GetHelpWantedDisplayText`, `GetClosedHiringDisplayText`) is server-authoritative; client-initiated calls route through ServerRpcs. Sign auto-update fires from `_isHiring.OnValueChanged` server-side and from a vacancy-changed event the existing hire/quit pipeline raises. Player UI: `UI_DisplayTextReader` (read any sign + Apply button when applicable) + `UI_OwnerHiringPanel` (owner-only open/close toggle + custom text editor).

**Tech Stack:** Unity 6, NGO (Netcode for GameObjects), C#, NUnit for EditMode tests, NetworkVariable for replication.

**Source spec:** [docs/superpowers/specs/2026-04-29-farmer-job-and-tool-storage-design.md §15](../specs/2026-04-29-farmer-job-and-tool-storage-design.md). Builds on Plan 1 (Tool Storage primitive) shipped 2026-04-29.

**Phase scope:** Plan 2 of 3 (Plan 1 = Tool Storage; Plan 2 = this; Plan 3 = Farmer integration). After this plan ships, an existing `HarvestingBuilding` instance can be wired with a Help Wanted sign for smoke testing — no Farmer dependency.

**Critical invariant (per user direction during brainstorming):** quests are still produced **exclusively** by `BuildingTaskManager`. The Help Wanted sign and owner-hiring controls are pure **discovery + access-control** layers — they surface existing quests and gate hiring. Nothing in this plan generates quests directly.

---

## Files affected

**Created:**
- `Assets/Scripts/World/Furniture/DisplayTextFurniture.cs`
- `Assets/Scripts/World/Furniture/DisplayTextFurnitureNetSync.cs`
- `Assets/Scripts/UI/PlayerHUD/UI_DisplayTextReader.cs` + UI prefab
- `Assets/Scripts/UI/PlayerHUD/UI_OwnerHiringPanel.cs` + UI prefab
- `.agent/skills/help-wanted-and-hiring/SKILL.md`
- `wiki/systems/help-wanted-and-hiring.md`
- `docs/superpowers/smoketests/2026-04-30-help-wanted-and-hiring-smoketest.md`

**Modified:**
- `Assets/Scripts/World/Buildings/CommercialBuilding.cs` — add `_isHiring` NetworkVariable + `_helpWantedFurniture` reference + hiring API + `HandleHiringStateChanged` + `HandleVacancyChanged` + ServerRpc routing.
- `Assets/Scripts/Character/CharacterInteraction/InteractionAskForJob.cs` — gate `CanExecute` on `_building.IsHiring`.
- `Assets/Scripts/World/Buildings/BuildingManager.cs` — filter `FindAvailableJob` candidates on `IsHiring` to keep NPC `NeedJob` from queueing applications at closed buildings.
- `wiki/systems/commercial-building.md` — change-log entry.
- `wiki/systems/character-interaction.md` (if exists) — change-log entry.

---

## Task 1: DisplayTextFurniture base + NetSync sibling

**Files:**
- Create: `Assets/Scripts/World/Furniture/DisplayTextFurniture.cs`
- Create: `Assets/Scripts/World/Furniture/DisplayTextFurnitureNetSync.cs`

Mirrors the existing `StorageFurniture` + `StorageFurnitureNetworkSync` pattern: the gameplay-data class extends `Furniture` (MonoBehaviour), and a sibling NetworkBehaviour on the same GameObject owns the `NetworkVariable`. Both components live on the same prefab; the prefab inherits the existing `Furniture_prefab` NetworkObject.

- [ ] **Step 1: Read the existing pattern**

Read `Assets/Scripts/World/Furniture/StorageFurniture.cs` (the gameplay-data MonoBehaviour) and `Assets/Scripts/World/Furniture/StorageFurnitureNetworkSync.cs` (the sibling NetworkBehaviour). Note especially:
- How `StorageFurnitureNetworkSync` resolves its sibling `StorageFurniture` (typically `GetComponent<StorageFurniture>()` in `Awake`).
- How `OnInventoryChanged` events bridge from gameplay to network.
- The `OnNetworkSpawn` path that pushes initial state to late joiners.

Mirror this pattern for `DisplayTextFurniture` + `DisplayTextFurnitureNetSync`.

- [ ] **Step 2: Create DisplayTextFurniture.cs**

```csharp
using UnityEngine;

/// <summary>
/// Generic placard / signboard / notice-board furniture. Holds server-authoritative text
/// (replicated via the sibling <see cref="DisplayTextFurnitureNetSync"/> component). Any
/// player or NPC can interact with it → reads the text. Owner of the parent
/// <see cref="CommercialBuilding"/> can edit the text via
/// <see cref="DisplayTextFurnitureNetSync.TrySetDisplayText"/>.
///
/// Authoring: drop a <c>DisplayTextFurniture_Placard.prefab</c> instance in a building.
/// Set <see cref="_initialText"/> for static signs (welcome messages, lore plates, room
/// labels). For Help Wanted signs, also reference this furniture as
/// <c>CommercialBuilding._helpWantedFurniture</c> — the building will auto-write
/// vacancy text whenever hiring opens/closes (Plan 2 Task 4).
/// </summary>
public class DisplayTextFurniture : Furniture
{
    [Header("Display Text")]
    [Tooltip("Authoring-time default text. Used as the initial DisplayText if no value is set at runtime.")]
    [TextArea(2, 8)]
    [SerializeField] private string _initialText = "";

    private DisplayTextFurnitureNetSync _netSync;

    public string InitialText => _initialText;
    public DisplayTextFurnitureNetSync NetSync => _netSync;

    /// <summary>Current displayed text. Server-authoritative; replicates via NetSync.</summary>
    public string DisplayText => _netSync != null ? _netSync.DisplayText : _initialText;

    /// <summary>Fires whenever <see cref="DisplayText"/> changes (server + clients).</summary>
    public event System.Action<string> OnDisplayTextChanged;

    protected virtual void Awake()
    {
        _netSync = GetComponent<DisplayTextFurnitureNetSync>();
        if (_netSync != null)
        {
            _netSync.OnDisplayTextChanged += HandleNetSyncTextChanged;
        }
        else
        {
            Debug.LogError($"[DisplayTextFurniture] {name} has no sibling DisplayTextFurnitureNetSync — text will not replicate.");
        }
    }

    protected virtual void OnDestroy()
    {
        if (_netSync != null)
            _netSync.OnDisplayTextChanged -= HandleNetSyncTextChanged;
    }

    private void HandleNetSyncTextChanged(string newText) => OnDisplayTextChanged?.Invoke(newText);

    /// <summary>Owner-gated text mutation. Routes via NetSync ServerRpc when called from a client.</summary>
    public bool TrySetDisplayText(Character requester, string newText)
    {
        if (_netSync == null) return false;
        return _netSync.TrySetDisplayText(requester, newText);
    }
}
```

- [ ] **Step 3: Create DisplayTextFurnitureNetSync.cs**

```csharp
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative replication for <see cref="DisplayTextFurniture"/>.
///
/// Mirrors the pattern in <see cref="StorageFurnitureNetworkSync"/>: the Furniture base is
/// a plain MonoBehaviour, so the NetworkVariable lives on this sibling NetworkBehaviour.
/// Both share the same GameObject + NetworkObject from the Furniture_prefab base.
///
/// Authority: server-only writer (<c>NetworkVariableWritePermission.Server</c>); everyone
/// reads. Client mutation requests route through <see cref="TrySetDisplayText"/> which
/// runs an owner-authority check and dispatches a ServerRpc.
///
/// Late joiners: NetworkVariable auto-syncs current value during spawn handshake. No
/// extra catch-up RPC needed.
/// </summary>
[RequireComponent(typeof(DisplayTextFurniture))]
public class DisplayTextFurnitureNetSync : NetworkBehaviour
{
    private DisplayTextFurniture _furniture;

    private NetworkVariable<FixedString512Bytes> _displayText = new(
        new FixedString512Bytes(),
        readPerm: NetworkVariableReadPermission.Everyone,
        writePerm: NetworkVariableWritePermission.Server);

    public string DisplayText => _displayText.Value.ToString();
    public event System.Action<string> OnDisplayTextChanged;

    private void Awake()
    {
        _furniture = GetComponent<DisplayTextFurniture>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Server: seed from authoring _initialText if empty.
        if (IsServer && string.IsNullOrEmpty(_displayText.Value.ToString()))
        {
            string seed = _furniture != null ? _furniture.InitialText : "";
            if (!string.IsNullOrEmpty(seed))
                _displayText.Value = SanitiseAndClamp(seed);
        }

        _displayText.OnValueChanged += HandleNetVarChanged;
        OnDisplayTextChanged?.Invoke(_displayText.Value.ToString());
    }

    public override void OnNetworkDespawn()
    {
        _displayText.OnValueChanged -= HandleNetVarChanged;
        base.OnNetworkDespawn();
    }

    private void HandleNetVarChanged(FixedString512Bytes _, FixedString512Bytes newVal)
    {
        OnDisplayTextChanged?.Invoke(newVal.ToString());
    }

    /// <summary>
    /// Owner-gated text mutation. Validates the requester has authority over the parent
    /// CommercialBuilding (via CanRequesterControlHiring — added in Task 3). Returns true
    /// if the mutation succeeded. Routes via ServerRpc when called from a client.
    /// </summary>
    public bool TrySetDisplayText(Character requester, string newText)
    {
        var building = GetComponentInParent<CommercialBuilding>();
        if (building == null)
        {
            Debug.LogWarning($"[DisplayTextFurniture] {name} not parented under a CommercialBuilding; mutations rejected.");
            return false;
        }
        if (requester != null && !building.CanRequesterControlHiring(requester))
            return false;

        if (IsServer)
        {
            _displayText.Value = SanitiseAndClamp(newText);
            return true;
        }

        // Client path — route to server.
        TrySetDisplayTextServerRpc(SanitiseAndClamp(newText), requester != null ? requester.NetworkObjectId : 0);
        return true; // optimistic; actual write is server-side
    }

    [Rpc(SendTo.Server)]
    private void TrySetDisplayTextServerRpc(FixedString512Bytes newText, ulong requesterNetId, RpcParams rpcParams = default)
    {
        var building = GetComponentInParent<CommercialBuilding>();
        if (building == null) return;

        Character requester = null;
        if (requesterNetId != 0
            && NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(requesterNetId, out var netObj))
        {
            requester = netObj.GetComponent<Character>();
        }

        if (requester != null && !building.CanRequesterControlHiring(requester))
            return;

        _displayText.Value = newText;
    }

    /// <summary>Unrestricted server-only setter — used by parent CommercialBuilding to
    /// auto-update Help Wanted text. NOT callable from client RPCs.</summary>
    internal void ServerSetDisplayText(string newText)
    {
        if (!IsServer)
        {
            Debug.LogError("[DisplayTextFurniture] ServerSetDisplayText called from client — ignored.");
            return;
        }
        _displayText.Value = SanitiseAndClamp(newText);
    }

    private static FixedString512Bytes SanitiseAndClamp(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return new FixedString512Bytes();

        // Strip control chars (including newline-only carriage returns; allow \n).
        var sb = new System.Text.StringBuilder(raw.Length);
        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];
            if (c == '\n' || c == '\t') { sb.Append(c); continue; }
            if (char.IsControl(c)) continue;
            sb.Append(c);
        }
        string clean = sb.ToString();

        // FixedString512Bytes holds up to 511 UTF-8 bytes + null terminator; clamp aggressively.
        const int maxBytes = 480;
        if (System.Text.Encoding.UTF8.GetByteCount(clean) > maxBytes)
        {
            // Truncate by char count first; refine if needed.
            int maxChars = maxBytes; // worst case 1 char = 1 byte; loop refines.
            while (System.Text.Encoding.UTF8.GetByteCount(clean.Substring(0, System.Math.Min(maxChars, clean.Length))) > maxBytes)
                maxChars--;
            clean = clean.Substring(0, maxChars);
        }

        return new FixedString512Bytes(clean);
    }
}
```

- [ ] **Step 4: Compile + commit**

Build via Unity. Verify zero new compile errors. (Use `mcp__ai-game-developer__console-get-logs` if needed.)

```bash
git add Assets/Scripts/World/Furniture/DisplayTextFurniture.cs Assets/Scripts/World/Furniture/DisplayTextFurnitureNetSync.cs
git commit -m "feat(furniture): DisplayTextFurniture + NetSync — generic signboard primitive

Two-component shape mirroring StorageFurniture pattern: gameplay-data class
extends Furniture (MonoBehaviour), sibling NetworkBehaviour holds the
NetworkVariable<FixedString512Bytes> for the display text. Server-only
writes; everyone reads. Owner-gated TrySetDisplayText routes via ServerRpc
when called from a client; ServerSetDisplayText is the unrestricted
server-side path used by CommercialBuilding's hiring auto-update (Task 4).

Sanitisation strips control chars (preserves \\n / \\t) and clamps to
~480 UTF-8 bytes to leave headroom inside the 512-byte FixedString.

Part of: help-wanted-and-hiring plan, Task 1/10.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: CommercialBuilding hiring state

**Files:**
- Modify: `Assets/Scripts/World/Buildings/CommercialBuilding.cs`

Adds the serialised `_helpWantedFurniture` reference, the `_isHiring` NetworkVariable + `_initialHiringOpen` authoring flag, the `IsHiring` accessor, and the `OnHiringStateChanged` event. Hooks the NetworkVariable's `OnValueChanged` callback to `HandleHiringStateChanged` (which Task 4 wires to refresh the sign).

- [ ] **Step 1: Add the serialised fields**

In the field declaration section (place near the existing `[Header("Tool Storage")]` block from Plan 1 — they're conceptually related):

```csharp
    [Header("Hiring")]
    [Tooltip("Designer reference to a DisplayTextFurniture inside this building. When set, opening hiring auto-writes formatted vacancy text; closing hiring reverts to the closed-state text. Null = no auto-managed sign (hiring still works, just no in-world sign).")]
    [SerializeField] private DisplayTextFurniture _helpWantedFurniture;

    [Tooltip("Initial hiring state at scene start / building creation. true = open by default (existing buildings remain backward compatible — they all auto-load as 'currently hiring'). false = start closed; owner must open hiring before applications are accepted.")]
    [SerializeField] private bool _initialHiringOpen = true;

    private NetworkVariable<bool> _isHiring = new(
        true,
        readPerm: NetworkVariableReadPermission.Everyone,
        writePerm: NetworkVariableWritePermission.Server);

    public bool IsHiring => _isHiring.Value;
    public DisplayTextFurniture HelpWantedSign => _helpWantedFurniture;
    public event System.Action<bool> OnHiringStateChanged;
```

- [ ] **Step 2: Hook OnNetworkSpawn**

Find the existing `OnNetworkSpawn` override on `CommercialBuilding`. Append:

```csharp
        // Hiring state.
        if (IsServer)
        {
            // Authoring default applies on first spawn / fresh save. Existing replicated values
            // (e.g. on a late-joining client) are NOT overwritten.
            // We can't easily detect "first spawn" without an additional flag, so the simplest
            // semantically-safe approach: only seed when the current value still matches the
            // _isHiring.Value default and _initialHiringOpen disagrees with it. Otherwise leave alone.
            // Practical effect: the first spawn writes _initialHiringOpen; subsequent spawns are no-ops.
            if (_isHiring.Value != _initialHiringOpen)
                _isHiring.Value = _initialHiringOpen;
        }

        _isHiring.OnValueChanged += HandleIsHiringChanged;

        // Initial sign refresh on the server so authoring's _initialHiringOpen is reflected
        // in the sign text from the very first frame.
        if (IsServer && _helpWantedFurniture != null)
        {
            HandleHiringStateChanged(_isHiring.Value);
        }
```

- [ ] **Step 3: Hook OnNetworkDespawn**

In the existing `OnNetworkDespawn`, append:

```csharp
        _isHiring.OnValueChanged -= HandleIsHiringChanged;
```

- [ ] **Step 4: Add the event-bridge handler**

Place near the field block:

```csharp
    private void HandleIsHiringChanged(bool oldVal, bool newVal)
    {
        OnHiringStateChanged?.Invoke(newVal);
        if (IsServer)
            HandleHiringStateChanged(newVal);
    }
```

`HandleHiringStateChanged(bool)` is the auto-update method added in Task 4. For Task 2, just declare a stub:

```csharp
    /// <summary>
    /// Server-side: refresh the Help Wanted sign text when hiring state flips. Called by
    /// HandleIsHiringChanged. Implementation lands in Task 4; for now this stub does nothing.
    /// </summary>
    private void HandleHiringStateChanged(bool isHiring)
    {
        // Implemented in Task 4.
    }
```

- [ ] **Step 5: Compile + commit**

```bash
git add Assets/Scripts/World/Buildings/CommercialBuilding.cs
git commit -m "feat(building): _isHiring NetworkVariable + _helpWantedFurniture reference

Designer-set DisplayTextFurniture reference (null = no auto-managed sign).
_initialHiringOpen authoring flag (default true — existing buildings stay
backward compatible). _isHiring NetworkVariable replicates server-only
writes to all peers. OnHiringStateChanged event fires on flip.

HandleHiringStateChanged stub added; Task 4 will wire the sign auto-update.

Part of: help-wanted-and-hiring plan, Task 2/10.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: Owner-controlled hiring API

**Files:**
- Modify: `Assets/Scripts/World/Buildings/CommercialBuilding.cs`

Implement the public API surface from spec §15.3. Server-authoritative; client wrappers route via ServerRpcs. All gameplay paths (player UI, future NPC AI, dev tools, save/load) call the same methods — no validation duplication.

- [ ] **Step 1: Add CanRequesterControlHiring**

```csharp
    /// <summary>
    /// Server-authoritative: returns true if <paramref name="requester"/> has authority to
    /// toggle this building's hiring state. Currently: requester is the building's Owner OR
    /// the community leader of the building's community. Returns true when called server-side
    /// with requester == null (internal calls only — DO NOT pass null from client paths).
    /// </summary>
    public bool CanRequesterControlHiring(Character requester)
    {
        if (requester == null) return true; // internal server call
        if (HasOwner && Owner == requester) return true;
        // TODO Plan 3 / future: community-leader check via CommunityTracker.
        return false;
    }
```

- [ ] **Step 2: Add GetVacantJobs**

```csharp
    /// <summary>
    /// Returns the list of jobs in <see cref="_jobs"/> whose Worker is unassigned. Reused by
    /// the Help Wanted sign formatter (Task 4) and by the player's job-application UI (Task 7).
    /// Allocates a fresh List per call — not hot path (called only on UI refresh / sign update).
    /// </summary>
    public IReadOnlyList<Job> GetVacantJobs()
    {
        var vacant = new List<Job>(_jobs.Count);
        for (int i = 0; i < _jobs.Count; i++)
        {
            var job = _jobs[i];
            if (job != null && !job.IsAssigned) vacant.Add(job);
        }
        return vacant;
    }
```

- [ ] **Step 3: Add TryOpenHiring + TryCloseHiring + a ServerRpc wrapper**

```csharp
    /// <summary>
    /// Server-authoritative: open hiring. Validates Owner authority + at least one vacant
    /// position. On success: flips _isHiring, fires OnHiringStateChanged, auto-refreshes
    /// _helpWantedFurniture text. Returns true if the mutation succeeded.
    /// Client callers route via TryOpenHiringServerRpc.
    /// </summary>
    public bool TryOpenHiring(Character requester)
    {
        if (!IsServer)
        {
            ulong reqId = requester != null ? requester.NetworkObjectId : 0;
            TryOpenHiringServerRpc(reqId);
            return true; // optimistic
        }
        return ServerTryOpenHiring(requester);
    }

    private bool ServerTryOpenHiring(Character requester)
    {
        if (!CanRequesterControlHiring(requester)) return false;
        if (GetVacantJobs().Count == 0) return false;
        if (_isHiring.Value) return true; // already open
        _isHiring.Value = true; // triggers HandleIsHiringChanged → HandleHiringStateChanged
        return true;
    }

    [Rpc(SendTo.Server)]
    private void TryOpenHiringServerRpc(ulong requesterNetId, RpcParams rpcParams = default)
    {
        Character requester = ResolveCharacterByNetId(requesterNetId);
        ServerTryOpenHiring(requester);
    }

    /// <summary>
    /// Server-authoritative: close hiring. Existing employees are NOT fired — only future
    /// applications are blocked. Auto-resets sign text to GetClosedHiringDisplayText.
    /// </summary>
    public bool TryCloseHiring(Character requester)
    {
        if (!IsServer)
        {
            ulong reqId = requester != null ? requester.NetworkObjectId : 0;
            TryCloseHiringServerRpc(reqId);
            return true;
        }
        return ServerTryCloseHiring(requester);
    }

    private bool ServerTryCloseHiring(Character requester)
    {
        if (!CanRequesterControlHiring(requester)) return false;
        if (!_isHiring.Value) return true; // already closed
        _isHiring.Value = false;
        return true;
    }

    [Rpc(SendTo.Server)]
    private void TryCloseHiringServerRpc(ulong requesterNetId, RpcParams rpcParams = default)
    {
        Character requester = ResolveCharacterByNetId(requesterNetId);
        ServerTryCloseHiring(requester);
    }

    private static Character ResolveCharacterByNetId(ulong netId)
    {
        if (netId == 0) return null;
        if (NetworkManager.Singleton == null) return null;
        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(netId, out var netObj)) return null;
        return netObj != null ? netObj.GetComponent<Character>() : null;
    }
```

- [ ] **Step 4: Add formatted-text builders**

Place these as `protected virtual` so subclasses (FarmingBuilding in Plan 3, ShopBuilding, etc.) can override flavor text:

```csharp
    /// <summary>
    /// Format the Help Wanted sign text from current vacant positions + building name.
    /// Override per CommercialBuilding subclass to customise wording.
    /// Default:
    ///     "Hiring at {buildingName}:
    ///      • 2 Farmer positions
    ///      • 1 Logistics Manager
    ///     Approach the owner to apply."
    /// </summary>
    protected virtual string GetHelpWantedDisplayText()
    {
        var vacant = GetVacantJobs();
        if (vacant.Count == 0) return GetClosedHiringDisplayText();

        // Group by JobTitle and count.
        var byTitle = new Dictionary<string, int>(vacant.Count);
        for (int i = 0; i < vacant.Count; i++)
        {
            string title = vacant[i].JobTitle;
            if (string.IsNullOrEmpty(title)) title = "Worker";
            byTitle.TryGetValue(title, out int n);
            byTitle[title] = n + 1;
        }

        var sb = new System.Text.StringBuilder(128);
        sb.Append("Hiring at ").Append(BuildingName).Append(":\n");
        foreach (var pair in byTitle)
        {
            sb.Append("• ").Append(pair.Value).Append(' ').Append(pair.Key);
            if (pair.Value > 1) sb.Append(" positions");
            sb.Append('\n');
        }
        sb.Append("Approach the owner to apply.");
        return sb.ToString();
    }

    /// <summary>Text written to the sign when hiring closes. Default: empty (sign goes blank).
    /// Override for flavor (e.g. "Currently fully staffed — check back another day.").</summary>
    protected virtual string GetClosedHiringDisplayText() => "";
```

- [ ] **Step 5: Compile + commit**

```bash
git add Assets/Scripts/World/Buildings/CommercialBuilding.cs
git commit -m "feat(building): hiring API — TryOpen/TryClose + GetVacantJobs + display text

Public API surface for owner-controlled hiring (Plan 2 §15.3 of source
spec). Server-authoritative; client callers route via ServerRpcs. The
same methods serve every gameplay path: player UI, future NPC owner
GOAP (Phase 2), dev tools, save/load reconciliation.

CanRequesterControlHiring centralises the owner-check (currently
Owner == requester; community-leader check is a TODO for Plan 3).

GetHelpWantedDisplayText is protected virtual — subclasses (FarmingBuilding
in Plan 3, ShopBuilding, etc.) can override flavor text. Default is a
generic "Hiring at {Name}: • N {JobTitle}…" multi-line format.

Part of: help-wanted-and-hiring plan, Task 3/10.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 4: Sign auto-update on hiring + vacancy changes

**Files:**
- Modify: `Assets/Scripts/World/Buildings/CommercialBuilding.cs`

Wire the Task 2 stub `HandleHiringStateChanged(bool)` to the real auto-update logic. Also hook a vacancy-changed event so the sign refreshes when a worker is hired or quits while hiring is open (otherwise the sign's "• 2 Farmer positions" would lie until the next toggle).

- [ ] **Step 1: Replace the stub**

```csharp
    /// <summary>
    /// Server-side: refresh the Help Wanted sign text whenever hiring state flips. Called
    /// from HandleIsHiringChanged. No-op if no _helpWantedFurniture is referenced.
    /// </summary>
    private void HandleHiringStateChanged(bool isHiring)
    {
        if (_helpWantedFurniture == null) return;
        if (_helpWantedFurniture.NetSync == null) return;
        string text = isHiring ? GetHelpWantedDisplayText() : GetClosedHiringDisplayText();
        _helpWantedFurniture.NetSync.ServerSetDisplayText(text);
    }

    /// <summary>
    /// Server-side: refresh the Help Wanted sign text when a vacancy changes (worker hired
    /// or quit) while hiring is open. Called by Task 5's hire/quit hooks. No-op when hiring
    /// is closed (the sign already shows the closed-state text).
    /// </summary>
    private void HandleVacancyChanged()
    {
        if (!IsServer) return;
        if (!_isHiring.Value) return;
        if (_helpWantedFurniture == null || _helpWantedFurniture.NetSync == null) return;
        _helpWantedFurniture.NetSync.ServerSetDisplayText(GetHelpWantedDisplayText());
    }
```

- [ ] **Step 2: Wire HandleVacancyChanged into hire / quit paths**

Find the existing methods on `CommercialBuilding` that fire when a `Job.IsAssigned` flips — likely `AskForJob` (on success) and the path that runs when `CharacterJob.QuitJob` clears a job's worker.

`AskForJob` (existing — search for `public bool AskForJob`): at the very end of the success branch, after the worker is assigned, add `HandleVacancyChanged();`.

For the quit-side: the canonical hook is the `Job.OnWorkerUnassigned` event (or equivalent — verify by reading `Job.cs`). If no event exists, the simplest hook is: in `CharacterJob.QuitJob` (or wherever the per-job worker reference is cleared), call `assignment.Workplace.NotifyVacancyChanged()`. Add a public method:

```csharp
    /// <summary>Called by CharacterJob.QuitJob when a worker leaves. Refreshes the sign if
    /// hiring is open. Safe to call from any peer; HandleVacancyChanged early-returns on client.</summary>
    public void NotifyVacancyChanged()
    {
        HandleVacancyChanged();
    }
```

In `Assets/Scripts/Character/CharacterJob/CharacterJob.cs`, find the existing `QuitJob(Job)` body and add a call to `assignment.Workplace.NotifyVacancyChanged()` after the worker is removed from the job (after the existing assignment removal logic).

**Verify**: if `Job.cs` has an existing `OnAssignedChanged` event or similar, prefer wiring through that — it's cleaner than adding hooks to two separate call sites. Read `Assets/Scripts/World/Jobs/Job.cs` first to decide.

- [ ] **Step 3: Compile + commit**

```bash
git add Assets/Scripts/World/Buildings/CommercialBuilding.cs Assets/Scripts/Character/CharacterJob/CharacterJob.cs
git commit -m "feat(building): sign auto-refresh on hiring state + vacancy churn

HandleHiringStateChanged (server-only) writes formatted text to the
Help Wanted sign whenever _isHiring flips. HandleVacancyChanged (server-
only) refreshes the sign when a worker is hired or quits while hiring
is open — without it, the sign's job count would lie until the next
toggle.

Vacancy hook fires from AskForJob success branch and from
CharacterJob.QuitJob via the public NotifyVacancyChanged shim.

Part of: help-wanted-and-hiring plan, Task 4/10.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 5: Application gate (InteractionAskForJob + BuildingManager + NeedJob)

**Files:**
- Modify: `Assets/Scripts/Character/CharacterInteraction/InteractionAskForJob.cs`
- Modify: `Assets/Scripts/World/Buildings/BuildingManager.cs`

Block applications when `IsHiring == false`. The two consumers that need updating:

**Player path:** `InteractionAskForJob.CanExecute` is the gate that allows / rejects the interaction (the player's hold-E "Apply for {JobTitle}" entry). Add an `IsHiring` check.

**NPC path:** `NeedJob.GetGoapActions` calls `BuildingManager.Instance.FindAvailableJob<Job>(true)` to find a candidate. Filter the candidates inside `BuildingManager.FindAvailableJob` so closed buildings are skipped — keeps NPCs from queueing rejected applications.

- [ ] **Step 1: Update InteractionAskForJob.CanExecute**

```csharp
    public override bool CanExecute(Character source, Character target)
    {
        if (source.CharacterJob != null && source.CharacterJob.HasJob)
            return false;

        if (_building == null || _job == null) return false;
        if (!_building.HasOwner) return false;
        if (!_building.IsHiring) return false;        // NEW: closed buildings reject applications.
        if (_job.IsAssigned) return false;
        return true;
    }
```

(The existing logic packed several conditions into one return statement; spreading them out improves readability and makes the new check obvious.)

- [ ] **Step 2: Update BuildingManager.FindAvailableJob**

```csharp
    public (CommercialBuilding building, T job) FindAvailableJob<T>(bool requireBoss = false) where T : Job
    {
        int count = allBuildings.Count;
        if (count == 0) return (null, null);

        int start = UnityEngine.Random.Range(0, count);
        for (int offset = 0; offset < count; offset++)
        {
            var building = allBuildings[(start + offset) % count];
            if (building is CommercialBuilding commercial)
            {
                if (requireBoss && !commercial.HasOwner) continue;
                if (!commercial.IsHiring) continue;       // NEW: skip closed buildings.

                T availableJob = commercial.FindAvailableJob<T>();
                if (availableJob != null)
                {
                    return (commercial, availableJob);
                }
            }
        }
        return (null, null);
    }
```

- [ ] **Step 3: Compile + commit**

```bash
git add Assets/Scripts/Character/CharacterInteraction/InteractionAskForJob.cs Assets/Scripts/World/Buildings/BuildingManager.cs
git commit -m "feat(hiring): gate AskForJob + FindAvailableJob on IsHiring

Player path (InteractionAskForJob.CanExecute) and NPC path (NeedJob via
BuildingManager.FindAvailableJob) both respect _building.IsHiring.
Closed buildings reject applications cleanly — players see no Apply
button on the Help Wanted sign UI (Task 7); NPCs replan with no thrash.

Part of: help-wanted-and-hiring plan, Task 5/10.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 6: UI_DisplayTextReader

**Files:**
- Create: `Assets/Scripts/UI/PlayerHUD/UI_DisplayTextReader.cs`
- Create: `Assets/UI/Player HUD/UI_DisplayTextReader.prefab`

Player walks up to a `DisplayTextFurniture`, presses E (existing Interactable plumbing fires `Furniture.Use(character)` or `Interact(character)` — verify which by reading `Furniture.cs`). The reader UI shows the sign's title (parent building name if applicable, else "Sign") + the multi-line text + Close button + outside-click dismiss.

If the sign is referenced as the parent building's `_helpWantedFurniture` AND `building.IsHiring == true`, the panel adds an **"Apply for a job"** button at the bottom — clicking queues the existing `InteractionAskForJob` against the building's owner. (Implementation in Task 7 builds on this base.)

- [ ] **Step 1: Override Furniture.Interact in DisplayTextFurniture**

In `DisplayTextFurniture.cs`, override the canonical "interact with this furniture" entry point (`Use(Character)` or `Interact(Character)` — read `Furniture.cs` to confirm):

```csharp
    public override bool Use(Character character)   // or Interact, depending on actual base API
    {
        if (character == null) return false;
        if (!character.IsPlayer()) return true;     // NPCs have no need to "read" — silent success
        UI_DisplayTextReader.Show(this);
        return true;
    }
```

- [ ] **Step 2: Create UI_DisplayTextReader.cs**

```csharp
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Player-facing reader for <see cref="DisplayTextFurniture"/>. Singleton-on-demand: first
/// call to Show() instantiates the prefab under the active PlayerUI canvas.
/// Closes via Close button, outside-click, or ESC.
/// </summary>
public class UI_DisplayTextReader : MonoBehaviour
{
    private const string PrefabResourcePath = "UI/UI_DisplayTextReader";

    private static UI_DisplayTextReader _instance;

    [SerializeField] private TextMeshProUGUI _titleLabel;
    [SerializeField] private TextMeshProUGUI _bodyLabel;
    [SerializeField] private GameObject _applyButton;          // shown when sign is help-wanted (Task 7)
    [SerializeField] private Button _closeButton;
    [SerializeField] private Button _dismissOverlay;
    [SerializeField] private Button _applyButtonComponent;     // Button on _applyButton

    private DisplayTextFurniture _currentSign;
    private CommercialBuilding _currentBuilding;

    public static void Show(DisplayTextFurniture sign)
    {
        if (sign == null) return;
        if (_instance == null)
        {
            var prefab = Resources.Load<UI_DisplayTextReader>(PrefabResourcePath);
            if (prefab == null)
            {
                Debug.LogWarning($"[UI_DisplayTextReader] No prefab at Resources/{PrefabResourcePath}.");
                return;
            }
            var canvas = Object.FindFirstObjectByType<Canvas>();
            _instance = Instantiate(prefab, canvas != null ? canvas.transform : null);
        }
        _instance.ShowInternal(sign);
    }

    private void Awake()
    {
        if (_closeButton != null) _closeButton.onClick.AddListener(Close);
        if (_dismissOverlay != null) _dismissOverlay.onClick.AddListener(Close);
        if (_applyButtonComponent != null) _applyButtonComponent.onClick.AddListener(OnApplyClicked);
    }

    private void ShowInternal(DisplayTextFurniture sign)
    {
        _currentSign = sign;
        _currentBuilding = sign.GetComponentInParent<CommercialBuilding>();

        bool isHelpWanted = _currentBuilding != null
            && _currentBuilding.HelpWantedSign == sign
            && _currentBuilding.IsHiring;

        string title = _currentBuilding != null ? _currentBuilding.BuildingName : "Sign";
        _titleLabel.text = title;
        _bodyLabel.text = sign.DisplayText;
        if (_applyButton != null) _applyButton.SetActive(isHelpWanted);

        gameObject.SetActive(true);
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) Close();
    }

    private void Close()
    {
        gameObject.SetActive(false);
        _currentSign = null;
        _currentBuilding = null;
    }

    private void OnApplyClicked()
    {
        // Implementation lands in Task 7 when the player-side application path is wired.
        // Stub here so Awake-bound listener doesn't NRE.
        Debug.Log("[UI_DisplayTextReader] Apply clicked — Task 7 will wire this up.");
    }
}
```

- [ ] **Step 3: Create the prefab**

In Unity Editor:
1. Open the Player HUD scene or any scene with an active Canvas.
2. Create `GameObject → UI → Panel`. Name it `UI_DisplayTextReader`.
3. Add a child `Image` filling the screen with raycast-target enabled — use it as `_dismissOverlay` (semi-transparent dark backdrop, e.g. `Color (0,0,0,0.4)`). Make it full-screen-stretch.
4. Add a content panel inside — center-anchored, ~60% width / ~50% height, dark background.
5. Inside the content panel: add `TextMeshProUGUI` child for title (top, large bold) and `_bodyLabel` (centered, multi-line). Add a `Close` button + an `Apply` button (initially inactive).
6. Wire all references on the script component.
7. Save the prefab to `Assets/Resources/UI/UI_DisplayTextReader.prefab`. Delete the scene instance.

- [ ] **Step 4: Compile + commit**

```bash
git add Assets/Scripts/World/Furniture/DisplayTextFurniture.cs Assets/Scripts/UI/PlayerHUD/UI_DisplayTextReader.cs "Assets/Resources/UI/UI_DisplayTextReader.prefab"
git commit -m "feat(ui): UI_DisplayTextReader — player reads any DisplayTextFurniture

Singleton-on-demand panel. Title = parent building name (if any), body =
sign's DisplayText. Apply button is visible only when the sign is the
parent building's _helpWantedFurniture AND IsHiring is true; click handler
is a stub here, wired in Task 7.

DisplayTextFurniture.Use override opens the reader for player workers;
NPCs silent-success (no UI need).

Part of: help-wanted-and-hiring plan, Task 6/10.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 7: Apply for Job button — wire the player-side application

**Files:**
- Modify: `Assets/Scripts/UI/PlayerHUD/UI_DisplayTextReader.cs`

Replace the Task 6 `OnApplyClicked` stub with the real flow: enumerate vacant jobs at the parent building, present them (a sub-menu if multiple, or auto-pick if one), and trigger the existing `InteractionAskForJob` against the building's owner.

- [ ] **Step 1: Replace OnApplyClicked**

```csharp
    private void OnApplyClicked()
    {
        if (_currentBuilding == null || !_currentBuilding.IsHiring) return;
        if (!_currentBuilding.HasOwner)
        {
            Debug.LogWarning("[UI_DisplayTextReader] Apply rejected — building has no Owner to apply to.");
            return;
        }

        var localPlayer = PlayerController.LocalPlayer;     // adapt to actual API
        if (localPlayer == null || localPlayer.Character == null) return;
        if (localPlayer.Character.CharacterJob != null && localPlayer.Character.CharacterJob.HasJob)
        {
            Debug.Log("[UI_DisplayTextReader] Apply rejected — player already has a job.");
            return;
        }

        var vacancies = _currentBuilding.GetVacantJobs();
        if (vacancies.Count == 0) return;

        // V1: auto-pick the first vacancy. A future iteration can show a sub-menu when multiple
        // distinct JobTitles are open.
        var job = vacancies[0];

        var interaction = new InteractionAskForJob(_currentBuilding, job);
        if (!interaction.CanExecute(localPlayer.Character, _currentBuilding.Owner))
        {
            Debug.Log("[UI_DisplayTextReader] Apply rejected by InteractionAskForJob.CanExecute.");
            return;
        }

        // Trigger the social interaction. The exact API for "kick off an InteractionInvitation
        // between two characters" depends on the codebase's existing pattern — verify by
        // reading the existing Apply-for-Job code path used by hold-E menus in CharacterJob.cs
        // (look for RequestJobApplicationServerRpc).
        localPlayer.Character.CharacterJob.RequestJobApplicationServerRpc(
            _currentBuilding.Owner.NetworkObjectId,
            _currentBuilding.GetJobStableIndex(job));

        Close();
    }
```

**Verify:** the canonical "trigger an Apply for Job interaction" entry point. Per `wiki/systems/character-job.md` change-log 2026-04-24, `CharacterJob.RequestJobApplicationServerRpc(ownerNetId, jobStableIndex)` is the existing client-routed path for hold-E clicks. Reuse it.

If `CommercialBuilding.GetJobStableIndex(Job)` doesn't already exist, add a small public helper:

```csharp
    public int GetJobStableIndex(Job job) => _jobs.IndexOf(job);
```

- [ ] **Step 2: Compile + commit**

```bash
git add Assets/Scripts/UI/PlayerHUD/UI_DisplayTextReader.cs Assets/Scripts/World/Buildings/CommercialBuilding.cs
git commit -m "feat(ui): wire Apply for Job button on Help Wanted reader

Replaces Task 6's stub. Apply path:
1. Validate IsHiring + HasOwner + player has no job + vacancies > 0.
2. Pick first vacancy (V1; multi-vacancy sub-menu is a follow-up).
3. Build InteractionAskForJob, validate CanExecute (existing IsHiring
   gate from Task 5 also runs).
4. Route via CharacterJob.RequestJobApplicationServerRpc (existing
   client→server hold-E pattern from 2026-04-24).
5. Close the reader UI.

GetJobStableIndex helper added to CommercialBuilding so the UI doesn't
have to walk _jobs itself.

Part of: help-wanted-and-hiring plan, Task 7/10.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 8: UI_OwnerHiringPanel

**Files:**
- Create: `Assets/Scripts/UI/PlayerHUD/UI_OwnerHiringPanel.cs`
- Create: `Assets/UI/Player HUD/UI_OwnerHiringPanel.prefab`

Owner-only panel exposed via the building's interaction menu. Lists all `_jobs` rows (vacant / filled by NPCName), shows current `IsHiring` state, gives an Open / Close toggle button + an "Edit Sign Text" multi-line text field that calls `_helpWantedFurniture.TrySetDisplayText(...)`.

- [ ] **Step 1: Create UI_OwnerHiringPanel.cs**

```csharp
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>
/// Owner-only management panel for a CommercialBuilding's hiring state. Opened via the
/// building's interaction menu (Task 8 wires the menu entry). Lets the owning player toggle
/// hiring open/closed and write custom text to the Help Wanted sign.
///
/// Custom sign text is overwritten when hiring re-opens (per spec §15.8 Q15.1) — the
/// hint label below the input field calls this out so owners aren't surprised.
/// </summary>
public class UI_OwnerHiringPanel : MonoBehaviour
{
    private const string PrefabResourcePath = "UI/UI_OwnerHiringPanel";
    private static UI_OwnerHiringPanel _instance;

    [SerializeField] private TextMeshProUGUI _titleLabel;
    [SerializeField] private TextMeshProUGUI _statusLabel;
    [SerializeField] private Transform _jobListRoot;
    [SerializeField] private GameObject _jobRowPrefab;       // one row = "JobTitle — vacant" or "JobTitle — Bob"
    [SerializeField] private Button _toggleHiringButton;
    [SerializeField] private TextMeshProUGUI _toggleHiringLabel;
    [SerializeField] private TMP_InputField _customTextInput;
    [SerializeField] private Button _submitTextButton;
    [SerializeField] private TextMeshProUGUI _customTextHint;
    [SerializeField] private Button _closeButton;

    private CommercialBuilding _building;
    private readonly List<GameObject> _spawnedRows = new List<GameObject>(8);

    public static void Show(CommercialBuilding building)
    {
        if (building == null) return;
        if (_instance == null)
        {
            var prefab = Resources.Load<UI_OwnerHiringPanel>(PrefabResourcePath);
            if (prefab == null) return;
            var canvas = Object.FindFirstObjectByType<Canvas>();
            _instance = Instantiate(prefab, canvas != null ? canvas.transform : null);
        }
        _instance.ShowInternal(building);
    }

    private void Awake()
    {
        if (_toggleHiringButton != null) _toggleHiringButton.onClick.AddListener(OnToggleHiring);
        if (_submitTextButton != null) _submitTextButton.onClick.AddListener(OnSubmitText);
        if (_closeButton != null) _closeButton.onClick.AddListener(Close);
    }

    private void ShowInternal(CommercialBuilding building)
    {
        _building = building;
        _titleLabel.text = $"Manage Hiring — {building.BuildingName}";

        // Listen for live updates so the panel reflects RPC-driven state changes.
        building.OnHiringStateChanged += HandleHiringChanged;

        Refresh();
        gameObject.SetActive(true);
    }

    private void HandleHiringChanged(bool _) => Refresh();

    private void Refresh()
    {
        if (_building == null) return;

        _statusLabel.text = _building.IsHiring ? "Currently Hiring: <color=#56C26B>Yes</color>" : "Currently Hiring: <color=#C25656>No</color>";
        _toggleHiringLabel.text = _building.IsHiring ? "Close Hiring" : "Open Hiring";

        // Sign-edit row only enabled when a sign is referenced.
        bool hasSign = _building.HelpWantedSign != null;
        if (_customTextInput != null) _customTextInput.interactable = hasSign;
        if (_submitTextButton != null) _submitTextButton.interactable = hasSign;
        if (_customTextHint != null) _customTextHint.text = hasSign
            ? "Custom text resets when hiring is reopened."
            : "(No Help Wanted sign assigned to this building.)";

        // Rebuild job list rows.
        for (int i = 0; i < _spawnedRows.Count; i++) Destroy(_spawnedRows[i]);
        _spawnedRows.Clear();

        var allJobs = _building.Jobs;     // verify accessor name; might be `_jobs` private + new public Jobs.
        for (int i = 0; i < allJobs.Count; i++)
        {
            var job = allJobs[i];
            if (job == null) continue;
            var row = Instantiate(_jobRowPrefab, _jobListRoot);
            var label = row.GetComponentInChildren<TextMeshProUGUI>();
            string status = job.IsAssigned ? job.Worker?.CharacterName ?? "(filled)" : "vacant";
            label.text = $"{job.JobTitle} — {status}";
            _spawnedRows.Add(row);
        }
    }

    private void OnToggleHiring()
    {
        if (_building == null) return;
        var localPlayer = PlayerController.LocalPlayer;
        if (localPlayer == null || localPlayer.Character == null) return;

        if (_building.IsHiring) _building.TryCloseHiring(localPlayer.Character);
        else _building.TryOpenHiring(localPlayer.Character);
        // Refresh on next OnHiringStateChanged event.
    }

    private void OnSubmitText()
    {
        if (_building == null || _building.HelpWantedSign == null) return;
        var localPlayer = PlayerController.LocalPlayer;
        if (localPlayer == null || localPlayer.Character == null) return;

        string text = _customTextInput != null ? _customTextInput.text : "";
        _building.HelpWantedSign.TrySetDisplayText(localPlayer.Character, text);
        // Empty input → reverts to auto-formatted next refresh, but only if hiring is currently
        // closed (open hiring overwrites on next toggle anyway).
    }

    private void Close()
    {
        if (_building != null) _building.OnHiringStateChanged -= HandleHiringChanged;
        gameObject.SetActive(false);
    }
}
```

- [ ] **Step 2: Verify CommercialBuilding.Jobs accessor**

The script references `_building.Jobs` (a public read-only list). If only `_jobs` (private) exists, add a small accessor:

```csharp
    public IReadOnlyList<Job> Jobs => _jobs;
```

- [ ] **Step 3: Wire the menu entry**

The owner needs a way to open this panel. The codebase has a hold-E interaction menu pattern (per `wiki/systems/character-job.md` change-log 2026-04-24, owner sees "Apply for {JobTitle}" entries). Add a sibling "Manage Hiring..." entry visible only to the building's owner.

Find `CharacterJob.GetInteractionOptions(interactor)` (mentioned in change-log). Add an additional option when `interactor == this.Workplace.Owner` (i.e. the player IS the boss of that workplace):

```csharp
    // Inside GetInteractionOptions:
    // ... existing "Apply for {JobTitle}" entries ...
    if (OwnedBuilding != null && OwnedBuilding == buildingBeingInteractedWith)   // adapt to actual API
    {
        yield return new InteractionOption("Manage Hiring...", () => UI_OwnerHiringPanel.Show(OwnedBuilding));
    }
```

The exact integration depends on the existing menu plumbing. Read the file first.

- [ ] **Step 4: Create the prefab**

Build the panel layout in Unity Editor: title row, status line, scrollable job list (use a `ScrollView` with `_jobListRoot` as the content), toggle hiring button, multi-line input field for custom sign text + Submit button + hint label, Close button. Save to `Assets/Resources/UI/UI_OwnerHiringPanel.prefab`.

- [ ] **Step 5: Compile + commit**

```bash
git add Assets/Scripts/UI/PlayerHUD/UI_OwnerHiringPanel.cs Assets/Scripts/Character/CharacterJob/CharacterJob.cs Assets/Scripts/World/Buildings/CommercialBuilding.cs "Assets/Resources/UI/UI_OwnerHiringPanel.prefab"
git commit -m "feat(ui): UI_OwnerHiringPanel — owner toggles hiring + edits sign

Owner-only management panel. Status line, scrollable job list, Open/Close
toggle, multi-line custom-text input field, hint label calling out the
'custom text resets on reopen' invariant from spec Q15.1.

Menu integration: GetInteractionOptions now emits 'Manage Hiring...'
entry when the interactor is the building's owner.

Part of: help-wanted-and-hiring plan, Task 8/10.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 9: Smoketest

**Files:**
- Create: `docs/superpowers/smoketests/2026-04-30-help-wanted-and-hiring-smoketest.md`

Hand-driven validation against an existing `HarvestingBuilding` (no FarmingBuilding dependency).

- [ ] **Step 1: Write the smoketest**

Path: `docs/superpowers/smoketests/2026-04-30-help-wanted-and-hiring-smoketest.md`

```markdown
# Help Wanted + Owner-Controlled Hiring — Smoketest

**Date:** 2026-04-30
**Plan:** [docs/superpowers/plans/2026-04-30-help-wanted-and-owner-hiring.md](../plans/2026-04-30-help-wanted-and-owner-hiring.md)
**Status:** _(replace with Pass / Fail-with-notes after running)_

## Setup
- Open or create a test scene with a `HarvestingBuilding` prefab.
- The HarvestingBuilding must have an Owner (a hired NPC or a player). Note the Owner's reference for the manage-panel test.
- Drop a `DisplayTextFurniture_Placard.prefab` instance inside the building. Set its `_initialText` to "Welcome to the lumber mill" (or any default).
- On the HarvestingBuilding's CommercialBuilding component, set `_helpWantedFurniture` to reference the Placard. Set `_initialHiringOpen = true`.
- Save the scene.

## Smoke A — Static sign reads correctly
- [ ] Walk a player up to the placard. Press E.
- [ ] **Assert**: the reader UI opens with title = building name, body = `_initialText` (or current sign text), no Apply button (sign is the help-wanted slot but…wait — IsHiring is true, so Apply DOES show. Adapt assertion: Apply IS visible.)

## Smoke B — Help Wanted text auto-populates on hiring open
- [ ] In the Console, call `building.TryOpenHiring(building.Owner)`.
- [ ] **Assert**: `building.IsHiring == true` (already was, idempotent).
- [ ] **Assert**: the placard's `DisplayText` now reads "Hiring at <BuildingName>: • N <JobTitle>… Approach the owner to apply." (formatted by `GetHelpWantedDisplayText`).
- [ ] Open the reader UI again — body should match.

## Smoke C — Closing hiring reverts sign + blocks applications
- [ ] `building.TryCloseHiring(building.Owner)`.
- [ ] **Assert**: `building.IsHiring == false`.
- [ ] **Assert**: placard text = `GetClosedHiringDisplayText()` (default empty).
- [ ] Reader UI: Apply button is hidden.
- [ ] As player, attempt to apply via hold-E menu on the Owner — Apply entry should not be selectable.
- [ ] As NPC, fire a `NeedJob.GetGoapActions` evaluation. Verify it returns no actions (FindAvailableJob skips closed buildings).

## Smoke D — Reopening hiring restores sign
- [ ] `building.TryOpenHiring(building.Owner)`.
- [ ] **Assert**: placard text = formatted vacancy text again.
- [ ] **Assert**: applications work again (player + NPC).

## Smoke E — Vacancy churn refreshes sign
- [ ] With hiring open and 2 vacant Harvester slots, hire one NPC into a Harvester role via existing flow.
- [ ] **Assert**: placard text now shows "1 Harvester" (count decremented).
- [ ] Have the NPC quit the job (`worker.CharacterJob.QuitJob(job)`).
- [ ] **Assert**: placard text shows "2 Harvesters" again.

## Smoke F — Owner-only authority
- [ ] Set the player to be a NON-owner (i.e. the Owner is a different NPC).
- [ ] Call `building.TryOpenHiring(player.Character)` — expect `false`.
- [ ] Same for `TryCloseHiring`.
- [ ] Verify `_isHiring` did not change.
- [ ] Owner-Hiring panel: NPC owner trying to manage another building's panel → `CanRequesterControlHiring` rejects.

## Smoke G — Custom sign text from owner
- [ ] As the Owner player, open the Manage Hiring panel.
- [ ] Type "Come find me at the back tent for an interview." in the custom text field. Click Submit.
- [ ] **Assert**: placard text now shows the custom string.
- [ ] Close hiring + reopen.
- [ ] **Assert**: placard text reverted to auto-formatted vacancy text. (Q15.1 invariant — custom text doesn't survive a reopen.)

## Smoke H — Multi-peer replication
- [ ] Multiplayer: Host + 1 Client.
- [ ] On the Host, open hiring. Verify both Host and Client see the same placard text after a frame.
- [ ] On the Client, walk up to the sign. Reader UI shows the correct text (via NetworkVariable replication).
- [ ] As Owner-on-the-Host, change custom text. Client sees the update within one frame.
- [ ] As Owner-on-the-Client (different scenario — Client is the Owner), call `TryOpenHiring`. Verify the ServerRpc round-trips correctly: Host's `_isHiring` flips, Client sees the replication, sign refreshes on both peers.

## Result

All 8 smokes pass → mark Status: **Pass**. Commit:

```bash
git add docs/superpowers/smoketests/2026-04-30-help-wanted-and-hiring-smoketest.md
git commit -m "test(help-wanted): smoketest pass — primitive validated on HarvestingBuilding"
```
```

(Trim Smoke A's contradictory wording before writing — the placard IS in the help-wanted slot AND IsHiring is true, so Apply IS visible; rephrase the assertion accordingly.)

- [ ] **Step 2: Commit**

```bash
git add docs/superpowers/smoketests/2026-04-30-help-wanted-and-hiring-smoketest.md
git commit -m "test(help-wanted): smoketest checklist (Task 9 of 10)

Hand-driven validation: 8 scenarios covering static-sign read, hiring
open auto-format, hiring close gate, reopen restoration, vacancy
churn refresh, owner-only authority, custom text + reopen-overwrite
invariant, multi-peer replication. Status field left blank — Kevin
to mark Pass after running.

Part of: help-wanted-and-hiring plan, Task 9/10.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 10: Documentation

**Files:**
- Create: `.agent/skills/help-wanted-and-hiring/SKILL.md`
- Create: `wiki/systems/help-wanted-and-hiring.md`
- Modify: `wiki/systems/commercial-building.md` (change-log)

- [ ] **Step 1: SKILL.md**

`.agent/skills/help-wanted-and-hiring/SKILL.md` — capture the public API surface:

- `DisplayTextFurniture` (`InitialText`, `DisplayText`, `OnDisplayTextChanged`, `TrySetDisplayText`).
- `DisplayTextFurnitureNetSync` (server-only `ServerSetDisplayText`).
- `CommercialBuilding` hiring API (`IsHiring`, `HelpWantedSign`, `OnHiringStateChanged`, `CanRequesterControlHiring`, `GetVacantJobs`, `TryOpenHiring`, `TryCloseHiring`, `NotifyVacancyChanged`, `GetHelpWantedDisplayText` / `GetClosedHiringDisplayText` virtual overrides).
- Integration points (`InteractionAskForJob.CanExecute` gate, `BuildingManager.FindAvailableJob` filter).
- Player UI (`UI_DisplayTextReader`, `UI_OwnerHiringPanel`).
- Gotchas + follow-ups (NPC-owner GOAP for hiring is Phase 2; multi-vacancy sub-menu in Apply UI is Phase 2).

- [ ] **Step 2: Wiki page**

`wiki/systems/help-wanted-and-hiring.md` — full system page using the system template. Include:
- Required frontmatter (`type: system`, `primary_agent`, `owner_code_path`, etc.).
- Purpose, Responsibilities, Non-responsibilities.
- Key classes / files table.
- Public API entry points (link to SKILL.md).
- Data flow diagram (text/ASCII).
- Dependencies (upstream + downstream).
- State & persistence (`_isHiring` NetworkVariable + `_displayText` NetworkVariable, no new save fields beyond replication).
- Network rules (server-only writes, ClientRpc-free — pure NetworkVariable replication).
- Known gotchas / edge cases (custom text reset on reopen; multi-sign per building unsupported in v1).
- Open questions / TODO (NPC-owner hiring AI; Apply UI sub-menu; multi-sign coordination).
- Change log: `2026-04-30 — Initial implementation, Plan 2 of 3 in the Farmer rollout. — claude`.
- Sources.

- [ ] **Step 3: Cross-references**

Update `wiki/systems/commercial-building.md`:
- Add `[[help-wanted-and-hiring]]` to `related[]` frontmatter.
- Bump `updated:` to `2026-04-30`.
- Append change-log entry: `2026-04-30 — Hiring API: _isHiring NetworkVariable + _helpWantedFurniture reference + TryOpenHiring/TryCloseHiring/CanRequesterControlHiring/GetVacantJobs/GetHelpWantedDisplayText (virtual). InteractionAskForJob + BuildingManager.FindAvailableJob now gate on IsHiring. See [[help-wanted-and-hiring]]. — claude`.

- [ ] **Step 4: Commit**

```bash
git add .agent/skills/help-wanted-and-hiring/ wiki/systems/help-wanted-and-hiring.md wiki/systems/commercial-building.md
git commit -m "docs(help-wanted): SKILL.md + wiki page + cross-references (Task 10 of 10)

Captures the primitive's public API, integration points, gotchas, and
follow-ups (NPC-owner hiring AI, multi-vacancy Apply sub-menu).

Cross-reference: commercial-building.md change-log + related[].

Part of: help-wanted-and-hiring plan, Task 10/10.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Self-Review Checklist

**1. Spec coverage** — Each spec section §15 sub-section is implemented:
- §15.1 `DisplayTextFurniture` + NetSync → Task 1 ✓
- §15.2 `_isHiring` + `_helpWantedFurniture` → Task 2 ✓
- §15.3 hiring API (`CanRequesterControlHiring`, `GetVacantJobs`, `TryOpenHiring`, `TryCloseHiring`, `GetHelpWantedDisplayText`, `GetClosedHiringDisplayText`) → Task 3 ✓
- §15.4 `InteractionAskForJob` + `NeedJob` gate → Task 5 ✓
- §15.5 player UI (reader + owner panel) → Tasks 6+7+8 ✓
- §15.6 persistence (zero new save fields beyond NetworkVariable replication) ✓
- §15.7 network rules (server-auth, NetworkVariable replication) ✓
- §15.8 sign auto-update on hiring open/close + on vacancy churn → Task 4 ✓
- §15.9 tests → Task 9 smoketest ✓

**2. Placeholder scan** — every step contains code or specific instructions; no "implement appropriate handling" / "TBD" / "TODO" left in.

**3. Type consistency** — `_isHiring` (Task 2) ↔ `IsHiring` accessor (Tasks 2, 5) ↔ `OnHiringStateChanged` (Tasks 2, 8) ↔ `HandleIsHiringChanged` / `HandleHiringStateChanged` (Tasks 2, 4) ↔ `_helpWantedFurniture` (Tasks 2, 4) ↔ `HelpWantedSign` accessor (Tasks 2, 6, 7, 8) — all consistent.

**4. Phase boundary** — at end of plan, the primitive is testable on its own against an existing `HarvestingBuilding` (Task 9 smoke). No dependency on the not-yet-built `FarmingBuilding`.

---

## Acceptance Criteria

- [ ] All 10 tasks committed.
- [ ] Smoketest checklist (Task 9) marked Pass on the existing HarvestingBuilding.
- [ ] Wiki + SKILL.md updated.
- [ ] No regressions in existing job/building/character-action tests.

After this plan ships and is verified, **Plan 3 (Farmer integration)** is the next plan to write — `FarmingBuilding` + `JobFarmer` + plant/water tasks + the full farming loop. Plan 3 will consume both Plan 1 (Tool Storage) and Plan 2 (Help Wanted + Hiring).
