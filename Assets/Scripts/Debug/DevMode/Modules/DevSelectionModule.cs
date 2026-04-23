using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>
/// Select tab of the dev-mode panel. Owns cross-cutting selection state and the click-to-select loop.
/// Holds a general <see cref="InteractableObject"/> selection; <see cref="SelectedCharacter"/> is a
/// back-compat convenience populated whenever the interactable resolves to a Character. All existing
/// <c>IDevAction</c> implementations that depend on <see cref="SelectedCharacter"/> keep working.
/// </summary>
public class DevSelectionModule : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Toggle _armedToggle;
    [SerializeField] private TMP_Text _selectedLabel;
    [SerializeField] private Button _clearButton;

    [Header("Raycast")]
    [Tooltip("Layer mask for picks. Defaults to 'RigidBody' at runtime if left at zero. Widen here when adding WorldItem/Building inspectors later.")]
    [FormerlySerializedAs("_characterLayerMask")]
    [SerializeField] private LayerMask _selectableLayerMask;
    private bool _layerMaskResolved;

    public InteractableObject SelectedInteractable { get; private set; }
    public Character SelectedCharacter { get; private set; }

    /// <summary>Fires whenever <see cref="SelectedInteractable"/> changes (including to/from null).</summary>
    public event Action<InteractableObject> OnInteractableSelectionChanged;

    /// <summary>Fires whenever <see cref="SelectedCharacter"/> changes. Kept for back-compat with existing IDevActions.</summary>
    public event Action OnSelectionChanged;

    private void Start()
    {
        ResolveLayerMask();
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
            if (_armedToggle != null && _armedToggle.isOn) _armedToggle.isOn = false;
            if (SelectedInteractable != null) ClearSelection();
        }
    }

    private void HandleClickConsumerChanged()
    {
        if (DevModeManager.Instance == null) return;
        if (DevModeManager.Instance.ActiveClickConsumer == this) return;
        if (_armedToggle != null && _armedToggle.isOn) _armedToggle.isOn = false;
    }

    private void HandleSceneUnloaded(Scene _)
    {
        if (SelectedInteractable != null) ClearSelection();
    }

    // ─── Public API ───────────────────────────────────────────────────

    /// <summary>General entry point. Accepts any InteractableObject; derives SelectedCharacter automatically.</summary>
    public void SetSelectedInteractable(InteractableObject interactable)
    {
        if (SelectedInteractable == interactable) return;

        SelectedInteractable = interactable;
        Character derived = null;
        if (interactable is CharacterInteractable ci)
        {
            derived = ci.Character;
        }
        UpdateDerivedCharacter(derived);
        RefreshLabel();
        OnInteractableSelectionChanged?.Invoke(SelectedInteractable);
    }

    /// <summary>Back-compat convenience. Prefer <see cref="SetSelectedInteractable"/>.</summary>
    public void SetSelectedCharacter(Character c)
    {
        if (c == null) { ClearSelection(); return; }
        var interactable = c.GetComponentInChildren<CharacterInteractable>();
        if (interactable == null)
        {
            // No CharacterInteractable on this Character — fall back to direct-character selection.
            if (SelectedCharacter == c) return;
            bool interactableChanged = SelectedInteractable != null;
            SelectedInteractable = null;
            UpdateDerivedCharacter(c);
            RefreshLabel();
            if (interactableChanged) OnInteractableSelectionChanged?.Invoke(null);
            return;
        }
        SetSelectedInteractable(interactable);
    }

    public void ClearSelection()
    {
        bool hadSomething = SelectedInteractable != null || SelectedCharacter != null;
        SelectedInteractable = null;
        UpdateDerivedCharacter(null);
        RefreshLabel();
        if (hadSomething) OnInteractableSelectionChanged?.Invoke(null);
    }

    private void UpdateDerivedCharacter(Character c)
    {
        if (SelectedCharacter == c) return;
        SelectedCharacter = c;
        OnSelectionChanged?.Invoke();
    }

    private void RefreshLabel()
    {
        if (_selectedLabel == null) return;
        if (SelectedCharacter != null)
        {
            _selectedLabel.text = $"Selected: {SelectedCharacter.CharacterName}";
        }
        else if (SelectedInteractable != null)
        {
            _selectedLabel.text = $"Selected: {SelectedInteractable.gameObject.name}";
        }
        else
        {
            _selectedLabel.text = "Selected: —";
        }
    }

    private void ResolveLayerMask()
    {
        if (_selectableLayerMask.value != 0)
        {
            _layerMaskResolved = true;
            return;
        }

        int layer = LayerMask.NameToLayer("RigidBody");
        if (layer < 0)
        {
            Debug.LogError("<color=red>[DevSelect]</color> 'RigidBody' layer is missing from Tags & Layers. Character pick will not function.");
            _layerMaskResolved = false;
            return;
        }
        _selectableLayerMask = 1 << layer;
        _layerMaskResolved = true;
    }

    private void Update()
    {
        if (DevModeManager.Instance == null || !DevModeManager.Instance.IsEnabled) return;
        if (_armedToggle == null || !_armedToggle.isOn) return;
        if (DevModeManager.Instance.ActiveClickConsumer != this) return;
        if (!_layerMaskResolved) return;

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
        if (!Physics.Raycast(ray, out RaycastHit hit, 500f, _selectableLayerMask))
        {
            Debug.LogWarning("<color=orange>[DevSelect]</color> Click missed the selectable layer.");
            return;
        }

        // General resolution: prefer an InteractableObject in the parent chain; fall back to a direct Character hit.
        InteractableObject interactable = hit.collider.GetComponentInParent<InteractableObject>();
        if (interactable != null)
        {
            SetSelectedInteractable(interactable);
            _armedToggle.isOn = false;
            Debug.Log($"<color=cyan>[DevSelect]</color> Selected interactable: {interactable.gameObject.name}");
            return;
        }

        Character c = hit.collider.GetComponentInParent<Character>();
        if (c != null)
        {
            SetSelectedCharacter(c);
            _armedToggle.isOn = false;
            Debug.Log($"<color=cyan>[DevSelect]</color> Selected character: {c.CharacterName}");
            return;
        }

        Debug.LogWarning("<color=orange>[DevSelect]</color> Hit found no InteractableObject or Character in the parent chain.");
    }
}
