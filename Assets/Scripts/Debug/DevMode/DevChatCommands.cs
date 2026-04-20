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
}
