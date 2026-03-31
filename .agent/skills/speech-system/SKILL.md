---
name: speech-system
description: Multi-bubble speech stacking with entrance/exit animations, per-bubble expiration, scripted dialogue support, and cross-character collision avoidance (Habbo Hotel style). All visual logic is client-local — RPC structure is unchanged.
---

# Speech System

The Speech System displays speech bubbles above characters. It supports stacking multiple concurrent bubbles, smooth entrance/exit animations, per-bubble expiration timers, scripted dialogue bubbles that persist until explicitly dismissed, and cross-character vertical spacing so nearby characters' stacks never visually overlap.

All stacking and animation is purely local visual state. No stack data is synchronized over the network.

## When to use this skill

- When triggering speech from gameplay logic, AI, or dialogue scripts via `CharacterSpeech`.
- When integrating `DialogueManager` with `SayScripted()` / `CloseSpeech()` / `ResetSpeech()`.
- When touching `SpeechBubbleStack`, `SpeechBubbleInstance`, or `SpeechZoneManager`.
- When adding a new character type that needs speech bubble support.
- When debugging missing, overlapping, or stale speech bubbles.

---

## Architecture Overview

```
CharacterSpeech  (CharacterSystem, child of Character root)
    └── delegates to ──>  SpeechBubbleStack  (on speech anchor child, pos 0,9,0)
                               └── spawns ──>  SpeechBubbleInstance  (per-bubble prefab children)
                               └── notifies ──>  SpeechZoneManager  (lazy scene singleton)
                                                     └── pushes ──>  other SpeechBubbleStacks
```

The `Character` facade holds `CharacterSpeech`. `CharacterSpeech` owns all public speech API and RPC routing. It delegates all visual work to `SpeechBubbleStack`. Each bubble in the stack is an independent `SpeechBubbleInstance` prefab that manages its own lifecycle.

---

## Components

### `CharacterSpeech` (CharacterSystem)

The public entry point for all speech on a character. Handles Netcode RPC routing so callers never need to know whether they are Server, Owner, or a remote client.

**Serialized Fields:**
| Field | Type | Purpose |
|---|---|---|
| `_bodyPartsController` | `CharacterBodyPartsController` | Source of `MouthController` reference passed to the stack on init |
| `_speechBubbleStack` | `SpeechBubbleStack` | The character's bubble stack, on the speech anchor child |
| `_audioSource` | `AudioSource` | Shared audio source for voice playback |
| `_voiceSO` | `VoiceSO` | ScriptableObject containing voice clips for typing sounds |
| `_voicePitch` | `float` (0.85–1.25) | Per-character randomized pitch, set on `Start()` |

**Public Properties:**
| Property | Returns | Description |
|---|---|---|
| `IsTyping` | `bool` | `true` if any bubble in the stack is currently typing |
| `IsSpeaking` | `bool` | `true` if the stack has at least one active bubble |

**Public Methods:**

`void Say(string message, float duration = 3f, float typingSpeed = 0f)`
Pushes an auto-expiring bubble. The bubble types out, waits `duration` seconds after typing completes, then fades out. Routed via RPCs so all clients display the bubble.

`void SayScripted(string message, float typingSpeed = 0f, Action onTypingFinished = null)`
Pushes a persistent bubble that never auto-expires. Persists until `CloseSpeech()` is called. `onTypingFinished` callback fires locally only — it is not transmitted over RPCs (scripted callbacks are not serializable).

`void CloseSpeech()`
Dismisses the bottom-most bubble with exit animation. Used by `DialogueManager` to advance scripted dialogue lines. Synced via RPCs. When called during a `ResetSpeech()` flow, calls `ClearAll()` instead (immediate, no animation).

`void ResetSpeech()`
Immediately clears all bubbles without animation. Used for dialogue end, character death, and despawn cleanup. Sets `_isResetting = true`, calls `CloseSpeech()` internally, then resets the flag.

**Death / Incapacitation hooks (CharacterSystem overrides):**
Both `HandleDeath()` and `HandleIncapacitated()` call `_speechBubbleStack.ClearAll()`. No animation — bubbles are destroyed immediately.

**RPC routing rules:**
| Caller | Behavior |
|---|---|
| Server | Calls `ClientRpc` to all non-server clients, then executes locally |
| Owner (non-server) | Calls `ServerRpc`; server broadcasts to clients and executes locally |
| Non-owner client | Executes locally only (no sync — edge case, logs a warning for `Say`) |

