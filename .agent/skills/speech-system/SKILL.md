---
name: speech-system
description: Multi-bubble speech stacking with entrance/exit animations, per-bubble expiration, scripted dialogue support, and Habbo Hotel style cross-character collision avoidance via SphereCollider trigger. All visual logic is client-local — RPC structure is unchanged.
---

# Speech System

The Speech System displays speech bubbles above characters. It supports stacking multiple concurrent bubbles, smooth entrance/exit animations, per-bubble expiration timers, scripted dialogue bubbles that persist until explicitly dismissed, and cross-character vertical spacing so nearby characters' stacks never visually overlap.

All stacking and animation is purely local visual state. No stack data is synchronized over the network.

## When to use this skill

- When triggering speech from gameplay logic, AI, or dialogue scripts via `CharacterSpeech`.
- When integrating `DialogueManager` with `SayScripted()` / `CloseSpeech()` / `ResetSpeech()`.
- When touching `SpeechBubbleStack` or `SpeechBubbleInstance`.
- When adding a new character type that needs speech bubble support.
- When debugging missing, overlapping, or stale speech bubbles.

---

## Architecture Overview

```
CharacterSpeech  (CharacterSystem, child of Character root)
    └── delegates to ──>  SpeechBubbleStack  (on speech anchor child, pos 0,9,0)
                               ├── spawns ──>  SpeechBubbleInstance  (per-bubble prefab children)
                               ├── SphereCollider trigger ──> detects nearby SpeechBubbleStacks
                               └── on PushBubble: pushes ALL bubbles in own stack + nearby stacks UP
```

There is **no SpeechZoneManager singleton**. Cross-character detection is handled per-stack via a `SphereCollider` trigger on the `SpeechBubbleStack` GameObject itself. When a character pushes a new bubble, it directly iterates its own `_nearbyStacks` set (populated by trigger enter/exit) and calls `PushAllBubblesUpBy()` on each.

The `Character` facade holds `CharacterSpeech`. `CharacterSpeech` owns all public speech API and RPC routing. It delegates all visual work to `SpeechBubbleStack`. Each bubble in the stack is an independent `SpeechBubbleInstance` prefab that manages its own lifecycle.

---

## Cross-Character Collision Model (Habbo Hotel Style)

This is the core design principle of the stacking system:

1. **New bubble always appears at the base** (Y=0, closest to the character head).
2. **All existing bubbles — own AND nearby — are pushed UP** by the new bubble's height when it spawns.
3. **Bubbles never come back down**. When a bubble expires or is dismissed, it fades out and leaves empty vertical space. There is no gap-closing or repositioning after removal.
4. **When a nearby stack gets pushed**, its bubbles' expiration timers are reset (via `ResetExpirationTimer()`) so they stay visible during the conversation.
5. Cross-character detection uses **Physics layer 14 (SpeechZone)**. The SphereCollider is set to only collide with itself on that layer, preventing interference with gameplay physics.

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

Lives on the speech anchor child GameObject (local position 0, 9, 0). Manages the ordered list of all active `SpeechBubbleInstance` objects for one character. Also owns the `SphereCollider` and `Rigidbody` for cross-character detection.

**Required Components:** `SphereCollider` (set to trigger, radius = `_speechZoneRadius`), `Rigidbody` (kinematic, no gravity).

**Serialized Fields:**
| Field | Type | Default | Purpose |
|---|---|---|---|
| `_bubbleInstancePrefab` | `SpeechBubbleInstance` | — | Prefab to instantiate for each new bubble |
| `_maxBubbles` | `int` | 5 | Maximum simultaneous visible bubbles |
| `_separatorSpacing` | `float` | 0.03 | Extra vertical gap (world units) added between stacked bubbles |
| `_speechZoneRadius` | `float` | 15 | World-space radius of the trigger sphere for cross-character detection |

**Public Properties:**
| Property | Returns | Description |
|---|---|---|
| `OwnerRoot` | `Transform` | Root `Character` transform. Set via `Init()`. |
| `IsAnyTyping` | `bool` | `true` when `_typingCount > 0` |
| `HasActiveBubbles` | `bool` | `true` when `_bubbles.Count > 0` |

