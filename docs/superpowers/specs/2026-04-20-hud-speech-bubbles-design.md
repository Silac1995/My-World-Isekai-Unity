# HUD Speech Bubbles — Design Spec

**Date:** 2026-04-20
**Topic:** Move speech bubbles from world-space above character heads to a screen-space HUD layer, proximity-gated by the local player character.
**Related system:** `.agent/skills/speech-system/SKILL.md`
**Supersedes behaviours from:** `docs/superpowers/specs/2026-03-31-speech-bubble-stacking-design.md` (stacking logic preserved; rendering layer swapped).

---

## 1. Problem

Today, every `SpeechBubbleInstance` is a WorldSpace Canvas parented to the character's speech anchor (`transform.localPosition = (0, 9, 0)`), kept camera-facing by a `Billboard`. Because they live in world space at full scale, the local player sees speech bubbles from arbitrarily distant characters — there is no distance falloff. This degrades readability and floods the view in towns with multiple speakers.

## 2. Goal

Move speech bubbles onto the local player's HUD (screen-space canvas), positioned via world-to-screen projection over each speaker's speech anchor. Show a given speaker's bubbles only when the local player character is within a proximity radius and the speaker is on-screen.

Explicit non-goals (for this spec):
- Off-screen arrow indicators / edge clamping.
- Per-archetype proximity radii (single global default for now).
- Replacing the Habbo-style cross-character push with something new (kept; just ported to pixels).
- Changing any RPC, dialogue, or voice behaviour.

## 3. Design Defaults (confirmed with user)

| Default | Value |
|---|---|
| Proximity radius | **25** world units |
| Gating mode | **Live** (fades in/out as the local player moves) |
| Proximity origin | Local player **Character transform** (not camera) |
| Off-screen / behind-camera speaker | Bubble wrapper fades to **alpha 0** (no arrow, no clamp) |
| Habbo cross-speaker push | Preserved, units converted **world → HUD pixels** |
| HUD layers per client | **One** per local player |
| Behaviour on character death / incapacitation | **No special handling** — bubbles follow their normal lifecycle (expiration / dismissal / despawn) |

## 4. Architecture

### 4.1 New: `HUDSpeechBubbleLayer` (MonoBehaviour)

Lives under the local player's HUD Canvas (`Screen Space - Overlay` or `Screen Space - Camera`).

Responsibilities:
- Single point of discovery: `public static HUDSpeechBubbleLayer Local { get; private set; }`. Set in `OnEnable`, cleared in `OnDisable`.
- Owns `RectTransform ContentRoot` — parent for every stack's bubble wrapper.
- Owns `Camera Camera` — cached from `Camera.main` lazily; re-resolves if the cached reference becomes null (camera rebind after portal gate, etc.).
- Owns `Transform LocalPlayerAnchor` — the local player's speech anchor (or root transform). Resolved lazily via `NetworkManager.Singleton.LocalClient?.PlayerObject?.GetComponent<Character>()`. Re-resolved when the cached anchor becomes null (respawn, portal-gate return, NPC possession).

### 4.2 Modified: `SpeechBubbleStack` (existing class)

Kept responsibilities:
- `_bubbles` list, `_maxBubbles` cap enforcement.
- Habbo push via world-space SphereCollider trigger + `_nearbyStacks` HashSet.
- RPC-agnostic API (`PushBubble`, `PushScriptedBubble`, `DismissBottom`, `DismissAll`, `DismissAllScripted`, `ClearAll`).
- Mouth controller reference counting (`_typingCount`).
- `OnDisable` → `ClearAll` cleanup path (hibernation, despawn, scene unload).

