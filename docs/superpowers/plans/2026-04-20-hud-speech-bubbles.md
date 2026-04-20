# HUD Speech Bubbles Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move speech bubbles from world-space above character heads to the local player's HUD, positioned via world-to-screen projection over each speaker's head and shown only when the local player character is within 25 world units.

**Architecture:** Keep `SpeechBubbleStack` / `SpeechBubbleInstance` / `CharacterSpeech` as the logical owners (list, cap, Habbo push, cross-stack trigger, RPC routing — all unchanged). Add a new `HUDSpeechBubbleLayer` MonoBehaviour on the player HUD canvas that provides a static `Local` accessor + `ContentRoot` transform. Bubbles instantiate under a per-stack `CanvasGroup` wrapper inside `ContentRoot`; each bubble does `Camera.WorldToScreenPoint(speakerAnchor.position)` per frame and lerps its `anchoredPosition`. Each stack fades its wrapper based on proximity and on-screen status.

**Tech Stack:** Unity 2D sprites in 3D environment, Unity Netcode for GameObjects (NGO), TextMeshPro, Unity MCP for prefab edits.

**Spec:** [docs/superpowers/specs/2026-04-20-hud-speech-bubbles-design.md](../specs/2026-04-20-hud-speech-bubbles-design.md)

**Project Rules:** [CLAUDE.md](../../../CLAUDE.md) — especially rule 19 (multiplayer validation), rule 26 (use `Time.unscaledDeltaTime` for UI), rule 28 (update SKILL.md on any change), rule 32 (11 Unity units ≈ 1.67m).

---

## File Structure

| File | Action | Responsibility |
|---|---|---|
| `Assets/Scripts/UI/HUDSpeechBubbleLayer.cs` | **Create** | Static `Local` registry; `ContentRoot`, `Camera`, `LocalPlayerAnchor` lazy resolution |
| `Assets/Scripts/Character/CharacterSpeech/SpeechBubbleInstance.cs` | **Modify** | Screen-space positioning; pixel slide distances; `_speakerAnchor`/`_camera`/`_stackOffsetPx`; `GetHeightPx` |
| `Assets/Scripts/Character/CharacterSpeech/SpeechBubbleStack.cs` | **Modify** | HUD wrapper creation, proximity + off-screen fade, pixel push heights, trigger radius 25 |
| `Assets/Scripts/Character/CharacterSpeech/CharacterSpeech.cs` | **Modify** | Remove `HandleDeath` / `HandleIncapacitated` overrides |
| `Assets/Prefabs/SpeechBubbleInstance_Prefab.prefab` | **Modify** | Root → RectTransform + CanvasGroup + script; remove nested WorldSpace Canvas |
| `Assets/UI/Player HUD/UI_PlayerHUD.prefab` | **Modify** | Add `HUDSpeechBubbleLayer` + `ContentRoot` children |
| `Assets/Prefabs/Character/Character_Default.prefab` | **Modify** | Remove Billboard on speech anchor; bump SphereCollider radius 15 → 25 |
| `Assets/Prefabs/Character/Character_Default_Humanoid.prefab` | **Modify** | Same as above |
| `Assets/Prefabs/Character/Character_Default_Quadruped.prefab` | **Modify** | Same as above |
| `.agent/skills/speech-system/SKILL.md` | **Modify** | Document HUD rendering, proximity gate, pixel push |
| `.claude/agents/character-system-specialist.md` | **Modify** | One-liner about new HUD dependency |

**Testing:** No Unity EditMode test assembly exists in this project. Verification is done via **compile (domain reload), prefab inspection through Unity MCP, and Play Mode manual test scenarios**. All visual/animation behaviour is manually validated — no automated assertions.

---

## Task 0: Pre-flight — Baseline & Branch

**Files:** (none)

- [ ] **Step 1: Confirm clean working tree**

Run: `git status`
Expected: `On branch multiplayyer` with no uncommitted changes (the spec commits from brainstorming already landed).

- [ ] **Step 2: Confirm starting state compiles in Unity**

Use MCP: `mcp__ai-game-developer__console-get-logs`
Expected: no recent compile errors. If there are pre-existing errors, stop and resolve them before starting — this plan must start from a clean baseline so any new errors are provably from our changes.

- [ ] **Step 3: Record three file line counts for later sanity-check**

Run:
```bash
wc -l "Assets/Scripts/Character/CharacterSpeech/CharacterSpeech.cs" \
      "Assets/Scripts/Character/CharacterSpeech/SpeechBubbleStack.cs" \
      "Assets/Scripts/Character/CharacterSpeech/SpeechBubbleInstance.cs"
```
Write the numbers down. CharacterSpeech should shrink by ~2 lines; the other two will grow.

---

## Task 1: Create `HUDSpeechBubbleLayer.cs`

**Files:**
- Create: `Assets/Scripts/UI/HUDSpeechBubbleLayer.cs`

- [ ] **Step 1: Write the new script**

Use `mcp__ai-game-developer__script-update-or-create` with the following content, **exactly**:

```csharp
using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Single HUD anchor for all speech bubbles on the local player's screen.
/// Bubbles from every character's SpeechBubbleStack parent under ContentRoot and
/// position themselves via Camera.WorldToScreenPoint each frame.
///
/// Lifecycle: lives on the local player's HUD Canvas prefab. OnEnable registers
/// itself as HUDSpeechBubbleLayer.Local; OnDisable clears the static. Camera and
/// LocalPlayerAnchor are resolved lazily and re-resolved whenever the cached
/// reference becomes null (portal-gate return, character respawn, camera rebind).
/// </summary>
public class HUDSpeechBubbleLayer : MonoBehaviour
{
    public static HUDSpeechBubbleLayer Local { get; private set; }

    [SerializeField] private RectTransform _contentRoot;
    [Tooltip("Optional explicit camera override. If null, resolves Camera.main lazily.")]
    [SerializeField] private Camera _cameraOverride;

    private Camera _cachedCamera;
    private Transform _cachedLocalPlayerAnchor;

    public RectTransform ContentRoot => _contentRoot;

    public Camera Camera
    {
        get
        {
            if (_cameraOverride != null) return _cameraOverride;
            if (_cachedCamera == null) _cachedCamera = Camera.main;
            return _cachedCamera;
        }
    }

    /// <summary>
    /// Resolves the local player's speech anchor transform. Returns null while no
    /// local player Character exists yet (session boot, scene transition).
    /// </summary>
    public Transform LocalPlayerAnchor
    {
        get
        {
            if (_cachedLocalPlayerAnchor != null) return _cachedLocalPlayerAnchor;
            try
            {
                var nm = NetworkManager.Singleton;
                if (nm == null || nm.LocalClient == null) return null;
                var playerObj = nm.LocalClient.PlayerObject;
                if (playerObj == null) return null;
                var character = playerObj.GetComponent<Character>();
                if (character == null) return null;
                _cachedLocalPlayerAnchor = character.transform;
                return _cachedLocalPlayerAnchor;
            }
            catch (Exception e)
            {
                Debug.LogError($"[HUDSpeechBubbleLayer] LocalPlayerAnchor resolve failed: {e.Message}");
                return null;
            }
        }
    }

    private void OnEnable()
    {
        if (Local != null && Local != this)
        {
            Debug.LogWarning($"[HUDSpeechBubbleLayer] A second instance enabled on '{gameObject.name}'. Replacing previous.");
        }
        Local = this;
    }

    private void OnDisable()
    {
        if (Local == this) Local = null;
        _cachedCamera = null;
        _cachedLocalPlayerAnchor = null;
    }
}
```

