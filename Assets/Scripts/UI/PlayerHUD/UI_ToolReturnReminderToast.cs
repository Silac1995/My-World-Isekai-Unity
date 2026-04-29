using UnityEngine;
using MWI.UI.Notifications;

/// <summary>
/// STUB created in Task 6 of the tool-storage-primitive plan.
///
/// Real implementation lands in Task 7 — at that point this becomes a thin wrapper that
/// hands off to a dedicated toast prefab with a wrench icon, "Return tools" header, and
/// per-item lines parsed from the reason string.
///
/// For now we just route through the existing global <see cref="UI_Toast"/> channel so
/// the punch-out gate is end-to-end testable: a player who tries to leave Work while
/// holding tools will see a generic toast displaying the reason text built by
/// <c>CharacterJob.CanPunchOut</c> ("Return tools to the tool storage before punching
/// out: Pickaxe (Mine), Hammer (Forge).").
/// </summary>
public static class UI_ToolReturnReminderToast
{
    public static void Show(string reason)
    {
        if (string.IsNullOrEmpty(reason))
        {
            Debug.LogWarning("[ToolReturnReminderToast] Show called with empty reason; ignoring.");
            return;
        }

        // Route through the existing global toast channel (initialised by PlayerUI). Falls
        // back to a Debug.Log in headless / pre-PlayerUI scenarios — UI_Toast.Show already
        // logs a warning if the channel isn't wired up.
        Debug.Log($"[ToolReturnReminderToast STUB] {reason}");
        UI_Toast.Show(reason, ToastType.Warning, duration: 4f, title: "Return tools");
    }
}
