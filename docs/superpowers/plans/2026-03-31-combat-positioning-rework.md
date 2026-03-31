# Combat Positioning & Movement Rework — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the facing bug, replace proximity-based engagements with targeting-graph-based engagements, and rework combat positioning/movement for an organic tactical brawl feel.

**Architecture:** The engagement coordinator is rebuilt as a graph solver that evaluates targeting relationships each tick. CombatFormation is replaced with role-based organic positioning. CombatTacticalPacer is reworked with idle sway, circling, and dynamic spacing. Facing is consolidated to a single authority (CharacterVisual reading own PlannedTarget).

**Tech Stack:** Unity 2022+ / C# / NGO (Netcode for GameObjects) / NavMesh

**Spec:** `docs/superpowers/specs/2026-03-31-combat-positioning-design.md`

---

## File Structure

### Files to Modify

| File | Responsibility After Change |
|------|----------------------------|
| `Assets/Scripts/Character/CharacterVisual.cs` | Single source of facing truth — reads own character's PlannedTarget |
| `Assets/Scripts/Character/CharacterCombat/CharacterCombat.cs` | Decoupled from cross-character facing — SetActionIntent only affects self |
| `Assets/Scripts/AI/CombatAILogic.cs` | Remove direct UpdateFlip calls; delegate facing to CharacterVisual |
| `Assets/Scripts/BattleManager/CombatEngagementCoordinator.cs` | Full rewrite — targeting-graph algorithm for form/join/split/follow |
| `Assets/Scripts/BattleManager/CombatEngagement.cs` | Soft anchor point + outnumber ratio tracking |
| `Assets/Scripts/BattleManager/CombatFormation.cs` | Full rewrite — organic role-based positioning (no fixed slots) |
| `Assets/Scripts/BattleManager/EngagementGroup.cs` | Light touch — add outnumber ratio helper |
| `Assets/Scripts/Character/CharacterCombat/CombatTacticalPacer.cs` | Full rewrite — idle sway, circling, dynamic spacing, unengaged follow |
| `Assets/Scripts/BattleManager/BattleManager.cs` | Light API changes to match new coordinator signatures |

### No New Files

All changes fit within existing files. The responsibility boundaries remain the same — we're changing internals, not adding new components.

---

## Task 1: Fix Facing Bug — Single Source of Truth

**Files:**
- Modify: `Assets/Scripts/Character/CharacterCombat/CharacterCombat.cs:44-61` (SetActionIntent)
- Modify: `Assets/Scripts/Character/CharacterCombat/CharacterCombat.cs:79-88` (SetPlannedTarget)
- Modify: `Assets/Scripts/AI/CombatAILogic.cs:29-184` (Tick method — remove UpdateFlip calls)
- Modify: `Assets/Scripts/Character/CharacterVisual.cs:219-226` (LateUpdate — read combat target)

**Context:** Three systems fight over facing: `CombatAILogic.UpdateFlip()`, `CharacterVisual.LateUpdate()` via `_lookTarget`, and `SetPlannedTarget→SetActionIntent→SetLookTarget` cross-character calls. We consolidate to one: CharacterVisual reads the character's own PlannedTarget.

- [ ] **Step 1: Fix SetActionIntent to only affect the acting character's facing**

In `CharacterCombat.cs`, `SetActionIntent` (lines 44-61) currently calls `_character.CharacterVisual.SetLookTarget(target)` which sets the look target on the *acting* character. This is correct. But verify the call chain: `SetPlannedTarget` (line 79-88) calls `RequestEngagement` which may trigger other characters' facing changes indirectly. The fix is to ensure `SetLookTarget` is only ever called on `_character` (self), never on the target character.

Read `SetActionIntent` at line 44. The current code sets `_character.CharacterVisual.SetLookTarget(target)` — this sets the *acting character's* look target TO the target character. This is correct behavior. The bug is elsewhere.

Read `SetPlannedTarget` at line 79. It sets `PlannedTarget = target` and calls `RequestEngagement`. The engagement request itself is fine. The problem is that `SetActionIntent` is called by EACH character's AI for THEMSELVES, but the look target gets overwritten because multiple paths compete.

The real fix: ensure `SetLookTarget` uses the PlannedTarget's Transform (not a separate field that can be overwritten by external calls).

In `CharacterCombat.cs`, modify `SetActionIntent` (line 44-61):
```csharp
public void SetActionIntent(Func<bool> action, Character target)
{
    PlannedAction = action;
    PlannedTarget = target;

    if (target != null && IsInBattle && CurrentBattleManager != null && CurrentBattleManager.Coordinator != null)
    {
        CurrentBattleManager.Coordinator.RequestEngagement(_character, target);
    }

    // Face OUR target — only affect our own visual
    if (_character != null && _character.CharacterVisual != null && target != null)
    {
        _character.CharacterVisual.SetLookTarget(target);
    }

    OnActionIntentDecided?.Invoke(target, action);
}
```

Verify this matches the existing logic minus any cross-character calls. The key: `_character.CharacterVisual.SetLookTarget(target)` sets THIS character to look at the target. This is already self-referencing. If the existing code is already doing this correctly, the bug may be in CombatAILogic instead.

- [ ] **Step 2: Remove competing UpdateFlip calls from CombatAILogic**

In `CombatAILogic.cs`, the `Tick` method (lines 29-184) contains direct `UpdateFlip` and `FaceTarget` calls that compete with `CharacterVisual.LateUpdate()`. These must be removed since CharacterVisual's LateUpdate now handles all facing via the look target.