- [ ] **Step 2: Force Unity recompile and check for errors**

Use MCP: `mcp__ai-game-developer__assets-refresh`
Then: `mcp__ai-game-developer__console-get-logs`
Expected: no compile errors related to `HUDSpeechBubbleLayer`. If `Character` or `NetworkManager` namespaces need imports adjusted, correct them based on the error message.

- [ ] **Step 3: Commit**

```bash
git add "Assets/Scripts/UI/HUDSpeechBubbleLayer.cs" "Assets/Scripts/UI/HUDSpeechBubbleLayer.cs.meta"
git commit -m "feat(speech): add HUDSpeechBubbleLayer for screen-space speech rendering"
```

---

## Task 2: Wire `HUDSpeechBubbleLayer` into `UI_PlayerHUD.prefab`

**Files:**
- Modify: `Assets/UI/Player HUD/UI_PlayerHUD.prefab`

- [ ] **Step 1: Open the HUD prefab**

Use MCP: `mcp__ai-game-developer__assets-find` with filter `UI_PlayerHUD t:Prefab`
Then: `mcp__ai-game-developer__assets-prefab-open` with the resolved path.

- [ ] **Step 2: Inspect the root Canvas**

Use MCP: `mcp__ai-game-developer__gameobject-find` with `name: UI_PlayerHUD`, `includeChildrenDepth: 1`.
Verify there is a Canvas component. Note its `renderMode`. If not `Screen Space - Overlay` or `Screen Space - Camera`, abort and ask the human — we need a screen-space canvas as the parent for the HUD layer.

- [ ] **Step 3: Create the `HUDSpeechBubbleLayer` GameObject as a child of the root**

Use MCP: `mcp__ai-game-developer__gameobject-create` with parent = the HUD prefab root, name `HUDSpeechBubbleLayer`.
Add a `RectTransform` sized to stretch fill (anchors min (0,0), max (1,1); offset 0/0/0/0). In practice this is the default for UI children under a Canvas.

- [ ] **Step 4: Add the `HUDSpeechBubbleLayer` component**

Use MCP: `mcp__ai-game-developer__gameobject-component-add` with `typeName: HUDSpeechBubbleLayer`.

- [ ] **Step 5: Create the `ContentRoot` child GameObject**

Use MCP: `mcp__ai-game-developer__gameobject-create` with parent = the `HUDSpeechBubbleLayer` GameObject, name `ContentRoot`.
Same full-stretch RectTransform.

- [ ] **Step 6: Assign `ContentRoot` to the script field**

Use MCP: `mcp__ai-game-developer__gameobject-component-modify` on the HUDSpeechBubbleLayer component, setting `_contentRoot` to a reference pointing at the `ContentRoot` RectTransform.

- [ ] **Step 7: Save the prefab**

Use MCP: `mcp__ai-game-developer__assets-prefab-save`, then `mcp__ai-game-developer__assets-prefab-close`.

- [ ] **Step 8: Verify no errors**

`mcp__ai-game-developer__console-get-logs` — expect clean.

- [ ] **Step 9: Commit**

```bash
git add "Assets/UI/Player HUD/UI_PlayerHUD.prefab"
git commit -m "feat(speech): add HUDSpeechBubbleLayer + ContentRoot to UI_PlayerHUD"
```

---

## Task 3: Refactor `SpeechBubbleInstance.cs` to screen-space positioning

**Files:**
- Modify: `Assets/Scripts/Character/CharacterSpeech/SpeechBubbleInstance.cs`

- [ ] **Step 1: Replace the file**

Use MCP: `mcp__ai-game-developer__script-update-or-create` with the following **complete** content (this is a full rewrite — the structure of the file changes enough that a series of in-place edits is noisier than a replacement):

