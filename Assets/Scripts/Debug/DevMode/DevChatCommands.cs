using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Static command parser invoked by UI_ChatBar when the submitted text starts with "/".
/// All commands are host-only for now.
/// </summary>
public static class DevChatCommands
{
    /// <summary>
    /// Entry point. rawInput includes the leading "/". Returns silently (no speech)
    /// once handled — caller suppresses the Say() path when input starts with "/".
    /// </summary>
    public static void Handle(string rawInput)
    {
        if (string.IsNullOrWhiteSpace(rawInput) || rawInput.Length < 2 || rawInput[0] != '/') return;

        string body = rawInput.Substring(1).Trim();
        if (string.IsNullOrEmpty(body)) return;

        string[] parts = body.Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
        string cmd = parts[0].ToLowerInvariant();

        switch (cmd)
        {
            case "devmode":
                HandleDevmode(parts);
                break;
            case "timeskip":
                HandleTimeSkip(parts);
                break;
            case "togglebars":
            case "showbars":
                HandleToggleBars(parts);
                break;
            default:
                Debug.LogWarning($"<color=orange>[DevChat]</color> Unknown command: /{cmd}");
                break;
        }
    }

    private static void HandleDevmode(string[] parts)
    {
        // Host check
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && !NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning("<color=orange>[DevChat]</color> Dev mode is host-only.");
            return;
        }

        if (DevModeManager.Instance == null)
        {
            Debug.LogError("<color=red>[DevChat]</color> DevModeManager is not present in the scene.");
            return;
        }

        if (parts.Length < 2)
        {
            Debug.Log("<color=magenta>[DevChat]</color> Usage: /devmode on | off");
            return;
        }

        string arg = parts[1].ToLowerInvariant();
        switch (arg)
        {
            case "on":
                DevModeManager.Instance.Unlock();
                DevModeManager.Instance.TryEnable();
                break;
            case "off":
                DevModeManager.Instance.Disable();
                break;
            default:
                Debug.Log("<color=magenta>[DevChat]</color> Usage: /devmode on | off");
                break;
        }
    }

    /// <summary>
    /// /togglebars [on|off]  — toggles remote-character action indicators (NPC + remote player
    /// progress arcs above their heads). No-arg flips the current state. Persists via PlayerPrefs
    /// (key RemoteActionIndicatorLayer.PlayerPrefsKey) so the choice survives restarts.
    /// Client-side: each peer toggles its own HUD layer; not host-gated.
    /// </summary>
    private static void HandleToggleBars(string[] parts)
    {
        var layer = RemoteActionIndicatorLayer.Local;
        if (layer == null)
        {
            Debug.LogWarning("<color=orange>[DevChat]</color> RemoteActionIndicatorLayer.Local is null — author UI_RemoteActionIndicatorLayer under UI_PlayerHUD's Canvas in the scene.");
            return;
        }

        bool target;
        if (parts.Length >= 2)
        {
            string arg = parts[1].ToLowerInvariant();
            if (arg == "on" || arg == "true" || arg == "1") target = true;
            else if (arg == "off" || arg == "false" || arg == "0") target = false;
            else { Debug.Log("<color=magenta>[DevChat]</color> Usage: /togglebars [on|off]"); return; }
        }
        else
        {
            target = !layer.IsEnabled;
        }

        layer.SetEnabled(target);
        Debug.Log($"<color=magenta>[DevChat]</color> Remote action bars: {(target ? "ON" : "OFF")} (persisted).");
    }

    private static void HandleTimeSkip(string[] parts)
    {
        // Host check — same shape as devmode.
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && !NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning("<color=orange>[DevChat]</color> /timeskip is host-only.");
            return;
        }
        if (parts.Length < 2 || !int.TryParse(parts[1], out int hours))
        {
            Debug.Log("<color=magenta>[DevChat]</color> Usage: /timeskip <hours>  (1-168)");
            return;
        }
        if (MWI.Time.TimeSkipController.Instance == null)
        {
            Debug.LogError("<color=red>[DevChat]</color> TimeSkipController is not present in the scene.");
            return;
        }
        bool ok = MWI.Time.TimeSkipController.Instance.RequestSkip(hours, force: true);
        Debug.Log($"<color=magenta>[DevChat]</color> /timeskip {hours} → {(ok ? "started" : "rejected (see prior log)")}.");
    }
}
