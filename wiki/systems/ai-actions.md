---
type: system
title: "AI Actions"
tags: [ai, goap, actions, tier-2, stub]
created: 2026-04-19
updated: 2026-05-15
sources: []
related: ["[[ai]]", "[[ai-goap]]", "[[jobs-and-logistics]]", "[[character-needs]]", "[[shops]]", "[[interactable-proximity-distance-anti-pattern]]", "[[kevin]]"]
status: stable
confidence: medium
primary_agent: npc-ai-specialist
owner_code_path: "Assets/Scripts/AI/Actions/"
depends_on: ["[[ai-goap]]"]
depended_on_by: ["[[ai-goap]]"]
---

# AI Actions

## Summary
The GOAP action library. Concrete `GoapAction` subclasses that do things — move, socialize, place order, load/unload transport, harvest, fight, sleep, eat. Each defines preconditions, effects, cost, and frame-loop execution. SKILL lists 19 total.

## Examples
- `GoapAction_MoveTo`
- `GoapAction_Socialize`
- `GoapAction_PlaceOrder`
- `GoapAction_LoadTransport`, `GoapAction_UnloadTransport`
- `GoapAction_Harvest`, `GoapAction_Deposit`
- **Shop-buy chains for character needs (added 2026-05-15):**
  - `GoapAction_BuyFood` → `GoapAction_EatCarriedFood` (the [[character-needs|NeedHunger]] shop chain).
  - `GoapAction_BuyClothing` → `GoapAction_EquipCarriedClothing` (the [[character-needs|NeedToWearClothing]] shop chain).
  - Both reuse `CharacterAction_BuyFromShop(BuyMode.NPC)` for the buy commit, then a need-specific terminator action for consume / equip. Movement gate follows the canonical proximity pattern documented in [[interactable-proximity-distance-anti-pattern]] (CLAUDE.md rule #36): `InteractableObject.IsCharacterInInteractionZone` containment + softlock guard + path-loss recovery — never raw `Vector3.Distance` against `GetInteractionPosition`.

## Folder
- `Assets/Scripts/AI/Actions/`.

## Open questions
- [ ] Enumerate the full action set (`Assets/Scripts/AI/GOAP/Actions/`). Currently the `.claude/agents/npc-ai-specialist.md` GOAP-action table is the closest reference. Tracked in [[TODO-skills]].

## Change log
- 2026-04-19 — Stub. — Claude / [[kevin]]
- 2026-05-15 — Added the shop-buy chain examples (`GoapAction_BuyFood`, `GoapAction_BuyClothing`, `GoapAction_EatCarriedFood`, `GoapAction_EquipCarriedClothing`) and cross-linked to [[character-needs]] + [[shops]] + the proximity gotcha that landed in the same week. — claude

## Sources
- [.agent/skills/goap/SKILL.md](../../.agent/skills/goap/SKILL.md)
- [[ai]] and [[ai-goap]] parents.
