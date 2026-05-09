# Post-merge TODO — systems blocked on `feature/character-archetype-system`

Systems whose code lives on the feature branch `feature/character-archetype-system`
(currently not merged into `multiplayyer`). Their wiki pages on this branch are
**stubs** — they must be fully fleshed out after that branch merges.

| Wiki page | Reason | Priority |
|---|---|---|
| [[terrain-and-weather]] | Entire Terrain/, Weather/, CharacterTerrain/, Audio/footstep code is on feature branch. SKILL.md files for `terrain-weather` and `character-terrain` also live only there. | high |
| [[character-archetype]] | `Assets/Scripts/Character/Archetype/` is empty on `multiplayyer`; code on feature branch. | high |
| [[character-terrain]] | `Assets/Scripts/Character/CharacterTerrain/` is empty on `multiplayyer`; code on feature branch. | medium |

## How to process after merge

1. Check out `multiplayyer` once the feature branch is merged.
2. For each row, run `/document-system <name>` to refresh the page from the
   now-landed code.
3. Flip the page's `confidence` to `high` and remove the stub banner.
4. Remove the row from this table.
