/// <summary>
/// Records a character's association with a specific world instance,
/// including last known position and session timestamp.
/// </summary>
[System.Serializable]
public class WorldAssociation
{
    public string worldGuid;
    public string worldName;
    public string lastMapId;
    public float positionX, positionY, positionZ;
    public string lastPlayed;
}
