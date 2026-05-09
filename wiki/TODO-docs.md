# TODO — missing design documents & external docs

External documents that the wiki references but that **do not yet exist** in the
repo. These are design docs, specs, or architecture documents that the codebase
or project rules reference as if they were present.

| Missing document | Referenced from | Suggested location | Priority |
|---|---|---|---|
| `NETWORK_ARCHITECTURE.md` | Root `CLAUDE.md` rule #18; will be referenced from `wiki/systems/network.md` in Batch 3 | `raw/design-docs/NETWORK_ARCHITECTURE.md` (or project root — keep rule #18 path working) | high |
| Pricing model spec | `wiki/systems/shops.md` — open question on whether pricing lives on `ItemSO` or per-shop | `raw/design-docs/pricing-model.md` | medium |
| Dialogue multiplayer advance semantics | `wiki/systems/dialogue.md` — which player's input advances in co-op? | `raw/design-docs/dialogue-mp-rules.md` | low |

## How to process

1. Kevin authors the missing doc (or confirms it should be dropped).
2. If authored, drop it into `raw/design-docs/`.
3. Run `/ingest raw/design-docs/<file>.md` to enrich the relevant wiki pages.
4. Remove the row from this table.
