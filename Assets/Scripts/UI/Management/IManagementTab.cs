namespace MWI.UI.Management
{
    /// <summary>
    /// Spec for one tab in the owner management panel. Plain C# class — no Unity lifecycle.
    /// Constructed once per panel-open by <see cref="CommercialBuilding.GetManagementTabs"/>;
    /// instantiates its view on demand via <see cref="CreateView"/>.
    ///
    /// Subtypes append tabs by overriding <c>GetManagementTabs()</c> on their building class —
    /// the panel never knows the concrete tab type.
    /// </summary>
    public interface IManagementTab
    {
        /// <summary>Header pill label, e.g. "Hiring".</summary>
        string Name { get; }

        /// <summary>
        /// Factory — instantiates and binds the view MonoBehaviour. Returns null on failure
        /// (e.g. missing Resources prefab); callers must null-check.
        /// </summary>
        IManagementTabView CreateView();
    }
}
