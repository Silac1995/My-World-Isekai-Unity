using UnityEngine;
using TMPro;

public class UI_SessionManager : MonoBehaviour
{
    [Header("Connection Settings")]
    [SerializeField] private TMP_InputField _ipInput;
    [SerializeField] private TMP_InputField _portInput;

    public void Click_StartSolo()
    {
        UpdateConnectionParameters();
        GameSessionManager.Instance?.StartSolo();
    }

    public void Click_JoinMultiplayer()
    {
        UpdateConnectionParameters();
        GameSessionManager.Instance?.JoinMultiplayer();
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