Changes:
1. **Bubble parenting.** `PushBubble` / `PushScriptedBubble` instantiate under `EnsureStackWrapper().transform` rather than `this.transform`. The wrapper is a child of `HUDSpeechBubbleLayer.Local.ContentRoot`, owned 1-per-stack, carrying a single `CanvasGroup` (shared fade target).
2. **Proximity + off-screen gate.** New `Update()` that, while `_bubbles.Count > 0`:
   - Reads `HUDSpeechBubbleLayer.Local` and its `LocalPlayerAnchor`.
   - If either is null: wrapper alpha → 0 (no visible bubbles).
   - Else: `inRange = (LocalPlayerAnchor.position - transform.position).sqrMagnitude <= _proximityRadius * _proximityRadius`.
   - `anyOnScreen` = OR of each bubble's `_isOffScreen` flag (computed inside the bubble's own `Update`).
   - `targetAlpha = (inRange && anyOnScreen) ? 1f : 0f`.
   - `wrapper.alpha = MoveTowards(current, target, _fadeSpeed * Time.unscaledDeltaTime)`. Default `_fadeSpeed = 4f`.
3. **Push-height units.** `pushHeight = instance.GetHeightPx() + _separatorSpacingPx`. New field `[SerializeField] float _separatorSpacingPx = 4f`. Old `_separatorSpacing` world-unit field removed.
4. **Trigger radius.** `_speechZoneRadius` default bumped 15 → **25** to match `_proximityRadius`. Kept as serialized for per-prefab override.
5. **HUD layer unavailability.** If `HUDSpeechBubbleLayer.Local` is null at `PushBubble` time (NPC speech before HUD is up, or during scene transition), the stack still runs its logical cap / push math; the bubble instance is instantiated under a detached wrapper and its GameObject is toggled inactive until a HUD layer is found. Purely defensive — the common path always has a HUD.

### 4.3 Modified: `SpeechBubbleInstance`

New serialized / runtime fields:
- `Transform _speakerAnchor` — set via `SetSpeakerAnchor(Transform)` immediately after `Setup/SetupScripted`.
- `Camera _camera` — cached from the owning `HUDSpeechBubbleLayer`.
- `Vector2 _stackOffsetPx` — set via `SetStackOffsetPx(Vector2)`. Replaces the old world-space `_targetPosition`.
- `bool _isOffScreen` — refreshed each frame; read by the owning stack.

Per-frame (`Update`):

```csharp
if (_speakerAnchor == null || _camera == null) return;
Vector3 sp = _camera.WorldToScreenPoint(_speakerAnchor.position);
_isOffScreen = sp.z < 0f
            || sp.x < 0f || sp.x > Screen.width
            || sp.y < 0f || sp.y > Screen.height;

Vector2 target = (Vector2)sp + _stackOffsetPx;
_rect.anchoredPosition = Vector2.Lerp(_rect.anchoredPosition, target,
                                      8f * Time.unscaledDeltaTime);
```

Existing preserved behaviour:
- Entrance / exit coroutines, EaseOut / EaseIn curves, typing coroutine with voice playback, `CompleteTypingImmediately`, `ResetExpirationTimer`, `Dismiss`, `OnExpired / OnHeightChanged / OnTypingStateChanged` events, coroutine cleanup in `OnDisable`, delegate nulling in `OnDestroy`.

Rescales (world units → HUD pixels):
- `_entranceSlideDistance`: 15 → **40**
- `_exitSlideDistance`: 10 → **25**

Renames / replacements:
- `GetHeight()` → `GetHeightPx()`: returns `_rect.rect.height` (no `localScale.y` multiplication — children of a screen-space canvas).
- `SetTargetPosition(Vector3)` / `GetTargetPosition()` → `SetStackOffsetPx(Vector2)` / `GetStackOffsetPx()`.

Prefab root changes (see §7).

### 4.4 Modified: `CharacterSpeech`

Only change: remove the `HandleDeath` and `HandleIncapacitated` overrides entirely. Bubbles on a dying character continue their natural lifecycle (expiration timer, dismissal, or `OnDisable → ClearAll` when the character is despawned). Everything else (`Say`, `SayScripted`, `CloseSpeech`, `ResetSpeech`, RPC routing, `VoiceSO` / audio / pitch) untouched.

