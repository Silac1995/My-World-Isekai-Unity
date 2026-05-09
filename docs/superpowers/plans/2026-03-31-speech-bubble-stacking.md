# Speech Bubble Stacking & Animation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the single-bubble speech system with animated, stackable speech bubbles that support cross-character collision avoidance (Habbo Hotel style).

**Architecture:** Three new components (`SpeechBubbleInstance`, `SpeechBubbleStack`, `SpeechZoneManager`) replace `Speech.cs` and `ScriptedSpeech.cs`. `CharacterSpeech.cs` is refactored internally but keeps its public API unchanged — no callers need updating. A new prefab (`SpeechBubbleInstance_Prefab`) is created via MCP. All stacking/animation is client-local visual behavior; RPCs unchanged.

**Tech Stack:** Unity 2022 LTS, Unity NGO (Netcode for GameObjects), TextMeshPro, MCP (Unity Editor connection for prefab creation)

**Spec:** `docs/superpowers/specs/2026-03-31-speech-bubble-stacking-design.md`

---

## File Map

| Action | File | Responsibility |
|--------|------|----------------|
| Create | `Assets/Scripts/Character/CharacterSpeech/SpeechBubbleInstance.cs` | Single bubble lifecycle: typing, animation, expiration, voice |
| Create | `Assets/Scripts/Character/CharacterSpeech/SpeechBubbleStack.cs` | Manages list of instances, positioning, mouth controller, cap |
| Create | `Assets/Scripts/Character/CharacterSpeech/SpeechZoneManager.cs` | Cross-character collision avoidance singleton |
| Modify | `Assets/Scripts/Character/CharacterSpeech/CharacterSpeech.cs` | Delegate to stack instead of single prefab |
| Delete | `Assets/Scripts/Character/CharacterSpeech/Speech.cs` | Replaced by SpeechBubbleInstance |
| Delete | `Assets/Scripts/Character/CharacterSpeech/ScriptedSpeech.cs` | Replaced by SpeechBubbleInstance |
| Create | `Assets/Prefabs/SpeechBubbleInstance_Prefab.prefab` | Via MCP — spawnable bubble instance |
| Modify | `Assets/Prefabs/Character/Character_Default.prefab` | Update speech references via MCP |
| Modify | `Assets/Prefabs/Character/Character_Default_Humanoid.prefab` | Update speech references via MCP |
| Modify | `Assets/Prefabs/Character/Character_Default_Quadruped.prefab` | Update speech references via MCP |
| Create | `.agent/skills/speech-system/SKILL.md` | System documentation per CLAUDE.md rule 28 |

---

## Task 1: Create `SpeechBubbleInstance.cs`

**Files:**
- Create: `Assets/Scripts/Character/CharacterSpeech/SpeechBubbleInstance.cs`
- Reference: `Assets/Scripts/Character/CharacterSpeech/Speech.cs` (extract typing logic from here)
- Reference: `Assets/Resources/Data/Sounds/Voices/VoiceSO.cs` (voice playback API)

This is the core building block. Each spawned bubble is one instance. It handles its own typing animation, voice playback, fade/slide entrance/exit, expiration timer, and height tracking. Extracted from the current `Speech.cs` typing coroutine.

- [ ] **Step 1: Read the current `Speech.cs` to understand the typing coroutine logic**

Read `Assets/Scripts/Character/CharacterSpeech/Speech.cs` and `Assets/Resources/Data/Sounds/Voices/VoiceSO.cs`. The typing logic (time accumulator, letter-by-letter, voice every 3rd char) must be preserved exactly.

- [ ] **Step 2: Write `SpeechBubbleInstance.cs`**

Create the file at `Assets/Scripts/Character/CharacterSpeech/SpeechBubbleInstance.cs`.

