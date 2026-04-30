---
type: system
title: "Help Wanted + Owner-Controlled Hiring"
tags: [building, character-job, ui, network, hiring, tier-2]
created: 2026-04-30
updated: 2026-04-30
sources: []
related:
  - "[[commercial-building]]"
  - "[[character-job]]"
  - "[[character-needs]]"
  - "[[storage-furniture]]"
  - "[[tool-storage]]"
  - "[[player-ui]]"
  - "[[kevin]]"
status: stable
confidence: high
primary_agent: building-furniture-specialist
secondary_agents:
  - npc-ai-specialist
  - character-system-specialist
owner_code_path: "Assets/Scripts/World/Furniture/"
depends_on:
  - "[[commercial-building]]"
  - "[[character-job]]"
  - "[[storage-furniture]]"
depended_on_by: []
---

# Help Wanted + Owner-Controlled Hiring

## Summary

Two coupled-but-independent primitives. **`DisplayTextFurniture`** is a generic placard / signboard / notice-board (gameplay-data MonoBehaviour + sibling `DisplayTextFurnitureNetSync` NetworkBehaviour for the `NetworkVariable<FixedString512Bytes>` text — mirrors the `StorageFurniture` + `StorageFurnitureNetworkSync` pattern). **Owner-controlled hiring** adds an `_isHiring: NetworkVariable<bool>` to `CommercialBuilding`, a designer-set `_helpWantedFurniture` reference, and a clean Owner-only API (`TryOpenHiring`, `TryCloseHiring`, `CanRequesterControlHiring`, `GetVacantJobs`, virtual text builders). The two compose: when both are wired, opening hiring auto-writes formatted vacancy text to the sign; closing reverts to the closed-state text. Both player owners (via `UI_OwnerHiringPanel`) and future NPC owners (Phase 2 GOAP) call the same API.

## Purpose

Plan 1 (Tool Storage primitive) shipped the management-gameplay foundation for tool stocking. Plan 2 (this) closes the **discovery + access-control** loop for hiring: a player walking into a town can read a building's Help Wanted sign and apply for a job in one click, while owners (player or future NPC) explicitly control whether they're accepting applications. The `IsHiring` gate also prevents NPC `NeedJob` AI from queueing applications at closed buildings (no thrash, no rejected-application reputation hits). Plan 3 (Farmer integration) consumes both primitives.

**Critical invariant** (per user direction during brainstorming): quests are still produced exclusively by `BuildingTaskManager`. The Help Wanted sign and owner-hiring controls are pure **discovery + access-control** layers — they surface existing quests and gate hiring. Nothing in this system generates quests directly.

## Responsibilities

- Designating a `DisplayTextFurniture` as a building's Help Wanted sign via `_helpWantedFurniture` reference.
- Server-authoritative `IsHiring` toggling via `TryOpenHiring` / `TryCloseHiring`.
- Owner-authority validation via `CanRequesterControlHiring`.
- Auto-formatting Help Wanted text on hiring open / close.
- Auto-refreshing sign text on hire-or-quit churn while hiring is open.
- Gating `InteractionAskForJob.CanExecute` and `BuildingManager.FindAvailableJob` on `IsHiring`.
- Player UI: `UI_DisplayTextReader` (read sign + Apply for Job button) and `UI_OwnerHiringPanel` (owner toggles + custom text editing).

## Non-responsibilities

- **Does not** create quests. `BuildingTaskManager` remains the sole quest source.
- **Does not** define what "owner" means beyond the existing `CommercialBuilding.Owner` reference.
- **Does not** support multi-vacancy Apply sub-menus in v1 (auto-picks first vacant job).
- **Does not** support multiple Help Wanted signs per building (one designer reference).
- **Does not** ship NPC owner AI for hiring decisions — Phase 2 reuses the same API surface.

## Key classes / files

