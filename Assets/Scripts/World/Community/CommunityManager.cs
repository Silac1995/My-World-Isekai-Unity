using System.Collections.Generic;
using UnityEngine;

public class CommunityManager : MonoBehaviour
{
    public static CommunityManager Instance { get; private set; }

    [Header("Active Communities")]
    public List<Community> activeCommunities = new List<Community>();

    [Header("Zone Generation Settings")]
    [SerializeField] private Vector3 _defaultCampSize = new Vector3(15f, 10f, 15f);

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Creates a new root-level community and registers it.
    /// </summary>
    public Community CreateNewCommunity(Character founder, string communityName)
    {
        if (founder == null) return null;

        Community newComm = new Community(communityName, founder);
        activeCommunities.Add(newComm);
        
        Debug.Log($"<color=cyan>[Community Manager]</color> New community founded: {communityName} by {founder.name}");
        return newComm;
    }

    /// <summary>
    /// Instantiates a new Zone GameObject dynamically for a specific community in the world.
    /// </summary>
    public Zone EstablishCommunityZone(Community community, Vector3 position, ZoneType type, string zoneName)
    {
        if (community == null) return null;

        // 1. Create the GameObject
        GameObject zoneObj = new GameObject($"Zone_{zoneName}");
        zoneObj.transform.position = position;

        // 2. Add dependencies
        BoxCollider box = zoneObj.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = _defaultCampSize; // Can be scaled based on community level later

        Zone newZone = zoneObj.AddComponent<Zone>();
        newZone.zoneType = type;
        newZone.zoneName = zoneName;

        // 3. Register it
        community.communityZones.Add(newZone);

        Debug.Log($"<color=cyan>[Community Manager]</color> Established {type} '{zoneName}' for {community.communityName} at {position}.");
        return newZone;
    }
}
