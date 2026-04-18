# TODO — missing design documents & external docs

External documents that the wiki references but that **do not yet exist** in the
repo. These are design docs, specs, or architecture documents that the codebase
or project rules reference as if they were present.

| Missing document | Referenced from | Suggested location | Priority |
|---|---|---|---|
| `NETWORK_ARCHITECTURE.md` | Root `CLAUDE.md` rule #18; `wiki/systems/network.md` | `raw/design-docs/NETWORK_ARCHITECTURE.md` (or project root — keep rule #18 path working) | high |
| (more rows appended by Batch 1 and onward as gaps are found) | | | |

## How to process

1. Kevin authors the missing doc (or confirms it should be dropped).
2. If authored, drop it into `raw/design-docs/`.
3. Run `/ingest raw/design-docs/<file>.md` to enrich the relevant wiki pages.
4. Remove the row from this table.
