# Management Furniture + Application Flow Refinement Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the three v1 trade-offs from Plan 2:
1. Replace the "Manage Hiring..." menu pollution with a physical `ManagementFurniture` placed inside each building. The owner walks up, presses E, the hiring panel opens. The menu entry stays as a fallback when no furniture is wired (Option B from the 2026-04-30 design conversation).
2. Remove the Apply-for-Job button from the Help Wanted sign. The sign is **informative-only** — both player and NPC applicants must physically walk to the boss to apply (existing `InteractionAskForJob` path).
3. Throttle the NPC `NeedJob` per-tick check to event-driven once-per-new-day via `TimeManager.OnNewDay`. Cache the discovered `(building, job)` candidate for the day. Substantial perf win at scale (100 NPCs × 1 query/day vs. 100 × N-ticks/day).

**Architecture:** `ManagementFurniture : Furniture` (MonoBehaviour, no NetworkBehaviour sibling — owns no replicated state). `Use(Character)` overrides the canonical Furniture interaction entry; if the actor is the parent `CommercialBuilding.Owner`, opens existing `UI_OwnerHiringPanel`; otherwise toast-feedback "Only the owner can use this management desk." `CommercialBuilding._managementFurniture` is a designer reference (may be null). `CharacterJob.GetInteractionOptions` Section B is gated on `_managementFurniture == null` so the menu entry only appears as a fallback. `UI_DisplayTextReader` Apply button is removed; its `OnApplyClicked` handler + `ResolveLocalPlayerCharacter` helper get deleted (the reader becomes a pure read-only viewer). `NeedJob` subscribes to `TimeManager.OnNewDay` server-side, caches `(building, job)` until the next event; `GetUrgency` returns 0 while cache is empty.

The `ManagementFurniture` primitive is also a **deliberate precursor** to the parallel-session "driveable entities" system. v1 opens UI on E-press; the future migration replaces `Use` with an "occupy this driveable entity" call, with the UI opening as a side-effect of being seated. Public API stays stable; migration is internal.

**Tech Stack:** Unity 6, NGO, C#. No new EditMode tests (consistent with Plan 1's strategy — smoke tests cover end-to-end).

**Source spec:** [docs/superpowers/specs/2026-04-29-farmer-job-and-tool-storage-design.md §15](../specs/2026-04-29-farmer-job-and-tool-storage-design.md). Refinements per 2026-04-30 conversation (sign informative-only, NPC OnNewDay throttle, management furniture).

**Phase scope:** Plan 2.5 of a 4-plan rollout (Plan 1 = Tool Storage; Plan 2 = Help Wanted base; **Plan 2.5 = this**; Plan 3 = Farmer integration). After this plan ships, Plan 2's smoketest scenarios needing the Apply button get rewritten in the new smoketest doc; Plan 3's FarmingBuilding will inherit the management furniture pattern automatically.

---

## Files affected

**Created:**
- `Assets/Scripts/World/Furniture/ManagementFurniture.cs`
- `docs/superpowers/smoketests/2026-04-30-management-furniture-and-application-flow-smoketest.md`

**Modified:**
- `Assets/Scripts/World/Buildings/CommercialBuilding.cs` — add `_managementFurniture` field + `ManagementFurniture` accessor.
- `Assets/Scripts/Character/CharacterJob/CharacterJob.cs` — gate Section B "Manage Hiring..." menu entry on `_managementFurniture == null`.
- `Assets/Scripts/UI/PlayerHUD/UI_DisplayTextReader.cs` — remove Apply button + handler + `ResolveLocalPlayerCharacter` helper. Hide the `_applyButton` GameObject permanently (or remove from prefab).
- `Assets/Resources/UI/UI_DisplayTextReader.prefab` — remove the Apply button from the prefab hierarchy (or set inactive permanently).
- `Assets/Scripts/World/Buildings/CommercialBuilding.cs` — tweak `GetHelpWantedDisplayText` last-line wording.
- `Assets/Scripts/Character/CharacterNeeds/NeedJob.cs` — throttle to `TimeManager.OnNewDay` event-driven cache.
- `wiki/systems/help-wanted-and-hiring.md` — change-log + updated UI section + remove Apply button mentions.
- `wiki/systems/character-job.md` — change-log entry for the management furniture fallback.
- `.agent/skills/help-wanted-and-hiring/SKILL.md` — update API surface to reflect informative-only sign.

