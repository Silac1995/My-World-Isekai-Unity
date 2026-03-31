using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Manages all active speech bubble instances for a single character.
/// Handles spawning, positioning, bubble cap, mouth controller, and separator visibility.
///
/// Cross-character collision avoidance (Habbo Hotel style):
/// - Uses a SphereCollider trigger on the SpeechZone layer to detect nearby stacks
/// - When this character speaks, ALL existing bubbles in ALL nearby stacks (and own)
///   are pushed UP by the new bubble's height
/// - New bubble always appears at the base (closest to character)
/// - Pushed bubbles never come back down — they stay where they were pushed
/// - Each bubble expires independently; no gap closing across characters
///
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
    [SerializeField] private float _speechZoneRadius = 15f;

    // ── Private Fields ─────────────────────────────────────────────────
    private readonly List<SpeechBubbleInstance> _bubbles = new List<SpeechBubbleInstance>();
    private readonly HashSet<SpeechBubbleStack> _nearbyStacks = new();
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
        _zoneCollider = GetComponent<SphereCollider>();
        _zoneCollider.isTrigger = true;
        _zoneCollider.radius = _speechZoneRadius;

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
    /// New bubble appears at base (Y=0). All existing nearby bubbles are pushed up.
    /// </summary>
    public void PushBubble(string message, float duration, float typingSpeed,
        AudioSource audioSource, VoiceSO voiceSO, float pitch)
    {
        try
        {
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

            // 3. Instantiate new bubble at base position
            var instance = Instantiate(_bubbleInstancePrefab, transform);

            // 4. Setup the bubble
            instance.Setup(message, audioSource, voiceSO, pitch, typingSpeed, duration,
                onExpired: () => OnBubbleExpired(instance));

            // 5. Subscribe to events
            instance.OnTypingStateChanged += OnTypingStateChanged;

            // 6. Insert at front (newest = index 0)
            _bubbles.Insert(0, instance);

            // 7. New bubble starts at base (Y=0)
            instance.SetTargetPosition(Vector3.zero);

            // 8. Push ALL existing bubbles (own + nearby) up by this bubble's height
            float pushHeight = instance.GetHeight() + _separatorSpacing;
            PushAllBubblesUp(pushHeight, instance);

            // 9. Update separator visibility
            UpdateSeparatorVisibility();
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
            instance.OnTypingStateChanged += OnTypingStateChanged;

            // 6. Insert at front
            _bubbles.Insert(0, instance);

            // 7. New bubble at base
            instance.SetTargetPosition(Vector3.zero);

            // 8. Push all existing bubbles up
            float pushHeight = instance.GetHeight() + _separatorSpacing;
            PushAllBubblesUp(pushHeight, instance);

            // 9. Update separators
            UpdateSeparatorVisibility();
        }
        catch (Exception e)
        {
            Debug.LogError($"[SpeechBubbleStack] Exception in PushScriptedBubble: {e.Message}\n{e.StackTrace}");
        }
    }

    /// <summary>
    /// Dismisses the bottom-most (newest) bubble in the stack (index 0).
    /// </summary>
    public void DismissBottom()
    {
        if (_bubbles.Count <= 0) return;

        var bottom = _bubbles[0];
        bottom.Dismiss(() => { RemoveBubble(bottom); });
    }

    /// <summary>
    /// Dismisses all active bubbles with exit animations.
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
        _typingCount = 0;
        _mouthController?.StopTalking();
    }

    /// <summary>
    /// Pushes all bubbles in this stack up by the given height.
    /// Called by nearby stacks when they spawn a new bubble.
    /// </summary>
    public void PushAllBubblesUpBy(float height)
    {
        foreach (var bubble in _bubbles)
        {
            if (bubble == null) continue;
            var currentTarget = bubble.GetTargetPosition();
            bubble.SetTargetPosition(new Vector3(currentTarget.x, currentTarget.y + height, currentTarget.z));
            bubble.ResetExpirationTimer();
        }
    }

    // ── Private Methods ────────────────────────────────────────────────

    /// <summary>
    /// Pushes ALL existing bubbles up — own older bubbles + all nearby stacks' bubbles.
    /// The newly created bubble (excludeInstance) is skipped since it's at base.
    /// </summary>
    private void PushAllBubblesUp(float height, SpeechBubbleInstance excludeInstance)
    {
        // Push own older bubbles up
        foreach (var bubble in _bubbles)
        {
            if (bubble == null || bubble == excludeInstance) continue;
            var currentTarget = bubble.GetTargetPosition();
            bubble.SetTargetPosition(new Vector3(currentTarget.x, currentTarget.y + height, currentTarget.z));
        }

        // Push all nearby stacks' bubbles up
        _nearbyStacks.RemoveWhere(s => s == null);
        foreach (var stack in _nearbyStacks)
        {
            if (stack.HasActiveBubbles)
            {
                stack.PushAllBubblesUpBy(height);
            }
        }
    }

    /// <summary>
    /// Callback when a bubble's expiration timer fires.
    /// Bubble fades out on its own — no gap closing, no repositioning.
    /// </summary>
    private void OnBubbleExpired(SpeechBubbleInstance instance)
    {
        RemoveBubble(instance);
    }

    /// <summary>
    /// Removes a bubble from the list. No repositioning — bubbles stay where they are.
    /// </summary>
    private void RemoveBubble(SpeechBubbleInstance instance)
    {
        if (instance == null) return;
        if (!_bubbles.Contains(instance)) return;

        UnsubscribeEvents(instance);
        _bubbles.Remove(instance);
        UpdateSeparatorVisibility();
        // No RecalculatePositions — bubbles stay where they were pushed (Habbo style)
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
                _mouthController?.StartTalking();
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
                _bubbles[i].SetSeparatorVisible(i > 0);
        }
    }

    /// <summary>
    /// Unsubscribes from a bubble instance's events.
    /// </summary>
    private void UnsubscribeEvents(SpeechBubbleInstance instance)
    {
        if (instance == null) return;
        instance.OnTypingStateChanged -= OnTypingStateChanged;
    }
}
