using UnityEngine;
using MWI.UI.Notifications;

/// <summary>
/// Player-facing toast surfaced when <see cref="CharacterJob.CanPunchOut"/> blocks a Work→
/// non-Work schedule transition because the worker still carries items stamped with their
/// workplace's BuildingId (unreturned tools from the building's tool storage).
///
/// Implementation: thin wrapper that routes through the existing global
/// <see cref="UI_Toast"/> channel (initialised by PlayerUI). The plan originally specified a
/// dedicated singleton-on-demand prefab, but the existing toast system already handles
/// queueing, fade-in/out, on-screen positioning, and unscaled-time playback — building a
/// parallel prefab would duplicate that infrastructure for no UX gain.
///
/// Rate-limiting is upstream in <see cref="CharacterSchedule.NotifyPunchOutBlocked"/>
/// (one toast per 30 seconds real-time per worker, measured via <c>Time.unscaledTime</c>
/// per rule #26).
/// </summary>
public static class UI_ToolReturnReminderToast
{
    /// <summary>Display duration in seconds (real-time / unscaled).</summary>
    private const float DisplayDurationSeconds = 4f;

    /// <summary>Toast title shown above the tool-list message.</summary>
    private const string ToastTitle = "Return tools";

    public static void Show(string reason)
    {
        if (string.IsNullOrEmpty(reason)) return;

        UI_Toast.Show(reason, ToastType.Warning, DisplayDurationSeconds, ToastTitle);
    }
}
