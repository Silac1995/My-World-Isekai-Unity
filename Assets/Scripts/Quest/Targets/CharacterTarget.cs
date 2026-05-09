using UnityEngine;

namespace MWI.Quests
{
    /// <summary>Quest target wrapping a Character (NPC to talk to / deliver to).</summary>
    public class CharacterTarget : IQuestTarget
    {
        private readonly Character _character;

        public CharacterTarget(Character character) { _character = character; }

        public Vector3 GetWorldPosition() =>
            _character != null ? _character.transform.position : Vector3.zero;
        public Vector3? GetMovementTarget() => null;  // diamond marker over head, not beacon
        public Bounds? GetZoneBounds() => null;
        public string GetDisplayName() =>
            _character != null ? _character.CharacterName : "<destroyed>";
        public bool IsVisibleToPlayer(Character viewer) => true;
    }
}
