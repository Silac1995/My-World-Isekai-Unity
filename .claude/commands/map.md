---
description: Regenerate wiki/INDEX.md from the current state of the wiki.
argument-hint: (no arguments)
---

# /map — regenerate wiki/INDEX.md

Produce a fresh `wiki/INDEX.md` listing every page grouped by entity type.

## Procedure

1. Walk every `.md` file under `wiki/` except:
   - `wiki/CLAUDE.md`
   - `wiki/_templates/*`
   - `wiki/INDEX.md` itself
   - section-level `README.md` files (they get linked, not listed as entries).
2. For each page, read its frontmatter — pull `title`, `type`, `status`, `updated`.
3. Group by `type`, sort within group by `title`.
4. Emit `wiki/INDEX.md` with this shape:

```markdown
# Wiki Index

_Last regenerated: YYYY-MM-DD_

## Systems (N)
See also: [systems/README.md](systems/README.md)

- [[system-name]] — status · updated YYYY-MM-DD
- ...

## Mechanics (N)
- ...

## Decisions (N)
- ...

(etc. for every type with at least one page)

## Orphans
Pages with no inbound links and not listed in any section README:
- [[orphan-page]]
```

## Rules

- Only write `wiki/INDEX.md`. Never edit other files during `/map`.
- Exclude drafts? No — include everything, annotate with status.
- If a page is missing required frontmatter, list it under a `## Malformed` section at the bottom and suggest running `/lint`.
