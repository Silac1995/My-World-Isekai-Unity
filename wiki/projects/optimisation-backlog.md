---
type: project
title: "Optimisation Backlog"
tags: [optimisation, performance, backlog, deferred-work]
created: 2026-04-25
updated: 2026-04-25
sources: []
related: ["[[storage-furniture]]"]
status: active
confidence: high
start_date: 2026-04-25
target_date: null
---

# Optimisation Backlog

## Summary
Catch-all tracker for performance / scalability / culling work that's been **deliberately deferred** to keep current features unblocked. Each entry names the system, the trade-off being held open, and what "good enough" looks like when we eventually pick it up. Anything that lives here is a known compromise — not a forgotten bug.

## Goals
- Keep optimisation TODOs out of the source code (where they rot) and out of conversation memory (where they vanish across sessions).
- Make it easy for a future agent or Kevin to see at a glance what shortcuts are in flight and decide whether to invest now.

## Non-goals
- Tracking general bugs (those go to GitHub issues).
- Tracking systemic refactors (those get their own project page).
- Pre-mature optimisation — entries here should be backed by an observed cost or a clear scaling concern.

## Current state
**Active deferrals:**

### 1. StorageVisualDisplay — per-player local distance/visibility culling
- **Where:** [Assets/Scripts/World/Furniture/StorageVisualDisplay.cs](../../Assets/Scripts/World/Furniture/StorageVisualDisplay.cs) (see TODO comment on the class docstring).
- **What was there:** a coroutine-based squared-distance check against `NetworkManager.Singleton.LocalClient.PlayerObject` that deactivated all displays when the local player was farther than `_activationDistance` (default 25 Unity units).
- **Why it was removed (2026-04-25):** the gating was a single-peer host-side decision that ran on every machine. On the host, distant rooms got their displays culled — fine. On clients, the *same* coroutine ran but resolved a different `LocalPlayerObject` and could end up flipping displays on/off out of phase with the host's storage state, leaving clients with empty shelves even when the storage was stocked.
- **What the replacement should look like:**
  - Run **per-peer**, on each peer's own copy of the `StorageVisualDisplay`. No server authority needed — pure local culling decision.
  - Inputs: this peer's local player transform (already resolvable via `NetworkManager.Singleton.LocalClient.PlayerObject` once that's reliable for clients), the storage's world position.
  - Decoupled from inventory sync — the inventory layer always carries the data; the visual layer culls independently.
  - Reasonable threshold: `~50 Unity units (≈7.6 m)` for a default. Builders can tune per-prefab.
- **Until then:** displays are always-on whenever the storage contains items. Acceptable cost: a few SpriteRenderers/MeshRenderers per shelf, and the per-`ItemSO` pool keeps allocations bounded. Becomes a real concern only when scenes have hundreds of populated shelves visible from camera.
- **Owner:** [[building-furniture-specialist]] for the visual layer; [[network-specialist]] if the local-player resolution turns out to need network-aware fallback.

## Milestones
- [ ] StorageVisualDisplay per-peer culling — no fixed date; pick up when shelf-count perf becomes measurable, OR when player-count testing shows the always-on cost hurts.

## Stakeholders
- [[kevin]] — decides when to invest.

## Links
- [[storage-furniture]]
- [[building-furniture-specialist]]

## Sources
- 2026-04-25 conversation with Kevin — original deferral.
- [Assets/Scripts/World/Furniture/StorageVisualDisplay.cs](../../Assets/Scripts/World/Furniture/StorageVisualDisplay.cs) — class-level TODO comment.
