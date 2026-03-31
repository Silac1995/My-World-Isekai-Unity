# Speech Bubble Stacking & Animation Design

**Date:** 2026-03-31  
**Status:** Approved  
**Scope:** CharacterSpeech system — animation transitions + multi-bubble stacking

## Problem

The current speech system has three issues:
1. **No animation** — bubbles appear/disappear instantly via `SetActive(true/false)`.
2. **Override behavior** — when a character says multiple lines, the latest message replaces the previous one. There is no visual history of what was said.
3. **Cross-character overlap** — when multiple nearby characters speak, their bubbles overlap each other with no collision avoidance.

## Requirements

1. **Smooth transitions**: Bubbles fade in + slide up on appear. Fade out + slide up on disappear.
2. **Stacking**: Multiple bubbles stack vertically. Newest bubble appears at the base (closest to character). Older bubbles are pushed upward.
3. **Auto-complete typing**: When a new bubble arrives, any still-typing previous bubble instantly completes its text.
4. **Separator lines**: A centered white line (~60% width) separates stacked bubbles.
5. **Independent expiration**: Each `Say()` bubble has its own duration timer. When it expires, it fades out and remaining bubbles slide down to close the gap.
6. **Scripted dialog support**: `SayScripted()` bubbles stack the same way but do not auto-expire. They persist until explicitly dismissed (player click to advance). On dismiss, fade out, gap closes, next scripted line is pushed by the dialogue system.
7. **Network parity**: RPC structure unchanged. All visual stacking is local-only (each client manages its own bubble stack).
8. **Bubble cap**: Maximum 5 bubbles visible at once. When exceeded, the oldest bubble is force-dismissed before the new one is pushed.
9. **Death/despawn cleanup**: All bubbles are cleared immediately (no animation) when the character dies, is incapacitated, or is despawned.
10. **Mixed stacking**: `Say()` and `SayScripted()` bubbles can coexist in the same stack. `DismissBottom()` always targets the bottom-most bubble regardless of type. `DismissAllScripted()` targets only scripted bubbles (for dialogue end cleanup).
11. **Cross-character collision avoidance (Habbo Hotel style)**: When characters within a 15 world-unit radius both have active speech bubbles, a new bubble from one character pushes the other character's entire stack upward with empty space (no separator line). Pushed bubbles stay at their pushed position — they do not slide back when the pushing bubble expires.

## Architecture

### New Components

#### `SpeechBubbleInstance` (MonoBehaviour)
Lives on each spawned bubble prefab instance. Manages its own lifecycle:

- **Text display**: Owns a `TextMeshProUGUI` reference and typing coroutine (extracted from current `Speech.cs` logic).
- **Voice playback**: Plays voice clips during typing (same logic as current `Speech.TypeMessage`).
- **Animation**: Uses a `CanvasGroup` for alpha fade + local position offset for slide. Entrance: fade in (0→1) + slide up (offset → 0) over ~0.3s. Exit: fade out (1→0) + slide up (0 → -offset) over ~0.3s.
- **Expiration**: For `Say()` bubbles, starts a timer after typing completes. On expiry, plays exit animation then calls `OnExpired` callback.
- **Separator line**: An `Image` element at the top of the bubble. Enabled/disabled by the stack manager based on position.
- **Public API**:
  - `Setup(message, audioSource, voiceSO, pitch, typingSpeed, duration, onExpired)` — for auto-expiring bubbles
  - `SetupScripted(message, audioSource, voiceSO, pitch, typingSpeed, onTypingFinished)` — for scripted bubbles (no auto-expire)
  - `CompleteTypingImmediately()` — instantly fills all remaining text, stops voice, fires typing-complete callback
  - `Dismiss()` — plays exit animation, then destroys self
  - `GetHeight()` — returns current RectTransform height (for stack offset calculation)
  - `SetTargetPosition(Vector3 localPos)` — smoothly lerps to target position (used by stack for repositioning)
  - `SetSeparatorVisible(bool visible)` — shows/hides the top separator line
- **Events/Callbacks**:
  - `Action OnExpired` — fired when auto-expire timer finishes exit animation
  - `Action OnHeightChanged` — fired when ContentSizeFitter changes height (text wraps), so stack can reposition. Implementation: after each typed character, compare `RectTransform.rect.height` to cached value; fire if changed. Also fires once after `CompleteTypingImmediately()`.
  - `Action OnTypingStateChanged(bool isTyping)` — fired when typing starts or stops, so stack can manage MouthController

