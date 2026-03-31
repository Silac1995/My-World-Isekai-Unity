using UnityEngine;

/// <summary>
/// Stub for SpeechZoneManager — manages cross-character speech bubble offset zones.
/// Full implementation in Task 3. This stub exists so SpeechBubbleStack compiles.
/// </summary>
public class SpeechZoneManager : MonoBehaviour
{
    public static SpeechZoneManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// Registers a speech bubble stack for cross-character offset tracking.
    /// </summary>
    public void RegisterStack(SpeechBubbleStack stack)
    {
        // Stub — full implementation in Task 3
    }

    /// <summary>
    /// Unregisters a speech bubble stack when it is disabled or destroyed.
    /// </summary>
    public void UnregisterStack(SpeechBubbleStack stack)
    {
        // Stub — full implementation in Task 3
    }

    /// <summary>
    /// Notifies the manager that a bubble was pushed, so nearby stacks can be offset.
    /// </summary>
    public void NotifyBubblePushed(SpeechBubbleStack source, float bubbleHeight)
    {
        // Stub — full implementation in Task 3
    }
}
