# TODO — missing SKILL.md files

Project rule #28 says every system has a SKILL.md. This backlog lists systems
documented in the wiki that **do not yet have** a corresponding
`.agent/skills/<name>/SKILL.md`. Kevin writes these; Claude never creates SKILL.md.

Each row: system name (wikilink), one-line scope suggestion, priority.

| System | Suggested SKILL.md scope | Priority |
|---|---|---|
| [[character-progression]] | Level/XP progression, how combat XP feeds `CharacterCombatLevel`, stat-point allocation UX | medium |
| [[character-profile]] | Personality, compatibility, `GetCompatibilityWith`; how profile data plugs into [[save-load]] | medium |
| [[character-speech]] | Speech bubble rendering API (`SayAmbient`, `SayScripted`), networking, pacing rules | medium |
| [[character-body-parts]] | Body part rendering, bone attachment, per-part customization | medium |
| [[character-animation-sync]] | How animation state syncs to clients; overrides for combat actions | medium |
| [[character-book-knowledge]] | Books as `IAbilitySource`; learning abilities outside mentorship | low |
| [[character-community]] | Character-side adapter (founding gate); relationship with world-community | medium |
| [[character-blueprints]] | `UnlockedBuildingIds`, how leaders expand cities offline | medium |
| [[character-locations]] | `CharacterLocations.cs` purpose and API | low |
| [[combat-engagement]] | Formation, zone control, `CombatEngagementCoordinator` internals | high |
| [[combat-abilities]] | `AbilitySO` hierarchy, `CharacterAbilities`, 9 passive triggers — partially covered in combat_system SKILL already; decide whether to split | medium |
| [[ai-actions]] | GOAP action library (the 19 actions) | medium |
| [[ai-conditions]] | BT condition library | medium |
| time-manager (engine-plumbing) | Global time, `TimeManager.CurrentDay`/`CurrentTime01`; distinct from day/night visuals | high |
| day-night-cycle (engine-plumbing) | Visual day/night transition; relation to time-manager | medium |
| camera-follow (engine-plumbing) | `CameraFollow.cs` behaviour, target switch | low |
| spawn-manager (engine-plumbing) | Character/item spawning; networked spawn flow | medium |
| screen-fade-manager (engine-plumbing) | Transition fades, real-time (unscaled) | low |
| map-system | `MapController`, `MapSaveData`, transition pipeline — may warrant its own SKILL separate from world-system | high |
| world-zones | Named zones within a map | low |
| world-data | World-level persistent data SOs | low |
| grass-system | `Assets/Scripts/Grass/` — scope unknown; flag for Kevin | low |
| `ItemMaterial.cs` | Purpose of this new script (in `Assets/Scripts/Items/`) | medium |

## How to process

1. Pick a row.
2. Follow `.agent/skills/skill-creator/SKILL.md` to scaffold a new skill.
3. Fill it with operational procedures (NOT architecture — that's what the
   matching wiki page is for).
4. Add the back-reference from the wiki page's `Sources` section.
5. Remove the row from this table.
