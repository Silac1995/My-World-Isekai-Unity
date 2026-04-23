using UnityEngine;

namespace MWI.Quests
{
    /// <summary>Quest target wrapping a Zone — region target with optional center waypoint.</summary>
    public class ZoneTarget : IQuestTarget
    {
        private readonly Zone _zone;
        private readonly string _displayName;

        public ZoneTarget(Zone zone, string displayName)
        {
            _zone = zone;
            _displayName = displayName;
        }

        public Vector3 GetWorldPosition() => _zone != null ? _zone.Bounds.center : Vector3.zero;
        public Vector3? GetMovementTarget() => null;     // zone-fill renders; no separate beacon
        public Bounds? GetZoneBounds() => _zone != null ? _zone.Bounds : (Bounds?)null;
        public string GetDisplayName() => _displayName ?? "<unnamed zone>";
        public bool IsVisibleToPlayer(Character viewer) => true;
    }
}