**Initialization:**
`void Init(Transform ownerRoot, MouthController mouthController)`
Must be called by `CharacterSpeech.Start()` before any bubbles are pushed. Stores owner root and mouth controller reference.

**Public Methods:**

`void PushBubble(string message, float duration, float typingSpeed, AudioSource audioSource, VoiceSO voiceSO, float pitch)`
Spawns an auto-expiring bubble at Y=0 (base of stack). Calls `PushAllBubblesUp()` to push all existing bubbles (own + nearby stacks) upward. Enforces bubble cap.

`void PushScriptedBubble(string message, float typingSpeed, AudioSource audioSource, VoiceSO voiceSO, float pitch, Action onTypingFinished)`
Same push behavior as `PushBubble` but creates a bubble with no expiration timer.

`void PushAllBubblesUpBy(float height)`
Called by a nearby stack when it spawns a new bubble. Shifts every bubble in this stack upward by `height` and calls `ResetExpirationTimer()` on each.

`void DismissBottom()`
Dismisses the bubble at index 0 (the newest/bottom bubble) with exit animation. After animation completes, the bubble is removed from the list. **No repositioning** — other bubbles stay where they are.

`void DismissAll()`
Triggers exit animation on all bubbles, then clears the internal list. Stops mouth animation immediately.

`void DismissAllScripted()`
Targets only bubbles where `IsScripted == true`. Non-scripted (auto-expiring) bubbles are unaffected.

`void ClearAll()`
Immediately `Destroy()`s all bubble GameObjects with no animation. Stops mouth animation. Called by `ResetSpeech()`, `HandleDeath()`, `HandleIncapacitated()`, and `OnDisable()`.

**Bubble list convention:**
- Index 0 = newest bubble, positioned at the base (Y=0, closest to the character head).
- Index `Count - 1` = oldest bubble, at the highest Y position.
- The bottom bubble (index 0) never shows a separator line. All others do.
- When a bubble is removed (expired or dismissed), the remaining bubbles do **not** reposition — each bubble stays at its absolute Y.

**Mouth controller management:**
The stack tracks `_typingCount` — a reference count of how many bubbles are actively typing. `MouthController.StartTalking()` is called when count rises from 0 to 1. `StopTalking()` is called when it falls back to 0. Individual `SpeechBubbleInstance` objects never call the mouth controller directly.

**Stack lifecycle (OnDisable):**
`OnDisable` calls `ClearAll()` and clears `_nearbyStacks`. This means any scene unload, character despawn, or hibernation automatically cleans up all bubbles. No registration with a central manager is needed.

---

### `SpeechBubbleInstance` (MonoBehaviour)

Spawned as a child of `SpeechBubbleStack`. Each instance is an independent prefab that owns its typing coroutine, voice playback, entrance/exit animations, expiration timer, and separator line.

**Requires:** `CanvasGroup` (used for alpha fade)

**Serialized Fields:**
| Field | Type | Default | Purpose |
|---|---|---|---|
| `_textElement` | `TextMeshProUGUI` | — | Text display |
| `_separatorLine` | `GameObject` | — | White horizontal rule shown between stacked bubbles |
| `_entranceDuration` | `float` | 0.3s | Duration of fade-in + slide-up entrance |
| `_entranceSlideDistance` | `float` | 15 | How far below base position the bubble starts (slides UP to reach base) |
| `_exitDuration` | `float` | 0.3s | Duration of fade-out + slide-up exit |
| `_exitSlideDistance` | `float` | 10 | How far upward the bubble drifts during exit |

**Public Properties:**
| Property | Returns | Description |
|---|---|---|
| `IsTyping` | `bool` | `true` while the typing coroutine is running |
| `IsScripted` | `bool` | `true` for bubbles created via `SetupScripted()` |

**Events:**
| Event | Signature | Fired when |
|---|---|---|
| `OnExpired` | `Action` | Auto-expiring bubble finishes exit animation (after timer + fade) |
| `OnHeightChanged` | `Action` | `RectTransform.rect.height` changes after typing starts (text wrap detection) |
| `OnTypingStateChanged` | `Action<bool>` | Typing starts (`true`) or stops (`false`) — used by stack to track `_typingCount` |

All event delegates are set to `null` in `OnDestroy` to prevent stale subscriber leaks.

