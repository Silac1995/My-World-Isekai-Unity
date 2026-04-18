# LLM Wiki — Schema & Operating Rules

This file is the single source of truth for how the wiki at `wiki/` is maintained.
It is loaded by Claude at the start of every wiki operation. It does **not** replace
the root `CLAUDE.md` — root rules still apply to all code and all conversations.
This file only governs wiki content.

> **Relationship to `.agent/skills/`:** The skill files are the **source of truth for
> procedures** (how to do things). The wiki is the **source of truth for architecture**
> (what things are, why they exist, how they connect). No procedural duplication.
> Every system page in `wiki/systems/` must link to its relevant SKILL.md in its
> Sources section.

---

## 1. Core principle — raw/ vs wiki/

```
raw/   = the user's space. Sources, transcripts, notes, design docs. Immutable to Claude.
wiki/  = Claude's space. Distilled, linked, structured. Immutable to the user.
```

**Hard rules:**

- Claude **never** modifies, renames, deletes, or reorders any file under `raw/`.
- The user **never** writes directly in `wiki/` — all wiki changes go through
  a Claude operation (`INGEST`, `QUERY`, `LINT`, `DOCUMENT-SYSTEM`, `SAVE`, `MAP`).
- When `raw/` and `wiki/` disagree, `raw/` wins — the wiki is re-derived, not
  the other way around.
- Source code (`Assets/`, `Packages/`, etc.) is also **raw** for the purposes of
  this wiki — never edit source code as part of a wiki operation.

## 2. Entity types

Every file under `wiki/` (except `INDEX.md` and sub-index `README.md` files) is a
**page** that represents exactly one entity. The entity type dictates the folder
and the required frontmatter fields.

| Type      | Folder                 | Purpose                                                      |
|-----------|------------------------|--------------------------------------------------------------|
| concept   | `wiki/concepts/`       | Atomic idea, definition, term, invariant.                    |
| project   | `wiki/projects/`       | Ongoing effort, initiative, milestone.                       |
| person    | `wiki/people/`         | Collaborator, stakeholder, external contact.                 |
| reference | `wiki/references/`     | External pointer (URL, paper, video, book, spec).            |
| meeting   | `wiki/meetings/`       | A single meeting or sync — notes, decisions, action items.   |
| decision  | `wiki/decisions/`      | ADR — one accepted architectural/design decision.            |
| gotcha    | `wiki/gotchas/`        | Pitfall, bug class, lesson learned, non-obvious constraint.  |
| system    | `wiki/systems/`        | A game system — architectural encyclopedia entry.            |
| mechanic  | `wiki/mechanics/`      | A gameplay mechanic (combat move, crafting recipe class).    |
| content   | `wiki/content/`        | Concrete game content (characters, items, levels, enemies).  |
| pipeline  | `wiki/pipelines/`      | Build, CI, asset pipeline, deployment, tooling flow.         |

## 3. Required frontmatter

Every page starts with YAML frontmatter. Fields marked **required** must be present.

### Common fields (all entity types)

```yaml
---
type: <one of: concept | project | person | reference | meeting | decision | gotcha | system | mechanic | content | pipeline>  # required
title: "Natural-language title"   # required — matches the H1
tags: [kebab-case, tag-list]       # required, can be empty []
created: YYYY-MM-DD                # required — date the page was first created
updated: YYYY-MM-DD                # required — bumped on every edit
sources: []                        # required — see §5
related: []                        # required — list of [[wikilinks]] to related pages, can be empty
status: active                     # required — see allowed values per type below
---
```

### Per-type `status` values

| Type      | Allowed `status` values                                          |
|-----------|------------------------------------------------------------------|
| concept   | `draft | active | deprecated`                                    |
| project   | `planned | active | paused | done | abandoned`                   |
| person    | `active | inactive`                                              |
| reference | `active | broken | archived`                                     |
| meeting   | `upcoming | held | cancelled`                                    |
| decision  | `proposed | accepted | superseded | rejected`                    |
| gotcha    | `open | mitigated | resolved`                                    |
| system    | `planned | wip | stable | deprecated`                            |
| mechanic  | `planned | prototype | shipped | cut`                            |
| content   | `planned | wip | shipped | cut`                                  |
| pipeline  | `planned | wip | stable | deprecated`                            |

### Additional fields per type

- **system** (required):
  - `primary_agent: <agent-filename-without-extension or null>` — the specialist
    agent from `.claude/agents/` that owns this system. Null if none matches.
  - `secondary_agents: []` — other relevant agents.
  - `owner_code_path: <relative path to the main folder/file>` — e.g. `Assets/Scripts/Character/`.
  - `depends_on: []` — list of `[[other-system]]` wikilinks it needs to function.
  - `depended_on_by: []` — systems that consume this one.
