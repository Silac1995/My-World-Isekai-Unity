using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Select tab of the dev-mode panel. Owns cross-cutting selection state (currently just
/// SelectedCharacter) and handles the click-to-select loop for characters. Actions attach
/// as children of the actions container and consume this module via the IDevAction contract.
/// </summary>
public class DevSelectionModule : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Toggle _armedToggle;
    [SerializeField] private TMP_Text _selectedLabel;
    [SerializeField] private Button _clearButton;

    public Character SelectedCharacter { get; private set; }

    /// <summary>Fires whenever SelectedCharacter changes (including to/from null).</summary>
    public event Action OnSelectionChanged;

    private void Start()
    {
        WireListeners();
        RefreshLabel();
    }

    private void OnDestroy()
    {
        UnwireListeners();
    }

    private void OnEnable()
    {
        SceneManager.sceneUnloaded += HandleSceneUnloaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneUnloaded -= HandleSceneUnloaded;
    }

    // ─── Wiring ───────────────────────────────────────────────────────

    private void WireListeners()
    {
        if (_armedToggle != null) _armedToggle.onValueChanged.AddListener(HandleArmedChanged);
        if (_clearButton != null) _clearButton.onClick.AddListener(ClearSelection);

        if (DevModeManager.Instance != null)
        {
            DevModeManager.Instance.OnDevModeChanged += HandleDevModeChanged;
            DevModeManager.Instance.OnClickConsumerChanged += HandleClickConsumerChanged;
        }
    }

    private void UnwireListeners()
    {
        if (_armedToggle != null) _armedToggle.onValueChanged.RemoveListener(HandleArmedChanged);
        if (_clearButton != null) _clearButton.onClick.RemoveListener(ClearSelection);

        if (DevModeManager.Instance != null)
        {
            DevModeManager.Instance.OnDevModeChanged -= HandleDevModeChanged;
            DevModeManager.Instance.OnClickConsumerChanged -= HandleClickConsumerChanged;
        }
    }

    private void HandleArmedChanged(bool armed)
    {
        Debug.Log($"<color=cyan>[DevSelect]</color> Armed: {armed}");
        if (DevModeManager.Instance == null) return;
        if (armed) DevModeManager.Instance.SetClickConsumer(this);
        else DevModeManager.Instance.ClearClickConsumer(this);
    }

    private void HandleDevModeChanged(bool isEnabled)
    {
        if (!isEnabled)
        {
            // Dev mode turned off — disarm and clear selection so we don't carry stale state.
            if (_armedToggle != null && _armedToggle.isOn) _armedToggle.isOn = false;
            if (SelectedCharacter != null) ClearSelection();
        }
    }

    private void HandleClickConsumerChanged()
    {
        if (DevModeManager.Instance == null) return;
        if (DevModeManager.Instance.ActiveClickConsumer == this) return;
        // Another module claimed the click stream — disarm our toggle.
        if (_armedToggle != null && _armedToggle.isOn) _armedToggle.isOn = false;
    }

    private void HandleSceneUnloaded(Scene _)
    {
        if (SelectedCharacter != null) ClearSelection();
    }

    // ─── Public API ───────────────────────────────────────────────────

    public void SetSelectedCharacter(Character c)
    {
        if (SelectedCharacter == c) return;
        SelectedCharacter = c;
        RefreshLabel();
        OnSelectionChanged?.Invoke();
    }

    public void ClearSelection()
    {
        if (SelectedCharacter == null)
        {
            RefreshLabel();
            return;
        }
        SelectedCharacter = null;
        RefreshLabel();
        OnSelectionChanged?.Invoke();
    }

    private void RefreshLabel()
    {
        if (_selectedLabel == null) return;
        _selectedLabel.text = SelectedCharacter != null
            ? $"Selected: {SelectedCharacter.CharacterName}"
            : "Selected: —";
    }

    // ─── Click loop ───────────────────────────────────────────────────

    private void Update()
    {
        if (DevModeManager.Instance == null || !DevModeManager.Instance.IsEnabled) return;
        if (_armedToggle == null || !_armedToggle.isOn) return;
        if (DevModeManager.Instance.ActiveClickConsumer != this) return;

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            _armedToggle.isOn = false;
            return;
        }

        if (!Input.GetMouseButtonDown(0)) return;

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            return;
        }

        Camera cam = Camera.main;
        if (cam == null)
        {
            Debug.LogWarning("<color=orange>[DevSelect]</color> Camera.main is null — cannot select.");
            return;
        }

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, 500f, ~0))
        {
            Debug.LogWarning("<color=orange>[DevSelect]</color> Raycast missed — click into the world.");
            return;
        }

        Character c = hit.collider.GetComponentInParent<Character>();
        if (c == null)
        {
            Debug.LogWarning("<color=orange>[DevSelect]</color> Click missed a Character.");
            return;
        }

        SetSelectedCharacter(c);
        _armedToggle.isOn = false;
        Debug.Log($"<color=cyan>[DevSelect]</color> Selected: {c.CharacterName}");
    }
}