`SayScripted` has an additional guard in `SayScriptedClientRpc`: if the receiver is the Client Owner who already executed locally, the RPC is ignored to prevent double execution.

---

### `SpeechBubbleStack` (MonoBehaviour)

Lives on the speech anchor child GameObject (local position 0, 9, 0). Manages the ordered list of all active `SpeechBubbleInstance` objects for one character.

**Serialized Fields:**
| Field | Type | Default | Purpose |
|---|---|---|---|
| `_bubbleInstancePrefab` | `SpeechBubbleInstance` | — | Prefab to instantiate for each new bubble |
| `_maxBubbles` | `int` | 5 | Maximum simultaneous visible bubbles |
| `_separatorSpacing` | `float` | 0.5 | Extra vertical gap (world units) added between stacked bubbles |
| `_maxCrossCharacterOffset` | `float` | 350 | Ceiling on accumulated cross-character push to prevent runaway stacking in busy scenes |

**Public Properties:**
| Property | Returns | Description |
|---|---|---|
| `OwnerRoot` | `Transform` | Root `Character` transform. Set via `Init()`. Used by `SpeechZoneManager` for XZ distance checks |
| `IsAnyTyping` | `bool` | `true` when `_typingCount > 0` |
| `HasActiveBubbles` | `bool` | `true` when `_bubbles.Count > 0` |

**Initialization:**
`void Init(Transform ownerRoot, MouthController mouthController)`
Must be called by `CharacterSpeech.Start()` before any bubbles are pushed. Stores owner root for distance math and mouth controller for talking animation.

**Public Methods:**

`void PushBubble(string message, float duration, float typingSpeed, AudioSource audioSource, VoiceSO voiceSO, float pitch)`
Spawns an auto-expiring bubble at index 0 (bottom of stack). Pushes older bubbles upward. Enforces bubble cap. Notifies `SpeechZoneManager`.

`void PushScriptedBubble(string message, float typingSpeed, AudioSource audioSource, VoiceSO voiceSO, float pitch, Action onTypingFinished)`
Same stacking behavior as `PushBubble` but creates a bubble with no expiration timer. Persists until explicitly dismissed.

`void DismissBottom()`
Dismisses the bubble at index 0 (the newest/bottom bubble) with exit animation. Used for scripted dialogue advance. After animation completes, the bubble calls back to `RemoveBubble()` which closes the gap.

`void DismissAll()`
Triggers exit animation on all bubbles, then clears the internal list. Stops mouth animation immediately.

`void DismissAllScripted()`
Targets only bubbles where `IsScripted == true`. Non-scripted (auto-expiring) bubbles are unaffected. Used for dialogue system cleanup when ending a conversation while ambient `Say()` bubbles may still be visible.

`void ClearAll()`
Immediately `Destroy()`s all bubble GameObjects with no animation. Resets `_crossCharacterOffset` to zero. Stops mouth animation. Called by `ResetSpeech()`, `HandleDeath()`, `HandleIncapacitated()`, and `OnDisable()`.

`void AddCrossCharacterOffset(float height)`
Increments `_crossCharacterOffset` by `height`, clamped to `_maxCrossCharacterOffset`. Triggers a position recalculation — all existing bubbles smoothly lerp upward. This offset is permanent until `ClearAll()`.

`float GetTotalStackHeight()`
Returns the sum of all bubble heights + separator spacings + `_crossCharacterOffset`. Used by `SpeechZoneManager` to determine how much to push nearby stacks.

**Bubble list convention:**
- Index 0 = newest bubble, positioned at the bottom (closest to the character head).
- Index `Count - 1` = oldest bubble, positioned at the top.
- The bottom bubble (index 0) never shows a separator line. All others do.

**Mouth controller management:**
The stack tracks `_typingCount` — a reference count of how many bubbles are actively typing. `MouthController.StartTalking()` is called when count rises from 0 to 1. `StopTalking()` is called when it falls back to 0. Individual `SpeechBubbleInstance` objects never call the mouth controller directly.

**Stack lifecycle (OnEnable / OnDisable):**
`OnEnable` registers with `SpeechZoneManager`. `OnDisable` calls `ClearAll()` then unregisters. This means any scene unload, character despawn, or hibernation automatically cleans up all bubbles and removes the stack from the collision manager's registry.

---

### `SpeechBubbleInstance` (MonoBehaviour)

Spawned as a child of `SpeechBubbleStack`. Each instance is an independent prefab that owns its typing coroutine, voice playback, entrance/exit animations, expiration timer, and separator line.

