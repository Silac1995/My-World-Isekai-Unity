using UnityEngine;

public class CharacterLocations : MonoBehaviour
{
    [Header("Assigned Zones")]
    public Zone homeZone;
    public Zone workZone;

    [Header("Assigned Buildings")]
    public ResidentialBuilding homeBuilding;
    public CommercialBuilding workBuilding;
    public Job currentJob;

    /// <summary>
    /// Retrieves the specific Zone instance assigned to this character based on the requested ZoneType.
    /// </summary>
    /// <param name="type">The type of zone requested.</param>
    /// <returns>The Zone instance, or null if not assigned/handled.</returns>
    public Zone GetZoneByType(ZoneType type)
    {
        switch (type)
            {
            case ZoneType.Home:
                return homeZone;
            case ZoneType.Job:
                return workZone;
            default:
                Debug.LogWarning($"[CharacterLocations] ZoneType {type} is not yet handled or assigned for {gameObject.name}");
                return null;
        }
    }
}