- **decision** (required):
  - `decision_date: YYYY-MM-DD`
  - `decided_by: [[person-name]]` or `"team"` if collective.
  - `supersedes: [[previous-decision]]` or `null`.
- **meeting** (required):
  - `meeting_date: YYYY-MM-DD`
  - `attendees: [[...]]`
- **person** (required):
  - `role: "short role description"`
- **reference** (required):
  - `url: <full URL or local path>`
  - `kind: article | paper | video | book | spec | repo | other`
- **project** (required):
  - `start_date: YYYY-MM-DD`
  - `target_date: YYYY-MM-DD | null`
- **content** (required):
  - `content_kind: character | item | level | enemy | quest | dialogue | other`

## 4. Page body

Every page body MUST have these sections in this order (even if a section is
just `_(none)_` for now):

```markdown
# <Natural-language title — matches frontmatter `title`>

## Summary
One paragraph. Reading just this should tell a new reader what this page is about.

## <Type-specific sections — see templates in wiki/_templates/>

## Links
- Inbound / outbound [[wikilinks]] not already in frontmatter `related`.

## Sources
- Bulleted list of raw/ files, source code files, URLs, meetings. See §5.
```

For `system` pages specifically, the type-specific section layout is:

1. Purpose
2. Responsibilities
3. Key classes / files
4. Public API / entry points
5. Data flow
6. Dependencies (upstream / downstream)
7. State & persistence
8. Known gotchas / edge cases
9. Open questions / TODO
10. Change log

## 5. Linking & sourcing rules

### 5.1 Wikilinks are mandatory

- Every time a page mentions another entity that exists or should exist in the
  wiki, it **must** use Obsidian-style wikilinks: `[[system-name]]`, `[[jane-doe]]`,
  `[[adr-0003-networking-authority]]`.
- If the target page doesn't exist yet, the wikilink is still required — it
  creates a dangling link that `LINT` will flag for creation.
- Never use a bare plain-text mention of another entity without linking it.

### 5.2 Sources list

Every page ends with a `## Sources` section listing every file, URL, or
conversation that contributed to the page. Format:

```markdown
## Sources
- [raw/design-docs/combat-v2.md](../../raw/design-docs/combat-v2.md) — original design brief
- [CharacterCombat.cs](../../Assets/Scripts/Character/CharacterCombat.cs) — primary implementation
- [.agent/skills/combat_system/SKILL.md](../../.agent/skills/combat_system/SKILL.md) — operational procedures
- 2026-04-18 conversation with Kevin — tier decisions
```

- Relative paths, always from the page's location. Prefer `../../Assets/...` style
  over absolute paths.
- Every `system` page **must** list at least one source code file and (if one
  exists) its matching `.agent/skills/*/SKILL.md`.
- Every `decision`, `meeting`, `gotcha` page **must** list the conversation or
  source that triggered it.

### 5.3 Backlinks

When you create or update a page, also update the `related` frontmatter (or add
a `Links` section entry) on every page it points to, so backlinks are reciprocal.

## 6. The four core operations

### 6.1 INGEST — `/ingest <path-in-raw>`

Trigger: user adds a file under `raw/` and runs `/ingest <path>`.

Procedure:
1. Read the entire file at `<path>`. Do not skim.
2. Identify **5–15** pages it impacts (existing or new).
3. For each impacted page:
   - If it exists: update the relevant sections, bump `updated:`, append to
     `sources:` and `related:`, add any new gotchas/decisions as separate pages.
   - If it doesn't exist: create it from the matching template in
     `wiki/_templates/`.
4. Add reciprocal backlinks.
5. Summarize to the user: what was updated, what was created, what open
   questions remain.

Rules:
- If the source is ambiguous or contradicts existing wiki content, **stop and
  ask** before writing.
- Never delete a raw/ file. Never rename it.

### 6.2 QUERY — `/query <question>`

Trigger: user asks a question via `/query` or by natural prompt.

Procedure:
1. First, consult `wiki/` — read `INDEX.md`, then the most relevant pages.
2. Only if the wiki is clearly insufficient: reread relevant files in `raw/`
   or source code.
3. Answer the user's question and **cite every wiki page used** (by wikilink).
4. If you had to fall back to `raw/` or source code, flag that and suggest
   the user run `/ingest` on that path to enrich the wiki.

Rules:
- Never answer from memory alone. Always cite.
- If the wiki has a stale page on the topic, flag it and propose an update.

### 6.3 LINT — `/lint`

Trigger: user runs `/lint`.