**Requires:** `CanvasGroup` (used for alpha fade)

**Serialized Fields:**
| Field | Type | Default | Purpose |
|---|---|---|---|
| `_textElement` | `TextMeshProUGUI` | — | Text display |
| `_separatorLine` | `GameObject` | — | White horizontal rule shown between stacked bubbles |
| `_entranceDuration` | `float` | 0.3s | Duration of fade-in + slide-down entrance |
| `_exitDuration` | `float` | 0.3s | Duration of fade-out + slide-up exit |

**Public Properties:**
| Property | Returns | Description |
|---|---|---|
| `IsTyping` | `bool` | `true` while the typing coroutine is running |
| `IsScripted` | `bool` | `true` for bubbles created via `SetupScripted()` |

**Events:**
| Event | Signature | Fired when |
|---|---|---|
| `OnExpired` | `Action` | Auto-expiring bubble finishes exit animation (after timer + fade) |
| `OnHeightChanged` | `Action` | `RectTransform.rect.height` changes during or after typing (text wrap detection) |
| `OnTypingStateChanged` | `Action<bool>` | Typing starts (`true`) or stops (`false`) — used by stack to track `_typingCount` |

All event delegates are set to `null` in `OnDestroy` to prevent stale subscriber leaks.

**Public Methods:**

`void Setup(string message, AudioSource audioSource, VoiceSO voiceSO, float pitch, float typingSpeed, float duration, Action onExpired)`
Configures a standard auto-expiring bubble. Chain: entrance animation → typing coroutine → expiration timer → exit animation → `Destroy`. `onExpired` callback fires after the exit animation completes.

`void SetupScripted(string message, AudioSource audioSource, VoiceSO voiceSO, float pitch, float typingSpeed, Action onTypingFinished)`
Configures a persistent bubble. Chain: entrance animation → typing coroutine → `onTypingFinished` callback. No expiration timer. Bubble persists until `Dismiss()` is called externally.

`void CompleteTypingImmediately()`
Stops the typing coroutine. Sets text to the full message. Fires `OnTypingStateChanged(false)` and `OnHeightChanged`. For scripted bubbles: fires `onTypingFinished` immediately. For non-scripted: starts the expiration timer. Called by `SpeechBubbleStack` when a new bubble is pushed onto an already-typing bottom bubble.

`void Dismiss(Action onComplete = null)`
Stops typing and expiration coroutines. Plays exit animation. On animation end: invokes `onComplete`, then calls `Destroy(gameObject)`. Safe to call multiple times — the expiration coroutine is stopped first to prevent double-dismiss.

`float GetHeight()`
Returns `RectTransform.rect.height`. Used by the stack for position offset calculations.

`void SetTargetPosition(Vector3 localPos)`
Updates `_targetPosition`. The `Update()` loop lerps `transform.localPosition` toward this target every frame using `8f * Time.unscaledDeltaTime`.

`void SetSeparatorVisible(bool visible)`
Shows or hides the `_separatorLine` GameObject. Controlled entirely by `SpeechBubbleStack.UpdateSeparatorVisibility()`.

**Coroutine cleanup:**
`OnDisable` stops all three coroutines (`_typeRoutine`, `_animRoutine`, `_expirationRoutine`) to prevent dangling coroutines when the GameObject is deactivated or destroyed mid-flight.

---

### `SpeechZoneManager` (MonoBehaviour — lazy scene singleton)

A lightweight scene-level coordinator. Tracks all active `SpeechBubbleStack` instances and applies vertical push offsets when nearby characters speak simultaneously.

**Not** `DontDestroyOnLoad` — each map scene has its own instance that is naturally destroyed on scene unload, resetting all cross-character state.

**Serialized Fields:**
| Field | Type | Default | Purpose |
|---|---|---|---|
| `_speechZoneRadius` | `float` | 15 | World-space radius for collision avoidance detection (XZ plane only) |

**Public Methods:**

`void RegisterStack(SpeechBubbleStack stack)`
Adds a stack to the registry. Called by `SpeechBubbleStack.OnEnable`.

`void UnregisterStack(SpeechBubbleStack stack)`
Removes a stack from the registry. Called by `SpeechBubbleStack.OnDisable`.

`void NotifyBubblePushed(SpeechBubbleStack source, float bubbleHeight)`
The main collision avoidance method. Called by `SpeechBubbleStack` after every `PushBubble` or `PushScriptedBubble`. Iterates the registry, excludes `source`, checks XZ distance using `OwnerRoot.position` (not the anchor position), and calls `AddCrossCharacterOffset(bubbleHeight)` on every nearby stack that currently has active bubbles. Stacks with no active bubbles are skipped — a character who is not speaking is not pushed.

