using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A single row in a multi-entry list (combat styles or skills).
/// Layout: [Dropdown][Level IntField][X Button]
/// </summary>
public class DevSpawnRow : MonoBehaviour
{
    [SerializeField] private TMP_Dropdown _dropdown;
    [SerializeField] private TMP_InputField _levelField;
    [SerializeField] private Button _removeButton;

    public event Action<DevSpawnRow> OnRemoveClicked;

    public int SelectedIndex => _dropdown != null ? _dropdown.value : -1;

    public int Level
    {
        get
        {
            if (_levelField == null) return 1;
            if (int.TryParse(_levelField.text, out int v)) return Mathf.Max(1, v);
            return 1;
        }
    }

    private void Awake()
    {
        if (_removeButton != null)
        {
            _removeButton.onClick.AddListener(HandleRemoveClicked);
        }
    }

    private void OnDestroy()
    {
        if (_removeButton != null)
        {
            _removeButton.onClick.RemoveListener(HandleRemoveClicked);
        }
    }

    /// <summary>
    /// Populates the dropdown with string options and defaults the level to 1.
    /// </summary>
    public void Populate(System.Collections.Generic.List<string> options, int defaultLevel = 1)
    {
        if (_dropdown != null)
        {
            _dropdown.ClearOptions();
            _dropdown.AddOptions(options);
            _dropdown.value = 0;
            _dropdown.RefreshShownValue();
        }
        if (_levelField != null)
        {
            _levelField.text = defaultLevel.ToString();
        }
    }

    private void HandleRemoveClicked()
    {
        OnRemoveClicked?.Invoke(this);
    }
}
