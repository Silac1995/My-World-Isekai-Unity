using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Manages all active speech bubble instances for a single character.
/// Handles spawning, positioning, bubble cap enforcement, mouth controller
/// integration, separator visibility, and cross-character collision avoidance.
///
/// Cross-character collision uses a SphereCollider trigger on the SpeechZone layer.
/// When another SpeechBubbleStack enters the zone, it is tracked locally.
/// On bubble push, only nearby stacks (inside the trigger) are offset.
/// This is a plain MonoBehaviour — purely local visual management.
/// </summary>
[RequireComponent(typeof(SphereCollider))]
[RequireComponent(typeof(Rigidbody))]
public class SpeechBubbleStack : MonoBehaviour
{
    // ── Serialized Fields ──────────────────────────────────────────────
    [SerializeField] private SpeechBubbleInstance _bubbleInstancePrefab;
    [SerializeField] private int _maxBubbles = 5;
    [SerializeField] private float _separatorSpacing = 0.03f;
    [SerializeField] private float _maxCrossCharacterOffset = 350f;
    [SerializeField] private float _speechZoneRadius = 15f;

    // ── Private Fields ─────────────────────────────────────────────────
    private readonly List<SpeechBubbleInstance> _bubbles = new List<SpeechBubbleInstance>();
    private readonly HashSet<SpeechBubbleStack> _nearbyStacks = new();
    private float _crossCharacterOffset;
    private MouthController _mouthController;
    private int _typingCount;
    private SphereCollider _zoneCollider;

    // ── Public Properties ──────────────────────────────────────────────
    public Transform OwnerRoot { get; private set; }
    public bool IsAnyTyping => _typingCount > 0;
    public bool HasActiveBubbles => _bubbles.Count > 0;

    // ── Initialization ─────────────────────────────────────────────────

    /// <summary>
    /// Called by CharacterSpeech during setup. Stores owner root and mouth controller references.
    /// </summary>
    public void Init(Transform ownerRoot, MouthController mouthController)
    {
        OwnerRoot = ownerRoot;
        _mouthController = mouthController;
    }

    // ── Unity Lifecycle ────────────────────────────────────────────────

    private void Awake()
    {
        // Configure the sphere collider as a trigger zone for speech collision
        _zoneCollider = GetComponent<SphereCollider>();
        _zoneCollider.isTrigger = true;
        _zoneCollider.radius = _speechZoneRadius;

        // Rigidbody must be kinematic — we don't want physics forces, just trigger events
        var rb = GetComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
    }

    private void OnDisable()
    {
        try
        {
            ClearAll();
            _nearbyStacks.Clear();
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

    /// <summary>
    /// Pushes a standard timed bubble onto the stack.
    /// </summary>
    public void PushBubble(string message, float duration, float typingSpeed,
        AudioSource audioSource, VoiceSO voiceSO, float pitch)
    {
        try
        {
            // 0. Always reset cross-character offset — new bubble starts at base (closest to character)
            // Nearby stacks will be pushed up in step 9 to make room
            _crossCharacterOffset = 0f;

            // 1. Enforce bubble cap — dismiss oldest if at max
            if (_bubbles.Count >= _maxBubbles)
            {
                var oldest = _bubbles[_bubbles.Count - 1];
                _bubbles.RemoveAt(_bubbles.Count - 1);
                UnsubscribeEvents(oldest);
                oldest.Dismiss();
            }

            // 2. If the newest bubble is still typing, complete it immediately
            if (_bubbles.Count > 0 && _bubbles[0].IsTyping)
            {
                _bubbles[0].CompleteTypingImmediately();
            }

            // 3. Instantiate new bubble as child of this transform
            var instance = Instantiate(_bubbleInstancePrefab, transform);

            // 4. Setup the bubble
            instance.Setup(message, audioSource, voiceSO, pitch, typingSpeed, duration,
                onExpired: () => OnBubbleExpired(instance));

            // 5. Subscribe to events
            instance.OnHeightChanged += RecalculatePositions;
            instance.OnTypingStateChanged += OnTypingStateChanged;

            // 6. Insert at front (newest = index 0)
            _bubbles.Insert(0, instance);

            // 7. Update separator visibility: index 0 hidden, all others visible
            UpdateSeparatorVisibility();

            // 8. Recalculate positions
            RecalculatePositions();

            // 9. Push nearby stacks (cross-character collision avoidance)
            PushNearbyStacks(instance.GetHeight());
        }
        catch (Exception e)
        {
            Debug.LogError($"[SpeechBubbleStack] Exception in PushBubble: {e.Message}\n{e.StackTrace}");
        }
    }

    /// <summary>
    /// Pushes a scripted bubble (no auto-expiration) onto the stack.
    /// </summary>
    public void PushScriptedBubble(string message, float typingSpeed,
        AudioSource audioSource, VoiceSO voiceSO, float pitch, Action onTypingFinished)
    {
        try
        {
            // 0. Always reset cross-character offset — new bubble starts at base (closest to character)
            // Nearby stacks will be pushed up in step 9 to make room
            _crossCharacterOffset = 0f;

            // 1. Enforce bubble cap
            if (_bubbles.Count >= _maxBubbles)
            {
                var oldest = _bubbles[_bubbles.Count - 1];
                _bubbles.RemoveAt(_bubbles.Count - 1);
                UnsubscribeEvents(oldest);
                oldest.Dismiss();
            }

            // 2. Complete typing on newest if still typing
            if (_bubbles.Count > 0 && _bubbles[0].IsTyping)
            {
                _bubbles[0].CompleteTypingImmediately();
            }

            // 3. Instantiate
            var instance = Instantiate(_bubbleInstancePrefab, transform);

            // 4. Setup scripted
            instance.SetupScripted(message, audioSource, voiceSO, pitch, typingSpeed, onTypingFinished);

            // 5. Subscribe to events
            instance.OnHeightChanged += RecalculatePositions;
            instance.OnTypingStateChanged += OnTypingStateChanged;

            // 6. Insert at front
            _bubbles.Insert(0, instance);

            // 7. Update separators
            UpdateSeparatorVisibility();

            // 8. Recalculate positions
            RecalculatePositions();

            // 9. Push nearby stacks (cross-character collision avoidance)
            PushNearbyStacks(instance.GetHeight());
        }
        catch (Exception e)
        {
            Debug.LogError($"[SpeechBubbleStack] Exception in PushScriptedBubble: {e.Message}\n{e.StackTrace}");
        }
    }

    /// <summary>
    /// Dismisses the bottom-most (newest) bubble in the stack (index 0).
    /// Used for scripted dialogue advance — dismiss current line before showing next.
    /// </summary>
    public void DismissBottom()
    {
        if (_bubbles.Count <= 0) return;

        var bottom = _bubbles[0];
        bottom.Dismiss(() => { RemoveBubble(bottom); });
    }

    /// <summary>
    /// Dismisses all active bubbles with exit animations.
    /// Clears the internal list immediately to prevent stale callbacks.
    /// </summary>
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

    /// <summary>
    /// Dismisses only scripted bubbles.
    /// </summary>
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

        UpdateSeparatorVisibility();
        RecalculatePositions();
    }

    /// <summary>
    /// Immediately destroys all bubbles without exit animations.
    /// </summary>
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
        _crossCharacterOffset = 0f;
        _typingCount = 0;
        _mouthController?.StopTalking();
    }

