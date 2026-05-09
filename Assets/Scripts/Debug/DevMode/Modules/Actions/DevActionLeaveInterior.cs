using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Dev-mode action: queue CharacterLeaveInteriorAction on the selected character.
/// No click-armed state — runs immediately on button press.
/// Host-only (DevMode is host-only).
/// </summary>
public class DevActionLeaveInterior : MonoBehaviour, IDevAction
{
    [Header("References")]
    [SerializeField] private DevSelectionModule _selection;
    [SerializeField] private Button _button;
    [SerializeField] private TMP_Text _buttonLabel;

    private const string DEFAULT_LABEL = "Order: Leave Interior";

    public string Label => DEFAULT_LABEL;

    public bool IsAvailable(DevSelectionModule sel)
    {
        return sel != null && sel.SelectedCharacter != null;
    }

    public void Execute(DevSelectionModule sel)
    {
        if (!IsAvailable(sel))
        {
            Debug.LogWarning("<color=orange>[DevAction]</color> Leave Interior: no character selected.");
            return;
        }

        Character target = sel.SelectedCharacter;
        var action = new CharacterLeaveInteriorAction(target);
        bool queued = target.CharacterActions.ExecuteAction(action);
        Debug.Log($"<color=green>[DevAction]</color> Queued CharacterLeaveInteriorAction on {target.CharacterName}. (queued={queued})");
    }

    private void Start()
    {
        if (_buttonLabel != null) _buttonLabel.text = DEFAULT_LABEL;
        if (_button != null) _button.onClick.AddListener(OnButtonClicked);
        if (_selection != null) _selection.OnSelectionChanged += RefreshAvailability;
        RefreshAvailability();
    }

    private void OnDestroy()
    {
        if (_button != null) _button.onClick.RemoveListener(OnButtonClicked);
        if (_selection != null) _selection.OnSelectionChanged -= RefreshAvailability;
    }

    private void OnButtonClicked() { if (_selection != null) Execute(_selection); }

    private void RefreshAvailability()
    {
        if (_button == null) return;
        _button.interactable = IsAvailable(_selection);
    }
}