Find and remove all lines in `CombatAILogic.Tick` that call:
- `_self.CharacterVisual?.UpdateFlip(...)` 
- `_self.CharacterVisual?.FaceTarget(...)`

These are in the execution phase (Phase 2, around lines 84-161) and the tactical movement phase (Phase 3, around lines 162-178). Search for all occurrences.

Replace them with nothing — `CharacterVisual.LateUpdate()` handles facing because the look target is already set by `SetActionIntent`.

- [ ] **Step 3: Ensure CharacterVisual.LateUpdate reads combat target correctly**

In `CharacterVisual.cs`, `LateUpdate` (lines 219-226) already faces the `_lookTarget` if set. Verify this works when `_lookTarget` is a character's Transform that may be destroyed mid-combat.

Add a null-safety check:
```csharp
private void LateUpdate()
{
    if (_lookTarget != null)
    {
        FaceTarget(_lookTarget.position);
    }
}
```

This should already be the case, but verify the existing code doesn't have additional logic that could interfere. If there are extra conditions (knockback checks, etc.), preserve them — they're valid blocking conditions.

- [ ] **Step 4: Verify SetPlannedTarget doesn't trigger cross-character facing**

In `CharacterCombat.cs`, `SetPlannedTarget` (lines 79-88) calls `RequestEngagement`. Trace through `CombatEngagementCoordinator.RequestEngagement` to confirm it doesn't call `SetLookTarget` or `SetActionIntent` on the *target* character. If it does, remove those calls.

Read `CombatEngagementCoordinator.RequestEngagement` (lines 74-153) and search for any `SetLookTarget`, `SetActionIntent`, `FaceTarget`, or `UpdateFlip` calls within it.

- [ ] **Step 5: Test the facing fix**

Enter Play Mode. Trigger a 1v5 combat (one player character vs 5 NPCs). Observe:
- The solo character should face ONE target consistently
- The solo character should NOT flip left-right rapidly
- Each attacker should face the defender independently
- When the solo character changes their own target (via AI or player input), only then should their facing change

Add temporary `Debug.Log` statements if needed:
```csharp
// In CharacterVisual.FaceTarget, temporarily add:
Debug.Log($"[Facing] {_character.name} facing target at {targetPosition}, IsFacingRight={IsFacingRight}");
```

Remove debug logs after confirming fix.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Character/CharacterCombat/CharacterCombat.cs Assets/Scripts/AI/CombatAILogic.cs Assets/Scripts/Character/CharacterVisual.cs
git commit -m "fix(combat): single facing authority — character faces only own target, remove competing flip systems"
```

---

## Task 2: Rework CombatEngagementCoordinator — Targeting Graph Algorithm

**Files:**
- Modify: `Assets/Scripts/BattleManager/CombatEngagementCoordinator.cs` (full rewrite of core logic)
- Modify: `Assets/Scripts/BattleManager/BattleManager.cs` (API call adjustments)

**Context:** The coordinator currently groups by proximity (10m merge radius). We replace this with a targeting-graph algorithm: mutual targeting forms engagements, one-way targeting into existing engagements causes joins, subgroup clusters cause splits, and characters follow their targets during reorganization.

- [ ] **Step 1: Add targeting graph data structure to CombatEngagementCoordinator**

Replace the existing `RequestEngagement` and proximity logic. Add fields to track the targeting graph:

```csharp
public class CombatEngagementCoordinator
{
    private BattleManager _manager;
    private List<CombatEngagement> _activeEngagements;
    
    // Targeting graph: who each character is targeting
    private Dictionary<Character, Character> _targetingGraph;
    
    public IReadOnlyList<CombatEngagement> ActiveEngagements => _activeEngagements;

