# City Admin Console, Migration & Tier-Up Implementation Plan (Plan 4c)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the player-facing surface + the citizen lifecycle for the city-founding loop. After Plan 4c, a chartered city is fully playable end-to-end: the leader walks to the [[administrative-building|AdministrativeBuilding]]'s CityManagement furniture, opens the UI panel, picks a tier-unlocked Civic blueprint, RTS-places it on the BuildingGrid, the BuildOrder cascades through `JobLogisticsManager` (Plan 4b), `JobBuilder` constructs it (Plan 4b), and the building goes live. Daily drifters spawn at map edges and walk to the AB's `JoinRequestDesk` to request citizenship; the leader accepts/declines via the same UI panel. When population + buildings + treasury hit the tier-up thresholds, the leader promotes the community to the next level, unlocking new Civic blueprints.

**Scope (8 tasks):**

1. **`BuildingSO` placement additions** — `_gridFootprintCells`, `_blueprintCategory`, `_minTier`, `BlueprintCategory` enum.
2. **`CommunityTierRequirementsSO`** — new SO per `CommunityLevel` + bootstrap assets.
3. **`Community.TryPromoteLevel` + `AB.RequestPromoteLevelServerRpc`** — tier-up gate.
4. **AB Civic-placement server path** — `AdministrativeBuilding.PlaceCityBlueprintServerRpc` + `BuildingPlacementManager` integration + auto-`BuildOrder` creation.
5. **`DrifterMigrationSystem`** — daily ticker spawning drifter NPCs at map edges, each carrying a "go to AB JoinRequestDesk" BT intent.
6. **`JoinRequestDesk` furniture + `JoinRequest` struct + `AB.PendingJoinRequests` NetworkList + Accept/Decline ServerRpcs** — full applicant lifecycle.
7. **`CityManagementFurniture` + `UI_CityManagementPanel`** — multi-tab management window (TierUpTab + PlaceBuildingTab + JoinRequestsTab + read-only LeadersTab/TreasuryTab).
8. **AB.prefab authoring + final docs sync** — preplaced furniture wiring, change log entries on all five touched wiki pages, follow-up `wiki/systems/city-management.md` page, agent file updates.

**Tech Stack:** Unity 6.0 / NGO 2.x, C# 9. No new asmdef. Roslyn / MCP for AB.prefab + tier asset authoring (`mcp__ai-game-developer__script-execute`). New EditMode tests under `Assets/Editor/Tests/CityAdminConsole/` (Assembly-CSharp-Editor).

**Rules enforced throughout:** CLAUDE.md rules #1-#8 (think first; trace dependencies through the full city loop before editing), #9-#14 (SOLID — each new class single-purpose, NetVar/RPC plumbing factored through standard Building / Furniture abstractions), #15 (`_underscorePrefix`), #16 (events subscribed in `Awake`/`OnNetworkSpawn` unsubscribed in `OnDestroy`/`OnNetworkDespawn`), #18/#19/#19b (server-only state — full audit below; **late-joiner repro is mandatory before claiming any task done**), #22 (player↔NPC parity — every UI action has an NPC-callable server method equivalent; the player path is a UI shell on top of the server method), #28/#29/#29b (skill / agent / wiki sync at the end), #31 (defensive null-checks throughout — missing AB, missing community, missing leader, missing tier requirement), #33 (player input only in `PlayerController` — the RTS placement cursor is a `PlayerController` mode, not a free-floating input poll), #34 (per-frame allocation discipline — UI lists reuse row instances; tier-requirement scans cache; `Physics.OverlapNonAlloc` for any spatial query; `Debug.Log` gated behind `NPCDebug.VerboseJobs` / `VerboseActions` / equivalent UI toggle), #36 (interaction proximity uses `InteractableObject.IsCharacterInInteractionZone` for the JoinRequestDesk + CityManagement furniture; softlock guard mirrored verbatim), #39 (UI HUD architecture — `UI_CityManagementPanel` is a Prefab Variant of [`UI_WindowBase.prefab`](../../Assets/UI/Player%20HUD/UI_WindowBase.prefab) under `Assets/UI/Player HUD/`; row prefabs nested as leaf children with `Initialize` callback decoupling; `PlayerUI.Open<Name>Window` is the canonical entry; flat-façade rule).