**Distance calculation note:** Uses `OwnerRoot.position` (the Character root, at ground level) for distance checks. This avoids inflating XZ distance readings with the Y=9 anchor offset.

**Performance:** Only runs on `PushBubble` / `PushScriptedBubble` events — not per-frame. Iteration is O(n) over registered stacks.

---

## Data Flow

### `Say()` — Auto-Expiring Bubble

```
1. CharacterSpeech.Say(message, duration, typingSpeed)
2.   → RPC routing (Server / Owner / local fallback)
3.   → ExecuteSayLocally() → _speechBubbleStack.PushBubble(...)
4.     → Enforce cap: if _bubbles.Count >= _maxBubbles, force-dismiss oldest bubble (last index)
5.     → CompleteTypingImmediately() on current index-0 bubble if it is still typing
6.     → Instantiate SpeechBubbleInstance, call Setup(...)
7.     → Subscribe: OnHeightChanged → RecalculatePositions, OnTypingStateChanged → OnTypingStateChanged
8.     → Insert at index 0; UpdateSeparatorVisibility(); RecalculatePositions()
9.     → SpeechZoneManager.NotifyBubblePushed(this, instance.GetHeight())
10.    → SpeechBubbleInstance plays entrance animation (fade in + slide down)
11.    → TypeMessage coroutine runs: letter-by-letter, fires OnTypingStateChanged(true) at start
12.    → Each character added: CheckHeightChanged() fires OnHeightChanged if text wraps
13.    → Typing completes: fires OnTypingStateChanged(false); ExpirationTimer coroutine starts
14.    → WaitForSecondsRealtime(duration)
15.    → Dismiss(): exits animation (fade out + slide up), then Destroy(gameObject)
16.    → OnExpired fires → SpeechBubbleStack.OnBubbleExpired() → RemoveBubble()
17.    → RecalculatePositions(): remaining bubbles lerp down to close the gap
```

### `SayScripted()` — Persistent Bubble

```
1. CharacterSpeech.SayScripted(message, typingSpeed, onTypingFinished)
2.   → RPC routing (Owner also executes locally; ClientRpc has guard to skip if Client Owner)
3.   → ExecuteSayScriptedLocally() → _speechBubbleStack.PushScriptedBubble(...)
4.   → Same stacking as Say() steps 4–9
5.   → SpeechBubbleInstance plays entrance animation
6.   → TypeMessage runs, completes, fires onTypingFinished callback (local only, not sent over RPC)
7.   → Bubble persists indefinitely
8.   → Dialogue system calls CharacterSpeech.CloseSpeech() to advance
9.   → _speechBubbleStack.DismissBottom() → index-0 bubble plays exit animation
10.  → After animation: RemoveBubble(); RecalculatePositions(); gap closes
11.  → Dialogue system calls SayScripted() for next line — cycle repeats
```

### `CloseSpeech()` — Scripted Advance

```
1. CharacterSpeech.CloseSpeech()
2.   → RPC routing
3.   → ExecuteCloseSpeechLocally()
4.   If _isResetting: _speechBubbleStack.ClearAll()    (no animation)
5.   Else:           _speechBubbleStack.DismissBottom() (with animation)
```

### `ResetSpeech()` — Full Cleanup

```
1. CharacterSpeech.ResetSpeech()
2.   → _isResetting = true
3.   → CloseSpeech() — which calls ExecuteCloseSpeechLocally() → ClearAll()
4.   → _isResetting = false
Note: ResetSpeech does NOT send RPCs beyond what CloseSpeech already sends.
Use _speechBubbleStack.ClearAll() directly for death/despawn (no RPC needed — visual only).
```

### Cross-Character Collision Avoidance

```
1. CharacterA pushes a bubble (PushBubble or PushScriptedBubble)
2. Stack calls SpeechZoneManager.Instance.NotifyBubblePushed(stackA, bubbleHeight)
3. Manager iterates all registered stacks:
   - Skip stackA (self)
   - Skip stacks with no active bubbles
   - For each remaining stack: compute XZ distance using OwnerRoot.position
   - If distance <= _speechZoneRadius (15 units):
       stack.AddCrossCharacterOffset(bubbleHeight)
4. Affected stack increments _crossCharacterOffset (clamped to _maxCrossCharacterOffset = 350)
5. RecalculatePositions() shifts ALL of that stack's bubbles upward (animated lerp)
6. The gap has no separator line — it is just empty vertical space
7. This offset is permanent — it does not decrease when CharacterA's bubble expires
8. The offset only resets when the AFFECTED stack itself calls ClearAll()
```