### 4.5 Removed

- `Billboard` component on the speech anchor GameObject.
- The per-bubble WorldSpace `Canvas` + `CanvasScaler` inside `SpeechBubbleInstance_Prefab`.

### 4.6 Unchanged

- `CharacterSpeech` RPC path (Server / Owner / non-owner fallback) and `SayScriptedClientRpc` double-execution guard.
- `SpeechBubbleStack.Init(Transform ownerRoot, MouthController mouthController)`.
- Mouth controller start/stop via `_typingCount`.
- Separator visibility rules (bottom bubble hidden, others visible).
- DialogueManager integration (`SayScripted` → `CloseSpeech` advance).
- Bubble cap force-dismiss-oldest (`UnsubscribeEvents` before `Dismiss`).
- Late-joining clients see no pre-existing bubbles (ephemeral UI — intentional).
- Physics Layer 14 (SpeechZone) + kinematic Rigidbody on the speech anchor.

## 5. Data Flow

### 5.1 `Say()` / `SayScripted()` — push path

```
CharacterSpeech.Say() / SayScripted()
  → RPC routing (Server / Owner / non-owner)  [UNCHANGED]
  → ExecuteSayLocally / ExecuteSayScriptedLocally
  → _speechBubbleStack.PushBubble / PushScriptedBubble
      1. Enforce cap (force-dismiss oldest if _bubbles.Count >= _maxBubbles)
      2. CompleteTypingImmediately on current newest if still typing
      3. wrapper = EnsureStackWrapper()                   // CanvasGroup child of HUD ContentRoot
      4. instance = Instantiate(_bubbleInstancePrefab, wrapper.transform)
      5. instance.SetSpeakerAnchor(transform)             // stack transform = speech anchor
      6. instance.SetCamera(HUDSpeechBubbleLayer.Local.Camera)
      7. instance.Setup(...) / SetupScripted(...)         // typing, voice, expiration — unchanged
      8. instance.SetStackOffsetPx(Vector2.zero)          // new bubble at base
      9. _bubbles.Insert(0, instance)
     10. pushHeightPx = instance.GetHeightPx() + _separatorSpacingPx
     11. PushAllBubblesUp(pushHeightPx, instance)         // own older + nearby stacks
     12. UpdateSeparatorVisibility()
```

### 5.2 Per-frame runtime

```
SpeechBubbleInstance.Update():
  compute screen position → update _isOffScreen → lerp anchoredPosition toward (screenPos + _stackOffsetPx)

SpeechBubbleStack.Update() (only while _bubbles.Count > 0):
  compute distSq to LocalPlayerAnchor → inRange check (25² = 625)
  anyOnScreen = OR over bubbles' _isOffScreen (inverted)
  targetAlpha = (inRange && anyOnScreen) ? 1f : 0f
  wrapper.alpha = MoveTowards(current, target, _fadeSpeed * unscaledDeltaTime)
```

### 5.3 Habbo push in pixel space

Unchanged structurally; units differ. When `PushBubble` creates a new bubble:
- Own older bubbles (index ≥ 1): `_stackOffsetPx.y += pushHeightPx`.
- For each `stack` in `_nearbyStacks` with `HasActiveBubbles`: `stack.PushAllBubblesUpBy(pushHeightPx)` (same method name — argument is now pixels).
- Pushed bubbles have their expiration timer reset (already current behaviour).

Nearby-stack detection stays world-space. The SphereCollider radius = 25u matches the hearing radius, so two characters that push each other also fall within each other's proximity gate.

### 5.4 Dismissal / reset (unchanged semantics)

- `DismissBottom` → index-0 plays exit animation → `RemoveBubble` (no repositioning).
- `DismissAll` / `DismissAllScripted` / `ClearAll` — identical behaviour, operating on HUD-parented instances.
- `ResetSpeech()` still piggybacks on `CloseSpeech()` with `_isResetting = true`.

## 6. Multiplayer Considerations (CLAUDE.md rule 19)

