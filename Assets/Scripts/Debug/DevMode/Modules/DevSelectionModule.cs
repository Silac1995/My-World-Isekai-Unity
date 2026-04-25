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
    [Tooltip("Layer mask for the Ctrl+Click 'interior' pick (characters + furniture). Defaults at runtime to 'RigidBody + Furniture' when left at zero — buildings are excluded so their shell colliders don't block selection of furniture inside.")]
    [FormerlySerializedAs("_characterLayerMask")]
    [SerializeField] private LayerMask _selectableLayerMask;
    [Tooltip("Layer mask for the Alt+Click 'building' pick. Defaults at runtime to 'Building' when left at zero.")]
    [SerializeField] private LayerMask _buildingLayerMask;
    private bool _layerMaskResolved;

    // Default layer names per pick kind. Missing layers are tolerated — BuildMask skips any name
    // not present in Tags & Layers, so the project can drop a layer without breaking dev-mode selection.
    private static readonly string[] _defaultInteriorLayers = { "RigidBody", "Furniture" };
    private static readonly string[] _defaultBuildingLayers = { "Building" };

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
        if (_selectableLayerMask.value == 0) _selectableLayerMask = BuildMask(_defaultInteriorLayers);
        if (_buildingLayerMask.value == 0) _buildingLayerMask = BuildMask(_defaultBuildingLayers);

        if (_selectableLayerMask.value == 0 && _buildingLayerMask.value == 0)
        {
            Debug.LogError($"<color=red>[DevSelect]</color> None of the default selectable layers exist in Tags & Layers (interior: {string.Join(", ", _defaultInteriorLayers)} | building: {string.Join(", ", _defaultBuildingLayers)}). Selection will not function.");
            _layerMaskResolved = false;
            return;
        }

        _layerMaskResolved = true;
    }

    private static int BuildMask(string[] layerNames)
    {
        int mask = 0;
        for (int i = 0; i < layerNames.Length; i++)
        {
            int layer = LayerMask.NameToLayer(layerNames[i]);
            if (layer >= 0) mask |= 1 << layer;
        }
        return mask;
    }

    private void Update()
    {
        if (DevModeManager.Instance == null || !DevModeManager.Instance.IsEnabled) return;

        // Armed click-loop (legacy path — kept for discoverability via the toggle).
        // Global shortcuts (Ctrl+Click / Space+Click / ESC) are handled by DevModeManager so
        // they keep working regardless of which tab's content is currently active.
        if (_armedToggle == null || !_armedToggle.isOn) return;
        if (DevModeManager.Instance.ActiveClickConsumer != this) return;
        if (!_layerMaskResolved) return;

        // If Ctrl / Alt / Space is held, DevModeManager handles the click — don't double-fire here.
        if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) return;
        if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) return;
        if (Input.GetKey(KeyCode.Space)) return;

        if (!Input.GetMouseButtonDown(0)) return;

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            return;
        }

        if (TrySelectAtCursor(out string label))
        {
            _armedToggle.isOn = false;
            Debug.Log($"<color=cyan>[DevSelect]</color> Selected: {label}");
        }
    }

    // ─── Shortcut API (invoked by DevModeManager) ─────────────────────

    /// <summary>
    /// Ctrl+Click entry point. Raycasts the interior mask (RigidBody + Furniture by default) and
    /// selects the first InteractableObject — Character is a fallback when the hit collider
    /// has no InteractableObject component. Buildings are excluded so their shell colliders
    /// don't block selection of furniture / characters inside.
    /// </summary>
    public bool TrySelectAtCursor(out string label)
    {
        return TryRaycastAndSelect(_selectableLayerMask, out label);
    }

    /// <summary>
    /// Alt+Click entry point. Raycasts the building mask only and selects the first
    /// InteractableObject (a Building's interactable wrapper). Used to bypass the interior
    /// pick when the user explicitly wants the building shell, even when furniture or
    /// characters are present along the same ray.
    /// </summary>
    public bool TrySelectBuildingAtCursor(out string label)
    {
        return TryRaycastAndSelect(_buildingLayerMask, out label);
    }

    private bool TryRaycastAndSelect(LayerMask mask, out string label)
    {
        label = null;
        if (!_layerMaskResolved) return false;
        if (mask.value == 0) return false;

        Camera cam = Camera.main;
        if (cam == null)
        {
            Debug.LogWarning("<color=orange>[DevSelect]</color> Camera.main is null — cannot select.");
            return false;
        }

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, 500f, mask))
        {
            return false;
        }

        InteractableObject interactable = hit.collider.GetComponentInParent<InteractableObject>();
        if (interactable != null)
        {
            SetSelectedInteractable(interactable);
            label = interactable.gameObject.name;
            return true;
        }

        Character c = hit.collider.GetComponentInParent<Character>();
        if (c != null)
        {
            SetSelectedCharacter(c);
            label = c.CharacterName;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Disarm the armed toggle if on. Used by DevModeManager's ESC shortcut so a single ESC
    /// cancels both the active selection and any armed state in one keystroke.
    /// </summary>
    public void DisarmToggle()
    {
        if (_armedToggle != null && _armedToggle.isOn) _armedToggle.isOn = false;
    }

    /// <summary>True iff the armed Select toggle is currently on.</summary>
    public bool IsArmed => _armedToggle != null && _armedToggle.isOn;
}
