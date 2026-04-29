using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Root controller for the dev-mode Inspect tab. Listens to <see cref="DevSelectionModule"/> and
/// activates the first child view whose <c>CanInspect</c> returns true. Routes both selection kinds:
/// <list type="bullet">
///   <item><description><see cref="IInspectorView"/> for <see cref="InteractableObject"/> targets (Ctrl+Click pick).</description></item>
///   <item><description><see cref="IBuildingInspectorView"/> for raw <see cref="Building"/> targets (Alt+Click pick — buildings have no <c>InteractableObject</c> in their shell parent chain).</description></item>
/// </list>
/// Views are discovered at Awake; drop a new view into the prefab hierarchy and it becomes
/// live with no edits here. The two selection kinds are mutually exclusive on
/// <see cref="DevSelectionModule"/>, so at most one view is ever active.
/// </summary>
public class DevInspectModule : MonoBehaviour
{
    [Header("Wiring")]
    [Tooltip("Reference to the Select tab module. Wired in the prefab inspector.")]
    [SerializeField] private DevSelectionModule _selectionModule;

    [Header("Placeholder")]
    [Tooltip("Shown when no view matches the current selection (or nothing is selected).")]
    [SerializeField] private GameObject _placeholder;

    private readonly List<IInspectorView> _interactableViews = new();
    private readonly List<IBuildingInspectorView> _buildingViews = new();
    private MonoBehaviour _activeView;

    private void Awake()
    {
        CollectViews();
        ShowPlaceholder();
    }

    private void CollectViews()
    {
        _interactableViews.Clear();
        _buildingViews.Clear();

        foreach (var v in GetComponentsInChildren<IInspectorView>(true))
        {
            _interactableViews.Add(v);
            // Start every view inactive so Awake/OnEnable still fire but the scene is clean.
            if (v is MonoBehaviour mb) mb.gameObject.SetActive(false);
        }

        foreach (var v in GetComponentsInChildren<IBuildingInspectorView>(true))
        {
            _buildingViews.Add(v);
            if (v is MonoBehaviour mb) mb.gameObject.SetActive(false);
        }
    }

    private void OnEnable()
    {
        if (_selectionModule != null)
        {
            _selectionModule.OnInteractableSelectionChanged += HandleInteractableSelection;
            _selectionModule.OnBuildingSelectionChanged += HandleBuildingSelection;
            // Fire once so we sync with whatever is selected now.
            if (_selectionModule.SelectedBuilding != null)
            {
                HandleBuildingSelection(_selectionModule.SelectedBuilding);
            }
            else
            {
                HandleInteractableSelection(_selectionModule.SelectedInteractable);
            }
        }
    }

    private void OnDisable()
    {
        if (_selectionModule != null)
        {
            _selectionModule.OnInteractableSelectionChanged -= HandleInteractableSelection;
            _selectionModule.OnBuildingSelectionChanged -= HandleBuildingSelection;
        }
    }

    private void HandleInteractableSelection(InteractableObject target)
    {
        if (target == null)
        {
            // Don't clobber an active building view when the interactable selection clears.
            // The building selection event handles its own lifecycle independently.
            if (_activeView is IBuildingInspectorView) return;

            DeactivateActive();
            ShowPlaceholder();
            return;
        }

        IInspectorView match = null;
        for (int i = 0; i < _interactableViews.Count; i++)
        {
            var v = _interactableViews[i];
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

        ActivateView(match as MonoBehaviour);

        try { match.SetTarget(target); }
        catch (System.Exception e) { Debug.LogException(e, this); }

        if (_placeholder != null) _placeholder.SetActive(false);
    }

    private void HandleBuildingSelection(Building target)
    {
        if (target == null)
        {
            if (_activeView is IInspectorView) return;

            DeactivateActive();
            ShowPlaceholder();
            return;
        }

        IBuildingInspectorView match = null;
        for (int i = 0; i < _buildingViews.Count; i++)
        {
            var v = _buildingViews[i];
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

        ActivateView(match as MonoBehaviour);

        try { match.SetTarget(target); }
        catch (System.Exception e) { Debug.LogException(e, this); }

        if (_placeholder != null) _placeholder.SetActive(false);
    }

    private void ActivateView(MonoBehaviour view)
    {
        if (view == _activeView) return;
        DeactivateActive();
        _activeView = view;
        if (_activeView != null) _activeView.gameObject.SetActive(true);
    }

    private void DeactivateActive()
    {
        if (_activeView == null) return;
        try
        {
            switch (_activeView)
            {
                case IInspectorView iv: iv.Clear(); break;
                case IBuildingInspectorView bv: bv.Clear(); break;
            }
        }
        catch (System.Exception e) { Debug.LogException(e, this); }
        _activeView.gameObject.SetActive(false);
        _activeView = null;
    }

    private void ShowPlaceholder()
    {
        if (_placeholder != null) _placeholder.SetActive(true);
    }
}
