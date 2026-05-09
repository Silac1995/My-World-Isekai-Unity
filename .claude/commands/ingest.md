---
description: Ingest a file from raw/ into the wiki — distill and cross-link 5–15 pages.
argument-hint: <path-under-raw> (e.g. raw/articles/karpathy-llm-os.md)
---

# /ingest — INGEST operation

Run the INGEST procedure on the file at `$ARGUMENTS`.

## Rules (from wiki/CLAUDE.md §6.1)

1. The path must be under `raw/`. If it is not, stop and ask the user to move the file.
2. Read the file **completely** — no skimming.
3. Identify **5 to 15** wiki pages that the content impacts. These can be:
   - existing pages (update them) or
   - new pages you need to create from templates in `wiki/_templates/`.
4. For every impacted page:
   - Update the relevant sections.
   - Bump `updated:` to today's date.
   - Append the raw file to the page's `sources:` list.
   - Add any newly-learned relationships to the `related:` list (wikilinks only).
5. Add **reciprocal backlinks** on every page you linked to.
6. If more than 5 files will be touched, output a summary diff first and wait for approval (see wiki/CLAUDE.md §8).
7. If anything is ambiguous or contradicts existing wiki content, **stop and ask numbered questions** (§9).

## Output

At the end, summarize:
- Files created (with wikilinks).
- Files updated (with wikilinks).
- Open questions that remain.
- Suggested next `/ingest` or `/document-system` calls.

## Reminders
- Never modify, rename, or delete the raw file.
- Never copy procedural content from a SKILL.md into the wiki — link, don't mirror.