**Public Methods:**

`void Setup(string message, AudioSource audioSource, VoiceSO voiceSO, float pitch, float typingSpeed, float duration, Action onExpired)`
Configures a standard auto-expiring bubble. Chain: entrance animation → typing coroutine → expiration timer → exit animation → `Destroy`. `onExpired` callback fires after the exit animation completes.

`void SetupScripted(string message, AudioSource audioSource, VoiceSO voiceSO, float pitch, float typingSpeed, Action onTypingFinished)`
Configures a persistent bubble. Chain: entrance animation → typing coroutine → `onTypingFinished` callback. No expiration timer. Bubble persists until `Dismiss()` is called externally.

`void CompleteTypingImmediately()`
Stops the typing coroutine. Sets `maxVisibleCharacters` to full message length (the full text string was already assigned at typing start — only visibility changes). Fires `OnTypingStateChanged(false)` and `CheckHeightChanged()`. For scripted bubbles: fires `onTypingFinished`. For non-scripted: starts the expiration timer.

`void ResetExpirationTimer()`
Restarts the expiration timer from the full `_duration`. Called when a nearby character pushes a bubble, keeping this bubble visible during the conversation. Has no effect on scripted bubbles or bubbles that haven't started expiring yet.

`void Dismiss(Action onComplete = null)`
Stops typing and expiration coroutines. Plays exit animation. On animation end: invokes `onComplete`, then calls `Destroy(gameObject)`. Safe to call multiple times — the expiration coroutine is stopped first to prevent double-dismiss.

`float GetHeight()`
Returns `canvas.GetComponent<RectTransform>().rect.height * transform.localScale.y`. Uses the canvas RectTransform (not the root RectTransform) for the full bubble height, then multiplies by root `localScale.y` to convert canvas-local units to the stack's world-space coordinate system.

`void SetTargetPosition(Vector3 localPos)`
Updates `_targetPosition`. The `Update()` loop lerps `transform.localPosition` toward this target every frame using `8f * Time.unscaledDeltaTime`.

`Vector3 GetTargetPosition()`
Returns the current `_targetPosition` the bubble is lerping toward. Used by `SpeechBubbleStack.PushAllBubblesUpBy()` to compute the new pushed position.

`void SetSeparatorVisible(bool visible)`
Shows or hides the `_separatorLine` GameObject. Controlled entirely by `SpeechBubbleStack.UpdateSeparatorVisibility()`.

**Typing implementation:**
The full message string is assigned to `_textElement.text` immediately at typing start, and `_textElement.ForceMeshUpdate()` is called so `ContentSizeFitter` computes the final frame size right away. Characters are revealed progressively via `maxVisibleCharacters`. This ensures the bubble occupies its full final height from the first frame, so the push height calculation is accurate.

**Coroutine cleanup:**
`OnDisable` stops all three coroutines (`_typeRoutine`, `_animRoutine`, `_expirationRoutine`) to prevent dangling coroutines when the GameObject is deactivated or destroyed mid-flight.

---

## Data Flow

### `Say()` — Auto-Expiring Bubble

```
1. CharacterSpeech.Say(message, duration, typingSpeed)
2.   → RPC routing (Server / Owner / local fallback)
3.   → ExecuteSayLocally() → _speechBubbleStack.PushBubble(...)
4.     → Enforce cap: if _bubbles.Count >= _maxBubbles, force-dismiss oldest bubble (last index)
5.     → CompleteTypingImmediately() on current index-0 bubble if it is still typing
6.     → Instantiate SpeechBubbleInstance, insert at index 0
7.     → instance.SetTargetPosition(Vector3.zero)  — new bubble always at base
8.     → Subscribe: instance.OnTypingStateChanged → OnTypingStateChanged
9.     → pushHeight = instance.GetHeight() + _separatorSpacing
10.    → PushAllBubblesUp(pushHeight, instance):
          - shifts all own existing bubbles (index 1+) upward
          - calls PushAllBubblesUpBy(pushHeight) on every nearby stack with active bubbles
11.    → UpdateSeparatorVisibility()
12.    → SpeechBubbleInstance plays entrance animation (fade in + slide UP from below)
13.    → TypeMessage coroutine runs:
          - assigns full text, calls ForceMeshUpdate (sets final size immediately)
          - reveals characters via maxVisibleCharacters
          - fires OnTypingStateChanged(true) at start
14.    → Typing completes → OnTypingStateChanged(false); ExpirationTimer coroutine starts
15.    → WaitForSecondsRealtime(duration)
16.    → Dismiss(): exit animation (fade out + slide up), then Destroy(gameObject)
17.    → OnExpired / _onExpiredCallback fires → RemoveBubble()
18.    → RemoveBubble: removes from list, calls UpdateSeparatorVisibility()
         NOTE: No RecalculatePositions — remaining bubbles stay at their current Y positions
```