Key implementation details:
- `[RequireComponent(typeof(CanvasGroup))]` on the class
- Serialized fields: `_textElement` (TextMeshProUGUI), `_separatorLine` (GameObject), `_entranceDuration = 0.3f`, `_exitDuration = 0.3f`
- Private fields: `_canvasGroup`, `_rectTransform`, `_typeRoutine`, `_animRoutine`, `_expirationRoutine`, `_targetPosition`, `_cachedHeight`, `_isScripted`
- `Setup(message, audioSource, voiceSO, pitch, typingSpeed, duration, Action onExpired)` — stores params, starts entrance animation coroutine, then typing coroutine. On typing complete, starts expiration timer coroutine (`WaitForSecondsRealtime(duration)` → calls `Dismiss()` → fires `onExpired`)
- `SetupScripted(message, audioSource, voiceSO, pitch, typingSpeed, Action onTypingFinished)` — same but `_isScripted = true`, no expiration timer, fires `onTypingFinished` when typing completes
- `CompleteTypingImmediately()` — if `_typeRoutine != null`, stop it, set `_textElement.text = fullMessage`, fire typing-complete callback, fire `OnTypingStateChanged(false)`, check height change
- `Dismiss(Action onComplete = null)` — plays exit animation coroutine (fade out + slide up +10 over 0.3s using `Time.unscaledDeltaTime`), then calls `onComplete`, then `Destroy(gameObject)`
- `GetHeight()` — returns `_rectTransform.rect.height`
- `SetTargetPosition(Vector3 localPos)` — sets `_targetPosition`, lerping happens in `Update()`
- `SetSeparatorVisible(bool visible)` — `_separatorLine.SetActive(visible)`
- `Update()` — lerps `transform.localPosition` toward `_targetPosition` using `Mathf.Lerp` with `8f * Time.unscaledDeltaTime`
- **Typing coroutine** `TypeMessage()` — copied from `Speech.cs` but:
  - Uses `Time.unscaledDeltaTime` (already does in current code)
  - After each character added, check if `_rectTransform.rect.height != _cachedHeight` → fire `OnHeightChanged`, update cache
  - Fires `OnTypingStateChanged(true)` at start, `OnTypingStateChanged(false)` at end
- **Entrance animation** coroutine: set `_canvasGroup.alpha = 0`, `localPosition.y += 15`, lerp to alpha=1 and target Y over 0.3s with EaseOut, using `Time.unscaledDeltaTime`
- **Exit animation** coroutine: lerp `_canvasGroup.alpha` to 0 and `localPosition.y += 10` over 0.3s with EaseIn, using `Time.unscaledDeltaTime`
- `OnDisable()` — stop all coroutines, null them out
- `OnDestroy()` — null all callbacks (`OnExpired`, `OnHeightChanged`, `OnTypingStateChanged`) to prevent stale references (per CLAUDE.md rule 16)
- Events: `Action OnExpired`, `Action OnHeightChanged`, `Action<bool> OnTypingStateChanged`
- `public bool IsTyping => _typeRoutine != null`
- `public bool IsScripted => _isScripted`
- Store `_fullMessage` for use in `CompleteTypingImmediately()`

- [ ] **Step 3: Refresh assets to trigger compilation**

