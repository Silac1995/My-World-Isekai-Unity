using System;
using UnityEngine;
using System.Linq;
using System.Collections.Generic;

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

    public override bool Resolve(NPCController npc)
    {
        if (npc.HasBehaviour<MoveToTargetBehaviour>()) return false;

        List<WearableType> missingTypes = new List<WearableType>();
        if (_character.CharacterEquipment.IsGroinExposed()) missingTypes.Add(WearableType.Pants);
        if (_character.CharacterEquipment.IsChestExposed()) missingTypes.Add(WearableType.Armor);

        ItemInteractable targetInteractable = FindClosestWearable(npc.transform.position, missingTypes);

        if (targetInteractable != null && targetInteractable.ItemInstance is EquipmentInstance equip)
        {
            GameObject rootObject = targetInteractable.RootGameObject;

            Action atArrival = () => {
                if (targetInteractable == null) return;
                if (targetInteractable.TryCollect())
                {
                    CharacterEquipAction equipAction = new CharacterEquipAction(npc.Character, equip);
                    npc.Character.CharacterActions.ExecuteAction(equipAction);
                    if (rootObject != null) UnityEngine.Object.Destroy(rootObject);
                }
            };

            npc.PushBehaviour(new MoveToTargetBehaviour(npc, rootObject, 1.2f, atArrival));
            return true;
        }
        return false;
    }

    private ItemInteractable FindClosestWearable(Vector3 currentPosition, List<WearableType> typesToFind)
    {
        var awareness = _character.CharacterAwareness;
        if (awareness == null) return null;

        return awareness.GetVisibleInteractables<ItemInteractable>()
            .Where(item => {
                if (item.ItemInstance is WearableInstance w)
                    return typesToFind.Contains(((WearableSO)w.ItemSO).WearableType);
                return false;
            })
            .OrderBy(item => Vector3.Distance(currentPosition, item.transform.position))
            .FirstOrDefault();
    }
}
