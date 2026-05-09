using UnityEngine;

namespace MWI.Quests
{
    /// <summary>
    /// What a quest points at in the world. Pluggable so a single renderer
    /// handles Harvestables, WorldItems, Zones, Buildings, Characters via one path.
    /// </summary>
    public interface IQuestTarget
    {
        /// <summary>World position used by the floating-marker renderer (style A).</summary>
        Vector3 GetWorldPosition();

        /// <summary>Non-null = "go here" target (style B beacon). Null = "interact with this object" target (style A diamond).</summary>
        Vector3? GetMovementTarget();

        /// <summary>Non-null = region target (zone fill renders). Null = point target.</summary>
        Bounds? GetZoneBounds();

        /// <summary>Display name for HUD text ("the East Woods", "Bob's Smithy").</summary>
        string GetDisplayName();

        /// <summary>v1: always returns true. Stub for future fog-of-war / hidden quests.</summary>
        bool IsVisibleToPlayer(Character viewer);
    }
}