Use MCP `assets-refresh` tool. Check `console-get-logs` for compilation errors. Fix any errors.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Character/CharacterSpeech/SpeechBubbleInstance.cs
git commit -m "feat(speech): add SpeechBubbleInstance — per-bubble lifecycle with typing, animation, expiration"
```

---

## Task 2: Create `SpeechBubbleStack.cs`

**Files:**
- Create: `Assets/Scripts/Character/CharacterSpeech/SpeechBubbleStack.cs`

The stack manages all active bubble instances for one character. It handles spawning, positioning, bubble cap, mouth controller, separator visibility, and cross-character offset.

- [ ] **Step 1: Write `SpeechBubbleStack.cs`**

Create at `Assets/Scripts/Character/CharacterSpeech/SpeechBubbleStack.cs`.

Key implementation details:
- **Important**: `SpeechBubbleStack` is a plain `MonoBehaviour`, NOT a `NetworkBehaviour` or `CharacterSystem`. It is purely local visual management that happens to live on a networked prefab hierarchy — this is fine.
- Serialized fields: `_bubbleInstancePrefab` (SpeechBubbleInstance), `_maxBubbles = 5`, `_separatorSpacing = 0.5f` (world units between separator and next bubble), `_maxCrossCharacterOffset = 350f` (5 bubble heights approx)
- Private fields: `_bubbles` (List<SpeechBubbleInstance>), `_crossCharacterOffset` (float), `_mouthController` (MouthController), `_typingCount` (int — number of currently-typing bubbles)
- `Transform OwnerRoot { get; private set; }` — set via `Init()`
- `Init(Transform ownerRoot, MouthController mouthController)` — called by CharacterSpeech during setup. Stores references.
- `PushBubble(message, duration, typingSpeed, audioSource, voiceSO, pitch)`:
  1. If `_bubbles.Count >= _maxBubbles`, force-dismiss oldest (`_bubbles[_bubbles.Count - 1].Dismiss(...)` and remove from list)
  2. If `_bubbles.Count > 0 && _bubbles[0].IsTyping`, call `_bubbles[0].CompleteTypingImmediately()`
  3. Instantiate `_bubbleInstancePrefab` as child of this transform
  4. Call instance `Setup(message, audioSource, voiceSO, pitch, typingSpeed, duration, onExpired: () => OnBubbleExpired(instance))`
  5. Subscribe to instance's `OnHeightChanged += RecalculatePositions`, `OnTypingStateChanged += OnTypingStateChanged`
  6. Insert at index 0 of `_bubbles`
  7. Update separator visibility (index 0 hidden, all others visible)
  8. Call `RecalculatePositions()`
  9. Notify `SpeechZoneManager.Instance?.NotifyBubblePushed(this, instance.GetHeight())`
- `PushScriptedBubble(message, typingSpeed, audioSource, voiceSO, pitch, Action onTypingFinished)`:
  Same as above but calls `instance.SetupScripted(...)` instead. Also notifies SpeechZoneManager.
- `DismissBottom()`:
  If `_bubbles.Count > 0`, call `_bubbles[0].Dismiss(() => { RemoveBubble(_bubbles[0]); })`
- `DismissAll()`:
  Take a copy of the list (`var toRemove = new List<SpeechBubbleInstance>(_bubbles)`). Clear `_bubbles` immediately. For each bubble in `toRemove`, unsubscribe events and call `Dismiss()` (which self-destructs after exit animation). Since `_bubbles` is already cleared, the `OnBubbleExpired` callbacks won't try to remove from a stale list. Reset `_typingCount = 0`, call `_mouthController?.StopTalking()`.
- `DismissAllScripted()`:
  Filter `_bubbles.Where(b => b.IsScripted)` and dismiss each.
- `ClearAll()`:
  For each bubble, `Destroy(bubble.gameObject)` immediately. Clear list. Reset `_crossCharacterOffset = 0`. Reset `_typingCount = 0`. Call `_mouthController?.StopTalking()`.
- `AddCrossCharacterOffset(float height)`:
  `_crossCharacterOffset = Mathf.Min(_crossCharacterOffset + height, _maxCrossCharacterOffset)`. Call `RecalculatePositions()`.
- `RecalculatePositions()`:
  Starting from index 0 (newest/bottom): `targetY = _crossCharacterOffset`. For each bubble at index i: `_bubbles[i].SetTargetPosition(new Vector3(0, targetY, 0))`. Then `targetY += _bubbles[i].GetHeight() + _separatorSpacing`.
- `OnBubbleExpired(SpeechBubbleInstance instance)`:
  Remove from `_bubbles`, unsubscribe events. Update separators. Recalculate positions.
- `RemoveBubble(SpeechBubbleInstance instance)`:
  Same as OnBubbleExpired — shared removal logic.
- `OnTypingStateChanged(bool isTyping)`:
  If `isTyping`: `_typingCount++`. If `_typingCount == 1`, call `_mouthController?.StartTalking()`.
  If `!isTyping`: `_typingCount--`. If `_typingCount <= 0`, call `_mouthController?.StopTalking()`. Clamp to 0.
- `bool IsAnyTyping => _typingCount > 0`
- `bool HasActiveBubbles => _bubbles.Count > 0`
- `float GetTotalStackHeight()` — sum of all bubble heights + separators + `_crossCharacterOffset`
- `OnEnable()` — `SpeechZoneManager.Instance?.RegisterStack(this)`
- `OnDisable()` — `ClearAll()`. `SpeechZoneManager.Instance?.UnregisterStack(this)`

- [ ] **Step 2: Refresh assets and check compilation**

Use MCP `assets-refresh`. Check `console-get-logs` for errors. Fix any.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Character/CharacterSpeech/SpeechBubbleStack.cs
git commit -m "feat(speech): add SpeechBubbleStack — multi-bubble positioning, cap, mouth control"
```

