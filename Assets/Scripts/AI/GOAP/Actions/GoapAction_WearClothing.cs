using System;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class GoapAction_WearClothing : GoapAction
{
    public override string ActionName => "WearClothing";

    public override Dictionary<string, bool> Preconditions => new Dictionary<string, bool>();

    public override Dictionary<string, bool> Effects => new Dictionary<string, bool>
    {
        { "isNaked", false }
    };

    public override float Cost => 1f;

    private bool _isComplete = false;
    private bool _hasStartedMoving = false;
    
    public override bool IsComplete => _isComplete;

    public override bool IsValid(Character worker)
    {
        List<WearableType> missingTypes = GetMissingTypes(worker);
        return FindClosestWearable(worker, missingTypes) != null;
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

        if (_hasStartedMoving)
        {
            // The action is complete when we stop moving
            if (!(npc.CurrentBehaviour is MoveToTargetBehaviour))
            {
                _isComplete = true;
            }
            return;
        }

        List<WearableType> missingTypes = GetMissingTypes(worker);
        ItemInteractable targetInteractable = FindClosestWearable(worker, missingTypes);

        if (targetInteractable != null && targetInteractable.ItemInstance is EquipmentInstance equip)
        {
            _hasStartedMoving = true;
            GameObject rootObject = targetInteractable.RootGameObject;

            Action atArrival = () => {
                if (targetInteractable == null) return;
                
                // If we successfully acquire it from the world
                if (targetInteractable.TryCollect())
                {
                    CharacterEquipAction equipAction = new CharacterEquipAction(worker, equip);
                    worker.CharacterActions.ExecuteAction(equipAction);
                    
                    if (rootObject != null) UnityEngine.Object.Destroy(rootObject);
                }
            };

            npc.PushBehaviour(new MoveToTargetBehaviour(npc, rootObject, 1.2f, atArrival));
        }
        else
        {
            _isComplete = true;
        }
    }

    private List<WearableType> GetMissingTypes(Character worker)
    {
        List<WearableType> missingTypes = new List<WearableType>();
        if (worker.CharacterEquipment.IsGroinExposed()) missingTypes.Add(WearableType.Pants);
        if (worker.CharacterEquipment.IsChestExposed()) missingTypes.Add(WearableType.Armor);
        return missingTypes;
    }

    private ItemInteractable FindClosestWearable(Character worker, List<WearableType> typesToFind)
    {
        var awareness = worker.CharacterAwareness;
        if (awareness == null) return null;

        return awareness.GetVisibleInteractables<ItemInteractable>()
            .Where(item => {
                if (item.ItemInstance is WearableInstance w)
                    return typesToFind.Contains(((WearableSO)w.ItemSO).WearableType);
                return false;
            })
            .OrderBy(item => Vector3.Distance(worker.transform.position, item.transform.position))
            .FirstOrDefault();
    }
}
