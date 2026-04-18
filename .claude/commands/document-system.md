---
description: Document one game system — produces pages in wiki/systems/ from source code and skills.
argument-hint: <system-name-or-entry-file> (e.g. "Combat" or "Assets/Scripts/Character/CharacterCombat.cs")
---

# /document-system — DOCUMENT-SYSTEM operation

Document the system identified by `$ARGUMENTS`.

## Step 0 — resolve the target

Parse `$ARGUMENTS`:
- If it looks like a file path (`*.cs`, `Assets/...`), the system is the folder or module containing that file.
- Otherwise, treat it as a system name and search:
  1. `wiki/systems/<kebab-name>.md` — if it exists, this is an update.
  2. `.agent/skills/<kebab-name>/SKILL.md` — if it exists, use as procedural source.
  3. `.claude/agents/<kebab-name>-specialist.md` or `<kebab-name>-architect.md` — primary agent.
  4. `Assets/Scripts/<kebab-name>/` or similar folder.

**If the scope is unclear** — more than one plausible match, or no match at all — **stop and ask numbered questions**.

## Step 1 — gather sources

Read, in this order:
1. The matching `.agent/skills/*/SKILL.md` (procedures — source for linking, not content).
2. The specialist agent `.claude/agents/*.md` (context only — do NOT ingest).
3. The primary code path (read enough to understand the architecture).
4. Any `raw/design-docs/*` that mention the system.
5. Any existing `wiki/systems/<name>.md` for change-log continuity.

## Step 2 — produce the page

Use `wiki/_templates/system.md` as the starting shape. Fill every required section:

1. **Summary** (1 paragraph).
2. **Purpose** (why it exists).
3. **Responsibilities** (what it owns; include explicit non-responsibilities).
4. **Key classes / files** (table with relative links to source).
5. **Public API / entry points** (methods, events).
6. **Data flow** (inputs → processing → outputs; Server vs Client authority if networked).
7. **Dependencies** (upstream + downstream, all wikilinks).
8. **State & persistence** (runtime + saved state).
9. **Known gotchas / edge cases** (wikilinks to `wiki/gotchas/*`).
10. **Open questions / TODO**.
11. **Change log** (append today's entry).
12. **Sources** (MUST include at least one source file + the matching SKILL.md if one exists).

## Step 3 — frontmatter

- `primary_agent`: matching agent filename (no extension) from `.claude/agents/`, or `null`.
- `secondary_agents`: other relevant agents.
- `owner_code_path`: relative path to the main folder/file.
- `depends_on`: `[[wikilinks]]` to upstream systems.
- `depended_on_by`: `[[wikilinks]]` to downstream systems.
- `status`: `planned | wip | stable | deprecated`.
- `updated`: today's date.

## Step 4 — splitting large systems

If the system has >10 classes or multiple sub-modules, split:
- A **parent overview page** at `wiki/systems/<name>.md`.
- **Child sub-pages** at `wiki/systems/<name>-<subsystem>.md`.
- Parent links to children; children link back to parent.

## Step 5 — cross-link & backlinks

- Every mentioned system, gotcha, decision, mechanic must be a wikilink.
- Update reciprocal `related:` on every target page.
- Update `wiki/systems/README.md` if new pages were created.

## Rules

- **Never copy procedural content from SKILL.md.** Link to it in Sources and reference it as the how-to source.
- If there is no matching agent, set `primary_agent: null` and note in Open questions that one may be worth creating.
- If the user didn't specify scope and the system is large, ask whether they want a full tier-1 page or a tier-2 stub.
- If touching more than 5 files, output a summary diff first and wait for approval.