    public CombatEngagementCoordinator(BattleManager manager)
    {
        _manager = manager;
        _activeEngagements = new List<CombatEngagement>();
        _targetingGraph = new Dictionary<Character, Character>();
    }
```

Keep `ClearAll()`, `GetBestTargetFor()`, and `GetClosestFromList()` — these are still needed. Remove: `RequestEngagement()` (old proximity-based), `SplitEngagement()` (old size-based), `ForceRetarget()` (stub).

- [ ] **Step 2: Implement targeting graph update method**

Add a method that characters call when they change targets, replacing the old `RequestEngagement`:

```csharp
/// <summary>
/// Called when a character selects/changes their combat target.
/// Updates the targeting graph. Engagement resolution happens in EvaluateEngagements().
/// </summary>
public void SetTargeting(Character attacker, Character target)
{
    if (attacker == null) return;
    
    if (target != null)
        _targetingGraph[attacker] = target;
    else
        _targetingGraph.Remove(attacker);
}

/// <summary>
/// Called when a character leaves battle or dies.
/// </summary>
public void RemoveFromGraph(Character character)
{
    _targetingGraph.Remove(character);
    LeaveCurrentEngagement(character);
}
```

- [ ] **Step 3: Implement the core engagement evaluation algorithm**

This is the heart of the rework. Called once per battle tick (from `BattleManager.PerformBattleTick`):

```csharp
/// <summary>
/// Evaluates the targeting graph and forms/merges/splits engagements.
/// Called once per battle tick.
/// </summary>
public void EvaluateEngagements()
{
    // Step 1: Find all mutual pairs (A→B AND B→A)
    var mutualPairs = new HashSet<(Character, Character)>();
    foreach (var kvp in _targetingGraph)
    {
        Character a = kvp.Key;
        Character b = kvp.Value;
        if (b != null && _targetingGraph.TryGetValue(b, out Character bTarget) && bTarget == a)
        {
            // Normalize pair order to avoid duplicates
            var pair = a.GetInstanceID() < b.GetInstanceID() ? (a, b) : (b, a);
            mutualPairs.Add(pair);
        }
    }

    // Step 2: Build connected components using Union-Find
    // Start with mutual pairs as seeds, then add join edges
    var unionFind = new Dictionary<Character, Character>(); // parent map
    
    // Seed with mutual pairs
    foreach (var (a, b) in mutualPairs)
    {
        EnsureInUnionFind(unionFind, a);
        EnsureInUnionFind(unionFind, b);
        Union(unionFind, a, b);
    }

    // Add join edges: if X targets someone in an engagement seed, X joins that component
    foreach (var kvp in _targetingGraph)
    {
        Character attacker = kvp.Key;
        Character target = kvp.Value;
        if (target != null && unionFind.ContainsKey(target))
        {
            EnsureInUnionFind(unionFind, attacker);
            Union(unionFind, attacker, target);
        }
    }

    // Step 3: Collect components
    var components = new Dictionary<Character, List<Character>>(); // root → members
    foreach (var kvp in unionFind)
    {
        Character root = Find(unionFind, kvp.Key);
        if (!components.ContainsKey(root))
            components[root] = new List<Character>();
        components[root].Add(kvp.Key);
    }

    // Step 4: Reconcile components with existing engagements
    ReconcileEngagements(components);
    
    // Step 5: Clean up empty/finished engagements
    _activeEngagements.RemoveAll(e => e.IsFinished());
}
```

- [ ] **Step 4: Implement Union-Find helpers**

```csharp
private void EnsureInUnionFind(Dictionary<Character, Character> uf, Character c)
{
    if (!uf.ContainsKey(c))
        uf[c] = c;
}

private Character Find(Dictionary<Character, Character> uf, Character c)
{
    while (uf[c] != c)
    {
        uf[c] = uf[uf[c]]; // path compression
        c = uf[c];
    }
    return c;
}

private void Union(Dictionary<Character, Character> uf, Character a, Character b)
{
    Character rootA = Find(uf, a);
    Character rootB = Find(uf, b);
    if (rootA != rootB)
        uf[rootA] = rootB;
}
```

- [ ] **Step 5: Implement ReconcileEngagements**

This compares the newly computed components against existing engagements and handles create/merge/split:

```csharp
private void ReconcileEngagements(Dictionary<Character, List<Character>> components)
{
    var handledCharacters = new HashSet<Character>();
    var engagementsToRemove = new HashSet<CombatEngagement>();

    foreach (var kvp in components)
    {
        List<Character> members = kvp.Value;
        
        // Find which existing engagements these members currently belong to
        var touchedEngagements = new HashSet<CombatEngagement>();
        foreach (Character member in members)
        {
            CombatEngagement existing = GetEngagementOf(member);
            if (existing != null)
                touchedEngagements.Add(existing);
        }

        if (touchedEngagements.Count == 0)
        {
            // No existing engagement — create new one
            CreateEngagementForComponent(members);
        }
        else if (touchedEngagements.Count == 1)
        {
            // One existing engagement — add new members, remove departed ones
            CombatEngagement engagement = touchedEngagements.First();
            SyncEngagementMembers(engagement, members);
        }
        else
        {
            // Multiple engagements need merging — pick the largest, absorb others
            CombatEngagement primary = touchedEngagements.OrderByDescending(e => 
                e.GroupA.Members.Count + e.GroupB.Members.Count).First();
            
            foreach (var other in touchedEngagements)
            {
                if (other != primary)
                    engagementsToRemove.Add(other);
            }
            
            SyncEngagementMembers(primary, members);
        }

        foreach (Character member in members)
            handledCharacters.Add(member);
    }

    // Remove characters no longer in any component from their engagements
    foreach (var engagement in _activeEngagements)
    {
        var allMembers = engagement.GroupA.Members.Concat(engagement.GroupB.Members).ToList();
        foreach (Character member in allMembers)
        {
            if (!handledCharacters.Contains(member))
                engagement.LeaveEngagement(member);
        }
    }

    // Remove merged/empty engagements
    foreach (var e in engagementsToRemove)
        _activeEngagements.Remove(e);
}
```

- [ ] **Step 6: Implement helper methods for reconciliation**

```csharp
private CombatEngagement GetEngagementOf(Character character)
{
    foreach (var engagement in _activeEngagements)
    {
        if (engagement.GroupA.Members.Contains(character) || 
            engagement.GroupB.Members.Contains(character))
            return engagement;
    }
    return null;
}

private void CreateEngagementForComponent(List<Character> members)
{
    // Need at least one member from each team
    BattleTeam teamA = null, teamB = null;
    foreach (Character member in members)
    {
        BattleTeam team = _manager.GetTeamOf(member);
        if (team == null) continue;
        
        if (teamA == null)
            teamA = team;
        else if (team != teamA)
            teamB = team;
    }

    if (teamA == null || teamB == null) return; // Can't form engagement without two teams

    var engagement = new CombatEngagement(_manager, teamA, teamB);
    foreach (Character member in members)
        engagement.JoinEngagement(member);
    
    // Set anchor point at midpoint of the two sides
    engagement.SetAnchorPoint(CalculateMidpoint(members, teamA, teamB));
    
    _activeEngagements.Add(engagement);
}

private Vector3 CalculateMidpoint(List<Character> members, BattleTeam teamA, BattleTeam teamB)
{
    Vector3 sumA = Vector3.zero, sumB = Vector3.zero;
    int countA = 0, countB = 0;
    
    foreach (Character member in members)
    {
        BattleTeam team = _manager.GetTeamOf(member);
        if (team == teamA) { sumA += member.transform.position; countA++; }
        else { sumB += member.transform.position; countB++; }
    }
    
    Vector3 centerA = countA > 0 ? sumA / countA : Vector3.zero;
    Vector3 centerB = countB > 0 ? sumB / countB : Vector3.zero;
    return (centerA + centerB) / 2f;
}

private void SyncEngagementMembers(CombatEngagement engagement, List<Character> expectedMembers)
{
    var currentMembers = engagement.GroupA.Members.Concat(engagement.GroupB.Members).ToList();
    
    // Add missing members
    foreach (Character member in expectedMembers)
    {
        if (!currentMembers.Contains(member))
            engagement.JoinEngagement(member);
    }
    
    // Remove extra members
    foreach (Character member in currentMembers)
    {
        if (!expectedMembers.Contains(member))
            engagement.LeaveEngagement(member);
    }
}
```

- [ ] **Step 7: Update BattleManager to use new coordinator API**

In `BattleManager.cs`:

1. In `PerformBattleTick()` (line 221-234), add `_engagementCoordinator.EvaluateEngagements()` call:
```csharp
private void PerformBattleTick()
{
    _engagementCoordinator.EvaluateEngagements();
    _engagementCoordinator.CleanupEngagements();
    
    // Existing initiative update logic stays
    foreach (Character participant in _allParticipants)
    {
        if (participant != null && participant.CharacterHealth.IsAlive)
        {
            participant.CharacterCombat.UpdateInitiativeTick();
        }
    }
}
```

2. Replace `RequestEngagement` passthrough (line 444-447) with `SetTargeting`:
```csharp
public void SetTargeting(Character attacker, Character target)
{
    _engagementCoordinator?.SetTargeting(attacker, target);
}
```

3. In `RegisterCharacter` (line 252-281), replace `RequestEngagement` call with `SetTargeting`.

4. In `HandleCharacterIncapacitated` (line 450-469), call `RemoveFromGraph` instead of `LeaveCurrentEngagement`.

- [ ] **Step 8: Update CharacterCombat to use new BattleManager API**

In `CharacterCombat.cs`, update `SetPlannedTarget` (line 79-88) and `SetActionIntent` (line 44-61) to call `SetTargeting` instead of `RequestEngagement`:

```csharp
public void SetPlannedTarget(Character target)
{
    PlannedTarget = target;
    if (target != null && IsInBattle && CurrentBattleManager != null)
    {
        CurrentBattleManager.SetTargeting(_character, target);
    }
}
```

- [ ] **Step 9: Test engagement formation**

Enter Play Mode. Test the following scenarios with `Debug.Log` in `EvaluateEngagements`:

1. **2v2 mutual:** A→D, D→A should form engagement {A,D}. B→D should join → {A,B,D}. 
2. **No mutual:** A→D but D→B — no engagement forms, both unengaged.
3. **Split:** Start with {A,B,C,D,E} engagement. B and C retarget E, E targets B → {B,C,E} splits off.

```csharp
// Temporary debug in EvaluateEngagements:
Debug.Log($"[Engagement] Mutual pairs: {mutualPairs.Count}, Components: {components.Count}, Active: {_activeEngagements.Count}");
```

Remove debug logs after confirming.

- [ ] **Step 10: Commit**

```bash
git add Assets/Scripts/BattleManager/CombatEngagementCoordinator.cs Assets/Scripts/BattleManager/BattleManager.cs Assets/Scripts/Character/CharacterCombat/CharacterCombat.cs
git commit -m "feat(combat): targeting-graph engagement system — form/join/split/follow based on who-targets-whom"
```

---

## Task 3: Add Soft Anchor and Outnumber Tracking to CombatEngagement

**Files:**
- Modify: `Assets/Scripts/BattleManager/CombatEngagement.cs`
- Modify: `Assets/Scripts/BattleManager/EngagementGroup.cs`

**Context:** Each engagement needs a spatial anchor point (where the fight started) and outnumber ratio tracking (for circling behavior).

- [ ] **Step 1: Add anchor point and leash radius to CombatEngagement**

In `CombatEngagement.cs`, add fields and methods:

```csharp
private Vector3 _anchorPoint;
private const float LEASH_RADIUS = 15f;

public Vector3 AnchorPoint => _anchorPoint;
public float LeashRadius => LEASH_RADIUS;

public void SetAnchorPoint(Vector3 point)
{
    _anchorPoint = point;
}

/// <summary>
/// Returns the outnumber ratio for a given team's side.
/// E.g., if GroupA has 4 and GroupB has 2, ratio for GroupA = 2.0, for GroupB = 0.5
/// </summary>
public float GetOutnumberRatio(Character character)
{
    bool inGroupA = GroupA.Members.Contains(character);
    int myCount = inGroupA ? GroupA.AliveCount : GroupB.AliveCount;
    int theirCount = inGroupA ? GroupB.AliveCount : GroupA.AliveCount;
    
    if (theirCount == 0) return float.MaxValue;
    return (float)myCount / theirCount;
}

/// <summary>
/// Returns the center of the opposing group for this character.
/// </summary>
public Vector3 GetOpponentCenter(Character character)
{
    bool inGroupA = GroupA.Members.Contains(character);
    EngagementGroup opponents = inGroupA ? GroupB : GroupA;
    return opponents.TryGetCenter(out Vector3 center) ? center : _anchorPoint;
}
```

- [ ] **Step 2: Add AliveCount to EngagementGroup**

In `EngagementGroup.cs`, add a helper:

```csharp
public int AliveCount
{
    get
    {
        int count = 0;
        foreach (Character member in _members)
        {
            if (member != null && member.CharacterHealth != null && member.CharacterHealth.IsAlive)
                count++;
        }
        return count;
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/BattleManager/CombatEngagement.cs Assets/Scripts/BattleManager/EngagementGroup.cs
git commit -m "feat(combat): add soft anchor point and outnumber ratio to CombatEngagement"
```

---

## Task 4: Rewrite CombatFormation — Organic Role-Based Positioning

**Files:**
- Modify: `Assets/Scripts/BattleManager/CombatFormation.cs` (full rewrite)

**Context:** Replace the fixed 3-ring slot system with dynamic organic positioning. Melee positions close to opponents, ranged stays back. No fixed home slots — positions are calculated relative to the engagement state.

- [ ] **Step 1: Rewrite CombatFormation with role-based positioning**

Replace the entire class:

```csharp
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Calculates organic combat positions based on character role (melee/ranged)
/// relative to the engagement's opponent center and anchor point.
/// No fixed slots — positions evolve dynamically.
/// </summary>
public class CombatFormation
{
    private const float MELEE_PREFERRED_DISTANCE = 4.0f;
    private const float MELEE_SPACING = 2.5f;
    private const float RANGED_MIN_DISTANCE = 8.0f;
    private const float RANGED_SPACING = 2.0f;
    private const float Z_SPREAD = 1.2f; // Vertical stagger for 2D-in-3D

    private Dictionary<Character, Vector3> _lastAssignedPositions;
    
    public CombatFormation()
    {
        _lastAssignedPositions = new Dictionary<Character, Vector3>();
    }

    /// <summary>
    /// Calculates the ideal position for a character within their engagement.
    /// </summary>
    /// <param name="character">The character to position</param>
    /// <param name="allies">All allies in this engagement group (including this character)</param>
    /// <param name="opponentCenter">Center of the opposing group</param>
    /// <param name="anchorPoint">Engagement anchor for leash constraint</param>
    /// <param name="teamSideSign">+1 or -1 indicating which X side this team is on</param>
    public Vector3 GetOrganicPosition(Character character, IReadOnlyList<Character> allies, 
        Vector3 opponentCenter, Vector3 anchorPoint, float teamSideSign)
    {
        bool isRanged = IsRangedCharacter(character);
        
        // Find this character's index among same-role allies for spacing
        int roleIndex = 0;
        int roleCount = 0;
        for (int i = 0; i < allies.Count; i++)
        {
            if (allies[i] == null || !allies[i].CharacterHealth.IsAlive) continue;
            bool allyIsRanged = IsRangedCharacter(allies[i]);
            if (allyIsRanged == isRanged)
            {
                if (allies[i] == character) roleIndex = roleCount;
                roleCount++;
            }
        }

        float distance = isRanged ? RANGED_MIN_DISTANCE : MELEE_PREFERRED_DISTANCE;
        float spacing = isRanged ? RANGED_SPACING : MELEE_SPACING;

        // Position on our team's side of the engagement
        Vector3 dirFromOpponent = (anchorPoint - opponentCenter).normalized;
        if (dirFromOpponent.sqrMagnitude < 0.01f)
            dirFromOpponent = new Vector3(teamSideSign, 0, 0);

        Vector3 basePosition = opponentCenter + dirFromOpponent * distance;

        // Spread allies along Z axis (perpendicular to fight direction)
        float zOffset = 0f;
        if (roleCount > 1)
        {
            float totalSpread = (roleCount - 1) * spacing;
            zOffset = -totalSpread / 2f + roleIndex * spacing;
        }

        // Add Z stagger for depth
        basePosition.z += zOffset;
        
        // Small deterministic jitter to avoid perfect lines
        float jitterSeed = character.GetInstanceID() * 0.1f;
        basePosition.x += Mathf.Sin(jitterSeed) * 0.4f;
        basePosition.z += Mathf.Cos(jitterSeed) * 0.3f;

        _lastAssignedPositions[character] = basePosition;
        return basePosition;
    }

    /// <summary>
    /// Returns the last assigned position for a character (for reference by pacing system).
    /// </summary>
    public bool TryGetLastPosition(Character character, out Vector3 position)
    {
        return _lastAssignedPositions.TryGetValue(character, out position);
    }

    public void RemoveCharacter(Character character)
    {
        _lastAssignedPositions.Remove(character);
    }

    public void Clear()
    {
        _lastAssignedPositions.Clear();
    }

    private bool IsRangedCharacter(Character character)
    {
        if (character?.CharacterCombat?.CurrentCombatStyleExpertise?.CombatStyle == null)
            return false;
        return character.CharacterCombat.CurrentCombatStyleExpertise.CombatStyle is RangedCombatStyleSO;
    }
}
```

- [ ] **Step 2: Update CombatEngagement.GetAssignedPosition to use new formation**

In `CombatEngagement.cs`, update `GetAssignedPosition` (lines 108-130) to call the new organic positioning:

```csharp
public Vector3 GetAssignedPosition(Character participant)
{
    bool inGroupA = GroupA.Members.Contains(participant);
    EngagementGroup myGroup = inGroupA ? GroupA : GroupB;
    EngagementGroup opponentGroup = inGroupA ? GroupB : GroupA;
    
    if (!opponentGroup.TryGetCenter(out Vector3 opponentCenter))
        opponentCenter = _anchorPoint;

    // Determine side sign: GroupA on left (-1), GroupB on right (+1)
    float teamSideSign = inGroupA ? -1f : 1f;

    return myGroup.Formation.GetOrganicPosition(
        participant, myGroup.Members, opponentCenter, _anchorPoint, teamSideSign);
}
```

- [ ] **Step 3: Test positioning**

Enter Play Mode with a 3v2 combat. Verify:
- Melee characters position close (~4m) to opponent center
- Ranged characters position further back (~8m)
- Characters are spread along Z axis, not stacked
- No characters overlapping

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/BattleManager/CombatFormation.cs Assets/Scripts/BattleManager/CombatEngagement.cs
git commit -m "feat(combat): organic role-based positioning — melee close, ranged back, no fixed slots"
```

---

## Task 5: Rewrite CombatTacticalPacer — Dynamic Movement Behaviors

**Files:**
- Modify: `Assets/Scripts/Character/CharacterCombat/CombatTacticalPacer.cs` (full rewrite)

**Context:** Replace the current pacing (stand in slot, kite if ranged) with: idle sway, tactical circling when outnumbering 2:1+, dynamic spacing after melee attacks, unengaged follow behavior, and ranged holds ground.

- [ ] **Step 1: Rewrite CombatTacticalPacer with all movement states**

Replace the entire class:

```csharp
using UnityEngine;

/// <summary>
/// Determines combat movement based on character state:
/// - Idle sway (waiting for initiative)
/// - Tactical circling (outnumbering 2:1+)
/// - Dynamic spacing (post-attack step-back for melee)
/// - Unengaged follow (trailing target without engagement)
/// - Ranged hold (no reactive kiting)
/// </summary>
public class CombatTacticalPacer
{
    private const float IDLE_SWAY_RADIUS = 0.7f;
    private const float IDLE_SWAY_SPEED = 0.5f;
    private const float CIRCLE_SPEED = 1.5f;
    private const float CIRCLE_RADIUS_OFFSET = 2.0f;
    private const float MELEE_STEPBACK_DISTANCE = 2.0f;
    private const float UNENGAGED_FOLLOW_MELEE_DISTANCE = 5.0f;
    private const float LEASH_PULL_STRENGTH = 0.3f;
    private const float PATH_UPDATE_INTERVAL = 0.8f;

    private Character _self;
    private Vector3 _swayCenter;
    private float _perlinSeedX;
    private float _perlinSeedZ;
    private float _circleAngle;
    private float _lastPathUpdateTime;
    private bool _needsStepBack;

    public CombatTacticalPacer(Character self)
    {
        _self = self;
        _perlinSeedX = Random.Range(0f, 100f);
        _perlinSeedZ = Random.Range(0f, 100f);
        _circleAngle = Random.Range(0f, Mathf.PI * 2f);
        _swayCenter = self.transform.position;
    }

    /// <summary>
    /// Called when the character finishes a melee attack — triggers step-back.
    /// </summary>
    public void NotifyAttackCompleted()
    {
        _needsStepBack = true;
    }

    /// <summary>
    /// Updates the sway center to the character's current position.
    /// Called after major position changes (engagement join, charge complete).
    /// </summary>
    public void ResetSwayCenter()
    {
        _swayCenter = _self.transform.position;
    }

    /// <summary>
    /// Main entry point: returns the desired movement destination.
    /// </summary>
    public Vector3 GetTacticalDestination(Character target, float attackRange, 
        CombatEngagement engagement, bool isCharging)
    {
        if (isCharging || target == null)
            return _self.transform.position;

        float now = Time.time;
        if (now - _lastPathUpdateTime < PATH_UPDATE_INTERVAL)
            return _self.transform.position; // No update needed yet
        _lastPathUpdateTime = now;

        Vector3 destination;
        bool isRanged = IsRangedCharacter(_self);

        // Priority 1: Post-attack step-back (melee only)
        if (_needsStepBack && !isRanged)
        {
            _needsStepBack = false;
            destination = CalculateStepBack(target);
            _swayCenter = destination;
            return ApplyLeash(destination, engagement);
        }

        // Priority 2: Unengaged — follow target at distance
        if (engagement == null)
        {
            return CalculateUnengagedFollow(target, attackRange, isRanged);
        }

        // Priority 3: Tactical circling (outnumbering 2:1+, melee only)
        float outnumberRatio = engagement.GetOutnumberRatio(_self);
        if (!isRanged && outnumberRatio >= 2.0f)
        {
            destination = CalculateCirclingPosition(engagement, target);
            return ApplyLeash(destination, engagement);
        }

        // Priority 4: Idle sway
        destination = CalculateIdleSway();
        return ApplyLeash(destination, engagement);
    }

    private Vector3 CalculateStepBack(Character target)
    {
        Vector3 selfPos = _self.transform.position;
        Vector3 awayFromTarget = (selfPos - target.transform.position).normalized;
        return selfPos + awayFromTarget * MELEE_STEPBACK_DISTANCE;
    }

    private Vector3 CalculateUnengagedFollow(Character target, float attackRange, bool isRanged)
    {
        Vector3 selfPos = _self.transform.position;
        Vector3 targetPos = target.transform.position;
        float followDistance = isRanged ? attackRange : UNENGAGED_FOLLOW_MELEE_DISTANCE;
        float currentDist = Vector3.Distance(selfPos, targetPos);

        if (currentDist > followDistance * 1.2f)
        {
            // Too far — move closer
            Vector3 dirToTarget = (targetPos - selfPos).normalized;
            return targetPos - dirToTarget * followDistance;
        }
        
        // Within range — minor sway only
        return CalculateIdleSway();
    }

    private Vector3 CalculateCirclingPosition(CombatEngagement engagement, Character target)
    {
        Vector3 opponentCenter = engagement.GetOpponentCenter(_self);
        float distToCenter = Vector3.Distance(_self.transform.position, opponentCenter);
        float circleRadius = distToCenter + CIRCLE_RADIUS_OFFSET;

        _circleAngle += CIRCLE_SPEED * PATH_UPDATE_INTERVAL;
        if (_circleAngle > Mathf.PI * 2f) _circleAngle -= Mathf.PI * 2f;

        Vector3 circlePos = opponentCenter + new Vector3(
            Mathf.Cos(_circleAngle) * circleRadius,
            0,
            Mathf.Sin(_circleAngle) * circleRadius
        );

        _swayCenter = circlePos;
        return circlePos;
    }

    private Vector3 CalculateIdleSway()
    {
        float time = Time.time;
        float noiseX = Mathf.PerlinNoise(_perlinSeedX + time * IDLE_SWAY_SPEED, 0) * 2f - 1f;
        float noiseZ = Mathf.PerlinNoise(0, _perlinSeedZ + time * IDLE_SWAY_SPEED) * 2f - 1f;
        
        return _swayCenter + new Vector3(
            noiseX * IDLE_SWAY_RADIUS,
            0,
            noiseZ * IDLE_SWAY_RADIUS
        );
    }

    private Vector3 ApplyLeash(Vector3 destination, CombatEngagement engagement)
    {
        if (engagement == null) return destination;
        
        Vector3 anchor = engagement.AnchorPoint;
        float leashRadius = engagement.LeashRadius;
        float distFromAnchor = Vector3.Distance(destination, anchor);

        if (distFromAnchor > leashRadius)
        {
            // Pull back toward anchor
            Vector3 toAnchor = (anchor - destination).normalized;
            float overshoot = distFromAnchor - leashRadius;
            destination += toAnchor * (overshoot * LEASH_PULL_STRENGTH);
        }

        return destination;
    }

    private bool IsRangedCharacter(Character character)
    {
        if (character?.CharacterCombat?.CurrentCombatStyleExpertise?.CombatStyle == null)
            return false;
        return character.CharacterCombat.CurrentCombatStyleExpertise.CombatStyle is RangedCombatStyleSO;
    }
}
```

- [ ] **Step 2: Update CombatAILogic to pass engagement and call NotifyAttackCompleted**

In `CombatAILogic.cs`, update the `Tick` method:

1. In Phase 3 (tactical movement, ~line 162-178), pass the engagement to the pacer:
```csharp
// Get character's current engagement
CombatEngagement engagement = null;
if (_self.CharacterCombat.IsInBattle)
{
    engagement = _self.CharacterCombat.CurrentBattleManager.Coordinator
        .GetEngagementOf(_self);
}

Vector3 tacticalDest = _combatPacer.GetTacticalDestination(
    currentTarget, attackRange, engagement, _isChargingTarget);
```

2. After action execution succeeds (Phase 2, when `ExecuteAction` returns true), notify pacer:
```csharp
if (executed)
{
    _combatPacer.NotifyAttackCompleted();
    _combatPacer.ResetSwayCenter();
}
```

3. Add a public `GetEngagementOf` method to `CombatEngagementCoordinator`:
```csharp
public CombatEngagement GetEngagementOf(Character character)
{
    foreach (var engagement in _activeEngagements)
    {
        if (engagement.GroupA.Members.Contains(character) || 
            engagement.GroupB.Members.Contains(character))
            return engagement;
    }
    return null;
}
```

Note: This method is also used by `ReconcileEngagements` from Task 2. If already added there, just verify it's public.

- [ ] **Step 3: Test all movement states**

Enter Play Mode and test each state:

1. **Idle sway:** Characters in engagement with initiative charging drift subtly (~0.7m)
2. **Circling:** Create 4v2 — the 4-side melee characters should orbit the 2 defenders
3. **Step-back:** After a melee attack, character backs off slightly then sways at new position
4. **Unengaged follow:** Set up scenario where one character targets another who doesn't reciprocate — follower should trail at distance
5. **Ranged hold:** Ranged character should NOT move when a melee enemy approaches

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Character/CharacterCombat/CombatTacticalPacer.cs Assets/Scripts/AI/CombatAILogic.cs Assets/Scripts/BattleManager/CombatEngagementCoordinator.cs
git commit -m "feat(combat): dynamic movement — idle sway, circling, step-back, unengaged follow"
```

---

## Task 6: Update CombatAILogic Charge Phase for New Positioning

**Files:**
- Modify: `Assets/Scripts/AI/CombatAILogic.cs` (Phase 2 approach logic)

**Context:** The charge/approach phase in CombatAILogic needs to work with the new system: both sides walk toward each other (melee goes further), ranged stops at weapon range. The existing Pythagorean positioning logic needs adjustment.

- [ ] **Step 1: Update Phase 2A approach logic**

In `CombatAILogic.cs`, the approach logic (Phase 2A, ~lines 84-130) currently calculates a strike position. Update it so:

- Melee: approaches the target's position directly (closing most of the gap)
- Ranged: approaches only to weapon range distance, then stops

The existing Pythagorean calculation for staggered Z positions is still useful — keep the Z stagger logic but update the distance calculation:

```csharp
// In the approach phase, after calculating strike position:
bool isRanged = IsRangedCharacter(_self);

if (isRanged)
{
    // Ranged: stop at weapon range, don't approach further
    float weaponRange = attackRange * 0.9f; // Slight buffer inside max range
    float currentDist = Vector3.Distance(_self.transform.position, currentTarget.transform.position);
    
    if (currentDist <= weaponRange)
    {
        // Already in range — don't move closer
        _isChargingTarget = false;
        // Skip to execution
    }
}
```

Keep the existing Z-stagger logic (7 unique Z values, -1.5 to 1.5) — it prevents clustering and works well with the organic feel.

- [ ] **Step 2: Ensure ranged characters don't flee during approach phase**

Verify that the approach logic never moves a ranged character AWAY from a target who is approaching them. The old `CalculateEscapeDestination` in the pacer is gone (removed in Task 5), but check that CombatAILogic doesn't have its own escape logic.

Search CombatAILogic for any code that moves characters away from targets (escape, kite, retreat, flee). If found, wrap it with a ranged-only guard that only fires after the character's OWN attack turn, not reactively.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/AI/CombatAILogic.cs
git commit -m "feat(combat): update charge phase — ranged stops at weapon range, no reactive kiting"
```

---

## Task 7: Integration Testing & Edge Cases

**Files:**
- All modified files from Tasks 1-6

**Context:** Final verification across all combat scenarios to catch edge cases.

- [ ] **Step 1: Test 1v1 combat**

Enter Play Mode. Initiate a 1v1 fight. Verify:
- Mutual targeting forms immediately
- Both characters approach, melee meets in middle
- Initiative system works normally
- Facing is stable (each faces the other)
- After attacks, melee steps back slightly

- [ ] **Step 2: Test 1v5 combat (the original bug scenario)**

Initiate a 1v5. Verify:
- Solo character faces ONE target and does NOT flip-flop
- All 5 attackers join the same engagement
- Melee attackers surround (circling behavior, 5:1 ratio > 2:1)
- Solo character's facing only changes when HE changes target

- [ ] **Step 3: Test 3v3 with engagement splits**

Initiate a 3v3. Observe natural engagement formation:
- Mutual pairs should form engagements
- If AI causes a subgroup to retarget, engagements should split/merge
- Characters should follow their targets between engagements

- [ ] **Step 4: Test ranged character behavior**

Include ranged characters in combat. Verify:
- Ranged stays at weapon range distance
- Ranged does NOT flee when melee enemy approaches
- Ranged shoots from current position (no approach on attack turn)
- Ranged holds ground when hit

- [ ] **Step 5: Test target death and retargeting**

Kill a target mid-combat. Verify:
- Killer immediately acquires new target
- Killer joins new target's engagement or forms new one
- No orphaned engagements with dead members
- No null reference exceptions during transition

- [ ] **Step 6: Test multiplayer (Host + Client)**

Test with two players:
- Verify facing syncs correctly via `_netIsFacingRight`
- Verify positions sync via NetworkTransform
- Verify engagement state is server-authoritative
- Client-controlled character facing should only change based on own target

- [ ] **Step 7: Final commit**

Clean up any remaining debug logs. Commit:
```bash
git add -A
git commit -m "test(combat): verify all combat positioning scenarios — facing, engagements, movement"
```

---

## Task 8: Update SKILL.md and Agent Documentation

**Files:**
- Modify: `.agent/skills/combat_system/SKILL.md`
- Evaluate: `.claude/agents/combat-gameplay-architect.md` for updates

**Context:** Per project rules, every system modification must update its SKILL.md. The combat system has changed significantly.

- [ ] **Step 1: Update combat system SKILL.md**

Read the current `.agent/skills/combat_system/SKILL.md` and update to reflect:
- New engagement rules (targeting-graph based, mutual required)
- New positioning system (organic, role-based)
- New movement behaviors (idle sway, circling, step-back, unengaged follow)
- Facing authority model (single source, own target only)
- Updated API: `SetTargeting` replaces `RequestEngagement`, `EvaluateEngagements` runs per tick

- [ ] **Step 2: Evaluate combat-gameplay-architect agent**

Read `.claude/agents/combat-gameplay-architect.md`. Update its knowledge base to include:
- The new engagement system rules
- The facing ownership model
- The movement state machine

- [ ] **Step 3: Commit**

```bash
git add .agent/skills/combat_system/SKILL.md .claude/agents/combat-gameplay-architect.md
git commit -m "docs(combat): update SKILL.md and agent with new engagement/positioning system"
```