---

## Task 3: Create `SpeechZoneManager.cs`

**Files:**
- Create: `Assets/Scripts/Character/CharacterSpeech/SpeechZoneManager.cs`

Lightweight singleton that coordinates cross-character bubble collision avoidance.

- [ ] **Step 1: Write `SpeechZoneManager.cs`**

Create at `Assets/Scripts/Character/CharacterSpeech/SpeechZoneManager.cs`.

Key implementation details:
- Lazy singleton pattern: `private static SpeechZoneManager _instance`. `public static SpeechZoneManager Instance` getter: if `_instance == null`, create a new GameObject("SpeechZoneManager") and AddComponent. `Awake()` sets `_instance = this` synchronously (which happens inside `AddComponent` before the getter returns). Return `_instance`. Do NOT use `DontDestroyOnLoad` — scene-level lifecycle. **Important**: `Awake()` MUST set `_instance` before any other method is called, since `SpeechBubbleStack.OnEnable()` calls `Instance.RegisterStack()` early in lifecycle.
- `[SerializeField] private float _speechZoneRadius = 15f`
- `private HashSet<SpeechBubbleStack> _stacks = new()`
- `RegisterStack(SpeechBubbleStack stack)` — `_stacks.Add(stack)`
- `UnregisterStack(SpeechBubbleStack stack)` — `_stacks.Remove(stack)`
- `NotifyBubblePushed(SpeechBubbleStack source, float bubbleHeight)`:
  1. If `source.OwnerRoot == null` return (safety)
  2. `Vector3 sourcePos = source.OwnerRoot.position`
  3. Iterate `_stacks`. For each stack != source:
     - Skip if `!stack.HasActiveBubbles`
     - Skip if `stack.OwnerRoot == null`
     - XZ distance: `Vector2 diff = new(stack.OwnerRoot.position.x - sourcePos.x, stack.OwnerRoot.position.z - sourcePos.z)`. If `diff.magnitude <= _speechZoneRadius`: call `stack.AddCrossCharacterOffset(bubbleHeight)`
  4. Wrap in try/catch (defensive coding per CLAUDE.md rule 30)
- `OnDestroy()` — `if (_instance == this) _instance = null`. Clear `_stacks`.

- [ ] **Step 2: Refresh assets and check compilation**

