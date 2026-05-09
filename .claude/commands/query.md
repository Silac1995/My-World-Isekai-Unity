---
description: Answer a question using the wiki first, source code only as fallback.
argument-hint: <free-form question>
---

# /query — QUERY operation

Answer: **"$ARGUMENTS"** using the wiki as primary source.

## Procedure (from wiki/CLAUDE.md §6.2)

1. **Consult `wiki/` first.**
   - Read `wiki/INDEX.md` if it exists.
   - Read the systems index at `wiki/systems/README.md`.
   - Follow wikilinks to the most relevant pages.
2. Only if the wiki is **clearly insufficient**:
   - Read relevant files under `raw/`.
   - As a last resort, read source code under `Assets/`.
3. Formulate the answer.
4. **Cite every wiki page used** by wikilink in the answer.
5. If you had to fall back to `raw/` or source code, flag that at the end and suggest running `/ingest <path>` or `/document-system <name>` to close the gap.

## Rules

- Never answer from memory alone. Always cite.
- If a wiki page seems stale (`updated` > 90 days old on an `active`/`wip` page), flag it.
- If two wiki pages contradict each other, flag that conflict and suggest `/lint` to merge them.

## Output shape

```
**Answer:** <prose>

**Sources used:**
- [[page-one]]
- [[page-two]]

**Gaps (if any):**
- <gap> — suggested action: `/ingest ...` or `/document-system ...`
```
