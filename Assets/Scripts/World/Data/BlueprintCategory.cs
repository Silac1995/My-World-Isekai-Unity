namespace MWI.WorldSystem
{
    /// <summary>
    /// Top-level placement gate for <see cref="BuildingSO"/>:
    /// <list type="bullet">
    /// <item><c>Personal</c> — any character with the blueprint can place it via the normal
    /// <see cref="BuildingPlacementManager"/> ghost flow (e.g. a House on your own land).</item>
    /// <item><c>Civic</c> — only a community leader can place it via the admin console
    /// (Plan 5), and the community must have reached <see cref="BuildingSO.MinTier"/>
    /// (e.g. a Town Hall).</item>
    /// </list>
    /// Plan 2 only exposes the field. The authority gate ships with the admin console.
    /// </summary>
    public enum BlueprintCategory
    {
        Personal = 0,
        Civic = 1,
    }
}
