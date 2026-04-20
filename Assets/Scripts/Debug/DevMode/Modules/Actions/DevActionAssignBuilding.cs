using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// First concrete IDevAction. Assigns the selected Character as the owner of a
/// player-clicked Building. Supports both CommercialBuilding and ResidentialBuilding
/// polymorphically via their SetOwner entry points.
///
/// Flow:
///   1. IsAvailable requires sel.SelectedCharacter != null → button enabled only then.
///   2. Execute claims the click slot and enters an armed state. Button label flips to
///      "Pick a building… (ESC to cancel)" and the button is disabled.
///   3. Update polls for Mouse0 (or ESC). On a valid Building-layer hit, SetOwner runs
///      and the action releases the click slot. On ESC, the action cancels.
/// </summary>
public class DevActionAssignBuilding : MonoBehaviour, IDevAction
{
    [Header("References")]
    [SerializeField] private DevSelectionModule _selection;
    [SerializeField] private Button _button;
    [SerializeField] private TMP_Text _buttonLabel;

    [Header("Raycast")]
    [Tooltip("Layer mask for building picks. Defaults to 'Building' at runtime if left at zero.")]
    [SerializeField] private LayerMask _buildingLayerMask;
    private bool _layerMaskResolved;

    private const string DEFAULT_LABEL = "Assign Building as Owner";
    private const string ARMED_LABEL = "Pick a building… (ESC to cancel)";

    private bool _waitingForBuildingPick;
    private Character _pendingCharacter;

    public string Label => DEFAULT_LABEL;

    public bool IsAvailable(DevSelectionModule sel)
    {
        return sel != null && sel.SelectedCharacter != null;
    }

    public void Execute(DevSelectionModule sel)
    {
        if (!IsAvailable(sel))
        {
            Debug.LogWarning("<color=orange>[DevAction]</color> Assign Building: no character selected.");
            return;
        }

        _pendingCharacter = sel.SelectedCharacter;
        _waitingForBuildingPick = true;

        if (DevModeManager.Instance != null) DevModeManager.Instance.SetClickConsumer(this);
        SetButtonState(armed: true);

        Debug.Log($"<color=cyan>[DevAction]</color> Assign Building: pick a building for {_pendingCharacter.CharacterName} (ESC to cancel).");
    }

    // ─── Unity lifecycle ──────────────────────────────────────────────

    private void Start()
    {
        ResolveLayerMask();
        SetButtonState(armed: false);

        if (_button != null) _button.onClick.AddListener(OnButtonClicked);
        if (_selection != null) _selection.OnSelectionChanged += RefreshAvailability;
        if (DevModeManager.Instance != null)
        {
            DevModeManager.Instance.OnClickConsumerChanged += HandleClickConsumerChanged;
            DevModeManager.Instance.OnDevModeChanged += HandleDevModeChanged;
        }

        RefreshAvailability();
    }

    private void OnDestroy()
    {
        if (_button != null) _button.onClick.RemoveListener(OnButtonClicked);
        if (_selection != null) _selection.OnSelectionChanged -= RefreshAvailability;
        if (DevModeManager.Instance != null)
        {
            DevModeManager.Instance.OnClickConsumerChanged -= HandleClickConsumerChanged;
            DevModeManager.Instance.OnDevModeChanged -= HandleDevModeChanged;
        }
    }

    private void ResolveLayerMask()
    {
        if (_buildingLayerMask.value != 0)
        {
            _layerMaskResolved = true;
            return;
        }

        int layer = LayerMask.NameToLayer("Building");
        if (layer < 0)
        {
            Debug.LogError("<color=red>[DevAction]</color> 'Building' layer is missing from Tags & Layers. Assign Building will not function.");
            _layerMaskResolved = false;
            return;
        }
        _buildingLayerMask = 1 << layer;
        _layerMaskResolved = true;
    }

    // ─── Button + availability ────────────────────────────────────────

    private void OnButtonClicked()
    {
        if (_selection == null) return;
        Execute(_selection);
    }

    private void RefreshAvailability()
    {
        if (_button == null) return;
        _button.interactable = !_waitingForBuildingPick && IsAvailable(_selection);
    }

    private void SetButtonState(bool armed)
    {
        _waitingForBuildingPick = armed;
        if (_buttonLabel != null)
        {
            _buttonLabel.text = armed ? ARMED_LABEL : DEFAULT_LABEL;
        }
        RefreshAvailability();
    }

    private void Cancel(string reason)
    {
        if (!_waitingForBuildingPick) return;
        _waitingForBuildingPick = false;
        _pendingCharacter = null;
        SetButtonState(armed: false);
        if (DevModeManager.Instance != null) DevModeManager.Instance.ClearClickConsumer(this);
        Debug.Log($"<color=cyan>[DevAction]</color> Assign Building: {reason}.");
    }

    private void HandleClickConsumerChanged()
    {
        if (!_waitingForBuildingPick) return;
        if (DevModeManager.Instance == null) return;
        if (DevModeManager.Instance.ActiveClickConsumer == this) return;
        // Someone else claimed the click slot — cancel our pending pick.
        Cancel("superseded by another module");
    }

    private void HandleDevModeChanged(bool isEnabled)
    {
        if (!isEnabled && _waitingForBuildingPick)
        {
            Cancel("dev mode disabled");
        }
    }

    // ─── Click loop ───────────────────────────────────────────────────

    private void Update()
    {
        if (!_waitingForBuildingPick) return;
        if (DevModeManager.Instance == null || !DevModeManager.Instance.IsEnabled) return;
        if (DevModeManager.Instance.ActiveClickConsumer != this) return;
        if (!_layerMaskResolved) return;

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cancel("cancelled by user");
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
            Debug.LogWarning("<color=orange>[DevAction]</color> Camera.main is null — cannot pick building.");
            return;
        }

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, 500f, _buildingLayerMask))
        {
            Debug.LogWarning("<color=orange>[DevAction]</color> Click missed the Building layer.");
            return;
        }

        Building building = hit.collider.GetComponentInParent<Building>();
        if (building == null)
        {
            Debug.LogWarning("<color=orange>[DevAction]</color> Building layer hit but no Building component found in parent chain.");
            return;
        }

        if (_pendingCharacter == null)
        {
            Debug.LogError("<color=red>[DevAction]</color> Pending character was lost mid-action — cancelling.");
            Cancel("pending character null");
            return;
        }

        // CommercialBuilding is abstract; the pattern-match covers every concrete
        // subclass (Tavern, Shop, etc.) polymorphically. Same for ResidentialBuilding
        // subclasses. Order matters only if a subclass inherits from both — none does.
        if (building is CommercialBuilding commercial)
        {
            commercial.SetOwner(_pendingCharacter, null);
        }
        else if (building is ResidentialBuilding residential)
        {
            residential.SetOwner(_pendingCharacter);
        }
        else
        {
            Debug.LogWarning($"<color=orange>[DevAction]</color> {building.GetType().Name} does not support SetOwner. Stay armed, pick a different building.");
            return;
        }

        Debug.Log($"<color=green>[DevAction]</color> {_pendingCharacter.CharacterName} set as owner of {building.name}.");

        // Release the click slot; keep selection intact so user can chain further actions.
        Character doneChar = _pendingCharacter;
        _pendingCharacter = null;
        _waitingForBuildingPick = false;
        SetButtonState(armed: false);
        if (DevModeManager.Instance != null) DevModeManager.Instance.ClearClickConsumer(this);
    }
}
