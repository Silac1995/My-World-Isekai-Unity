/// <summary>
/// Dev-mode action contract. Implementers live as MonoBehaviours under the Select tab's
/// ActionsContainer. Each action owns its own button; it subscribes to
/// DevSelectionModule.OnSelectionChanged to refresh IsAvailable, and runs Execute when the
/// button is clicked.
///
/// Execute can run synchronously, open a prompt, enter a click-armed state, etc. — the
/// interface leaves that open. Actions that consume clicks must coordinate via
/// DevModeManager.SetClickConsumer.
/// </summary>
public interface IDevAction
{
    /// <summary>Display label for the action button.</summary>
    string Label { get; }

    /// <summary>True when this action can be invoked given the current selection state.</summary>
    bool IsAvailable(DevSelectionModule sel);

    /// <summary>Run the action. Caller has already confirmed IsAvailable.</summary>
    void Execute(DevSelectionModule sel);
}