Procedure:
1. Scan all `.md` files under `wiki/`.
2. Detect and report:
   - Duplicate entities (two pages for the same concept/system).
   - Broken wikilinks (pointing to non-existent pages).
   - Orphan pages (no inbound links, not in any INDEX).
   - Missing frontmatter fields.
   - Inconsistent tag casing or naming.
   - Stale `updated:` dates (> 90 days old on `status: active|wip` pages).
   - Pages missing a `Sources` section or with zero sources.
3. Propose a fix plan (as a diff preview) **before** executing anything.
4. Execute only after the user approves.

Rules:
- Never merge two pages silently. Always show the proposed merge first.
- Never delete a page silently. Always ask first.

### 6.4 DOCUMENT-SYSTEM — `/document-system <system-name-or-entry-file>`

Trigger: user runs `/document-system Combat` or `/document-system Assets/Scripts/Character/CharacterCombat.cs`.

Procedure:
1. If the system scope is unclear, **stop and ask numbered questions**.
2. Read:
   - The matching `.agent/skills/*/SKILL.md` if one exists.
   - The specialist agent `.claude/agents/*.md` if one exists (for context, not
     as a source — see rule #3 of the project setup).
   - The primary code path (declared by user or inferred from entry file).
   - Any `raw/design-docs/*` that mention the system.
3. Produce (or update) a page in `wiki/systems/` using the `system` template.
4. If the system is large (>10 classes or multiple sub-modules), split into a
   parent overview page + child sub-pages, linked via wikilinks.
5. Fill frontmatter fully: `primary_agent`, `owner_code_path`, `depends_on`,
   `depended_on_by`, `status`.
6. Cross-link to every mentioned system, gotcha, decision, mechanic.
7. Update the system's `change_log` section with the documentation pass date.

Rules:
- **Never copy procedural content from the SKILL.md.** Link to it in `Sources`
  and reference it as the how-to source. The wiki page describes architecture,
  not steps.
- If a system has no matching agent, set `primary_agent: null` and add a note
  in "Open questions / TODO" suggesting one could be created.

## 7. Naming conventions

- Filenames: `kebab-case.md`. Example: `character-combat.md`, `adr-0004-netcode-authority.md`.
- Page titles (H1 and frontmatter `title`): natural language. Example:
  `# Character Combat`, `# ADR 0004 — Netcode Authority Model`.
- Tags: `kebab-case`. Example: `character-system`, `net-authority`, `wip`.
- ADR filenames: `adr-NNNN-short-title.md` with zero-padded 4-digit number.
- Meeting filenames: `YYYY-MM-DD-short-title.md`.
- System sub-pages: `<parent-system>-<subsystem>.md`. Example: `combat-damage.md`.

## 8. Diff rule

- For any operation that touches **more than 5 files**, Claude **must** first
  output a summary diff (file-by-file: create / update / delete / move) and
  ask for explicit approval.
- For operations touching ≤ 5 files, Claude may proceed and show the diff in
  the final summary.
- A "file touched" includes frontmatter-only edits.

## 9. Ambiguity rule

If at any point during INGEST, QUERY, LINT, or DOCUMENT-SYSTEM Claude encounters
ambiguity that would **materially change the output** — missing context, two
plausible interpretations, unknown scope — Claude **must stop and ask numbered
questions**. Do not guess. Do not fabricate. The user prefers 5 sharp questions
over a hallucinated decision.

Examples of material ambiguity:
- A system has two candidate code paths and it's unclear which is canonical.
- A raw source mentions a concept that partially overlaps an existing wiki page.
- A template field has no clear value from the source.
- User intent for a new page is unclear (a stub vs a full fleshed-out page).

Non-ambiguous cases (proceed without asking):
- Straightforward field updates with obvious values from the source.
- Backlink maintenance.
- Typo fixes in existing pages.

## 10. Change log & audit trail

- Every `system`, `project`, `decision` page has a `## Change log` section (last
  section before `## Sources`).
- Format: `- YYYY-MM-DD — <summary> — <agent/person>`
- Every edit bumps `updated:` and appends a change log line. The only edits
  that do **not** bump the change log are pure backlink/`related` maintenance.

---

## Quick reference — the rhythm

```
raw/ → /ingest → wiki/      (user feeds Claude; Claude distills)
wiki/ → /query → answer     (Claude uses the wiki to answer)
wiki/ → /lint → cleanup     (Claude audits and proposes fixes)
code → /document-system → wiki/systems/   (Claude reads source and skills)
session → /save → decisions or gotchas    (Claude captures conversational knowledge)
wiki/ → /map → INDEX.md     (Claude regenerates the top-level index)
```