```csharp
using UnityEngine;
using TMPro;
using System;
using System.Collections;
using Random = UnityEngine.Random;

/// <summary>
/// A single speech bubble instance — typing, voice, entrance/exit animation,
/// expiration timer, height tracking. Spawned and owned by SpeechBubbleStack.
///
/// HUD-space rewrite: bubbles now live as RectTransform children of
/// HUDSpeechBubbleLayer.Local.ContentRoot (inside a per-stack CanvasGroup
/// wrapper). Each frame the bubble projects its speaker anchor's world
/// position to screen coordinates and lerps anchoredPosition.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
[RequireComponent(typeof(RectTransform))]
public class SpeechBubbleInstance : MonoBehaviour
{
    // ── Serialized Fields ──────────────────────────────────────────────
    [SerializeField] private TextMeshProUGUI _textElement;
    [SerializeField] private GameObject _separatorLine;

    [Header("Animation (reference-resolution pixels)")]
    [SerializeField] private float _entranceDuration = 0.3f;
    [SerializeField] private float _entranceSlideDistance = 40f;
    [SerializeField] private float _exitDuration = 0.3f;
    [SerializeField] private float _exitSlideDistance = 25f;
    [SerializeField] private float _positionLerpSpeed = 8f;

    // ── Events ─────────────────────────────────────────────────────────
    public Action OnExpired;
    public Action OnHeightChanged;
    public Action<bool> OnTypingStateChanged;

    // ── Public Properties ──────────────────────────────────────────────
    public bool IsTyping => _typeRoutine != null;
    public bool IsScripted => _isScripted;
    public bool IsOffScreen => _isOffScreen;

    // ── Private Fields ─────────────────────────────────────────────────
    private CanvasGroup _canvasGroup;
    private RectTransform _rect;
    private Coroutine _typeRoutine;
    private Coroutine _animRoutine;
    private Coroutine _expirationRoutine;

    private Transform _speakerAnchor;
    private Camera _camera;
    private Vector2 _stackOffsetPx;
    private bool _isOffScreen;
    private float _cachedHeight;
    private bool _isScripted;

    // Stored params for typing
    private string _fullMessage;
    private AudioSource _audioSource;
    private VoiceSO _voiceSO;
    private float _pitch;
    private float _typingSpeed;
    private float _duration;
    private Action _onExpiredCallback;
    private Action _onTypingFinishedCallback;

    // ── Unity Lifecycle ────────────────────────────────────────────────

    private void Awake()
    {
        _canvasGroup = GetComponent<CanvasGroup>();
        _rect = GetComponent<RectTransform>();
        _cachedHeight = _rect.rect.height;
    }

    private void Update()
    {
        if (_speakerAnchor == null) return;

        // Lazy camera resolution: NPC bubbles can be created before the local player HUD
        // is ready on a freshly-joined client. Re-resolve on every frame until we have one.
        if (_camera == null)
        {
            _camera = HUDSpeechBubbleLayer.Local?.Camera;
            if (_camera == null) return;
        }

        Vector3 sp = _camera.WorldToScreenPoint(_speakerAnchor.position);
        _isOffScreen = sp.z < 0f
                    || sp.x < 0f || sp.x > Screen.width
                    || sp.y < 0f || sp.y > Screen.height;

        Vector2 target = (Vector2)sp + _stackOffsetPx;
        _rect.anchoredPosition = Vector2.Lerp(
            _rect.anchoredPosition,
            target,
            _positionLerpSpeed * Time.unscaledDeltaTime);
    }

    private void OnDisable()
    {
        if (_typeRoutine != null) StopCoroutine(_typeRoutine);
        _typeRoutine = null;

        if (_animRoutine != null) StopCoroutine(_animRoutine);
        _animRoutine = null;

        if (_expirationRoutine != null) StopCoroutine(_expirationRoutine);
        _expirationRoutine = null;
    }

    private void OnDestroy()
    {
        OnExpired = null;
        OnHeightChanged = null;
        OnTypingStateChanged = null;
        _onExpiredCallback = null;
        _onTypingFinishedCallback = null;
    }

    // ── Public API ─────────────────────────────────────────────────────

    public void SetSpeakerAnchor(Transform anchor) => _speakerAnchor = anchor;
    public void SetCamera(Camera camera) => _camera = camera;
    public void SetStackOffsetPx(Vector2 offsetPx) => _stackOffsetPx = offsetPx;
    public Vector2 GetStackOffsetPx() => _stackOffsetPx;

    public void Setup(string message, AudioSource audioSource, VoiceSO voiceSO,
        float pitch, float typingSpeed, float duration, Action onExpired)
    {
        try
        {
            _fullMessage = message;
            _audioSource = audioSource;
            _voiceSO = voiceSO;
            _pitch = pitch;
            _typingSpeed = typingSpeed;
            _duration = duration;
            _onExpiredCallback = onExpired;
            _isScripted = false;

            _cachedHeight = _rect.rect.height;

            if (_animRoutine != null) StopCoroutine(_animRoutine);
            _animRoutine = StartCoroutine(EntranceAnimation(() =>
            {
                if (_typeRoutine != null) StopCoroutine(_typeRoutine);
                _typeRoutine = StartCoroutine(TypeMessage(() =>
                {
                    if (_expirationRoutine != null) StopCoroutine(_expirationRoutine);
                    _expirationRoutine = StartCoroutine(ExpirationTimer());
                }));
            }));
        }
        catch (Exception e)
        {
            Debug.LogError($"[SpeechBubbleInstance] Exception in Setup: {e.Message}\n{e.StackTrace}");
        }
    }

    public void SetupScripted(string message, AudioSource audioSource, VoiceSO voiceSO,
        float pitch, float typingSpeed, Action onTypingFinished)
    {
        try
        {
            _fullMessage = message;
            _audioSource = audioSource;
            _voiceSO = voiceSO;
            _pitch = pitch;
            _typingSpeed = typingSpeed;
            _onTypingFinishedCallback = onTypingFinished;
            _isScripted = true;

            _cachedHeight = _rect.rect.height;

            if (_animRoutine != null) StopCoroutine(_animRoutine);
            _animRoutine = StartCoroutine(EntranceAnimation(() =>
            {
                if (_typeRoutine != null) StopCoroutine(_typeRoutine);
                _typeRoutine = StartCoroutine(TypeMessage(() =>
                {
                    _onTypingFinishedCallback?.Invoke();
                }));
            }));
        }
        catch (Exception e)
        {
            Debug.LogError($"[SpeechBubbleInstance] Exception in SetupScripted: {e.Message}\n{e.StackTrace}");
        }
    }

    public void CompleteTypingImmediately()
    {
        if (_typeRoutine == null) return;

        StopCoroutine(_typeRoutine);
        _typeRoutine = null;

        if (_textElement != null)
            _textElement.maxVisibleCharacters = _fullMessage.Length;

        OnTypingStateChanged?.Invoke(false);
        CheckHeightChanged();

        if (_isScripted)
        {
            _onTypingFinishedCallback?.Invoke();
            _onTypingFinishedCallback = null;
        }
        else
        {
            if (_expirationRoutine != null) StopCoroutine(_expirationRoutine);
            _expirationRoutine = StartCoroutine(ExpirationTimer());
        }
    }

    public void ResetExpirationTimer()
    {
        if (_isScripted || _duration <= 0f) return;
        if (_expirationRoutine == null) return;

        StopCoroutine(_expirationRoutine);
        _expirationRoutine = StartCoroutine(ExpirationTimer());
    }

    public void Dismiss(Action onComplete = null)
    {
        if (_expirationRoutine != null)
        {
            StopCoroutine(_expirationRoutine);
            _expirationRoutine = null;
        }

        if (_typeRoutine != null)
        {
            StopCoroutine(_typeRoutine);
            _typeRoutine = null;
        }

        if (_animRoutine != null) StopCoroutine(_animRoutine);
        _animRoutine = StartCoroutine(ExitAnimation(() =>
        {
            onComplete?.Invoke();
            Destroy(gameObject);
        }));
    }

    /// <summary>
    /// Returns the bubble's current rendered height in reference-resolution HUD pixels.
    /// Called right after Setup() to compute the push height for the Habbo stack.
    /// </summary>
    public float GetHeightPx()
    {
        if (_rect == null) return 0f;
        return _rect.rect.height;
    }

    public void SetSeparatorVisible(bool visible)
    {
        if (_separatorLine != null)
            _separatorLine.SetActive(visible);
    }

    // ── Coroutines ─────────────────────────────────────────────────────

    private IEnumerator TypeMessage(Action onComplete)
    {
        OnTypingStateChanged?.Invoke(true);

        _textElement.text = _fullMessage;
        _textElement.maxVisibleCharacters = 0;

        _textElement.ForceMeshUpdate();
        CheckHeightChanged();

        float currentSpeed = _typingSpeed > 0f ? _typingSpeed : 0.04f;

        if (currentSpeed <= 0f)
        {
            _textElement.maxVisibleCharacters = _fullMessage.Length;
            _typeRoutine = null;
            OnTypingStateChanged?.Invoke(false);
            onComplete?.Invoke();
            yield break;
        }

        int charCount = 0;
        float timeAccumulator = 0f;
        char[] characters = _fullMessage.ToCharArray();

        while (charCount < characters.Length)
        {
            timeAccumulator += Time.unscaledDeltaTime;

            int lettersToAdd = Mathf.FloorToInt(timeAccumulator / currentSpeed);

            if (lettersToAdd > 0)
            {
                int lettersAdded = 0;
                while (lettersAdded < lettersToAdd && charCount < characters.Length)
                {
                    char letter = characters[charCount];
                    charCount++;
                    lettersAdded++;

                    if (letter != ' ' && charCount % 3 == 0 && _voiceSO != null && _audioSource != null)
                    {
                        AudioClip clipToPlay = _voiceSO.GetRandomClip();
                        if (clipToPlay != null)
                        {
                            _audioSource.pitch = _pitch + Random.Range(-0.05f, 0.05f);
                            _audioSource.PlayOneShot(clipToPlay);
                        }
                    }
                }

                _textElement.maxVisibleCharacters = charCount;
                timeAccumulator -= lettersToAdd * currentSpeed;
            }

            yield return null;
        }

        _typeRoutine = null;
        OnTypingStateChanged?.Invoke(false);
        onComplete?.Invoke();
    }

    private IEnumerator EntranceAnimation(Action onComplete)
    {
        _canvasGroup.alpha = 0f;

        // Start position: _stackOffsetPx shifted DOWN by the slide distance.
        // The Update() lerp will aim at _stackOffsetPx, so we temporarily bias the
        // offset downward, fade in, then restore.
        Vector2 targetOffset = _stackOffsetPx;
        _stackOffsetPx = new Vector2(targetOffset.x, targetOffset.y - _entranceSlideDistance);

        float elapsed = 0f;

        while (elapsed < _entranceDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / _entranceDuration);
            float eased = 1f - (1f - t) * (1f - t);

            _canvasGroup.alpha = Mathf.Lerp(0f, 1f, eased);
            _stackOffsetPx = new Vector2(
                targetOffset.x,
                Mathf.Lerp(targetOffset.y - _entranceSlideDistance, targetOffset.y, eased));

            yield return null;
        }

        _canvasGroup.alpha = 1f;
        _stackOffsetPx = targetOffset;
        _animRoutine = null;

        onComplete?.Invoke();
    }

    private IEnumerator ExitAnimation(Action onComplete)
    {
        Vector2 startOffset = _stackOffsetPx;
        Vector2 endOffset = new Vector2(startOffset.x, startOffset.y + _exitSlideDistance);

        float startAlpha = _canvasGroup.alpha;
        float elapsed = 0f;

        while (elapsed < _exitDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / _exitDuration);
            float eased = t * t;

            _canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, eased);
            _stackOffsetPx = Vector2.Lerp(startOffset, endOffset, eased);

            yield return null;
        }

        _canvasGroup.alpha = 0f;
        _animRoutine = null;

        onComplete?.Invoke();
    }

    private IEnumerator ExpirationTimer()
    {
        yield return new WaitForSecondsRealtime(_duration);

        _expirationRoutine = null;

        Dismiss(() =>
        {
            OnExpired?.Invoke();
            _onExpiredCallback?.Invoke();
        });
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private void CheckHeightChanged()
    {
        float currentHeight = _rect.rect.height;
        if (!Mathf.Approximately(currentHeight, _cachedHeight))
        {
            _cachedHeight = currentHeight;
            OnHeightChanged?.Invoke();
        }
    }
}
```