| File | Role |
|---|---|
| [Assets/Scripts/World/Furniture/DisplayTextFurniture.cs](../../Assets/Scripts/World/Furniture/DisplayTextFurniture.cs) | Furniture subclass; gameplay-data MonoBehaviour |
| [Assets/Scripts/World/Furniture/DisplayTextFurnitureNetSync.cs](../../Assets/Scripts/World/Furniture/DisplayTextFurnitureNetSync.cs) | Sibling NetworkBehaviour with `NetworkVariable<FixedString512Bytes>` |
| [Assets/Scripts/World/Buildings/CommercialBuilding.cs](../../Assets/Scripts/World/Buildings/CommercialBuilding.cs) | `_isHiring`, `_helpWantedFurniture`, full hiring API, `HandleHiringStateChanged`, `HandleVacancyChanged` |
| [Assets/Scripts/World/Buildings/BuildingManager.cs](../../Assets/Scripts/World/Buildings/BuildingManager.cs) | `FindAvailableJob` IsHiring filter |
| [Assets/Scripts/Character/CharacterInteraction/InteractionAskForJob.cs](../../Assets/Scripts/Character/CharacterInteraction/InteractionAskForJob.cs) | `CanExecute` IsHiring gate |
| [Assets/Scripts/Character/CharacterJob/CharacterJob.cs](../../Assets/Scripts/Character/CharacterJob/CharacterJob.cs) | `GetInteractionOptions` Section B (Manage Hiring entry); `QuitJob` calls `NotifyVacancyChanged` |
| [Assets/Scripts/UI/PlayerHUD/UI_DisplayTextReader.cs](../../Assets/Scripts/UI/PlayerHUD/UI_DisplayTextReader.cs) | Player reader UI; routes Apply through existing `RequestJobApplicationServerRpc` |
| [Assets/Scripts/UI/PlayerHUD/UI_OwnerHiringPanel.cs](../../Assets/Scripts/UI/PlayerHUD/UI_OwnerHiringPanel.cs) | Owner management panel |
| [Assets/Resources/UI/UI_DisplayTextReader.prefab](../../Assets/Resources/UI/UI_DisplayTextReader.prefab) | Singleton-on-demand reader prefab |
| [Assets/Resources/UI/UI_OwnerHiringPanel.prefab](../../Assets/Resources/UI/UI_OwnerHiringPanel.prefab) | Singleton-on-demand owner panel prefab |

## Public API / entry points

See [help-wanted-and-hiring|SKILL.md] for full method signatures. Headline:
- `building.IsHiring` (read-only).
- `building.TryOpenHiring(player.Character)` / `TryCloseHiring`.
- `building.HelpWantedSign.TrySetDisplayText(player.Character, "...")`.
- `UI_DisplayTextReader.Show(sign)` (player presses E on sign → calls this).
- `UI_OwnerHiringPanel.Show(building)` (owner clicks "Manage Hiring..." menu entry → calls this).

## Data flow

```
Server flips _isHiring (via TryOpenHiring or TryCloseHiring)
        │
        │ NetworkVariable.OnValueChanged fires on Server + all Clients
        ▼
HandleIsHiringChanged(oldVal, newVal)
        ├─ public OnHiringStateChanged event invoked (server + clients)
        └─ if (IsServer): HandleHiringStateChanged(newVal)
                   │
                   ▼
        Read GetHelpWantedDisplayText() (open) or GetClosedHiringDisplayText() (close)
                   │
                   ▼
        _helpWantedFurniture.NetSync.ServerSetDisplayText(text)
                   │
                   ▼
        _displayText.Value = text  (NetworkVariable replicates to all clients)
                   │
                   ▼
        DisplayText getter on Furniture + UI bindings refresh

Vacancy churn (worker hired / quit):
        AssignWorker  → HandleVacancyChanged()
        QuitJob       → assignment.Workplace.NotifyVacancyChanged() → HandleVacancyChanged()
                                                  ├─ if (!IsServer || !_isHiring.Value) return;
                                                  └─ _helpWantedFurniture.NetSync.ServerSetDisplayText(GetHelpWantedDisplayText())

Player reads sign:
        Player presses E on DisplayTextFurniture
                   │
                   ▼
        Furniture.Use(character) → DisplayTextFurniture.Use override
                   │
                   ▼
        if (character.IsPlayer() && character.IsOwner): UI_DisplayTextReader.Show(this)

Player applies for job (sign Apply button):
        UI_DisplayTextReader.OnApplyClicked
                   │
                   ▼
        Validate (IsHiring + HasOwner + player has no job + vacancies > 0)
                   │
                   ▼
        Auto-pick first vacant Job → CharacterJob.RequestJobApplicationServerRpc(ownerNetId, stableIdx)
                   │
                   ▼
        Server-side: existing InteractionAskForJob path (re-validates IsHiring via CanExecute)

Player owner manages hiring:
        Owner walks up to any character → CharacterJob.GetInteractionOptions
                   │
                   ▼
        Section A "Apply for {JobTitle}" entries (existing) +
        Section B "Manage Hiring..." entry (NEW, when interactor.OwnedBuilding != null)
                   │
                   ▼
        UI_OwnerHiringPanel.Show(OwnedBuilding) → toggle hiring + edit sign text
```

## Dependencies

