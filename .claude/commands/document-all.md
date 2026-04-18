---
description: Scan the codebase, propose a system inventory, then document each with /document-system.
argument-hint: (no arguments — asks for confirmation before writing)
---

# /document-all — full bootstrap / refresh of wiki/systems/

Produce (or refresh) the complete system inventory.

## Step 1 — scan

1. Read `.agent/skills/` and list every `<name>/SKILL.md` — that's the baseline.
2. Scan `Assets/Scripts/` folder structure: top-level folders are candidate systems.
3. Cross-reference with `.claude/agents/*.md` to map which specialist owns what.
4. Read any `raw/design-docs/*` for systems not yet in code.

## Step 2 — reconcile

Produce a **single merged list** of systems with:
- Name (kebab-case).
- One-line purpose guess.
- Confidence: `high | medium | low`.
- Source of detection: `skill | code | agent | design-doc`.
- Suggested tier: `1 (full) | 2 (stub+diagram) | 3 (aggregated engine-plumbing)`.
- Existing wiki page? `yes | no`.
- Matching agent.

## Step 3 — ASK THE USER

**Do not write any wiki page yet.** Present the full list to the user and ask:
1. Any systems to add or remove?
2. Any tier re-assignments?
3. Any systems that are legacy / deprecated and should be skipped?
4. Any two that should be merged into one page?

Wait for explicit confirmation before proceeding.

## Step 4 — iterate

After the user confirms:
- For each **tier 1** system, run the full `/document-system` procedure.
- For each **tier 2** system, produce a stub page with:
  - Summary, Purpose, Key classes table, Data flow (1 paragraph), Dependencies, and a TODO section listing what's missing.
- For all **tier 3** systems, produce a **single aggregated page** at
  `wiki/systems/engine-plumbing.md` with 2–3 lines per subsystem.

After each tier, show a checkpoint: what was created, what's next.

## Step 5 — finalize

1. Regenerate `wiki/systems/README.md` — index grouped by category (gameplay / engine / tooling / content / infrastructure).
2. Run the `/map` operation to regenerate `wiki/INDEX.md`.
3. Output a final summary:
   - N pages created.
   - M systems at each tier.
   - Low-confidence systems that need user input.
   - Missing agents / missing SKILL.md files that the user might want to create.

## Rules

- Do NOT skip step 3. The user must confirm the inventory before any writes happen.
- If the total write would touch > 20 files, batch into 3–5 file chunks with approval gates between them.
- Every system page must link to its matching `.agent/skills/*/SKILL.md` in Sources (if one exists).
- Every system page frontmatter must have `primary_agent` filled — either the matching specialist agent filename, or `null` with a TODO note.