### `SayScripted()` — Persistent Bubble

```
1. CharacterSpeech.SayScripted(message, typingSpeed, onTypingFinished)
2.   → RPC routing (Owner also executes locally; ClientRpc has guard to skip if Client Owner)
3.   → ExecuteSayScriptedLocally() → _speechBubbleStack.PushScriptedBubble(...)
4.   → Same push steps as Say() steps 4–11
5.   → SpeechBubbleInstance plays entrance animation
6.   → TypeMessage runs, completes, fires onTypingFinished callback (local only)
7.   → Bubble persists indefinitely
8.   → Dialogue system calls CharacterSpeech.CloseSpeech() to advance
9.   → _speechBubbleStack.DismissBottom() → index-0 bubble plays exit animation
10.  → After animation: RemoveBubble() — no repositioning
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

### Cross-Character Collision Avoidance (Habbo Hotel)

```
1. SpeechBubbleStack.Awake() sets up SphereCollider (trigger, radius=_speechZoneRadius) + Rigidbody (kinematic)
2. Physics layer 14 (SpeechZone) — collider only detects other SpeechZone colliders
3. OnTriggerEnter: adds the other SpeechBubbleStack to _nearbyStacks
4. OnTriggerExit:  removes from _nearbyStacks (also cleaned of nulls on each push)
5. When PushBubble() or PushScriptedBubble() is called:
   a. New bubble is placed at Y=0 (base)
   b. pushHeight = newBubble.GetHeight() + _separatorSpacing
   c. Own existing bubbles (index 1+) are shifted upward by pushHeight
   d. For each nearby stack with HasActiveBubbles:
       → stack.PushAllBubblesUpBy(pushHeight)
       → Each of that stack's bubbles: target Y += pushHeight, ResetExpirationTimer()