---

## Animation Details

All animations use `Time.unscaledDeltaTime` to remain functional during game pause or at high `GameSpeedController` scales (CLAUDE.md rule 26).

| Phase | Alpha | Y Position | Curve | Duration |
|---|---|---|---|---|
| Entrance | 0 → 1 | +15 → 0 (slides down) | EaseOut: `1 - (1-t)²` | 0.3s |
| Exit | 1 → 0 | 0 → +10 (drifts up) | EaseIn: `t²` | 0.3s |
| Reposition | — | current → target | `Lerp(pos, target, 8f * unscaledDeltaTime)` | continuous |

**Reposition:** Runs in `SpeechBubbleInstance.Update()`. The lerp coefficient (8) produces a fast initial snap with smooth deceleration. Convergence threshold is `sqrMagnitude < 0.001f` to stop updating once settled.

**Separator line:** A white horizontal `Image`, 60% of parent width, 1px height. Shown on all bubbles except index 0 (the bottom/newest). Visibility is re-evaluated on every `PushBubble`, `RemoveBubble`, `DismissAllScripted`, and `UpdateSeparatorVisibility()` call.

---

## Network Considerations

- No changes to the RPC structure from the pre-stacking system.
- `SpeechBubbleInstance` prefabs are **not NetworkObjects** — they are pure local UI.
- Stack state is never synchronized. Each client independently builds its own stack from incoming RPCs.
- `onTypingFinished` callbacks in `SayScripted` are local-only and are not transmitted over the network. Remotes receive `null` for this callback.
- `SpeechZoneManager` is a local scene object — cross-character offsets are computed independently on each client from their own local stack state.
- Late-joining clients miss all prior RPCs and see no pre-existing speech bubbles. This matches the original behavior and is intentional — speech is ephemeral UI, not game state.

**Validate against all player relationships before modifying CharacterSpeech RPCs:**
- Host (Server + Client): calls ClientRpc to others, executes locally.
- Client Owner: calls ServerRpc; server broadcasts + executes; ClientRpc guard prevents double-execution on the Owner.
- Client non-owner: executes locally only (no sync).
- NPC (server-authoritative): treated the same as Host — calls ClientRpc then executes locally.

---

## Dependencies

| Dependency | Type | Purpose |
|---|---|---|
| `CharacterSystem` | Base class | `CharacterSpeech` inherits from this; provides `_character` reference and `HandleDeath` / `HandleIncapacitated` hooks |
| `Character` | Facade | Root character reference; `OwnerRoot` in the stack; passed to `Init()` |
| `CharacterBodyPartsController` | Component | Supplies the `MouthController` reference |
| `MouthController` | Component | `StartTalking()` / `StopTalking()` during typing |
| `SpeechBubbleInstance` prefab | Prefab | Spawned per bubble; must have `CanvasGroup`, `TextMeshProUGUI`, separator `Image` |
| `VoiceSO` | ScriptableObject | Provides random voice clips for typing sounds |
| `AudioSource` | Component | Shared audio source on the character for voice playback |
| `Billboard` | Component | Stays on the speech anchor so all bubbles face the camera |
| `TextMeshPro` | Package | Required for `TextMeshProUGUI` text rendering |

---

## Prefab Structure: `SpeechBubbleInstance_Prefab`

```
SpeechBubbleInstance (Root)
├── CanvasGroup              (alpha for fade animation)
├── SpeechBubbleInstance.cs  (script component)
├── SeparatorLine            (Image, white, ~60% width, 1px height — disabled by default)
└── Canvas (WorldSpace, 300x70)
    ├── CanvasScaler (800x600 reference)
    └── Text_Speech (TextMeshProUGUI)
        ├── ContentSizeFitter (VerticalFit: PreferredSize)
        ├── Font: Default TMP, 36pt, white, center-aligned
        └── Word wrap enabled
```

The `ContentSizeFitter` on `Text_Speech` makes the bubble expand vertically when text wraps. `SpeechBubbleInstance.CheckHeightChanged()` detects these changes and fires `OnHeightChanged` so the stack repositions.

---

## Character Prefab Setup

The speech anchor is a child of the Character root at local position (0, 9, 0).