    /// <summary>
    /// Sets the cross-character offset to the given height if it's larger than the current offset.
    /// This positions this stack's bubbles above the speaker's bubbles without accumulating.
    /// </summary>
    public void SetCrossCharacterOffset(float height)
    {
        _crossCharacterOffset = Mathf.Min(height, _maxCrossCharacterOffset);
        RecalculatePositions();

        // Reset expiration timers on all pushed bubbles so they stay visible during conversation
        foreach (var bubble in _bubbles)
        {
            bubble?.ResetExpirationTimer();
        }
    }

    /// <summary>
    /// Returns the total height of the entire stack including separators and cross-character offset.
    /// </summary>
    public float GetTotalStackHeight()
    {
        float total = _crossCharacterOffset;

        for (int i = 0; i < _bubbles.Count; i++)
        {
            if (_bubbles[i] != null)
            {
                total += _bubbles[i].GetHeight();
                if (i < _bubbles.Count - 1)
                    total += _separatorSpacing;
            }
        }

        return total;
    }

    // ── Private Methods ────────────────────────────────────────────────

    /// <summary>
    /// Sets nearby stacks' cross-character offset to at least this stack's total height.
    /// This ensures nearby bubbles sit above ours without accumulating offset on every exchange.
    /// </summary>
    private void PushNearbyStacks(float newBubbleHeight)
    {
        _nearbyStacks.RemoveWhere(s => s == null);

        float myTotalHeight = GetTotalStackHeight();

        foreach (var stack in _nearbyStacks)
        {
            if (stack.HasActiveBubbles)
            {
                stack.SetCrossCharacterOffset(myTotalHeight);
            }
        }
    }

    /// <summary>
    /// Recalculates vertical positions for all bubbles in the stack.
    /// Index 0 is the newest (bottom), stacking upward.
    /// </summary>
    private void RecalculatePositions()
    {
        float targetY = _crossCharacterOffset;

        for (int i = 0; i < _bubbles.Count; i++)
        {
            if (_bubbles[i] == null) continue;

            _bubbles[i].SetTargetPosition(new Vector3(0f, targetY, 0f));
            targetY += _bubbles[i].GetHeight() + _separatorSpacing;
        }
    }

    /// <summary>
    /// Callback when a bubble's expiration timer fires — removes it from the stack.
    /// </summary>
    private void OnBubbleExpired(SpeechBubbleInstance instance)
    {
        RemoveBubble(instance);
    }

    /// <summary>
    /// Shared removal logic: unsubscribe events, remove from list, update visuals.
    /// </summary>
    private void RemoveBubble(SpeechBubbleInstance instance)
    {
        if (instance == null) return;
        if (!_bubbles.Contains(instance)) return;

        UnsubscribeEvents(instance);
        _bubbles.Remove(instance);
        UpdateSeparatorVisibility();
        RecalculatePositions();
    }

    /// <summary>
    /// Tracks how many bubbles are currently typing to control mouth animation.
    /// </summary>
    private void OnTypingStateChanged(bool isTyping)
    {
        if (isTyping)
        {
            _typingCount++;
            if (_typingCount == 1)
            {
                _mouthController?.StartTalking();
            }
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

    /// <summary>
    /// Updates separator line visibility: hidden for index 0 (newest), visible for all others.
    /// </summary>
    private void UpdateSeparatorVisibility()
    {
        for (int i = 0; i < _bubbles.Count; i++)
        {
            if (_bubbles[i] != null)
            {
                _bubbles[i].SetSeparatorVisible(i > 0);
            }
        }
    }

    /// <summary>
    /// Unsubscribes from a bubble instance's events to prevent stale callbacks.
    /// </summary>
    private void UnsubscribeEvents(SpeechBubbleInstance instance)
    {
        if (instance == null) return;
        instance.OnHeightChanged -= RecalculatePositions;
        instance.OnTypingStateChanged -= OnTypingStateChanged;
    }
}
