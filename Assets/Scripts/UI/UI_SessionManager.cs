using Unity.Netcode;
using UnityEngine;
using TMPro;

public class UI_SessionManager : MonoBehaviour
{
    [Header("Connection Settings")]
    [SerializeField] private TMP_InputField _ipInput;
    [SerializeField] private TMP_InputField _portInput;

    [Header("UI Panels")]
    [SerializeField] private GameObject _sessionButtonsPanel;
    [SerializeField] private GameObject _debugPanel;

    [Header("Notifications")]
    [SerializeField] private MWI.UI.Notifications.ToastNotificationChannel _toastChannel;

    private void OnDestroy()
    {
        UnsubscribeFromNetwork();
    }

    private bool _isSolo;

    public void Click_StartSolo()
    {
        UpdateConnectionParameters();
        _isSolo = true;
        SubscribeToNetwork();
        ShowToast("Starting Solo Session...", MWI.UI.Notifications.ToastType.Info);
        GameSessionManager.Instance?.StartSolo();
    }

    public void Click_JoinMultiplayer()
    {
        UpdateConnectionParameters();
        _isSolo = false;
        SubscribeToNetwork();
        ShowToast($"Connecting to {GameSessionManager.TargetIP}:{GameSessionManager.TargetPort}...", MWI.UI.Notifications.ToastType.Info);
        GameSessionManager.Instance?.JoinMultiplayer();
    }

    private void SubscribeToNetwork()
    {
        UnsubscribeFromNetwork();
        if (NetworkManager.Singleton == null) return;
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
    }

    private void UnsubscribeFromNetwork()
    {
        if (NetworkManager.Singleton == null) return;
        NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
    }

    private void OnClientConnected(ulong clientId)
    {
        // Host: only react to our own connection (clientId 0), ignore remote clients joining.
        // Client: every callback is for us since we only receive our own connection event.
        if (NetworkManager.Singleton.IsHost && clientId != 0) return;

        ShowToast("Connected!", MWI.UI.Notifications.ToastType.Success);
        HideSessionButtons();
        if (_isSolo && _debugPanel != null) _debugPanel.SetActive(true);
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            ShowToast("Disconnected from server.", MWI.UI.Notifications.ToastType.Error);
            ShowSessionButtons();
        }
    }

    public void HideSessionButtons()
    {
        if (_sessionButtonsPanel != null) _sessionButtonsPanel.SetActive(false);
    }

    private void ShowSessionButtons()
    {
        if (_sessionButtonsPanel != null) _sessionButtonsPanel.SetActive(true);
    }

    private void UpdateConnectionParameters()
    {
        if (_ipInput != null && !string.IsNullOrEmpty(_ipInput.text))
        {
            GameSessionManager.TargetIP = _ipInput.text.Trim();
        }

        if (_portInput != null && ushort.TryParse(_portInput.text.Trim(), out ushort port))
        {
            GameSessionManager.TargetPort = port;
        }
    }

    private void ShowToast(string message, MWI.UI.Notifications.ToastType type)
    {
        if (_toastChannel != null)
        {
            _toastChannel.Raise(new MWI.UI.Notifications.ToastNotificationPayload(
                message: message,
                type: type,
                duration: 4f
            ));
        }
    }
}