Required components on the anchor:
- `SpeechBubbleStack` — with `_bubbleInstancePrefab` assigned
- `Billboard` — so all bubbles automatically face the camera

`CharacterSpeech` (on its own child GameObject per the Facade pattern) needs:
- `_speechBubbleStack` — dragged from the speech anchor
- `_bodyPartsController` — for mouth animation
- `_audioSource` and `_voiceSO` — for voice playback

`CharacterSpeech.Start()` calls `_speechBubbleStack.Init(_character.transform, _bodyPartsController?.MouthController)`.

---

## Integration Points

### DialogueManager

Uses `SayScripted()` to display scripted lines and `CloseSpeech()` to advance. When the dialogue ends or is abandoned, call `ResetSpeech()` to clear any remaining scripted bubbles immediately.

For multi-character dialogues, each participant has their own `CharacterSpeech`. `DialogueManager` routes lines to the correct character's `SayScripted()` by `characterIndex`.

If no player characters are participating, `DialogueManager` auto-advances with a 1.5-second delay after `onTypingFinished` fires.

### UI_ChatBar / Player Chat Input

Routes through `character.Speech.Say(message)`. No direct access to `SpeechBubbleStack` needed.

### Interaction System / CharacterInteraction (Generic Dialogues)

Uses `Say()` for ambient NPC-to-NPC exchanges in the `DialogueSequence` coroutine. Roles alternate between Speaker and Listener up to `MAX_EXCHANGES`. Wait times are injected between calls, not inside the speech system.

### AI / GOAP

NPC AI calls `character.Speech.Say()` or `character.Speech.SayScripted()` through `CharacterAction` — never directly. This preserves player/NPC parity as required by CLAUDE.md rule 22.

### Death / Incapacitation

`CharacterSpeech.HandleDeath()` and `HandleIncapacitated()` both call `_speechBubbleStack.ClearAll()` directly. No RPC is needed — this is local visual cleanup.

### Map Hibernation / Despawn

`SpeechBubbleStack.OnDisable()` calls `ClearAll()` and unregisters from `SpeechZoneManager`. This is the single cleanup path for all despawn scenarios — no additional teardown is needed.

---

## Edge Cases and Known Limitations

**Late-joining clients:** Clients joining mid-conversation miss prior RPCs and see no existing bubbles. Speech is ephemeral UI, not replicated game state. This is intentional and matches the original behavior.

**Bubble cap (max 5):** When `_bubbles.Count >= _maxBubbles`, the oldest bubble (last index) is force-dismissed before the new one is pushed. The oldest bubble's `UnsubscribeEvents` is called before `Dismiss()` to prevent its `OnExpired` callback from triggering a second `RemoveBubble`.

**Cross-character offset accumulation:** In busy towns, offsets can build up. Mitigated by:
1. Per-stack bubble cap (max 5 bubbles, limiting how many pushes can be issued per character).
2. Natural expiration — `ClearAll()` on death/despawn resets the offset.
3. `_maxCrossCharacterOffset = 350` hard ceiling — additional pushes above this threshold are ignored.

**Characters moving apart after speech exchange:** Cross-character offsets are fire-and-forget. If two characters were within 15 units when one spoke, the pushed offset on the other's stack persists even if they walk apart. No dynamic recalculation occurs. Offsets only reset via `ClearAll()`.

**Bubble height for cross-character offset calculation:** `GetHeight()` is called at push time using the prefab's default RectTransform height, not the post-layout height after text wraps. This is an acceptable approximation because the true wrapped height is unavailable until end of frame.

**Object pooling:** Not implemented in v1. If GC pressure becomes measurable in towns with many active speakers, add a pool of ~5 instances per stack as a follow-up.

**`DismissBottom()` on mixed stacks:** Always targets index 0, regardless of whether it is scripted or auto-expiring. `DismissAllScripted()` should be used when a dialogue ends while ambient `Say()` bubbles may still be present.

**Double-execution guard in `SayScriptedClientRpc`:** A Client Owner calls `ExecuteSayScriptedLocally` before sending the `ServerRpc`. The server then broadcasts `SayScriptedClientRpc` to all clients including the Owner. The guard `if (IsOwner && !IsServer) return;` prevents the Owner from executing the bubble push a second time.

**`ResetSpeech()` does not send a dedicated RPC.** It piggybacks on `CloseSpeech()`'s RPC with `_isResetting = true`. Since `ClearAll()` is purely visual, this is intentional — remotes do not need to mirror the reset; they will handle their own `OnDisable` cleanup via the stack lifecycle.
