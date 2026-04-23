using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Root controller for the dev-mode Inspect tab. Listens to <see cref="DevSelectionModule"/> and
/// activates the first child <see cref="IInspectorView"/> whose <c>CanInspect</c> returns true.
/// Views are discovered at Awake; drop a new IInspectorView into the prefab hierarchy and it becomes
/// live with no edits here.
/// </summary>
public class DevInspectModule : MonoBehaviour
{
    [Header("Wiring")]
    [Tooltip("Reference to the Select tab module. Wired in the prefab inspector.")]
    [SerializeField] private DevSelectionModule _selectionModule;

    [Header("Placeholder")]
    [Tooltip("Shown when no IInspectorView matches the current selection (or nothing is selected).")]
    [SerializeField] private GameObject _placeholder;

    private readonly List<IInspectorView> _views = new();
    private IInspectorView _active;

    private void Awake()
    {
        CollectViews();
        ShowPlaceholder();
    }

    private void CollectViews()
    {
        _views.Clear();
        foreach (var v in GetComponentsInChildren<IInspectorView>(true))
        {
            _views.Add(v);
            // Start every view inactive so Awake/OnEnable still fire but the scene is clean.
            if (v is MonoBehaviour mb) mb.gameObject.SetActive(false);
        }
    }

    private void OnEnable()
    {
        if (_selectionModule != null)
        {
            _selectionModule.OnInteractableSelectionChanged += HandleSelection;
            // Fire once so we sync with whatever is selected now.
            HandleSelection(_selectionModule.SelectedInteractable);
        }
    }

    private void OnDisable()
    {
        if (_selectionModule != null)
        {
            _selectionModule.OnInteractableSelectionChanged -= HandleSelection;
        }
    }

    private void HandleSelection(InteractableObject target)
    {
        if (target == null)
        {
            DeactivateActive();
            ShowPlaceholder();
            return;
        }

        IInspectorView match = null;
        for (int i = 0; i < _views.Count; i++)
        {
            var v = _views[i];
            try
            {
                if (v != null && v.CanInspect(target)) { match = v; break; }
            }
            catch (System.Exception e)
            {
                Debug.LogException(e, this);
            }
        }

        if (match == null)
        {
            DeactivateActive();
            ShowPlaceholder();
            return;
        }

        if (match != _active)
        {
            DeactivateActive();
            _active = match;
            if (_active is MonoBehaviour mb) mb.gameObject.SetActive(true);
        }

        try { _active.SetTarget(target); }
        catch (System.Exception e) { Debug.LogException(e, this); }

        if (_placeholder != null) _placeholder.SetActive(false);
    }

    private void DeactivateActive()
    {
        if (_active == null) return;
        try { _active.Clear(); }
        catch (System.Exception e) { Debug.LogException(e, this); }
        if (_active is MonoBehaviour mb) mb.gameObject.SetActive(false);
        _active = null;
    }

    private void ShowPlaceholder()
    {
        if (_placeholder != null) _placeholder.SetActive(true);
    }
}
