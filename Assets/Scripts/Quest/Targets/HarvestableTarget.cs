using UnityEngine;

namespace MWI.Quests
{
    /// <summary>Quest target wrapping a single Harvestable (tree, ore vein, berry bush).</summary>
    public class HarvestableTarget : IQuestTarget
    {
        private readonly Harvestable _harvestable;

        public HarvestableTarget(Harvestable harvestable) { _harvestable = harvestable; }

        public Vector3 GetWorldPosition() => _harvestable != null ? _harvestable.transform.position : Vector3.zero;
        public Vector3? GetMovementTarget() => null;  // object target, not movement
        public Bounds? GetZoneBounds() => null;
        public string GetDisplayName() => _harvestable != null ? _harvestable.name : "<destroyed>";
        public bool IsVisibleToPlayer(Character viewer) => true;  // v1
    }
}
