using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// IInspectorView for Character targets. Owns the tab bar and 10 CharacterSubTab children.
/// Refreshes the currently visible sub-tab every frame; inactive sub-tabs are skipped.
/// </summary>
public class CharacterInspectorView : MonoBehaviour, IInspectorView
{
    [Serializable]
    public struct SubTabEntry
    {
        public Button TabButton;
        public GameObject Content;
        public CharacterSubTab Tab;
    }

    [Header("Sub-tabs (fill in prefab)")]
    [SerializeField] private SubTabEntry[] _subTabs = System.Array.Empty<SubTabEntry>();

    [Header("Labels")]
    [SerializeField] private TMPro.TMP_Text _headerLabel;

    private int _activeIndex = -1;
    private Character _target;

    public bool CanInspect(InteractableObject target)
    {
        return target is CharacterInteractable;
    }

    public void SetTarget(InteractableObject target)
    {
        if (target is CharacterInteractable ci)
        {
            _target = ci.Character;
        }
        else
        {
            _target = null;
        }

        UpdateHeader();
        if (_activeIndex < 0 && _subTabs.Length > 0) SwitchTab(0);
    }

    public void Clear()
    {
        _target = null;
        UpdateHeader();
        for (int i = 0; i < _subTabs.Length; i++)
        {
            if (_subTabs[i].Tab != null) _subTabs[i].Tab.Clear();
        }
    }

    private void Awake()
    {
        for (int i = 0; i < _subTabs.Length; i++)
        {
            int captured = i;
            if (_subTabs[i].TabButton != null)
            {
                _subTabs[i].TabButton.onClick.AddListener(() => SwitchTab(captured));
            }
        }
        if (_subTabs.Length > 0) SwitchTab(0);
    }

    private void OnDestroy()
    {
        for (int i = 0; i < _subTabs.Length; i++)
        {
            if (_subTabs[i].TabButton != null) _subTabs[i].TabButton.onClick.RemoveAllListeners();
        }
    }

    private void SwitchTab(int index)
    {
        if (index < 0 || index >= _subTabs.Length) return;
        _activeIndex = index;
        for (int i = 0; i < _subTabs.Length; i++)
        {
            if (_subTabs[i].Content != null) _subTabs[i].Content.SetActive(i == index);
        }
    }

    private void Update()
    {
        if (_target == null) return;
        if (_activeIndex < 0 || _activeIndex >= _subTabs.Length) return;
        var tab = _subTabs[_activeIndex].Tab;
        if (tab == null) return;
        tab.Refresh(_target); // CharacterSubTab.Refresh wraps its own try/catch.
    }

    private void UpdateHeader()
    {
        if (_headerLabel == null) return;
        _headerLabel.text = _target != null ? $"Inspecting: {_target.CharacterName}" : "Inspecting: —";
    }
}
