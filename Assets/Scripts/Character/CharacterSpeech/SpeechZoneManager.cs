using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lightweight scene-level singleton that coordinates cross-character speech bubble
/// collision avoidance. When a character pushes a new bubble, nearby characters'
/// stacks are offset upward to prevent overlap (Habbo Hotel style).
/// </summary>
public class SpeechZoneManager : MonoBehaviour
{
    private static SpeechZoneManager _instance;

    /// <summary>
    /// Lazy singleton accessor. Creates the manager GameObject on first access.
    /// Awake() sets _instance synchronously inside AddComponent, so it is safe
    /// for callers (e.g. SpeechBubbleStack.OnEnable) to use the returned value immediately.
    /// </summary>
    public static SpeechZoneManager Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject("SpeechZoneManager");
                go.AddComponent<SpeechZoneManager>();
            }
            return _instance;
        }
    }

    [SerializeField] private float _speechZoneRadius = 15f;

    private HashSet<SpeechBubbleStack> _stacks = new();

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
    }

    private void OnDestroy()
    {
        if (_instance == this)
            _instance = null;

        _stacks.Clear();
    }

    /// <summary>
    /// Registers a speech bubble stack for cross-character offset tracking.
    /// Called by SpeechBubbleStack.OnEnable.
    /// </summary>
    public void RegisterStack(SpeechBubbleStack stack)
    {
        _stacks.Add(stack);
    }

    /// <summary>
    /// Unregisters a speech bubble stack when it is disabled or destroyed.
    /// Called by SpeechBubbleStack.OnDisable.
    /// </summary>
    public void UnregisterStack(SpeechBubbleStack stack)
    {
        _stacks.Remove(stack);
    }

    /// <summary>
    /// Notifies the manager that a bubble was pushed on <paramref name="source"/>.
    /// All other stacks within <see cref="_speechZoneRadius"/> (XZ distance) that
    /// have active bubbles receive a cross-character offset equal to the new bubble's height.
    /// </summary>
    public void NotifyBubblePushed(SpeechBubbleStack source, float bubbleHeight)
    {
        try
        {
            if (source.OwnerRoot == null)
                return;

            Vector3 sourcePos = source.OwnerRoot.position;

            foreach (var stack in _stacks)
            {
                if (stack == source)
                    continue;

                if (!stack.HasActiveBubbles)
                    continue;

                if (stack.OwnerRoot == null)
                    continue;

                Vector2 diff = new(
                    stack.OwnerRoot.position.x - sourcePos.x,
                    stack.OwnerRoot.position.z - sourcePos.z
                );

                if (diff.magnitude <= _speechZoneRadius)
                {
                    stack.AddCrossCharacterOffset(bubbleHeight);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[SpeechZoneManager] Error in NotifyBubblePushed: {e.Message}");
            Debug.LogException(e);
        }
    }
}