Use MCP `assets-refresh`. Check `console-get-logs`.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Character/CharacterSpeech/SpeechZoneManager.cs
git commit -m "feat(speech): add SpeechZoneManager — cross-character bubble collision avoidance"
```

---

## Task 4: Refactor `CharacterSpeech.cs`

**Files:**
- Modify: `Assets/Scripts/Character/CharacterSpeech/CharacterSpeech.cs`

Refactor internals to delegate to `SpeechBubbleStack` while keeping all public API signatures and RPC structure unchanged. All 20+ callers continue working without modification.

- [ ] **Step 1: Read the current `CharacterSpeech.cs` fully**

Read `Assets/Scripts/Character/CharacterSpeech/CharacterSpeech.cs` to understand every method and field.

- [ ] **Step 2: Rewrite `CharacterSpeech.cs`**

Changes to make:
- **Remove fields**: `_speechBubblePrefab` (GameObject), `_scriptedSpeech` (ScriptedSpeech), `_hideCoroutine` (Coroutine)
- **Add fields**: `[SerializeField] private SpeechBubbleStack _speechBubbleStack`, `private bool _isResetting`
- **Keep fields**: `_bodyPartsController`, `_audioSource`, `_voiceSO`, `_voicePitch`
- Note: the `_bubbleInstancePrefab` reference lives on `SpeechBubbleStack` only (serialized there, wired in the prefab). `CharacterSpeech` does NOT hold a duplicate reference.
- **`Start()`**: Keep pitch randomization. Remove `_speechBubblePrefab.SetActive(false)`. Add: `_speechBubbleStack?.Init(_character.transform, _bodyPartsController?.MouthController)`. Set `_speechBubbleStack._bubbleInstancePrefab` if needed (or pass via Init).
- **`OnDisable()`**: Remove `_hideCoroutine` stop. Add `base.OnDisable()` call (current code is missing this — `CharacterSystem.OnDisable()` unsubscribes from character events). The stack handles its own cleanup via its own `OnDisable()`.
- **`IsTyping`**: Change to `_speechBubbleStack != null && _speechBubbleStack.IsAnyTyping`
- **`IsSpeaking`**: Change to `_speechBubbleStack != null && _speechBubbleStack.HasActiveBubbles`
- **`Say()`, `SayServerRpc()`, `SayClientRpc()`**: Keep RPC structure identical. Keep the `if IsServer / else if IsOwner / else` branching. RPCs still call `ExecuteSayLocally()`.
- **`ExecuteSayLocally()`**: Replace body with:
  ```csharp
  try
  {
      if (_speechBubbleStack == null) { Debug.LogError(...); return; }
      _speechBubbleStack.PushBubble(message, duration, typingSpeed, _audioSource, _voiceSO, _voicePitch);
  }
  catch (Exception e) { Debug.LogError(...); }
  ```
  Remove all `_speechBubblePrefab`, `_bodyPartsController.MouthController` calls (stack handles mouth now).
- **`SayScripted()`, `SayScriptedServerRpc()`, `SayScriptedClientRpc()`**: Keep RPC structure. RPCs still call `ExecuteSayScriptedLocally()`.
- **`ExecuteSayScriptedLocally()`**: Replace body with:
  ```csharp
  if (_speechBubbleStack == null) return;
  _speechBubbleStack.PushScriptedBubble(message, typingSpeed, _audioSource, _voiceSO, _voicePitch, onTypingFinished);
  ```
- **`CloseSpeech()`, `CloseSpeechServerRpc()`, `CloseSpeechClientRpc()`**: Keep RPC structure.
- **`ExecuteCloseSpeechLocally()`**: Replace body with:
  ```csharp
  _speechBubbleStack?.DismissBottom();
  ```
- **`ResetSpeech()`**: Keep calling `CloseSpeech()` (which goes through the RPC chain to propagate to all clients). This preserves the current networking behavior — all callers (e.g., `DialogueManager.EndDialogue()`) expect `ResetSpeech()` to sync across clients. `CloseSpeech()` now calls `DismissAll()` instead of `DismissBottom()` when called via `ResetSpeech`. To distinguish, add a `private bool _isResetting` flag: set true before `CloseSpeech()`, checked in `ExecuteCloseSpeechLocally()` to decide `DismissAll()` vs `DismissBottom()`, reset after.
  ```csharp
  public void ResetSpeech()
  {
      _isResetting = true;
      CloseSpeech();
      _isResetting = false;
  }
  ```
  In `ExecuteCloseSpeechLocally()`:
  ```csharp
  if (_isResetting) _speechBubbleStack?.ClearAll();
  else _speechBubbleStack?.DismissBottom();
  ```
- **Remove `HideSpeechAfterDelay()`** coroutine entirely — expiration is per-bubble now.
- **Add death/incapacitation overrides**:
  ```csharp
  protected override void HandleDeath(Character character) => _speechBubbleStack?.ClearAll();
  protected override void HandleIncapacitated(Character character) => _speechBubbleStack?.ClearAll();
  ```

- [ ] **Step 3: Refresh assets and check compilation**

Use MCP `assets-refresh`. Check `console-get-logs`. Fix any compilation errors.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Character/CharacterSpeech/CharacterSpeech.cs
git commit -m "refactor(speech): delegate CharacterSpeech to SpeechBubbleStack, preserve public API and RPCs"
```

---

## Task 5: Create `SpeechBubbleInstance_Prefab` via MCP

**Files:**
- Create: `Assets/Prefabs/SpeechBubbleInstance_Prefab.prefab` (via MCP)
- Reference: `Assets/Prefabs/SpeechBubble_Prefab.prefab` (existing — copy settings from here)

