/// <summary>
/// A view that knows how to display one kind of <see cref="Building"/>. Parallel to
/// <see cref="IInspectorView"/> but typed to <c>Building</c> because buildings have no
/// <see cref="InteractableObject"/> in their shell parent chain — the chain is
/// <c>Building : ComplexRoom : Room : Zone : NetworkBehaviour</c>. <see cref="DevInspectModule"/>
/// discovers implementations via <c>GetComponentsInChildren</c> at Awake.
/// </summary>
public interface IBuildingInspectorView
{
    /// <summary>True if this view is capable of displaying the given target.</summary>
    bool CanInspect(Building target);

    /// <summary>Bind the view to a fresh target. Called when building selection changes and CanInspect returned true.</summary>
    void SetTarget(Building target);

    /// <summary>Release the current target and reset internal state. Called when building selection is cleared.</summary>
    void Clear();
}
