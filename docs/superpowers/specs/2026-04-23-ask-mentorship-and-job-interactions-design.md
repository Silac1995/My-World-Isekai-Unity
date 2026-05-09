# Ask-Mentorship & Ask-for-Job Hold-E Interactions Design

**Date:** 2026-04-23
**Branch:** `multiplayyer`
**Status:** Approved Design

## Problem Statement

Holding **E** on a character opens a radial hold-interaction menu listing things the interactor can do with that character ("Follow Me", "Greet", "Invite to Party"). The menu collects entries from all `IInteractionProvider` components on the target via [CharacterInteractable.cs:100-110](../../../Assets/Scripts/Interactable/CharacterInteractable.cs).

Two gameplay pathways that already exist in code are not surfaced in this menu:

1. **`InteractionMentorship`** — a student can request mentorship from any character whose `CharacterMentorship.GetTeachableSubjects()` is non-empty (skills/styles/abilities at Advanced tier ≥ 35). Today this is only triggerable via dialogue or GOAP; there is no direct hold-E entry.
2. **`InteractionAskForJob`** — an unemployed character can apply for a specific vacant job at a `CommercialBuilding` owned by the target. Today only NPCs request jobs via their own GOAP path; a player cannot do so from a menu.

Both interaction classes already inherit `InteractionInvitation`, so the accept/refuse flow (including the player-vs-NPC UI split inside [`CharacterInvitation.ReceiveInvitation`](../../../Assets/Scripts/Character/CharacterInvitation/CharacterInvitation.cs)) is fully implemented. This spec adds the menu entries and the thin networked routing that wires a menu click to the existing invitation pipeline.

### Requirements