Use the MCP Unity Editor connection to create the new prefab with the correct hierarchy and component setup.

- [ ] **Step 1: Inspect the existing `SpeechBubble_Prefab` via MCP**

Use `assets-find` to locate `SpeechBubble_Prefab`. Use `assets-get-data` to read its full structure — Canvas settings, TextMeshProUGUI settings, Billboard settings, transform position. We need to match the Canvas/TMP config exactly.

- [ ] **Step 2: Create root GameObject in scene for prefab assembly**

Use MCP `gameobject-create` to create a temporary root GameObject named "SpeechBubbleInstance" in the active scene. Set position to (0, 0, 0).

- [ ] **Step 3: Add CanvasGroup to root**

Use `gameobject-component-add` to add `CanvasGroup` to the root. Set alpha=1 (default). This will be manipulated by the entrance/exit animations.

- [ ] **Step 4: Add SpeechBubbleInstance script to root**

Use `gameobject-component-add` to add the `SpeechBubbleInstance` component. (The script must be compiled first from Task 1.)

- [ ] **Step 5: Create SeparatorLine child**

Use `gameobject-create` to create a child named "SeparatorLine" under the root. Add `RectTransform`, `CanvasRenderer`, and `UnityEngine.UI.Image` components. Configure:
- Image color: white (1,1,1,1)
- RectTransform: anchors centered, width = 180 (60% of 300), height = 1
- Set `gameObject.SetActive(false)` (disabled by default per spec)

- [ ] **Step 6: Create Canvas child**

Use `gameobject-create` to create a child named "Canvas" under the root. Add `Canvas` (renderMode = WorldSpace), `CanvasScaler` (referenceResolution = 800x600), `GraphicRaycaster`. Match the settings from the existing `SpeechBubble_Prefab`.
- RectTransform size: 300x70

- [ ] **Step 7: Create Text_Speech child under Canvas**

Use `gameobject-create` to create "Text_Speech" under Canvas. Add `TextMeshProUGUI` with:
- Font size: 36
- Color: white (1,1,1,1)
- Alignment: center, top
- Word wrap enabled
- Add `ContentSizeFitter` with verticalFit = PreferredSize
Match all settings from the existing prefab's Text_Speech.

- [ ] **Step 8: Wire SpeechBubbleInstance serialized references**

Use `gameobject-component-modify` on the root's `SpeechBubbleInstance` to wire:
- `_textElement` → reference to Text_Speech's TextMeshProUGUI
- `_separatorLine` → reference to SeparatorLine GameObject

- [ ] **Step 9: Save as prefab**

Use `assets-prefab-create` to save the assembled GameObject as a prefab at `Assets/Prefabs/SpeechBubbleInstance_Prefab.prefab`.

- [ ] **Step 10: Clean up temporary scene object**

Use `gameobject-destroy` to remove the temporary assembly GameObject from the scene.

- [ ] **Step 11: Commit**

```bash
git add Assets/Prefabs/SpeechBubbleInstance_Prefab.prefab Assets/Prefabs/SpeechBubbleInstance_Prefab.prefab.meta
git commit -m "feat(speech): create SpeechBubbleInstance prefab with CanvasGroup, separator, TMP text"
```

---

## Task 6: Update Character Prefabs via MCP

**Files:**
- Modify: `Assets/Prefabs/Character/Character_Default.prefab`
- Modify: `Assets/Prefabs/Character/Character_Default_Humanoid.prefab`
- Modify: `Assets/Prefabs/Character/Character_Default_Quadruped.prefab`

Update each character prefab to use the new stack system. For each prefab:

- [ ] **Step 1: Open Character_Default prefab in edit mode**

Use `assets-prefab-open` to enter prefab edit mode.

- [ ] **Step 2: Find the existing SpeechBubble child**

Use `gameobject-find` to locate the child with the `Speech` component (currently the speech bubble anchor). Note its transform position (should be ~(0,9,0)).

- [ ] **Step 3: Add `SpeechBubbleStack` component to the speech anchor**

