# My World Isekai — Unity Project

Unity game project. The code lives under `Assets/`. Everything else is tooling, docs, and the LLM wiki described below.

---

## LLM Wiki

This repo contains a persistent, self-evolving knowledge base built on Andrej Karpathy's "LLM Wiki" pattern. Two top-level directories are dedicated to it:

```
raw/   ← sources owned by the user (transcripts, articles, design docs, notes).
        Claude never modifies this.
wiki/  ← structured, cross-linked pages maintained by Claude.
        The user never writes directly here — everything goes through a command.
```

Source code under `Assets/` is the third leg — it is also "raw" in the sense that wiki operations never edit it, but `/document-system` may read it.

### Rules in one paragraph

- `raw/` belongs to the user. `wiki/` belongs to Claude. The user never writes in `wiki/`; Claude never modifies `raw/`.
- Every wiki page has YAML frontmatter, wikilinks to related pages, and a `Sources` section.
- Procedures live in `.agent/skills/*/SKILL.md`. Architecture lives in `wiki/systems/`. Each system page in the wiki links to its matching SKILL.md — no procedural duplication.
- If Claude is unsure about scope or context, it stops and asks numbered questions instead of guessing.

The full schema — entity types, required fields, all four operations, naming conventions — lives in [wiki/CLAUDE.md](wiki/CLAUDE.md).

### Commands

All commands live in `.claude/commands/`.

| Command | Purpose |
|---|---|
| `/ingest <path-under-raw>` | Distill a raw file into 5–15 wiki pages. |
| `/query <question>` | Answer a question from the wiki; fall back to raw or code only if needed. |
| `/lint` | Audit the wiki for duplicates, broken links, orphans, stale pages. |
| `/save decision <title>` / `/save gotcha <title>` | Capture a decision or pitfall from the current conversation. |
| `/map` | Regenerate `wiki/INDEX.md`. |
| `/document-system <name-or-file>` | Document one system from code + SKILL.md. |
| `/document-all` | Scan codebase, propose a system inventory, document all (with approval gate). |

### Day-to-day flow

1. Drop a source into `raw/inbox/` (or a typed subfolder like `raw/articles/`).
2. Run `/ingest raw/inbox/that-file.md`.
3. Claude proposes 5–15 impacted pages, shows a diff, applies it, and summarizes.
4. Ask questions later with `/query <anything>` — Claude consults the wiki first.

### Systems documentation

The `wiki/systems/` tree is the architectural encyclopedia of the game: purpose, responsibilities, data flow, dependencies, state, gotchas. It was bootstrapped by a full scan of the codebase and is refreshed over time via `/document-system`. See [wiki/systems/README.md](wiki/systems/README.md) for the index grouped by category.

---

## Running the game

_(project-specific build/run instructions live in Unity itself and in `.agent/skills/unity-initial-setup/SKILL.md`)_