### Upstream
- [[commercial-building]] — owns `_isHiring`, `_helpWantedFurniture`, hiring API.
- [[character-job]] — `GetInteractionOptions` exposes the menu entries; `QuitJob` calls vacancy refresh.
- [[storage-furniture]] — pattern reference for the Furniture + sibling NetworkBehaviour split.
- [[tool-storage]] — Plan 1 foundation; Plan 3 (Farmer integration) consumes both this and Plan 1.

### Downstream
- (Plan 3 / future) `FarmingBuilding` will inherit the hiring API and override `GetHelpWantedDisplayText` for farm-specific flavor.
- (Phase 2) NPC owner GOAP actions — reuse the same `TryOpenHiring` / `TryCloseHiring` API with the NPC character as `requester`.

## State & persistence

- `_isHiring: NetworkVariable<bool>` — server-write / everyone-read. Default `true`. **NOT persisted across save/load in v1** — see persistence gap below.
- `_displayText: NetworkVariable<FixedString512Bytes>` on `DisplayTextFurnitureNetSync` — same model. Default empty (seeded from `_initialText` on first server spawn). **NOT persisted** — same gap.
- `_helpWantedFurniture: DisplayTextFurniture` — designer reference, scene-authored. No runtime mutation, no save needed.
- `_initialHiringOpen: bool` — designer reference, scene-authored. No runtime mutation.

**Persistence gap (v1 trade-off, accepted):** `BuildingSaveData` (in [MapRegistry.cs](../../Assets/Scripts/World/MapSystem/MapRegistry.cs)) does NOT contain `IsHiring` or `DisplayText` fields, so:
- An owner who closes hiring + saves + reloads will see hiring re-open to `_initialHiringOpen` (defaults to `true`).
- An owner who writes custom sign text + saves + reloads will lose the custom text — sign reverts to authoring `_initialText` (or auto-formatted vacancy text if hiring is open).
- A standalone `DisplayTextFurniture` (welcome plate, lore text — not wired to a building) loses any owner-written customisations on reload.

Backward compat: pre-existing saves load with `_isHiring.Value = true` (default), `_displayText.Value = ""` (default). Existing buildings remain "currently hiring" — exactly the behaviour they had before this system landed. **This trade-off was accepted for v1 to keep the implementation surface small;** Phase 2 follow-up: add `bool IsHiring` + `string DisplayTextOverride` (or per-sign keyed list) to `BuildingSaveData`, mirroring how `StorageFurnitures` is currently handled.

## Network rules

| Mutation | Authority | RPC pattern |
|---|---|---|
| `_isHiring` write | Server-only | client `TryOpenHiring` / `TryCloseHiring` → `[Rpc(SendTo.Server)]` → server validates owner authority → flip |
| `_displayText` write (owner-edited) | Server-only | client `TrySetDisplayText` → `[Rpc(SendTo.Server)]` → server validates owner authority → write |
| `_displayText` write (auto-managed) | Server-only | direct `ServerSetDisplayText` from `HandleHiringStateChanged` / `HandleVacancyChanged` |
| `_isHiring` read | Everyone | NetworkVariable replication |
| `_displayText` read | Everyone | NetworkVariable replication |

All four player-relationship scenarios validated:
- **Host owner ↔ Client applicant**: host runs gate, sign + IsHiring replicate to client, client reads sign and applies via existing `RequestJobApplicationServerRpc`.
- **Client owner ↔ Client applicant (host is third party)**: client owner sends `TryOpenHiring` ServerRpc → host validates owner identity → mutates → both clients see replication.
- **Host/Client ↔ NPC**: server-authoritative checks; NPC `NeedJob` reads `IsHiring` directly server-side via `BuildingManager.FindAvailableJob` filter.
- **Late joiner**: NetworkVariable spawn payload carries current `_isHiring` and `_displayText`. Sign and gate are correct on first frame.

## Known gotchas / edge cases

- **Custom sign text resets on reopen** (Q15.1) — `HandleHiringStateChanged` always overwrites with auto-formatted text. Owners who want persistent custom text must avoid the close→open cycle. Documented in the OwnerHiringPanel hint label.
- **Manage Hiring menu placement is suboptimal in v1** — appears on every character menu the owner walks into. Future iteration should scope to the building's own interactable.
- **Auto-refresh on `_initialHiringOpen=false`** — server-side `OnNetworkSpawn` calls `HandleHiringStateChanged(_isHiring.Value)` once if `_helpWantedFurniture` is set, so authoring's closed default correctly triggers a closed-state sign refresh from the first frame.
- **Sanitisation truncates long text silently** — `SanitiseAndClamp` clamps to ~480 UTF-8 bytes. No user-facing warning. Acceptable for v1; Phase 2 could add a length-counter to the input field.
- **Local-owner gate on `DisplayTextFurniture.Use`** — opens the reader UI only on the local player's machine (not on remote clients when one player presses E). Important multiplayer detail; without it, every peer would pop a reader simultaneously.

