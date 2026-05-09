---
type: system
title: "Social"
tags: [social, relationships, dialogue, tier-1]
created: 2026-04-18
updated: 2026-04-18
sources: []
related:
  - "[[character]]"
  - "[[dialogue]]"
  - "[[party]]"
  - "[[ai]]"
  - "[[kevin]]"
status: stable
confidence: medium
primary_agent: character-social-architect
secondary_agents:
  - character-system-specialist
owner_code_path: "Assets/Scripts/Character/CharacterInteraction/"
depends_on:
  - "[[character]]"
  - "[[ai]]"
depended_on_by:
  - "[[party]]"
  - "[[dialogue]]"
  - "[[world]]"
---

# Social

## Summary
Two interconnected pillars. **The Present** — `CharacterInteraction` — ticks a `DialogueSequence` coroutine between two characters, alternating Speaker/Listener roles for up to 6 exchanges, waiting on speech-bubble completion for pacing. **The Past/Future** — `CharacterRelation` — stores bilateral relationships with per-character opinion values; updates are filtered through the character's `CharacterProfile` personality compatibility so the same "+10 friendly gesture" produces different deltas for different witnesses. Generic dynamic exchanges and scripted `DialogueSO` conversations share the Speaker-Listener vocabulary but live in separate systems — see [[dialogue]].

## Purpose
Give NPCs a believable inner social world that players can enter and perturb. Every interaction has **memory** — greeting an NPC whose friend you just insulted has a different opinion consequence. Personality compatibility makes reputation feel organic: charming the charismatic is easy; charming the grumpy backfires.

## Responsibilities
- Running symmetric turn-taking dialogue between two characters (`CharacterInteraction.DialogueSequence`).
- Freezing and un-freezing participants, pinning look-targets (`SetLookTarget`), holding positions during exchanges (`MoveToInteractionBehaviour`).
- Starting interactions safely — both participants must be free (`IsFree()`), both must not already be in dialogue.
- Maintaining `CharacterRelation` — bilateral (A adds B → B adds A instantly), compatibility-filtered deltas.
- Handling invitations (`CharacterInvitation`) — the template-method pipeline that gates party invites, dialogue requests, and other initiated exchanges.
- Mentorship — teaching skills or abilities to another character (`CharacterMentorship`).
- Social GOAP actions — `GoapAction_Socialize` drives NPC-to-NPC spontaneous talk.

**Non-responsibilities**:
- Does **not** own scripted story dialogue — see [[dialogue]] (`DialogueSO`, `DialogueManager`).
- Does **not** own party membership — see [[party]].
- Does **not** own speech bubble rendering — handled by `CharacterSpeech` under [[visuals]]/[[dialogue]].

## Key classes / files

### The present — interaction
| File | Role |
|------|------|
| `Assets/Scripts/Character/CharacterInteraction/CharacterInteraction.cs` | Host of the dialogue sequence and interaction lifecycle. |
| `Assets/Scripts/Character/CharacterInteraction/` (15 files) | `ICharacterInteractionAction` implementations: `InteractionTalk`, `InteractionBuyItem`, `InteractionPlaceOrder`, `InteractionInvitation`, etc. |
| [CharacterInteractionDetector.cs](../../Assets/Scripts/Character/CharacterInteractionDetector.cs) | Proximity + line-of-sight gating; emits candidates to player HUD / NPC AI. |

### Invitations
| File | Role |
|------|------|
| `Assets/Scripts/Character/CharacterInvitation/` | Template method: `CharacterInvitation` (base), typed subclasses (party invite, trade invite, etc.). |
| UI_InvitationPrompt | `Assets/Scripts/UI/UI_InvitationPrompt.cs` — player-side accept/decline HUD. |

### The memory — relation
| File | Role |
|------|------|
| `Assets/Scripts/Character/CharacterRelation/CharacterRelation.cs` | Per-character list of `Relationship` entries. `SetAsMet`, `UpdateRelation(other, delta)` with compatibility filter. |
| `Assets/Scripts/Character/CharacterRelation/Relationship.cs` | Bilateral bond between two characters; opinion value, last-updated timestamp. |
| `CharacterProfile` (personality) | Feeds `GetCompatibilityWith(other)` — multiplies/mitigates/amplifies deltas. |

### Mentorship
| File | Role |
|------|------|
| `Assets/Scripts/Character/CharacterMentorship/` (conceptual — location per SKILL) | `CharacterMentorship.ReceiveLessonTick`, teaching zone, XP/ability transfer. |

### AI hooks
| File | Role |
|------|------|
| `Assets/Scripts/AI/Actions/GoapAction_Socialize.cs` (conceptual) | NPC-to-NPC spontaneous exchanges. |
| `BTCond_WantsToSocialize`, `BTAction_Socialize` | BT slot 8 social fallback. |

## Public API / entry points

Interaction lifecycle:
- `CharacterInteraction.StartInteractionWith(other, action)` — checks `IsFree`, freezes target, sets look targets, registers in `CharacterRelation`.
- `CharacterInteraction.EndInteraction()` — frees participants, clears look, cleans behaviours.
- `CharacterInteraction.IsInteracting` — used by the BT to pause ticking.

Adding a new action:
- Implement `ICharacterInteractionAction` — `Execute(initiator, target)`, `OnCancel()`, relationship deltas.
- Register with the interaction detector / HUD action menu as needed.

Relation:
- `CharacterRelation.AddRelationship(other)` / `SetAsMet(other)` — bilateral.
- `CharacterRelation.UpdateRelation(other, delta)` — compatibility-filtered.
- `CharacterRelation.GetFriendCount()` — consumed by [[world]] community founding gate.

Invitation:
- `CharacterInvitation.Offer(other, payload)` — base flow.
- Subclass the template for each invite type.

