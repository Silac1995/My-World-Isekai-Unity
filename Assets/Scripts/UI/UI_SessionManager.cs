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

    public void Click_StartSolo()
    {
        UpdateConnectionParameters();
        GameSessionManager.Instance?.StartSolo();
        HideSessionButtons();
        if (_debugPanel != null) _debugPanel.SetActive(true);
    }

    public void Click_JoinMultiplayer()
    {
        UpdateConnectionParameters();
        GameSessionManager.Instance?.JoinMultiplayer();
        HideSessionButtons();
    }

    private void HideSessionButtons()
    {
        if (_sessionButtonsPanel != null) _sessionButtonsPanel.SetActive(false);
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
}
