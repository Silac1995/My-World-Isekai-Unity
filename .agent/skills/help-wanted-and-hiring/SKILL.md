# Help Wanted + Owner-Controlled Hiring System

Two coupled-but-independent primitives that surface job availability in-world (`DisplayTextFurniture` signboard + auto-formatted Help Wanted text) and let building owners explicitly gate `AskForJob` admissions (`IsHiring` NetworkVariable + Owner-only API). Built on top of Plan 1's Tool Storage primitive; consumed by Plan 3's Farmer integration.

## Public API

### `DisplayTextFurniture` (Furniture subclass)
```csharp
string InitialText { get; }                          // designer-set authoring default
string DisplayText { get; }                          // current text (replicated via NetSync)
event Action<string> OnDisplayTextChanged

bool TrySetDisplayText(Character requester, string newText)   // owner-gated mutation; routes ServerRpc on client
```

### `DisplayTextFurnitureNetSync` (sibling NetworkBehaviour)
```csharp
NetworkVariable<FixedString512Bytes> _displayText    // server-write / everyone-read
event Action<string> OnDisplayTextChanged

internal void ServerSetDisplayText(string newText)   // unrestricted server-side path; used by CommercialBuilding hiring auto-update
```

### `CommercialBuilding` (hiring extension)
```csharp
DisplayTextFurniture HelpWantedSign { get; }         // designer reference (may be null)
ManagementFurniture ManagementFurniture { get; }     // designer reference (may be null) — Plan 2.5
bool HasManagementFurniture { get; }                 // Plan 2.5
bool IsHiring { get; }                               // NetworkVariable<bool>, replicated
event Action<bool> OnHiringStateChanged

bool CanRequesterControlHiring(Character requester)  // owner-authority check
IReadOnlyList<Job> GetVacantJobs()
IReadOnlyList<Job> Jobs { get; }                     // stable index for ServerRpc round-trip
int GetJobStableIndex(Job job)

bool TryOpenHiring(Character requester)              // server-auth; client wrapper routes via [Rpc(SendTo.Server)]
bool TryCloseHiring(Character requester)
void NotifyVacancyChanged()                          // public shim called by CharacterJob.QuitJob

protected virtual string GetHelpWantedDisplayText()  // override per subclass for flavor
protected virtual string GetClosedHiringDisplayText()
```

### `ManagementFurniture` (Plan 2.5 — owner's hiring desk)
```csharp
// Owner-only Use override. Designer-placed inside a CommercialBuilding.
// - Player owner: opens UI_OwnerHiringPanel for the parent building.
// - Player non-owner: toast "Only the owner can use this management desk."
// - NPC: silent success (no AI uses it in v1; Phase 2 NPC-owner GOAP can call TryOpenHiring directly).
// - Remote-client gate: only the local player's machine pops the UI.
public override bool Use(Character character)
```

No NetworkBehaviour sibling — owns no replicated state. Future driveable-entity migration replaces `Use` internals; public API stable.

### Existing systems extended
- `InteractionAskForJob.CanExecute` — added `if (!_building.IsHiring) return false;` gate.
- `BuildingManager.FindAvailableJob<T>` — added `if (!commercial.IsHiring) continue;` filter (skips closed buildings for NPC `NeedJob` discovery).
- `CharacterJob.GetInteractionOptions` — added Section B emitting `Manage Hiring...` entry when `interactor.CharacterJob.OwnedBuilding != null`.
- `CommercialBuilding.AssignWorker` — calls `HandleVacancyChanged()` after binding.
- `CharacterJob.QuitJob` — calls `assignment.Workplace?.NotifyVacancyChanged()` after unbinding.

## Player UI

### `UI_DisplayTextReader` (singleton-on-demand) — informative-only as of Plan 2.5
- Opens on `DisplayTextFurniture.Use(Character)` when interactor is the local player.
- Title = parent `BuildingName` (or "Sign" if not building-parented), body = `DisplayText`.
- **No Apply button** (removed in Plan 2.5). Sign is purely informative — the auto-formatted Help Wanted text ends with "For application, see the owner in person." Both player and NPC applicants must walk to the boss in person and use the existing `InteractionAskForJob` path (the hold-E menu on the boss).
- ESC / Close button / outside-click overlay all dismiss.

### `UI_OwnerHiringPanel` (singleton-on-demand)
- Reachable via `CharacterJob.GetInteractionOptions` Section B "Manage Hiring..." entry when interactor has `OwnedBuilding != null`.
- Status header (green Yes / red No), scrollable `Jobs` list ("JobTitle — vacant" or "JobTitle — Bob"), Open/Close hiring toggle, multi-line custom-text input + Submit button, hint label calling out the Q15.1 reopen-overwrites-custom-text invariant.
- Auto-refreshes on `OnHiringStateChanged` event.

## Integration points