## Data flow

Interaction:
```
A initiates InteractionTalk with B
       │
       ├── CharacterInteraction.StartInteractionWith(B, InteractionTalk)
       │       │
       │       ├── Security: both IsFree?
       │       ├── Target freeze; SetLookTarget(A)
       │       ├── CharacterRelation.SetAsMet(B)  (bilateral)
       │       └── MoveToInteractionBehaviour (A walks to B)
       │
       ▼
DialogueSequence coroutine (up to 6 exchanges)
       │
       ├── Roles flip each exchange
       ├── WaitUntil CharacterSpeech.IsSpeaking == false
       ├── WaitForSeconds(1.0..2.5) between turns
       │
       ▼
EndInteraction
       │
       ├── Unfreeze; clear look
       ├── CharacterRelation.UpdateRelation(other, delta) — compatibility-filtered
       └── Cleanup MoveToInteractionBehaviour
```

Compatibility filter:
```
UpdateRelation(A → B, +10)
       │
       ▼
personalityFilter = B.CharacterProfile.GetCompatibilityWith(A)
       │
       ├── Compatible:   +10 × 1.5 = +15  (or -10 × 0.5 = -5 on conflict)
       └── Incompatible: +10 × 0.5 = +5   (or -10 × 1.5 = -15 on conflict)
```

## Dependencies

### Upstream
- [[character]] — subsystems (`CharacterInteraction`, `CharacterRelation`, `CharacterInvitation`, `CharacterMentorship`) live on the character.
- [[ai]] — `GoapAction_Socialize`, BT social slot.

### Downstream
- [[party]] — invitations route through `CharacterInvitation`; party gatherings use `CharacterInteraction`.
- [[dialogue]] — scripted sequences borrow the Speaker-Listener vocabulary; `ScriptedSpeech` and `DialogueManager` coordinate with `CharacterSpeech`.
- [[world]] — community founding reads `CharacterRelation.GetFriendCount() >= 4`.
- [[jobs-and-logistics]] — order placement and shop buying use `ICharacterInteractionAction` subclasses (`InteractionPlaceOrder`, `InteractionBuyItem`).

## State & persistence

- `CharacterRelation` lives on the character and saves via `ICharacterSaveData<T>` — persists across sessions.
- `Relationship` entries serialize as `(otherCharacterId, opinion, lastUpdated)`.
- In-progress dialogue is **not** persisted — if a player leaves mid-conversation, the coroutine is cancelled and `EndInteraction` runs.

## Known gotchas / edge cases

- **Both participants must be free** — if either returns false on `IsFree`, the interaction is rejected.
- **Deadlock after sudden interruption** — if a GOAP action or input event is cancelled mid-`DialogueSequence`, `EndInteraction()` **must** still be called. Any crash or early return inside the coroutine leaves both participants frozen.
- **`SetAsMet` is bilateral** — `AddRelationship` on A must add the symmetric entry on B. Breaking this causes one-way memory (weird NPC behaviour).
- **Compatibility filter confuses "unfair" feedback** — players seeing unexpected deltas almost always hit `CharacterProfile.GetCompatibilityWith()`. First place to look when a relation value seems wrong.
- **`character-community` vs `world-community` split** — `CharacterCommunity.cs` is the **character-side adapter** (founding gate, leadership flags). The underlying entity system lives under [[world]] (`Community`, `CommunityLevel`, `CommunityManager`). See Open questions.

## Open questions / TODO

- [ ] **Best-guess Q4 answer from bootstrap:** `CharacterCommunity` is an adapter on the character pointing at a `Community` owned by [[world]]. Based on folder contents only (one file in `Character/CharacterCommunity/`, three in `World/Community/`). Kevin to confirm or correct. (confidence: medium)
- [ ] `CharacterMentorship` exact folder path not verified — SKILL.md `character-mentorship` confirms the system but code location wasn't directly inspected. Folder check before batch 2.
- [ ] No SKILL.md for `character-speech` — tracked in [[TODO-skills]].
- [ ] Exact list of `ICharacterInteractionAction` implementations (15 files) — needs enumeration in the child sub-pages.

## Child sub-pages (to be written in Batch 2)

- [[character-interaction]] — lifecycle, dialogue coroutine, Freeze/Unfreeze rules.
- [[interaction-exchanges]] — turn-taking, Speaker/Listener, 6-exchange cap.
- [[character-invitation]] — template method, typed invitations (party, trade, lesson).
- [[character-mentorship]] — teaching ticks, XP / ability transfer.
- [[character-relation]] — bilateral bonds, compatibility filter, serialization.

Each sub-page must bidirectionally cross-link with [[character-relation]] per Kevin's Q7 answer.

## Change log
- 2026-04-18 — Initial documentation pass. Confidence medium because Q4 (character-community vs world-community) resolved by file inspection, not by explicit user confirmation. — Claude / [[kevin]]

## Sources
- [.agent/skills/social_system/SKILL.md](../../.agent/skills/social_system/SKILL.md)
- [.agent/skills/character_invitation/SKILL.md](../../.agent/skills/character_invitation/SKILL.md)
- [.agent/skills/interaction-exchanges/SKILL.md](../../.agent/skills/interaction-exchanges/SKILL.md)
- [.agent/skills/character-mentorship/SKILL.md](../../.agent/skills/character-mentorship/SKILL.md)
- [.claude/agents/character-social-architect.md](../../.claude/agents/character-social-architect.md)
- `Assets/Scripts/Character/CharacterInteraction/` (15 files).
- `Assets/Scripts/Character/CharacterRelation/` (3 files).
- `Assets/Scripts/Character/CharacterInvitation/` (1 file).
- 2026-04-18 conversation with [[kevin]] (Q4, Q7).
