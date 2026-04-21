using UnityEngine;

namespace MWI.WorldSystem
{
    /// <summary>
    /// Shared contract for any spatial entity in the world hierarchy.
    /// Implemented by Region, MapController, WildernessZone, and (future) WeatherFront.
    /// Used for uniform spatial queries (containment, distance, separation checks).
    /// </summary>
    public interface IWorldZone
    {
        /// <summary>Stable string identifier. Survives save/load.</summary>
        string ZoneId { get; }

        /// <summary>World-space center of the zone. For zones with a BoxCollider, this is typically the collider bounds center.</summary>
        Vector3 Center { get; }

        /// <summary>Effective radius in Unity units. Used for separation checks and streaming.</summary>
        float Radius { get; }

        /// <summary>True if the given world position is inside the zone's bounds.</summary>
        bool Contains(Vector3 worldPos);

        /// <summary>Distance from the zone's surface to the given world position. Returns 0 if inside.</summary>
        float DistanceTo(Vector3 worldPos);
    }
}
