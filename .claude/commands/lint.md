---
description: Audit the wiki — duplicates, broken links, orphans, stale pages, missing frontmatter.
argument-hint: (no arguments)
---

# /lint — LINT operation

Scan all `.md` files under `wiki/` and report issues. **Propose fixes before executing.**

## Detection checklist (from wiki/CLAUDE.md §6.3)

1. **Duplicate entities** — two pages covering the same concept/system.
2. **Broken wikilinks** — `[[name]]` pointing to non-existent pages.
3. **Orphan pages** — no inbound links, not listed in any `INDEX.md` or section `README.md`.
4. **Missing frontmatter fields** — check against the required fields in `wiki/CLAUDE.md §3`.
5. **Inconsistent tag casing** — tags must be kebab-case.
6. **Stale pages** — `status: active|wip` with `updated` more than 90 days old.
7. **Missing Sources** — pages with zero entries under `## Sources`.
8. **`system` pages with missing** `primary_agent`, `owner_code_path`, `depends_on`, or `depended_on_by`.

## Procedure

1. Produce a **report** grouped by issue category.
2. For each issue, propose a specific fix (rename, merge, delete, add link, add source).
3. Output a **summary diff** (file-by-file: create / update / delete / move).
4. **Wait for explicit user approval** before executing any fix that touches more than 5 files.
5. After execution, show a final summary: what was fixed, what was skipped, what still needs manual attention.

## Rules

- Never merge two pages silently. Always show the proposed merged content first.
- Never delete a page silently. If deletion is proposed, show what will be lost.
- If a fix requires judgment (e.g. "which of these two pages is canonical?"), **stop and ask**.
