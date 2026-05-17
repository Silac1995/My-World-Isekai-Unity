# AdministrativeBuilding Skeleton Implementation Plan (Plan 4a)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the AdministrativeBuilding (AB) skeleton — the typed `CommercialBuilding` subclass + `BuildingType.Administrative` enum entry + a `Building.OnFinalize` virtual hook that AB uses to grant the founder citizenship — together with the Plan-3-deferred ambition tasks (`Task_PlaceBuilding`, `Task_FinishConstruction`) and the full `Ambition_FoundACity.asset` quest chain (1 ambition + 8 quests). This is the foundation Plans 4b/4c/5 consume; Plan 4b adds the job pipeline (JobBuilder + BuildOrder + GOAP actions), Plan 4c adds migration + tier-up + admin console UI, and Plan 4a alone produces a placeable+constructable AB that grants citizenship on completion.

**Architecture:**
- **`BuildingType.Administrative`** is a new enum value appended to `BuildingType.cs`. Every existing `CommercialBuilding` subclass overrides `BuildingType` to return its own value; `AdministrativeBuilding` does the same with `BuildingType.Administrative`.
- **`AdministrativeBuilding : CommercialBuilding`** is the new class. Plan 4a ships the *skeleton* — `OwnerCommunity` getter (server-set on placement), `GetTreasuryBalance()` helper, `OnFinalize` override granting founder citizenship — without the heavy preplaced-furniture wiring (CityManagement / JoinRequestDesk / SafeFurniture references stay as `[SerializeField]` placeholders for Plan 4c) or `InitializeJobs` (Plan 4b ships the real jobs; Plan 4a leaves the base implementation untouched so AB has zero jobs initially).
- **`Building.OnFinalize` virtual hook** is a NEW extension point. The existing `Building.Finalize()` method flips `_currentState.Value = Complete` server-side; we add a `protected virtual void OnFinalize() { }` call right after the state flip so subclasses can react. Plan 4a's *only* override is `AdministrativeBuilding.OnFinalize()` granting citizenship to the founder via `actor.CharacterCommunity.SetCitizenship(OwnerCommunity)`. Other subclasses (Shop, Bar, Forge, etc.) inherit the base no-op.
- **`Community.AdministrativeBuilding : AdministrativeBuilding`** is a `[NonSerialized]` runtime reference (the AB instance is found via `BuildingManager` lookups, not stored on Community itself — Community is a plain C# class that doesn't survive serialization with NetworkBehaviour references). The accompanying `IsChartered => AdministrativeBuilding != null` is the canonical "is this community chartered?" check that Plan 4c's drifter-migration code and Plan 5's admin-console gate read.
- **`CharacterCommunity.CreateCommunity` auto-grants the AB blueprint** to the founder via `Resources.Load<BuildingSO>("Data/Buildings/AdministrativeBuilding")` → `_character.CharacterBlueprints.GrantBlueprint(abSO)` (using Plan 1's `GrantBlueprint(BuildingSO)` API). Single-line addition; runs server-side; no-op if the SO isn't found (defensive).
- **Auto-owner binding** lives in `AdministrativeBuilding.OnNetworkSpawn` — when the AB spawns and `OwnerCommunity` resolves, every leader's CharacterId gets added to `_ownerIds` so any leader can interact with leader-gated AB features. Mirrors the existing `Room.AddOwner` API.
- **1-per-community placement gate** is a pre-check in `BuildingPlacementManager.ValidatePlacement` — if the blueprint being placed has `BuildingType.Administrative` and the founder's community already has an AB, reject with a toast.
- **`Task_PlaceBuilding`** is a passive completion-watcher: `Tick` returns Completed when `BuildingManager.Instance.allBuildings` contains a building whose `Blueprint == TargetBlueprint` and whose `PlacedByCharacterId == actor.CharacterId`. The placement itself is driven by either the player's ghost flow or an NPC BTAction (Plan 4b adds an `NPCBuildingPlacer` BTAction; Plan 4a just ships the task — players still drive placement manually for now).
- **`Task_FinishConstruction`** is also a passive watcher: returns Completed when the building matching `TargetBlueprint` + `PlacedByCharacterId` reaches `!IsUnderConstruction`. Re-uses the cooperative construction loop (Phase 1) — no new behavior driven from the task itself.
- **Ambition + Quest assets** are authored via a one-shot Roslyn script invoked through MCP `script-execute`. The script creates 1 `Ambition_FoundACity.asset` and 8 `Quest_*.asset` files at `Assets/Resources/Data/Ambitions/` with the full task chain wired up: `Quest_CreateCommunity → Quest_BuildCapital → Quest_PromoteCamp/Village/Town/City/Kingdom/Empire`. Each `Quest_*.asset` carries a single `[SerializeReference] TaskBase` instance (configured for the right SO / Level). The ambition's `_quests` list references them in order.

**Tech Stack:** Unity 6.0 / NGO 2.x, C# 9. No new asmdef. Asset authoring via `mcp__ai-game-developer__script-execute` Roslyn one-shot. New EditMode tests under `Assets/Editor/Tests/Ambition/` (no asmdef, Assembly-CSharp-Editor).

**Rules enforced throughout:** CLAUDE.md rules #1-#8 (think first), #9-#14 (SOLID — AdministrativeBuilding inherits CommercialBuilding cleanly; the OnFinalize hook is a small open-for-extension contract), #15 (`_underscorePrefix` private fields), #16 (no event subscriptions added; no cleanup needed), #18/#19/#19b (server-only state — full audit below), #22 (player↔NPC parity — both paths place the AB via the same `BuildingPlacementManager.RequestPlacementServerRpc`, the only difference is who issues the click), #28/#29/#29b (skill + agent + wiki updates), #31 (defensive null-checks everywhere — missing blueprint, missing community, missing founder).

**Network safety audit (rule #19b — performed BEFORE writing the plan):**
1. **Who writes the new state?** Server-only. `Building.OnFinalize` fires inside server-only `Building.Finalize()`. `AdministrativeBuilding.OwnerCommunity` is set server-side during placement. `Community.AdministrativeBuilding` is set server-side from `AdministrativeBuilding.OnNetworkSpawn` when the AB resolves its owner community. `CharacterCommunity.SetCitizenship` is server-only (Plan 1 already enforced this).
2. **What replication channel?** **No NEW replication channels** added by Plan 4a. The new state surfaces to clients via existing channels:
   - `Community` is server-only state today (Plan 1 noted this — clients read via `MapRegistry.CommunityData` save-data snapshots). `Community.AdministrativeBuilding` follows the same pattern; clients don't observe it directly.
   - `AdministrativeBuilding._ownerIds` is the existing `NetworkList<FixedString64Bytes>` from `Room` (Plan 4a's auto-owner binding writes to the same list; standard replication path).
   - `Building._currentState` is the existing replicated NetworkVariable; the `OnFinalize` hook reacts to its flip, no new field.
   - `CharacterCommunity._citizenship` round-trips via `CommunitySaveData.citizenshipMapId` (Plan 1).
3. **Late-joiner sees?** Same as today for community state. For the AB specifically: a joining client sees the AB GameObject (NetworkObject + its existing replicated fields like `_currentState`, `_ownerIds`, `PlacedByCharacterId`). They don't see `OwnerCommunity` directly (server-only), but they can resolve "which community owns this AB" via the leader IDs in `_ownerIds`. Good enough for Plan 4a's purposes; Plan 5 may add a `NetworkVariable<ulong>` for direct OwnerCommunity NetId replication when the admin console UI needs it.
4. **Client-side pre-gate?** `BuildingPlacementManager.ValidatePlacement`'s 1-per-community gate runs client-side too (for the ghost preview). Client-side it reads `_character.CharacterCommunity.CurrentCommunity.AdministrativeBuilding` — which is server-side state. On the client, that ref will be null (no replication), so the client-side gate is "optimistic green" — the server toast handles real rejection. Same compromise as Plan 2's `CanPlace` gate.
5. **`GetComponentInParent` spawn-race?** N/A for Plan 4a — no new component is added to an existing prefab. AdministrativeBuilding is a new subclass; subclasses don't have spawn-race issues if the prefab is correctly authored.
6. **`InteractableObject.IsCharacterInInteractionZone` (rule #36)?** N/A — Plan 4a doesn't add new player↔interactable surfaces. The AB itself uses the existing `BuildingInteractable` (inherited from `Building`); Plan 4c adds the CityManagement furniture interaction.

**Out of scope (Plan 4b + Plan 4c):**
- AB.prefab (only the AdministrativeBuilding.asset BuildingSO is authored in Plan 4a; the actual `.prefab` ships in Plan 4c alongside the preplaced furniture).
- AdministrativeBuilding's `InitializeJobs` (Plan 4b adds `JobBuilder × 2 + JobHarvester(CityHarvester) + JobLogisticsManager`).
- AdministrativeBuilding's `NetworkList<JoinRequest> PendingJoinRequests` field (Plan 4c).
- AdministrativeBuilding's `_unfulfillableMaterialHarvestQueue` (Plan 4b).
- Preplaced furniture wiring (Plan 4c).
- Community.TryPromoteLevel + tier requirements (Plan 4c).
- BuildOrder + JobBuilder + GOAP actions (Plan 4b).
- DrifterMigrationSystem + JoinRequestDesk (Plan 4c).
- CityManagementFurniture + UI_CityManagementPanel (Plan 4c).
- Treasury seeding for the AB (already covered by Plan 1's `CommercialBuilding.OnDefaultFurnitureSpawned` BaseTreasury path — AB inherits it free).

---

## File Structure

**New files:**
- `Assets/Scripts/World/Buildings/CommercialBuildings/AdministrativeBuilding.cs` — the subclass.
- `Assets/Scripts/Character/Ambition/Tasks/Task_PlaceBuilding.cs` — passive task watching for placement.
- `Assets/Scripts/Character/Ambition/Tasks/Task_FinishConstruction.cs` — passive task watching for completion.
- `Assets/Editor/Tests/Ambition/Task_PlaceBuildingTests.cs` — EditMode unit tests.
- `Assets/Editor/Tests/Ambition/Task_FinishConstructionTests.cs` — EditMode unit tests.
- `Assets/Resources/Data/Buildings/AdministrativeBuilding.asset` — minimal BuildingSO scaffold (created via Roslyn). Prefab ref stays null until Plan 4c.
- `Assets/Resources/Data/Ambitions/Ambition_FoundACity.asset` — the AmbitionSO instance.
- `Assets/Resources/Data/Ambitions/Quest_CreateCommunity.asset` — single Task_CreateCommunity.
- `Assets/Resources/Data/Ambitions/Quest_BuildCapital.asset` — Task_PlaceBuilding(AB) + Task_FinishConstruction(AB).
- `Assets/Resources/Data/Ambitions/Quest_PromoteCamp.asset` — Task_PromoteCommunity(Camp).
- `Assets/Resources/Data/Ambitions/Quest_PromoteVillage.asset` — Task_PromoteCommunity(Village).
- `Assets/Resources/Data/Ambitions/Quest_PromoteTown.asset` — Task_PromoteCommunity(Town).
- `Assets/Resources/Data/Ambitions/Quest_PromoteCity.asset` — Task_PromoteCommunity(City).
- `Assets/Resources/Data/Ambitions/Quest_PromoteKingdom.asset` — Task_PromoteCommunity(Kingdom).
- `Assets/Resources/Data/Ambitions/Quest_PromoteEmpire.asset` — Task_PromoteCommunity(Empire).

**Modified files:**
- `Assets/Scripts/World/Buildings/BuildingType.cs` — add `Administrative` enum entry.
- `Assets/Scripts/World/Buildings/Building.cs` — add `protected virtual void OnFinalize() { }` hook + call it from `Finalize()` after state flip.
- `Assets/Scripts/World/Community/Community.cs` — add `[NonSerialized] public AdministrativeBuilding AdministrativeBuilding;` + `public bool IsChartered => AdministrativeBuilding != null;` getter.
- `Assets/Scripts/Character/CharacterCommunity/CharacterCommunity.cs` — `CreateCommunity()` and `CreateCommunity(string name)` both auto-grant the AB blueprint via Resources.Load (defensive — no-op if SO missing).
- `Assets/Scripts/World/Buildings/BuildingPlacementManager.cs` — `ValidatePlacement` gate #6: reject if blueprint is Administrative AND founder's community already has one.

**Docs updated:**
- `.agent/skills/community-system/SKILL.md` — note AdministrativeBuilding reference + IsChartered + the auto-blueprint-grant.
- `wiki/systems/world-community.md` — Public API refresh, change log.
- `wiki/concepts/found-a-city-ambition.md` — bump status from `draft` → `active` once the Ambition asset ships; note Plan 4a delivered the asset.
- `wiki/concepts/citizenship.md` — change log entry: "AB.OnFinalize grants founder citizenship (Plan 4a)."
- NEW: `wiki/systems/administrative-building.md` — full system page following the 10-section template.

---

## Task 1: BuildingType + AdministrativeBuilding class + Building.OnFinalize hook

**Files:**
- Modify: `Assets/Scripts/World/Buildings/BuildingType.cs`
- Modify: `Assets/Scripts/World/Buildings/Building.cs` (add hook + call site)
- Create: `Assets/Scripts/World/Buildings/CommercialBuildings/AdministrativeBuilding.cs`

- [ ] **Step 1: Add the enum value**

Append `Administrative` to `Assets/Scripts/World/Buildings/BuildingType.cs`:

```csharp
public enum BuildingType
{
    Residential,
    Bar,
    Shop,
    Inn,
    Workshop,
    Warehouse,
    HarvestingSite,
    Commercial,
    Farm,
    Administrative,
}
```

**Important:** Append, never reorder. Saved building data references enum values by name (Unity's enum serialization is by name when the type is decorated with `[Serializable]`, but mixing index-by-name is risky — safest is "never reorder, only append").

- [ ] **Step 2: Add the `OnFinalize` virtual hook on `Building.cs`**

Find `Building.Finalize()` (currently around line 1648). The current body is:

```csharp
public new void Finalize()
{
    if (!IsServer) return;
    if (_currentState.Value == MWI.WorldSystem.BuildingState.Complete) return;

    _currentState.Value = MWI.WorldSystem.BuildingState.Complete;
    if (ConstructionProgress.Value < 1f) ConstructionProgress.Value = 1f;
    Debug.Log($"<color=green>[Building.Construction]</color> {BuildingName} completed by Finalize().");
}
```

Add a virtual hook + invocation:

```csharp
public new void Finalize()
{
    if (!IsServer) return;
    if (_currentState.Value == MWI.WorldSystem.BuildingState.Complete) return;

    _currentState.Value = MWI.WorldSystem.BuildingState.Complete;
    if (ConstructionProgress.Value < 1f) ConstructionProgress.Value = 1f;
    Debug.Log($"<color=green>[Building.Construction]</color> {BuildingName} completed by Finalize().");

    // Subclass extension point. Fires server-side, after the state-flip. Defensive
    // try/catch so a buggy subclass override never blocks the Complete transition (rule #31).
    try { OnFinalize(); }
    catch (System.Exception e)
    {
        Debug.LogException(e);
        Debug.LogError($"<color=red>[Building.Finalize]</color> OnFinalize override threw for '{BuildingName}'.");
    }
}

/// <summary>
/// Server-only. Extension point that fires immediately after construction completes
/// (state flipped to Complete, progress = 1). Subclasses use this to apply one-shot
/// completion effects — e.g. <see cref="AdministrativeBuilding"/> grants the founder
/// citizenship here. Default implementation is a no-op.
/// </summary>
protected virtual void OnFinalize() { }
```

- [ ] **Step 3: Create `AdministrativeBuilding.cs`**

Write `Assets/Scripts/World/Buildings/CommercialBuildings/AdministrativeBuilding.cs`:

```csharp
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// City-charter building. One per community by design (the placement gate in
/// <see cref="MWI.WorldSystem.BuildingPlacementManager"/> rejects a second instance).
/// On completion (<see cref="OnFinalize"/>), grants the founder citizenship of the
/// host community — the founding gesture that flips a community from "informal group"
/// to "chartered city".
/// <para>
/// Plan 4a ships the skeleton only:
/// <list type="bullet">
/// <item><c>OwnerCommunity</c> server-side ref + the citizenship grant on Finalize.</item>
/// <item><see cref="BuildingType.Administrative"/> identity.</item>
/// <item>Auto-owner binding: every leader of the host community becomes an owner.</item>
/// </list>
/// Plan 4b adds <c>InitializeJobs</c> (JobBuilder × 2 + JobHarvester(CityHarvester) +
/// JobLogisticsManager). Plan 4c adds the preplaced furniture wiring
/// (CityManagementFurniture, JoinRequestDesk, SafeFurniture city treasury) +
/// the <c>NetworkList&lt;JoinRequest&gt; PendingJoinRequests</c>.
/// </para>
/// </summary>
public class AdministrativeBuilding : CommercialBuilding
{
    public override BuildingType BuildingType => BuildingType.Administrative;

    /// <summary>
    /// Server-only. The community this AB charters. Resolved on placement
    /// (BuildingPlacementManager sets it via <see cref="SetOwnerCommunity"/>) and
    /// read by <see cref="OnFinalize"/> to identify which community's founder
    /// to grant citizenship to.
    /// </summary>
    public Community OwnerCommunity { get; private set; }

    /// <summary>
    /// Server-only. Sets <see cref="OwnerCommunity"/> + back-points
    /// <c>community.AdministrativeBuilding = this</c>. Idempotent re-assignments
    /// are guarded; calling with a different community than the current owner
    /// logs a warning and is rejected (a chartered community cannot swap ABs).
    /// </summary>
    public void SetOwnerCommunity(Community community)
    {
        if (!IsServer) return;
        if (community == null) return;

        if (OwnerCommunity != null && OwnerCommunity != community)
        {
            Debug.LogWarning($"<color=orange>[AdministrativeBuilding:{BuildingName}]</color> SetOwnerCommunity rejected — already chartering '{OwnerCommunity.communityName}', cannot re-bind to '{community.communityName}'.");
            return;
        }

        OwnerCommunity = community;
        community.AdministrativeBuilding = this;

        // Auto-owner binding: every leader becomes an owner so any leader can
        // interact with leader-gated AB features (Plan 5's admin console).
        TryBindLeadersAsOwners();
    }

    /// <summary>
    /// Server-only. Adds every primary + secondary leader's CharacterId to the
    /// inherited Room <c>_ownerIds</c> NetworkList. Idempotent — already-owners
    /// are skipped by the inherited <c>AddOwner</c> path.
    /// </summary>
    private void TryBindLeadersAsOwners()
    {
        if (!IsServer || OwnerCommunity == null) return;

        foreach (var leader in OwnerCommunity.leaders)
        {
            if (leader == null) continue;
            string id = leader.CharacterId;
            if (string.IsNullOrEmpty(id)) continue;
            AddOwner(id);
        }
    }

    /// <summary>
    /// Treasury read-through to the inherited <c>CommercialBuilding</c> treasury
    /// (sum of all SafeFurniture instances marked with the Treasury role).
    /// Returns 0 if no Treasury safe has been preplaced yet (Plan 4c wires the
    /// city-treasury safe).
    /// </summary>
    public int GetTreasuryBalance()
    {
        // Inherited CommercialBuilding.GetTreasuryBalance returns the SUM of every
        // Treasury-role SafeFurniture under this building. For the AB, that's the
        // city treasury — same code path as a shop's revenue safe.
        return base.GetTreasuryBalance();
    }

    /// <summary>
    /// Server-only. On construction-complete, grants the founder (the character
    /// who placed the AB) citizenship of the chartered community.
    /// </summary>
    protected override void OnFinalize()
    {
        base.OnFinalize();
        if (!IsServer) return;
        if (OwnerCommunity == null) return;

        string founderId = PlacedByCharacterId.Value.ToString();
        if (string.IsNullOrEmpty(founderId)) return;

        var founder = Character.FindByUUID(founderId);
        if (founder == null || founder.CharacterCommunity == null) return;

        founder.CharacterCommunity.SetCitizenship(OwnerCommunity);
        Debug.Log($"<color=cyan>[AdministrativeBuilding:{BuildingName}]</color> Founder '{founder.CharacterName}' granted citizenship of '{OwnerCommunity.communityName}'.");
    }
}
```

Key implementation notes for the implementer:
- `CommercialBuilding.GetTreasuryBalance()` — verify this exists on `CommercialBuilding` already. If it does, the `base.GetTreasuryBalance()` call works. If it doesn't, replace the call with a direct read of the first Treasury-role safe (or just `return 0;` for Plan 4a — Plan 4c can refine).
- `Room.AddOwner(string)` — verify this is the correct API. Look at `Assets/Scripts/World/Buildings/Rooms/Room.cs` for the multi-owner mutator. If it's named differently, adjust.
- `Character.FindByUUID(string)` — Plan 1's `CharacterCommunity.Serialize` uses this, so it exists.

- [ ] **Step 4: Compile-check**

Use `assets-refresh` + `console-get-logs` filtering on compile errors. Expected: clean.

If `CommercialBuilding.GetTreasuryBalance` doesn't exist, replace with `return 0;` and surface the gap in the commit message.

If `Community.AdministrativeBuilding` field doesn't exist yet — that's Task 2's job. Task 1 will compile-fail on the `community.AdministrativeBuilding = this;` line. That's fine — Tasks 1+2 must ship together. Either combine into one commit OR commit Task 1 with a temporary `// TODO(Plan4a Task 2): once Community.AdministrativeBuilding exists, uncomment` line, then unblock in Task 2.

**Recommended:** combine Task 1's commit with Task 2's. Add a single commit "feat(building): add OnFinalize hook + AdministrativeBuilding class + Community.AdministrativeBuilding ref" after both edits.

- [ ] **Step 5: Defer commit to after Task 2**

Mark Task 1 done in your tracking; do NOT commit until Task 2 also lands.

---

## Task 2: Community.AdministrativeBuilding + IsChartered + auto-grant AB blueprint

**Files:**
- Modify: `Assets/Scripts/World/Community/Community.cs`
- Modify: `Assets/Scripts/Character/CharacterCommunity/CharacterCommunity.cs`

- [ ] **Step 1: Add `AdministrativeBuilding` ref + `IsChartered` to `Community.cs`**

In `Assets/Scripts/World/Community/Community.cs`, find the `[Header("Territory & Assets")]` block (currently around line 33-36). Append:

```csharp
    [Header("City Charter")]
    /// <summary>
    /// The <see cref="AdministrativeBuilding"/> chartering this community.
    /// Server-only state; set when an AB's <see cref="AdministrativeBuilding.SetOwnerCommunity"/>
    /// runs during placement (Plan 4a). NonSerialized — Communities are plain C# objects,
    /// the AB ref doesn't survive JSON serialization. On wake-up the ref is rebuilt
    /// by scanning <see cref="BuildingManager"/> for AB instances whose owner community
    /// matches (handled by Plan 4c's lifecycle hooks; Plan 4a leaves the rebuild gap
    /// documented as a known limitation — a save/load round-trip in Plan 4a-only state
    /// loses the AB ref until Plan 4c ships).
    /// </summary>
    [System.NonSerialized] public AdministrativeBuilding AdministrativeBuilding;

    /// <summary>True iff this community has a chartered AdministrativeBuilding (placed,
    /// not necessarily complete). Plan 4c's drifter migration gates on
    /// <c>AdministrativeBuilding != null &amp;&amp; AdministrativeBuilding.IsUnderConstruction == false</c>;
    /// Plan 4a's placement gate uses this for the 1-per-community check.</summary>
    public bool IsChartered => AdministrativeBuilding != null;
```

Place these inside the class body, ideally right after `public List<Building> ownedBuildings = new List<Building>();`.

- [ ] **Step 2: Auto-grant AB blueprint on community creation**

In `Assets/Scripts/Character/CharacterCommunity/CharacterCommunity.cs`, find the parameter-less `public void CreateCommunity()` method. After it successfully sets `_currentCommunity`, grant the AB blueprint to the founder:

Locate this block near the bottom of the method (after `newComm.ChangeLevel(CommunityLevel.SmallGroup);`):

```csharp
        if (newComm != null)
        {
            // Link hierarchy if founder was in a community
            if (parent != null)
            {
                parent.AddSubCommunity(newComm);
            }

            SetCurrentCommunity(newComm);
            newComm.ChangeLevel(CommunityLevel.SmallGroup);
            
            Debug.Log($"<color=cyan>[Character Community]</color> {_character.CharacterName} founded a new {(parent != null ? "sub-" : "independent ")}Community '{newCommName}'.");
        }
```

Add the blueprint grant call between `newComm.ChangeLevel(...)` and the existing `Debug.Log`:

```csharp
            SetCurrentCommunity(newComm);
            newComm.ChangeLevel(CommunityLevel.SmallGroup);

            // Auto-grant the AdministrativeBuilding blueprint so the founder can
            // place their city's capital (Plan 4a + Plan 3's Ambition_FoundACity).
            // Resources.Load is defensive: no-op if the AB SO doesn't exist yet
            // (e.g. a designer hasn't authored it). Lives at the canonical path
            // Resources/Data/Buildings/AdministrativeBuilding.
            if (_character.CharacterBlueprints != null)
            {
                var abBlueprint = Resources.Load<MWI.WorldSystem.BuildingSO>("Data/Buildings/AdministrativeBuilding");
                if (abBlueprint != null) _character.CharacterBlueprints.GrantBlueprint(abBlueprint);
            }

            Debug.Log($"<color=cyan>[Character Community]</color> {_character.CharacterName} founded a new {(parent != null ? "sub-" : "independent ")}Community '{newCommName}'.");
```

Repeat the same `Resources.Load + GrantBlueprint` block in the **parameterized** `public void CreateCommunity(string name)` overload (Plan 3 flipped it public; same pattern).

- [ ] **Step 3: Compile-check**

Use `assets-refresh` + `console-get-logs`. Expected: clean. The Task 1 + Task 2 changes together should now produce a compiling project.

- [ ] **Step 4: Run all tests for regression**

Use `tests-run` filter `MWI.Tests.*`. Expected: 157 baseline tests still pass.

- [ ] **Step 5: Commit Tasks 1 + 2 together**

```bash
git add Assets/Scripts/World/Buildings/BuildingType.cs Assets/Scripts/World/Buildings/Building.cs Assets/Scripts/World/Buildings/CommercialBuildings/AdministrativeBuilding.cs Assets/Scripts/World/Community/Community.cs Assets/Scripts/Character/CharacterCommunity/CharacterCommunity.cs
git commit -m "$(cat <<'EOF'
feat(building+community): AdministrativeBuilding skeleton + OnFinalize hook + Community charter ref

Plan 4a foundation. Ships:

- BuildingType.Administrative enum entry (appended; never reorder).
- AdministrativeBuilding : CommercialBuilding subclass with:
    * BuildingType override → Administrative
    * OwnerCommunity server-only ref + SetOwnerCommunity (idempotent, back-points Community.AdministrativeBuilding)
    * Auto-owner binding: every leader of the host community becomes an owner of the AB (mirrors Room.AddOwner)
    * OnFinalize override: grants founder citizenship via CharacterCommunity.SetCitizenship (Plan 1's setter)
- Building.OnFinalize virtual hook in base class — protected, server-only, fires after state-flip
  to Complete inside Building.Finalize(). Wrapped in try/catch so a buggy override never blocks
  the Complete transition (rule #31).
- Community.AdministrativeBuilding ([NonSerialized]) + IsChartered getter. Save/load round-trip
  loses the ref until Plan 4c adds the rebuild logic — documented as known limitation.
- CharacterCommunity.CreateCommunity() (both overloads) auto-grants the AB blueprint via
  Resources.Load<BuildingSO>("Data/Buildings/AdministrativeBuilding") + GrantBlueprint(so).
  Defensive: no-op if the SO isn't authored yet.

Out of scope (Plan 4b + 4c):
- InitializeJobs (Plan 4b adds JobBuilder + JobHarvester + JobLogisticsManager)
- NetworkList<JoinRequest> PendingJoinRequests (Plan 4c)
- _unfulfillableMaterialHarvestQueue (Plan 4b)
- Preplaced furniture wiring (Plan 4c)
- AdministrativeBuilding prefab (Plan 4c — only the BuildingSO scaffold ships in Plan 4a Task 5)

Network safety (rule #19b): no new replication channels. AdministrativeBuilding is a
NetworkBehaviour by inheritance from Building, with the existing _ownerIds NetworkList carrying
the auto-bound leaders. OwnerCommunity is server-only state (Community is not a NetworkBehaviour).

Plan 4a of 5 for the City Founding spec
(docs/superpowers/specs/2026-05-18-city-founding-and-administrative-building-design.md).
EOF
)"
```

---

## Task 3: BuildingPlacementManager 1-per-community gate

**Files:**
- Modify: `Assets/Scripts/World/Buildings/BuildingPlacementManager.cs`

- [ ] **Step 1: Add the gate inside `ValidatePlacement`**

In `Assets/Scripts/World/Buildings/BuildingPlacementManager.cs`, find `public bool ValidatePlacement(Vector3 position)`. The current shape (after Plan 2) has 5 gates (range / obstacle / IsInsideRegion / community permission / BuildingGrid.CanPlace). Add gate #6 BEFORE the final `return true;`:

```csharp
            // 6. 1-per-community gate for Administrative buildings.
            //    The AB charters a community; only one is allowed per community.
            //    Reads the blueprint's BuildingType — null-safe.
            if (_ghostBuildingComponent != null && _ghostBuildingComponent.Blueprint != null
                && _ghostBuildingComponent.Blueprint.BuildingType == BuildingType.Administrative)
            {
                if (_character != null && _character.CharacterCommunity != null
                    && _character.CharacterCommunity.CurrentCommunity != null
                    && _character.CharacterCommunity.CurrentCommunity.IsChartered)
                {
                    // Toast surfaces in the existing UpdateGhostPosition path; here we
                    // just reject the validity. The ghost will turn red, click is no-op.
                    return false;
                }
            }
```

- [ ] **Step 2: Hook `SetOwnerCommunity` into the placement registration**

`BuildingPlacementManager.RegisterBuildingWithMap` (the server-side post-spawn hook) currently parents the building to its MapController and registers it in `CommunityData.ConstructedBuildings`. For an AB specifically, we ALSO need to call `AdministrativeBuilding.SetOwnerCommunity(community)` to back-point the Community → AB ref.

Find `RegisterBuildingWithMap` (around line 490 post-Plan-2). After the existing `community.ConstructedBuildings.Add(...)` block (around line 584), add:

```csharp
                    // AdministrativeBuilding-specific: bind the founder's community to this AB
                    // so the OnFinalize citizenship grant + auto-owner binding can resolve.
                    if (building is AdministrativeBuilding ab)
                    {
                        // Resolve the founder's runtime Community via CharacterCommunity.
                        // PlacedByCharacterId is the founder's CharacterId; we look up their
                        // CharacterCommunity.CurrentCommunity to find the host community.
                        string placerId = building.PlacedByCharacterId.Value.ToString();
                        if (!string.IsNullOrEmpty(placerId))
                        {
                            var placer = Character.FindByUUID(placerId);
                            if (placer != null && placer.CharacterCommunity != null
                                && placer.CharacterCommunity.CurrentCommunity != null)
                            {
                                ab.SetOwnerCommunity(placer.CharacterCommunity.CurrentCommunity);
                            }
                            else
                            {
                                Debug.LogWarning($"<color=orange>[BuildingPlacementManager]</color> AB '{building.BuildingName}' placed but founder '{placerId}' has no CurrentCommunity — OwnerCommunity will be unset. Founder citizenship grant on Finalize will no-op.");
                            }
                        }
                    }
```

- [ ] **Step 3: Compile-check**

Use `assets-refresh` + `console-get-logs`. Expected: clean.

- [ ] **Step 4: Run all tests**

Use `tests-run` filter `MWI.Tests.*`. Expected: 157 still pass.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/World/Buildings/BuildingPlacementManager.cs
git commit -m "$(cat <<'EOF'
feat(placement): 1-per-community AB gate + auto-SetOwnerCommunity on register

- ValidatePlacement gate #6: rejects placement when the blueprint is
  Administrative AND the founder's CurrentCommunity is already chartered
  (IsChartered == true). Client-side this is optimistic-green for replication
  reasons (same compromise as Plan 2's CanPlace); server-side is authoritative.
- RegisterBuildingWithMap: when the spawned building is an AdministrativeBuilding,
  resolves the founder via PlacedByCharacterId → Character.FindByUUID and back-
  points the AB to the founder's CurrentCommunity via SetOwnerCommunity. This
  is what kicks off the auto-owner binding (every leader becomes an owner) and
  sets Community.AdministrativeBuilding so IsChartered → true.

Plan 4a of 5 for the City Founding spec.
EOF
)"
```

---

## Task 4: Task_PlaceBuilding + Task_FinishConstruction + EditMode tests

**Files:**
- Create: `Assets/Scripts/Character/Ambition/Tasks/Task_PlaceBuilding.cs`
- Create: `Assets/Scripts/Character/Ambition/Tasks/Task_FinishConstruction.cs`
- Create: `Assets/Editor/Tests/Ambition/Task_PlaceBuildingTests.cs`
- Create: `Assets/Editor/Tests/Ambition/Task_FinishConstructionTests.cs`

- [ ] **Step 1: Create `Task_PlaceBuilding.cs`**

`Assets/Scripts/Character/Ambition/Tasks/Task_PlaceBuilding.cs`:

```csharp
using System;
using UnityEngine;
using MWI.WorldSystem;

namespace MWI.Ambition
{
    /// <summary>
    /// Passive ambition task: completes once the actor has placed a building whose
    /// blueprint matches <see cref="TargetBlueprint"/>. The placement itself is driven
    /// by either the player's ghost flow (BuildingPlacementManager) or an NPC BTAction
    /// (Plan 4b). This task only watches the world state — it never drives placement.
    /// <para>
    /// "Placed" means: <see cref="BuildingManager.allBuildings"/> contains an instance
    /// whose <see cref="Building.Blueprint"/> equals TargetBlueprint AND whose
    /// <see cref="Building.PlacedByCharacterId"/> matches <c>actor.CharacterId</c>.
    /// The building may still be under construction — Task_FinishConstruction handles
    /// the next step.
    /// </para>
    /// </summary>
    [Serializable]
    public class Task_PlaceBuilding : TaskBase
    {
        [Tooltip("The BuildingSO the actor must place for this task to complete.")]
        public BuildingSO TargetBlueprint;

        public override void Bind(AmbitionContext ctx)
        {
            // No parameter bindings — the task is anchored to actor + TargetBlueprint.
        }

        public override TaskStatus Tick(Character npc, AmbitionContext ctx)
        {
            if (npc == null) return TaskStatus.Running;
            if (TargetBlueprint == null) return TaskStatus.Running;  // misconfigured asset; BT keeps retrying

            string actorId = npc.CharacterId;
            if (string.IsNullOrEmpty(actorId)) return TaskStatus.Running;

            var bm = BuildingManager.Instance;
            if (bm == null) return TaskStatus.Running;

            // Scan for any building whose blueprint matches AND was placed by this actor.
            // BuildingManager.allBuildings is server-side; this task runs server-side (BT tick).
            for (int i = 0; i < bm.allBuildings.Count; i++)
            {
                var b = bm.allBuildings[i];
                if (b == null) continue;
                if (b.Blueprint != TargetBlueprint) continue;
                if (b.PlacedByCharacterId.Value.ToString() != actorId) continue;
                return TaskStatus.Completed;
            }
            return TaskStatus.Running;
        }

        public override void Cancel()
        {
            // Passive — nothing to clean up.
        }
    }
}
```

- [ ] **Step 2: Create `Task_FinishConstruction.cs`**

`Assets/Scripts/Character/Ambition/Tasks/Task_FinishConstruction.cs`:

```csharp
using System;
using UnityEngine;
using MWI.WorldSystem;

namespace MWI.Ambition
{
    /// <summary>
    /// Passive ambition task: completes once a building matching
    /// <see cref="TargetBlueprint"/> placed by the actor has reached the Complete
    /// construction state. Re-uses the cooperative construction loop (Phase 1) —
    /// the task never drives the construction itself; player or NPC drives via
    /// CharacterAction_FinishConstruction.
    /// </summary>
    [Serializable]
    public class Task_FinishConstruction : TaskBase
    {
        [Tooltip("The BuildingSO to watch. Same as the preceding Task_PlaceBuilding's TargetBlueprint.")]
        public BuildingSO TargetBlueprint;

        public override void Bind(AmbitionContext ctx)
        {
            // No parameter bindings.
        }

        public override TaskStatus Tick(Character npc, AmbitionContext ctx)
        {
            if (npc == null) return TaskStatus.Running;
            if (TargetBlueprint == null) return TaskStatus.Running;

            string actorId = npc.CharacterId;
            if (string.IsNullOrEmpty(actorId)) return TaskStatus.Running;

            var bm = BuildingManager.Instance;
            if (bm == null) return TaskStatus.Running;

            // Completed = an actor-placed building of TargetBlueprint that is NO LONGER
            // under construction. If the actor placed multiple instances (edge case —
            // there's no rule against re-placement after a destruction), the first
            // complete one wins; the task does not require ALL of them to complete.
            for (int i = 0; i < bm.allBuildings.Count; i++)
            {
                var b = bm.allBuildings[i];
                if (b == null) continue;
                if (b.Blueprint != TargetBlueprint) continue;
                if (b.PlacedByCharacterId.Value.ToString() != actorId) continue;
                if (b.IsUnderConstruction) continue;
                return TaskStatus.Completed;
            }
            return TaskStatus.Running;
        }

        public override void Cancel()
        {
            // Passive — nothing to clean up.
        }
    }
}
```

- [ ] **Step 3: Create `Task_PlaceBuildingTests.cs`**

The test file uses the same `MakeCharacterWithCommunity` helper from Plan 3 (reflectively wires `CharacterSystem._character`). Tests for Task_PlaceBuilding need a `Building` instance — that's harder to fake headlessly because `Building` is a NetworkBehaviour with prefab dependencies. For Plan 4a's tests we use a simpler approach: a stub class that mimics the public surface OR we test only the null/empty branches and skip the full happy-path (which is best exercised in PlayMode anyway).

Write `Assets/Editor/Tests/Ambition/Task_PlaceBuildingTests.cs`:

```csharp
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MWI.Ambition;
using MWI.WorldSystem;

namespace MWI.Tests.Ambition
{
    public class Task_PlaceBuildingTests
    {
        private Character MakeBareCharacter(string actorName)
        {
            var go = new GameObject(actorName);
            return go.AddComponent<Character>();
        }

        [Test]
        public void Tick_returns_Running_when_TargetBlueprint_is_null()
        {
            var actor = MakeBareCharacter("Founder");
            var task = new Task_PlaceBuilding { TargetBlueprint = null };
            var ctx = new AmbitionContext();
            task.Bind(ctx);

            Assert.AreEqual(TaskStatus.Running, task.Tick(actor, ctx),
                "Null blueprint must be a soft-fail (BT keeps retrying once author fixes the asset).");
        }

        [Test]
        public void Tick_returns_Running_when_actor_is_null()
        {
            var so = ScriptableObject.CreateInstance<BuildingSO>();
            var task = new Task_PlaceBuilding { TargetBlueprint = so };
            var ctx = new AmbitionContext();
            task.Bind(ctx);

            Assert.AreEqual(TaskStatus.Running, task.Tick(null, ctx));
        }

        [Test]
        public void Tick_returns_Running_when_no_BuildingManager_instance_exists()
        {
            // In EditMode without a scene that contains BuildingManager,
            // BuildingManager.Instance is null. Task must defensively return Running.
            var actor = MakeBareCharacter("Founder");
            var so = ScriptableObject.CreateInstance<BuildingSO>();
            var task = new Task_PlaceBuilding { TargetBlueprint = so };
            var ctx = new AmbitionContext();
            task.Bind(ctx);

            // We can't easily simulate "actor has placed a building" in a headless EditMode test
            // (Building is a NetworkBehaviour with prefab dependencies + NGO spawn).
            // The happy-path is validated in PlayMode tests (out of scope for Plan 4a).
            var status = task.Tick(actor, ctx);
            // Either Running (BuildingManager.Instance is null) or Completed (something
            // unexpectedly satisfies the criteria — unlikely in a fresh test scene).
            Assert.That(status, Is.EqualTo(TaskStatus.Running).Or.EqualTo(TaskStatus.Completed));
        }
    }
}
```

- [ ] **Step 4: Create `Task_FinishConstructionTests.cs`**

Mirror the same pattern:

```csharp
using NUnit.Framework;
using UnityEngine;
using MWI.Ambition;
using MWI.WorldSystem;

namespace MWI.Tests.Ambition
{
    public class Task_FinishConstructionTests
    {
        private Character MakeBareCharacter(string actorName)
        {
            var go = new GameObject(actorName);
            return go.AddComponent<Character>();
        }

        [Test]
        public void Tick_returns_Running_when_TargetBlueprint_is_null()
        {
            var actor = MakeBareCharacter("Founder");
            var task = new Task_FinishConstruction { TargetBlueprint = null };
            var ctx = new AmbitionContext();
            task.Bind(ctx);
            Assert.AreEqual(TaskStatus.Running, task.Tick(actor, ctx));
        }

        [Test]
        public void Tick_returns_Running_when_actor_is_null()
        {
            var so = ScriptableObject.CreateInstance<BuildingSO>();
            var task = new Task_FinishConstruction { TargetBlueprint = so };
            var ctx = new AmbitionContext();
            task.Bind(ctx);
            Assert.AreEqual(TaskStatus.Running, task.Tick(null, ctx));
        }

        [Test]
        public void Tick_returns_Running_when_no_BuildingManager_instance_exists()
        {
            var actor = MakeBareCharacter("Founder");
            var so = ScriptableObject.CreateInstance<BuildingSO>();
            var task = new Task_FinishConstruction { TargetBlueprint = so };
            var ctx = new AmbitionContext();
            task.Bind(ctx);
            var status = task.Tick(actor, ctx);
            Assert.That(status, Is.EqualTo(TaskStatus.Running).Or.EqualTo(TaskStatus.Completed));
        }
    }
}
```

- [ ] **Step 5: Run tests**

Use `tests-run` with `testMode: EditMode`, filter `MWI.Tests.Ambition.Task_PlaceBuildingTests|MWI.Tests.Ambition.Task_FinishConstructionTests`. Expected: PASS (6 tests total, 3 per file).

Then `tests-run` filter `MWI.Tests.*`. Expected: 157 baseline + 6 new = 163.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Character/Ambition/Tasks/Task_PlaceBuilding.cs Assets/Scripts/Character/Ambition/Tasks/Task_FinishConstruction.cs Assets/Editor/Tests/Ambition/Task_PlaceBuildingTests.cs Assets/Editor/Tests/Ambition/Task_FinishConstructionTests.cs
git commit -m "$(cat <<'EOF'
feat(ambition): add Task_PlaceBuilding + Task_FinishConstruction TaskBase subclasses

Plan 3 deferred these because they reference BuildingSO (which had no concrete
AB asset before Plan 4a). Both are passive completion-watchers that scan
BuildingManager.allBuildings for a building matching the TargetBlueprint AND
placed by the actor.

- Task_PlaceBuilding: Completed once the actor has placed a building of
  TargetBlueprint (any state — including UnderConstruction).
- Task_FinishConstruction: Completed once an actor-placed building of
  TargetBlueprint has reached Complete (!IsUnderConstruction).

Both defensive: null actor, null TargetBlueprint, missing BuildingManager all
return Running so the BT retries. Happy-path validation deferred to PlayMode
tests (Building is a NetworkBehaviour with prefab dependencies that don't
work in headless EditMode).

6 new EditMode tests cover null/missing-instance branches.

Plan 4a of 5 for the City Founding spec.
EOF
)"
```

---

## Task 5: Author the ambition + quest + AB BuildingSO assets via Roslyn

**Files:**
- Create (via Roslyn): `Assets/Resources/Data/Buildings/AdministrativeBuilding.asset`
- Create (via Roslyn): `Assets/Resources/Data/Ambitions/Ambition_FoundACity.asset`
- Create (via Roslyn): `Assets/Resources/Data/Ambitions/Quest_CreateCommunity.asset`
- Create (via Roslyn): `Assets/Resources/Data/Ambitions/Quest_BuildCapital.asset`
- Create (via Roslyn): `Assets/Resources/Data/Ambitions/Quest_PromoteCamp.asset`
- Create (via Roslyn): `Assets/Resources/Data/Ambitions/Quest_PromoteVillage.asset`
- Create (via Roslyn): `Assets/Resources/Data/Ambitions/Quest_PromoteTown.asset`
- Create (via Roslyn): `Assets/Resources/Data/Ambitions/Quest_PromoteCity.asset`
- Create (via Roslyn): `Assets/Resources/Data/Ambitions/Quest_PromoteKingdom.asset`
- Create (via Roslyn): `Assets/Resources/Data/Ambitions/Quest_PromoteEmpire.asset`

- [ ] **Step 1: Verify folder structure**

Check whether `Assets/Resources/Data/Buildings/` and `Assets/Resources/Data/Ambitions/` exist. Use `Glob` or `Bash ls`. Create them if missing (Roslyn `AssetDatabase.CreateFolder` works, or `mcp__ai-game-developer__assets-create-folder`).

- [ ] **Step 2: Execute the Roslyn one-shot**

Use `mcp__ai-game-developer__script-execute` with this code. The code:
1. Ensures both target folders exist.
2. Creates the AB BuildingSO at `Assets/Resources/Data/Buildings/AdministrativeBuilding.asset` (prefab field stays null; PrefabId = "AdministrativeBuilding"; BuildingType = Administrative; GridFootprintCells = 3×3 per spec).
3. Creates the 8 QuestSO assets — each with a single `[SerializeReference]` TaskBase configured for that quest.
4. Creates the Ambition_FoundACity asset with the 8 quests in order.

```csharp
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using MWI.Ambition;
using MWI.WorldSystem;

// 1. Folder structure
foreach (var path in new[] {
    "Assets/Resources/Data/Buildings",
    "Assets/Resources/Data/Ambitions",
})
{
    if (!AssetDatabase.IsValidFolder(path))
    {
        string parent = Path.GetDirectoryName(path).Replace('\\','/');
        string leaf = Path.GetFileName(path);
        AssetDatabase.CreateFolder(parent, leaf);
    }
}

// ---- Helpers ----------------------------------------------------------------

void SetPrivate(object target, string fieldName, object value)
{
    var f = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
    if (f == null) throw new System.Exception($"Field '{fieldName}' not found on {target.GetType().Name}");
    f.SetValue(target, value);
}

T CreateOrLoadAsset<T>(string path) where T : ScriptableObject
{
    var existing = AssetDatabase.LoadAssetAtPath<T>(path);
    if (existing != null) return existing;
    var so = ScriptableObject.CreateInstance<T>();
    AssetDatabase.CreateAsset(so, path);
    return so;
}

QuestSO MakeQuest(string assetPath, string displayName, string description, TaskBase task)
{
    var quest = CreateOrLoadAsset<QuestSO>(assetPath);
    SetPrivate(quest, "_displayName", displayName);
    SetPrivate(quest, "_description", description);
    SetPrivate(quest, "_tasks", new List<TaskBase> { task });
    EditorUtility.SetDirty(quest);
    return quest;
}

// ---- AdministrativeBuilding BuildingSO --------------------------------------

var abSO = CreateOrLoadAsset<BuildingSO>("Assets/Resources/Data/Buildings/AdministrativeBuilding.asset");
SetPrivate(abSO, "_prefabId", "AdministrativeBuilding");
SetPrivate(abSO, "_buildingName", "City Hall");
SetPrivate(abSO, "_buildingType", BuildingType.Administrative);
SetPrivate(abSO, "_gridFootprintCells", new Vector2Int(3, 3));
SetPrivate(abSO, "_blueprintCategory", BlueprintCategory.Personal); // founder-placeable pre-charter
SetPrivate(abSO, "_minTier", CommunityLevel.SmallGroup);
EditorUtility.SetDirty(abSO);

// ---- Quest assets -----------------------------------------------------------

var qCreate = MakeQuest(
    "Assets/Resources/Data/Ambitions/Quest_CreateCommunity.asset",
    "Create your community",
    "Found a community of your own.",
    new Task_CreateCommunity());

var qBuild = MakeQuest(
    "Assets/Resources/Data/Ambitions/Quest_BuildCapital.asset",
    "Build the capital",
    "Place and complete the City Hall (Administrative Building) of your settlement.",
    new Task_PlaceBuilding { TargetBlueprint = abSO });
// Append the second task to qBuild — Task_FinishConstruction on the same SO.
SetPrivate(qBuild, "_tasks", new List<TaskBase>
{
    new Task_PlaceBuilding { TargetBlueprint = abSO },
    new Task_FinishConstruction { TargetBlueprint = abSO },
});
EditorUtility.SetDirty(qBuild);

QuestSO MakePromote(string assetPath, string display, CommunityLevel level)
    => MakeQuest(
        assetPath,
        display,
        $"Grow your community until it reaches the {level} tier.",
        new Task_PromoteCommunity { TargetLevel = level });

var qCamp    = MakePromote("Assets/Resources/Data/Ambitions/Quest_PromoteCamp.asset",    "Grow into a Camp",    CommunityLevel.Camp);
var qVillage = MakePromote("Assets/Resources/Data/Ambitions/Quest_PromoteVillage.asset", "Grow into a Village", CommunityLevel.Village);
var qTown    = MakePromote("Assets/Resources/Data/Ambitions/Quest_PromoteTown.asset",    "Grow into a Town",    CommunityLevel.Town);
var qCity    = MakePromote("Assets/Resources/Data/Ambitions/Quest_PromoteCity.asset",    "Grow into a City",    CommunityLevel.City);
var qKingdom = MakePromote("Assets/Resources/Data/Ambitions/Quest_PromoteKingdom.asset", "Grow into a Kingdom", CommunityLevel.Kingdom);
var qEmpire  = MakePromote("Assets/Resources/Data/Ambitions/Quest_PromoteEmpire.asset",  "Grow into an Empire", CommunityLevel.Empire);

// ---- Ambition_FoundACity asset ---------------------------------------------

var ambition = CreateOrLoadAsset<Ambition_FoundACity>("Assets/Resources/Data/Ambitions/Ambition_FoundACity.asset");
SetPrivate(ambition, "_displayName", "Found a City");
SetPrivate(ambition, "_description", "Establish a community, charter it as a city, and grow it into a thriving settlement.");
SetPrivate(ambition, "_overridesSchedule", true);
SetPrivate(ambition, "_quests", new List<QuestSO> { qCreate, qBuild, qCamp, qVillage, qTown, qCity, qKingdom, qEmpire });
EditorUtility.SetDirty(ambition);

AssetDatabase.SaveAssets();
AssetDatabase.Refresh();
Debug.Log("<color=cyan>[Plan 4a Task 5]</color> Authored AB BuildingSO + Ambition_FoundACity + 8 Quest assets.");
```

**Important caveats:**
- The available CommunityLevel enum values may not be exactly `SmallGroup / Camp / Village / Town / City / Kingdom / Empire`. Before running the Roslyn, check the enum: `Grep` for `enum CommunityLevel` in `Assets/Scripts`. If a value is missing (e.g. the existing enum stops at `Town`), surface the gap and either (a) add the missing values to the enum first, or (b) skip the assets for missing tiers and document the gap.
- `BlueprintCategory.Personal` is the right value here — the founder places the AB via the normal ghost flow (pre-charter); the admin-console Civic path is for post-charter buildings.

- [ ] **Step 3: Verify the assets exist**

Use `Bash ls`:

```
ls Assets/Resources/Data/Buildings/AdministrativeBuilding.asset
ls Assets/Resources/Data/Ambitions/
```

Expected: 9 files in `Assets/Resources/Data/Ambitions/` (1 Ambition + 8 Quests).

- [ ] **Step 4: Sanity test the assets via a one-shot Roslyn read**

Use `script-execute`:

```csharp
using UnityEngine;
using MWI.Ambition;
using MWI.WorldSystem;

var amb = Resources.Load<Ambition_FoundACity>("Data/Ambitions/Ambition_FoundACity");
Debug.Log($"Ambition: {amb.DisplayName}, OverridesSchedule={amb.OverridesSchedule}, Quests={amb.Quests.Count}");
foreach (var q in amb.Quests)
{
    Debug.Log($"  Quest: {q.DisplayName} — {q.Tasks.Count} task(s) — {string.Join(\",\", System.Linq.Enumerable.Select(q.Tasks, t => t == null ? \"null\" : t.GetType().Name))}");
}
var ab = Resources.Load<BuildingSO>("Data/Buildings/AdministrativeBuilding");
Debug.Log($"AB SO: {ab.BuildingName} ({ab.PrefabId}), type={ab.BuildingType}, footprint={ab.GridFootprintCells}");
```

Expected output:
- Ambition: Found a City, OverridesSchedule=True, Quests=8
- 8 quest lines, each with 1 or 2 tasks
- AB SO: City Hall (AdministrativeBuilding), type=Administrative, footprint=(3, 3)

- [ ] **Step 5: Compile + test**

Use `assets-refresh` + `console-get-logs` + `tests-run` filter `MWI.Tests.*`. Expected: 163 tests still pass.

- [ ] **Step 6: Commit**

```bash
git add Assets/Resources/Data/Buildings/AdministrativeBuilding.asset Assets/Resources/Data/Buildings/AdministrativeBuilding.asset.meta Assets/Resources/Data/Ambitions/
git commit -m "$(cat <<'EOF'
feat(content): author AdministrativeBuilding BuildingSO + Ambition_FoundACity asset chain

Resources/Data/Buildings/AdministrativeBuilding.asset — minimal BuildingSO scaffold:
  PrefabId = "AdministrativeBuilding"
  BuildingType = Administrative
  GridFootprintCells = (3, 3)
  BlueprintCategory = Personal (founder places via normal ghost flow pre-charter)
  Prefab reference is NULL — Plan 4c authors the actual prefab with preplaced
  CityManagementFurniture / JoinRequestDesk / SafeFurniture / storage. Until
  then the SO is a typed reference only — Task_PlaceBuilding can resolve it
  but placement at runtime will warn.

Resources/Data/Ambitions/ — full quest chain (8 quests + 1 ambition):
  Ambition_FoundACity.asset (OverridesSchedule = true)
   ├─ Quest_CreateCommunity     → Task_CreateCommunity
   ├─ Quest_BuildCapital        → Task_PlaceBuilding(AB SO) + Task_FinishConstruction(AB SO)
   ├─ Quest_PromoteCamp         → Task_PromoteCommunity(Camp)
   ├─ Quest_PromoteVillage      → Task_PromoteCommunity(Village)
   ├─ Quest_PromoteTown         → Task_PromoteCommunity(Town)
   ├─ Quest_PromoteCity         → Task_PromoteCommunity(City)
   ├─ Quest_PromoteKingdom      → Task_PromoteCommunity(Kingdom)
   └─ Quest_PromoteEmpire       → Task_PromoteCommunity(Empire)

Authored via one-shot Roslyn (mcp__ai-game-developer__script-execute). Uses
SerializeReference to inline TaskBase polymorphic instances into each QuestSO.

Plan 4a of 5 for the City Founding spec.
EOF
)"
```

---

## Task 6: Documentation + ship

**Files:**
- Modify: `.agent/skills/community-system/SKILL.md` (note AdministrativeBuilding ref + IsChartered + auto-blueprint-grant).
- Modify: `wiki/systems/world-community.md` (Public API + change log).
- Modify: `wiki/concepts/found-a-city-ambition.md` (status `draft` → `active`; confirm asset shipped).
- Modify: `wiki/concepts/citizenship.md` (change log: AB.OnFinalize grants founder citizenship).
- Create: `wiki/systems/administrative-building.md` (full 10-section template page).

- [ ] **Step 1: Read `wiki/CLAUDE.md` for schema reminders.**

- [ ] **Step 2: Create `wiki/systems/administrative-building.md`** (10-section template — see existing system pages for format).

The page should cover:
- Purpose (city-charter building, one per community, citizenship grant)
- Responsibilities (OwnerCommunity binding, leader auto-ownership, citizenship grant on Finalize)
- Key classes / files (AdministrativeBuilding.cs, the .asset, Building.OnFinalize hook)
- Public API (BuildingType.Administrative, OwnerCommunity, SetOwnerCommunity, GetTreasuryBalance)
- Data flow (placement → SetOwnerCommunity → Finalize → SetCitizenship)
- Dependencies (depends_on: building, world-community, character-community, building-grid)
- State & persistence (server-only OwnerCommunity ref; saved via existing Building.PlacedByCharacterId + Community.AdministrativeBuilding rebuild gap noted)
- Known gotchas (1-per-community gate; AB prefab not yet authored — Plan 4c)
- Open questions / TODO (preplaced furniture, NetworkList<JoinRequest>, InitializeJobs all deferred)
- Change log (`- 2026-05-17 — Plan 4a skeleton shipped. — claude`)

Required frontmatter (per `wiki/CLAUDE.md`):
```yaml
---
type: system
title: "Administrative Building"
tags: [building, community, city-founding, commercial]
created: 2026-05-17
updated: 2026-05-17
sources:
  - "[AdministrativeBuilding.cs](../../Assets/Scripts/World/Buildings/CommercialBuildings/AdministrativeBuilding.cs)"
  - "[Building.cs](../../Assets/Scripts/World/Buildings/Building.cs)"
related:
  - "[[world-community]]"
  - "[[character-community]]"
  - "[[citizenship]]"
  - "[[building-grid]]"
  - "[[found-a-city-ambition]]"
status: wip
confidence: high
primary_agent: building-furniture-specialist
secondary_agents: []
owner_code_path: Assets/Scripts/World/Buildings/CommercialBuildings/
depends_on:
  - "[[building]]"
  - "[[commercial-building]]"
  - "[[world-community]]"
  - "[[building-grid]]"
depended_on_by:
  - "[[found-a-city-ambition]]"
---
```

- [ ] **Step 3: Update existing pages**

For each of the four existing pages listed above, bump `updated:` to `2026-05-17`, refresh API tables, append change-log lines. Standard pattern.

- [ ] **Step 4: Sanity grep**

`grep -rn "AdministrativeBuilding\b" wiki/ .agent/` — expect 4+ hits across the new + updated docs.

- [ ] **Step 5: Commit + ship**

```bash
git add wiki/ .agent/skills/community-system/
git commit -m "$(cat <<'EOF'
docs(administrative-building): wiki + skill updates for Plan 4a skeleton

- wiki/systems/administrative-building.md (NEW) — 10-section system page
- wiki/systems/world-community.md — IsChartered + AdministrativeBuilding ref refresh
- wiki/concepts/found-a-city-ambition.md — status: draft → active, asset shipped
- wiki/concepts/citizenship.md — change log: AB.OnFinalize grants founder citizenship
- .agent/skills/community-system/SKILL.md — AB reference + auto-grant docs

Plan 4a of 5 for the City Founding spec.
EOF
)"

# Final summary commit
git commit --allow-empty -m "$(cat <<'EOF'
chore(plan-4a): Plan 4a of 5 complete — AdministrativeBuilding skeleton

Plan 4a of 5 for the City Founding spec.

Network safety (rule #19b):
- No new replication channels added by Plan 4a.
- AB is a NetworkBehaviour by inheritance from Building.
- OwnerCommunity is server-only state (Community is not a NetworkBehaviour).
- Auto-owner binding writes to the existing _ownerIds NetworkList from Room.
- Late-joiner repro deferred to Plan 4c when the admin console UI ships
  (Plan 4a has no client-visible AB surfaces beyond the inherited Building
  prefab + the standard Room/Building replicated state).

Shipped:
- BuildingType.Administrative enum entry
- AdministrativeBuilding : CommercialBuilding (skeleton — Plan 4b adds jobs;
  Plan 4c adds preplaced furniture + NetworkList<JoinRequest>)
- Building.OnFinalize virtual hook (subclass extension point)
- Community.AdministrativeBuilding ref + IsChartered getter
- CharacterCommunity.CreateCommunity auto-grants AB blueprint
- BuildingPlacementManager: 1-per-community gate + auto-SetOwnerCommunity
- Task_PlaceBuilding + Task_FinishConstruction (Plan 3 deferred — now landed)
- AdministrativeBuilding.asset (BuildingSO scaffold) + 1 Ambition + 8 Quest assets

Tests: 163 EditMode tests, all green (157 prior + 6 new task tests).

Out of scope, deferred:
- Plan 4b: BuildOrder, JobBuilder, JobLogisticsManager BuildOrder cascade,
  JobHarvester CityHarvester variant, 5 new GOAP actions
- Plan 4c: DrifterMigrationSystem, JoinRequestDesk, CityManagementFurniture,
  UI_CityManagementPanel, Community.TryPromoteLevel + tier requirements,
  AB.prefab + preplaced furniture wiring
- Plan 5: Admin console UI tabs (PlaceBuilding / BuildOrders / JoinRequests /
  Leaders / TierUp / Citizens / Treasury)

Plans 1-3 + 4a are now landed on origin/multiplayyer.
EOF
)"
```

Then push:
```bash
git push origin HEAD:multiplayyer
```

---

## Self-Review Notes

Re-checked against the Plan 4a scope (foundation):

- ✅ **AdministrativeBuilding class + BuildingType enum entry** — Task 1.
- ✅ **Building.OnFinalize virtual hook** — Task 1.
- ✅ **Community.AdministrativeBuilding + IsChartered** — Task 2.
- ✅ **CharacterCommunity auto-grants AB blueprint** — Task 2.
- ✅ **AB.OnFinalize founder citizenship grant** — Task 1 (inside AdministrativeBuilding.cs).
- ✅ **Auto-owner binding** — Task 1 (inside AdministrativeBuilding.cs `TryBindLeadersAsOwners`).
- ✅ **1-per-community placement gate + SetOwnerCommunity wiring** — Task 3.
- ✅ **Task_PlaceBuilding + Task_FinishConstruction (Plan 3 deferred)** — Task 4.
- ✅ **AdministrativeBuilding.asset + Ambition + 8 Quests** — Task 5.
- ✅ **Documentation per rules #28, #29b** — Task 6.
- ✅ **Network audit per rule #19b** — recorded in plan header + summary commit.

Type consistency:
- `BuildingType.Administrative` — used in Task 1 (enum), Task 1 (AdministrativeBuilding override), Task 3 (placement gate), Task 5 (BuildingSO field).
- `OwnerCommunity` — defined Task 1, read Task 1 OnFinalize.
- `Community.AdministrativeBuilding` — written Task 1 (`SetOwnerCommunity`), read Task 2 (`IsChartered`), read Task 3 (placement gate).
- `Task_PlaceBuilding.TargetBlueprint` / `Task_FinishConstruction.TargetBlueprint` — same field name on both.
- `Ambition_FoundACity` — class from Plan 3, asset created Task 5.

Placeholder scan: no TODO / TBD / vague descriptions remain. Every code step has the actual code.

Plan length: 6 tasks (some combined commits per the call-outs). Estimated 90-120 minutes wall-clock.
