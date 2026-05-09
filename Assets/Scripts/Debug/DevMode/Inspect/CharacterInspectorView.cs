using System;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// IInspectorView for Character targets. Owns the tab bar and 10 CharacterSubTab children.
/// Refreshes the currently visible sub-tab every frame; inactive sub-tabs are skipped.
/// Also exposes 3 dev-mode copy buttons (UID / Origin World GUID / both) that put the
/// inspected character's identity onto the system clipboard via GUIUtility.systemCopyBuffer.
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

    [Header("Identity copy buttons (clipboard)")]
    [Tooltip("Copies Character.CharacterId (the persistent UID) to the system clipboard.")]
    [SerializeField] private Button _copyUidButton;
    [Tooltip("Copies Character.OriginWorldGuid to the system clipboard.")]
    [SerializeField] private Button _copyWorldGuidButton;
    [Tooltip("Copies Name + UID + Origin World GUID as a multi-line block to the system clipboard.")]
    [SerializeField] private Button _copyAllButton;

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
        UpdateCopyButtonInteractable();
        if (_activeIndex < 0 && _subTabs.Length > 0) SwitchTab(0);
    }

    public void Clear()
    {
        _target = null;
        UpdateHeader();
        UpdateCopyButtonInteractable();
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

        if (_copyUidButton != null) _copyUidButton.onClick.AddListener(OnCopyUidClicked);
        if (_copyWorldGuidButton != null) _copyWorldGuidButton.onClick.AddListener(OnCopyWorldGuidClicked);
        if (_copyAllButton != null) _copyAllButton.onClick.AddListener(OnCopyAllClicked);
        UpdateCopyButtonInteractable();

        if (_subTabs.Length > 0) SwitchTab(0);
    }

    private void OnDestroy()
    {
        for (int i = 0; i < _subTabs.Length; i++)
        {
            if (_subTabs[i].TabButton != null) _subTabs[i].TabButton.onClick.RemoveAllListeners();
        }

        if (_copyUidButton != null) _copyUidButton.onClick.RemoveListener(OnCopyUidClicked);
        if (_copyWorldGuidButton != null) _copyWorldGuidButton.onClick.RemoveListener(OnCopyWorldGuidClicked);
        if (_copyAllButton != null) _copyAllButton.onClick.RemoveListener(OnCopyAllClicked);
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

    private void UpdateCopyButtonInteractable()
    {
        bool hasTarget = _target != null;
        if (_copyUidButton != null) _copyUidButton.interactable = hasTarget;
        if (_copyWorldGuidButton != null) _copyWorldGuidButton.interactable = hasTarget;
        if (_copyAllButton != null) _copyAllButton.interactable = hasTarget;
    }

    private void OnCopyUidClicked()
    {
        if (_target == null) return;
        try
        {
            string uid = _target.CharacterId ?? string.Empty;
            GUIUtility.systemCopyBuffer = uid;
            Debug.Log($"[DevMode] Copied character UID to clipboard: '{uid}'", this);
        }
        catch (Exception e)
        {
            Debug.LogException(e, this);
        }
    }

    private void OnCopyWorldGuidClicked()
    {
        if (_target == null) return;
        try
        {
            string world = _target.OriginWorldGuid ?? string.Empty;
            GUIUtility.systemCopyBuffer = world;
            Debug.Log($"[DevMode] Copied origin world GUID to clipboard: '{world}'", this);
        }
        catch (Exception e)
        {
            Debug.LogException(e, this);
        }
    }

    private void OnCopyAllClicked()
    {
        if (_target == null) return;
        try
        {
            var sb = new StringBuilder(256);
            sb.Append("Name: ").AppendLine(_target.CharacterName ?? string.Empty);
            sb.Append("CharacterId: ").AppendLine(_target.CharacterId ?? string.Empty);
            sb.Append("OriginWorldGuid: ").Append(_target.OriginWorldGuid ?? string.Empty);
            string text = sb.ToString();
            GUIUtility.systemCopyBuffer = text;
            Debug.Log($"[DevMode] Copied character identity to clipboard:\n{text}", this);
        }
        catch (Exception e)
        {
            Debug.LogException(e, this);
        }
    }
}
