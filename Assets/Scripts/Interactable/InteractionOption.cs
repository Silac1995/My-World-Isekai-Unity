/// <summary>
/// Represents a single interaction option that can be presented to the player.
/// Standalone class replacing the nested struct in InteractableObject.
/// </summary>
[System.Serializable]
public class InteractionOption
{
    public string Name;
    public System.Action Action;

    /// <summary>
    /// When true, the button is shown but grayed out / not clickable.
    /// </summary>
    public bool IsDisabled;

    /// <summary>
    /// If set, clicking the button swaps its label between Name and ToggleName.
    /// </summary>
    public string ToggleName;

    public InteractionOption() { }

    public InteractionOption(string name, System.Action action)
    {
        Name = name;
        Action = action;
    }
}
