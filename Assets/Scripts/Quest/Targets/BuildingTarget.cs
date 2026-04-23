using UnityEngine;

namespace MWI.Quests
{
    /// <summary>
    /// Quest target wrapping a Building. Movement target — uses the building's
    /// DeliveryZone center for "go here" beacon rendering.
    /// </summary>
    public class BuildingTarget : IQuestTarget
    {
        private readonly Building _building;

        public BuildingTarget(Building building) { _building = building; }

        public Vector3 GetWorldPosition() =>
            _building != null ? _building.transform.position : Vector3.zero;

        public Vector3? GetMovementTarget()
        {
            if (_building == null) return null;
            // Prefer DeliveryZone (authored on the Building base) for the "go here" beacon;
            // fall back to the building's transform when no delivery zone is wired up.
            if (_building.DeliveryZone != null)
                return _building.DeliveryZone.Bounds.center;
            return _building.transform.position;
        }

        public Bounds? GetZoneBounds() => null;  // no zone fill for building targets
        public string GetDisplayName() => _building != null ? _building.BuildingDisplayName : "<destroyed>";
        public bool IsVisibleToPlayer(Character viewer) => true;
    }
}