Use `gameobject-component-add` to add `SpeechBubbleStack` to the same GameObject that currently has the Billboard. Configure `_bubbleInstancePrefab` to reference the `SpeechBubbleInstance_Prefab.prefab` from Task 5. Set `_maxBubbles = 5`.

- [ ] **Step 4: Update CharacterSpeech serialized fields**

Use `gameobject-component-modify` on the CharacterSpeech component (on the CharacterSpeech child GameObject):
- Set `_speechBubbleStack` to reference the SpeechBubbleStack component added in step 3
- Clear `_speechBubblePrefab` (old field — will be removed from code)
- Clear `_scriptedSpeech` (old field — will be removed)

Note: `_bubbleInstancePrefab` is wired on the `SpeechBubbleStack` component (step 3), NOT on CharacterSpeech.

- [ ] **Step 5: Remove old Speech and ScriptedSpeech components from the prefab**

Use `gameobject-component-destroy` to remove the `Speech` component from the speech bubble child. Also remove `ScriptedSpeech` if present.

- [ ] **Step 6: Save and close prefab**

Use `assets-prefab-save` then `assets-prefab-close`.

- [ ] **Step 7: Repeat steps 1-6 for Character_Default_Humanoid**

- [ ] **Step 8: Repeat steps 1-6 for Character_Default_Quadruped**

Note: Quadruped may have different speech anchor position. Use `gameobject-find` to locate it and preserve its existing transform.

- [ ] **Step 9: Commit**

```bash
git add Assets/Prefabs/Character/
git commit -m "refactor(speech): update character prefabs to use SpeechBubbleStack system"
```

---

## Task 7: Delete Old Speech Files

**Files:**
- Delete: `Assets/Scripts/Character/CharacterSpeech/Speech.cs`
- Delete: `Assets/Scripts/Character/CharacterSpeech/ScriptedSpeech.cs`

- [ ] **Step 1: Delete `Speech.cs` and `ScriptedSpeech.cs`**

Use MCP `script-delete` to remove both files. This triggers asset refresh and recompilation.

- [ ] **Step 2: Check compilation**

Use `console-get-logs` to verify no compilation errors. The only references to these classes should have been in `CharacterSpeech.cs` (already refactored in Task 4) and the prefabs (already updated in Task 6).

If there are errors, read the error messages and fix any remaining references.

- [ ] **Step 3: Commit**

```bash
git add -A Assets/Scripts/Character/CharacterSpeech/Speech.cs Assets/Scripts/Character/CharacterSpeech/ScriptedSpeech.cs
git commit -m "chore(speech): remove deprecated Speech.cs and ScriptedSpeech.cs"
```

---

## Task 8: Integration Testing in Play Mode

**Files:**
- No file changes — manual play mode testing via MCP

- [ ] **Step 1: Enter play mode**

Use MCP `editor-application-set-state` to enter play mode.

- [ ] **Step 2: Test single Say() bubble**

Use MCP `script-execute` to find a character and call `Say()`:
```csharp
var character = GameObject.FindObjectOfType<Character>();
character.CharacterSpeech.Say("Hello world!", 5f, 0.04f);
```
Verify via `console-get-logs` that no errors occur. Take a `screenshot-game-view` to check visual.

- [ ] **Step 3: Test stacking — rapid Say() calls**

```csharp
var character = GameObject.FindObjectOfType<Character>();
character.CharacterSpeech.Say("First message", 8f, 0.04f);
// wait a moment then:
character.CharacterSpeech.Say("Second message", 8f, 0.04f);
character.CharacterSpeech.Say("Third message", 8f, 0.04f);
```
Verify: first message auto-completes typing when second arrives. Bubbles stack upward. Separator lines visible between same-character bubbles.

- [ ] **Step 4: Test expiration and gap closing**

Wait for individual bubbles to expire. Verify remaining bubbles slide down to close the gap smoothly.

- [ ] **Step 5: Test SayScripted() — persistent bubble**

```csharp
var character = GameObject.FindObjectOfType<Character>();
character.CharacterSpeech.SayScripted("Scripted line 1", 0.04f, () => Debug.Log("Typing done"));
```
Verify bubble stays after typing completes (no auto-expire). Then call:
```csharp
character.CharacterSpeech.CloseSpeech();
```
Verify bubble dismisses with fade animation.

