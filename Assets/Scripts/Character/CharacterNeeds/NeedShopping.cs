using System.Collections.Generic;
using UnityEngine;

public class NeedShopping : CharacterNeed
{
    private const float BASE_URGENCY = 55f;
    private ItemSO _desiredItem;

    public NeedShopping(Character character, ItemSO itemToBuy) : base(character)
    {
        _desiredItem = itemToBuy;
    }

    public override bool IsActive()
    {
        if (_character.Controller is PlayerController) return false;
        return _desiredItem != null; 
    }

    public override float GetUrgency()
    {
        return BASE_URGENCY;
    }

    public override GoapGoal GetGoapGoal()
    {
        return new GoapGoal("GoShopping", new Dictionary<string, bool> { { "shoppingDone", true } }, (int)GetUrgency());
    }

    public override List<GoapAction> GetGoapActions()
    {
        return new List<GoapAction>
        {
            new GoapAction_GoShopping(_desiredItem)
        };
    }
}