- [ ] **Step 2: Refresh & check for compile errors**

`mcp__ai-game-developer__assets-refresh` then `mcp__ai-game-developer__console-get-logs`.
Expected: `SpeechBubbleStack.cs` will now have compile errors referencing `GetHeight()`, `SetTargetPosition()`, etc. — **this is expected**; Task 4 fixes it. Do not commit yet.

- [ ] **Step 3: Verify no unrelated errors**

Scan logs for any errors that aren't in `SpeechBubbleStack.cs`. If found, stop and debug before proceeding.

---

## Task 4: Refactor `SpeechBubbleStack.cs` for HUD parenting + proximity gate

**Files:**
- Modify: `Assets/Scripts/Character/CharacterSpeech/SpeechBubbleStack.cs`

- [ ] **Step 1: Replace the file**

Use MCP: `mcp__ai-game-developer__script-update-or-create` with the following **complete** content:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Manages all active speech bubble instances for a single character.
/// Owns the logical list, cap, Habbo cross-character push, and mouth-animation
/// ref count.
///
/// HUD rewrite: bubbles are instantiated as children of a per-stack CanvasGroup
/// wrapper under HUDSpeechBubbleLayer.Local.ContentRoot (screen-space). Proximity
/// to the local player character and on-screen status drive a fade on the wrapper.
///
/// Cross-character detection (Habbo style):
/// - SphereCollider trigger on the SpeechZone physics layer (radius 25)
/// - When this character speaks, ALL existing bubbles in ALL nearby stacks (and own)
///   are pushed UP by the new bubble's height, measured in HUD pixels.
/// - New bubble always appears at the base (Y = 0)
/// - Pushed bubbles never come back down
/// </summary>
[RequireComponent(typeof(SphereCollider))]
[RequireComponent(typeof(Rigidbody))]
public class SpeechBubbleStack : MonoBehaviour
{
    // ── Serialized Fields ──────────────────────────────────────────────
    [SerializeField] private SpeechBubbleInstance _bubbleInstancePrefab;
    [SerializeField] private int _maxBubbles = 5;
    [SerializeField] private float _separatorSpacingPx = 4f;
    [SerializeField] private float _speechZoneRadius = 25f;
    [SerializeField] private float _proximityRadius = 25f;
    [SerializeField] private float _fadeSpeed = 4f;

    // ── Private Fields ─────────────────────────────────────────────────
    private readonly List<SpeechBubbleInstance> _bubbles = new List<SpeechBubbleInstance>();
    private readonly HashSet<SpeechBubbleStack> _nearbyStacks = new();
    private MouthController _mouthController;
    private int _typingCount;
    private SphereCollider _zoneCollider;

    private GameObject _wrapperGO;
    private CanvasGroup _wrapperGroup;
    private RectTransform _wrapperRect;

    // ── Public Properties ──────────────────────────────────────────────
    public Transform OwnerRoot { get; private set; }
    public bool IsAnyTyping => _typingCount > 0;
    public bool HasActiveBubbles => _bubbles.Count > 0;

    // ── Initialization ─────────────────────────────────────────────────

    public void Init(Transform ownerRoot, MouthController mouthController)
    {
        OwnerRoot = ownerRoot;
        _mouthController = mouthController;
    }

    // ── Unity Lifecycle ────────────────────────────────────────────────

    private void Awake()
    {
        _zoneCollider = GetComponent<SphereCollider>();
        _zoneCollider.isTrigger = true;
        _zoneCollider.radius = _speechZoneRadius;

        var rb = GetComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
    }

