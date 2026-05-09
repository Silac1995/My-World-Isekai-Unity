---
type: system
title: "Dialogue"
tags: [dialogue, conversation, scripted, tier-1]
created: 2026-04-18
updated: 2026-04-18
sources: []
related:
  - "[[character]]"
  - "[[social]]"
  - "[[visuals]]"
  - "[[player-ui]]"
  - "[[kevin]]"
status: stable
confidence: high
primary_agent: character-social-architect
secondary_agents:
  - character-system-specialist
owner_code_path: "Assets/Scripts/Dialogue/"
depends_on:
  - "[[character]]"
  - "[[social]]"
  - "[[network]]"
depended_on_by:
  - "[[player-ui]]"
---

# Dialogue

## Summary
Scripted, non-ambient conversations. `DialogueSO` ScriptableObjects hold ordered `DialogueLine`s addressed to specific participant indices. A `DialogueManager` on each player character starts a dialogue with a participant list and advances lines on player input (Space / Left Click), or — in fully-NPC conversations — auto-advances 1.5s after the speech bubble finishes typing. Dynamic NPC↔NPC banter is **not** handled here — that's `CharacterInteraction` + `DialogueSequence` under [[social]]. Speech bubbles themselves are rendered by `CharacterSpeech` (under [[visuals]]) via `ScriptedSpeech.SayScripted()`.

## Purpose
Author story moments, cutscenes, quest handoffs, and tutorial sequences as data (ScriptableObjects) rather than code. Let multiple player characters share the advancement gate so co-op sessions don't race ahead of each other.

## Responsibilities
- Storing scripted conversation data (`DialogueSO`, `DialogueLine`).
- Resolving placeholder tags (`[indexX].getName`) at runtime with actual character references.
- Advancing lines on player input or (fully-NPC mode) timer.
- Keeping `DialogueManager.IsInDialogue` true during a scripted run so other actions are blocked.
- Interfacing with `CharacterSpeech.SayScripted()` — bubbles persist until advance.
- Providing inspector testing: `_currentDialogue`, `_testParticipants`, "Trigger Serialized Dialogue" context menu.

**Non-responsibilities**:
- Does **not** handle dynamic NPC-to-NPC banter — see [[social]] `DialogueSequence`.
- Does **not** render speech bubbles — see [[visuals]] / `CharacterSpeech`.
- Does **not** own branching choice UI — current scope is linear lines (confirm).

## Key classes / files

| File | Role |
|------|------|
| [DialogueSO.cs](../../Assets/Scripts/Dialogue/DialogueSO.cs) | ScriptableObject container; ordered `DialogueLine`s. |
| [DialogueManager.cs](../../Assets/Scripts/Dialogue/DialogueManager.cs) | Per-player-character; runs dialogues, handles input/auto-advance, manages participant mapping. |
| `Assets/Scripts/Character/CharacterSpeech/` | `CharacterSpeech.SayScripted()` + `ScriptedSpeech` component on the bubble prefab. |
| `Assets/Scripts/UI/Dialogue/` | `UI_Dialogue*` — HUD for dialogue prompt + advance hint. |

## Public API / entry points

- `DialogueManager.StartDialogue(DialogueSO, List<Character> participants)`.
- `DialogueManager.IsInDialogue` — read by other systems to block actions.
- `CharacterSpeech.SayScripted(text)` — low-level speech-bubble display.
- Inspector testing: `_currentDialogue`, `_testParticipants`, "Trigger Serialized Dialogue" (context menu).

Placeholder syntax in `DialogueLine.lineText`:
- `[indexX].getName` → replaced with `participants[X-1].DisplayName` at runtime.
- (Any other tags used? Confirm in Open questions.)

## Data flow

Scripted dialogue:
```
Player / quest triggers DialogueManager.StartDialogue(SO, participants)
       │
       ▼
DialogueManager enters IsInDialogue = true
       │
       ▼
For each DialogueLine in SO.lines:
       │
       ├── Resolve placeholders against participants
       ├── speaker = participants[line.characterIndex - 1]
       ├── speaker.CharacterSpeech.SayScripted(resolvedText)
       │     │
       │     └── bubble persists (no auto-timeout)
       │
       ├── Advance gate:
       │     │
       │     ├── any player participants? ──► wait for Space / Left Click
       │     └── else                     ──► WaitForSeconds(1.5) after bubble finishes typing
       │
       ▼
IsInDialogue = false; fire OnDialogueEnded
```

Compare with dynamic NPC↔NPC (social):
```
CharacterInteraction.StartInteractionWith(other, InteractionTalk)
       │
       └── DialogueSequence coroutine — alternating Speaker/Listener, up to 6 exchanges
              (see social.md — distinct system, same speech-bubble substrate)
```

## Dependencies

### Upstream
- [[character]] — `DialogueManager` is a per-player component; consumes `Character.DisplayName`.
- [[social]] — shares speech substrate; `IsInDialogue` pauses the BT (same contract as `CharacterInteraction.IsInteracting`).
- [[network]] — for multiplayer, scripted dialogue state must sync advance input across players (any player's click advances — confirm).

### Downstream
- [[player-ui]] — renders the advance-prompt HUD, maybe a dialogue log.

## State & persistence

- Scripted dialogue is **not** persisted — active dialogue aborts on session boundary.
- Player-seen dialogue IDs **may** be persisted via a "dialogue history" component (not confirmed — Open question).
- No long-running state lives in `DialogueSO` — it's pure data.

## Known gotchas / edge cases

- **Multiple players + input**: current design auto-advances when **no** players are in the participant list. If at least one player is present, any player's input advances (or only a specific one?). **Needs verification.**
- **Placeholder indices are 1-based in text** (`[index1]`) but the `_testParticipants` list uses 0-based element indices (Element 0 = Index 1). Off-by-one is easy to hit.
- **`IsInDialogue` pauses BT** — the same rule as `CharacterInteraction.IsInteracting`. Forget to clear it and NPCs freeze forever.
- **Scripted vs dynamic boundary** — new contributors often confuse `DialogueSO` (scripted) with `CharacterInteraction.DialogueSequence` (dynamic). Different owners, different use cases.

## Open questions / TODO

- [ ] Multiplayer advance semantics: any-player-advances vs specific-participant-only. Confirm in code.
- [ ] Persistence of player-seen dialogues — quest gating needs this. Confirm existence.
- [ ] Branching / choice support — current template seems linear only. Is a choice system planned?
- [ ] `UI_Dialogue*` exact component list — enumerate in child sub-page.

## Child sub-pages (to be written in Batch 2)

- [[dialogue-data]] — `DialogueSO`, `DialogueLine`, placeholder tag resolution.
- [[dialogue-manager]] — advancement, multiplayer sync, testing tools.
- [[scripted-speech]] — `CharacterSpeech.SayScripted`, bubble persistence semantics.

## Change log
- 2026-04-18 — Initial documentation pass. — Claude / [[kevin]]

## Sources
- [.agent/skills/dialogue-system/SKILL.md](../../.agent/skills/dialogue-system/SKILL.md)
- [.claude/agents/character-social-architect.md](../../.claude/agents/character-social-architect.md)
- [DialogueManager.cs](../../Assets/Scripts/Dialogue/DialogueManager.cs)
- [DialogueSO.cs](../../Assets/Scripts/Dialogue/DialogueSO.cs)
- 2026-04-18 conversation with [[kevin]].
