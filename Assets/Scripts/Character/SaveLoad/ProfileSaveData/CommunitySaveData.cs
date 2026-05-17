[System.Serializable]
public class CommunitySaveData
{
    /// <summary>
    /// MapId of the community this character is currently a *member* of.
    /// Resolved lazily at runtime by <c>CharacterCommunity.Deserialize</c> via
    /// <c>MapRegistry.Instance.GetAllCommunities()</c>.
    /// </summary>
    public string communityMapId;

    /// <summary>
    /// MapId of the community this character is a *citizen* of (sticky — granted
    /// by completing an <c>AdministrativeBuilding</c>, see Plan 4).
    /// Resolved lazily by <c>CharacterCommunity.Deserialize</c>. Defaults to empty
    /// for legacy saves authored before 2026-05-17 (additive field).
    /// </summary>
    public string citizenshipMapId;
}
