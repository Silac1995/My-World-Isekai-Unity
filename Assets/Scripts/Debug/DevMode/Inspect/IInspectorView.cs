/// <summary>
/// A view that knows how to display one kind of <see cref="InteractableObject"/>.
/// Owned by <see cref="DevInspectModule"/> which discovers implementations via GetComponentsInChildren.
/// </summary>
public interface IInspectorView
{
    /// <summary>True if this view is capable of displaying the given target.</summary>
    bool CanInspect(InteractableObject target);

    /// <summary>Bind the view to a fresh target. Called when the selection changes and CanInspect returned true.</summary>
    void SetTarget(InteractableObject target);

    /// <summary>Release the current target and reset internal state. Called when selection is cleared.</summary>
    void Clear();
}
