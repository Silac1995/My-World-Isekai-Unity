using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class GoapAction_GoShopping : GoapAction
{
    public override string ActionName => "GoShopping";

    public override Dictionary<string, bool> Preconditions => new Dictionary<string, bool>();

    public override Dictionary<string, bool> Effects => new Dictionary<string, bool>
    {
        { "shoppingDone", true }
    };

    public override float Cost => 2f;

    private ItemSO _desiredItem;
    private bool _isComplete = false;
    private bool _hasStartedShopping = false;
    
    public override bool IsComplete => _isComplete;

    public GoapAction_GoShopping(ItemSO desiredItem)
    {
        _desiredItem = desiredItem;
    }

    public override bool IsValid(Character worker)
    {
        return FindShop() != null;
    }

    public override void Execute(Character worker)
    {
        if (_isComplete) return;

        NPCController npc = worker.Controller as NPCController;
        if (npc == null)
        {
            _isComplete = true;
            return;
        }

        if (_hasStartedShopping)
        {
            // We finished if we are no longer moving, AND we are no longer waiting in the queue
            if (!(npc.CurrentBehaviour is MoveToTargetBehaviour) && !(npc.CurrentBehaviour is WaitInQueueBehaviour))
            {
                _isComplete = true;
            }
            return;
        }

        ShopBuilding shop = FindShop();
        if (shop != null)
        {
            _hasStartedShopping = true;
            npc.PushBehaviour(new MoveToTargetBehaviour(npc, shop.gameObject, 3f, () =>
            {
                npc.PushBehaviour(new WaitInQueueBehaviour(npc, shop, _desiredItem));
            }));
        }
        else
        {
            _isComplete = true;
        }
    }

    private ShopBuilding FindShop()
    {
        if (BuildingManager.Instance == null) return null;
        return BuildingManager.Instance.allBuildings
            .OfType<ShopBuilding>()
            .FirstOrDefault(s => s.ItemsToSell.Contains(_desiredItem) && s.HasItemInStock(_desiredItem));
    }
}