    private void Update()
    {
        try
        {
            // Lazy re-parent in case the HUD layer appeared late (session boot race).
            if (_wrapperGO != null && _wrapperGO.transform.parent == null)
            {
                var layer = HUDSpeechBubbleLayer.Local;
                if (layer != null && layer.ContentRoot != null)
                {
                    _wrapperGO.transform.SetParent(layer.ContentRoot, worldPositionStays: false);
                    _wrapperGO.SetActive(true);
                }
            }

            if (_wrapperGroup == null || _bubbles.Count == 0) return;

            var local = HUDSpeechBubbleLayer.Local;
            if (local == null || local.LocalPlayerAnchor == null)
            {
                _wrapperGroup.alpha = Mathf.MoveTowards(_wrapperGroup.alpha, 0f, _fadeSpeed * Time.unscaledDeltaTime);
                return;
            }

            // Measure feet-to-feet (root-to-root). The stack's transform is the speech
            // anchor at +9u above the character's feet; using OwnerRoot keeps proximity
            // grounded so a "25u hearing range" matches player intuition about distance
            // to the character, not to their head.
            Vector3 speakerPos = OwnerRoot != null ? OwnerRoot.position : transform.position;
            float distSq = (local.LocalPlayerAnchor.position - speakerPos).sqrMagnitude;
            bool inRange = distSq <= _proximityRadius * _proximityRadius;

            bool anyOnScreen = false;
            for (int i = 0; i < _bubbles.Count; i++)
            {
                if (_bubbles[i] != null && !_bubbles[i].IsOffScreen) { anyOnScreen = true; break; }
            }

            float targetAlpha = (inRange && anyOnScreen) ? 1f : 0f;
            _wrapperGroup.alpha = Mathf.MoveTowards(_wrapperGroup.alpha, targetAlpha, _fadeSpeed * Time.unscaledDeltaTime);
        }
        catch (Exception e)
        {
            Debug.LogError($"[SpeechBubbleStack] Update error: {e.Message}\n{e.StackTrace}");
        }
    }

