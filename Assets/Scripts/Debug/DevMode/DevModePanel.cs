using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Root of the dev-mode UI. Lives on the DevModePanel prefab. Listens to
/// DevModeManager.OnDevModeChanged to show/hide its content root, and wires a simple
/// tab-switch bar (one tab visible at a time). Tab content GameObjects' Awake/OnEnable
/// still fire once when the prefab is instantiated, so MonoBehaviours inside inactive
/// tabs retain their serialized state.
/// </summary>
public class DevModePanel : MonoBehaviour
{
    [Serializable]
    public struct TabEntry
    {
        public Button TabButton;
        public GameObject Content;
    }

    [SerializeField] private GameObject _contentRoot;
    [SerializeField] private List<TabEntry> _tabs = new List<TabEntry>();

    private int _activeTabIndex = -1;

    private void Start()
    {
        // Wire each tab button to switch to its own index.
        for (int i = 0; i < _tabs.Count; i++)
        {
            int captured = i;
            if (_tabs[i].TabButton != null)
            {
                _tabs[i].TabButton.onClick.AddListener(() => SwitchTab(captured));
            }
        }

        if (_tabs.Count > 0) SwitchTab(0);
    }

    private void OnDestroy()
    {
        for (int i = 0; i < _tabs.Count; i++)
        {
            if (_tabs[i].TabButton != null)
            {
                _tabs[i].TabButton.onClick.RemoveAllListeners();
            }
        }
    }

    private void OnEnable()
    {
        if (DevModeManager.Instance != null)
        {
            DevModeManager.Instance.OnDevModeChanged += HandleDevModeChanged;
            HandleDevModeChanged(DevModeManager.Instance.IsEnabled);
        }
    }

    private void OnDisable()
    {
        if (DevModeManager.Instance != null)
        {
            DevModeManager.Instance.OnDevModeChanged -= HandleDevModeChanged;
        }
    }

    private void HandleDevModeChanged(bool isEnabled)
    {
        if (_contentRoot != null)
        {
            _contentRoot.SetActive(isEnabled);
        }
    }

    public void SwitchTab(int index)
    {
        if (index < 0 || index >= _tabs.Count) return;
        for (int i = 0; i < _tabs.Count; i++)
        {
            if (_tabs[i].Content != null)
            {
                _tabs[i].Content.SetActive(i == index);
            }
        }
        _activeTabIndex = index;
    }
}
