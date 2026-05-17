# Community Multi-Leader Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Migrate `Community.leader` (singular `Character` ref) → `Community.leaders : List<Character>`, strip the trait-gate + 4-friends-gate on `CharacterCommunity.CheckAndCreateCommunity`, add a first-class `Citizenship` field to `CharacterCommunity` with save round-trip, add `CharacterBlueprints.GrantBlueprint(BuildingSO)` / `HasBlueprint(BuildingSO)`, and document the new singular-vs-multi-leader pattern as a wiki gotcha — providing the data substrate that Plans 2-5 (BuildingGrid, AdministrativeBuilding, JobBuilder, Ambition_FoundACity, admin console UI) all build on.

**Architecture:**
- **`Community.leaders : List<Character>`** replaces the singular `leader` field. `PrimaryLeader => leaders[0]` and `IsLeader(Character c)` are the canonical accessors. Primary stays at index 0 by convention; list semantics make "primary leaves → secondary auto-promotes" a free `List.Remove` side-effect. `SetLeader(Character)` is deleted (mirrors the singular-owner-vs-multi-owner-isowner gotcha pattern).
- **`CharacterCommunity._citizenship : Community`** is a new server-side field. It rides the existing `_pendingCommunityMapId` resolution pattern via a new `_pendingCitizenshipMapId`. No live `NetworkVariable` for Plan 1 — citizenship round-trips through `CommunitySaveData.citizenshipMapId` only. The setter (`SetCitizenship`) gets called by `AdministrativeBuilding.OnFinalize` in Plan 4 (out of scope here); Plan 1 just exposes the API surface and the save channel.
- **`CharacterBlueprints.GrantBlueprint(BuildingSO so)` + `HasBlueprint(BuildingSO so)`** are thin wrappers around the existing `UnlockBuilding(string)` / `KnowsBlueprint(string)` keyed by `BuildingSO.PrefabId`. This makes `BuildingSO`-typed call sites (admin console, ambition steps, dev tools) type-safe without breaking the underlying `List<string>` storage that already round-trips through save.
- **`CommunityData.PrimaryLeaderId`** (new getter) replaces ad-hoc reads of `LeaderNpcId` in the CharacterCommunity save lookup. The legacy `LeaderNpcId` field stays for back-compat with old save files. New code reads `PrimaryLeaderId` which returns `LeaderIds[0]` with a `LeaderNpcId` fallback.

**Tech Stack:** Unity 6.0 / NGO 2.x, C# 9 / .NET Framework 4.8, NUnit EditMode tests via `tests-run` MCP tool, JSON save format. No new assemblies, no new dependencies.

**Rules enforced throughout:** CLAUDE.md rules #1-#8 (think first, walk every system the change touches), #9-#14 (SOLID, small interfaces), #15 (`_underscorePrefix`), #16 (clean up subscriptions), #18/#19/#19b (server-authoritative + late-joiner audit — written below), #20 (character save decoupling), #22 (player↔NPC parity through CharacterAction — no live writes touch this in Plan 1), #28/#29/#29b (SKILL + agent + wiki updates), #31 (try/catch on save deserialization), #34 (no per-frame allocs — N/A here, this is data shape only).