#### `SpeechBubbleStack` (MonoBehaviour)
Lives on the speech bubble anchor point (child of Character). Manages all active bubble instances:

- **Bubble list**: `List<SpeechBubbleInstance>` ordered bottom-to-top (index 0 = newest/bottom).
- **Spawning**: Instantiates `SpeechBubbleInstance` prefab as children of its transform.
- **Positioning**: Newest bubble at local position (0,0,0). Each older bubble offset upward by cumulative height of all bubbles below it + separator spacing.
- **Repositioning animation**: When a bubble is removed (expired or dismissed), remaining bubbles smoothly lerp to their new target positions.
- **Auto-complete**: On `PushBubble()`, calls `CompleteTypingImmediately()` on the current bottom bubble.
- **Separator management**: Bottom-most bubble has separator hidden. All others have it visible.
- **Bubble cap**: `[SerializeField] private int _maxBubbles = 5`. When exceeded, oldest bubble is force-dismissed before new one is pushed.
- **MouthController management**: Mouth is talking whenever ANY bubble in the stack is currently typing. `StartTalking()` on first typing bubble, `StopTalking()` only when zero bubbles are typing. Individual instances do NOT control the mouth — the stack does.
- **Public API**:
  - `PushBubble(message, duration, typingSpeed, audioSource, voiceSO, pitch)` — spawns auto-expiring bubble
  - `PushScriptedBubble(message, typingSpeed, audioSource, voiceSO, pitch, onTypingFinished)` — spawns persistent bubble
  - `DismissBottom()` — dismisses the bottom-most bubble (used for scripted advance)
  - `DismissAll()` — dismisses all bubbles with animation
  - `DismissAllScripted()` — dismisses only scripted (non-expiring) bubbles
  - `ClearAll()` — immediately destroys all bubbles (no animation, for cleanup/death/despawn)
  - `bool IsAnyTyping` — true if any bubble in the stack is currently typing
  - `bool HasActiveBubbles` — true if any bubbles exist in the stack
  - `AddCrossCharacterOffset(float height)` — increments `_crossCharacterOffset` (a single additive float) to push the entire stack upward. Reset to 0 on `ClearAll()`. No separator line for cross-character offsets.
  - `float GetTotalStackHeight()` — returns the total height of all bubbles + separators + `_crossCharacterOffset`. Used by `SpeechZoneManager` to calculate push amounts.
  - `Transform OwnerRoot` — reference to the root Character transform (set on init). Used by `SpeechZoneManager` for distance calculations.
- **Billboard**: The stack anchor retains the `Billboard` component so all bubbles face the camera together.

#### `SpeechZoneManager` (Singleton MonoBehaviour)
Manages cross-character bubble collision avoidance. Tracks all active `SpeechBubbleStack` instances and coordinates vertical spacing between nearby characters.