6. The pushed offset is permanent — bubbles never slide back down
7. Gap is left when a bubble expires — empty vertical space, no separator line
```

---

## Animation Details

All animations use `Time.unscaledDeltaTime` to remain functional during game pause or at high `GameSpeedController` scales (CLAUDE.md rule 26).

| Phase | Alpha | Y Position | Curve | Duration | Configurable |
|---|---|---|---|---|---|
| Entrance | 0 → 1 | startPos - _entranceSlideDistance → target (slides UP) | EaseOut: `1 - (1-t)²` | 0.3s | `_entranceDuration`, `_entranceSlideDistance` |
| Exit | 1 → 0 | current → current + _exitSlideDistance (drifts UP) | EaseIn: `t²` | 0.3s | `_exitDuration`, `_exitSlideDistance` |
| Reposition | — | current → target | `Lerp(pos, target, 8f * unscaledDeltaTime)` | continuous | — |

**Entrance direction:** Bubbles slide UP from below (start at target Y minus `_entranceSlideDistance`). This gives the impression of rising speech.

**Reposition:** Runs in `SpeechBubbleInstance.Update()`. The lerp coefficient (8) produces a fast initial snap with smooth deceleration. Convergence threshold is `sqrMagnitude < 0.001f` to stop updating once settled.

**Separator line:** A white horizontal `Image`, ~60% of parent width. Shown on all bubbles except index 0 (the bottom/newest). Visibility is re-evaluated on every `PushBubble`, `RemoveBubble`, and `UpdateSeparatorVisibility()` call.

---

## Network Considerations

- No changes to the RPC structure from the pre-stacking system.
- `SpeechBubbleInstance` prefabs are **not NetworkObjects** — they are pure local UI.
- Stack state is never synchronized. Each client independently builds its own stack from incoming RPCs.
- `onTypingFinished` callbacks in `SayScripted` are local-only and are not transmitted over the network. Remotes receive `null` for this callback.
- Cross-character collision avoidance is entirely local — each client runs its own physics trigger detection independently.
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
| `SphereCollider` + `Rigidbody` | Physics | Required on `SpeechBubbleStack` for trigger-based cross-character detection |
| Physics Layer 14 (SpeechZone) | Project Setting | Collider layer — only detects other SpeechZone colliders |

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

The `ContentSizeFitter` on `Text_Speech` is used for layout calculation. The actual reveal is done via `maxVisibleCharacters` — the full text and final height are established at frame 0 of typing so that push height calculations are accurate.

---

## Character Prefab Setup

The speech anchor is a child of the Character root at local position (0, 9, 0).

Required components on the anchor:
- `SpeechBubbleStack` — with `_bubbleInstancePrefab` assigned
- `SphereCollider` (auto-required by SpeechBubbleStack)
- `Rigidbody` (auto-required by SpeechBubbleStack; configured kinematic in Awake)
- `Billboard` — so all bubbles automatically face the camera

The SpeechBubbleStack GameObject must be on **Physics Layer 14 (SpeechZone)** so trigger detection only fires between speech stacks.

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

`SpeechBubbleStack.OnDisable()` calls `ClearAll()` and clears `_nearbyStacks`. This is the single cleanup path for all despawn scenarios — no additional teardown is needed. There is no central registry to unregister from.

---

## Edge Cases and Known Limitations

**Late-joining clients:** Clients joining mid-conversation miss prior RPCs and see no existing bubbles. Speech is ephemeral UI, not replicated game state. This is intentional.

**Bubble cap (max 5):** When `_bubbles.Count >= _maxBubbles`, the oldest bubble (last index) is force-dismissed before the new one is pushed. The oldest bubble's `UnsubscribeEvents` is called before `Dismiss()` to prevent its `OnExpired` callback from triggering a second `RemoveBubble`.

**No gap closing:** When any bubble expires or is dismissed, the remaining bubbles stay exactly where they are. Empty vertical space is left behind. This is intentional Habbo Hotel behavior — the visual history of who spoke when is preserved spatially.

**Expiration timer reset on push:** When a nearby character pushes a bubble, `PushAllBubblesUpBy()` calls `ResetExpirationTimer()` on each pushed bubble. This prevents a bubble that was about to expire from disappearing mid-conversation while still in view.

**Bubble height for push calculation:** `GetHeight()` uses `canvas.rect.height * transform.localScale.y`. Because `TypeMessage` calls `ForceMeshUpdate()` immediately with the full text, the canvas has already computed its final layout height by the time `GetHeight()` is called for the push. This gives an accurate height rather than the prefab default.

**Characters moving out of range:** When a character moves beyond `_speechZoneRadius`, `OnTriggerExit` fires and the stack is removed from `_nearbyStacks`. Future pushes no longer affect it. Previously applied push offsets on that stack persist — they are fire-and-forget.

**Null cleanup in _nearbyStacks:** Before iterating `_nearbyStacks` on each push, `RemoveWhere(s => s == null)` is called to discard destroyed references.

**Object pooling:** Not implemented. If GC pressure becomes measurable in towns with many active speakers, add a pool of ~5 instances per stack as a follow-up.

**`DismissBottom()` on mixed stacks:** Always targets index 0, regardless of whether it is scripted or auto-expiring. `DismissAllScripted()` should be used when a dialogue ends while ambient `Say()` bubbles may still be present.

**Double-execution guard in `SayScriptedClientRpc`:** A Client Owner calls `ExecuteSayScriptedLocally` before sending the `ServerRpc`. The server then broadcasts `SayScriptedClientRpc` to all clients including the Owner. The guard `if (IsOwner && !IsServer) return;` prevents the Owner from executing the bubble push a second time.

**`ResetSpeech()` does not send a dedicated RPC.** It piggybacks on `CloseSpeech()`'s RPC with `_isResetting = true`. Since `ClearAll()` is purely visual, this is intentional — remotes do not need to mirror the reset; they will handle their own `OnDisable` cleanup via the stack lifecycle.