**Network safety audit (rule #19b — performed BEFORE writing the plan, recorded here):**
1. **Who writes `Community.leaders`?** Server-side only (`CommunityManager.CreateNewCommunity`, `Community` constructor, `RemoveMember`, future admin-console mutators). No client write paths.
2. **What replication channel?** None added in Plan 1 — `Community` is not a `NetworkBehaviour` today and we don't promote it. Same as the current `leader` field. Any future client-visible state of leadership rides through ClientRpc surfaces added by Plan 5 (admin console).
3. **Late-joiner sees?** Same as today — no live `Community` state is replicated; clients that need leadership info pull it via server-authoritative queries (existing pattern via `MapRegistry.GetCommunity(mapId).LeaderIds`, which is preserved). `CommunityData.LeaderIds` is already a save-data list field, no replication path.
4. **Client-side pre-gate?** `InteractionInviteCommunity.CanExecute` runs server-side (it's an `InteractionInvitation` subclass, only invoked from `CharacterInteraction.RequestInvitation` which is a `ServerRpc`). No client pre-gate uses `.leader`. Confirmed by grep below.
5. **`GetComponentInParent` spawn-race?** Not relevant — no new component is added to a prefab in Plan 1. All changes are pure data-shape on plain C# classes.
6. **`InteractableObject.IsCharacterInInteractionZone` (rule #36)?** N/A — Plan 1 doesn't add any new player↔interactable surface.

`Citizenship` follows the same pattern — written server-side only, round-trips via `CommunitySaveData`. The late-joiner repro for citizenship will run in Plan 4 (when `AdministrativeBuilding.OnFinalize` actually calls `SetCitizenship`). Plan 1's commit message will state explicitly: "no client-visible state added; full late-joiner repro deferred to Plan 4 when the citizenship writer ships."

---

## File Structure

**Modified files:**
- `Assets/Scripts/World/Community/Community.cs` — `leader` → `leaders`, accessors (`PrimaryLeader` / `SecondaryLeaders` / `IsLeader` / `Citizens`), constructor update, `RemoveMember` list-aware, `SetLeader` deleted, server-only mutators (`PromoteToSecondaryLeader` / `DemoteFromLeadership` / `TransferPrimaryLeadership`).
- `Assets/Scripts/World/Community/CommunityManager.cs` — no behavioral change; one line of comment alignment, mostly verification.
- `Assets/Scripts/World/MapSystem/MapRegistry.cs` (CommunityData class body around line 599) — add `PrimaryLeaderId` getter with `LeaderNpcId` fallback.
- `Assets/Scripts/Character/CharacterCommunity/CharacterCommunity.cs` — strip the `CanCreateCommunity()` trait + 4-friends gates from `CheckAndCreateCommunity`, replace 5 `.leader` callsites with `IsLeader(c)` / `PrimaryLeader`, add `_citizenship` field + `Citizenship` getter + `SetCitizenship` / `RenounceCitizenship` mutators + `_pendingCitizenshipMapId` resolution, wire round-trip in `Serialize` / `Deserialize`, update default community name from "Band of Friends"/"Small Group of Friends" → "Settlement".
- `Assets/Scripts/Character/SaveLoad/ProfileSaveData/CommunitySaveData.cs` — add `citizenshipMapId : string` field.
- `Assets/Scripts/Character/CharacterTraits/CharacterTraits.cs` — delete `CanCreateCommunity()` method.
- `Assets/Scripts/Character/CharacterTraits/CharacterBehavioralTraitsSO.cs` — delete `canCreateCommunity` field + `[Header("Abilities")]` block.
- `Assets/Scripts/Character/CharacterInteraction/InteractionInviteCommunity.cs` — replace 1 `.leader` callsite with `IsLeader(c)`.
- `Assets/Scripts/Debug/DevMode/Inspect/SubTabs/SkillsTraitsSubTab.cs` — remove the "Can Create Community" display line (the trait method no longer exists).
- `Assets/Scripts/Character/CharacterBlueprints/CharacterBlueprints.cs` — add `GrantBlueprint(BuildingSO)` + `HasBlueprint(BuildingSO)` overloads.

**New test files:**
- `Assets/Editor/Tests/Community/CommunityMultiLeaderTests.cs` — `IsLeader` / `PrimaryLeader` / `SecondaryLeaders` correctness, `RemoveMember` leadership-shift, `Citizens` filter.
- `Assets/Editor/Tests/Community/CitizenshipSaveRoundTripTests.cs` — `CommunitySaveData.citizenshipMapId` round-trips through `JsonUtility`.
- `Assets/Editor/Tests/Community/CharacterBlueprintsGrantTests.cs` — `GrantBlueprint(BuildingSO)` is idempotent + `HasBlueprint(BuildingSO)` consistent with `KnowsBlueprint(string)`.

**Behavioral-traits asset sweep (read-only check — NO edit unless the field exists in YAML):**
- Grep `Assets/Resources/Data/Behavioural Traits/*.asset` for the YAML line `canCreateCommunity:`. If any matches, the field is being persisted on existing assets — surface to user before deleting the C# field. **If the grep is empty**, no asset migration needed; proceed with the C# delete (the field deserializes to `false` on load and Unity ignores unknown YAML keys on serialize-out per the asmdef-free pattern).

**Docs updated:**
- `.agent/skills/community-system/SKILL.md` — multi-leader API table, IsLeader contract, Citizenship section.
- `wiki/systems/world-community.md` — `## Change log` line + `Public API` section refresh + `## State & persistence` Citizenship section + `## Gotchas` link to the new wikilink.
- `wiki/systems/character-community.md` — `## Change log` + Citizenship subsection + `## Public API` refresh.
- `wiki/concepts/citizenship.md` (NEW) — definition page following the concept template.
- `wiki/gotchas/singular-leader-vs-multi-leader-isleader.md` (NEW) — mirrored from `singular-owner-vs-multi-owner-isowner.md`.

**Out of scope (deferred per the spec; Plan 1 must NOT touch these):**
- `AdministrativeBuilding` field on `Community` / `IsChartered` → Plan 4.
- `TryPromoteLevel` / `TierPromotionResult` enum → Plan 4 (depends on AB + tier requirements).
- `Citizens` accessor reading `m.CharacterCommunity.Citizenship == this` IS in scope (the filter is data-only — the writer ships in Plan 4). The accessor returns an empty enumerable until someone calls `SetCitizenship`.
- Any UI surface for promote/demote/transfer (admin console) → Plan 5.
- `BuildingPlacementManager.RequestPlaceCityBlueprintServerRpc` and authorization gate → Plan 5.
- `CommunityData.AdministrativeBuildingNetId` → Plan 4.

---

## Task 1: Migrate `Community.leader` → `Community.leaders : List<Character>` + accessor API

**Files:**
- Modify: `Assets/Scripts/World/Community/Community.cs` (entire class rewrite)
- Create: `Assets/Editor/Tests/Community/CommunityMultiLeaderTests.cs`
- Create: `Assets/Editor/Tests/Community/Community.meta` folder via Unity refresh (auto-generated)

- [ ] **Step 1: Write the failing tests**

Create `Assets/Editor/Tests/Community/CommunityMultiLeaderTests.cs`:

```csharp
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MWI.Tests.Community
{
    public class CommunityMultiLeaderTests
    {
        private Character MakeBareCharacter(string name)
        {
            // Headless: a bare GameObject with a Character component is enough for
            // these reference-identity tests (Community stores Character refs).
            var go = new GameObject(name);
            return go.AddComponent<Character>();
        }

        [Test]
        public void Constructor_seeds_leaders_with_founder_only()
        {
            var founder = MakeBareCharacter("Founder");
            var c = new global::Community("Test", founder);

            Assert.AreEqual(1, c.leaders.Count, "Founder must be the only leader.");
            Assert.AreSame(founder, c.leaders[0]);
            Assert.AreSame(founder, c.PrimaryLeader);
            CollectionAssert.IsEmpty(c.SecondaryLeaders.ToList(), "No secondaries on fresh community.");
            Assert.IsTrue(c.IsLeader(founder));
            Assert.IsFalse(c.IsLeader(null), "IsLeader(null) must be false.");
        }

        [Test]
        public void IsLeader_returns_true_for_every_leader_in_the_roster()
        {
            var f = MakeBareCharacter("F");
            var s = MakeBareCharacter("S");
            var c = new global::Community("Test", f);
            c.AddMember(s);
            c.leaders.Add(s);

            Assert.IsTrue(c.IsLeader(f));
            Assert.IsTrue(c.IsLeader(s), "Secondary leader must satisfy IsLeader.");
            Assert.AreSame(f, c.PrimaryLeader);
            Assert.AreSame(s, c.SecondaryLeaders.First());
        }

        [Test]
        public void RemoveMember_removes_a_secondary_leader_without_changing_primary()
        {
            var f = MakeBareCharacter("F");
            var s = MakeBareCharacter("S");
            var c = new global::Community("Test", f);
            c.AddMember(s);
            c.leaders.Add(s);

            c.RemoveMember(s);

            Assert.IsFalse(c.leaders.Contains(s));
            Assert.AreSame(f, c.PrimaryLeader);
            Assert.IsFalse(c.IsLeader(s));
        }

        [Test]
        public void RemoveMember_when_primary_leaves_auto_promotes_first_secondary()
        {
            var f = MakeBareCharacter("F");
            var s = MakeBareCharacter("S");
            var c = new global::Community("Test", f);
            c.AddMember(s);
            c.leaders.Add(s);

            c.RemoveMember(f);

            Assert.AreEqual(1, c.leaders.Count);
            Assert.AreSame(s, c.PrimaryLeader,
                "Removing the primary at index 0 must shift the next leader into the primary slot.");
        }

        [Test]
        public void RemoveMember_when_sole_leader_leaves_leaves_community_leaderless()
        {
            var f = MakeBareCharacter("F");
            var c = new global::Community("Test", f);

            c.RemoveMember(f);

            Assert.AreEqual(0, c.leaders.Count);
            Assert.IsNull(c.PrimaryLeader);
            Assert.IsFalse(c.IsLeader(f));
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Use the `tests-run` MCP tool with `testMode: EditMode`, filter `MWI.Tests.Community.CommunityMultiLeaderTests`.
Expected: FAIL with compile errors (`Community` has no `leaders` / `PrimaryLeader` / `SecondaryLeaders` / `IsLeader`).

- [ ] **Step 3: Rewrite `Community.cs`**

Replace the entire contents of `Assets/Scripts/World/Community/Community.cs` with:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Server-side group container. Holds members, leaders (List — primary at index 0,
/// secondaries 1..n), a hierarchy of sub-communities, and territory references.
/// Not a NetworkBehaviour — clients that need community state pull through
/// MapRegistry / CommunityData. See [[wiki/gotchas/singular-leader-vs-multi-leader-isleader]].
/// </summary>
[System.Serializable]
public class Community
{
    public string communityName;
    public CommunityLevel level;

    [Header("Leadership")]
    /// <summary>
    /// All leaders of the community. <c>leaders[0]</c> is the primary (decision-of-last-resort);
    /// <c>leaders[1..]</c> are secondaries (can co-administer via the admin console — Plan 5).
    /// Multi-leader is a *capability* of every community; communities founded by a single founder
    /// begin with one entry and stay single-leader until a primary uses the admin console to promote.
    /// </summary>
    public List<Character> leaders = new List<Character>();

    [Header("Members")]
    public List<Character> members = new List<Character>();

    [Header("Hierarchy")]
    [NonSerialized] public Community parentCommunity;
    [NonSerialized] public List<Community> subCommunities = new List<Community>();

    [Header("Territory & Assets")]
    public List<Zone> communityZones = new List<Zone>();
    public List<Building> ownedBuildings = new List<Building>();

    // ── Convenience accessors ─────────────────────────────────────────────
    /// <summary>The primary leader (index 0), or null if the community is currently leaderless.</summary>
    public Character PrimaryLeader => leaders.Count > 0 ? leaders[0] : null;
    /// <summary>All leaders except the primary, in roster order.</summary>
    public IEnumerable<Character> SecondaryLeaders => leaders.Skip(1);
    /// <summary>
    /// Canonical "is this character a recognised leader?" predicate. Mirrors
    /// <c>Room.IsOwner(Character)</c> from the building hierarchy and is the
    /// authority-gate you should use for every leader-only feature.
    /// See [[wiki/gotchas/singular-leader-vs-multi-leader-isleader]] for the
    /// rationale — never compare against <c>PrimaryLeader</c> or <c>leaders[0]</c>
    /// directly for an auth check.
    /// </summary>
    public bool IsLeader(Character c) => c != null && leaders.Contains(c);

    /// <summary>
    /// Members whose <see cref="CharacterCommunity.Citizenship"/> is this community.
    /// Citizenship is granted by completing an AdministrativeBuilding (Plan 4).
    /// In Plan 1 this returns an empty enumerable until a writer ships.
    /// </summary>
    public IEnumerable<Character> Citizens => members.Where(m =>
        m != null
        && m.CharacterCommunity != null
        && m.CharacterCommunity.Citizenship == this);

    public Community(string name, Character founder)
    {
        communityName = name;
        leaders.Add(founder);   // founder = primary leader (index 0)
        level = CommunityLevel.SmallGroup;
        members.Add(founder);
    }

    public void AddMember(Character newMember)
    {
        if (!members.Contains(newMember))
        {
            members.Add(newMember);
            if (newMember != null && newMember.CharacterCommunity != null)
            {
                newMember.CharacterCommunity.SetCurrentCommunity(this);
            }
        }
    }

    public void RemoveMember(Character member)
    {
        if (!members.Contains(member)) return;

        members.Remove(member);
        if (member != null && member.CharacterCommunity != null)
        {
            // Unset only if it currently points to this community to avoid bugs when swapping
            if (member.CharacterCommunity.CurrentCommunity == this)
            {
                member.CharacterCommunity.SetCurrentCommunity(null);
            }
        }

        // Multi-leader-aware: removing a leader from the roster shifts list indices, which
        // is the same as "auto-promote next secondary to primary" (the next leader is now
        // at index 0). No-op if the community becomes leaderless — it stays so until a new
        // leader is appointed or it dissolves.
        if (leaders.Contains(member))
        {
            leaders.Remove(member);
        }
    }

    /// <summary>
    /// Adds a sub-community. Note that a parent only tracks its DIRECT children.
    /// </summary>
    public void AddSubCommunity(Community subComm)
    {
        if (subComm == null || subComm == this) return;

        if (!subCommunities.Contains(subComm))
        {
            // If it already has a parent, leave it first
            subComm.DeclareIndependence();

            subCommunities.Add(subComm);
            subComm.parentCommunity = this;
        }
    }

    /// <summary>
    /// Breaks the link with the parent community, making this community independent.
    /// </summary>
    public void DeclareIndependence()
    {
        if (parentCommunity != null)
        {
            Debug.Log($"<color=orange>[Community]</color> {communityName} has declared independence from {parentCommunity.communityName}!");
            parentCommunity.subCommunities.Remove(this);
            parentCommunity = null;
        }
    }

    // ── Server-only leadership mutators ───────────────────────────────────
    /// <summary>
    /// Server-only. Adds <paramref name="c"/> to the leader roster as a secondary.
    /// No-op if <paramref name="c"/> is null, not a member, or already a leader.
    /// Authority gate (primary-only): the caller is expected to check
    /// <c>IsLeader(callingCharacter) &amp;&amp; callingCharacter == PrimaryLeader</c>.
    /// </summary>
    public bool PromoteToSecondaryLeader(Character c)
    {
        if (c == null || !members.Contains(c) || leaders.Contains(c)) return false;
        leaders.Add(c);
        return true;
    }

    /// <summary>
    /// Server-only. Removes <paramref name="c"/> from the leader roster.
    /// No-op if <paramref name="c"/> is the primary (use <see cref="TransferPrimaryLeadership"/>
    /// to step down) or is not a leader.
    /// </summary>
    public bool DemoteFromLeadership(Character c)
    {
        if (c == null || c == PrimaryLeader || !leaders.Contains(c)) return false;
        leaders.Remove(c);
        return true;
    }

    /// <summary>
    /// Server-only. Moves <paramref name="newPrimary"/> to index 0 (primary slot).
    /// Requires <paramref name="newPrimary"/> to already be a leader. Old primary
    /// drops into the secondary slot they were displaced from.
    /// </summary>
    public bool TransferPrimaryLeadership(Character newPrimary)
    {
        if (newPrimary == null || !leaders.Contains(newPrimary)) return false;
        if (newPrimary == PrimaryLeader) return false;
        leaders.Remove(newPrimary);
        leaders.Insert(0, newPrimary);
        return true;
    }

    public void ChangeLevel(CommunityLevel newLevel)
    {
        level = newLevel;
        Debug.Log($"<color=green>[Community]</color> {communityName} has evolved to {level}!");
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Use `tests-run` with `testMode: EditMode`, filter `MWI.Tests.Community.CommunityMultiLeaderTests`.
Expected: PASS (5 tests).

If any compile errors remain (consumers of `Community.leader` or `Community.SetLeader`), do NOT chase them here — Task 2 owns the migration. Compile errors at this stage are expected.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/World/Community/Community.cs Assets/Editor/Tests/Community/
git commit -m "$(cat <<'EOF'
refactor(community): migrate Community.leader → Community.leaders : List<Character>

- Replace singular leader field with leaders list (primary at index 0)
- Add PrimaryLeader / SecondaryLeaders / IsLeader / Citizens accessors
- Add PromoteToSecondaryLeader / DemoteFromLeadership / TransferPrimaryLeadership
  server-only mutators
- RemoveMember list-aware: removing primary auto-promotes next via list semantics
- Delete SetLeader (mirrors singular-owner-vs-multi-owner-isowner gotcha pattern)
- Add EditMode tests in Assets/Editor/Tests/Community/

Network safety: no client-visible state changed. Community is not a
NetworkBehaviour; clients pull through MapRegistry / CommunityData. Full
late-joiner repro deferred to Plan 4 when the citizenship writer ships.

Plan 1 of 5 for the City Founding spec
(docs/superpowers/specs/2026-05-18-city-founding-and-administrative-building-design.md).
EOF
)"
```

---

## Task 2: Migrate every `.leader` callsite to `IsLeader` / `PrimaryLeader`

**Files:**
- Modify: `Assets/Scripts/Character/CharacterCommunity/CharacterCommunity.cs` (5 callsites)
- Modify: `Assets/Scripts/Character/CharacterInteraction/InteractionInviteCommunity.cs:13`

- [ ] **Step 1: Grep for every `.leader` reference**

Use Grep with pattern `\.leader\b` against `Assets/Scripts`. Expected matches (6 total):
- `CharacterCommunity.cs:38` — `_currentCommunity.leader == _character` (in `CheckAndCreateCommunity`)
- `CharacterCommunity.cs:86` — `_currentCommunity.leader == _character` (in `BreakFreeFromParent`)
- `CharacterCommunity.cs:117` — `_currentCommunity.leader != _character` (in `InviteToCommunity`)
- `CharacterCommunity.cs:129` — `_currentCommunity.leader != _character` (in `RemoveFromCommunity`)
- `CharacterCommunity.cs:154` — `_currentCommunity.leader != null && commData.IsLeader(_currentCommunity.leader.CharacterId)` (in `Serialize`)
- `InteractionInviteCommunity.cs:13` — `source.CharacterCommunity.CurrentCommunity.leader != source`

(Task 3 will rewrite `CheckAndCreateCommunity` entirely — the line 38 migration in this task is therefore a small repair the next task supersedes. Keep it correct in case Task 3 is reverted independently.)

- [ ] **Step 2: Migrate `CharacterCommunity.cs` callsites**

Edit `Assets/Scripts/Character/CharacterCommunity/CharacterCommunity.cs`:

Line 38 (inside `CheckAndCreateCommunity`):
```csharp
// FROM
if (_currentCommunity != null && _currentCommunity.leader == _character) return;
// TO
if (_currentCommunity != null && _currentCommunity.IsLeader(_character)) return;
```

Line 86 (inside `BreakFreeFromParent`):
```csharp
// FROM
if (_currentCommunity != null && _currentCommunity.leader == _character)
// TO
if (_currentCommunity != null && _currentCommunity.IsLeader(_character))
```

Lines 117 and 129 (inside `InviteToCommunity` and `RemoveFromCommunity`):
```csharp
// FROM
if (target == null || _currentCommunity == null || _currentCommunity.leader != _character) return;
// TO
if (target == null || _currentCommunity == null || !_currentCommunity.IsLeader(_character)) return;
```

Line 154 (inside `Serialize`):
```csharp
// FROM
// Match by leader — communities are uniquely led
if (_currentCommunity.leader != null && commData.IsLeader(_currentCommunity.leader.CharacterId))
// TO
// Match by primary leader — every chartered community has a unique primary.
// Multi-leader communities still resolve to a single CommunityData because
// LeaderIds[0] equals PrimaryLeader.CharacterId by construction.
if (_currentCommunity.PrimaryLeader != null && commData.IsLeader(_currentCommunity.PrimaryLeader.CharacterId))
```

- [ ] **Step 3: Migrate `InteractionInviteCommunity.cs`**

Edit `Assets/Scripts/Character/CharacterInteraction/InteractionInviteCommunity.cs`:

Line 13:
```csharp
// FROM
if (source.CharacterCommunity == null || 
    source.CharacterCommunity.CurrentCommunity == null || 
    source.CharacterCommunity.CurrentCommunity.leader != source)
// TO
if (source.CharacterCommunity == null || 
    source.CharacterCommunity.CurrentCommunity == null || 
    !source.CharacterCommunity.CurrentCommunity.IsLeader(source))
```

- [ ] **Step 4: Re-grep `\.leader\b` against `Assets/Scripts` — expect 0 matches**

Use Grep with pattern `\.leader\b` against `Assets/Scripts`. 
Expected: empty result (the only remaining reference should be inside `Assets/Editor/Tests/Community/` or the comment-only mention in `Community.cs`'s docstring — verify those are exactly the only matches if any appear).

If any other production-code match appears (especially outside the files this task lists), STOP and surface the file path. There's a callsite the discovery grep missed.

- [ ] **Step 5: Compile-check via Unity console**

Use the MCP tool `assets-refresh`, then `console-get-logs` filtering on `Compile`. 
Expected: no compile errors. (The `Community.leaders` API from Task 1 + the migrations in Steps 2-3 should be enough to make the whole project compile.)

- [ ] **Step 6: Re-run the Task 1 tests**

Use `tests-run` with `testMode: EditMode`, filter `MWI.Tests.Community.CommunityMultiLeaderTests`.
Expected: PASS (5 tests, unchanged).

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/Character/CharacterCommunity/CharacterCommunity.cs Assets/Scripts/Character/CharacterInteraction/InteractionInviteCommunity.cs
git commit -m "$(cat <<'EOF'
refactor(community): migrate every .leader callsite to IsLeader(c) / PrimaryLeader

- CharacterCommunity.cs: 5 callsites (CheckAndCreateCommunity, BreakFreeFromParent,
  InviteToCommunity, RemoveFromCommunity, Serialize)
- InteractionInviteCommunity.cs:13 (CanExecute leader gate)
- Grep `\.leader\b` against Assets/Scripts now returns zero production-code hits

Mirrors the singular-owner-vs-multi-owner-isowner gotcha pattern: never compare
against PrimaryLeader for an auth check — always go through IsLeader(c).
EOF
)"
```

---

## Task 3: Strip community-creation gates from `CharacterCommunity.CheckAndCreateCommunity`

**Files:**
- Modify: `Assets/Scripts/Character/CharacterCommunity/CharacterCommunity.cs` (`CheckAndCreateCommunity` body + default community name)

- [ ] **Step 1: Replace the `CheckAndCreateCommunity` body**

In `Assets/Scripts/Character/CharacterCommunity/CharacterCommunity.cs`, find the method (currently around lines 30-46) and replace it:

```csharp
/// <summary>
/// Founds a new community led by this character. The only gate is "not already
/// leading a community" — the trait + 4-friends prerequisites were lifted as part
/// of the city-founding redesign (Plan 1 of 5). Any character with the
/// Ambition_FoundACity active (Plan 3) or invoking the dev "Create Community"
/// button (out of scope here) reaches this method.
/// </summary>
public void CheckAndCreateCommunity()
{
    if (_character == null) return;

    // Sole guard: cannot lead two communities at once.
    if (_currentCommunity != null && _currentCommunity.IsLeader(_character)) return;

    CreateCommunity();
}
```

- [ ] **Step 2: Update the default community name in `CreateCommunity()`**

In the same file, find the `public void CreateCommunity()` method (currently around line 51) and change the default name:

```csharp
// FROM
string newCommName = $"{_character.CharacterName}'s Small Group of Friends";
// TO
string newCommName = $"{_character.CharacterName}'s Settlement";
```

Keep the rest of the method body intact.

- [ ] **Step 3: Update the XML doc comment on `CheckAndCreateCommunity`**

The original doc comment referenced the trait + 4 friends gates. Already replaced in Step 1; double-check the summary above reflects the new gating policy.

- [ ] **Step 4: Compile-check via Unity console**

Use `assets-refresh` then `console-get-logs` filtering on `Compile`.
Expected: no compile errors. The deletion of the `CharacterTraits.CanCreateCommunity()` reference and `CharacterRelation.GetFriendCount()` reference inside the method body means those APIs are now unused (the standalone APIs still exist — they remain valid for Task 4's deletion path).

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Character/CharacterCommunity/CharacterCommunity.cs
git commit -m "$(cat <<'EOF'
feat(community): strip trait + 4-friends gates from CheckAndCreateCommunity

The trait gate (`CanCreateCommunity`) and 4-friends gate were obstacles to
the Ambition_FoundACity (Plan 3) and dev-mode founding flows. Sole remaining
guard: cannot lead two communities at once.

Default community name changed from "X's Small Group of Friends" → "X's Settlement"
to match the city-founding terminology.

Plan 1 of 5 for the City Founding spec.
EOF
)"
```

---

## Task 4: Delete the `CanCreateCommunity` trait API surface

**Files:**
- Modify: `Assets/Scripts/Character/CharacterTraits/CharacterTraits.cs` (delete `CanCreateCommunity()` method)
- Modify: `Assets/Scripts/Character/CharacterTraits/CharacterBehavioralTraitsSO.cs` (delete `canCreateCommunity` field + `[Header("Abilities")]`)
- Modify: `Assets/Scripts/Debug/DevMode/Inspect/SubTabs/SkillsTraitsSubTab.cs` (remove the display line)

- [ ] **Step 1: Pre-check — search for any external references to the trait field**

Use Grep with pattern `canCreateCommunity` (case-sensitive) against the entire repo (not just Assets/Scripts) including `.asset` YAML files.

```
Grep glob: **/*.{cs,asset,meta}
```

Expected: only the three production-code files listed in this task + (possibly) one or more `Assets/Resources/Data/Behavioural Traits/*.asset` files where the field is persisted as YAML.

**If asset files match:** Unity ignores unknown YAML keys on serialize-out, and the next AssetDatabase save sweep will strip them. We do NOT need to edit those .asset files by hand — the field disappearance from the C# class is enough. Document the matched asset paths in the commit message so a future "asset hygiene pass" can clean them up.

**If only `.cs` files match:** proceed.

- [ ] **Step 2: Delete the trait method**

In `Assets/Scripts/Character/CharacterTraits/CharacterTraits.cs`, delete lines 34-40 (the entire `CanCreateCommunity` method including the doc comment):

```csharp
// DELETE this block:
/// <summary>
/// Checks if the character has the ability to found a community.
/// </summary>
public bool CanCreateCommunity()
{
    return behavioralTraitsProfile != null ? behavioralTraitsProfile.canCreateCommunity : false;
}
```

- [ ] **Step 3: Delete the SO field + header**

In `Assets/Scripts/Character/CharacterTraits/CharacterBehavioralTraitsSO.cs`, delete the `[Header("Abilities")]` and `canCreateCommunity` lines (lines 19-21):

```csharp
// DELETE this block:
    [Header("Abilities")]
    [Tooltip("Can this character found a new community?")]
    public bool canCreateCommunity = false;
```

- [ ] **Step 4: Remove the dev-tools display line**

In `Assets/Scripts/Debug/DevMode/Inspect/SubTabs/SkillsTraitsSubTab.cs`, around line 20, delete the line:

```csharp
// DELETE this line:
sb.AppendLine($"  Can Create Community: {traits.CanCreateCommunity()}");
```

- [ ] **Step 5: Compile-check via Unity console**

Use `assets-refresh` then `console-get-logs` filtering on `Compile`.
Expected: no compile errors. The trait method is now gone; nothing references it.

- [ ] **Step 6: Re-grep `CanCreateCommunity` and `canCreateCommunity` against `Assets/Scripts`**

Use Grep with pattern `CanCreateCommunity|canCreateCommunity` against `Assets/Scripts`. 
Expected: zero matches in `Assets/Scripts/**/*.cs`. (Matches in `.asset` YAML files are tolerated — Step 1 documented those.)

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/Character/CharacterTraits/CharacterTraits.cs Assets/Scripts/Character/CharacterTraits/CharacterBehavioralTraitsSO.cs Assets/Scripts/Debug/DevMode/Inspect/SubTabs/SkillsTraitsSubTab.cs
git commit -m "$(cat <<'EOF'
feat(traits): delete CanCreateCommunity trait — replaced by ambition-driven founding

Removed:
- CharacterTraits.CanCreateCommunity() method
- CharacterBehavioralTraitsSO.canCreateCommunity field + Abilities header
- SkillsTraitsSubTab "Can Create Community" inspector line

Existing BehaviouralTraits .asset YAML may still contain `canCreateCommunity: 0/1`
entries — Unity ignores unknown YAML keys on load and strips them on next save.
No runtime impact; an asset-hygiene sweep can clean them up later.

Plan 1 of 5 for the City Founding spec.
EOF
)"
```

---

## Task 5: Add `CharacterCommunity._citizenship` field + `SetCitizenship` / `RenounceCitizenship`

**Files:**
- Modify: `Assets/Scripts/Character/CharacterCommunity/CharacterCommunity.cs`

- [ ] **Step 1: Add the field + accessors + mutators**

In `Assets/Scripts/Character/CharacterCommunity/CharacterCommunity.cs`, find the existing field block (around lines 10-15):

```csharp
private Community _currentCommunity;

/// <summary>
/// Saved community map ID from deserialization, resolved lazily at runtime.
/// </summary>
private string _pendingCommunityMapId;

public Character Character => _character;
public Community CurrentCommunity => _currentCommunity;
```

Replace it with the expanded block:

```csharp
private Community _currentCommunity;
private Community _citizenship;

/// <summary>
/// Saved community map ID from deserialization, resolved lazily at runtime.
/// </summary>
private string _pendingCommunityMapId;

/// <summary>
/// Saved citizenship map ID from deserialization, resolved lazily at runtime.
/// Mirrors the <c>_pendingCommunityMapId</c> pattern — the live Community
/// reference is rebound when MapRegistry has surfaced the matching CommunityData.
/// </summary>
private string _pendingCitizenshipMapId;

public Character Character => _character;
public Community CurrentCommunity => _currentCommunity;

/// <summary>
/// The community of which this character is a *citizen* (sticky — granted by
/// completing an <c>AdministrativeBuilding</c> in Plan 4). Distinct from
/// <see cref="CurrentCommunity"/> (which is the community the character is
/// currently a *member* of, transient).
/// </summary>
public Community Citizenship => _citizenship;
```

- [ ] **Step 2: Add server-only mutators after the existing `SetCurrentCommunity` method**

Locate `public void SetCurrentCommunity(Community newCommunity)` (around line 92). After its closing brace, add:

```csharp
/// <summary>
/// Server-only. Grants citizenship to <paramref name="c"/>. If the character was
/// already a citizen of a different community, that previous citizenship is
/// implicitly renounced (no double-citizenship in v1).
/// Called by <c>AdministrativeBuilding.OnFinalize</c> on the founder, and by
/// <c>JoinRequestDesk</c> when a join request is accepted (both ship in Plan 4).
/// </summary>
public void SetCitizenship(Community c)
{
    if (_citizenship == c) return;
    _citizenship = c;
}

/// <summary>
/// Server-only. Clears citizenship. Used when a character formally leaves a city
/// (UI exit gesture in Plan 5) or when a community dissolves.
/// </summary>
public void RenounceCitizenship()
{
    _citizenship = null;
}
```

- [ ] **Step 3: Compile-check via Unity console**

Use `assets-refresh` then `console-get-logs` filtering on `Compile`.
Expected: no compile errors.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Character/CharacterCommunity/CharacterCommunity.cs
git commit -m "$(cat <<'EOF'
feat(community): add Citizenship field + SetCitizenship / RenounceCitizenship

- CharacterCommunity._citizenship : Community (server-side)
- Citizenship getter
- SetCitizenship(Community) / RenounceCitizenship() server-only mutators
- _pendingCitizenshipMapId mirrors _pendingCommunityMapId for save round-trip

No live writer in Plan 1 — the AdministrativeBuilding.OnFinalize call site
ships in Plan 4. Plan 1 only exposes the API surface and the save channel
(round-trip wiring lands in Task 6 of this plan).

Plan 1 of 5 for the City Founding spec.
EOF
)"
```

---

## Task 6: Wire `citizenshipMapId` through `CommunitySaveData` round-trip

**Files:**
- Modify: `Assets/Scripts/Character/SaveLoad/ProfileSaveData/CommunitySaveData.cs`
- Modify: `Assets/Scripts/Character/CharacterCommunity/CharacterCommunity.cs` (Serialize + Deserialize)
- Modify: `Assets/Scripts/World/MapSystem/MapRegistry.cs` (CommunityData class — add `PrimaryLeaderId` getter)
- Create: `Assets/Editor/Tests/Community/CitizenshipSaveRoundTripTests.cs`

- [ ] **Step 1: Write the failing test**

Create `Assets/Editor/Tests/Community/CitizenshipSaveRoundTripTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;

namespace MWI.Tests.Community
{
    public class CitizenshipSaveRoundTripTests
    {
        [Test]
        public void CommunitySaveData_default_citizenshipMapId_is_null_or_empty()
        {
            var d = new CommunitySaveData();
            Assert.IsTrue(string.IsNullOrEmpty(d.citizenshipMapId),
                "Brand-new CommunitySaveData must default citizenshipMapId to null/empty so legacy saves deserialize cleanly.");
        }

        [Test]
        public void CommunitySaveData_citizenshipMapId_round_trips_through_JsonUtility()
        {
            var d = new CommunitySaveData
            {
                communityMapId = "current-map",
                citizenshipMapId = "citizen-map"
            };
            string json = JsonUtility.ToJson(d);
            var back = JsonUtility.FromJson<CommunitySaveData>(json);
            Assert.AreEqual("current-map", back.communityMapId);
            Assert.AreEqual("citizen-map", back.citizenshipMapId);
        }

        [Test]
        public void Legacy_json_without_citizenshipMapId_deserializes_to_empty()
        {
            // Snapshot of the pre-Plan-1 save shape — only communityMapId existed.
            string legacy = "{\"communityMapId\":\"legacy-map\"}";
            var d = JsonUtility.FromJson<CommunitySaveData>(legacy);
            Assert.AreEqual("legacy-map", d.communityMapId);
            Assert.IsTrue(string.IsNullOrEmpty(d.citizenshipMapId),
                "Legacy saves with no citizenshipMapId field must deserialize to null/empty (additive field, back-compatible).");
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Use `tests-run` with `testMode: EditMode`, filter `MWI.Tests.Community.CitizenshipSaveRoundTripTests`.
Expected: FAIL with "CommunitySaveData does not contain a definition for citizenshipMapId".

- [ ] **Step 3: Add the field**

Replace the entire contents of `Assets/Scripts/Character/SaveLoad/ProfileSaveData/CommunitySaveData.cs` with:

```csharp
[System.Serializable]
public class CommunitySaveData
{
    /// <summary>
    /// MapId of the community this character is currently a *member* of.
    /// Resolved lazily at runtime by <c>CharacterCommunity.Deserialize</c> via
    /// <c>MapRegistry.Instance.GetAllCommunities()</c>.
    /// </summary>
    public string communityMapId;

    /// <summary>
    /// MapId of the community this character is a *citizen* of (sticky — granted
    /// by completing an <c>AdministrativeBuilding</c>, see Plan 4).
    /// Resolved lazily by <c>CharacterCommunity.Deserialize</c>. Defaults to empty
    /// for legacy saves authored before 2026-05-17 (additive field).
    /// </summary>
    public string citizenshipMapId;
}
```

- [ ] **Step 4: Run the test to verify it passes**

Use `tests-run` with `testMode: EditMode`, filter `MWI.Tests.Community.CitizenshipSaveRoundTripTests`.
Expected: PASS (3 tests).

- [ ] **Step 5: Add `PrimaryLeaderId` getter on `CommunityData`**

In `Assets/Scripts/World/MapSystem/MapRegistry.cs`, locate `public class CommunityData` (around line 599). After `public bool IsLeader(string characterId)` (around line 629) and before `public void AddLeader(string characterId)`, add:

```csharp
        /// <summary>
        /// Primary leader ID — <c>LeaderIds[0]</c> if any, otherwise the legacy
        /// <c>LeaderNpcId</c> (back-compat for save files authored before the
        /// multi-leader migration). Returns null if the community is leaderless.
        /// </summary>
        public string PrimaryLeaderId
        {
            get
            {
                if (LeaderIds != null && LeaderIds.Count > 0) return LeaderIds[0];
                return string.IsNullOrEmpty(LeaderNpcId) ? null : LeaderNpcId;
            }
        }
```

- [ ] **Step 6: Wire citizenship round-trip in `CharacterCommunity.Serialize` / `Deserialize`**

In `Assets/Scripts/Character/CharacterCommunity/CharacterCommunity.cs`, locate `public CommunitySaveData Serialize()` (around line 141). Replace its body with the augmented version that also writes citizenshipMapId:

```csharp
public CommunitySaveData Serialize()
{
    var data = new CommunitySaveData();

    // --- communityMapId (existing) ---
    if (_currentCommunity != null)
    {
        string mapId = "";
        if (MWI.WorldSystem.MapRegistry.Instance != null)
        {
            foreach (var commData in MWI.WorldSystem.MapRegistry.Instance.GetAllCommunities())
            {
                // Match by primary leader — every chartered community has a unique primary.
                if (_currentCommunity.PrimaryLeader != null && commData.IsLeader(_currentCommunity.PrimaryLeader.CharacterId))
                {
                    mapId = commData.MapId;
                    break;
                }
            }
        }
        data.communityMapId = mapId;
    }
    else if (!string.IsNullOrEmpty(_pendingCommunityMapId))
    {
        data.communityMapId = _pendingCommunityMapId;
    }

    // --- citizenshipMapId (new) ---
    if (_citizenship != null)
    {
        string mapId = "";
        if (MWI.WorldSystem.MapRegistry.Instance != null)
        {
            foreach (var commData in MWI.WorldSystem.MapRegistry.Instance.GetAllCommunities())
            {
                if (_citizenship.PrimaryLeader != null && commData.IsLeader(_citizenship.PrimaryLeader.CharacterId))
                {
                    mapId = commData.MapId;
                    break;
                }
            }
        }
        data.citizenshipMapId = mapId;
    }
    else if (!string.IsNullOrEmpty(_pendingCitizenshipMapId))
    {
        data.citizenshipMapId = _pendingCitizenshipMapId;
    }

    return data;
}
```

Then locate `public void Deserialize(CommunitySaveData data)` and replace its body:

```csharp
public void Deserialize(CommunitySaveData data)
{
    if (data == null) return;

    _pendingCommunityMapId = data.communityMapId;
    _pendingCitizenshipMapId = data.citizenshipMapId;

    // Community + Citizenship references are resolved at runtime when the map
    // loads and MapRegistry becomes available. Defensive try/catch lives in
    // whichever subsystem performs the late-rebind (rule #31).
}
```

- [ ] **Step 7: Compile-check via Unity console**

Use `assets-refresh` then `console-get-logs` filtering on `Compile`.
Expected: no compile errors.

- [ ] **Step 8: Re-run all Community tests**

Use `tests-run` with `testMode: EditMode`, filter `MWI.Tests.Community.*`.
Expected: PASS (8 tests total — 5 from Task 1, 3 from this task).

- [ ] **Step 9: Commit**

```bash
git add Assets/Scripts/Character/SaveLoad/ProfileSaveData/CommunitySaveData.cs Assets/Scripts/Character/CharacterCommunity/CharacterCommunity.cs Assets/Scripts/World/MapSystem/MapRegistry.cs Assets/Editor/Tests/Community/CitizenshipSaveRoundTripTests.cs
git commit -m "$(cat <<'EOF'
feat(community): round-trip citizenship through CommunitySaveData

- CommunitySaveData.citizenshipMapId (additive — legacy saves deserialize to empty)
- CharacterCommunity.Serialize / Deserialize wire the new field via the same
  PrimaryLeader-id matching pattern used for communityMapId
- CommunityData.PrimaryLeaderId getter (LeaderIds[0] with LeaderNpcId fallback)
- EditMode tests: default empty + JsonUtility round-trip + legacy-JSON tolerance

No new replication channel; citizenship is server-side state persisted via
the existing CommunitySaveData profile save pipeline.

Plan 1 of 5 for the City Founding spec.
EOF
)"
```

---

## Task 7: Add `CharacterBlueprints.GrantBlueprint(BuildingSO)` + `HasBlueprint(BuildingSO)`

**Files:**
- Modify: `Assets/Scripts/Character/CharacterBlueprints/CharacterBlueprints.cs`
- Create: `Assets/Editor/Tests/Community/CharacterBlueprintsGrantTests.cs`

- [ ] **Step 1: Write the failing test**

Create `Assets/Editor/Tests/Community/CharacterBlueprintsGrantTests.cs`:

```csharp
using NUnit.Framework;
using MWI.WorldSystem;
using UnityEngine;

namespace MWI.Tests.Community
{
    public class CharacterBlueprintsGrantTests
    {
        private static BuildingSO MakeSO(string prefabId)
        {
            var so = ScriptableObject.CreateInstance<BuildingSO>();
            // BuildingSO._prefabId is private — set via reflection so the test
            // doesn't require a public test seam (mirrors how Buildings tests
            // construct SOs headlessly).
            typeof(BuildingSO)
                .GetField("_prefabId", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(so, prefabId);
            return so;
        }

        [Test]
        public void GrantBlueprint_adds_PrefabId_to_unlocked_list_and_HasBlueprint_returns_true()
        {
            var go = new GameObject("Char");
            var bp = go.AddComponent<CharacterBlueprints>();
            var so = MakeSO("AdministrativeBuilding");

            Assert.IsFalse(bp.HasBlueprint(so));
            bp.GrantBlueprint(so);
            Assert.IsTrue(bp.HasBlueprint(so));
            Assert.IsTrue(bp.KnowsBlueprint("AdministrativeBuilding"),
                "String-keyed lookup must agree with SO-keyed lookup.");
        }

        [Test]
        public void GrantBlueprint_is_idempotent_no_duplicate_entries()
        {
            var go = new GameObject("Char");
            var bp = go.AddComponent<CharacterBlueprints>();
            var so = MakeSO("House");

            bp.GrantBlueprint(so);
            bp.GrantBlueprint(so);
            bp.GrantBlueprint(so);

            int countOfHouse = 0;
            foreach (var id in bp.UnlockedBuildingIds)
            {
                if (id == "House") countOfHouse++;
            }
            Assert.AreEqual(1, countOfHouse, "GrantBlueprint must be idempotent.");
        }

        [Test]
        public void GrantBlueprint_null_or_empty_is_noop()
        {
            var go = new GameObject("Char");
            var bp = go.AddComponent<CharacterBlueprints>();

            bp.GrantBlueprint(null);
            Assert.AreEqual(0, bp.UnlockedBuildingIds.Count,
                "GrantBlueprint(null) must be a silent no-op (defensive, rule #31).");

            var so = MakeSO(""); // empty PrefabId
            bp.GrantBlueprint(so);
            Assert.AreEqual(0, bp.UnlockedBuildingIds.Count,
                "GrantBlueprint(SO with empty PrefabId) must be a silent no-op.");
        }

        [Test]
        public void HasBlueprint_null_or_empty_returns_false()
        {
            var go = new GameObject("Char");
            var bp = go.AddComponent<CharacterBlueprints>();

            Assert.IsFalse(bp.HasBlueprint(null));

            var so = MakeSO("");
            Assert.IsFalse(bp.HasBlueprint(so));
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Use `tests-run` with `testMode: EditMode`, filter `MWI.Tests.Community.CharacterBlueprintsGrantTests`.
Expected: FAIL with "CharacterBlueprints does not contain a definition for GrantBlueprint".

- [ ] **Step 3: Add the SO overloads**

In `Assets/Scripts/Character/CharacterBlueprints/CharacterBlueprints.cs`, after the existing `KnowsBlueprint(string buildingId)` method (around line 63), add:

```csharp
    /// <summary>
    /// Server-only. Grants knowledge of a building by SO (the preferred call surface for
    /// new code — keeps callers type-safe). Idempotent — a second grant of the same SO is
    /// a silent no-op. Used by <c>CharacterCommunity.CreateCommunity</c> (Plan 1) to seed
    /// the founder with the AB blueprint, and by tier-up unlock flows (Plan 4).
    /// </summary>
    public void GrantBlueprint(BuildingSO so)
    {
        if (so == null) return;
        UnlockBuilding(so.PrefabId);
    }

    /// <summary>
    /// SO-typed convenience predicate. Equivalent to <see cref="KnowsBlueprint(string)"/>
    /// with <c>so.PrefabId</c> but null-safe on the SO ref.
    /// </summary>
    public bool HasBlueprint(BuildingSO so)
    {
        if (so == null || string.IsNullOrEmpty(so.PrefabId)) return false;
        return KnowsBlueprint(so.PrefabId);
    }
```

- [ ] **Step 4: Run the test to verify it passes**

Use `tests-run` with `testMode: EditMode`, filter `MWI.Tests.Community.CharacterBlueprintsGrantTests`.
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Character/CharacterBlueprints/CharacterBlueprints.cs Assets/Editor/Tests/Community/CharacterBlueprintsGrantTests.cs
git commit -m "$(cat <<'EOF'
feat(blueprints): add CharacterBlueprints.GrantBlueprint(BuildingSO) + HasBlueprint

SO-typed overloads delegate to existing UnlockBuilding(string) / KnowsBlueprint(string)
keyed by BuildingSO.PrefabId. Type-safe surface for the admin-console grant flow
(Plan 5) and ambition-driven blueprint unlocks (Plan 3/4).

Idempotent + null-safe + empty-PrefabId-safe per rule #31.

EditMode tests cover: SO grant + idempotency + null/empty no-op + null HasBlueprint.

Plan 1 of 5 for the City Founding spec.
EOF
)"
```

---

## Task 8: Documentation + Wiki + Skill updates (Rules #28, #29, #29b)

**Files:**
- Modify: `.agent/skills/community-system/SKILL.md`
- Modify: `wiki/systems/world-community.md`
- Modify: `wiki/systems/character-community.md`
- Create: `wiki/concepts/citizenship.md`
- Create: `wiki/gotchas/singular-leader-vs-multi-leader-isleader.md`

- [ ] **Step 1: Read the wiki style for the existing gotcha**

Re-read `wiki/gotchas/singular-owner-vs-multi-owner-isowner.md` (already loaded in this session; reread to be sure formatting is mirrored). Pay attention to: frontmatter, the "When this bites you" / "The fix" / "Why this is sneaky" / "Where the canonical predicate lives" / "Audit list" / "Network safety" sections.

- [ ] **Step 2: Create the new gotcha**

Write `wiki/gotchas/singular-leader-vs-multi-leader-isleader.md`:

```markdown
---
type: gotcha
title: "Singular `PrimaryLeader` getter vs multi-leader `IsLeader(Character)` check"
tags: [community, leader, auth, multi-leader, networking]
created: 2026-05-17
updated: 2026-05-17
sources:
  - "[Community.cs](../../Assets/Scripts/World/Community/Community.cs)"
  - "[singular-owner-vs-multi-owner-isowner.md](singular-owner-vs-multi-owner-isowner.md)"
related:
  - "[[world-community]]"
  - "[[character-community]]"
  - "[[singular-owner-vs-multi-owner-isowner]]"
status: open
confidence: high
---

# Singular `PrimaryLeader` getter vs multi-leader `IsLeader(Character)` check

## Summary
`Community` (the server-side group container) supports **multiple leaders** via
`public List<Character> leaders` (primary at index 0, secondaries 1..n).
`Community.PrimaryLeader` is a convenience getter that returns **only the first
entry** (`leaders[0]`). Using it for auth checks (`if (community.PrimaryLeader != character)`)
silently rejects every leader except the primary — including any secondary
leader added via `PromoteToSecondaryLeader` (the canonical multi-leader mutation
path that the admin console's Leaders tab in Plan 5 uses). The correct predicate
is `Community.IsLeader(Character)`, which checks the full `leaders` list.

## When this bites you
- Symptom: a freshly-promoted secondary leader can't open / use a feature gated
  by a "leader" check. The toast / log message says something like "Only the
  leader can …".
- Root cause: the auth gate is written as `community.PrimaryLeader == character`
  or `community.leaders[0] == character`. Both compare against the primary only.
- Reproduction: in Plan 5's admin console, promote a second character to
  secondary leader, switch control to that secondary, attempt the gated action.
  The primary works; the secondary is rejected.

## The fix
Replace `community.PrimaryLeader == character` / `community.leaders[0] == character`
with `community.IsLeader(character)` / `!community.IsLeader(character)`.

```csharp
// WRONG — singular primary check, blocks secondaries.
if (community.PrimaryLeader != character) { … reject … }

// RIGHT — multi-leader-aware, null-safe.
if (!community.IsLeader(character)) { … reject … }
```

`IsLeader(null)` returns false safely.

## Why this is sneaky
- `PrimaryLeader` returns a non-null `Character` for the founder, which makes
  the line *look* correct in code review — there's no obvious null-handling
  smell.
- Until Plan 5 ships, every community has exactly one leader (the founder), so
  `PrimaryLeader == character` and `IsLeader(character)` produce the same
  result on every test path. The bug stays latent until a second leader is
  promoted.
- The pattern compiles, runs, and the auth toast you authored is exactly the
  toast that fires — so the developer who hits it assumes the gate is "working
  correctly" and looks for the bug elsewhere (e.g. "did promotion actually
  save?").

## Where the canonical predicate lives
- `Community.IsLeader(Character)` at `Assets/Scripts/World/Community/Community.cs`
  (around line 70 post-migration) — `c != null && leaders.Contains(c)`.
  Reference-equality comparison (Character is a `MonoBehaviour`, identity is fine).
- The matching string-keyed predicate on the save side:
  `MWI.WorldSystem.CommunityData.IsLeader(string)` at
  `Assets/Scripts/World/MapSystem/MapRegistry.cs` (around line 625) —
  `LeaderIds.Contains(characterId)`.

## Audit list (post-migration)
The 2026-05-17 multi-leader migration (Plan 1 of City Founding) swept every
`.leader` callsite. Production code now uses `IsLeader(c)` exclusively:
- `Assets/Scripts/Character/CharacterCommunity/CharacterCommunity.cs` (5 sites)
- `Assets/Scripts/Character/CharacterInteraction/InteractionInviteCommunity.cs:13`

Future leader-gated surfaces (Plans 4-5: admin console buttons, AB-furniture
interactions, join-request accept/decline, tier-up triggers) must use
`IsLeader(c)` from day one.

## Network safety
- `Community.leaders` is a plain `List<Character>` on a server-only class
  (Community is NOT a `NetworkBehaviour`).
- Clients that need to display leadership info pull through `MapRegistry.GetCommunity(mapId).LeaderIds`
  (a save-data field) or, for live state, through server-authoritative queries
  added by Plan 5.
- No replication channel is added by the migration itself — predicate swap only.
- Late-joiner repro: deferred to Plan 4/5 when the first client-visible
  leadership UI ships.

## Links
- [[world-community]]
- [[character-community]]
- [[singular-owner-vs-multi-owner-isowner]] — the sister gotcha that motivated
  this one (same pattern, different domain).

## Sources
- [Community.cs](../../Assets/Scripts/World/Community/Community.cs) — the source of truth.
- [singular-owner-vs-multi-owner-isowner.md](singular-owner-vs-multi-owner-isowner.md) — direct stylistic ancestor.
- [docs/superpowers/specs/2026-05-18-city-founding-and-administrative-building-design.md](../../docs/superpowers/specs/2026-05-18-city-founding-and-administrative-building-design.md) §"Files Changes Summary" — the spec line that mandated this gotcha (line 1348).
- [docs/superpowers/plans/2026-05-17-community-multi-leader-foundation.md](../../docs/superpowers/plans/2026-05-17-community-multi-leader-foundation.md) §Task 8 — the implementation pass that swept the callsites.
```

- [ ] **Step 3: Create the citizenship concept page**

Write `wiki/concepts/citizenship.md`:

```markdown
---
type: concept
title: "Citizenship"
tags: [community, character, city-founding, administrative-building]
created: 2026-05-17
updated: 2026-05-17
sources:
  - "[CharacterCommunity.cs](../../Assets/Scripts/Character/CharacterCommunity/CharacterCommunity.cs)"
  - "[CommunitySaveData.cs](../../Assets/Scripts/Character/SaveLoad/ProfileSaveData/CommunitySaveData.cs)"
related:
  - "[[character-community]]"
  - "[[world-community]]"
status: draft
confidence: medium
---

# Citizenship

## Summary
**Citizenship** is the sticky, formal "you belong to this city" relationship
between a `Character` and a `Community`. It is **distinct from membership**:
membership (`CharacterCommunity.CurrentCommunity`) is the community a character
is *currently in* (transient — can change every time they move maps);
citizenship (`CharacterCommunity.Citizenship`) is the community that has
**granted them civic status** (sticky — only changes on a deliberate gesture).

Membership is required to do things in a community's territory; citizenship is
required to access **civic privileges** (voting on tier-up, holding leadership,
appearing in the city's `Citizens` accessor, future tax/welfare hooks).

## Lifecycle

1. **Grant**: a character is granted citizenship when an
   `AdministrativeBuilding.OnFinalize` runs for an AB they founded (Plan 4), or
   when a `JoinRequestDesk` accepts their join request (Plan 4).
2. **Hold**: the `_citizenship : Community` field on `CharacterCommunity` holds
   the reference. The matching map ID is round-tripped through
   `CommunitySaveData.citizenshipMapId`.
3. **Renounce**: the character calls `RenounceCitizenship` (UI-driven leave
   gesture, Plan 5) or the community dissolves. A second `SetCitizenship` call
   implicitly renounces the prior citizenship — no double-citizenship in v1.

## Save semantics

`CharacterCommunity` writes `data.citizenshipMapId` in `Serialize` by matching
`_citizenship.PrimaryLeader.CharacterId` against `CommunityData.IsLeader(...)`.
On load, the raw map ID lands in `_pendingCitizenshipMapId`; the live `Community`
reference is rebound when `MapRegistry` surfaces the matching `CommunityData`
(deferred late-bind pattern, mirrors `_pendingCommunityMapId`).

Legacy saves (no `citizenshipMapId` field) deserialize to an empty string and
result in `_citizenship = null`.

## Why citizenship and membership are separate

- **Membership is automatic** (you joined a community by moving into it, by
  invitation, or by being born there).
- **Citizenship is deliberate** (a leader formally accepted you, or you
  completed a founding gesture).
- A drifter who is a member of a city for a few days while looking for work is
  NOT a citizen until accepted. They appear in `community.members` but not in
  `community.Citizens`.
- A citizen who travels to another city for a season is a member of the host
  city (`CurrentCommunity = host`) but still a citizen of home
  (`Citizenship = home`).

## Open questions / TODO
- *Plan 5 — dual-citizenship?* Spec line 1431 hints v1 does NOT support
  it ("no double-citizenship in v1"). Future versions might allow it for
  marriage / alliance scenarios.
- *Plan 4 — tier-up vote*: when a tier-up requires a vote, is the vote among
  `Community.Citizens` or `Community.members`? Spec defers; place-holder for
  Plan 4.

## Sources
- [CharacterCommunity.cs](../../Assets/Scripts/Character/CharacterCommunity/CharacterCommunity.cs) — `_citizenship` field, `SetCitizenship`, `RenounceCitizenship`.
- [CommunitySaveData.cs](../../Assets/Scripts/Character/SaveLoad/ProfileSaveData/CommunitySaveData.cs) — `citizenshipMapId` round-trip.
- [Community.cs](../../Assets/Scripts/World/Community/Community.cs) — `Citizens` accessor (filtered view of members).
- [docs/superpowers/specs/2026-05-18-city-founding-and-administrative-building-design.md](../../docs/superpowers/specs/2026-05-18-city-founding-and-administrative-building-design.md) §"`CharacterCommunity.Citizenship`" — design source.
- [docs/superpowers/plans/2026-05-17-community-multi-leader-foundation.md](../../docs/superpowers/plans/2026-05-17-community-multi-leader-foundation.md) — Plan 1 implementation.
```

- [ ] **Step 4: Update `wiki/systems/world-community.md`**

Read the file first (`wiki/systems/world-community.md`), then:

- Bump `updated:` to `2026-05-17`.
- In `## Public API`, replace any mention of `Community.leader` (singular) with `Community.leaders : List<Character>` + `PrimaryLeader` / `SecondaryLeaders` / `IsLeader(c)` accessors. Note the deletion of `SetLeader` and the addition of `PromoteToSecondaryLeader` / `DemoteFromLeadership` / `TransferPrimaryLeadership`.
- In `## State & persistence`, add a brief Citizenship subsection noting that `Community.Citizens` returns members filtered by `m.CharacterCommunity.Citizenship == this`.
- In `## Known gotchas / edge cases`, add a wikilink to `[[singular-leader-vs-multi-leader-isleader]]`.
- In `## Change log`, append:
  ```
  - 2026-05-17 — multi-leader migration: leader → leaders : List<Character>, IsLeader(c) is canonical, SetLeader deleted, PromoteToSecondaryLeader / DemoteFromLeadership / TransferPrimaryLeadership added. — claude
  ```
- If `depends_on` / `depended_on_by` / `related` don't yet include `[[citizenship]]` and `[[character-community]]`, add them.

- [ ] **Step 5: Update `wiki/systems/character-community.md`**

Read the file first, then:

- Bump `updated:` to `2026-05-17`.
- In `## Public API`, add `Citizenship : Community`, `SetCitizenship(Community)`, `RenounceCitizenship()`.
- Add a `## Citizenship` subsection that briefly explains the field (point to `[[citizenship]]` concept page for details).
- In `## State & persistence`, note that `CommunitySaveData.citizenshipMapId` round-trips through the existing profile-save pipeline; `_pendingCitizenshipMapId` mirrors `_pendingCommunityMapId`.
- In `## Change log`:
  ```
  - 2026-05-17 — added Citizenship field + SetCitizenship / RenounceCitizenship; CommunitySaveData.citizenshipMapId; default community name "Settlement"; stripped trait + 4-friends gates from CheckAndCreateCommunity. — claude
  ```
- Ensure `related:` includes `[[citizenship]]` and `[[singular-leader-vs-multi-leader-isleader]]`.

- [ ] **Step 6: Update `.agent/skills/community-system/SKILL.md`**

Read the file first (`.agent/skills/community-system/SKILL.md`). The skill documents the procedure for working with Community. Apply:

- Update the "Public API" / "Key methods" table:
  - Remove `SetLeader(Character)`.
  - Add `leaders : List<Character>` (primary at index 0), `PrimaryLeader`, `SecondaryLeaders`, `IsLeader(Character)`, `PromoteToSecondaryLeader(Character)`, `DemoteFromLeadership(Character)`, `TransferPrimaryLeadership(Character)`.
- Add a "Citizenship" section explaining the new field + the writers shipping in Plan 4.
- Add a "Founding a community" section that documents the new gating policy (sole guard: not already a primary leader; no trait, no 4-friends requirement).
- Add to the "Common pitfalls" / "Gotchas" section a one-line link to `wiki/gotchas/singular-leader-vs-multi-leader-isleader.md`.
- If the skill has a version / change-log footer, append `2026-05-17 — multi-leader migration + citizenship`.

- [ ] **Step 7: Sanity check via `LINT`-style sweep (manual)**

Manually run:
```
grep -rn "Community.leader\b" wiki/ .agent/
grep -rn "CanCreateCommunity" wiki/ .agent/
```
Expected: zero matches (any remaining references mean a stale doc snippet — fix in place).

- [ ] **Step 8: Commit**

```bash
git add wiki/ .agent/skills/community-system/
git commit -m "$(cat <<'EOF'
docs(community): wiki + skill updates for multi-leader migration + citizenship

- wiki/gotchas/singular-leader-vs-multi-leader-isleader.md (NEW) — mirrors the
  singular-owner-vs-multi-owner-isowner gotcha for the leader/Community domain
- wiki/concepts/citizenship.md (NEW) — concept page; lifecycle + save semantics +
  membership-vs-citizenship distinction
- wiki/systems/world-community.md — Public API refresh, gotcha link, change log
- wiki/systems/character-community.md — Citizenship section, Public API refresh,
  change log
- .agent/skills/community-system/SKILL.md — multi-leader + citizenship procedural updates

Per rules #28, #29b: every system touched in Plan 1 has SKILL.md + wiki page updated.

Plan 1 of 5 for the City Founding spec.
EOF
)"
```

---

## Task 9: Final verification + zero-`.leader` confirmation + late-joiner audit note

**Files:** none (verification only).

- [ ] **Step 1: Final `\.leader\b` grep against the whole repo**

```
Grep: \.leader\b
Paths: Assets/Scripts (excluding Editor/Tests for the test-helper inline references)
```

Expected: zero matches. Any match → block the merge; fix the missed callsite.

(If matches appear inside `Assets/Editor/Tests/Community/`, ignore — the test file uses `c.leaders` which contains the substring `.leader` only as a list-index prefix. Skim to confirm; the literal `.leader` standalone token should be absent.)

- [ ] **Step 2: Final `CanCreateCommunity` / `canCreateCommunity` grep**

```
Grep: CanCreateCommunity|canCreateCommunity
Paths: Assets/Scripts wiki .agent docs
```

Expected: zero matches in `Assets/Scripts/**/*.cs`. Matches in `Assets/Resources/Data/Behavioural Traits/*.asset` are tolerated (Task 4 Step 1 covered this). Matches in `docs/` referring to the *historical* gate (this plan or the spec) are tolerated as historical record.

- [ ] **Step 3: Compile-check + full Community-test sweep**

Use `assets-refresh` then `console-get-logs` filtering on `Compile`.
Expected: no errors.

Use `tests-run` with `testMode: EditMode`, filter `MWI.Tests.Community.*`.
Expected: 12 tests pass (5 from Task 1 + 3 from Task 6 + 4 from Task 7).

- [ ] **Step 4: Re-run existing Buildings tests as a regression guard**

Use `tests-run` with `testMode: EditMode`, filter `MWI.Tests.Buildings.*`.
Expected: all existing tests pass (the `PrimaryLeaderId` getter addition is additive — no Buildings test should break).

- [ ] **Step 5: Manual late-joiner thought-experiment (rule #19b)**

This is a documentation-only step — no code runs. Write the following observation as the final commit message (Step 6) so the network-safety claim is captured in git history:

> Plan 1 adds NO new client-visible state. All changes are server-side data
> shape (Community.leaders list) or character-profile save-data
> (CommunitySaveData.citizenshipMapId). No `NetworkBehaviour` was modified.
> A late-joining client sees the same world as today; the multi-leader and
> citizenship gates only become visible to clients when the admin-console UI
> ships in Plan 5.
>
> The mandatory late-joiner repro (rule #19b) will be performed in Plan 4
> when the first client-visible writer (`AdministrativeBuilding.OnFinalize`
> calling `SetCitizenship` + the admin console's "Promote to Secondary"
> button) lands.

- [ ] **Step 6: Final summary commit (or amend Task 8 — pick the cleaner option)**

If Tasks 1-8 are all atomic clean commits, this task creates a single
"meta" commit with no code changes that records the Plan-1 completion summary
and the late-joiner deferral note. If a Task 8 amend feels cleaner, fold the
note into Task 8's message instead — no separate commit.

```bash
git commit --allow-empty -m "$(cat <<'EOF'
chore(community): Plan 1 of 5 complete — community multi-leader foundation

Plan 1 of 5 for the City Founding spec
(docs/superpowers/specs/2026-05-18-city-founding-and-administrative-building-design.md).

Network safety (rule #19b):
Plan 1 adds NO new client-visible state. All changes are server-side data
shape (Community.leaders list) or character-profile save-data
(CommunitySaveData.citizenshipMapId). No NetworkBehaviour was modified.

A late-joining client sees the same world as today; the multi-leader and
citizenship gates only become visible to clients when the admin-console UI
ships in Plan 5.

The mandatory late-joiner repro (rule #19b) will be performed in Plan 4 when
the first client-visible writer (AdministrativeBuilding.OnFinalize calling
SetCitizenship + the admin console "Promote to Secondary" button) lands.

Tests: 12 EditMode tests under MWI.Tests.Community.* — all green.
Buildings regression suite: green.

Files changed (Plan 1):
- Assets/Scripts/World/Community/Community.cs (rewrite — multi-leader API)
- Assets/Scripts/World/MapSystem/MapRegistry.cs (CommunityData.PrimaryLeaderId)
- Assets/Scripts/Character/CharacterCommunity/CharacterCommunity.cs
  (.leader migration, gates stripped, Citizenship field + round-trip)
- Assets/Scripts/Character/CharacterInteraction/InteractionInviteCommunity.cs
- Assets/Scripts/Character/CharacterTraits/CharacterTraits.cs (delete CanCreateCommunity)
- Assets/Scripts/Character/CharacterTraits/CharacterBehavioralTraitsSO.cs
- Assets/Scripts/Debug/DevMode/Inspect/SubTabs/SkillsTraitsSubTab.cs
- Assets/Scripts/Character/CharacterBlueprints/CharacterBlueprints.cs (GrantBlueprint SO overload)
- Assets/Scripts/Character/SaveLoad/ProfileSaveData/CommunitySaveData.cs
- Assets/Editor/Tests/Community/*.cs (NEW)
- wiki/gotchas/singular-leader-vs-multi-leader-isleader.md (NEW)
- wiki/concepts/citizenship.md (NEW)
- wiki/systems/world-community.md (update)
- wiki/systems/character-community.md (update)
- .agent/skills/community-system/SKILL.md (update)

Ready for Plan 2 (BuildingGrid) and Plans 3-5 to consume the new API surface.
EOF
)"
```

---

## Self-Review Notes (post-write)

Re-checked against the user's Plan-1 scope statement:

- ✅ **Strip community-creation gates (trait + 4-friends)** — Task 3 + Task 4
- ✅ **Migrate `Community.leader` (singular) → `leaders` (List)** — Task 1
- ✅ **Migrate every callsite** — Task 2 (covers the 6 production-code matches the discovery grep found: 5 in CharacterCommunity, 1 in InteractionInviteCommunity)
- ✅ **Add Citizenship as first-class field** — Task 5 + Task 6
- ✅ **Add `CharacterBlueprints.GrantBlueprint`** — Task 7
- ✅ **Wiki gotcha mirror** — Task 8 (creates `singular-leader-vs-multi-leader-isleader.md`)
- ✅ **Per Rule #19b: explicit network-safety audit** — recorded in the plan header AND in the Task 9 final commit message

Out-of-scope items the spec mentioned (`AdministrativeBuilding` ref on `Community`, `IsChartered`, `TryPromoteLevel` / `TierPromotionResult`) are explicitly listed in "Out of scope" under File Structure with the deferral target plan.

Type-consistency check:
- `Community.leaders` used everywhere (Task 1, Task 2).
- `Community.IsLeader(Character)` used everywhere (Task 2, Task 8 gotcha doc).
- `CommunityData.PrimaryLeaderId` used in CharacterCommunity migration (Task 6).
- `CharacterCommunity._citizenship` + `Citizenship` getter (Task 5) + serialization (Task 6) — names match.
- `CharacterBlueprints.GrantBlueprint(BuildingSO)` + `HasBlueprint(BuildingSO)` (Task 7) — match the spec lines 1249-1250.
- `CommunitySaveData.citizenshipMapId` (Task 6) — matches spec line 680.

Placeholder scan: no "TODO", "TBD", "implement later", or unspecified-code blocks remain.

Plan length: 9 tasks, each end-to-end TDD (where tests apply) + commit. Estimated 2-3 hours total wall-clock for an engineer with this plan in hand.
