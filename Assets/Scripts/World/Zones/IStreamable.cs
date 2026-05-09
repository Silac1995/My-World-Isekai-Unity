using UnityEngine;

namespace MWI.WorldSystem
{
    /// <summary>
    /// Contract for content that materializes in and out of a WildernessZone based on player proximity.
    /// Phase 1 is data-only — live prefab instantiation implementations land in Phase 2 for harvestables and Phase 3 for wildlife.
    /// </summary>
    public interface IStreamable
    {
        string Id { get; }
        Vector3 WorldPosition { get; }

        /// <summary>Server-only. Instantiate the live representation at the given position.</summary>
        GameObject MaterializeAt(Vector3 pos);

        /// <summary>Server-only. Capture live state back into the record and destroy the GameObject.</summary>
        void SnapshotAndRelease(GameObject live);
    }
}
