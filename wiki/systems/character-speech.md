---
type: system
title: "Character Speech"
tags: [character, speech, dialogue, ui, hud, tier-2]
created: 2026-04-19
updated: 2026-04-20
sources: []
related: ["[[character]]", "[[dialogue]]", "[[social]]", "[[visuals]]", "[[kevin]]"]
status: stable
confidence: high
primary_agent: character-system-specialist
owner_code_path: "Assets/Scripts/Character/CharacterSpeech/"
depends_on: ["[[character]]"]
depended_on_by: ["[[dialogue]]", "[[social]]"]
---

# Character Speech

## Summary
Speech bubbles over character heads, rendered on the local player's HUD canvas (screen-space) and positioned each frame via `Camera.WorldToScreenPoint` of each speaker's world-space speech anchor. Bubbles only show when the local player character is within 25 world units of the speaker. Four classes carry the system: `CharacterSpeech` (per-character facade + Netcode RPC routing), `SpeechBubbleStack` (per-character list owner with Habbo-style cross-speaker push), `SpeechBubbleInstance` (per-bubble animation + typing + screen projection), and `HUDSpeechBubbleLayer` (one per client, provides the shared parent transform and resolves the local-player anchor). Ephemeral UI — no network state, no save/load.

## Purpose
Give characters visible speech with three ergonomic properties: readable at a glance (HUD text rather than shrunken world sprites), non-intrusive at distance (auto-hide for far speakers), and non-overlapping when characters stand close together (Habbo-style stacking so concurrent bubbles don't fight). Exposes `IsSpeaking` / `IsTyping` so the [[dialogue]] system can pace line advances.

## Responsibilities
- Queuing speech via `Say` (auto-timeout) and `SayScripted` (persist-until-`CloseSpeech`), routed over Netcode RPCs so Host / Client Owner / remote-client callers all reach the right execution path.
- Owning the per-speaker list of active bubbles (cap enforcement, mouth-animation reference counting, Habbo cross-speaker push in HUD pixels).
- Projecting each speaker's world-space anchor position to screen-space every frame and updating the bubble's `anchoredPosition`.
- Gating bubble visibility by proximity from the local player character to the speaker and by on-screen status.
- Publishing `IsSpeaking`, `IsTyping`, `HasActiveBubbles` for external pacing.

## Key classes / files
- [CharacterSpeech.cs](../../Assets/Scripts/Character/CharacterSpeech/CharacterSpeech.cs) — facade on the Character root. Owns `Say`, `SayScripted`, `CloseSpeech`, `ResetSpeech` plus the full RPC routing (Server / Owner / non-owner fallback). Post-2026-04-20: no longer overrides `HandleDeath` / `HandleIncapacitated` — bubbles follow their natural lifecycle; cleanup happens on despawn via `OnDisable → ClearAll`.
- [SpeechBubbleStack.cs](../../Assets/Scripts/Character/CharacterSpeech/SpeechBubbleStack.cs) — on the speech anchor child (+9u above the character root). Owns the bubble list, cap, Habbo cross-character push, mouth-animation reference count, and a per-stack `CanvasGroup` wrapper GameObject parented under `HUDSpeechBubbleLayer.Local.ContentRoot`. Per-frame `Update` computes proximity (feet-to-feet) and on-screen status, then lerps `wrapper.alpha` via `Mathf.MoveTowards`.
- [SpeechBubbleInstance.cs](../../Assets/Scripts/Character/CharacterSpeech/SpeechBubbleInstance.cs) — one per active bubble. Owns typing coroutine, voice playback, entrance/exit animations (bias-offset based, separate from stack offset), expiration timer. Per-frame `Update` projects `_speakerAnchor.position` to screen space and lerps `anchoredPosition`.
- [HUDSpeechBubbleLayer.cs](../../Assets/Scripts/UI/HUDSpeechBubbleLayer.cs) — one per client, lives on the local player's HUD canvas. Provides static `Local` accessor, shared `ContentRoot` transform, and lazy-resolved `Camera` + `LocalPlayerAnchor` (via `NetworkManager.Singleton.LocalClient.PlayerObject`).
- Prefabs: [SpeechBubbleInstance_Prefab.prefab](../../Assets/Prefabs/SpeechBubbleInstance_Prefab.prefab) (the bubble UI — `RectTransform` + `CanvasGroup` + Image bg + `Text_Speech` TMP + `SeparatorLine`), [UI_PlayerHUD.prefab](../../Assets/UI/Player%20HUD/UI_PlayerHUD.prefab) (hosts `HUDSpeechBubbleLayer` + `ContentRoot`).

## Public API / entry points
All external callers (player chat input, dialogue manager, NPC AI, character interactions) go through `CharacterSpeech`. Nothing outside the namespace should touch `SpeechBubbleStack` or `SpeechBubbleInstance` directly.

- `Say(string message, float duration = 3f, float typingSpeed = 0f)` — auto-expiring bubble. Network-safe.
- `SayScripted(string message, float typingSpeed = 0f, Action onTypingFinished = null)` — persistent bubble. The `onTypingFinished` callback is local-only (not RPC'd).
- `CloseSpeech()` — dismisses the bottom-most bubble; used by [[dialogue]] to advance lines.
- `ResetSpeech()` — clears all bubbles immediately, no animation.
- Properties: `IsSpeaking`, `IsTyping`.

Full behavioural contract and edge cases: [.agent/skills/speech-system/SKILL.md](../../.agent/skills/speech-system/SKILL.md).

## Data flow
1. Caller invokes `CharacterSpeech.Say(...)` or `SayScripted(...)`. Netcode routing: Server → `ClientRpc` to all + local execute; Owner (non-server) → `ServerRpc` → broadcast; non-owner → local-only fallback with warning.
2. Each client independently executes `_speechBubbleStack.PushBubble(...)`.
3. `PushBubble` enforces the cap (force-dismiss oldest), lazily creates a HUD wrapper under `HUDSpeechBubbleLayer.Local.ContentRoot`, instantiates a `SpeechBubbleInstance` inside the wrapper, binds `_speakerAnchor` to the stack's own transform (the +9 speech anchor), and inserts the bubble at index 0.
4. Habbo push: all older bubbles on this stack and all bubbles on every stack in `_nearbyStacks` (populated by a SphereCollider trigger on the SpeechZone physics layer, radius 25) are shifted up by `newBubble.GetHeightPx() + _separatorSpacingPx`. Each pushed bubble has its expiration timer reset so the older line doesn't silently time out mid-conversation.
5. Per-frame `SpeechBubbleInstance.Update`: computes screen point of speaker, OR's `IsOffScreen` against screen-rect bounds, then lerps `anchoredPosition` toward `screenPos + _stackOffsetPx + _animationBiasPx`.
6. Per-frame `SpeechBubbleStack.Update`: reads `HUDSpeechBubbleLayer.Local.LocalPlayerAnchor.position` and `OwnerRoot.position` (character root, not the +9 head), compares `sqrMagnitude` to `25²`, aggregates any-on-screen across the bubbles, and lerps `wrapper.alpha` toward 1 (in-range + on-screen) or 0 (otherwise).

## Dependencies

**Upstream (consumed):**
- [[character]] — root facade; `Character.transform` feeds `OwnerRoot`, and the speech anchor is a child of the character root.
- Unity Netcode for GameObjects — RPC routing and the `NetworkManager.Singleton.LocalClient.PlayerObject` handshake for local-player resolution.
- Unity TextMeshPro — text rendering in the bubble prefab.
- A screen-space Canvas on the local player's HUD (currently `UI_PlayerHUD`) — parent for the HUD layer.

**Downstream (consumers):**
- [[dialogue]] — `SayScripted` + `CloseSpeech` to advance `DialogueSO` sequences; waits on `IsTyping` for pacing.
- [[social]] — social interactions trigger dialogue which flows through this system.
- Player UI — `UI_ChatBar` routes player text via `Character.Speech.Say(...)`.

## State & persistence
- **Not persisted.** Speech is ephemeral UI. No `NetworkVariable`, no `ISaveable`, no save hooks.
- Stack state is rebuilt per client from incoming RPCs. Late-joining clients see no pre-existing bubbles (intentional — speech is not replicated game state).
- On character hibernation / despawn, `SpeechBubbleStack.OnDisable` calls `ClearAll()` which destroys every bubble GameObject and the wrapper. No central registry to unregister from.

## Known gotchas / edge cases
- **Bubble prefab root anchors must be `(0, 0)`.** `SpeechBubbleInstance.Update` treats `anchoredPosition` as absolute screen pixels. Any other anchor (e.g. `(0.5, 0)` center-bottom) causes the bubble to land at `anchor + screenPos` — visibly offset by ~half the screen. If you drag the bubble in the Scene view while editing the prefab, Unity silently re-anchors it. Re-verify after any prefab edit.
- **`_stackOffsetPx` and `_animationBiasPx` are separate.** `_stackOffsetPx` is the Habbo-push position, owned by `SpeechBubbleStack`. `_animationBiasPx` is the entrance/exit slide offset, owned by the animation coroutines. Conflating them (as the first HUD port did) caused the entrance animation's final `_stackOffsetPx = targetOffset` write to silently clobber any Habbo push that happened during the 0.3s entrance window.
- **Bubbles don't come back down.** When a bubble expires, remaining bubbles stay at their pushed Y — leaving empty vertical space. Intentional ("Habbo Hotel style" — preserves visual history of who spoke when).
- **Bubble cap: `_maxBubbles = 5`.** When exceeded, oldest is force-dismissed before the new push; `UnsubscribeEvents` runs first to prevent a stale `OnExpired` callback from double-removing.
- **Death no longer clears bubbles.** Post-2026-04-20, `CharacterSpeech` does not override `HandleDeath` / `HandleIncapacitated`. Bubbles follow their natural lifecycle (expiration, dismissal) and are only force-cleared on full despawn via `OnDisable`.
- **Camera lazy resolution per bubble.** `SpeechBubbleInstance.Update` re-resolves `_camera` on null so NPC bubbles created before the local player's HUD is fully up (session boot race) start rendering correctly as soon as the HUD appears.
- **Proximity is feet-to-feet, not head-to-head.** Uses `OwnerRoot.position` (character root) on both sides so the 25u "hearing range" corresponds to what the player intuitively sees as distance between two characters, not to the bubble anchors.
- **Wrapper alpha initialises to 1.** A freshly-pushed bubble from an in-range speaker is immediately visible; out-of-range speakers see a single-frame flash before the proximity gate drops the alpha. Chosen over the alternative (start at 0, fade in over 0.25s) which made every bubble's entrance look faint.
- **Prefab restructure pitfall (historical).** The pre-refactor bubble had a nested `WorldSpace Canvas` child that hosted both the background `Image` and the `Text_Speech`. Destroying that child during the HUD port lost the background Image and carried a stale `localScale = (0.02, 0.02, 0.02)` up to the root. Both required follow-up commits. Any future prefab restructure must enumerate every child-hosted component before destruction.

## Open questions / TODO
- **`SortingGroup` on the bubble prefab root.** Leftover from the WorldSpace era; currently disabled. Could be fully removed.
- **HUD layer GameObject layer.** `HUDSpeechBubbleLayer` and `ContentRoot` were created on Layer 0 (Default) rather than Layer 5 (UI) by the MCP tool. Functionally harmless but inconsistent with sibling HUD elements.
- **Off-screen edge indicator.** Speakers off-screen currently just fade to 0. A future enhancement could clamp the bubble to the screen edge with an arrow — explicitly deferred in the design spec.
- **Per-archetype proximity radius.** Currently one global `_proximityRadius = 25f`. NPCs with loud/quiet personalities might want overrides.
- **Bubble pooling.** Instantiate/Destroy per bubble. Acceptable in practice; add a pool if GC pressure shows up in crowded towns.
- **Humanoid prefab stale overrides.** `_maxCrossCharacterOffset` and the renamed `_separatorSpacing` still appear as variant overrides in `Character_Default_Humanoid.prefab`. Unity silently drops them (no `[FormerlySerializedAs]` bridge) but they're visual noise in the YAML.

## Change log
- 2026-04-20 — HUD screen-space rewrite. World-space bubbles replaced by HUD-parented bubbles via `HUDSpeechBubbleLayer`. Added 25u proximity gate, `_animationBiasPx` separation (fix entrance-vs-push race), removed `HandleDeath` / `HandleIncapacitated` overrides, Billboard removed from speech anchor, SphereCollider radius 15→25, bubble prefab restructured (WorldSpace Canvas gone), black translucent background restored after prefab restructure lost it. — Claude / [[kevin]]
- 2026-04-19 — Stub created. — Claude / [[kevin]]

## Sources
- [Assets/Scripts/Character/CharacterSpeech/CharacterSpeech.cs](../../Assets/Scripts/Character/CharacterSpeech/CharacterSpeech.cs) — facade + RPC routing.
- [Assets/Scripts/Character/CharacterSpeech/SpeechBubbleStack.cs](../../Assets/Scripts/Character/CharacterSpeech/SpeechBubbleStack.cs) — list owner, Habbo push, proximity gate.
- [Assets/Scripts/Character/CharacterSpeech/SpeechBubbleInstance.cs](../../Assets/Scripts/Character/CharacterSpeech/SpeechBubbleInstance.cs) — per-bubble animation, typing, screen projection.
- [Assets/Scripts/UI/HUDSpeechBubbleLayer.cs](../../Assets/Scripts/UI/HUDSpeechBubbleLayer.cs) — local HUD anchor + local-player resolution.
- [.agent/skills/speech-system/SKILL.md](../../.agent/skills/speech-system/SKILL.md) — operational procedures and full public API contract.
- [docs/superpowers/specs/2026-04-20-hud-speech-bubbles-design.md](../../docs/superpowers/specs/2026-04-20-hud-speech-bubbles-design.md) — design spec for the HUD rewrite.
- [docs/superpowers/plans/2026-04-20-hud-speech-bubbles.md](../../docs/superpowers/plans/2026-04-20-hud-speech-bubbles.md) — implementation plan.
- [docs/superpowers/specs/2026-03-31-speech-bubble-stacking-design.md](../../docs/superpowers/specs/2026-03-31-speech-bubble-stacking-design.md) — prior stacking spec (Habbo push, superseded by HUD rewrite at the rendering layer).