- [ ] **Step 6: Test bubble cap**

Push 6+ bubbles rapidly. Verify oldest is force-dismissed when cap (5) is reached.

- [ ] **Step 7: Test cross-character collision**

Find two characters within 15 units. Make both speak. Verify the second character's bubble pushes the first character's stack up with empty space (no separator). Verify the offset persists after the pushing bubble expires.

- [ ] **Step 8: Test ClearAll on exit play mode**

Exit play mode. Re-enter. Verify no leftover state.

- [ ] **Step 9: Check console for errors**

Use `console-get-logs` to scan for any errors or warnings during the test session. Fix any issues found.

- [ ] **Step 10: Commit any fixes**

If fixes were needed, commit them:
```bash
git add Assets/Scripts/Character/CharacterSpeech/
git commit -m "fix(speech): address issues found during integration testing"
```

---

## Task 9: Delete Old `SpeechBubble_Prefab`

**Files:**
- Delete: `Assets/Prefabs/SpeechBubble_Prefab.prefab`

Only do this after Task 8 confirms everything works.

- [ ] **Step 1: Verify no remaining references to the old prefab**

Search the project for any remaining references to `SpeechBubble_Prefab`. Use MCP `assets-find` or grep the codebase.

- [ ] **Step 2: Delete the old prefab**

Use MCP `assets-delete` to remove `Assets/Prefabs/SpeechBubble_Prefab.prefab`.

- [ ] **Step 3: Refresh and check**

Use `assets-refresh`. Verify no missing reference errors in `console-get-logs`.

- [ ] **Step 4: Commit**

```bash
git add -A Assets/Prefabs/SpeechBubble_Prefab.prefab
git commit -m "chore(speech): remove old SpeechBubble_Prefab — replaced by SpeechBubbleInstance_Prefab"
```

---

## Task 10: Write SKILL.md Documentation

**Files:**
- Create: `.agent/skills/speech-system/SKILL.md`

Per CLAUDE.md rule 28, every system must have a SKILL.md.

- [ ] **Step 1: Write the skill documentation**

Create `.agent/skills/speech-system/SKILL.md` covering:
- System purpose and overview
- Component descriptions: SpeechBubbleInstance, SpeechBubbleStack, SpeechZoneManager, CharacterSpeech
- Public API for each component
- Events and callbacks
- Data flow for Say(), SayScripted(), CloseSpeech()
- Cross-character collision avoidance behavior
- Animation details (entrance, exit, reposition)
- Network considerations (client-local, RPCs unchanged)
- Dependencies (TextMeshPro, CanvasGroup, VoiceSO, MouthController, Billboard)
- Integration points (DialogueManager, UI_ChatBar, interaction system, AI behavior trees)
- Edge cases (bubble cap, death cleanup, late joiners, offset accumulation)

- [ ] **Step 2: Commit**

```bash
git add .agent/skills/speech-system/SKILL.md
git commit -m "docs(speech): add SKILL.md for speech bubble stacking system"
```

---

## Dependency Order

```
Task 1 (SpeechBubbleInstance.cs)
  ↓
Task 2 (SpeechBubbleStack.cs) ← depends on Task 1
  ↓
Task 3 (SpeechZoneManager.cs) ← depends on Task 2
  ↓
Task 4 (Refactor CharacterSpeech.cs) ← depends on Tasks 1-3
  ↓
Task 5 (Create prefab via MCP) ← depends on Task 1 (script must compile)
  ↓
Task 6 (Update character prefabs) ← depends on Tasks 4-5
  ↓
Task 7 (Delete old Speech files) ← depends on Tasks 4, 6
  ↓
Task 8 (Integration testing) ← depends on all above
  ↓
Task 9 (Delete old prefab) ← depends on Task 8
  ↓
Task 10 (SKILL.md) ← can run anytime, but best after Task 8
```

Note: Tasks 1-3 could potentially be implemented in parallel by separate agents since they compile independently, but Task 2 references Task 1's type and Task 3 references Task 2's type — so sequential is safer.