No networked state added. All new work is per-client visibility.

| Scenario | Behaviour |
|---|---|
| Host speaking | Host's HUD always shows (distance = 0). Remote client shows iff its local player is within 25u of host's character. |
| Client speaking | Symmetric. |
| NPC (server-authoritative) speaking | Each client independently evaluates proximity from its own local player to the NPC. |
| Two clients near the same NPC | Both see the bubble; independent checks. |
| Client respawn / portal-gate return / NPC possession | `HUDSpeechBubbleLayer` re-resolves `LocalPlayerAnchor` when the cached transform becomes null. |
| Late-joining client | No pre-existing bubbles (ephemeral UI — unchanged intent). |
| Character far outside 25u while speaking | Stack's logical state runs normally (push, cap, expiration); wrapper alpha stays at 0 on that client. If the local player walks into range mid-speech, the wrapper fades up and currently-live bubbles become visible. |

Action item (post-implementation): dispatch `network-validator` agent to audit.

## 7. Prefab & Scene Changes

### 7.1 `SpeechBubbleInstance_Prefab`

New structure:

```
SpeechBubbleInstance (RectTransform, CanvasGroup, SpeechBubbleInstance.cs)
  Pivot (0.5, 0) — bubble bottom-center aligns to speaker's screen point.
├── SeparatorLine (Image, white, ~60% width, 1 px)   // disabled by default, anchored top
└── Text_Speech (TextMeshProUGUI + ContentSizeFitter [Vertical: PreferredSize])
    Font / size tuned for HUD readability (~28pt at 1080p reference).
```

Removed: the nested `Canvas (WorldSpace) + CanvasScaler`.

### 7.2 Character prefab — speech anchor GameObject

Before: `SpeechBubbleStack` + `SphereCollider (radius 15)` + `Rigidbody (kinematic)` + `Billboard`.

After: `SpeechBubbleStack` + `SphereCollider (radius 25)` + `Rigidbody (kinematic)`. Billboard removed.

### 7.3 HUD Canvas (local player HUD prefab)

Add under the existing HUD Canvas:

```
HUDSpeechBubbleLayer (RectTransform stretch, HUDSpeechBubbleLayer.cs)
└── ContentRoot (RectTransform stretch)
```

HUD layer script exposes an optional `[SerializeField] Camera _cameraOverride` for cases where `Camera.main` isn't the gameplay camera.

### 7.4 Physics layers

No change — still uses Physics Layer 14 (SpeechZone); the trigger-vs-trigger detection matrix is unchanged.

## 8. Skill Doc & Agent Maintenance

- **Update** `.agent/skills/speech-system/SKILL.md`:
  - Architecture diagram: bubbles parent under `HUDSpeechBubbleLayer.Local.ContentRoot`.
  - New dependency entry: `HUDSpeechBubbleLayer`.
  - Proximity gate section (25u, live, local-player-relative).
  - Off-screen behaviour (wrapper alpha 0).
  - Push units: pixels.
  - Billboard removed from speech anchor.
  - Note that `CharacterSpeech` no longer overrides `HandleDeath` / `HandleIncapacitated`.
- **Update** `.claude/agents/character-system-specialist.md`: add a one-liner referencing the new HUD layer + pixel-space stacking.
- **No new specialized agent** — this is a visual rework of an existing domain, not a new subsystem.

## 9. Testing Plan (manual, Play Mode)

