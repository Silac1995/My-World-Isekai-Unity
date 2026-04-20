using System;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Host-only dev/god mode controller. Toggles a debug panel, owns the IsEnabled
/// event, and gates player input while active.
///
/// Activation:
///   - Editor & development builds: IsUnlocked = true on Awake; F3 toggles.
///   - Release builds: IsUnlocked = false; /devmode on unlocks, then F3 works.
///   - Clients (non-host): F3 and /devmode both log "host-only" and do nothing.
///
/// Authority: all dev-mode actions are host-only. See spec §12 for known
/// replication gaps on personality/traits/combat.
/// </summary>
public class DevModeManager : MonoBehaviour
{
    public static DevModeManager Instance { get; private set; }

    [Tooltip("Prefab of the root dev-mode panel. Loaded from Resources/UI/DevModePanel if null.")]
    [SerializeField] private GameObject _panelPrefab;

    public bool IsUnlocked { get; private set; }
    public bool IsEnabled { get; private set; }

    /// <summary>
    /// True iff dev mode is currently active on this machine. Read by PlayerController
    /// and PlayerInteractionDetector to suppress player input while the god tool has focus.
    /// </summary>
    public static bool SuppressPlayerInput => Instance != null && Instance.IsEnabled;

    public event Action<bool> OnDevModeChanged;

    private GameObject _panelInstance;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        IsUnlocked = true;
        Debug.Log("<color=magenta>[DevMode]</color> Unlocked by default in Editor/Development build. Press F3 to toggle.");
#else
        IsUnlocked = false;
        Debug.Log("<color=magenta>[DevMode]</color> Locked in release build. Type /devmode on in chat to unlock.");
#endif
    }

    private void Update()
    {
        if (!IsUnlocked) return;
        if (Input.GetKeyDown(KeyCode.F3))
        {
            TryToggle();
        }
    }

    /// <summary>
    /// Arms the feature: makes F3 responsive. Safe to call repeatedly.
    /// </summary>
    public void Unlock()
    {
        if (!IsUnlocked)
        {
            IsUnlocked = true;
            Debug.Log("<color=magenta>[DevMode]</color> Unlocked.");
        }
    }

    /// <summary>
    /// Fully relocks: disables AND clears IsUnlocked. Only used by explicit teardown
    /// (scene unload, or an explicit admin command if added later). Chat /devmode off
    /// uses Disable() instead, so the host doesn't need to retype /devmode on.
    /// </summary>
    public void Lock()
    {
        Disable();
        IsUnlocked = false;
        Debug.Log("<color=magenta>[DevMode]</color> Fully locked.");
    }

    public bool TryEnable()
    {
        if (!IsUnlocked)
        {
            Debug.LogWarning("<color=orange>[DevMode]</color> Not unlocked — run /devmode on first.");
            return false;
        }

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && !NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning("<color=orange>[DevMode]</color> Dev mode is host-only.");
            return false;
        }

        if (IsEnabled) return true;

        EnsurePanel();
        if (_panelInstance != null) _panelInstance.SetActive(true);
        IsEnabled = true;
        OnDevModeChanged?.Invoke(true);
        Debug.Log("<color=magenta>[DevMode]</color> Enabled.");
        return true;
    }

    public void Disable()
    {
        if (!IsEnabled) return;
        IsEnabled = false;
        if (_panelInstance != null) _panelInstance.SetActive(false);
        OnDevModeChanged?.Invoke(false);
        Debug.Log("<color=magenta>[DevMode]</color> Disabled.");
    }

    public bool TryToggle()
    {
        if (IsEnabled) { Disable(); return true; }
        return TryEnable();
    }

    private void EnsurePanel()
    {
        if (_panelInstance != null) return;

        GameObject prefab = _panelPrefab;
        if (prefab == null)
        {
            prefab = Resources.Load<GameObject>("UI/DevModePanel");
            if (prefab == null)
            {
                Debug.LogError("<color=red>[DevMode]</color> DevModePanel prefab not found at Resources/UI/DevModePanel.");
                return;
            }
        }

        _panelInstance = Instantiate(prefab);
        _panelInstance.SetActive(false);
        DontDestroyOnLoad(_panelInstance);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