---

## Task 1: ManagementFurniture base class + Use override

**Files:**
- Create: `Assets/Scripts/World/Furniture/ManagementFurniture.cs`

The new furniture is a thin Furniture subclass — owner-only Use opens `UI_OwnerHiringPanel`. No replicated state of its own (the panel reads `CommercialBuilding`'s replicated state).

- [ ] **Step 1: Read existing patterns**

Reference files:
- `Assets/Scripts/World/Furniture/DisplayTextFurniture.cs` (Plan 2 Task 1) — same shape MINUS the NetSync sibling (since ManagementFurniture has no replicated state).
- `Assets/Scripts/World/Furniture/Furniture.cs` — base class. Note the `Use(Character)` virtual signature returning `bool`.
- `Assets/Scripts/UI/PlayerHUD/UI_OwnerHiringPanel.cs` — entry point this furniture opens (`UI_OwnerHiringPanel.Show(building)`).
- `Assets/Scripts/UI/Notifications/UI_Toast.cs` — used for the non-owner feedback toast.

- [ ] **Step 2: Create the file**

Path: `Assets/Scripts/World/Furniture/ManagementFurniture.cs`

```csharp
using UnityEngine;
using MWI.UI.Notifications;

/// <summary>
/// Owner's management desk for a <see cref="CommercialBuilding"/>. Owner walks up, presses E,
/// <see cref="UI_OwnerHiringPanel"/> opens for the parent building. Non-owners get a toast
/// "Only the owner can use this management desk."
///
/// Replaces the v1 "Manage Hiring..." menu entry on every NPC interaction (Plan 2 Task 8) —
/// the menu entry stays as a fallback only when <c>CommercialBuilding._managementFurniture</c>
/// is null.
///
/// **Future driveable-entity migration:** this furniture is a deliberate precursor to the
/// parallel-session "driveable entities" system. v1 opens UI on E-press immediately; the
/// future migration replaces <see cref="Use"/> with an "occupy this driveable entity" call
/// (the player gets seated at the desk; the UI opens as a side-effect of being seated; exiting
/// the desk closes the UI). Public API stays stable across the migration — only the internals
/// of <see cref="Use"/> change.
///
/// No NetworkBehaviour sibling needed — this furniture owns no replicated state. The panel
/// it opens reads <see cref="CommercialBuilding"/>'s already-replicated <c>_isHiring</c> +
/// <c>_helpWantedFurniture</c> state.
/// </summary>
public class ManagementFurniture : Furniture
{
    /// <summary>
    /// Owner-only Use. Resolves parent CommercialBuilding via GetComponentInParent, validates
    /// owner identity, opens the hiring panel.
    ///
    /// - Player owner: opens UI_OwnerHiringPanel.
    /// - Player non-owner: toast "Only the owner can use this management desk."
    /// - NPC: silent no-op (no NPC AI uses management furniture in v1; future Phase 2 can
    ///   add an NPC-owner GOAP path that calls TryOpenHiring directly without going through
    ///   this UI hop).
    /// - Remote-client gate: only the local player's machine opens the UI, even when multiple
    ///   peers see the press-E event (mirrors DisplayTextFurniture.Use's IsOwner gate).
    /// </summary>
    public override bool Use(Character character)
    {
        if (character == null) return false;

        // Remote-client gate: only the owning peer pops UI.
        if (character.IsSpawned && !character.IsOwner) return true;

        // NPCs: silent success (no UI pop).
        if (!character.IsPlayer()) return true;

        var building = GetComponentInParent<CommercialBuilding>();
        if (building == null)
        {
            Debug.LogWarning($"[ManagementFurniture] {name} not parented under a CommercialBuilding.");
            return false;
        }

        if (!building.HasOwner || building.Owner != character)
        {
            UI_Toast.Show("Only the owner can use this management desk.", ToastType.Warning, duration: 3f, title: "Not your desk");
            return true;
        }

        UI_OwnerHiringPanel.Show(building);
        return true;
    }
}
```

- [ ] **Step 3: Compile + commit**

Build via Unity. Verify zero new compile errors.

```bash
git add Assets/Scripts/World/Furniture/ManagementFurniture.cs
git commit -m "feat(furniture): ManagementFurniture — owner's hiring desk

New Furniture subclass. Owner walks up, presses E, UI_OwnerHiringPanel
opens. Non-owners get a toast 'Only the owner can use this management
desk.' NPCs silent-success (no AI uses it in v1). Remote-client IsOwner
gate mirrors DisplayTextFurniture pattern.

Designer-placed in any CommercialBuilding. Reference set on
CommercialBuilding._managementFurniture (Task 2 wires the field).

Future migration path: when the parallel-session driveable-entity system
ships, Use becomes an 'occupy this driveable entity' call — UI opens as
a side-effect of being seated. Public API stays stable.

Part of: management-furniture-and-application-flow plan, Task 1/6.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: CommercialBuilding._managementFurniture reference + accessor

**Files:**
- Modify: `Assets/Scripts/World/Buildings/CommercialBuilding.cs`

Wire the designer reference + accessor. Place in the field section near the existing `_helpWantedFurniture` and `_toolStorageFurniture` references.

- [ ] **Step 1: Add the field**

Find the existing `[Header("Hiring")]` block (Plan 2 Task 2). Below it (or merged into the same Header), add:

```csharp
    [Tooltip("Designer reference to a ManagementFurniture inside this building. When set, the owner walks up + presses E to open UI_OwnerHiringPanel. Null = no in-world management desk; the 'Manage Hiring...' menu entry on CharacterJob stays as the fallback (Plan 2 Task 8).")]
    [SerializeField] private ManagementFurniture _managementFurniture;
```

- [ ] **Step 2: Add the accessor**

Near the existing `HelpWantedSign` accessor:

```csharp
    public ManagementFurniture ManagementFurniture => _managementFurniture;
    public bool HasManagementFurniture => _managementFurniture != null;
```

- [ ] **Step 3: Compile + commit**

```bash
git add Assets/Scripts/World/Buildings/CommercialBuilding.cs
git commit -m "feat(building): _managementFurniture designer reference + accessor

Mirrors _helpWantedFurniture / _toolStorageFurniture pattern. Null =
fall back to existing 'Manage Hiring...' menu entry; non-null = owner
must use the in-world management desk to open the hiring panel.

Part of: management-furniture-and-application-flow plan, Task 2/6.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: Gate "Manage Hiring..." menu entry on _managementFurniture == null

**Files:**
- Modify: `Assets/Scripts/Character/CharacterJob/CharacterJob.cs`

The Plan 2 Task 8 menu entry remains as a fallback. Wrap it in a conditional that hides the entry when the building has a management furniture authored.

- [ ] **Step 1: Locate the existing Section B**

Open `Assets/Scripts/Character/CharacterJob/CharacterJob.cs`. Find `GetInteractionOptions(Character interactor)`. Section B emits the "Manage Hiring..." entry when `interactor.CharacterJob.OwnedBuilding != null`.

- [ ] **Step 2: Add the fallback gate**

Modify the existing block:

```csharp
        // ── B. "Manage Hiring..." entry — interactor IS the boss of some CommercialBuilding ──
        // Fallback path only: shown when the owned building has no ManagementFurniture wired.
        // When _managementFurniture is set, the owner uses the in-world desk instead (Plan 2.5).
        var interactorOwned = interactor.CharacterJob.OwnedBuilding;
        if (interactorOwned != null && !interactorOwned.HasManagementFurniture)
        {
            var capturedOwned = interactorOwned;
            options.Add(new InteractionOption
            {
                Name = "Manage Hiring...",
                IsDisabled = false,
                Action = () => UI_OwnerHiringPanel.Show(capturedOwned)
            });
        }
```

The diff from Plan 2's version is one new condition: `&& !interactorOwned.HasManagementFurniture`. Comment is updated to explain the fallback role.

- [ ] **Step 3: Compile + commit**

```bash
git add Assets/Scripts/Character/CharacterJob/CharacterJob.cs
git commit -m "feat(character-job): gate Manage Hiring menu entry as fallback

Section B 'Manage Hiring...' entry now only appears when the owner's
building has no ManagementFurniture wired. Buildings with the in-world
desk get the cleaner UX (no menu pollution); buildings without it
retain the menu entry so existing scenes don't break.

Part of: management-furniture-and-application-flow plan, Task 3/6.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 4: Remove Apply button from UI_DisplayTextReader + update sign wording

**Files:**
- Modify: `Assets/Scripts/UI/PlayerHUD/UI_DisplayTextReader.cs`
- Modify: `Assets/Resources/UI/UI_DisplayTextReader.prefab`
- Modify: `Assets/Scripts/World/Buildings/CommercialBuilding.cs` — tweak `GetHelpWantedDisplayText` final line wording.

The sign is informative-only. Player must walk to the boss in person — same as NPCs.

- [ ] **Step 1: Strip the Apply logic from UI_DisplayTextReader**

Open `Assets/Scripts/UI/PlayerHUD/UI_DisplayTextReader.cs`. Remove:
- `_applyButton` field and `_applyButtonComponent` field.
- `Awake` listener registration on `_applyButtonComponent.onClick`.
- `OnApplyClicked` method (and the `ApplyForJobAtCurrentBuilding` helper if it was inlined).
- `ResolveLocalPlayerCharacter` static helper (only `OnApplyClicked` used it; if it has no other callers, delete).
- Any references to `isHelpWanted` / `_applyButton.SetActive(...)` in `ShowInternal`.

`ShowInternal` becomes:

```csharp
    private void ShowInternal(DisplayTextFurniture sign)
    {
        _currentSign = sign;
        _currentBuilding = sign.GetComponentInParent<CommercialBuilding>();

        string title = _currentBuilding != null ? _currentBuilding.BuildingName : "Sign";
        if (_titleLabel != null) _titleLabel.text = title;
        if (_bodyLabel != null) _bodyLabel.text = sign.DisplayText;

        gameObject.SetActive(true);
    }
```

The reader becomes a pure read-only viewer. Update the file's class XML doc summary to reflect this — strip any Apply-related sentences.

- [ ] **Step 2: Remove Apply button from the prefab**

Edit `Assets/Resources/UI/UI_DisplayTextReader.prefab` either via Unity Editor (open the prefab, delete the Apply button GameObject + its Button child, save) OR via MCP `script-execute` modifying the prefab YAML. Either approach is fine; the goal is no orphaned Apply button GameObject in the prefab.

If you want to be extra-safe and keep the prefab simple to revert: instead of deleting the GameObject, set it inactive permanently and remove the SerializedField wiring. But cleaner is deletion.

- [ ] **Step 3: Tweak GetHelpWantedDisplayText wording**

Open `Assets/Scripts/World/Buildings/CommercialBuilding.cs`. Find the existing `GetHelpWantedDisplayText` method (Plan 2 Task 3). Replace the last line:

Before:
```csharp
        sb.Append("Approach the owner to apply.");
```

After:
```csharp
        sb.Append("For application, see the owner in person.");
```

Pick whichever wording you prefer; goal is unambiguous "you must physically find the boss" messaging.

- [ ] **Step 4: Compile + commit**

```bash
git add Assets/Scripts/UI/PlayerHUD/UI_DisplayTextReader.cs "Assets/Resources/UI/UI_DisplayTextReader.prefab" Assets/Scripts/World/Buildings/CommercialBuilding.cs
git commit -m "refactor(ui): UI_DisplayTextReader is informative-only — no Apply button

Per 2026-04-30 design refinement: the Help Wanted sign is purely
informative. Both player and NPC applicants must walk to the boss in
person and use the existing InteractionAskForJob path. Removes the
Apply button + its OnApplyClicked / ApplyForJobAtCurrentBuilding /
ResolveLocalPlayerCharacter logic from UI_DisplayTextReader.

Wording on the sign tweaked from 'Approach the owner to apply.' to
'For application, see the owner in person.' for unambiguous
messaging.

Part of: management-furniture-and-application-flow plan, Task 4/6.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 5: Throttle NeedJob to TimeManager.OnNewDay event-driven cache

**Files:**
- Modify: `Assets/Scripts/Character/CharacterNeeds/NeedJob.cs`

Move from per-tick polling to OnNewDay event-driven discovery. Cache the `(building, job)` pair. `GetUrgency` returns 0 when the cache is empty (need is dormant); `GetGoapActions` uses the cached pair. Cleared on day flip.

- [ ] **Step 1: Read the existing NeedJob structure**

Open `Assets/Scripts/Character/CharacterNeeds/NeedJob.cs`. Note:
- Current `GetUrgency()` always returns `BASE_URGENCY` (so the need is always asking for attention).
- Current `GetGoapActions()` calls `BuildingManager.Instance.FindAvailableJob<Job>(true)` every invocation.
- A `_lastJobSearchTime = UnityEngine.Time.time` cooldown is already there but the cooldown isn't enforced inside `GetUrgency` — only `GetGoapActions` uses it (poorly).

- [ ] **Step 2: Add subscription + cache fields**

Add to NeedJob:

```csharp
    // Cached candidate from the most recent OnNewDay scan. Cleared at the start of each new
    // day; refilled if FindAvailableJob returns a hit. While null, the need is dormant
    // (GetUrgency returns 0 — GOAP planner skips it).
    private CommercialBuilding _cachedBuilding;
    private Job _cachedJob;
    private bool _subscribed;
```

- [ ] **Step 3: Subscribe + unsubscribe lifecycle**

Override / hook `OnEnable` (or whichever lifecycle point already exists for the Need), AND `OnDisable`. If the Need pattern uses `Awake` / `OnDestroy`, mirror that. Read other Needs in `Assets/Scripts/Character/CharacterNeeds/` for the canonical pattern (e.g. `NeedHunger`).

```csharp
    protected override void OnEnable()    // adapt to existing lifecycle hook on Need base
    {
        base.OnEnable();
        TrySubscribe();
    }

    protected override void OnDisable()
    {
        if (_subscribed && MWI.Time.TimeManager.Instance != null)
        {
            MWI.Time.TimeManager.Instance.OnNewDay -= HandleNewDay;
            _subscribed = false;
        }
        base.OnDisable();
    }

    private void TrySubscribe()
    {
        if (_subscribed) return;
        if (MWI.Time.TimeManager.Instance == null) return;
        MWI.Time.TimeManager.Instance.OnNewDay += HandleNewDay;
        _subscribed = true;
    }

    private void Update()
    {
        // Idempotent: if TimeManager wasn't ready at OnEnable time, retry on the next frame.
        if (!_subscribed) TrySubscribe();
    }
```

- [ ] **Step 4: Implement HandleNewDay**

```csharp
    /// <summary>
    /// Server-side: refreshes the candidate building/job once per in-game day. Replaces the
    /// previous per-frame BuildingManager.FindAvailableJob call. Cache feeds GetUrgency
    /// (returns 0 when null) and GetGoapActions (uses the cached pair).
    /// </summary>
    private void HandleNewDay()
    {
        // Defensive: only the server runs the discovery. Client-side Need state is replicated
        // via the existing Need pipeline; client cache stays empty (GetUrgency=0 client-side
        // is fine — clients don't drive GOAP planning).
        if (Unity.Netcode.NetworkManager.Singleton != null && !Unity.Netcode.NetworkManager.Singleton.IsServer)
        {
            return;
        }

        // If the character already has a job, skip the scan — Need is satisfied.
        if (_character != null && _character.CharacterJob != null && _character.CharacterJob.HasJob)
        {
            _cachedBuilding = null;
            _cachedJob = null;
            return;
        }

        if (BuildingManager.Instance == null)
        {
            _cachedBuilding = null;
            _cachedJob = null;
            return;
        }

        var (building, job) = BuildingManager.Instance.FindAvailableJob<Job>(requireBoss: true);
        _cachedBuilding = building;
        _cachedJob = job;

        if (NPCDebug.VerboseJobs)   // existing toggle from Plan 1's gating convention
        {
            string label = building != null ? $"{building.BuildingName}/{job.JobTitle}" : "(none)";
            Debug.Log($"<color=yellow>[NeedJob]</color> {_character?.CharacterName} OnNewDay scan → cached {label}.");
        }
    }
```

(Adapt `_character` to the actual `Character` accessor on the Need base class; could be `_owner` / `Character` property / etc. Read the existing Need pattern.)

- [ ] **Step 5: Update GetUrgency + GetGoapActions to use the cache**

```csharp
    public override float GetUrgency()
    {
        // Need is dormant when no cached candidate exists. Server populates the cache once
        // per OnNewDay; if no eligible building exists in the world, the planner skips this
        // need until tomorrow.
        if (_cachedBuilding == null || _cachedJob == null) return 0f;
        return BASE_URGENCY;
    }

    public override GoapGoal GetGoapGoal()
    {
        return new GoapGoal("FindJob", new Dictionary<string, bool> { { "hasJob", true } }, (int)GetUrgency());
    }

    public override List<GoapAction> GetGoapActions()
    {
        var actions = new List<GoapAction>();

        if (_cachedBuilding == null || _cachedJob == null) return actions;

        // Re-validate the cached pair — it may have been claimed by another NPC since the
        // last OnNewDay scan. If invalid, return empty actions; the cache stays — the Need
        // becomes effectively dormant until tomorrow's re-scan picks a different candidate.
        if (_cachedJob.IsAssigned || !_cachedBuilding.IsHiring || !_cachedBuilding.HasOwner)
        {
            if (NPCDebug.VerboseJobs)
                Debug.Log($"<color=orange>[NeedJob]</color> {_character?.CharacterName} cached job stale; idling until next day.");
            return actions;
        }

        if (NPCDebug.VerboseJobs)
            Debug.Log($"<color=yellow>[NeedJob]</color> {_character?.CharacterName} planning Apply at {_cachedBuilding.BuildingName}/{_cachedJob.JobTitle}.");
        actions.Add(new GoapAction_GoToBoss(_cachedBuilding.Owner));
        actions.Add(new GoapAction_AskForJob(_cachedBuilding, _cachedJob));

        return actions;
    }
```

Delete the old `_lastJobSearchTime` cooldown logic — it's no longer needed.

- [ ] **Step 6: Compile + commit**

```bash
git add Assets/Scripts/Character/CharacterNeeds/NeedJob.cs
git commit -m "perf(needs): NeedJob throttle — OnNewDay event-driven cache

Replaces the per-tick BuildingManager.FindAvailableJob call with a
server-side OnNewDay event subscription. Cached (building, job) pair
lives until the next day flip; GetUrgency returns 0 while cache is
empty so the GOAP planner skips this Need cleanly.

Substantial perf win at scale: 100 NPCs × 1 query per day vs. 100 ×
N-ticks-per-day. Mid-day staleness (cached job filled by another NPC)
triggers a one-frame replan that returns no actions; NPC idles until
tomorrow's re-scan. New-buildings-built-mid-day also wait for the next
day to be discovered — feels organic.

Part of: management-furniture-and-application-flow plan, Task 5/6.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 6: Smoketest + documentation

**Files:**
- Create: `docs/superpowers/smoketests/2026-04-30-management-furniture-and-application-flow-smoketest.md`
- Modify: `wiki/systems/help-wanted-and-hiring.md` (change-log + remove Apply-button mentions + add management furniture mention)
- Modify: `wiki/systems/character-job.md` (change-log entry for menu fallback)
- Modify: `.agent/skills/help-wanted-and-hiring/SKILL.md` (update API surface + new ManagementFurniture entry)

- [ ] **Step 1: Write the smoketest**

Path: `docs/superpowers/smoketests/2026-04-30-management-furniture-and-application-flow-smoketest.md`

```markdown
# Management Furniture + Application Flow — Smoketest

**Date:** 2026-04-30
**Plan:** [docs/superpowers/plans/2026-04-30-management-furniture-and-application-flow.md](../plans/2026-04-30-management-furniture-and-application-flow.md)
**Status:** _(replace with Pass / Fail-with-notes after running)_

This smoketest validates the three Plan 2.5 refinements: ManagementFurniture, sign-becomes-informative-only (Apply button removed), and NeedJob OnNewDay throttle. Run on the same `HarvestingBuilding` test scene used for Plan 2.

## Setup
- HarvestingBuilding with Owner set, `_helpWantedFurniture` and `_toolStorageFurniture` already wired (Plan 1+2 setup).
- Drop a `ManagementFurniture` prefab inside the building.
- On the building's CommercialBuilding component, set `_managementFurniture` to reference the new desk.
- Save scene.

## Smoke A — Management furniture replaces menu entry
- [ ] As the Owner-player, walk up to a non-owner NPC. Press hold-E to open the interaction menu.
- [ ] **Assert**: "Manage Hiring..." entry is **NOT** in the menu (gated out because building has _managementFurniture).
- [ ] Walk up to the Management Furniture (the new desk). Press E.
- [ ] **Assert**: UI_OwnerHiringPanel opens for the building.

## Smoke B — Non-owner gets denial toast
- [ ] As a non-owner player, walk up to the Management Furniture. Press E.
- [ ] **Assert**: A toast appears: "Only the owner can use this management desk."
- [ ] **Assert**: UI_OwnerHiringPanel does NOT open.

## Smoke C — Sign is informative-only (no Apply button)
- [ ] Walk a player up to the Help Wanted placard. Press E.
- [ ] **Assert**: Reader UI opens with title + body text.
- [ ] **Assert**: NO "Apply for a job" button anywhere.
- [ ] **Assert**: The body text ends with "For application, see the owner in person." (or equivalent updated wording).

## Smoke D — Player application requires walking to boss
- [ ] As a player without a job, read the Help Wanted sign (it tells you to see the owner).
- [ ] Walk to the Owner NPC. Press hold-E to open the menu.
- [ ] **Assert**: "Apply for {JobTitle}" entry appears (existing 2026-04-24 path).
- [ ] Click it. Verify the InteractionAskForJob path runs (existing flow).
- [ ] **Assert**: Player is hired.

## Smoke E — Fallback menu entry when no management furniture
- [ ] On a different building (or temporarily), set `_managementFurniture = null`.
- [ ] As the Owner-player, walk up to any NPC. Press hold-E.
- [ ] **Assert**: "Manage Hiring..." entry appears (fallback). Click it → panel opens.

## Smoke F — NPC NeedJob OnNewDay throttle
- [ ] Setup: an unemployed NPC with `NeedJob` active. A vacant Harvester position at a building with `IsHiring == true` and an Owner.
- [ ] Pre-day: walk through `NeedJob.GetUrgency()` in debug. **Assert**: returns 0 (cache empty).
- [ ] Trigger a new day via `TimeManager.AdvanceToNextDay()` or equivalent dev tool.
- [ ] **Assert**: NeedJob cache is now populated. `GetUrgency()` returns BASE_URGENCY.
- [ ] **Assert**: Console shows `[NeedJob]` log line: "OnNewDay scan → cached <building>/<jobtitle>".
- [ ] Verify the NPC plans an Apply via `GetGoapActions()` and walks to the boss.
- [ ] **Assert**: NPC successfully applies + is hired.
- [ ] Now: with two unemployed NPCs and only ONE vacant job, on day boundary, both NPCs cache the SAME (building, job). The slower one's `GetGoapActions` re-validation should detect `_cachedJob.IsAssigned` and return empty actions. **Assert**: the slower NPC idles silently until next day.

## Smoke G — Performance regression check (manual)
- [ ] With 10+ unemployed NPCs in a scene with `NeedJob`, profile a 1-minute window in Unity Profiler.
- [ ] **Assert**: `BuildingManager.FindAvailableJob` is called at most 10 times per in-game day, NOT 10 × N-frames-per-day.
- [ ] **Assert**: NeedJob's GetUrgency / GetGoapActions are cheap when cache is empty (<1ms total per NPC per BT tick).

## Result
All 7 smokes pass → mark Status: **Pass** + commit.
```

- [ ] **Step 2: Update help-wanted-and-hiring wiki page**

In `wiki/systems/help-wanted-and-hiring.md`:
- Bump `updated:` to `2026-04-30`.
- Append change-log line: `- 2026-04-30 — Management furniture refinement (Plan 2.5): added ManagementFurniture primitive (owner walks to a desk to manage hiring); removed Apply button from UI_DisplayTextReader (sign is informative-only); throttled NPC NeedJob to OnNewDay event-driven cache. — claude`.
- Update the "UI" section to remove Apply-button mentions on the reader; describe the management furniture as the canonical owner-management surface.
- Add a `[[character-needs]]` link to `related[]` if not already there.
- Document the new ManagementFurniture in the "Key classes / files" table.

- [ ] **Step 3: Update character-job wiki page**

`wiki/systems/character-job.md`:
- Bump `updated:` to `2026-04-30`.
- Append change-log: `- 2026-04-30 — Section B 'Manage Hiring...' menu entry now gated on workplace.HasManagementFurniture; entry only appears as fallback when no in-world management desk is wired (Plan 2.5). — claude`.

- [ ] **Step 4: Update SKILL.md**

`.agent/skills/help-wanted-and-hiring/SKILL.md`:
- Add `ManagementFurniture` to the public API section.
- Add `CommercialBuilding.ManagementFurniture` / `HasManagementFurniture` accessors.
- Strip Apply-button mentions from the `UI_DisplayTextReader` description.
- Add a "NeedJob OnNewDay throttle" gotcha entry.

- [ ] **Step 5: Commit docs**

```bash
git add docs/superpowers/smoketests/2026-04-30-management-furniture-and-application-flow-smoketest.md wiki/systems/help-wanted-and-hiring.md wiki/systems/character-job.md .agent/skills/help-wanted-and-hiring/SKILL.md
git commit -m "docs(plan-2.5): smoketest + wiki + SKILL updates

Captures the three refinements: ManagementFurniture as owner's
management desk, sign-is-informative-only, NeedJob OnNewDay throttle.

Smoketest covers 7 scenarios (A-G): menu replacement, non-owner toast,
sign Apply removal, walk-to-boss flow, fallback menu, NeedJob cache,
performance regression sanity check.

Wiki + SKILL updates reflect the new public API surface and document
the gotchas (NeedJob mid-day staleness behaviour, fallback semantics).

Part of: management-furniture-and-application-flow plan, Task 6/6.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Self-Review Checklist

**1. Spec coverage** — three v1 trade-offs from Plan 2's final review are addressed:
- "Manage Hiring... menu pollution" → Tasks 1+2+3 (ManagementFurniture + fallback gate).
- Apply button bypass → Task 4 (informative-only sign).
- NPC NeedJob per-tick polling → Task 5 (OnNewDay throttle).

**2. Placeholder scan** — no TODOs / "implement appropriate handling" / etc. left in.

**3. Type consistency** — `_managementFurniture` field (Task 2) ↔ `ManagementFurniture` / `HasManagementFurniture` accessors (Task 2) ↔ `HasManagementFurniture` gate (Task 3). All consistent.

**4. Driveable-entity migration path** — explicitly documented in `ManagementFurniture.cs` XML doc + plan goal. Public API stable across the future migration.

---

## Acceptance Criteria

- [ ] All 6 tasks committed.
- [ ] Plan 2.5 smoketest (Task 6 §1) marked Pass on the existing HarvestingBuilding.
- [ ] Plan 2's Apply-button references in `2026-04-30-help-wanted-and-hiring-smoketest.md` rewritten to align with the new informative-only flow.
- [ ] Wiki + SKILL.md updates land.
- [ ] No regressions in existing job/building/character-action tests.

After this plan ships and is verified, **Plan 3 (Farmer integration)** is the next plan to write — `FarmingBuilding` + `JobFarmer` + plant/water tasks + the **multi-zone list** for farm field designation. Plan 3 will consume Plans 1, 2, AND 2.5.
