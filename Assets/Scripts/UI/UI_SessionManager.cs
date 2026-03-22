using UnityEngine;

public class UI_SessionManager : MonoBehaviour
{
    public void Click_StartSolo()
    {
        GameSessionManager.Instance?.StartSolo();
    }

    public void Click_JoinMultiplayer()
    {
        GameSessionManager.Instance?.JoinMultiplayer();
    }
}
