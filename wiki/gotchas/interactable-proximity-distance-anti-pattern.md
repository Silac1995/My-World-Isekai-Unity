---
type: gotcha
title: "NPC ↔ interactable proximity must use IsCharacterInInteractionZone, not Vector3.Distance"
tags: [goap, ai, interactable, navmesh, proximity, anti-pattern, claude-md-rule-36]
created: 2026-05-15
updated: 2026-05-15
sources:
  - "[Assets/Scripts/AI/GOAP/Actions/GoapAction_BuyFood.cs](../../Assets/Scripts/AI/GOAP/Actions/GoapAction_BuyFood.cs)"
  - "[Assets/Scripts/AI/GOAP/Actions/GoapAction_GoShopping.cs](../../Assets/Scripts/AI/GOAP/Actions/GoapAction_GoShopping.cs)"
  - "[Assets/Scripts/AI/GOAP/Actions/GoapAction_FetchSeed.cs](../../Assets/Scripts/AI/GOAP/Actions/GoapAction_FetchSeed.cs)"
  - "[Assets/Scripts/Character/CharacterMovement/CharacterMovement.cs](../../Assets/Scripts/Character/CharacterMovement/CharacterMovement.cs)"
  - "[CLAUDE.md](../../CLAUDE.md) rule #36"
  - "[.agent/skills/interactable-system/SKILL.md](../../.agent/skills/interactable-system/SKILL.md)"
  - "[.agent/skills/goap/SKILL.md](../../.agent/skills/goap/SKILL.md) rule #7"
  - "Commit `36b6afec` — fix(npc-ai): use InteractionZone containment for cashier proximity"
  - "2026-05-15 conversation with [[kevin]] where a hungry NPC selected a cashier but stood frozen in front of it"
related:
  - "[[character-needs]]"
  - "[[shops]]"
  - "[[ai-goap]]"
  - "[[ai-actions]]"
  - "[[character-interaction]]"
  - "[[ai-navmesh]]"
  - "[[chain-action-isvalid-pre-filter]]"
  - "[[worldstate-predicate-action-isvalid-divergence]]"
  - "[[host-only-state-blindspot]]"
status: mitigated
confidence: high
---

# NPC ↔ interactable proximity must use IsCharacterInInteractionZone, not Vector3.Distance