## Open questions / TODO

- **Phase 2: Persist `IsHiring` + `DisplayText` across save/load.** Add fields to `BuildingSaveData` (in [MapRegistry.cs](../../Assets/Scripts/World/MapSystem/MapRegistry.cs)) — currently the building reverts to `_initialHiringOpen` and signs revert to `_initialText` on every reload. See "Persistence gap" in State & persistence above.
- **Phase 2: NPC owner GOAP for hiring decisions.** Add `GoapAction_OwnerOpenHiring` / `GoapAction_OwnerCloseHiring` driven by a new `NeedHireWorkers` need. Trigger on vacancy + treasury threshold.
- **Phase 2: Multi-vacancy Apply sub-menu** when multiple distinct JobTitles are open at once.
- **Phase 2: Community-leader authority** in `CanRequesterControlHiring` — currently only checks `Owner == requester`.
- ~~**Phase 2: Move "Manage Hiring..." to building-scoped menu**~~ — **DONE** by Plan 2.5 (`ManagementFurniture`). Owner walks to the in-world desk; menu entry stays as fallback when no desk is wired.
- **Phase 2: Multi-sign support** per building.
- **Phase 2: Pool `UI_OwnerHiringPanel` job rows** instead of destroy + re-instantiate on every refresh (cosmetic for small lists; matters for buildings with many jobs).
- **Phase 2: Centralised `Character.LocalPlayer` accessor** — `ResolveLocalPlayerCharacter` is currently duplicated in `UI_DisplayTextReader` and `UI_OwnerHiringPanel` (and likely other UI scripts).

## Change log

- 2026-04-30 — **Plan 2.5 refinements:**
  - Added `ManagementFurniture` (`Assets/Scripts/World/Furniture/ManagementFurniture.cs`) — owner's hiring desk. Owner walks up + presses E → `UI_OwnerHiringPanel` opens. Non-owners get a "Only the owner can use this management desk" toast. Designer-set `_managementFurniture` reference on `CommercialBuilding`.
  - `CharacterJob.GetInteractionOptions` Section B "Manage Hiring..." entry now gated on `!HasManagementFurniture` — appears only as fallback when no in-world management desk is wired.
  - **Removed Apply-for-Job button from `UI_DisplayTextReader`.** Sign is now informative-only. Both player and NPC applicants must walk to the boss in person and use the existing `InteractionAskForJob` path. Sign wording tightened to "For application, see the owner in person." (Plan 2's button + handler + `ResolveLocalPlayerCharacter` helper deleted; prefab cleaned.)
  - **NPC `NeedJob` throttled to event-driven `TimeManager.OnNewDay` cache.** Per-tick `BuildingManager.FindAvailableJob` polling replaced with once-per-day server-side scan; cached `(building, job)` pair feeds `GetUrgency` (returns 0 when empty → Need is dormant). Substantial perf win at scale (100 NPCs × 1 query/day vs. 100 × N-ticks/day). `GetGoapActions` re-validates the cached pair (`IsAssigned`/`IsHiring`/`HasOwner`) before emitting actions. Mid-day staleness handled with one-frame replan returning empty actions; NPC idles until next day's re-scan. Subscribe pattern mirrors `NeedHunger.TrySubscribeToPhase` (POCO `CharacterNeed` driven by parent `CharacterNeeds` MonoBehaviour, not Unity lifecycle hooks). — claude
- 2026-04-30 — Initial implementation, Plan 2 of 3 in the Farmer rollout. Tasks 1-10 committed across `d9099024` … `692bfd02`. — claude

## Sources

- [docs/superpowers/specs/2026-04-29-farmer-job-and-tool-storage-design.md §15](../../docs/superpowers/specs/2026-04-29-farmer-job-and-tool-storage-design.md)
- [docs/superpowers/plans/2026-04-30-help-wanted-and-owner-hiring.md](../../docs/superpowers/plans/2026-04-30-help-wanted-and-owner-hiring.md)
- [docs/superpowers/smoketests/2026-04-30-help-wanted-and-hiring-smoketest.md](../../docs/superpowers/smoketests/2026-04-30-help-wanted-and-hiring-smoketest.md)
- [.agent/skills/help-wanted-and-hiring/SKILL.md](../../.agent/skills/help-wanted-and-hiring/SKILL.md)
- 2026-04-29 / 2026-04-30 conversation with [[kevin]]