| # | Scenario | Expected |
|---|---|---|
| 1 | Solo — walk toward an idle-speaking NPC | Bubble wrapper fades in as the local player crosses the 25u boundary; fades out on exit |
| 2 | Solo — NPC speaks directly behind the camera | Wrapper alpha stays at 0 until the player turns to face |
| 3 | Solo — two NPCs conversing close together | Habbo push still separates their stacks vertically in HUD pixel space |
| 4 | Solo — push 6 rapid lines (bubble cap) | Oldest force-dismissed; no NRE from `UnsubscribeEvents` / `Dismiss` double-path |
| 5 | Solo — scripted dialogue via `DialogueManager` | `SayScripted` / `CloseSpeech` cycle advances lines correctly |
| 6 | Host + Client — host speaks | Host HUD shows bubble; client HUD shows iff within 25u of host |
| 7 | Host + Client — client speaks | Symmetric of #6 |
| 8 | Host + NPC — NPC near host but far from client | Host sees bubble; client doesn't |
| 9 | Portal-gate return | After character re-spawn, proximity gate still works (`LocalPlayerAnchor` re-resolved) |
| 10 | Character death | Bubbles follow their normal lifecycle (expiration / dismissal). No forced clear. On full despawn, `SpeechBubbleStack.OnDisable → ClearAll` cleans up. |
| 11 | `ResetSpeech()` at dialogue end | All bubbles cleared instantly, no animation residue |
| 12 | Player walks out of range mid-speech | Wrapper fades to 0 smoothly within ~0.25s; logical stack state intact |
| 13 | Player walks back into range before expiration | Wrapper fades back to 1; current bubbles visible at their current pushed offsets |

## 10. Risks & Open Questions

- **Resolution / DPI** — HUD pixel units scale with the player's screen. If the HUD Canvas uses a `CanvasScaler` (recommended: `Scale With Screen Size`, reference 1920×1080), bubble sizes and push heights will auto-scale. Verify during testing.
- **World-to-screen cost at scale** — with N speaking NPCs on-map, we do N × M bubble `WorldToScreenPoint` calls per frame, where M ≤ `_maxBubbles`. Cheap; no concern for typical town density. Keep an eye on profiler if >30 simultaneous speakers.
- **Camera rebind edge cases** — if the active camera switches mid-speech (cinematic, etc.), `HUDSpeechBubbleLayer.Camera` must re-resolve. Lazy resolve on null handles the common case; cinematic systems that disable `Camera.main` and activate another may need to explicitly set `_cameraOverride`.
- **Billboard removal** — check whether any other system queries the Billboard on the speech anchor. Grep before deletion.

## 11. Implementation Summary

Files touched:

| File | Change |
|---|---|
| `Assets/Scripts/Character/CharacterSpeech/CharacterSpeech.cs` | Remove `HandleDeath` / `HandleIncapacitated` overrides. |
| `Assets/Scripts/Character/CharacterSpeech/SpeechBubbleStack.cs` | Add wrapper + HUD parenting, proximity `Update`, pixel-space `_separatorSpacingPx`, trigger radius 25. |
| `Assets/Scripts/Character/CharacterSpeech/SpeechBubbleInstance.cs` | Add `_speakerAnchor` / `_camera` / `_stackOffsetPx`, screen-space `Update`, pixel slide distances, `GetHeightPx()`. |
| `Assets/Scripts/UI/HUDSpeechBubbleLayer.cs` *(new)* | HUD layer with static `Local` accessor, camera + local-player-anchor lazy resolution. |
| `Assets/Prefabs/SpeechBubbleInstance_Prefab.prefab` | Remove WorldSpace Canvas; restructure as UI RectTransform; tune fonts/sizes. |
| `Assets/Prefabs/Character/Character_Default.prefab` | Remove Billboard on speech anchor; bump SphereCollider radius 15 → 25. Apply to all Character prefab variants. |
| HUD Canvas prefab *(existing local-player HUD)* | Add `HUDSpeechBubbleLayer` + `ContentRoot` children. |
| `.agent/skills/speech-system/SKILL.md` | Update per §8. |
| `.claude/agents/character-system-specialist.md` | One-liner reference update. |

## 12. Out-of-Scope (follow-ups)

- Off-screen arrow / edge indicator for speakers just outside the frustum.
- Per-archetype proximity radius overrides.
- Bubble object pooling (deferred; still acceptable per the original stacking spec).
- Optional "quiet" mode that hides non-scripted ambient bubbles while keeping dialogue bubbles.
