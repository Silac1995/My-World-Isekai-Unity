---
name: feedback_always_opus
description: All specialized agents must use model opus — the project systems are too complex for lighter models
type: feedback
---

Always use `model: opus` for specialized agents in this project.

**Why:** The user explicitly stated that the game's systems are too complex for lighter models like Sonnet. Opus provides the deeper reasoning needed for interconnected systems (world sim, netcode, character hierarchy, etc.).

**How to apply:** When creating any new `.claude/agents/` specialist, always set `model: opus` in the frontmatter.