**Network safety audit (rule #19b — performed BEFORE writing the plan):**

1. **Who writes the new state?** Server-only across the board.
   - `BuildingSO` fields are SerializeField (designer-authored at edit time, no runtime mutation).
   - `CommunityTierRequirementsSO` instances are ScriptableObject assets (read-only at runtime).
   - `Community.TryPromoteLevel` mutates `_level` server-side via existing `ChangeLevel` path; replicates via existing `CommunityData` save round-trip + future NetVar.
   - `AdministrativeBuilding.PendingJoinRequests` is a new `NetworkList<JoinRequest>` (server writes via ServerRpc; clients read via standard NetworkList replication).
   - `DrifterMigrationSystem` is `[ServerOnly]` — spawns Characters via existing `NetworkObject.Spawn`.
   - `JoinRequestDesk.OnInteract` is server-only (interactable callback already gated by NGO ownership convention).
   - `AdministrativeBuilding.PlaceCityBlueprintServerRpc` writes through `BuildingPlacementManager.RequestPlacementServerRpc` (existing server path).

2. **What replication channels?**
   - **`AdministrativeBuilding.PendingJoinRequests`**: NEW `NetworkList<JoinRequest>` (`INetworkSerializable` struct). Standard NGO NetworkList replication handles late-joiners.
   - **`Community.Level`**: existing `CommunityData` save round-trip + the existing leader-list NetVar pattern (defer dedicated `CommunityNetSync` to a follow-up; for v1 the AB's UI reads `Level` directly from the server-side Community via the leader's authority gate, and on tier-up fires a `LevelChangedClientRpc` for UI refresh).
   - **`BuildOrder` collection**: server-only `LogisticsOrderBook._activeBuildOrders` (unchanged from Plan 4b). UI reads via existing logistics introspection (`BuildingLogisticsManager.ActiveBuildOrders`).
   - **`BuildingGrid` occupancy**: existing Plan 2 NetworkList (no Plan 4c changes).
   - **`CharacterCommunity.Citizenship`**: existing per-character save round-trip + future NetVar (Plan 4a's compromise stands — clients see citizenship via `CommunitySaveData.citizenshipMapId` snapshots).

3. **Late-joiner sees?**
   - **Active BuildOrders**: not directly replicated (server-only list). Late-joiner sees in-progress construction sites via existing `Building.ConstructionProgress` NetVar; the abstract "this AB has 3 pending orders" only shows in the UI panel, which reads the server's list on open.
   - **Pending JoinRequests**: `NetworkList<JoinRequest>` replays on connect — UI binds correctly.
   - **Community.Level**: ridden via existing save round-trip; first authoritative tier value pulled from `CommunityData` snapshot.
   - **Drifters mid-walk**: NGO `NetworkObject` replicates position + `CharacterAction` proxy.
   - **Tier requirements (`CommunityTierRequirementsSO`)**: ScriptableObject assets, baked into the build — every client has them.

4. **Client-side pre-gate?**
   - **TierUpTab Promote button**: client-side gate reads `CommunityTierRequirementsSO` for the local community's next level + counts members/buildings/treasury from existing replicated state. Authoritative gate runs in `Community.TryPromoteLevel` server-side. UI is optimistic-green-or-grey; server's veto fires via ClientRpc toast.
   - **PlaceBuildingTab placement**: client ghost computes `BuildingGrid.CanPlace` from the existing replicated NetworkList. Server re-validates on `PlaceCityBlueprintServerRpc`.
   - **JoinRequestsTab Accept/Decline**: client reads `AB.PendingJoinRequests` (NetworkList) directly; the buttons fire ServerRpcs that re-validate authority + request existence.

5. **`GetComponentInParent` spawn-race?**
   - `CityManagementFurniture` lives baked into the AB.prefab — `GetComponentInParent<AdministrativeBuilding>` runs in `Awake`/`OnEnable`. **Mitigation**: use `TryRegisterWithAB()` pattern (see `Cashier.TryRegisterWithShop`) — null-safe call that re-attempts on the next interact tick if the AB isn't yet `IsServer`-ready.
   - `JoinRequestDesk` same shape — same mitigation.

6. **`InteractableObject.IsCharacterInInteractionZone` (rule #36)?**
   - `CityManagementFurniture.OnInteract` (player) — gated through `InteractableObject` parent's zone check (inherited).
   - `JoinRequestDesk.OnInteract` (drifter) — same.
   - `DrifterMigrationSystem` BT injection (`GoToInteractable(community.AB.JoinRequestDesk)`) — uses the standard `BTAction_GoToInteractable` which goes through `IsCharacterInInteractionZone`.

**Out of scope (deferred — listed at the end of this plan):**
- Persistence of `BuildOrder` across server restart (Plan 4b's deferred follow-up; still deferred).
- `Community.Level` dedicated NetVar replication (Plan 4c's stretch goal — using save round-trip + per-action ClientRpc as a v1 stopgap).
- `WAV_OnTierUp` cinematic / banner effect (UI polish; deferred).
- NPC-leader heuristics for accept/decline (v1 = always accept).
- Population caps per tier.
- Renounce-then-rejoin citizenship UX (drifter who renounces from another city → join request flow).
- Demolition + refund of city buildings.

---

## File Structure

**New files:**
- `Assets/Scripts/World/Data/BlueprintCategory.cs` *(exists from Plan 4a meta but content not yet shipped)* — `Personal | Civic` enum + helper.
- `Assets/Scripts/World/Community/CommunityTierRequirementsSO.cs` — per-`CommunityLevel` requirements SO.
- `Assets/Scripts/World/Community/CommunityTierRegistry.cs` — `Resources.LoadAll<CommunityTierRequirementsSO>` lookup, lazy-init per rule #34 (static registry).
- `Assets/Scripts/World/Community/DrifterMigrationSystem.cs` — server-only `MonoBehaviour` subscribed to `TimeManager.OnNewDay` per-map.
- `Assets/Scripts/World/Furniture/JoinRequestDesk.cs` — `OccupiableFurniture` subclass.
- `Assets/Scripts/World/Furniture/CityManagementFurniture.cs` — `InteractableObject` subclass; opens UI window.
- `Assets/Scripts/World/Community/JoinRequest.cs` — `INetworkSerializable` struct (`ApplicantNetId`, `RequestedAtDay`).
- `Assets/Scripts/UI/CityManagement/UI_CityManagementPanel.cs` — `UI_WindowBase` subclass.
- `Assets/Scripts/UI/CityManagement/UI_TierUpTab.cs` — `MonoBehaviour` row controller.
- `Assets/Scripts/UI/CityManagement/UI_PlaceBuildingTab.cs` — `MonoBehaviour` row controller.
- `Assets/Scripts/UI/CityManagement/UI_JoinRequestsTab.cs` — `MonoBehaviour` row controller.
- `Assets/Scripts/UI/CityManagement/UI_JoinRequestRow.cs` — row leaf.
- `Assets/Scripts/UI/CityManagement/UI_CivicBlueprintRow.cs` — row leaf.
- `Assets/UI/Player HUD/UI_CityManagementPanel.prefab` — UI_WindowBase variant.
- `Assets/UI/Player HUD/Rows/UI_JoinRequestRow.prefab`, `UI_CivicBlueprintRow.prefab` — row prefabs.
- `Assets/Editor/Tests/CityAdminConsole/BuildingSOPlacementTests.cs`
- `Assets/Editor/Tests/CityAdminConsole/CommunityTierRequirementsTests.cs`
- `Assets/Editor/Tests/CityAdminConsole/CommunityTryPromoteLevelTests.cs`
- `Assets/Editor/Tests/CityAdminConsole/JoinRequestTests.cs`
- `Assets/Resources/Data/CommunityTiers/TierRequirements_SmallGroup.asset`
- `Assets/Resources/Data/CommunityTiers/TierRequirements_Camp.asset`
- `Assets/Resources/Data/CommunityTiers/TierRequirements_Village.asset`
- `Assets/Resources/Data/CommunityTiers/TierRequirements_Town.asset`
- `Assets/Resources/Data/CommunityTiers/TierRequirements_City.asset`
- `Assets/Resources/Data/CommunityTiers/TierRequirements_Kingdom.asset`
- `Assets/Resources/Data/CommunityTiers/TierRequirements_Empire.asset`

**Modified files:**
- `Assets/Scripts/World/Buildings/BuildingSO.cs` — append `_gridFootprintCells`, `_blueprintCategory`, `_minTier` fields + getters.
- `Assets/Scripts/World/Community/Community.cs` — add `TryPromoteLevel()` + `LevelChangedClientRpc` (via AB).
- `Assets/Scripts/World/Buildings/CommercialBuildings/AdministrativeBuilding.cs` — add `PlaceCityBlueprintServerRpc`, `RequestPromoteLevelServerRpc`, `AcceptJoinRequestServerRpc`, `DeclineJoinRequestServerRpc`, `PendingJoinRequests` NetworkList, helper accessors.
- `Assets/Scripts/World/Buildings/BuildingPlacementManager.cs` — add `RequestPlaceCivicBuildingServerRpc` entry path that auto-creates the `BuildOrder` after spawn.
- `Assets/Scripts/UI/PlayerUI.cs` — add `_cityManagementWindow` SerializeField + `OpenCityManagementWindow(AdministrativeBuilding)` / `CloseCityManagementWindow` + null-guard warning.
- `Assets/Scripts/Character/CharacterControllers/PlayerController.cs` — add `CityPlacementMode` cursor state + handlers.

**Docs to update (in the final wrap-up task):**
- `wiki/systems/administrative-building.md` — bump `updated:`, add Plan 4c change log entry, refresh Public API section with new RPCs + NetworkList.
- `wiki/systems/world-community.md` — `TryPromoteLevel` + tier requirements docs.
- `wiki/systems/citizenship.md` — JoinRequest flow + drifter migration.
- `wiki/systems/player-hud.md` — `UI_CityManagementPanel` entry.
- `wiki/systems/city-management.md` (NEW) — full system page from `wiki/_templates/system.md`.
- `.agent/skills/community-system/SKILL.md` — tier-up + drifter migration procedural notes.
- `.agent/skills/ui-hud/SKILL.md` — `UI_CityManagementPanel` authoring recipe (variant of `UI_WindowBase`, tab pattern).
- `.claude/agents/building-furniture-specialist.md` — JoinRequestDesk + CityManagementFurniture domain expansion.
- `.claude/agents/ui-hud-specialist.md` — `UI_CityManagementPanel` registry entry.

---

## Task 1: BuildingSO placement additions

**Files:**
- Create: `Assets/Scripts/World/Data/BlueprintCategory.cs`
- Modify: `Assets/Scripts/World/Buildings/BuildingSO.cs`
- Create: `Assets/Editor/Tests/CityAdminConsole/BuildingSOPlacementTests.cs`

- [ ] **Step 1: BlueprintCategory enum + helper**

```csharp
public enum BlueprintCategory
{
    Personal = 0,   // Placeable by any character via normal flow (e.g. AB itself, future starter kit)
    Civic    = 1,   // Placeable ONLY via admin console of a city at MinTier or higher
}

public static class BlueprintCategoryExtensions
{
    public static bool IsCivic(this BlueprintCategory c) => c == BlueprintCategory.Civic;
    public static bool IsPersonal(this BlueprintCategory c) => c == BlueprintCategory.Personal;
}
```

- [ ] **Step 2: BuildingSO fields**

Append in the placement-data section of `BuildingSO`:

```csharp
[Header("Placement (Plan 4c)")]
[SerializeField] private Vector2Int _gridFootprintCells = new Vector2Int(1, 1);
[SerializeField] private BlueprintCategory _blueprintCategory = BlueprintCategory.Personal;
[SerializeField, Tooltip("For Civic blueprints: minimum CommunityLevel that unlocks this in the admin console.")]
private CommunityLevel _minTier = CommunityLevel.SmallGroup;

public Vector2Int GridFootprintCells => _gridFootprintCells;
public BlueprintCategory BlueprintCategory => _blueprintCategory;
public CommunityLevel MinTier => _minTier;
```

- [ ] **Step 3: AdministrativeBuilding SO override**

In `Resources/Data/Buildings/AdministrativeBuilding.asset` (already authored in Plan 4a), set `_gridFootprintCells = (3,3)`, `_blueprintCategory = Personal`. (Personal because the founder places it pre-charter via normal flow, NOT via admin console.)

- [ ] **Step 4: Tests**

`BuildingSOPlacementTests.cs`:
- `DefaultGridFootprint_IsOneByOne` — instantiate a fresh `BuildingSO` ScriptableObject, assert `GridFootprintCells == (1,1)`.
- `DefaultBlueprintCategory_IsPersonal` — assert default category.
- `DefaultMinTier_IsSmallGroup` — assert append-only order safety.
- `BlueprintCategory_IsCivic_TrueOnlyForCivic` — extension method coverage.

- [ ] **Step 5: Commit**

```
feat(building-so): GridFootprintCells + BlueprintCategory + MinTier

Plan 4c Task 1 of 8. Adds three new SerializeField properties to BuildingSO:
GridFootprintCells (default 1x1), BlueprintCategory (Personal default), MinTier
(CommunityLevel.SmallGroup default). New BlueprintCategory enum (Personal=0,
Civic=1) lives in Assets/Scripts/World/Data/BlueprintCategory.cs with helper
extensions. Auto-bump on the AB.asset to (3,3) Personal.

Tests (4) cover defaults + extension method correctness.
```

---

## Task 2: CommunityTierRequirementsSO + tier registry + asset authoring

**Files:**
- Create: `Assets/Scripts/World/Community/CommunityTierRequirementsSO.cs`
- Create: `Assets/Scripts/World/Community/CommunityTierRegistry.cs`
- Create: 7 `.asset` files under `Assets/Resources/Data/CommunityTiers/`
- Create: `Assets/Editor/Tests/CityAdminConsole/CommunityTierRequirementsTests.cs`

- [ ] **Step 1: CommunityTierRequirementsSO**

```csharp
[CreateAssetMenu(menuName = "MWI/Community/Tier Requirements", fileName = "TierRequirements_")]
public class CommunityTierRequirementsSO : ScriptableObject
{
    [SerializeField] private CommunityLevel _level;
    [SerializeField] private int _minPopulation = 1;
    [SerializeField] private List<BuildingSO> _requiredBuildings = new();
    [SerializeField] private int _minTreasury = 0;
    [SerializeField] private List<BuildingSO> _unlockedBlueprints = new();

    public CommunityLevel Level => _level;
    public int MinPopulation => _minPopulation;
    public IReadOnlyList<BuildingSO> RequiredBuildings => _requiredBuildings;
    public int MinTreasury => _minTreasury;
    public IReadOnlyList<BuildingSO> UnlockedBlueprints => _unlockedBlueprints;
}
```

- [ ] **Step 2: CommunityTierRegistry (lazy static)**

```csharp
public static class CommunityTierRegistry
{
    private static Dictionary<CommunityLevel, CommunityTierRequirementsSO> _byLevel;

    public static CommunityTierRequirementsSO Get(CommunityLevel level)
    {
        if (_byLevel == null) LazyInit();
        return _byLevel.TryGetValue(level, out var v) ? v : null;
    }

    private static void LazyInit()
    {
        _byLevel = new Dictionary<CommunityLevel, CommunityTierRequirementsSO>();
        var all = Resources.LoadAll<CommunityTierRequirementsSO>("Data/CommunityTiers");
        foreach (var so in all)
        {
            if (so == null) continue;
            _byLevel[so.Level] = so;
        }
    }

    // Editor reset hook for tests
    public static void ResetForTests() { _byLevel = null; }
}
```

**Important** (rule #34 + memory `feedback_lazy_static_registry_pattern.md`): lazy-init in `Get()` so joining clients (who skip `GameLauncher.LaunchSequence`) still get a working registry.

- [ ] **Step 3: Author the 7 tier asset files via Roslyn**

Run via `mcp__ai-game-developer__script-execute`:

```csharp
var so = ScriptableObject.CreateInstance<CommunityTierRequirementsSO>();
SetPrivateField(so, "_level", CommunityLevel.SmallGroup);
SetPrivateField(so, "_minPopulation", 1);
SetPrivateField(so, "_minTreasury", 0);
SetPrivateField(so, "_unlockedBlueprints", new List<BuildingSO>{/* AdministrativeBuilding only */});
AssetDatabase.CreateAsset(so, "Assets/Resources/Data/CommunityTiers/TierRequirements_SmallGroup.asset");

// repeat for Camp / Village / Town / City / Kingdom / Empire with progressively richer requirements
```

Designer-tunable values (initial bootstrap):
| Tier | MinPop | MinTreasury | Required Buildings | Unlocked |
|---|---|---|---|---|
| SmallGroup | 1 | 0 | (none) | AB |
| Camp | 3 | 100 | House×2 | House, Storage |
| Village | 8 | 500 | House×3, Farm×1 | House, Farm, Bar, Shop |
| Town | 20 | 2500 | House×5, Farm×2, Bar×1, Shop×1 | + Forge, Lumberyard |
| City | 50 | 10000 | House×10, Farm×3, Forge×1 | + Transporter, Inn |
| Kingdom | 100 | 30000 | House×20, Town hall×1 | (no new — admin already maxed) |
| Empire | 250 | 100000 | House×40, Inn×3 | — |

(Designer can tune via Inspector post-bootstrap.)

- [ ] **Step 4: Tests**

`CommunityTierRequirementsTests.cs`:
- `Registry_GetSmallGroup_ReturnsNonNull`
- `Registry_GetEmpire_ReturnsNonNull`
- `Registry_GetInvalidLevel_ReturnsNull` (using a hypothetical out-of-range cast — defensive)
- `MinPopulation_IncreasesAcrossTiers` (sanity check on bootstrap values)
- `LazyInit_IsIdempotent_AcrossMultipleGets`

- [ ] **Step 5: Commit**

```
feat(community): CommunityTierRequirementsSO + registry + 7 tier assets

Plan 4c Task 2 of 8. Per-CommunityLevel ScriptableObject defining tier-up
criteria (MinPopulation, RequiredBuildings, MinTreasury, UnlockedBlueprints).
CommunityTierRegistry.Get(level) is the runtime entry; lazy-init from
Resources/Data/CommunityTiers/ on first call (rule #34 / joining-clients-skip-
GameLauncher pattern).

7 bootstrap assets authored via Roslyn one-shot. Tunable via Inspector after
ship. Tests (5) cover registry lookups + monotonic-population sanity.
```

---

## Task 3: Community.TryPromoteLevel + AB.RequestPromoteLevelServerRpc

**Files:**
- Modify: `Assets/Scripts/World/Community/Community.cs`
- Modify: `Assets/Scripts/World/Buildings/CommercialBuildings/AdministrativeBuilding.cs`
- Create: `Assets/Editor/Tests/CityAdminConsole/CommunityTryPromoteLevelTests.cs`

- [ ] **Step 1: Community.TryPromoteLevel**

```csharp
/// <summary>
/// Server-only. Validates the next tier's requirements (population + buildings + treasury)
/// against the AB's accumulators. On success: ChangeLevel(currentLevel + 1) and returns
/// (true, null). On failure: returns (false, reason) for UI display.
/// </summary>
public (bool ok, string reason) TryPromoteLevel(AdministrativeBuilding ab)
{
    if (!NetworkManager.Singleton.IsServer) return (false, "Server-only");
    var nextLevel = (CommunityLevel)((int)Level + 1);
    var req = CommunityTierRegistry.Get(nextLevel);
    if (req == null) return (false, $"No tier requirements authored for {nextLevel}.");

    if (members.Count < req.MinPopulation)
        return (false, $"Need {req.MinPopulation - members.Count} more citizens.");

    if (ab != null && ab.GetTreasuryBalance(MWI.Economy.CurrencyId.Gold) < req.MinTreasury)
        return (false, $"Treasury needs {req.MinTreasury - ab.GetTreasuryBalance(MWI.Economy.CurrencyId.Gold)} more gold.");

    foreach (var requiredSO in req.RequiredBuildings)
    {
        int have = CountOwnedCompletedBuildingsOfSO(requiredSO);
        // (RequiredBuildings list semantics: counts duplicates. e.g. [House, House, Farm] = 2 houses + 1 farm.)
        int needed = CountInList(req.RequiredBuildings, requiredSO);
        if (have < needed)
            return (false, $"Need {needed - have} more {requiredSO.BuildingDisplayName}.");
    }

    ChangeLevel(nextLevel);
    return (true, null);
}

private int CountOwnedCompletedBuildingsOfSO(BuildingSO so)
{
    int n = 0;
    foreach (var b in ownedBuildings)
        if (b != null && !b.IsUnderConstruction && b.Blueprint == so) n++;
    return n;
}

private static int CountInList(IReadOnlyList<BuildingSO> list, BuildingSO so)
{
    int n = 0;
    for (int i = 0; i < list.Count; i++) if (list[i] == so) n++;
    return n;
}
```

- [ ] **Step 2: AdministrativeBuilding.RequestPromoteLevelServerRpc**

```csharp
[ServerRpc(RequireOwnership = false)]
public void RequestPromoteLevelServerRpc(ulong requesterNetId, ServerRpcParams rpcParams = default)
{
    // Authority gate: requester must be a leader of OwnerCommunity.
    var requester = ResolveCharacterById(requesterNetId);
    if (requester == null || OwnerCommunity == null) return;
    if (!OwnerCommunity.leaders.Contains(requester))
    {
        TierUpResultClientRpc(false, "Not a leader of this community.",
            new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { rpcParams.Receive.SenderClientId } } });
        return;
    }

    var (ok, reason) = OwnerCommunity.TryPromoteLevel(this);
    TierUpResultClientRpc(ok, reason ?? "Promoted!",
        new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { rpcParams.Receive.SenderClientId } } });
}

[ClientRpc]
private void TierUpResultClientRpc(bool ok, string message, ClientRpcParams rpcParams = default)
{
    // UI subscribes via PlayerUI.OnTierUpResult event.
    PlayerUI.Instance?.RaiseTierUpResult(ok, message);
}
```

(NB: `ResolveCharacterById` is a helper that walks the scene's spawned network objects + falls back to `MapRegistry` for hibernated NPCs. If it doesn't exist yet, this task adds it.)

- [ ] **Step 3: Tests**

`CommunityTryPromoteLevelTests.cs`:
- `TryPromote_FromSmallGroup_FailsWhenZeroMembers` — assert `(false, reason)`.
- `TryPromote_FromSmallGroup_SucceedsWithMinReqs` — stub a Community with 3 members + AB with 100 treasury.
- `TryPromote_NoRequirementsForLevel_ReturnsFalse` — assert defensive null path.
- `TryPromote_CountsDuplicateBuildingsCorrectly` — House×3 required, only have 2 → fails.

- [ ] **Step 4: Commit**

```
feat(community): TryPromoteLevel + AB.RequestPromoteLevelServerRpc

Plan 4c Task 3 of 8. Server-authoritative tier-up gate. Community.TryPromoteLevel
reads the next tier's CommunityTierRequirementsSO, validates pop/treasury/
buildings, calls ChangeLevel on success and returns (true, null). On failure
returns (false, reason) for UI display. AdministrativeBuilding.
RequestPromoteLevelServerRpc adds the leader-authority gate + ClientRpc reply
TierUpResultClientRpc routes the result to the requesting client only.

Tests (4) cover (a) empty-community-fails, (b) min-reqs-succeeds, (c) no-tier-
authored, (d) duplicate-building counting.
```

---

## Task 4: AB Civic-placement server path + auto-BuildOrder

**Files:**
- Modify: `Assets/Scripts/World/Buildings/BuildingPlacementManager.cs`
- Modify: `Assets/Scripts/World/Buildings/CommercialBuildings/AdministrativeBuilding.cs`

- [ ] **Step 1: AdministrativeBuilding.PlaceCityBlueprintServerRpc**

```csharp
[ServerRpc(RequireOwnership = false)]
public void PlaceCityBlueprintServerRpc(string blueprintId, Vector2Int targetCell,
                                         ulong requesterNetId,
                                         ServerRpcParams rpcParams = default)
{
    if (!IsServer) return;
    var requester = ResolveCharacterById(requesterNetId);
    if (requester == null || OwnerCommunity == null) return;
    if (!OwnerCommunity.leaders.Contains(requester)) { /* deny + ClientRpc toast */ return; }

    var blueprint = BuildingRegistry.GetBuildingSOById(blueprintId);
    if (blueprint == null) return;
    if (!blueprint.BlueprintCategory.IsCivic())
    {
        // Civic-only path; Personal blueprints use the normal placement flow.
        return;
    }

    // Tier-unlock gate.
    var req = CommunityTierRegistry.Get(OwnerCommunity.Level);
    if (req == null || !req.UnlockedBlueprints.Contains(blueprint))
    {
        PlaceCityBlueprintResultClientRpc(false, "Blueprint not unlocked at current tier.",
            new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { rpcParams.Receive.SenderClientId } } });
        return;
    }

    var grid = GetHostMap()?.BuildingGrid;
    if (grid == null) return;
    if (!grid.CanPlace(targetCell, blueprint.GridFootprintCells))
    {
        PlaceCityBlueprintResultClientRpc(false, "Cell occupied or out of bounds.",
            new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { rpcParams.Receive.SenderClientId } } });
        return;
    }

    Vector3 worldPos = grid.SnapToGridCenter(targetCell);

    // Delegate to existing placement entry — reuses validation chain + spawn.
    var newBuilding = BuildingPlacementManager.Instance.RequestPlaceByCharacterAtPosition(
        blueprint, requester, worldPos);
    if (newBuilding == null) return;

    // Wire community ownership + multi-owner + BuildingGrid registration.
    OwnerCommunity.AddOwnedBuilding(newBuilding);
    foreach (var leader in OwnerCommunity.leaders)
        if (leader != null) newBuilding.AddOwner(leader);
    grid.Register(newBuilding, targetCell, blueprint.GridFootprintCells);

    // Auto-create the BuildOrder on THIS AB so JobBuilder + JobLogisticsManager pick it up.
    var order = new BuildOrder(newBuilding, this, requester,
                               MWI.Time.TimeManager.Instance != null ? MWI.Time.TimeManager.Instance.CurrentDay : 0);
    LogisticsManager?.AddBuildOrder(order);

    PlaceCityBlueprintResultClientRpc(true, $"{blueprint.BuildingDisplayName} placed.",
        new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { rpcParams.Receive.SenderClientId } } });
}

[ClientRpc]
private void PlaceCityBlueprintResultClientRpc(bool ok, string message, ClientRpcParams rpcParams = default)
{
    PlayerUI.Instance?.RaisePlaceCityBlueprintResult(ok, message);
}
```

- [ ] **Step 2: BuildingPlacementManager.RequestPlaceByCharacterAtPosition**

If not already exposed, add a server-callable entry that wraps the existing `RequestPlacementServerRpc` body without the client-side ghost step. Reuses the existing validation chain (`IsInsideRegion`, `IsInsideMap`, `CanPlace`).

- [ ] **Step 3: Verify auto-BuildOrder firing**

Plan 4b's `JobLogisticsManager.ProcessActiveBuildOrders` already drives the cascade. No new code needed; just verify the `AddBuildOrder` event chain reaches `OnBuildOrderAdded` and `JobBuilder.CurrentBuildOrder` resolves on the next tick.

- [ ] **Step 4: Commit**

```
feat(building): AB.PlaceCityBlueprintServerRpc + auto-BuildOrder cascade

Plan 4c Task 4 of 8. Adds the server-side entry the admin console UI calls to
RTS-place a Civic blueprint. Server validates: requester is leader, blueprint
is Civic + unlocked at current tier, BuildingGrid CanPlace at targetCell. On
success: snaps to grid center, delegates to BuildingPlacementManager for the
spawn, registers ownership/multi-owner/grid, auto-creates a BuildOrder on
this AB's logistics manager, and replies via ClientRpc.

JobLogisticsManager.ProcessActiveBuildOrders (Plan 4b Task 6) consumes the new
order on its next tick — full cascade verified.
```

---

## Task 5: DrifterMigrationSystem

**Files:**
- Create: `Assets/Scripts/World/Community/DrifterMigrationSystem.cs`
- Modify: `Assets/Scripts/World/MapController.cs` — instantiate DrifterMigrationSystem on map start.

- [ ] **Step 1: DrifterMigrationSystem**

```csharp
public class DrifterMigrationSystem : MonoBehaviour
{
    [SerializeField] private MapController _map;
    [SerializeField] private int _maxDriftersPerCommunityPerDay = 1;
    [SerializeField] private float _spawnEdgePadding = 5f;

    private void OnEnable()
    {
        if (!NetworkManager.Singleton.IsServer) { enabled = false; return; }
        MWI.Time.TimeManager.OnNewDay += HandleNewDay;
    }

    private void OnDisable()
    {
        MWI.Time.TimeManager.OnNewDay -= HandleNewDay;
    }

    private void HandleNewDay(int day)
    {
        if (_map == null || _map.CommunityData == null) return;
        var community = _map.GetCommunity();
        if (community == null || !community.IsChartered) return;

        for (int i = 0; i < _maxDriftersPerCommunityPerDay; i++)
            TrySpawnOneDrifter(community);
    }

    private void TrySpawnOneDrifter(Community community)
    {
        Vector3 spawnPos = PickRandomMapEdgePoint();
        if (!NavMesh.SamplePosition(spawnPos, out var hit, 10f, NavMesh.AllAreas)) return;
        var drifter = CharacterSpawner.SpawnGeneratedNPC(hit.position);  // existing or NEW helper
        if (drifter == null) return;

        // Inject BT intent: "go to AB JoinRequestDesk, interact, then wait."
        var ab = community.AdministrativeBuilding;
        if (ab == null) return;
        var desk = ab.GetJoinRequestDesk();
        if (desk == null) return;

        drifter.CharacterAI?.InjectPriorityIntent(new BTIntent_GoToInteractable(desk));
    }

    private Vector3 PickRandomMapEdgePoint()
    {
        // Pick a point on one of the map's bounding rectangle edges + slight inward padding.
        var bounds = _map.MapBounds;
        int edge = UnityEngine.Random.Range(0, 4);
        // ... (4 edges: N/S/E/W, random T along the edge, padded inward by _spawnEdgePadding)
        return Vector3.zero; // detail elided
    }
}
```

(NB: `BTIntent_GoToInteractable` may not exist; if so, this task adds a minimal subclass of `BTAction` that's injected as a priority. Alternative: subscribe drifter's `CharacterAI` to a one-shot `OnSpawn` hook that calls `CharacterMovement.SetDestination(desk.GetInteractionPosition(drifter.transform.position))` directly, simpler for v1.)

- [ ] **Step 2: CharacterSpawner.SpawnGeneratedNPC**

If not yet exposed, add: takes a position, instantiates a randomized `Character` (`DrifterArchetypeSO` pool — random name/visual/traits), spawns the `NetworkObject`, returns the `Character`. Server-only.

- [ ] **Step 3: Tests**

(Heavy mock — defer to PlayMode-MP smoketest. EditMode tests only cover the day-tick guard logic.)

`DrifterMigrationTests.cs`:
- `HandleNewDay_NoChartered_NoSpawn` — stub a community with `IsChartered=false`; verify no spawn calls.
- `HandleNewDay_NoAB_NoSpawn` — IsChartered=true but `AdministrativeBuilding=null`.

- [ ] **Step 4: Commit**

```
feat(community): DrifterMigrationSystem daily ticker

Plan 4c Task 5 of 8. Server-only MonoBehaviour subscribed to TimeManager.OnNewDay.
For each chartered community on its host map, spawns up to N drifter NPCs at
random map-edge points (NavMesh-sampled). Each drifter receives a BT intent to
walk to the community's AB JoinRequestDesk.

Drifter archetype + CharacterSpawner.SpawnGeneratedNPC helper introduced as
part of this task.
```

---

## Task 6: JoinRequestDesk + JoinRequest + AB.PendingJoinRequests + Accept/Decline RPCs

**Files:**
- Create: `Assets/Scripts/World/Community/JoinRequest.cs` — INetworkSerializable struct.
- Create: `Assets/Scripts/World/Furniture/JoinRequestDesk.cs`
- Modify: `Assets/Scripts/World/Buildings/CommercialBuildings/AdministrativeBuilding.cs`
- Create: `Assets/Editor/Tests/CityAdminConsole/JoinRequestTests.cs`

- [ ] **Step 1: JoinRequest struct**

```csharp
public struct JoinRequest : INetworkSerializable, System.IEquatable<JoinRequest>
{
    public ulong ApplicantNetId;
    public int RequestedAtDay;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref ApplicantNetId);
        serializer.SerializeValue(ref RequestedAtDay);
    }

    public bool Equals(JoinRequest other) => ApplicantNetId == other.ApplicantNetId;
}
```

- [ ] **Step 2: AdministrativeBuilding.PendingJoinRequests + Add/Accept/Decline RPCs**

```csharp
private NetworkList<JoinRequest> _pendingJoinRequests;
public NetworkList<JoinRequest> PendingJoinRequests => _pendingJoinRequests;

public override void OnNetworkSpawn()
{
    base.OnNetworkSpawn();
    _pendingJoinRequests = new NetworkList<JoinRequest>();
}

public override void OnNetworkDespawn()
{
    _pendingJoinRequests?.Dispose();
    _pendingJoinRequests = null;
    base.OnNetworkDespawn();
}

[ServerRpc(RequireOwnership = false)]
public void SubmitJoinRequestServerRpc(ulong applicantNetId, ServerRpcParams rpcParams = default)
{
    if (!IsServer || OwnerCommunity == null) return;
    if (IsUnderConstruction) return;

    var applicant = ResolveCharacterById(applicantNetId);
    if (applicant == null) return;
    if (applicant.CharacterCommunity == null) return;
    if (applicant.CharacterCommunity.CurrentCommunity != null) return; // already in a community
    if (applicant.CharacterCommunity.Citizenship != null) return;     // already a citizen

    int day = MWI.Time.TimeManager.Instance != null ? MWI.Time.TimeManager.Instance.CurrentDay : 0;
    var req = new JoinRequest { ApplicantNetId = applicantNetId, RequestedAtDay = day };

    // Dedupe — applicant already in queue?
    foreach (var existing in _pendingJoinRequests)
        if (existing.ApplicantNetId == applicantNetId) return;

    _pendingJoinRequests.Add(req);
}

[ServerRpc(RequireOwnership = false)]
public void AcceptJoinRequestServerRpc(ulong applicantNetId, ulong leaderNetId, ServerRpcParams rpcParams = default)
{
    if (!IsServer || OwnerCommunity == null) return;
    var leader = ResolveCharacterById(leaderNetId);
    if (leader == null || !OwnerCommunity.leaders.Contains(leader)) return;
    var applicant = ResolveCharacterById(applicantNetId);
    if (applicant == null) return;

    OwnerCommunity.AddMember(applicant);
    applicant.CharacterCommunity?.JoinCommunity(OwnerCommunity);
    applicant.CharacterCommunity?.SetCitizenship(OwnerCommunity);

    RemoveJoinRequestInternal(applicantNetId);

    // Drifter released from JoinRequestDesk (existing CharacterAction_OccupyFurniture cancel path).
    applicant.CharacterActions?.ClearCurrentAction();
}

[ServerRpc(RequireOwnership = false)]
public void DeclineJoinRequestServerRpc(ulong applicantNetId, ulong leaderNetId, ServerRpcParams rpcParams = default)
{
    if (!IsServer || OwnerCommunity == null) return;
    var leader = ResolveCharacterById(leaderNetId);
    if (leader == null || !OwnerCommunity.leaders.Contains(leader)) return;
    var applicant = ResolveCharacterById(applicantNetId);

    RemoveJoinRequestInternal(applicantNetId);
    applicant?.CharacterActions?.ClearCurrentAction();
}

private void RemoveJoinRequestInternal(ulong applicantNetId)
{
    for (int i = _pendingJoinRequests.Count - 1; i >= 0; i--)
        if (_pendingJoinRequests[i].ApplicantNetId == applicantNetId)
            _pendingJoinRequests.RemoveAt(i);
}
```

- [ ] **Step 3: JoinRequestDesk furniture**

```csharp
public class JoinRequestDesk : OccupiableFurniture
{
    private AdministrativeBuilding _ab;

    protected override void Awake()
    {
        base.Awake();
        _ab = GetComponentInParent<AdministrativeBuilding>();
    }

    public void TryRegisterWithAB()
    {
        if (_ab == null) _ab = GetComponentInParent<AdministrativeBuilding>();
    }

    public override void OnInteract(Character actor)
    {
        TryRegisterWithAB();
        if (_ab == null) return;
        if (actor == null || actor.NetworkObject == null) return;
        _ab.SubmitJoinRequestServerRpc(actor.NetworkObject.NetworkObjectId);
        base.OnInteract(actor); // queues CharacterAction_OccupyFurniture for the "wait in line" visual
    }
}
```

- [ ] **Step 4: Tests**

`JoinRequestTests.cs`:
- `JoinRequest_Serialize_RoundTrip` — INetworkSerializable correctness.
- `JoinRequest_Equality_KeyedOnApplicantNetId`.
- `JoinRequest_RequestedAtDay_Preserved`.

(AB-side ServerRpc tests deferred — too much NetworkManager mock overhead. Manual PlayMode-MP smoketest in Task 8 covers end-to-end.)

- [ ] **Step 5: Commit**

```
feat(community): JoinRequest + JoinRequestDesk + AB join-request RPCs

Plan 4c Task 6 of 8. New JoinRequest struct (INetworkSerializable, equality
keyed on ApplicantNetId). AdministrativeBuilding gains a NetworkList<JoinRequest>
PendingJoinRequests + Submit/Accept/Decline ServerRpcs (leader-authority gates).
JoinRequestDesk : OccupiableFurniture forwards drifter-side interact to
AB.SubmitJoinRequestServerRpc + queues CharacterAction_OccupyFurniture for the
"wait in line" visual.

Tests (3) cover the struct serialization + equality contract.
```

---

## Task 7: CityManagementFurniture + UI_CityManagementPanel (HUD)

**Files:**
- Create: `Assets/Scripts/World/Furniture/CityManagementFurniture.cs`
- Create: `Assets/Scripts/UI/CityManagement/UI_CityManagementPanel.cs` (extends `UI_WindowBase`)
- Create: `Assets/Scripts/UI/CityManagement/UI_TierUpTab.cs`, `UI_PlaceBuildingTab.cs`, `UI_JoinRequestsTab.cs`
- Create: `Assets/Scripts/UI/CityManagement/UI_JoinRequestRow.cs`, `UI_CivicBlueprintRow.cs`
- Create: `Assets/UI/Player HUD/UI_CityManagementPanel.prefab` (variant of `UI_WindowBase.prefab`)
- Create: `Assets/UI/Player HUD/Rows/UI_JoinRequestRow.prefab`, `UI_CivicBlueprintRow.prefab`
- Modify: `Assets/Scripts/UI/PlayerUI.cs` — `_cityManagementWindow` SerializeField + entry API.
- Modify: `Assets/Scripts/Character/CharacterControllers/PlayerController.cs` — `CityPlacementMode` cursor state.

- [ ] **Step 1: CityManagementFurniture script**

```csharp
public class CityManagementFurniture : Furniture
{
    private AdministrativeBuilding _ab;

    protected override void Awake()
    {
        base.Awake();
        _ab = GetComponentInParent<AdministrativeBuilding>();
    }

    public void TryRegisterWithAB()
    {
        if (_ab == null) _ab = GetComponentInParent<AdministrativeBuilding>();
    }

    public override void OnInteract(Character actor)
    {
        TryRegisterWithAB();
        if (_ab == null || actor == null) return;
        if (_ab.OwnerCommunity == null) return;
        if (!_ab.OwnerCommunity.leaders.Contains(actor)) return;
        // Local-player only — open the HUD window. NPCs use their own server-method path.
        if (!actor.IsLocalPlayer) return;
        PlayerUI.Instance?.OpenCityManagementWindow(_ab);
    }
}
```

- [ ] **Step 2: UI_CityManagementPanel + tabs (script side)**

Follow rule #39 — `UI_CityManagementPanel : UI_WindowBase`. Three tabs as `MonoBehaviour` children: TierUp, PlaceBuilding, JoinRequests. Each tab has its own `_rowContainer` + row-prefab `SerializeField`. Initialize-callback decoupling pattern.

```csharp
public class UI_CityManagementPanel : UI_WindowBase
{
    [SerializeField] private UI_TierUpTab _tierUpTab;
    [SerializeField] private UI_PlaceBuildingTab _placeBuildingTab;
    [SerializeField] private UI_JoinRequestsTab _joinRequestsTab;

    private AdministrativeBuilding _ab;

    public void Initialize(AdministrativeBuilding ab)
    {
        _ab = ab;
        _tierUpTab?.Initialize(ab);
        _placeBuildingTab?.Initialize(ab);
        _joinRequestsTab?.Initialize(ab);
        OpenWindow();
    }
}
```

- [ ] **Step 3: Prefab authoring via MCP**

Variant of `UI_WindowBase.prefab` at `Assets/UI/Player HUD/UI_CityManagementPanel.prefab`. Tab GameObjects + row prefabs nested per pattern. Use `assets-prefab-create` + `gameobject-component-add` + `assets-prefab-save`.

The script-execute Roslyn one-shot:
```csharp
var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/UI/Player HUD/UI_WindowBase.prefab");
var instance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
instance.name = "UI_CityManagementPanel";
var script = instance.AddComponent<UI_CityManagementPanel>();
// ... build tab structure + wire row prefabs via SetPrivateField reflection
PrefabUtility.SaveAsPrefabAsset(instance, "Assets/UI/Player HUD/UI_CityManagementPanel.prefab");
```

- [ ] **Step 4: PlayerUI hooks**

```csharp
[SerializeField] private UI_CityManagementPanel _cityManagementWindow;

public void OpenCityManagementWindow(AdministrativeBuilding ab)
{
    if (_cityManagementWindow == null)
    {
        Debug.LogWarning("<color=orange>[PlayerUI]</color> OpenCityManagementWindow called but _cityManagementWindow SerializeField is null — author the prefab (variant of UI_WindowBase.prefab) and wire it to PlayerUI._cityManagementWindow in the Inspector.");
        return;
    }
    _cityManagementWindow.Initialize(ab);
}

public void CloseCityManagementWindow() => _cityManagementWindow?.CloseWindow();

public bool IsCityManagementWindowOpen() => _cityManagementWindow != null && _cityManagementWindow.gameObject.activeSelf;
```

- [ ] **Step 5: PlayerController RTS-placement cursor mode**

```csharp
public enum CursorMode { Default, BuildingGhost, CityPlacementGhost }
private CursorMode _cursorMode = CursorMode.Default;
private BuildingSO _pendingCivicBlueprint;

public void BeginCityPlacementMode(BuildingSO civicBlueprint)
{
    _cursorMode = CursorMode.CityPlacementGhost;
    _pendingCivicBlueprint = civicBlueprint;
    // Show grid overlay, etc.
}

// In Update() (gated by IsOwner):
if (_cursorMode == CursorMode.CityPlacementGhost)
{
    // Raycast cursor → world → snap to BuildingGrid → preview ghost
    if (Input.GetMouseButtonDown(0))
    {
        var ab = PlayerController.Instance.CurrentCharacter?.CharacterCommunity?.CurrentCommunity?.AdministrativeBuilding;
        if (ab != null && _hoveredGridCell.HasValue)
        {
            ab.PlaceCityBlueprintServerRpc(_pendingCivicBlueprint.BuildingId, _hoveredGridCell.Value,
                                            character.NetworkObject.NetworkObjectId);
        }
        _cursorMode = CursorMode.Default;
    }
    if (Input.GetKeyDown(KeyCode.Escape)) _cursorMode = CursorMode.Default;
}
```

(NB: full cursor mode visual polish — ghost preview prefab + green/red cell highlight — is part of this task but can ship with placeholder visuals; final art is a separate polish pass.)

- [ ] **Step 6: Tests**

UI-side EditMode tests cover only the tab-row prefab contract:
- `UI_JoinRequestRow_HasInitialize` — assert public `Initialize` exists.
- `UI_CivicBlueprintRow_HasInitialize`.

Manual smoketest steps documented in Task 8.

- [ ] **Step 7: Commit**

```
feat(ui): CityManagementFurniture + UI_CityManagementPanel HUD

Plan 4c Task 7 of 8. CityManagementFurniture is the in-world interactable that
opens the HUD window. Owner-only (community leaders). Local-player only on the
client side — NPCs use server-method paths.

UI_CityManagementPanel : UI_WindowBase variant under Assets/UI/Player HUD/.
Three tabs: TierUp (next-tier progress + Promote button), PlaceBuilding (list
of tier-unlocked Civic blueprints + RTS cursor mode hand-off), JoinRequests
(applicant queue + Accept/Decline).

PlayerController gains a CityPlacementGhost cursor mode. PlayerUI gains
OpenCityManagementWindow / CloseCityManagementWindow with the null-guard
warning pattern (rule #39).

Prefabs authored via MCP Roslyn script. Placeholder visuals — full polish pass
deferred.
```

---

## Task 8: AB.prefab authoring + final docs sync

**Files:**
- Modify (asset): `Assets/Resources/Buildings/AdministrativeBuilding.prefab` — preplaced furniture wiring.
- Modify: 5 wiki pages + 4 SKILL.md + 3 agent files (full list in "Docs to update" above).
- Create: `wiki/systems/city-management.md` (NEW system page).

- [ ] **Step 1: AB.prefab preplaced furniture**

Use MCP `assets-prefab-open` → `gameobject-create` to add:
- `CityManagementFurniture` (preplaced near AB entrance)
- `JoinRequestDesk` (preplaced near AB entrance, second spot)
- `SafeFurniture` (city treasury — reuses existing safe prefab)
- 2× `StorageFurniture` (material stockpile)

Then `assets-prefab-save`.

- [ ] **Step 2: Wiki + SKILL + agent updates**

(As listed in "Docs to update" — each gets a change log entry + relevant section refresh.)

- [ ] **Step 3: PlayMode-MP smoketest checklist**

In `wiki/systems/city-management.md` "Smoketest" section, document:
- Host runs `/dev` debug spawn AB at fresh map → places via normal flow → finishes construction → AB.OnFinalize grants citizenship → CityManagement opens with player as leader.
- Place House (Civic) via RTS mode → BuildOrder created → JobBuilder picks it up → constructs.
- Day tick → drifter spawns → walks to JoinRequestDesk → submits request → leader accepts via UI → drifter becomes citizen.
- Promote button greyed until pop+treasury+buildings clear → click Promote → tier-up succeeds → new blueprints unlock in PlaceBuildingTab.

- [ ] **Step 4: Commit**

```
docs+content(plan-4c): AB.prefab preplaced furniture + full docs sync

Plan 4c Task 8 of 8 — final wrap-up. AdministrativeBuilding.prefab now ships
with preplaced CityManagementFurniture + JoinRequestDesk + SafeFurniture +
2× StorageFurniture. Wiki / SKILL / agent docs all updated with Plan 4c entries.
NEW wiki page: wiki/systems/city-management.md (full 10-section template).

Plan 4c of 5 complete. The city-founding loop is now end-to-end playable.
```

---

## Risk register

- **R1: `CharacterSpawner.SpawnGeneratedNPC` doesn't exist** — Plan 4c Task 5 adds it.
- **R2: `BTIntent_GoToInteractable` / BT injection API may need plumbing** — fall back to `CharacterMovement.SetDestination` + scheduled `JoinRequestDesk.OnInteract` if BT injection is too invasive.
- **R3: `BuildingPlacementManager.RequestPlaceByCharacterAtPosition` may need to be added** — Task 4 plans this; if existing API doesn't expose, wrap via private-method call or refactor.
- **R4: NetworkList<JoinRequest> requires `INetworkSerializable` + `IEquatable` on the struct** — Task 6 covers both.
- **R5: UI prefab authoring via MCP** — `UI_CityManagementPanel` is a deeply-nested variant; authoring via Roslyn is feasible but slow. Authoring time isn't an issue; testing is — verify the prefab renders correctly in the Game view (not just Scene view) per rule #39's ScreenSpaceCamera convention.
- **R6: PlayerController cursor mode conflicts** — the existing `BuildingPlacementMode` (or equivalent) shouldn't fight with the new `CityPlacementGhost` mode. Task 7 reuses or extends the existing cursor-mode machinery if present.
- **R7: Tier-up "duplicate building counting"** — the `RequiredBuildings` list semantic (list with duplicates) needs the helper `CountInList` to work correctly. Tests must cover.
- **R8: `Community.Level` replication** — v1 uses save round-trip + per-action ClientRpc. If the UI feels laggy on tier-up, follow up with a `NetworkVariable<int> CommunityLevelNet` on AdministrativeBuilding.

---

## Verification

Per-task verification (run after each commit):
- `mcp__ai-game-developer__assets-refresh` clean compile.
- `mcp__ai-game-developer__tests-run` testMode=EditMode → 0 failed.

Final PlayMode-MP smoketest (in Task 8) — full end-to-end city-founding loop.

---

## Out of scope (final summary)

- `BuildOrder` persistence across server restarts (still deferred from Plan 4b).
- `Community.Level` dedicated NetVar (using save round-trip + per-action ClientRpc as v1 stopgap).
- NPC-leader Accept/Decline heuristics (v1 = always accept).
- Population caps per tier.
- Demolition + refund + ownership transfer.
- Multi-AB cities.
- Renounce-then-rejoin citizenship UX.
- Tier-up cinematic / banner effect.

---

**Status:** ready to execute.