## Summary
Any `GoapAction.Execute`, `CharacterAction.OnStart/OnTick`, `BTAction` movement gate, or `IPlayerCommand` that walks a character to a specific interactable before acting on it MUST decide "am I close enough?" with `InteractableObject.IsCharacterInInteractionZone(worker)` (plus a softlock guard), **not** with a raw `Vector3.Distance(worker, target.GetInteractionPosition(...)) < N` threshold. The naive distance check looks reasonable but is load-bearing-wrong: `CharacterMovement.SetDestination` runs `NavMesh.SamplePosition(target, 5m, NavMesh.AllAreas)` internally, the agent lands at the **sampled** NavMesh point (typically a few metres off the interaction-point hint because the interactable's mesh blocks the NavMesh under it), and the gate keeps measuring against the original off-mesh target — so the distance never falls under the threshold, `_isMoving` stays `true`, `SetDestination` is never re-issued, and the NPC stands frozen in front of the object forever. Codified as [CLAUDE.md rule #36](../../CLAUDE.md), [interactable-system/SKILL.md "Movement gate for GOAP / BT actions"](../../.agent/skills/interactable-system/SKILL.md), and [goap/SKILL.md rule #7](../../.agent/skills/goap/SKILL.md).

## Symptom
- NPC's dev inspector shows the plan loaded ("Life GOAP: Buy Food" / "Buy Item" / "Use Crafting Station"), the BT branch is GOAP, but `Action: Idle` and `Agent: RUNNING | No Path`.
- NPC physically arrives within a couple of metres of the target but never enqueues the consume / interact `CharacterAction`.
- Repeatedly observed as "Alicia walked to the cashier, then just stood there." Same shape will appear for any interactable whose interaction-point sits inside a solid mesh (cashier counter, chest interior, crafting station body, bed mattress, door panel, anvil head).
- Live `NavMesh.SamplePosition` probe against the recorded dest comes back **MISS at radius 0.5–2 m, HIT at 4 m with a 3.3 m offset**. `NavMesh.CalculatePath` from the worker's actual position to the raw dest returns `status=PathInvalid`; the same call from the worker to the *sampled* landing point returns `status=PathComplete`. That delta IS the bug.

## Root cause
1. `CharacterMovement.SetDestination(target, …)` (see `Assets/Scripts/Character/CharacterMovement/CharacterMovement.cs` ~line 299) calls `NavMesh.SamplePosition(target, out hit, 5f, NavMesh.AllAreas)` and feeds `hit.position` to `_agent.SetDestination`. The actual walked destination is the NavMesh-projected point, not the requested one.
2. The interactable's `GetInteractionPosition(...)` typically returns the bounds centre of its `InteractionZone` collider (see `Furniture.GetInteractionPosition` line ~112) — which is often inside the interactable's mesh, off any baked NavMesh.
3. A naive `Vector3.Distance(worker.transform.position, dest) > 1.5f` gate compares the worker (now standing at the sampled landing) to the original off-mesh `dest`. The result is the projection offset itself — perpetually larger than the threshold, regardless of how long the NPC stands there.
4. The gate's `if (!_isMoving) { SetDestination; _isMoving = true; }` then never re-fires (`_isMoving` is sticky), and the planner doesn't replan because `_currentAction.IsValid` still returns true.

## Fix / canonical pattern
Replace the distance gate with the [[character-interaction]]-canonical `IsCharacterInInteractionZone` containment test, plus a softlock guard for paths whose NavMesh landing fell just outside the zone. Verbatim from `GoapAction_FetchSeed`, `GoapAction_ReturnToolToStorage`, `GoapAction_FetchToolFromStorage`, `GoapAction_BuyFood`, `GoapAction_GoShopping`, `GoapAction_GatherStorageItems`, `GoapAction_TakeFromSourceFurniture`:

```csharp
var interactable = target.GetComponent<InteractableObject>();
bool inZone;
if (interactable != null && interactable.InteractionZone != null)
{
    inZone = interactable.IsCharacterInInteractionZone(worker);
    if (!inZone)
    {
        // Softlock guard — path target landed just outside the zone.
        bool arrived = !movement.HasPath
            || movement.RemainingDistance <= movement.StoppingDistance + 0.5f;
        if (arrived)
        {
            Vector3 ip = target.GetInteractionPosition(worker.transform.position);
            Vector3 wp = worker.transform.position;
            if (Vector3.Distance(new Vector3(wp.x, 0f, wp.z),
                                 new Vector3(ip.x, 0f, ip.z)) <= 2f) inZone = true;
        }
    }
}
else
{
    // Legacy fallback for interactables without an authored zone.
    Vector3 ip = target.GetInteractionPosition(worker.transform.position);
    Vector3 wp = worker.transform.position;
    inZone = Vector3.Distance(new Vector3(wp.x, 0f, wp.z),
                              new Vector3(ip.x, 0f, ip.z)) <= 1.5f;
}

if (!inZone)
{
    if (!_isMoving)
    {
        movement.SetDestination(target.GetInteractionPosition(worker.transform.position));
        _isMoving = true;
    }
    return;
}
// arrived → enqueue CharacterAction / fire interaction
```

## Designer-side companion rule — author InteractionZone colliders generously
The `InteractionZone` collider on every new `InteractableObject` must be sized **at least a few times the NavMeshAgent's stopping distance**, ideally a ring around the body of the interactable rather than the body itself. The NavMesh-sampled landing point (which can be up to 5 m off the interaction-point hint) needs to land **inside** the zone for the containment test to succeed without falling through to the softlock guard. If a real-world workflow requires "1.5 m grace radius inside the action code" to make NPCs interact, the **zone is too small** — enlarge the zone on the prefab, never widen the action's distance check. Same physical zone is read by the server-side anti-cheat re-check in `CharacterActions.ExecuteAction`, so the NPC-side gate and the server-side gate stay symmetric (see [[host-only-state-blindspot]] question 6).

## How to avoid
Before pressing Save on any new NPC↔interactable behaviour, walk this 4-step checklist (mirrored from CLAUDE.md rule #36):

1. Does the target expose `InteractableObject.InteractionZone`? If yes, gate on `IsCharacterInInteractionZone`. If no, add a zone — don't paper over with `Vector3.Distance`.
2. Is the softlock guard for "path landed just outside the zone" present? Mirror the `HasPath` / `RemainingDistance` block.
3. Does the movement gate agree with the server-side anti-cheat re-check in `CharacterActions.ExecuteAction`? Both call the same `IsCharacterInInteractionZone` — if they diverge, the action queues then gets rejected immediately.
4. If a player can also reach this action (rule #33), the same zone gates the player `Interact()` call. Keep them symmetric.

## Affected systems
- [[ai-goap]] — every GOAP action that walks to an interactable.
- [[ai-actions]] — every `CharacterAction` whose `OnStart` re-validates proximity (e.g. `CharacterAction_BuyFromShop`, `CharacterAction_OccupyFurniture`, `CharacterCraftAction`, `CharacterPickUpItem`, `CharacterDoorTraversalAction`).
- [[character-interaction]] — owns the `_interactionZone` collider on the character side; the inverse-direction proximity-check API.
- [[character-needs]] — `NeedHunger` registers `GoapAction_BuyFood`, which is the first action that hit this bug in practice.
- [[shops]] — `Cashier` / `CashierInteractable` is the first interactable whose authored zone position routinely tripped the latent gate.
- [[ai-navmesh]] — root cause traces back to `NavMesh.SamplePosition`'s landing-offset behaviour over solid interactable meshes.

## Links
- [[chain-action-isvalid-pre-filter]] — sibling GOAP discipline pitfall on the `IsValid` side.
- [[worldstate-predicate-action-isvalid-divergence]] — sibling GOAP discipline pitfall on the worldState side.
- [[host-only-state-blindspot]] — multiplayer audit checklist that catches the symmetric server-gate question (#6).

## Sources
- [CLAUDE.md](../../CLAUDE.md) rule #36 — project-level rule, canonical code block, designer checklist.
- [.agent/skills/interactable-system/SKILL.md](../../.agent/skills/interactable-system/SKILL.md) — Core Rule #1 + "Movement gate for GOAP / BT actions" subsection.
- [.agent/skills/goap/SKILL.md](../../.agent/skills/goap/SKILL.md) rule #7 — Strict Architectural Rules, Interaction Distance entry.
- [Assets/Scripts/AI/GOAP/Actions/GoapAction_BuyFood.cs](../../Assets/Scripts/AI/GOAP/Actions/GoapAction_BuyFood.cs) — post-fix reference implementation.
- [Assets/Scripts/AI/GOAP/Actions/GoapAction_GoShopping.cs](../../Assets/Scripts/AI/GOAP/Actions/GoapAction_GoShopping.cs) — post-fix reference implementation.
- [Assets/Scripts/AI/GOAP/Actions/GoapAction_FetchSeed.cs](../../Assets/Scripts/AI/GOAP/Actions/GoapAction_FetchSeed.cs) — earliest canonical implementation.
- [Assets/Scripts/Character/CharacterMovement/CharacterMovement.cs](../../Assets/Scripts/Character/CharacterMovement/CharacterMovement.cs) — `SetDestination` with the internal `NavMesh.SamplePosition` projection that creates the offset.
- Commit `36b6afec` — fix(npc-ai): use InteractionZone containment for cashier proximity, drop fragile Vector3.Distance gate.
- 2026-05-15 conversation with [[kevin]] where the symptom was reported, MCP-probed live, and fixed.
