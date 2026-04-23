using UnityEngine;

namespace MWI.Quests
{
    /// <summary>Quest target wrapping a single WorldItem (loose item to pick up).</summary>
    public class WorldItemTarget : IQuestTarget
    {
        private readonly WorldItem _worldItem;

        public WorldItemTarget(WorldItem worldItem) { _worldItem = worldItem; }

        public Vector3 GetWorldPosition() => _worldItem != null ? _worldItem.transform.position : Vector3.zero;
        public Vector3? GetMovementTarget() => null;
        public Bounds? GetZoneBounds() => null;
        public string GetDisplayName() => _worldItem != null && _worldItem.ItemInstance != null && _worldItem.ItemInstance.ItemSO != null
            ? _worldItem.ItemInstance.ItemSO.ItemName
            : "<destroyed>";
        public bool IsVisibleToPlayer(Character viewer) => true;
    }
}