1. **Per-option entries (flat).** When the target can teach N subjects, emit N entries. When the target owns a building with M vacant jobs, emit M entries. No submenu, no auto-pick.
2. **Disabled-with-reason when source is ineligible.** When the interactor already has a mentor or a job, entries stay visible but are grayed out via `InteractionOption.IsDisabled` with a suffix like "(you already have a mentor)". Hidden entirely only when the component is missing or the target structurally cannot advertise (0 teachable, not a boss, 0 vacant jobs).
3. **SOLID-clean extension (rules #9, #10, #12).** Each subsystem advertises its own entries by implementing `IInteractionProvider` directly. `CharacterInteractable` is not modified.
4. **Player-to-player uses accept/refuse UI (rule #19).** When the target is a remote player, the invitation must route through the existing `UI_InvitationPrompt` flow with accept/refuse buttons. When the target is an NPC, the existing `EvaluateCustomInvitation` path runs on the server.
5. **Server-authoritative (rule #18).** A client cannot request a subject the mentor can't teach or a job that's already taken. The server re-validates everything the client claimed and silently rejects invalid requests.
6. **Host↔Client, Client↔Client, Host/Client↔NPC parity (rule #19).** All four target×initiator combinations produce identical visible behaviour on every machine.
7. **No new UI prefab, no new menu code, no new icons.** The existing `InteractionOption` shape (`Name`, `Action`, `IsDisabled`, `ToggleName`) is sufficient.

### Non-Goals

- **Submenu grouping** ("Ask to teach ▶ Tailoring / Leadership"). Option C (flat entries) was chosen deliberately. If menu bloat becomes a real problem in playtesting, this can be revisited.
- **Icons on menu entries.** The current `InteractionOption` struct does not carry icons; adding visual affordance is outside the scope of this feature.
- **Toast feedback when a click silently fails** (e.g. another NPC took the job between menu-build and click). Server-side refusal is acceptable for now.
- **Cancel-outbound-invitation UI** for the source player after clicking. The invitation has a 10 s response timeout; player just waits.
- **NPC-initiated versions.** NPCs already request mentorship and jobs via GOAP. This feature only adds the player-initiated menu entries.
- **Relocating the existing "Invite to Party" block from `CharacterInteractable` onto `CharacterParty`.** That cleanup is arguably overdue but is out of scope — it can be done later without breaking this work.

## Architecture

### Shape

Two existing `CharacterSystem` classes gain a single interface:

- `CharacterMentorship : CharacterSystem, IInteractionProvider`
- `CharacterJob : CharacterSystem, ICharacterSaveData<JobSaveData>, IInteractionProvider`

Each implements `List<InteractionOption> GetInteractionOptions(Character interactor)` returning per-subject / per-job entries.

The menu pickup is already in place: [CharacterInteractable.cs:103](../../../Assets/Scripts/Interactable/CharacterInteractable.cs#L103) calls `_character.GetAll<IInteractionProvider>()` and concatenates results into the hold-E menu. No changes there.

### Networking primitives (new)

Two new `ServerRpc`s live on the source's subsystem — mirroring the `PartyInvitation` pattern where `CharacterParty.RequestInviteToPartyServerRpc` handles the client→server hop:

- `CharacterMentorship.RequestMentorshipServerRpc(ulong mentorNetId, string subjectAssetKey)`
- `CharacterJob.RequestJobApplicationServerRpc(ulong ownerNetId, int jobStableIndex)`

The menu Action closure checks `IsServer` and either calls `invitation.Execute(source, target)` directly (host path) or fires the ServerRpc (client path).

### One small refactor

`InteractionMentorship.CanStudentStillLearn(student, mentor, subject)` is a private static-logic method that needs to be called from both the interaction class (existing use) and the new provider (to decide `IsDisabled`). It moves to `CharacterMentorship` as:

```csharp
public bool CanTeachStudent(Character student, ScriptableObject subject)
```

…where `this` is the mentor. `InteractionMentorship.CanStudentStillLearn` is either deleted (interaction calls `target.CharacterMentorship.CanTeachStudent(source, subject)` directly) or becomes a one-line passthrough. This is necessary de-duplication, not gratuitous refactoring — both callers need the same rule.

### What is NOT touched

- `CharacterInteractable.cs` — zero changes
- `InteractionOption` struct — zero changes
- `IInteractionProvider` interface — zero changes
- `InteractionInvitation` base class — zero changes
- `CharacterInvitation` (the MonoBehaviour handling delay, accept/refuse, RPC routing) — zero changes
- `UI_InvitationPrompt` — zero changes
- `CommercialBuilding` — zero changes
- Acceptance-chance math (`CharacterMentorship.CalculateAcceptanceChance`, `CommercialBuilding.AskForJob`) — zero changes
- Save/load (`JobSaveData`, mentorship state) — unaffected

## Components

### `CharacterMentorship.GetInteractionOptions`

```
if (interactor.CharacterMentorship == null) return null;   // source can't learn
teachable = this.GetTeachableSubjects();
if (teachable.Count == 0) return null;

options = new List<InteractionOption>(teachable.Count);
foreach (subject in teachable):
    if (interactor.CharacterMentorship.CurrentMentor == this.Character) continue;  // already with THIS mentor
    
    disabled = false; reason = null
    if (interactor.CharacterMentorship.CurrentMentor != null):
        disabled = true; reason = "you already have a mentor"
    else if (!this.CanTeachStudent(interactor, subject)):
        disabled = true; reason = "you're already skilled enough"
    
    name = disabled ? $"Ask to teach {SubjectName(subject)} ({reason})"
                    : $"Ask to teach {SubjectName(subject)}"
    
    options.Add(new InteractionOption {
        Name = name,
        IsDisabled = disabled,
        Action = BuildMentorshipClickHandler(interactor, subject)
    });

return options;
```

`SubjectName` resolves `SkillSO → SkillName`, `CombatStyleSO → StyleName`, `AbilitySO → <name-property>` (exact property verified during implementation).

### `CharacterJob.GetInteractionOptions`

```
if (!this.IsOwner) return null;
workplace = _ownedBuilding;
if (workplace == null) return null;
vacant = workplace.GetAvailableJobs().ToList();
if (vacant.Count == 0) return null;
if (interactor.CharacterJob == null) return null;   // source can't hold jobs

options = new List<InteractionOption>(vacant.Count);
foreach (job in vacant):
    disabled = false; reason = null
    if (interactor.CharacterJob.HasJob):
        disabled = true; reason = "you already have a job"
    
    name = disabled ? $"Apply for {job.JobTitle} ({reason})"
                    : $"Apply for {job.JobTitle}"
    
    options.Add(new InteractionOption {
        Name = name,
        IsDisabled = disabled,
        Action = BuildJobClickHandler(interactor, workplace, job)
    });

return options;
```

### Click handlers (inside each provider)

```csharp
// CharacterMentorship click handler
Action = () =>
{
    if (disabled) return;  // defensive; menu should prevent but closures fire either way
    if (IsServer)
    {
        var invitation = new InteractionMentorship(subject);
        if (invitation.CanExecute(interactor, Character))
            invitation.Execute(interactor, Character);
    }
    else
    {
        string key = ResolveSubjectKey(subject);  // e.g. "Skills/Tailoring"
        interactor.CharacterMentorship.RequestMentorshipServerRpc(
            Character.NetworkObject.NetworkObjectId, key);
    }
}

// CharacterJob click handler — identical structure
Action = () =>
{
    if (disabled) return;
    int stableIdx = workplace.Jobs.IndexOf(job);  // stable within the building's full list
    if (IsServer)
    {
        var invitation = new InteractionAskForJob(workplace, job);
        if (invitation.CanExecute(interactor, Character))
            invitation.Execute(interactor, Character);
    }
    else
    {
        interactor.CharacterJob.RequestJobApplicationServerRpc(
            Character.NetworkObject.NetworkObjectId, stableIdx);
    }
}
```

### Server RPCs (both on the source's subsystem)

```csharp
[Rpc(SendTo.Server)]
public void RequestMentorshipServerRpc(ulong mentorNetId, string subjectAssetKey)
{
    if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(mentorNetId, out var mentorObj))
        return;
    var mentor = mentorObj.GetComponent<Character>();
    if (mentor == null || mentor.CharacterMentorship == null) return;
    
    // Resolves the subject from within the mentor's teachable list — this is
    // both the lookup AND the security check in one step (see Subject-key resolver).
    var subject = ResolveSubject(mentor, subjectAssetKey);
    if (subject == null) return;
    
    var invitation = new InteractionMentorship(subject);
    if (!invitation.CanExecute(Character, mentor)) return;
    invitation.Execute(Character, mentor);
}

[Rpc(SendTo.Server)]
public void RequestJobApplicationServerRpc(ulong ownerNetId, int jobStableIndex)
{
    if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(ownerNetId, out var ownerObj))
        return;
    var owner = ownerObj.GetComponent<Character>();
    if (owner?.CharacterJob == null || !owner.CharacterJob.IsOwner) return;
    
    var building = owner.CharacterJob.Workplace;
    if (building == null) return;
    if (jobStableIndex < 0 || jobStableIndex >= building.Jobs.Count) return;
    var job = building.Jobs[jobStableIndex];
    if (job.IsAssigned) return;  // race: filled since client built menu
    
    var invitation = new InteractionAskForJob(building, job);
    if (!invitation.CanExecute(Character, owner)) return;
    invitation.Execute(Character, owner);
}
```

### Subject-key resolver

The network transport is a single string: `"{TypeName}:{AssetName}"`, e.g. `"SkillSO:Tailoring"`, `"CombatStyleSO:SwordStyle_Nameless"`, `"AbilitySO:Fireball"`.

- **Client-side construction:** `$"{subject.GetType().Name}:{subject.name}"`.
- **Server-side resolution:** look it up **inside the mentor's teachable list**, not globally:
  ```csharp
  ScriptableObject ResolveSubject(Character mentor, string key)
  {
      var parts = key.Split(':');
      if (parts.Length != 2) return null;
      return mentor.CharacterMentorship.GetTeachableSubjects()
          .FirstOrDefault(s => s != null && s.GetType().Name == parts[0] && s.name == parts[1]);
  }
  ```
  Resolving scoped to `GetTeachableSubjects()` is both the security check (a client can't request a subject the mentor doesn't actually offer) and the lookup — no `Resources.Load` call, no cached registry, no reliance on folder conventions. Works for any future ScriptableObject type added to teachable subjects without resolver changes.

## Data Flow

### Solo (host-only) — NPC target

1. Player holds E on NPC mentor → `CharacterInteractable.GetHoldInteractionOptions` runs.
2. Provider walk finds `CharacterMentorship`, calls `GetInteractionOptions(player)` → N entries.
3. Player clicks "Ask to teach Tailoring" → closure fires, `IsServer == true` → direct `invitation.Execute(player, mentor)`.
4. `Execute` speaks the invitation line, calls `mentor.CharacterInvitation.ReceiveInvitation(invitation, player)`.
5. Mentor is NPC → `ProcessInvitation` coroutine waits `_responseDelay` (~1 s) → `EvaluateCustomInvitation` runs the acceptance-chance formula.
6. On accept: speech bubble, `OnAccepted` runs `SetMentor` / `EnrollStudentToClass`, mentor class spawns.
7. On refuse: speech bubble, `OnRefused` applies -1 relation penalty.

### Multiplayer — player-to-player

1. Client A holds E on Client B's player character. Menu is built on A's client using B's replicated state.
2. A clicks "Ask to teach Tailoring" → closure fires, `IsServer == false` → `RequestMentorshipServerRpc` fires to server.
3. **Server** runs the RPC: validates ownership (the RPC is invoked on A's own `CharacterMentorship` NetworkBehaviour — NGO 2.x enforces ownership by default for `[Rpc(SendTo.Server)]`, matching the existing `CharacterParty.RequestInviteToPartyServerRpc` pattern), re-looks-up B's character, re-validates subject is in `GetTeachableSubjects()`, re-validates `CanExecute`, then calls `invitation.Execute(A, B)`.
4. Server-side `Execute` → B's speech played via `CharacterSpeech.Say` (network-replicated), then `B.CharacterInvitation.ReceiveInvitation(invitation, A)` on server.
5. Server detects B is a player owned by another client → sends `ReceiveInvitationClientRpc` only to B's owner ([CharacterInvitation.cs:64-69](../../../Assets/Scripts/Character/CharacterInvitation/CharacterInvitation.cs#L64-L69)).
6. B's client shows `UI_InvitationPrompt` with the invitation message, 10 s countdown.
7. B clicks Accept → `ResolvePlayerInvitation(true)` → `ResolvePlayerInvitationServerRpc(true)` → server's coroutine resumes.
8. Server runs `OnAccepted(A, B)` → state mutations propagate via the mentorship subsystem's existing network sync (NetworkVariables / ClientRpcs on `CharacterMentorship`).

### Job flow

Identical shape, substituting `RequestJobApplicationServerRpc` + `InteractionAskForJob`. On accept, server runs `OnAccepted` which calls `A.CharacterJob.TakeJob(job, building)` — state visible to host and all clients via `CharacterJob`'s existing save/network sync.

## Error Handling & Race Conditions

- **Target component missing** (mentor has no `CharacterMentorship`, target has no `CharacterJob`) → `GetAll<IInteractionProvider>` skips, zero entries — no crash.
- **Subject becomes unteachable between menu-build and click** (e.g. mentor hibernates, dies, or student gained a mentor via a parallel interaction) → `InteractionMentorship.CanExecute` returns false on the server side → `Execute` never runs → silent failure. Acceptable.
- **Job fills between menu-build and click** → `RequestJobApplicationServerRpc` checks `job.IsAssigned` before constructing the invitation → silent failure. Acceptable.
- **Client forges a ServerRpc** (asks for a subject that the mentor can't teach, or a job index out of range) → all three RPC validations catch it: null check, teachable-list membership check, range check, `CanExecute` check.
- **Client holds E on a character being destroyed / hibernated mid-menu** → `NetworkObject` lookup in the ServerRpc fails → early return. Menu already shows stale data this one frame; next frame the menu closes (target no longer `InteractableObject`).
- **Source player already issued an invitation to someone else and it's still pending** → [CharacterInvitation.cs:51-56](../../../Assets/Scripts/Character/CharacterInvitation/CharacterInvitation.cs#L51-L56) auto-refuses the second one. Acceptable.

No `try/catch` blocks are needed in the Action closures or RPC bodies — these paths are tight (`GetComponent`, dictionary lookup, list indexing, in-memory list scan), and failures are always early-return–able. Rule #31 applies to I/O, deserialization, and external data parsing; this path has none.

## Testing

Unity Test Framework is not set up for integration tests involving networking + interactions. Testing is manual, using the existing `UI_CharacterDebug` + `UI_CommercialBuildingDebug` panels and the Dev-Mode god tool.

**Solo smoke (host only, 1 player):**
1. NPC mentor with 3 teachable skills (forced via Dev-Mode) → 3 "Ask to teach X" entries with correct names.
2. NPC boss with 2 vacant jobs → 2 "Apply for X" entries.
3. Player already has mentor → mentorship entries grayed, "(you already have a mentor)" suffix.
4. Player already has job → job entries grayed, "(you already have a job)" suffix.
5. Non-owner NPC employee → zero job entries.
6. Mentor with all skills < 35 → zero mentorship entries.
7. NPC with no `CharacterJob` component → zero job entries (no null-ref).
8. Click "Ask to teach X" → speech bubble, ~1 s delay, accept/refuse, state visible in debug panel.
9. Click "Apply for X" → same flow; on accept `CharacterJob.HasJob == true` in debug.

**Multiplayer (host + 1 remote client):**
10. Client holds E on host's NPC mentor → entries appear with correct gating → click → ServerRpc fires → NPC evaluates → result syncs.
11. Client holds E on host's NPC boss → same flow, `CharacterJob` NetworkVariable updates on client.
12. Host holds E on client's player character who is a boss → host clicks → client sees `UI_InvitationPrompt` → client accepts → host is hired.
13. Client A holds E on Client B who is a mentor → A clicks → B sees prompt → B refuses → A gets -1 relation with B, visible in A's debug panel.
14. **Race:** two clients simultaneously click "Apply for Clerk" on the same single-vacancy building → first processes, second rejected at `RequestJobApplicationServerRpc` `CanExecute` check → only first client hired.

**Parity check (rule #19):**
15. Same NPC boss visible from both host and client perspectives → both see identical menu entries (no server-only state leak).

## File Touch List

**Modified:**
- `Assets/Scripts/Character/CharacterSkills/CharacterMentorship.cs` — add `IInteractionProvider`, `GetInteractionOptions`, `RequestMentorshipServerRpc`, `CanTeachStudent` (moved in from `InteractionMentorship`).
- `Assets/Scripts/Character/CharacterJob/CharacterJob.cs` — add `IInteractionProvider`, `GetInteractionOptions`, `RequestJobApplicationServerRpc`.
- `Assets/Scripts/Character/CharacterInteraction/InteractionMentorship.cs` — delete or thin out `CanStudentStillLearn`, route to `CharacterMentorship.CanTeachStudent`.

**New:** none.

**Documentation (rules #28, #29, #29b):**
- Update `.agent/skills/character-social-architect/SKILL.md` (mentorship section) to mention the new `CanTeachStudent` public API and the hold-E entry wiring.
- Update `.agent/skills/npc-ai-specialist/SKILL.md` (job section) to note the new `RequestJobApplicationServerRpc` entry point for player-initiated applications.
- Update `wiki/systems/character-mentorship.md` (or create if missing) with a one-liner in **Public API** + a **Change log** entry.
- Update `wiki/systems/jobs-and-logistics.md` similarly.
- No new agent file needed — this fits within `character-social-architect` and `npc-ai-specialist`.

## Change Log

- 2026-04-23 — initial design approved by user — claude