- **Registry**: `HashSet<SpeechBubbleStack>` of all active stacks. Stacks register on `OnEnable()`, unregister on `OnDisable()`.
- **Speech zone radius**: `[SerializeField] private float _speechZoneRadius = 15f` (world-space distance).
- **Cross-character push**: When a stack pushes a new bubble (either `PushBubble` or `PushScriptedBubble`), it notifies the `SpeechZoneManager`. The manager finds all other stacks within `_speechZoneRadius` and calls `AddCrossCharacterOffset(float height)` on each nearby stack. This inserts an empty vertical spacer (no separator line) that pushes the entire stack upward.
- **Distance calculation**: Uses the **root Character transform position** (not the stack anchor's position), to avoid the Y=9 anchor offset inflating distances. Calculated in XZ plane only (2D sprites in 3D world).
- **Self-exclusion**: The source stack is always excluded from the push — a character does not push its own stack.
- **No slide-back**: Cross-character offsets are fire-and-forget. When the pushing bubble expires, the offset on the other character's stack remains. Offsets only reset when the *affected* stack itself calls `ClearAll()` (death, despawn, hibernation).
- **Public API**:
  - `RegisterStack(SpeechBubbleStack stack)` / `UnregisterStack(SpeechBubbleStack stack)`
  - `NotifyBubblePushed(SpeechBubbleStack source, float bubbleHeight)` — called by a stack when it spawns any new bubble (Say or Scripted). Finds nearby stacks and applies cross-character offset.
- **Performance**: Only iterates registered stacks on push (not per-frame). Stacks with no active bubbles are skipped.
- **Lifecycle**: Lazy singleton (`Instance` property). Lives as a scene-level object, not `DontDestroyOnLoad` — each map has its own instance, which naturally resets cross-character state on map transitions.

#### `CharacterSpeech` (Refactored)
Minimal changes to public API. Internal delegation changes:

- Replaces `_speechBubblePrefab` (single GameObject) with `_speechBubbleStack` (SpeechBubbleStack reference) and `_speechBubbleInstancePrefab` (prefab to spawn).
- `ExecuteSayLocally()` → calls `_speechBubbleStack.PushBubble(...)` instead of toggling a single prefab.
- `ExecuteSayScriptedLocally()` → calls `_speechBubbleStack.PushScriptedBubble(...)`.
- `ExecuteCloseSpeechLocally()` → calls `_speechBubbleStack.DismissBottom()` for scripted advance, `_speechBubbleStack.DismissAll()` for full cleanup.
- `IsSpeaking` → true if stack has any active bubbles. `IsTyping` → delegates to `_speechBubbleStack.IsAnyTyping`.
- `ResetSpeech()` → calls `_speechBubbleStack.ClearAll()` (immediate, no animation — used for dialogue end, death, despawn).
- `_bodyPartsController` reference retained — but mouth control delegated to stack (see MouthController management above). `CharacterSpeech` passes the mouth controller reference to the stack on init.
- RPC structure unchanged.
- `_hideCoroutine` removed — expiration is now per-bubble, managed by `SpeechBubbleInstance`.

#### `Speech.cs` — Deprecated/Removed
Its typing logic moves into `SpeechBubbleInstance`. `ScriptedSpeech.cs` is also removed — the scripted vs auto-expire distinction is handled by `SpeechBubbleInstance.Setup()` vs `SetupScripted()`.

### Prefab: `SpeechBubbleInstance_Prefab`

```
SpeechBubbleInstance (Root)
├── CanvasGroup (for fade animation)
├── SpeechBubbleInstance (script)
│
├── SeparatorLine (Image, white, centered, 60% parent width, 1px height)
│   └── Disabled by default
│
└── Canvas (WorldSpace, 300x70)
    ├── CanvasScaler (800x600 reference)
    └── Text_Speech (TextMeshProUGUI)
        ├── ContentSizeFitter (VerticalFit: PreferredSize)
        ├── Font: Default TMP, 36pt, white, center-aligned
        └── Word wrap enabled
```

### Character Prefab Changes

- Remove old `SpeechBubble_Prefab` child reference from CharacterSpeech.
- Add `SpeechBubbleStack` component on the speech anchor child GameObject (position 0,9,0 — same as current bubble position).
- `Billboard` component stays on the stack anchor.
- `CharacterSpeech` serialized fields change:
  - Remove: `_speechBubblePrefab` (GameObject)
  - Add: `_speechBubbleStack` (SpeechBubbleStack), `_bubbleInstancePrefab` (SpeechBubbleInstance prefab reference)

## Data Flow

### Say() — Auto-Expiring Bubble
1. `CharacterSpeech.Say()` → RPC (unchanged) → `ExecuteSayLocally()`
2. `ExecuteSayLocally()` → `_speechBubbleStack.PushBubble(message, duration, typingSpeed, ...)`
3. Stack calls `CompleteTypingImmediately()` on current bottom bubble (if typing)
4. Stack instantiates `SpeechBubbleInstance`, inserts at index 0
5. Stack repositions all older bubbles upward (animated lerp)
6. New instance plays entrance animation (fade in + slide up)
7. Instance starts typing coroutine with voice playback
8. Typing completes → expiration timer starts (duration parameter)
9. Timer expires → instance plays exit animation → fires `OnExpired`
10. Stack removes instance from list → repositions remaining bubbles downward

### SayScripted() — Persistent Bubble
1. `CharacterSpeech.SayScripted()` → RPC (unchanged) → `ExecuteSayScriptedLocally()`
2. `ExecuteSayScriptedLocally()` → `_speechBubbleStack.PushScriptedBubble(message, typingSpeed, ..., onTypingFinished)`
3. Same stacking behavior as Say() but no expiration timer
4. Bubble persists until `CloseSpeech()` → `_speechBubbleStack.DismissBottom()`
5. Dialogue system calls next `SayScripted()` line → cycle repeats

### Cross-Character Collision (Habbo Hotel Style)
1. `SpeechBubbleStack.PushBubble()` or `PushScriptedBubble()` spawns a new bubble at base position
2. Stack calls `SpeechZoneManager.Instance.NotifyBubblePushed(this, newBubbleHeight)`
3. Manager iterates all registered stacks (excluding source), finds those within 15 units using `OwnerRoot` position (XZ distance)
4. For each nearby stack that has active bubbles: calls `nearbyStack.AddCrossCharacterOffset(newBubbleHeight)`
5. Nearby stack increments `_crossCharacterOffset` by the new bubble's height, pushing all its bubbles upward (animated lerp)
6. The offset is permanent (fire-and-forget) — it does not get removed when the source bubble expires
7. Visual result: empty gap between characters' bubbles (no white separator line)
8. **Bubble height for offset**: uses the prefab's default height (not post-layout height). Acceptable approximation — exact text-wrapped height is not available until end of frame.

### Bubble Removal & Gap Closing
1. Any bubble (expired or dismissed) plays exit animation (fade out + slide up, ~0.3s)
2. On animation complete → `Destroy(gameObject)`
3. Stack removes it from list
4. Stack recalculates positions for all remaining bubbles
5. Remaining bubbles lerp to new positions (smooth slide down)

## Animation Details

- **Entrance**: CanvasGroup alpha 0→1 + localPosition.y offset (+15 units) → 0 over 0.3s, EaseOut
- **Exit**: CanvasGroup alpha 1→0 + localPosition.y offset 0 → (+10 units) over 0.3s, EaseIn (bubble drifts upward and fades, remaining bubbles slide down into the gap)
- **Reposition**: `Mathf.Lerp(current, target, 8f * Time.unscaledDeltaTime)` — fast initial movement, smooth deceleration regardless of distance
- All animations use `Time.unscaledDeltaTime` (UI must work during game pause per CLAUDE.md rule 26)

## Network Considerations

- No changes to RPC structure. All stacking/animation is client-local visual behavior.
- `SpeechBubbleInstance` prefab is NOT a NetworkObject — it's local UI spawned by each client independently.
- The stack state is not synchronized — each client builds its own stack from received RPCs.

## Edge Cases & Known Limitations

- **Late-joining clients**: A client joining mid-conversation will miss prior RPCs and see no existing bubbles. This matches current behavior and is acceptable — speech is ephemeral UI, not game state.
- **Character death/incapacitation**: `CharacterSpeech` should override `HandleDeath()` / `HandleIncapacitated()` to call `_speechBubbleStack.ClearAll()`.
- **Character despawn (hibernation)**: `OnDisable()` / `OnDestroy()` on `SpeechBubbleStack` calls `ClearAll()` to prevent orphaned instances.
- **Object pooling**: Not in v1. If GC pressure becomes measurable in towns with many NPCs, add a simple pool of ~5 instances per stack as a follow-up.
- **Cross-character offset accumulation**: In busy towns with many NPCs talking, cross-character offsets can accumulate. Mitigated by: per-stack bubble cap (max 5), natural expiration, a `_maxCrossCharacterOffset` cap on the stack (e.g., 5 bubble heights — additional pushes are ignored), and `ClearAll()` resetting offsets on death/despawn/hibernation.
- **Characters moving apart**: If two characters were in speech zone range, exchanged bubbles, then moved apart (>15 units), existing cross-character offsets remain. No dynamic recalculation — offsets are fire-and-forget.

## Migration

- Old `SpeechBubble_Prefab` is replaced by `SpeechBubbleInstance_Prefab` (created via MCP).
- Character prefabs (biped + quadruped) updated to reference new stack component and instance prefab.
- `Speech.cs` and `ScriptedSpeech.cs` removed after migration.
