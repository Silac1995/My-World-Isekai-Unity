using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Manages all active speech bubble instances for a single character.
/// Owns the logical list, cap, Habbo cross-character push, and mouth-animation
/// ref count.
///
/// HUD rewrite: bubbles are instantiated as children of a per-stack CanvasGroup
/// wrapper under HUDSpeechBubbleLayer.Local.ContentRoot (screen-space). The
/// wrapper's alpha is driven each frame by a linear X-Z distance fade between
/// <see cref="_fadeStartDistance"/> and <see cref="_fadeEndDistance"/>, gated
/// further by an on-screen check. The local player's own stack skips both
/// gates and stays fully opaque — mirrors <see cref="UI_RemoteActionIndicator"/>'s
/// _isLocalPlayer fast path so the two HUD layers read the same way.
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
    [Header("Distance fade (matches UI_RemoteActionIndicator)")]
    [Tooltip("World units. <= FadeStart = full opacity. Local-player stack skips this entirely.")]
    [SerializeField, Range(0f, 50f)] private float _fadeStartDistance = 12f;
    [SerializeField, Range(1f, 200f)] private float _fadeEndDistance = 30f;
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

    // Local-player detection — cached on first successful resolve. Tri-state via the resolved flag.
    private bool _isLocalPlayer;
    private bool _isLocalPlayerResolved;

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

            // Local-player stack: always fully opaque — skip distance + on-screen gates.
            if (IsLocalPlayerStack())
            {
                _wrapperGroup.alpha = Mathf.MoveTowards(_wrapperGroup.alpha, 1f, _fadeSpeed * Time.unscaledDeltaTime);
                return;
            }

            var local = HUDSpeechBubbleLayer.Local;
            if (local == null || local.LocalPlayerAnchor == null)
            {
                _wrapperGroup.alpha = Mathf.MoveTowards(_wrapperGroup.alpha, 0f, _fadeSpeed * Time.unscaledDeltaTime);
                return;
            }

            // 2D X-Z distance — Y doesn't matter for "is the other character close to me".
            Vector3 speakerPos = OwnerRoot != null ? OwnerRoot.position : transform.position;
            Vector3 a = local.LocalPlayerAnchor.position; a.y = 0f;
            Vector3 b = speakerPos; b.y = 0f;
            float d = Vector3.Distance(a, b);

            float distanceAlpha;
            if (d <= _fadeStartDistance)      distanceAlpha = 1f;
            else if (d >= _fadeEndDistance)   distanceAlpha = 0f;
            else                              distanceAlpha = 1f - (d - _fadeStartDistance) / Mathf.Max(0.001f, _fadeEndDistance - _fadeStartDistance);

            bool anyOnScreen = false;
            for (int i = 0; i < _bubbles.Count; i++)
            {
                if (_bubbles[i] != null && !_bubbles[i].IsOffScreen) { anyOnScreen = true; break; }
            }

            float targetAlpha = anyOnScreen ? distanceAlpha : 0f;
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

    // ── Local-Player / Display Resolution ──────────────────────────────

    /// <summary>
    /// Lazy-resolves whether this stack belongs to the local player's Character.
    /// Returns false while NetworkManager / LocalClient / PlayerObject aren't ready yet;
    /// re-tries each call until it can answer once, then caches the resolved value.
    /// Mirrors RemoteActionIndicatorLayer.IsLocalPlayer.
    /// </summary>
    private bool IsLocalPlayerStack()
    {
        if (_isLocalPlayerResolved) return _isLocalPlayer;
        try
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || nm.LocalClient == null) return false;
            var localObj = nm.LocalClient.PlayerObject;
            if (localObj == null) return false;
            if (OwnerRoot == null) return false;
            var ownerChar = OwnerRoot.GetComponent<Character>();
            if (ownerChar == null) ownerChar = OwnerRoot.GetComponentInParent<Character>();
            _isLocalPlayer = ownerChar != null && ownerChar.NetworkObject == localObj;
            _isLocalPlayerResolved = true;
            return _isLocalPlayer;
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            return false;
        }
    }

    /// <summary>
    /// Computes the (accent colour, display name) pair for this stack's speaker.
    /// Display name resolves via local player's CharacterRelation.GetRelationshipWith(speaker).KnowsName,
    /// falling back to "???" when KnowsName is false (or no relationship entry exists).
    /// </summary>
    private (Color accent, string displayName) ResolveSpeakerDisplay()
    {
        var speaker = OwnerRoot != null ? OwnerRoot.GetComponent<Character>() : null;
        if (speaker == null && OwnerRoot != null) speaker = OwnerRoot.GetComponentInParent<Character>();

        Color accent = speaker != null ? speaker.AccentColor : new Color(0.78f, 0.48f, 0.23f);

        string displayName = "???";
        if (speaker != null)
        {
            bool isLocalPlayer = IsLocalPlayerStack();
            if (isLocalPlayer)
            {
                displayName = speaker.CharacterName;
            }
            else
            {
                try
                {
                    var nm = NetworkManager.Singleton;
                    var localObj = nm?.LocalClient?.PlayerObject;
                    var localChar = localObj != null ? localObj.GetComponent<Character>() : null;
                    var relSys = localChar != null ? localChar.GetComponentInChildren<CharacterRelation>() : null;
                    var rel = relSys != null ? relSys.GetRelationshipWith(speaker) : null;
                    if (rel != null && rel.KnowsName)
                        displayName = speaker.CharacterName;
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        return (accent, displayName);
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

            var (accent, displayName) = ResolveSpeakerDisplay();
            instance.SetSpeakerDisplay(accent, displayName);

            // Tail handover: previously-newest (index 0 of the soon-to-grow list) loses its tail.
            if (_bubbles.Count > 0) _bubbles[0].SetIsNewest(false);

            instance.Setup(message, audioSource, voiceSO, pitch, typingSpeed, duration,
                onExpired: () => OnBubbleExpired(instance));

            instance.OnTypingStateChanged += OnTypingStateChanged;
            instance.SetStackOffsetPx(Vector2.zero);

            _bubbles.Insert(0, instance);
            instance.SetIsNewest(true);

            float pushHeightPx = instance.GetHeightPx() + _separatorSpacingPx;
            PushAllBubblesUp(pushHeightPx, instance);
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

            var (accent, displayName) = ResolveSpeakerDisplay();
            instance.SetSpeakerDisplay(accent, displayName);

            // Tail handover: previously-newest (index 0 of the soon-to-grow list) loses its tail.
            if (_bubbles.Count > 0) _bubbles[0].SetIsNewest(false);

            instance.SetupScripted(message, audioSource, voiceSO, pitch, typingSpeed, onTypingFinished);

            instance.OnTypingStateChanged += OnTypingStateChanged;
            instance.SetStackOffsetPx(Vector2.zero);

            _bubbles.Insert(0, instance);
            instance.SetIsNewest(true);

            float pushHeightPx = instance.GetHeightPx() + _separatorSpacingPx;
            PushAllBubblesUp(pushHeightPx, instance);
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
        _wrapperGroup.alpha = 1f;
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
        if (_bubbles.Count > 0) _bubbles[0].SetIsNewest(true);
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

    private void UnsubscribeEvents(SpeechBubbleInstance instance)
    {
        if (instance == null) return;
        instance.OnTypingStateChanged -= OnTypingStateChanged;
    }
}
