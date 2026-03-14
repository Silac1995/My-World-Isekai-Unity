using System.Collections.Generic;
using UnityEngine;

public class NeedToWearClothing : CharacterNeed
{
    public NeedToWearClothing(Character character) : base(character) { }

    public override bool IsActive()
    {
        bool needsClothing = _character.CharacterEquipment.IsChestExposed() || _character.CharacterEquipment.IsGroinExposed();
        if (!needsClothing) return false;
        if (_character.CharacterActions.CurrentAction is CharacterEquipAction) return false;
        return true;
    }

    public override float GetUrgency()
    {
        if (!IsActive()) return 0f;
        if (_character.CharacterEquipment.IsGroinExposed()) return 100f;
        return 60f;
    }

    public override GoapGoal GetGoapGoal()
    {
        return new GoapGoal("WearClothing", new Dictionary<string, bool> { { "isNaked", false } }, (int)GetUrgency());
    }

    public override List<GoapAction> GetGoapActions()
    {
        return new List<GoapAction>
        {
            new GoapAction_WearClothing()
        };
    }
}