- **Sign auto-update on hiring flip** — `_isHiring.OnValueChanged → HandleIsHiringChanged → HandleHiringStateChanged` (server-only) writes `GetHelpWantedDisplayText()` (open) or `GetClosedHiringDisplayText()` (close) to `_helpWantedFurniture.NetSync`.
- **Sign auto-update on vacancy churn** — `AssignWorker / NotifyVacancyChanged → HandleVacancyChanged` (server-only) refreshes the sign when hiring is open and a worker is hired or quits.
- **NetworkVariable replication** — `_displayText` and `_isHiring` are server-write / everyone-read; clients see updates within one frame.
- **Late-joiner support** — both NetworkVariables auto-sync current values during the spawn handshake.

## Events

- `DisplayTextFurniture.OnDisplayTextChanged(string)` — fires on server + clients when text changes.
- `CommercialBuilding.OnHiringStateChanged(bool)` — fires on server + clients when `_isHiring` flips.

## Dependencies

- `Furniture` base class (existing `Use(Character)` virtual) — reading entry point.
- `StorageFurniture` + `StorageFurnitureNetworkSync` pattern — `DisplayTextFurniture` mirrors this for sibling-NetworkBehaviour replication.
- `CommercialBuilding.Owner` (existing) — basis for `CanRequesterControlHiring`.
- `CharacterJob.RequestJobApplicationServerRpc` (existing per 2026-04-24 hold-E menu work) — Apply button reuse.

## Gotchas

- **`_displayText` MUST stay `[SerializeField]`-equivalent** via NetworkVariable — pure replication; no JsonUtility round-trip needed because the field lives on a NetworkBehaviour, not a serialised data class. Persistence relies on the NetworkObject save path (which captures NetworkVariable values).
- **Custom sign text is overwritten on reopen** (Q15.1) — by design. Owner's custom message survives `TryOpenHiring` while hiring is already open (no overwrite triggered), but a `TryCloseHiring → TryOpenHiring` cycle resets to auto-formatted text. Documented in the OwnerHiringPanel hint label.
- **Manage Hiring menu placement** — V1 emits the entry on every character interaction the owner-player walks into, not just on the building itself. Pragmatic but pollutes other menus. Future iteration can move to a building-specific interactable for cleaner UX scoping.
- **Multi-vacancy Apply UI** — V1 auto-picks the FIRST vacant job from `GetVacantJobs()` when the player clicks Apply. If a building has multiple distinct JobTitles open (e.g. "Harvester" + "Logistics Manager"), the player can't choose which to apply for. Phase 2 follow-up: sub-menu when multiple distinct titles are vacant.
- **Sanitisation** — `TrySetDisplayText` strips control chars (preserves `\n` / `\t`) and clamps to ~480 UTF-8 bytes to leave headroom inside the 512-byte FixedString. Long pastes get silently truncated.
- **Closed-state default text** — `GetClosedHiringDisplayText()` returns empty string by default, so the sign goes blank when hiring closes. Subclasses can override for flavor (e.g. ShopBuilding might say "Shop is staffed; check back another day.").

## Follow-ups (Phase 2 candidates)

- NPC owner GOAP for hiring: `GoapAction_OwnerOpenHiring` / `GoapAction_OwnerCloseHiring` reuse the same `TryOpenHiring` / `TryCloseHiring` API. Trigger: vacancy + treasury health.
- Multi-vacancy Apply sub-menu when multiple distinct job titles are open at once.
- Move "Manage Hiring..." entry to the building's own interactable so it's scoped correctly (currently appears on any character menu the owner-player approaches).
- Multi-sign-per-building support (currently `_helpWantedFurniture` is a single reference; designer can place multiple `DisplayTextFurniture` instances but only the referenced one is auto-managed).
- Community-leader authority in `CanRequesterControlHiring` — currently only checks `Owner == requester`; future addition would also accept community leaders.

## See also

- Spec: [docs/superpowers/specs/2026-04-29-farmer-job-and-tool-storage-design.md §15](../../docs/superpowers/specs/2026-04-29-farmer-job-and-tool-storage-design.md)
- Plan: [docs/superpowers/plans/2026-04-30-help-wanted-and-owner-hiring.md](../../docs/superpowers/plans/2026-04-30-help-wanted-and-owner-hiring.md)
- Smoketest: [docs/superpowers/smoketests/2026-04-30-help-wanted-and-hiring-smoketest.md](../../docs/superpowers/smoketests/2026-04-30-help-wanted-and-hiring-smoketest.md)
- Wiki page: [wiki/systems/help-wanted-and-hiring.md](../../wiki/systems/help-wanted-and-hiring.md)
- Plan 1 (Tool Storage): [docs/superpowers/plans/2026-04-29-tool-storage-primitive.md](../../docs/superpowers/plans/2026-04-29-tool-storage-primitive.md) — foundation this builds on.
