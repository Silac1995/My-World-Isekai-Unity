---
type: gotcha
title: "Double Interact on E tap (Rule #33 violation)"
tags: [player-input, interactable, rule-33, multiplayer]
created: 2026-05-09
updated: 2026-05-09
sources:
  - "[../../docs/superpowers/specs/2026-05-09-shop-buy-panel-and-interact-deduplication-design.md](../../docs/superpowers/specs/2026-05-09-shop-buy-panel-and-interact-deduplication-design.md)"
  - "[../../docs/superpowers/plans/2026-05-09-shop-buy-panel-and-interact-deduplication.md](../../docs/superpowers/plans/2026-05-09-shop-buy-panel-and-interact-deduplication.md)"
  - "[../../docs/superpowers/audits/2026-05-09-interact-dedup-audit.md](../../docs/superpowers/audits/2026-05-09-interact-dedup-audit.md)"
related:
  - "[[character-interaction]]"
status: resolved
confidence: high
---

# Double Interact on E tap (Rule #33 violation)

## Summary
Two separate `Input.GetKeyUp(KeyCode.E)` reads each invoked `InteractableObject.Interact(Character)` per tap — one in `PlayerController.HandleEKeyUp`, one in `PlayerInteractionDetector.Update`. Every E tap fired the interactable twice. Storage panels tolerated it (re-Initialize was idempotent); cashier flow did not — `RequestStartBuyServerRpc` fired twice, risking double-debit.

## Symptom
Logs show TWO `[Furniture] ... utilise <name>.` lines per single E tap. For cashier, `CharacterAction_BuyFromShop` is enqueued twice; `OpenBuyPanelClientRpc` invocation count is two; `RequestStartBuyServerRpc` is rejected on the second attempt because the lock is already held.

## Root cause
`PlayerInteractionDetector` was an ad-hoc input reader for the E key in its `Update()` body — `Input.GetKeyDown`, `Input.GetKey`, `Input.GetKeyUp` all on `KeyCode.E`. `PlayerController.HandleEKeyUp` also read `Input.GetKeyUp(KeyCode.E)` (per Project Rule #33 — PlayerController is the canonical input owner) and called `nearest.Interact(_character)`. Both paths fired on the same release frame.

This violated **Project Rule #33**: *"All player input that controls the player character lives in `PlayerController.cs`. Do not scatter `Input.GetKey…` / `Input.GetMouseButton…` calls for player-character control across HUD scripts, UI managers, ad-hoc MonoBehaviours, or other character subsystems."*

## How to avoid
- Never call `Input.GetKey*` / `Input.GetMouseButton*` for player-character control outside `PlayerController.Update()`. Carve-out: UI widgets reading input that targets the UI itself (menu open, text fields).
- Detector / proximity / awareness scripts expose **data** (`CurrentTarget`, `IsTargetInRange`) and **helper APIs** (`TriggerTapInteract`, `TriggerHoldMenu`, `SetPromptHoldProgress`). They do not poll input.
- Code-review every new `MonoBehaviour.Update` for `Input.GetKey…` lines. Grep periodically.

## How to fix (if already hit)
1. Delete every `Input.GetKey*(KeyCode.X)` callsite outside `PlayerController` for the affected key.
2. Move the dispatch logic into `PlayerController.HandleE*` (or equivalent per-key handler).
3. If the detector held intermediate state for the input loop (hold timers, latches), delete those fields too — `PlayerController` owns the timer.
4. Re-test on Host↔Client, Client↔Client, and Host/Client↔NPC — exactly one effect per tap, on every peer.

Full procedure: see [.agent/skills/interactable-system/SKILL.md](../../.agent/skills/interactable-system/SKILL.md) "Player Input & Interaction Menus" section.

## Affected systems
- [[character-interaction]]

## Links
- Audit: `docs/superpowers/audits/2026-05-09-interact-dedup-audit.md`
- Spec: `docs/superpowers/specs/2026-05-09-shop-buy-panel-and-interact-deduplication-design.md`
- Plan: `docs/superpowers/plans/2026-05-09-shop-buy-panel-and-interact-deduplication.md`

## Sources
- Bug report: user logs, May 9 2026 — `[Furniture] ... utilise Crate.` appearing twice per tap.
- Resolved by commit chain `e56ce30c..4bce344a` on branch `claude/elastic-banzai-c5e88c`.
- Project Rule #33 (root `CLAUDE.md`).
