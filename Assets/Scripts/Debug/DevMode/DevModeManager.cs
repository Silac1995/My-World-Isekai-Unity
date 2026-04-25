using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
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
    /// and PlayerInteractionDetector to suppress gameplay action inputs (right-click move,
    /// TAB target, Space attack, E interact) while the god tool has focus. WASD movement
    /// is still allowed — the player controller swaps in <see cref="GodModeMovementSpeed"/>
    /// so flying around the scene stays fast.
    /// </summary>
    public static bool SuppressPlayerInput => Instance != null && Instance.IsEnabled;

    /// <summary>
    /// WASD movement speed (units/second) used by PlayerController while dev mode is active.
    /// </summary>
    public const float GodModeMovementSpeed = 17f;

    public event Action<bool> OnDevModeChanged;

    /// <summary>
    /// The MonoBehaviour that currently owns the click stream (e.g. the active dev module's
    /// armed state). Only one module consumes clicks at a time; when a new module claims the
    /// slot, the previous owner is evicted and auto-disarms via OnClickConsumerChanged.
    /// </summary>
    public MonoBehaviour ActiveClickConsumer { get; private set; }

    /// <summary>
    /// Fires whenever ActiveClickConsumer changes. Click-reading modules subscribe to disarm
    /// themselves when they are no longer the owner.
    /// </summary>
    public event Action OnClickConsumerChanged;

    private GameObject _panelInstance;

    // Cached references on the instantiated panel; populated in EnsurePanel so global shortcuts
    // can dispatch to the modules regardless of which tab is currently active.
    private DevSelectionModule _selectionModule;
    private DevSpawnModule _spawnModule;

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

        // Global shortcuts — active only while dev mode is enabled. These live here (not on
        // individual tab modules) because tab content GameObjects are deactivated when another
        // tab is selected, which would otherwise suspend their Update loop.
        if (IsEnabled) HandleGlobalShortcuts();
    }

    private void HandleGlobalShortcuts()
    {
        if (IsTextInputFocused()) return;

        bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
        bool space = Input.GetKey(KeyCode.Space);

        // ESC — cancel everything: clear selection and disarm any armed toggle.
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            bool handled = false;
            if (_selectionModule != null && _selectionModule.SelectedInteractable != null)
            {
                _selectionModule.ClearSelection();
                handled = true;
            }
            if (_selectionModule != null && _selectionModule.IsArmed)
            {
                _selectionModule.DisarmToggle();
                handled = true;
            }
            if (_spawnModule != null && _spawnModule.IsArmed)
            {
                _spawnModule.DisarmToggle();
                handled = true;
            }
            if (handled) Debug.Log("<color=magenta>[DevMode]</color> ESC — cancelled");
        }

        // Ctrl + Left-Click → interior select (RigidBody + Furniture). Mutually exclusive with Alt and Space.
        if (ctrl && !alt && !space && Input.GetMouseButtonDown(0))
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
            if (_selectionModule != null && _selectionModule.TrySelectAtCursor(out string label))
            {
                Debug.Log($"<color=magenta>[DevMode]</color> Ctrl+Click selected: {label}");
            }
        }

        // Alt + Left-Click → building select (Building layer only). Mutually exclusive with Ctrl and Space.
        if (alt && !ctrl && !space && Input.GetMouseButtonDown(0))
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
            if (_selectionModule != null && _selectionModule.TrySelectBuildingAtCursor(out string label))
            {
                Debug.Log($"<color=magenta>[DevMode]</color> Alt+Click selected building: {label}");
            }
        }

        // Space + Left-Click → spawn. Mutually exclusive with the Select shortcuts.
        if (space && !ctrl && !alt && Input.GetMouseButtonDown(0))
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
            if (_spawnModule != null && _spawnModule.TrySpawnAtCursor())
            {
                Debug.Log("<color=magenta>[DevMode]</color> Space+Click spawned");
            }
        }
    }

    private static bool IsTextInputFocused()
    {
        if (EventSystem.current == null) return false;
        var sel = EventSystem.current.currentSelectedGameObject;
        if (sel == null) return false;
        return sel.GetComponent<TMP_InputField>() != null
            || sel.GetComponent<UnityEngine.UI.InputField>() != null;
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
        ActiveClickConsumer = null;
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

    /// <summary>
    /// Claims the click slot for the given consumer. If a different consumer held the slot,
    /// OnClickConsumerChanged fires so the previous owner can auto-disarm. Passing null
    /// releases the slot (same as ClearClickConsumer).
    /// </summary>
    public void SetClickConsumer(MonoBehaviour consumer)
    {
        if (ActiveClickConsumer == consumer) return;
        ActiveClickConsumer = consumer;
        OnClickConsumerChanged?.Invoke();
    }

    /// <summary>
    /// Releases the click slot, but only if the given consumer is the current owner. Other
    /// callers are ignored — prevents a stale subscriber from clearing someone else's claim.
    /// </summary>
    public void ClearClickConsumer(MonoBehaviour consumer)
    {
        if (ActiveClickConsumer != consumer) return;
        ActiveClickConsumer = null;
        OnClickConsumerChanged?.Invoke();
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

        // Cache module references via includeInactive so we find them even when their owning
        // tab content GameObject is disabled (the non-current tab state).
        _selectionModule = _panelInstance.GetComponentInChildren<DevSelectionModule>(true);
        _spawnModule = _panelInstance.GetComponentInChildren<DevSpawnModule>(true);
        if (_selectionModule == null) Debug.LogWarning("<color=orange>[DevMode]</color> DevSelectionModule not found on panel — shortcuts will no-op for selection.");
        if (_spawnModule == null) Debug.LogWarning("<color=orange>[DevMode]</color> DevSpawnModule not found on panel — shortcuts will no-op for spawning.");
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
