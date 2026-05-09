---
type: system
title: "Character Interaction"
tags: [character, social, dialogue, tier-2]
created: 2026-04-18
updated: 2026-05-02
sources: []
related:
  - "[[character]]"
  - "[[social]]"
  - "[[dialogue]]"
  - "[[kevin]]"
status: stable
confidence: high
primary_agent: character-social-architect
owner_code_path: "Assets/Scripts/Character/CharacterInteraction/"
depends_on:
  - "[[character]]"
  - "[[social]]"
depended_on_by:
  - "[[social]]"
  - "[[party]]"
  - "[[jobs-and-logistics]]"
  - "[[shops]]"
---

# Character Interaction

## Summary
The lifecycle wrapper around any dynamic, turn-taking exchange between two characters. `StartInteractionWith(other, action)` checks `IsFree`, freezes the target, sets mutual look targets, registers acquaintance in [[character-relation]], and hands control to a `DialogueSequence` coroutine. `EndInteraction()` unfreezes participants, clears look targets, and cleans up. All concrete actions (`InteractionTalk`, `InteractionBuyItem`, `InteractionPlaceOrder`, `InteractionInvitation`, etc.) implement `ICharacterInteractionAction`.

## Purpose
Centralize the "two characters are talking/exchanging" state machine so all social actions (talk, buy, place order, invite) share freeze/unfreeze, look-target, BT-pause, and relation-registration behaviour. Keep the dialogue coroutine decoupled from scripted `DialogueSO` story dialogue (see [[dialogue]]).

## Responsibilities
- Start/end lifecycle with safety checks (`IsFree`).
- Freezing participants and setting look targets.
- Running `DialogueSequence` coroutine — alternating Speaker/Listener, up to `MAX_EXCHANGES` (default 6), pausing on speech-bubble completion, injecting 1.0–2.5s natural-response delays.
- Dispatching to `ICharacterInteractionAction.Execute`.
- Maintaining `IsInteracting` flag — consumed by [[ai]] to pause the BT.
- Registering the acquaintance (`SetAsMet`) on first interaction.

**Non-responsibilities**:
- Does **not** own scripted story dialogue — [[dialogue]] (`DialogueSO`, `DialogueManager`) is separate.
- Does **not** own relationship math — [[character-relation]].
- Does **not** own speech bubble rendering — `CharacterSpeech`.

## Key classes / files

- `Assets/Scripts/Character/CharacterInteraction/CharacterInteraction.cs` — the component + coroutine.
- `ICharacterInteractionAction` (interface).
- Concrete actions (in same folder):
  - `InteractionTalk` — generic dynamic chat.
  - `InteractionBuyItem` — consumed by [[shops]].
  - `InteractionPlaceOrder` — consumed by [[jobs-and-logistics]].
  - `InteractionInvitation` — template for [[party]] / trade / lesson invites.
  - `InteractionInsult`, `InteractionGiveGift`, `InteractionProposeMarriage` (examples listed in SKILL — confirm presence).

## Public API

- `character.CharacterInteraction.StartInteractionWith(other, action)`.
- `character.CharacterInteraction.EndInteraction()`.
- `character.CharacterInteraction.IsInteracting` — read-only; pauses BT (see [[ai]]).
- Each action class: implement `Execute(initiator, target)`, `OnCancel()`.

## Data flow

```
A initiates (player click, GOAP, invitation accept, etc.)
       │
       ▼
CharacterInteraction.StartInteractionWith(B, InteractionTalk)
       │
       ├── Both IsFree?                       ──► abort if either busy
       ├── B.Freeze()                          — stop movement/BT
       ├── B.Visual.SetLookTarget(A)
       ├── A.CharacterRelation.SetAsMet(B)    (bilateral)
       └── Push MoveToInteractionBehaviour on A (walk to B)
       │
       ▼
Start DialogueSequence coroutine
       │
       ├── up to 6 exchanges, alternating Speaker/Listener
       ├── WaitUntil !CharacterSpeech.IsSpeaking (bubble finishes)
       ├── WaitForSeconds(1.0..2.5) — natural pause
       │
       ▼
action.Execute(A, B) — actually performs the side effect (buy, place order, etc.)
       │
       ▼
EndInteraction
       │
       ├── B.Unfreeze
       ├── B.Visual.ClearLookTarget
       ├── Cleanup MoveToInteractionBehaviour
       └── UpdateRelation deltas (action-defined)
```

## Dependencies

### Upstream
- [[character]] — subsystem component.
- [[social]] — parent system (semantics).
- `CharacterSpeech` — for bubble timing (see [[visuals]] / [[dialogue]]).

### Downstream
- [[party]] — invitations.
- [[shops]] — `InteractionBuyItem`.
- [[jobs-and-logistics]] — `InteractionPlaceOrder`.
- [[ai]] — BT paused during interaction.

## State & persistence

- No persistent state — interactions cancel on session boundary.
- `IsInteracting` is transient; resets on save/load.

## Known gotchas

- **`EndInteraction` must run on cancel** — if the GOAP/input cancels mid-sequence, call `EndInteraction` explicitly. Otherwise both participants remain frozen.
- **`IsFree` guard is load-bearing** — skipping it allows interactions to start during combat/dialogue/crafting, deadlocking characters.
- **`SetLookTarget` always points at root Character** — never at the child `CharacterInteractable` transform (see [[combat]] gotcha).
- **Player-in-dialogue pauses auto-advance** — dynamic exchanges with at least one player wait on input; pure NPC-vs-NPC auto-advances after bubble + pause.
- **Dialogue beats are scaled by `GameSpeedController`.** All four `DialogueSequence` waits (invitation poll, post-invitation read pause, inter-exchange random delay, end-of-conversation linger) use `WaitForSeconds`, not `WaitForSecondsRealtime`. At 5× speed NPC banter is 5× snappier; on pause the sequence freezes. The same convention applies to `CharacterSpeech` typing + expiration. The player-turn timer (`PLAYER_WAIT_DELAY = 8s`) is also scaled — at high speeds the human player has correspondingly less real time to pick an action; if that becomes punishing, switch *only* the player-turn timer to unscaled. Full table: [.agent/skills/interaction-exchanges/SKILL.md § Time Scaling](../../.agent/skills/interaction-exchanges/SKILL.md).

## Open questions

- [ ] Enumerate the full list of `ICharacterInteractionAction` implementations in the 15-file folder.
- [ ] Can an interaction be interrupted by combat? Confirm behavior.

## Change log
- 2026-05-02 — Switched all four `DialogueSequence` `WaitForSecondsRealtime` calls to `WaitForSeconds` so NPC dialogue pacing scales with `GameSpeedController` (matches the corresponding fix in `CharacterSpeech` for typing speed + bubble lifetime). Added a "Time Scaling" subsection to the SKILL with the per-wait reasoning. — Claude / [[kevin]]
- 2026-04-18 — Initial pass. — Claude / [[kevin]]

## Sources
- [.agent/skills/social_system/SKILL.md](../../.agent/skills/social_system/SKILL.md) §1.
- [[social]] parent.
- `Assets/Scripts/Character/CharacterInteraction/` (15 files).
