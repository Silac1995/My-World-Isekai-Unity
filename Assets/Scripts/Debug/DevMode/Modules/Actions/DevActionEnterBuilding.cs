using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Dev-mode action: queue CharacterEnterBuildingAction on the selected character,
/// targeting a building picked by the next mouse click on the Building layer.
/// Host-only (DevMode is host-only).
/// </summary>
public class DevActionEnterBuilding : MonoBehaviour, IDevAction
{
    [Header("References")]
    [SerializeField] private DevSelectionModule _selection;
    [SerializeField] private Button _button;
    [SerializeField] private TMP_Text _buttonLabel;

    [Header("Raycast")]
    [SerializeField] private LayerMask _buildingLayerMask;
    private bool _layerMaskResolved;

    private const string DEFAULT_LABEL = "Order: Enter Building";
    private const string ARMED_LABEL = "Pick a building to enter… (ESC to cancel)";

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
            Debug.LogWarning("<color=orange>[DevAction]</color> Enter Building: no character selected.");
            return;
        }

        _pendingCharacter = sel.SelectedCharacter;
        _waitingForBuildingPick = true;

        if (DevModeManager.Instance != null) DevModeManager.Instance.SetClickConsumer(this);
        SetButtonState(armed: true);

        Debug.Log($"<color=cyan>[DevAction]</color> Enter Building: pick a building for {_pendingCharacter.CharacterName} (ESC to cancel).");
    }

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
        if (_buildingLayerMask.value != 0) { _layerMaskResolved = true; return; }
        int layer = LayerMask.NameToLayer("Building");
        if (layer < 0)
        {
            Debug.LogError("<color=red>[DevAction]</color> 'Building' layer is missing.");
            _layerMaskResolved = false;
            return;
        }
        _buildingLayerMask = 1 << layer;
        _layerMaskResolved = true;
    }

    private void OnButtonClicked() { if (_selection != null) Execute(_selection); }

    private void RefreshAvailability()
    {
        if (_button == null) return;
        _button.interactable = _layerMaskResolved && !_waitingForBuildingPick && IsAvailable(_selection);
    }

    private void SetButtonState(bool armed)
    {
        _waitingForBuildingPick = armed;
        if (_buttonLabel != null) _buttonLabel.text = armed ? ARMED_LABEL : DEFAULT_LABEL;
        RefreshAvailability();
    }

    private void Cancel(string reason)
    {
        if (!_waitingForBuildingPick) return;
        _waitingForBuildingPick = false;
        _pendingCharacter = null;
        SetButtonState(armed: false);
        if (DevModeManager.Instance != null) DevModeManager.Instance.ClearClickConsumer(this);
        Debug.Log($"<color=cyan>[DevAction]</color> Enter Building: {reason}.");
    }

    private void HandleClickConsumerChanged()
    {
        if (!_waitingForBuildingPick) return;
        if (DevModeManager.Instance == null) return;
        if (DevModeManager.Instance.ActiveClickConsumer == this) return;
        Cancel("superseded by another module");
    }

    private void HandleDevModeChanged(bool isEnabled)
    {
        if (!isEnabled && _waitingForBuildingPick) Cancel("dev mode disabled");
    }

    private void Update()
    {
        if (!_waitingForBuildingPick) return;
        if (DevModeManager.Instance == null || !DevModeManager.Instance.IsEnabled) return;
        if (DevModeManager.Instance.ActiveClickConsumer != this) return;
        if (!_layerMaskResolved) return;

        if (Input.GetKeyDown(KeyCode.Escape)) { Cancel("cancelled by user"); return; }
        if (!Input.GetMouseButtonDown(0)) return;

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        Camera cam = Camera.main;
        if (cam == null) { Debug.LogWarning("<color=orange>[DevAction]</color> Camera.main is null."); return; }

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, 500f, _buildingLayerMask))
        {
            Debug.LogWarning("<color=orange>[DevAction]</color> Click missed the Building layer.");
            return;
        }

        Building building = hit.collider.GetComponentInParent<Building>();
        if (building == null) { Debug.LogWarning("<color=orange>[DevAction]</color> No Building in parent."); return; }

        if (_pendingCharacter == null) { Cancel("pending character lost"); return; }

        var action = new CharacterEnterBuildingAction(_pendingCharacter, building);
        bool queued = _pendingCharacter.CharacterActions.ExecuteAction(action);
        Debug.Log($"<color=green>[DevAction]</color> Queued CharacterEnterBuildingAction on {_pendingCharacter.CharacterName} → {building.name}. (queued={queued})");

        _pendingCharacter = null;
        _waitingForBuildingPick = false;
        SetButtonState(armed: false);
        if (DevModeManager.Instance != null) DevModeManager.Instance.ClearClickConsumer(this);
    }
}