    private void OnDisable()
    {
        try
        {
            ClearAll();
            _nearbyStacks.Clear();
            if (_wrapperGO != null)
            {
                Destroy(_wrapperGO);
                _wrapperGO = null;
                _wrapperGroup = null;
                _wrapperRect = null;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[SpeechBubbleStack] Exception in OnDisable: {e.Message}\n{e.StackTrace}");
        }
    }

    // ── Trigger-Based Speech Zone Detection ────────────────────────────

    private void OnTriggerEnter(Collider other)
    {
        if (other.TryGetComponent<SpeechBubbleStack>(out var otherStack) && otherStack != this)
        {
            _nearbyStacks.Add(otherStack);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.TryGetComponent<SpeechBubbleStack>(out var otherStack))
        {
            _nearbyStacks.Remove(otherStack);
        }
    }

    // ── Public API ─────────────────────────────────────────────────────

    public void PushBubble(string message, float duration, float typingSpeed,
        AudioSource audioSource, VoiceSO voiceSO, float pitch)
    {
        try
        {
            EnforceCap();
            CompleteNewestTyping();

            var wrapper = EnsureStackWrapper();
            var instance = Instantiate(_bubbleInstancePrefab, wrapper);

            instance.SetSpeakerAnchor(transform);
            instance.SetCamera(HUDSpeechBubbleLayer.Local?.Camera);

            instance.Setup(message, audioSource, voiceSO, pitch, typingSpeed, duration,
                onExpired: () => OnBubbleExpired(instance));

            instance.OnTypingStateChanged += OnTypingStateChanged;
            instance.SetStackOffsetPx(Vector2.zero);

            _bubbles.Insert(0, instance);

            float pushHeightPx = instance.GetHeightPx() + _separatorSpacingPx;
            PushAllBubblesUp(pushHeightPx, instance);

            UpdateSeparatorVisibility();
        }
        catch (Exception e)
        {
            Debug.LogError($"[SpeechBubbleStack] Exception in PushBubble: {e.Message}\n{e.StackTrace}");
        }
    }

    public void PushScriptedBubble(string message, float typingSpeed,
        AudioSource audioSource, VoiceSO voiceSO, float pitch, Action onTypingFinished)
    {
        try
        {
            EnforceCap();
            CompleteNewestTyping();

            var wrapper = EnsureStackWrapper();
            var instance = Instantiate(_bubbleInstancePrefab, wrapper);

            instance.SetSpeakerAnchor(transform);
            instance.SetCamera(HUDSpeechBubbleLayer.Local?.Camera);

            instance.SetupScripted(message, audioSource, voiceSO, pitch, typingSpeed, onTypingFinished);

            instance.OnTypingStateChanged += OnTypingStateChanged;
            instance.SetStackOffsetPx(Vector2.zero);

            _bubbles.Insert(0, instance);

            float pushHeightPx = instance.GetHeightPx() + _separatorSpacingPx;
            PushAllBubblesUp(pushHeightPx, instance);

            UpdateSeparatorVisibility();
        }
        catch (Exception e)
        {
            Debug.LogError($"[SpeechBubbleStack] Exception in PushScriptedBubble: {e.Message}\n{e.StackTrace}");
        }
    }

    public void DismissBottom()
    {
        if (_bubbles.Count <= 0) return;

        var bottom = _bubbles[0];
        bottom.Dismiss(() => { RemoveBubble(bottom); });
    }

    public void DismissAll()
    {
        var toRemove = new List<SpeechBubbleInstance>(_bubbles);
        _bubbles.Clear();

        foreach (var bubble in toRemove)
        {
            UnsubscribeEvents(bubble);
            bubble.Dismiss();
        }

        _typingCount = 0;
        _mouthController?.StopTalking();
    }

    public void DismissAllScripted()
    {
        var scripted = _bubbles.Where(b => b.IsScripted).ToList();
        foreach (var bubble in scripted)
        {
            if (bubble.IsTyping) _typingCount--;
            _bubbles.Remove(bubble);
            UnsubscribeEvents(bubble);
            bubble.Dismiss();
        }

        _typingCount = Mathf.Max(_typingCount, 0);
        if (_typingCount == 0) _mouthController?.StopTalking();
    }

    public void ClearAll()
    {
        foreach (var bubble in _bubbles)
        {
            if (bubble != null)
            {
                UnsubscribeEvents(bubble);
                Destroy(bubble.gameObject);
            }
        }

        _bubbles.Clear();
        _typingCount = 0;
        _mouthController?.StopTalking();
    }

    public void PushAllBubblesUpBy(float heightPx)
    {
        foreach (var bubble in _bubbles)
        {
            if (bubble == null) continue;
            var currentOffset = bubble.GetStackOffsetPx();
            bubble.SetStackOffsetPx(new Vector2(currentOffset.x, currentOffset.y + heightPx));
            bubble.ResetExpirationTimer();
        }
    }

    // ── Private Methods ────────────────────────────────────────────────

    private Transform EnsureStackWrapper()
    {
        if (_wrapperGO != null) return _wrapperGO.transform;

        _wrapperGO = new GameObject($"SpeechStackWrapper_{gameObject.name}", typeof(RectTransform), typeof(CanvasGroup));
        _wrapperRect = _wrapperGO.GetComponent<RectTransform>();
        _wrapperGroup = _wrapperGO.GetComponent<CanvasGroup>();
        _wrapperGroup.alpha = 0f;
        _wrapperGroup.blocksRaycasts = false;
        _wrapperGroup.interactable = false;

        _wrapperRect.anchorMin = Vector2.zero;
        _wrapperRect.anchorMax = Vector2.one;
        _wrapperRect.offsetMin = Vector2.zero;
        _wrapperRect.offsetMax = Vector2.zero;

        var layer = HUDSpeechBubbleLayer.Local;
        if (layer != null && layer.ContentRoot != null)
        {
            _wrapperGO.transform.SetParent(layer.ContentRoot, worldPositionStays: false);
        }
        else
        {
            _wrapperGO.SetActive(false);
            Debug.LogWarning($"[SpeechBubbleStack] HUDSpeechBubbleLayer.Local missing at PushBubble — wrapper parked inactive until layer appears.");
        }

        return _wrapperGO.transform;
    }

    private void EnforceCap()
    {
        if (_bubbles.Count < _maxBubbles) return;

        var oldest = _bubbles[_bubbles.Count - 1];
        _bubbles.RemoveAt(_bubbles.Count - 1);
        UnsubscribeEvents(oldest);
        oldest.Dismiss();
    }

    private void CompleteNewestTyping()
    {
        if (_bubbles.Count > 0 && _bubbles[0].IsTyping)
        {
            _bubbles[0].CompleteTypingImmediately();
        }
    }

    private void PushAllBubblesUp(float heightPx, SpeechBubbleInstance excludeInstance)
    {
        foreach (var bubble in _bubbles)
        {
            if (bubble == null || bubble == excludeInstance) continue;
            var currentOffset = bubble.GetStackOffsetPx();
            bubble.SetStackOffsetPx(new Vector2(currentOffset.x, currentOffset.y + heightPx));
        }

        _nearbyStacks.RemoveWhere(s => s == null);
        foreach (var stack in _nearbyStacks)
        {
            if (stack.HasActiveBubbles)
            {
                stack.PushAllBubblesUpBy(heightPx);
            }
        }
    }

    private void OnBubbleExpired(SpeechBubbleInstance instance)
    {
        RemoveBubble(instance);
    }

    private void RemoveBubble(SpeechBubbleInstance instance)
    {
        if (instance == null) return;
        if (!_bubbles.Contains(instance)) return;

        UnsubscribeEvents(instance);
        _bubbles.Remove(instance);
        UpdateSeparatorVisibility();
    }

    private void OnTypingStateChanged(bool isTyping)
    {
        if (isTyping)
        {
            _typingCount++;
            if (_typingCount == 1) _mouthController?.StartTalking();
        }
        else
        {
            _typingCount--;
            if (_typingCount <= 0)
            {
                _typingCount = 0;
                _mouthController?.StopTalking();
            }
        }
    }

    private void UpdateSeparatorVisibility()
    {
        for (int i = 0; i < _bubbles.Count; i++)
        {
            if (_bubbles[i] != null)
                _bubbles[i].SetSeparatorVisible(i > 0);
        }
    }

    private void UnsubscribeEvents(SpeechBubbleInstance instance)
    {
        if (instance == null) return;
        instance.OnTypingStateChanged -= OnTypingStateChanged;
    }
}
```

- [ ] **Step 2: Refresh & check**

`mcp__ai-game-developer__assets-refresh` then `mcp__ai-game-developer__console-get-logs`.
Expected: **no compile errors.** Tasks 3 + 4 together produce a compilable state.

- [ ] **Step 3: Commit**

```bash
git add "Assets/Scripts/Character/CharacterSpeech/SpeechBubbleInstance.cs" \
        "Assets/Scripts/Character/CharacterSpeech/SpeechBubbleStack.cs"
git commit -m "refactor(speech): port SpeechBubbleStack/Instance to HUD screen-space"
```

---

## Task 5: Remove death/incapacitation overrides from `CharacterSpeech.cs`

**Files:**
- Modify: `Assets/Scripts/Character/CharacterSpeech/CharacterSpeech.cs:185-186`

- [ ] **Step 1: Remove the two override lines**

Use the Edit tool (not MCP) to delete exactly these two lines at the end of the class (lines 185-186 in the pre-change file):

```csharp
    protected override void HandleDeath(Character character) => _speechBubbleStack?.ClearAll();
    protected override void HandleIncapacitated(Character character) => _speechBubbleStack?.ClearAll();
```

Verify these were virtual no-ops in the base class (already confirmed during spec review: `CharacterSystem.cs` lines 51 and 61 — `protected virtual void HandleDeath/HandleIncapacitated(Character character) { }`).

- [ ] **Step 2: Refresh & check**

`mcp__ai-game-developer__assets-refresh` then `mcp__ai-game-developer__console-get-logs`.
Expected: clean compile. CharacterSpeech.cs should now be ~2 lines shorter than Task 0's recorded count.

- [ ] **Step 3: Commit**

```bash
git add "Assets/Scripts/Character/CharacterSpeech/CharacterSpeech.cs"
git commit -m "refactor(speech): let bubbles live past character death/incapacitation"
```

---

## Task 6: Rework `SpeechBubbleInstance_Prefab`

**Files:**
- Modify: `Assets/Prefabs/SpeechBubbleInstance_Prefab.prefab`

- [ ] **Step 1: Open the prefab**

MCP: `mcp__ai-game-developer__assets-prefab-open` on `Assets/Prefabs/SpeechBubbleInstance_Prefab.prefab`.

- [ ] **Step 2: Inspect the current hierarchy**

MCP: `mcp__ai-game-developer__gameobject-find` on the prefab root, `includeChildrenDepth: 2`.

Record: names of children (expected `SeparatorLine` + `Canvas` → `Text_Speech`). Note any additional components on the root beyond `SpeechBubbleInstance`, `CanvasGroup`, and the soon-to-be-dropped world-space Canvas pieces.

- [ ] **Step 3: Ensure root has a `RectTransform`**

The root currently has a plain `Transform` (since it was a world-space object). In Unity, you cannot remove a root Transform or simply `Add Component → RectTransform` via MCP — the engine either auto-upgrades on load (because of the new `[RequireComponent(typeof(RectTransform))]`) or refuses the swap.

Try in this order and stop at the first that works:
1. **Auto-upgrade via `[RequireComponent]`:** open the prefab fresh and check the root — Unity often promotes `Transform → RectTransform` on load. Verify via `mcp__ai-game-developer__gameobject-component-get` on the root.
2. **Try MCP `gameobject-component-add` with `typeName: RectTransform`.** This sometimes succeeds when the script changes ahead of the prefab open.
3. **Fallback: replace the root.** Create a fresh RectTransform GameObject as a sibling of the current prefab root, move all children and the `SpeechBubbleInstance` + `CanvasGroup` components onto it, delete the old Transform root, and promote the new one to root (save as a new prefab and redirect references). This is a larger surgery — if needed, stop and surface to the human rather than attempting it blind.

Verify the final result via `gameobject-component-get` — `RectTransform` must be present before continuing.

- [ ] **Step 4: Re-parent `Text_Speech` directly under the root**

The existing nested `Canvas` GameObject wraps `Text_Speech` in the world-space layout. Re-parent `Text_Speech` to be a direct child of the root prefab GameObject using `mcp__ai-game-developer__gameobject-set-parent`.

- [ ] **Step 5: Destroy the now-empty nested `Canvas` GameObject**

MCP: `mcp__ai-game-developer__gameobject-destroy` on the `Canvas` child. This also removes the `CanvasScaler` attached to it.

- [ ] **Step 6: Set root pivot to (0.5, 0)**

MCP: `mcp__ai-game-developer__gameobject-component-modify` on the root `RectTransform`: set `pivot: {x: 0.5, y: 0}`. This makes the bubble's bottom-center the anchor point — so assigning `anchoredPosition = speakerScreenPos` visually places the bubble's bottom at the speaker's head.

- [ ] **Step 7: Configure `Text_Speech` RectTransform**

MCP: `mcp__ai-game-developer__gameobject-component-modify` on `Text_Speech`'s RectTransform:
- anchors min/max: (0.5, 0) / (0.5, 0) — center-bottom anchoring so it sits where the root pivots
- pivot: (0.5, 0)
- sizeDelta: (320, 70) as a sensible starting width/height (the ContentSizeFitter vertical fit will expand height as text grows)

Also on `Text_Speech`'s `TextMeshProUGUI` component: set `fontSize: 28` (matches the spec §7.1 "~28pt at 1080p reference"). Keep word-wrap on; alignment center.

Ensure TMP ContentSizeFitter `verticalFit` is `PreferredSize` and `horizontalFit` is `Unconstrained` (width fixed at 320 so word-wrap works). Use `mcp__ai-game-developer__gameobject-component-get` to inspect, then `gameobject-component-modify` to correct if needed.

- [ ] **Step 8: Configure `SeparatorLine` RectTransform**

Anchor to top-center of the bubble (anchorMin/Max = (0.5, 1)). Set anchoredPosition Y = 0, width = ~200px, height = 1px.

- [ ] **Step 9: Verify the `SpeechBubbleInstance` script's `_textElement` and `_separatorLine` fields still reference the (now reparented) objects**

MCP: `mcp__ai-game-developer__gameobject-component-get` on the root's `SpeechBubbleInstance` component.
If either reference is missing due to reparenting, use `gameobject-component-modify` to reassign.

- [ ] **Step 10: Save & close**

MCP: `mcp__ai-game-developer__assets-prefab-save` then `mcp__ai-game-developer__assets-prefab-close`.

- [ ] **Step 11: Check for errors**

`mcp__ai-game-developer__console-get-logs`. Expected clean.

- [ ] **Step 12: Commit**

```bash
git add "Assets/Prefabs/SpeechBubbleInstance_Prefab.prefab"
git commit -m "refactor(speech): convert SpeechBubbleInstance_Prefab to HUD RectTransform"
```

---

## Task 7: Update Character prefab variants

**Files:**
- Modify: `Assets/Prefabs/Character/Character_Default.prefab`
- Modify: `Assets/Prefabs/Character/Character_Default_Humanoid.prefab`
- Modify: `Assets/Prefabs/Character/Character_Default_Quadruped.prefab`

Repeat the following sub-steps for **each** of the three prefabs listed above.

- [ ] **Step 1: Open the prefab**

MCP: `mcp__ai-game-developer__assets-prefab-open`.

- [ ] **Step 2: Locate the speech anchor GameObject**

MCP: `mcp__ai-game-developer__gameobject-find` — look for the child holding `SpeechBubbleStack`. Typically named something like `SpeechAnchor` at local position `(0, 9, 0)`.

- [ ] **Step 3: Verify/bump SphereCollider radius**

MCP: `mcp__ai-game-developer__gameobject-component-get` on the SphereCollider. If `radius != 25`, modify via `gameobject-component-modify` to set `radius: 25`.

- [ ] **Step 4: Remove the Billboard component if present**

MCP: `mcp__ai-game-developer__gameobject-component-destroy` targeting the `Billboard` component on the same GameObject. Skip this step silently if Billboard is already absent.

- [ ] **Step 5: Save & close**

MCP: `mcp__ai-game-developer__assets-prefab-save`, then `assets-prefab-close`.

- [ ] **Step 6: Check for errors (after all three prefabs are done)**

`mcp__ai-game-developer__console-get-logs`. Expected clean.

- [ ] **Step 7: Commit**

```bash
git add "Assets/Prefabs/Character/Character_Default.prefab" \
        "Assets/Prefabs/Character/Character_Default_Humanoid.prefab" \
        "Assets/Prefabs/Character/Character_Default_Quadruped.prefab"
git commit -m "refactor(speech): 25u speech radius, drop Billboard on character variants"
```

---

## Task 8: Play Mode manual verification

**Files:** (runtime only)

Open `Assets/Scenes/GameScene.unity`, enter Play Mode, and walk through each scenario from the spec. Record any observed bug.

- [ ] **Step 1: Scenario 1 — walk toward idle-speaking NPC**

Find an NPC that `Say()`s periodically (ambient chatter / GOAP). Stand > 25u away while the NPC speaks. Expected: bubble wrapper alpha stays 0 (invisible). Walk toward the NPC. Expected: wrapper fades up to 1 as you cross the 25u boundary. Walk away. Expected: fades back to 0 over ~0.25s.

- [ ] **Step 2: Scenario 2 — NPC behind camera**

Stand inside 25u of a speaking NPC but rotate the camera so the NPC is behind you. Expected: alpha 0. Rotate to face: alpha 1.

- [ ] **Step 3: Scenario 3 — two NPCs conversing close together**

Stand near two NPCs having an ambient exchange. Expected: Habbo push works — new bubbles from either character push BOTH stacks up in HUD pixel space.

- [ ] **Step 4: Scenario 4 — bubble cap**

Via console or debug tool, call `Say()` 6 times rapidly on one character. Expected: the 6th push force-dismisses the oldest; no NullReferenceException in the console.

- [ ] **Step 5: Scenario 5 — scripted dialogue**

Trigger a `DialogueManager` scripted dialogue with a character in proximity. Expected: scripted bubbles appear, advance on `CloseSpeech()`, clear on `ResetSpeech()`.

- [ ] **Step 6: Scenario 6 — Host says something**

Start a host+client session (two Unity Editor instances OR Editor + build). Have the host character say something. Expected: host's HUD shows bubble. Client's HUD shows bubble only if the client's local player is within 25u of the host.

- [ ] **Step 7: Scenario 7 — Client says something**

Symmetric of #6.

- [ ] **Step 8: Scenario 8 — NPC near host, far from client**

Same networked session. NPC near the host but outside 25u from the client. Expected: host HUD sees bubble, client HUD stays at alpha 0.

- [ ] **Step 9: Scenario 9 — Portal gate return**

In solo mode, go through a portal gate that re-spawns the player. Trigger NPC speech. Expected: proximity gate works with the newly-spawned local player character.

- [ ] **Step 10: Scenario 10 — Character death**

Kill an NPC while they have active speech bubbles. Expected: bubbles stay visible and follow their normal timer / dismissal. On full despawn, bubbles are cleared via `OnDisable → ClearAll`.

- [ ] **Step 11: Scenario 11 — ResetSpeech at dialogue end**

End a dialogue mid-line. Expected: all scripted bubbles immediately cleared, no residual fade-out animation.

- [ ] **Step 12: Scenario 12 — walk out of range mid-speech**

Stand next to speaking NPC, then walk out of range before the bubble expires. Expected: wrapper fades to 0 smoothly; the bubble's internal timer still runs.

- [ ] **Step 13: Scenario 13 — walk back into range before expiration**

Continuation of 12: walk back within 25u before the bubble's natural expiration. Expected: wrapper fades back to 1, bubble still there at its pushed offset. (If the timer expired during the out-of-range period, the bubble will be gone — this is correct.)

- [ ] **Step 14: Commit (if prefab tweaks were needed during testing)**

Only commit if Play Mode exposed prefab / script tuning needs (font size, slide distances, etc.). Use a single commit:

```bash
git commit -am "tune(speech): manual verification adjustments"
```

Otherwise skip this step.

---

## Task 9: Update documentation

**Files:**
- Modify: `.agent/skills/speech-system/SKILL.md`
- Modify: `.claude/agents/character-system-specialist.md`

- [ ] **Step 1: Update `.agent/skills/speech-system/SKILL.md`**

Edit the skill doc to replace the world-space architecture description with the HUD-space one:

1. Update the frontmatter `description` to mention HUD rendering + proximity gate.
2. Architecture diagram: bubbles parent under `HUDSpeechBubbleLayer.Local.ContentRoot` via a per-stack CanvasGroup wrapper.
3. Add a new "Proximity & Visibility Gate" section documenting: 25u radius, live gating, off-screen hides, CanvasGroup fade.
4. In the "Components" tables, update `SpeechBubbleStack` (new fields: `_proximityRadius`, `_fadeSpeed`, `_separatorSpacingPx`; renamed: trigger radius 15 → 25), `SpeechBubbleInstance` (new: `SetSpeakerAnchor`/`SetCamera`/`SetStackOffsetPx`/`IsOffScreen`; renamed `GetHeight → GetHeightPx`; dropped `SetTargetPosition`/`GetTargetPosition`).
5. Add `HUDSpeechBubbleLayer` as a new dependency row.
6. Update the "Death / Incapacitation" integration section to state that `CharacterSpeech` no longer overrides those hooks — bubbles follow their normal lifecycle.
7. Replace the "Character Prefab Setup" section to note: Billboard removed; SphereCollider radius 25.
8. Update "Prefab Structure" to show a RectTransform root (no WorldSpace Canvas).

Do NOT add redundant commentary. Preserve the existing edge-cases list and add: "Bubble visibility uses CanvasGroup alpha — all of a stack's bubbles fade as one unit, not individually."

- [ ] **Step 2: Update `.claude/agents/character-system-specialist.md`**

Add a single line under the speech system summary noting: "Speech bubbles render on the local player's HUD (`HUDSpeechBubbleLayer.Local.ContentRoot`), proximity-gated at 25 world units from the local player character. Stack push math is in HUD pixels."

- [ ] **Step 3: Commit**

```bash
git add ".agent/skills/speech-system/SKILL.md" ".claude/agents/character-system-specialist.md"
git commit -m "docs(speech): update SKILL.md and agent for HUD speech bubbles"
```

---

## Task 10: Post-implementation multiplayer validation (CLAUDE.md rule 19)

**Files:** (no direct file edits from this task — agent will flag any it finds)

- [ ] **Step 1: Dispatch `network-validator` agent**

Use the Agent tool with `subagent_type: network-validator`. Prompt the agent with:

> Audit the recent HUD speech bubble refactor on branch `multiplayyer`. Files touched: `Assets/Scripts/UI/HUDSpeechBubbleLayer.cs` (new), `Assets/Scripts/Character/CharacterSpeech/SpeechBubbleStack.cs` (refactor), `Assets/Scripts/Character/CharacterSpeech/SpeechBubbleInstance.cs` (refactor), `Assets/Scripts/Character/CharacterSpeech/CharacterSpeech.cs` (removed two override lines).
>
> All new state is per-client; no NetworkVariables or RPCs were added or changed. Verify: (a) no server-authoritative state is assumed to be visible to clients without replication, (b) late-joining clients still behave per spec (no pre-existing bubbles), (c) NPC (server-authoritative) speech still renders identically on all clients within their own local player's proximity, (d) host and non-host clients both resolve `NetworkManager.Singleton.LocalClient.PlayerObject` correctly for local player anchor resolution.
>
> Spec: `docs/superpowers/specs/2026-04-20-hud-speech-bubbles-design.md`.

- [ ] **Step 2: Address any issues flagged by the agent**

If the agent returns "Issues Found", fix each one with a targeted edit, re-refresh Unity, and commit. If the fix is non-trivial, add a new task back into this plan and re-run the validator.

- [ ] **Step 3: Final commit if fixes were made**

```bash
git commit -am "fix(speech): address network-validator findings"
```

- [ ] **Step 4: Mark the feature complete**

Summarize the delivered work in a final message to the user, including: commits, files touched, test scenario outcomes, and any deferred follow-ups (off-screen arrow / pooling / per-archetype radius from spec §12).

---

## Testing / Verification Summary

- **Compile verification:** Unity MCP `console-get-logs` after every script-touching task.
- **Prefab verification:** Unity MCP `gameobject-find` / `gameobject-component-get` after every prefab change.
- **Behaviour verification:** 13 manual Play Mode scenarios in Task 8.
- **Multiplayer verification:** Task 8 scenarios 6–8 + `network-validator` agent in Task 10.

## Skills Referenced

- @superpowers:subagent-driven-development — recommended execution style
- @superpowers:systematic-debugging — if any Play Mode scenario fails
- @superpowers:verification-before-completion — before claiming done in Task 10 Step 4

## Known Follow-ups (out of scope per spec §12)

- Off-screen arrow / edge-clamp indicator.
- Per-archetype proximity radius overrides.
- Bubble object pooling.
- Optional "quiet mode" that hides ambient bubbles but keeps scripted dialogue.
